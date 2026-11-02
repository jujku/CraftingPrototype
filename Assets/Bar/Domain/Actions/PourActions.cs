using System;
using Bar.Data;

namespace Bar.Domain
{
    /// <summary>根据酒瓶倾角向可接近的敞口容器提出连续倒料请求。</summary>
    public sealed class BottlePourAction : IBarAction
    {
        private readonly TiltFlowModel flow;
        private readonly Controls controls;
        /// <summary>稳定标识，用于定义或动作查询。</summary>
        public string Id => "pour.bottle_to_container";
        /// <summary>交互菜单中的中文动作名称。</summary>
        public string Label => "倒入";
        /// <summary>动作需要的鼠标或菜单输入方式。</summary>
        public InputStyle InputStyle => InputStyle.HoldTilt;
        /// <summary>创建BottlePourAction。</summary>
        /// <precondition>Controls 非空且所有流量、角度、时长与手势参数通过校验。</precondition>
        /// <effects>保存只读控制依赖；创建流量模型，不改变任何库存。</effects>
        public BottlePourAction(Controls controls) {
            this.controls = controls ?? throw new ArgumentNullException(nameof(controls));
            flow = new TiltFlowModel(controls);
        }
        /// <summary>判断当前施动者和目标是否满足该动作规则。</summary>
        /// <precondition>持有开启且尚有库存的酒瓶；目标可接近、敞口、有容量且无挂起请求。</precondition>
        /// <effects>仅返回是否可执行，不改变手或物品。</effects>
        public bool CanExecute(Item actor, Item target, Hands hands)
        {
            var source = actor as ILiquidSource;
            return hands != null && actor != null && hands.CanUse(actor, Id) &&
                source?.LiquidControl == LiquidControl.BottleTilt && LiquidTransfer.CanTransfer(source, target, hands);
        }
        internal static bool Reachable(Item target, Hands hands) => InteractionAccess.Reachable(target, hands);
        /// <summary>重新验证候选并开始该动作。</summary>
        /// <precondition>持有开启且尚有库存的酒瓶；目标可接近、敞口、有容量且无挂起请求。</precondition>
        /// <effects>创建独立倒料激活并锁双手；Begin 不扣库存，Update 才提出容量限幅请求。</effects>
        public ActiveAction Begin(Item actor, Item target, Hands hands)
        {
            if (!CanExecute(actor, target, hands)) throw new InvalidOperationException("Cannot pour bottle.");
            return new BottlePourSession(this, actor, (ILiquidReceiver)target, hands, flow, controls);
        }
    }
    internal sealed class BottlePourSession : ActiveAction
    {
        private readonly ILiquidSource source;
        private readonly ILiquidReceiver target;
        private readonly TiltFlowModel flow;
        private readonly Controls controls;
        private readonly bool guided;
        private double elapsed;
        internal BottlePourSession(IBarAction action, Item actor, ILiquidReceiver target, Hands hands,
            TiltFlowModel flow, Controls controls,bool guided=false) : base(action, actor, (Item)target, hands, true)
        {
            source = (ILiquidSource)actor;
            this.target = target;
            this.flow = flow;
            this.controls = controls;this.guided=guided;
        }
        public override void Update(ActionInput input, double dt)
        {
            ValidateUpdate(input, dt);
            Guard.Range(input.TiltDegrees, controls.MaximumTiltDegrees);
            elapsed += dt;
            if (dt == 0) return;
            double intended = flow.Flow(input.TiltDegrees, source.LiquidAvailableMl,
                source.LiquidCapacityMl, source.FixedFlowMlPerSecond,Actor.Definition.Id) * dt * (guided?controls.GuidedFlowScale:1);
            var request = LiquidTransfer.Request(source, target, intended, input.TiltDegrees, elapsed, dt);
            if (request != null){if(guided)request.GuideCarbonated();Add(request);}
        }
    }
    /// <summary>单击时提出朝上量酒杯的整杯液体转移请求。</summary>
    public sealed class JiggerPourAction : IBarAction
    {
        private readonly Controls controls;
        /// <summary>稳定标识，用于定义或动作查询。</summary>
        public string Id => "pour.jigger_to_container";
        /// <summary>交互菜单中的中文动作名称。</summary>
        public string Label => "量酒器倒出";
        /// <summary>动作需要的鼠标或菜单输入方式。</summary>
        public InputStyle InputStyle => InputStyle.Click;
        /// <summary>创建JiggerPourAction。</summary>
        /// <precondition>Controls 非空且所有流量、角度、时长与手势参数通过校验。</precondition>
        /// <effects>保存只读控制依赖；创建流量模型，不改变任何库存。</effects>
        public JiggerPourAction(Controls controls) {
            this.controls = controls ?? throw new ArgumentNullException(nameof(controls));
            controls.Validate();
        }
        /// <summary>判断当前施动者和目标是否满足该动作规则。</summary>
        /// <precondition>持有量酒器或其朝上杯；来源有液体；目标不同且敞口，能容下整杯，无挂起请求。</precondition>
        /// <effects>仅返回是否可执行，不改变手或物品。</effects>
        public bool CanExecute(Item actor, Item target, Hands hands)
        {
            var source = actor as ILiquidSource;
            return hands != null && actor != null && hands.CanUse(actor, Id) &&
                source?.LiquidControl == LiquidControl.Measured &&
                LiquidTransfer.CanTransfer(source, target, hands, source.LiquidAvailableMl);
        }
        /// <summary>重新验证候选并开始该动作。</summary>
        /// <precondition>持有量酒器或其朝上杯；来源有液体；目标不同且敞口，能容下整杯，无挂起请求。</precondition>
        /// <effects>锁双手并立即提出一次整杯转移请求；确认前不改变快照；视图动画不再次产生请求。</effects>
        public ActiveAction Begin(Item actor, Item target, Hands hands)
        {
            if (!CanExecute(actor, target, hands)) throw new InvalidOperationException("Cannot empty jigger.");
            return new JiggerPourSession(this, actor, (ILiquidSource)actor, (ILiquidReceiver)target, hands, controls.JiggerPourSeconds);
        }
    }
    internal sealed class JiggerPourSession : ActiveAction
    {
        internal JiggerPourSession(IBarAction action, Item actor, ILiquidSource source, ILiquidReceiver target, Hands hands, double seconds) : base(action, actor, (Item)target, hands, true)
        { Add(LiquidTransfer.Request(source, target, source.LiquidAvailableMl, 0, 0, seconds)); }
        public override void Update(ActionInput input, double dt) { ValidateUpdate(input, dt); }
    }
}
