using System;
using System.Collections.Generic;
using Bar.Domain;
using Bar.Logging;

namespace Bar.Unity
{
    /// <summary>累计带事件时间的输入，在固定 0.1 秒外部节拍内按事件边界分段消费。</summary>
    public sealed class SimulationClock
    {
        private sealed class Command { internal double Time; internal long Order; internal Action<SimulationClock> Run; }
        private readonly List<Command> queue = new List<Command>();
        private readonly WorldAdapter adapter;
        private long order;
        private double processed, nextTick = .1, tilt, clickRemaining,gestureX,gestureY,press;
        /// <summary>本时钟使用的双手领域状态。</summary>
        public Hands Hands { get; }
        /// <summary>当前激活，结束后可保留以供视图检查其事件。</summary>
        public ActiveAction Active { get; private set; }
        /// <summary>已经处理的外部节拍数。</summary>
        public long Ticks { get; private set; }
        /// <summary>最后一次事件被规则拒绝时的提示；成功开始操作后清空。</summary>
        public string Error { get; private set; }
        private VesselStateHolder activation;
        private sealed class VesselStateHolder
        {internal CraftingResearch.CoreV2.VesselState[] Before;internal string Id,Hand;internal double Held;}
        private void Forward(DomainEvent e)
        {
            var r=e.Request;var row=new LogRecord{actionId=Active.Action.Id,activationId=activation.Id,actorId=Active.Actor?.InstanceId,targetId=Active.Target?.InstanceId,hand=activation.Hand,
                requestId=r?.RequestId,before=activation.Before,after=adapter.States(Active.Actor,Active.Target),amountUnit=r?.AmountUnit,
                tiltDegrees=r?.IntentTiltDegrees??tilt,heldSeconds=r?.HeldSeconds??activation.Held,pressFraction=r?.IntentPressFraction??press,gestureStrength=r?.Intensity,
                intendedAmount=r?.IntendedMl,requestedAmount=r?.RequestedMl,actualAmount=r?.ActualAmount,rejected=e.Kind=="operation_rejected",
                reason=e.Kind=="operation_rejected"?adapter.LastError??"cancelled":null};
            if(e.Observation is ToolIceDiscardReceipt discarded)
            {row.toolIceCubes=discarded.Cubes;row.amountUnit="g";row.intendedAmount=row.requestedAmount=row.actualAmount=discarded.Grams;}
            adapter.Publish(e.Kind,row);
            if(e.Kind=="action_ended"||e.Kind=="action_cancelled")
            {adapter.CurrentActionId=adapter.CurrentActivationId=adapter.CurrentHand=adapter.CurrentActor=adapter.CurrentTarget=null;}
        }
        /// <summary>创建以模型 step 为 0.1 秒的调度器。</summary>
        /// <precondition>adapter 与 hands 非空，注册容器必须在第一次 AdvanceFrame 之前完成。</precondition>
        /// <effects>清空事件队列，模型时间初始为零，不创建 Unity Update 或 FixedUpdate。</effects>
        public SimulationClock(WorldAdapter adapter, Hands hands)
        {
            this.adapter=adapter??throw new ArgumentNullException(nameof(adapter));Hands=hands??throw new ArgumentNullException(nameof(hands));
            Hands.Changed+=e=>
            {
                bool belongs=activation!=null&&adapter.CurrentActivationId==activation.Id;
                if(belongs){activation.Hand=e.Detail;adapter.CurrentHand=e.Detail;}
                adapter.Publish(e.Kind,new LogRecord{actionId=adapter.CurrentActionId,activationId=adapter.CurrentActivationId,hand=e.Detail,actorId=e.ActorId,
                    before=belongs?activation.Before:null,after=adapter.States(Hands.Left,Hands.Right)});
            };
        }
        /// <summary>加入发生在指定输入时刻的领域命令。</summary>
        /// <precondition>时间为有限非负秒数且回调非空；晚到事件被限到已经处理的边界。</precondition>
        /// <effects>按时间及原始入队顺序稳定排序，未到节拍时不执行回调。</effects>
        public void Enqueue(double time, Action<SimulationClock> action)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0 || action == null) throw new ArgumentException("事件时间无效");
            queue.Add(new Command { Time = Math.Max(processed, time), Order = order++, Run = action }); queue.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Order.CompareTo(b.Order));
        }
        /// <summary>在节拍内部开始一个重新校验过的动作候选。</summary>
        /// <precondition>option 非空且规则仍满足当前持物状态；该调用应来自 Enqueue 回调。</precondition>
        /// <effects>结束旧激活，倾斜归零，创建新激活并提交 Begin 产生的整杯请求；单击倒量酒器保留动画时长的双手锁。</effects>
        public void Begin(ActionOption option)
        {
            if (option == null) return;
            Active?.End(); tilt = gestureX = gestureY = press = 0; Error = null;
            activation=new VesselStateHolder{Before=adapter.States(option.Actor,option.Target),Id=Guid.NewGuid().ToString("N"),Hand=option.Hand?.ToString()??"none"};
            adapter.CurrentActionId=option.Action.Id;adapter.CurrentActivationId=activation.Id;adapter.CurrentHand=activation.Hand;adapter.CurrentActor=option.Actor?.InstanceId;adapter.CurrentTarget=option.Target?.InstanceId;
            try { Active=option.Begin(Hands); }
            catch(InvalidOperationException e){adapter.Publish("action_rejected",new LogRecord{actionId=option.Action.Id,activationId=activation.Id,hand=activation.Hand,actorId=option.Actor?.InstanceId,targetId=option.Target?.InstanceId,before=activation.Before,after=adapter.States(option.Actor,option.Target),rejected=true,reason=e.Message});throw;}
            foreach(var e in Active.Events)Forward(e);Active.Emitted+=Forward;Drain();
            clickRemaining = Active.Action.InputStyle == InputStyle.Click && !Active.IsEnded && Active.Requests.Count > 0 ? Active.Requests[0].DurationSeconds : 0;
        }
        /// <summary>改变当前动作的绝对倾角。</summary>
        /// <precondition>angle 有限非负，具体最大角由流量规则验证；从事件回调调用。</precondition>
        /// <effects>保存下一段时间使用的倾角，不直接生成请求。</effects>
        public void SetTilt(double angle) { if (double.IsNaN(angle) || double.IsInfinity(angle) || angle < 0) throw new ArgumentException("倾角无效"); tilt = angle; }
        /// <summary>记录带时间事件的手势位置和下压程度。</summary>
        /// <precondition>x、y 有限，pressFraction 在零到一之间；从事件回调调用。</precondition>
        /// <effects>仅改变下一输入片段；不立即推进核心。</effects>
        public void SetGesture(double x,double y,double pressFraction)
        {if(double.IsNaN(x)||double.IsNaN(y)||double.IsInfinity(x)||double.IsInfinity(y)||double.IsNaN(pressFraction)||double.IsInfinity(pressFraction)||pressFraction<0||pressFraction>1)throw new ArgumentException("手势无效");gestureX=x;gestureY=y;press=pressFraction;}
        /// <summary>松开连续倒料或取消当前动作。</summary>
        /// <precondition>可在没有当前激活时调用；cancel=true 用于 Esc 或失焦。</precondition>
        /// <effects>结束 HoldTilt；取消时也结束单击动画，释放尚未确认的预留和双手锁。</effects>
        public void Stop(bool cancel = false)
        { if (Active != null && !Active.IsEnded && (cancel || Active.Action.InputStyle == InputStyle.HoldTilt || Active.Action.InputStyle == InputStyle.HoldPress || Active.Action.InputStyle == InputStyle.HoldOscillate || Active.Action.InputStyle == InputStyle.HoldCircle)) Active.End(cancel); }
        /// <summary>消费截至当前帧时间的完整固定节拍。</summary>
        /// <precondition>输入时间有限、非负且不倒退；事件应在处理该帧前全部入队。</precondition>
        /// <effects>每个完整 0.1 秒节拍只处理一次；内部按事件时间分段，保留不足节拍的输入，不因帧率丢弃时间。</effects>
        public void AdvanceFrame(double inputTime)
        {
            if (double.IsNaN(inputTime) || double.IsInfinity(inputTime) || inputTime < processed - 1e-9) throw new ArgumentException("输入时间倒退");
            while (nextTick <= inputTime + 1e-9)
            {
                while (queue.Count > 0 && queue[0].Time <= nextTick + 1e-10)
                {
                    var command = queue[0]; queue.RemoveAt(0); Integrate(Math.Max(0, command.Time - processed)); processed = command.Time;
                    try { command.Run(this); } catch (InvalidOperationException ex) { Error = ex.Message; Stop(true); }
                }
                Integrate(Math.Max(0, nextTick - processed)); processed = nextTick; nextTick = (++Ticks + 1) * .1;
            }
        }
        private void Integrate(double dt)
        {
            if (dt <= 1e-12) return;
            double timeBefore=adapter.World.Time;
            if (Active != null && !Active.IsEnded)
            {
                activation.Held+=dt;
                Active.Update(new ActionInput(tilt,gestureX,gestureY,press), dt); Drain();
                if (clickRemaining > 0) { clickRemaining -= dt; if (clickRemaining <= 1e-9) Active.End(); }
            }
            double remaining=dt-(adapter.World.Time-timeBefore);
            if(remaining < -1e-8)throw new InvalidOperationException("动作重复消费了模型时间");
            adapter.Advance(Math.Max(0,remaining));
        }
        private void Drain() { if (Active == null) return; foreach (var request in Active.Requests) if (request.IsPending) adapter.Submit(request); }
    }
}
