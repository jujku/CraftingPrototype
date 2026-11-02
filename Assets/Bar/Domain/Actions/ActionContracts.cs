using System;
using System.Collections.Generic;

namespace Bar.Domain
{
    /// <summary>视图应采集的动作输入方式。</summary>
    public enum InputStyle { Click, HoldTilt, HoldOscillate, HoldCircle, HoldPress, Menu }
    /// <summary>传递与 Unity 无关的一次倾斜角输入。</summary>
    public sealed class ActionInput
    {
        /// <summary>相对直立方向的倾斜角，单位度。</summary>
        public double TiltDegrees { get; }
        /// <summary>创建ActionInput。</summary>
        /// <precondition>倾斜角有限且非负。</precondition>
        /// <effects>保存一次只读输入；最大角由具体流量规则检查。</effects>
        public ActionInput(double tiltDegrees = 0,double gestureX=0,double gestureY=0,double pressFraction=0)
        { Guard.Nonnegative(tiltDegrees);Guard.Finite(gestureX);Guard.Finite(gestureY);Guard.Range(pressFraction,1);TiltDegrees=tiltDegrees;GestureX=gestureX;GestureY=gestureY;PressFraction=pressFraction; }
        /// <summary>按事件累计的鼠标横向位置，用于圆周手势。</summary>
        public double GestureX { get; }
        /// <summary>按事件累计的鼠标纵向位置，用于往复和圆周手势。</summary>
        public double GestureY { get; }
        /// <summary>归一化下压程度，范围零到一。</summary>
        public double PressFraction { get; }
    }
    /// <summary>携带动作类别、施动者、目标以及关联请求，供视图和日志订阅。</summary>
    public sealed class DomainEvent
    {
        /// <summary>当前定义、事件或逻辑位置的类别。</summary>
        public string Kind { get; }
        /// <summary>施动者实例标识，空手动作时可为空。</summary>
        public string ActorId { get; }
        /// <summary>动作目标实例标识。</summary>
        public string TargetId { get; }
        /// <summary>动作标识或其他简短分类信息。</summary>
        public string Detail { get; }
        /// <summary>与本事件关联的操作请求，可为空。</summary>
        public OperationRequest Request { get; }
        /// <summary>可选的只读宿主观察包；领域不解释其内容。</summary>
        public object Observation { get; }
        /// <summary>创建DomainEvent。</summary>
        /// <precondition>事件种类非空；施动者、目标和关联请求可为空。</precondition>
        /// <effects>创建只读事件数据，不自行调用订阅者。</effects>
        public DomainEvent(string kind, string actorId, string targetId, string detail, OperationRequest request = null, object observation = null)
        { Guard.Text(kind); Kind = kind; ActorId = actorId; TargetId = targetId; Detail = detail; Request = request; Observation = observation; }
    }
    /// <summary>约定动作标识、前置条件与每次激活的生命周期入口。</summary>
    public interface IBarAction
    {
        /// <summary>稳定标识，用于定义或动作查询。</summary>
        string Id { get; }
        /// <summary>交互菜单中的中文动作名称。</summary>
        string Label { get; }
        /// <summary>动作需要的鼠标或菜单输入方式。</summary>
        InputStyle InputStyle { get; }
        /// <summary>判断当前施动者和目标是否满足该动作规则。</summary>
        /// <precondition>调用方提供当前 Hands；失效目标或不满足规则时返回 false。</precondition>
        /// <effects>仅查询物品与手状态，不生成请求或事件。</effects>
        bool CanExecute(Item actor, Item target, Hands hands);
        /// <summary>重新验证候选并开始该动作。</summary>
        /// <precondition>当前 CanExecute 为 true，候选不能依赖已经失效的菜单状态。</precondition>
        /// <effects>创建独立 ActiveAction，记录 action_began；具体规则决定持物变更或请求。</effects>
        ActiveAction Begin(Item actor, Item target, Hands hands);
    }
    /// <summary>管理单次动作的输入、请求、事件、取消以及双手锁。</summary>
    public abstract class ActiveAction
    {
        private readonly List<OperationRequest> requests = new List<OperationRequest>();
        private readonly List<DomainEvent> events = new List<DomainEvent>();
        /// <summary>创建本次激活或菜单项的动作规则。</summary>
        public IBarAction Action { get; }
        /// <summary>手持施动者；空手拿取或表面拆装时可为空。</summary>
        public Item Actor { get; }
        /// <summary>逻辑目标，可以是另一只手里的物品。</summary>
        public Item Target { get; }
        /// <summary>是否已结束、取消或完成即时动作。</summary>
        public bool IsEnded { get; private set; }
        /// <summary>本次激活已经提出的只读操作请求列表。</summary>
        public IReadOnlyList<OperationRequest> Requests => requests.AsReadOnly();
        /// <summary>本次激活已经记录的只读领域事件列表。</summary>
        public IReadOnlyList<DomainEvent> Events => events.AsReadOnly();
        /// <summary>在记录新领域事件后通知订阅者；Begin 阶段事件需先回放 Events。</summary>
        public event System.Action<DomainEvent> Emitted;
        /// <summary>本次连续动作锁定的双手状态。</summary>
        protected Hands Hands { get; }
        /// <summary>创建ActiveAction。</summary>
        /// <precondition>动作规则及上下文已通过 Begin 校验；需要锁手时 Hands 必须空闲。</precondition>
        /// <effects>保存上下文；连续动作占用双手锁；记录 action_began。</effects>
        protected ActiveAction(IBarAction action, Item actor, Item target, Hands hands, bool reserveHands)
        {
            Action = action; Actor = actor; Target = target; Hands = hands;
            if (reserveHands && !hands.Acquire(this)) throw new InvalidOperationException("Hands already in use.");
            Emit(new DomainEvent("action_began", actor?.InstanceId, target?.InstanceId, action.Id));
        }
        /// <summary>处理本次激活的一段输入。</summary>
        /// <precondition>动作未结束，输入非空，dt 为有限非负秒数。</precondition>
        /// <effects>由具体实现生成请求与事件；抽象契约本身不推进 World。</effects>
        public abstract void Update(ActionInput input, double dt);
        /// <summary>结束或取消本次激活。</summary>
        /// <precondition>允许重复调用；只有第一次处理尚未结束的激活。</precondition>
        /// <effects>拒绝全部未确认请求并释放库存/容量，释放双手锁，记录 action_ended 或 action_cancelled。</effects>
        public virtual void End(bool cancelled = false)
        {
            if (IsEnded) return;
            foreach (var request in requests) if (request.IsPending) request.Reject();
            IsEnded = true; Hands.Release(this); Emit(new DomainEvent(cancelled ? "action_cancelled" : "action_ended", Actor?.InstanceId, Target?.InstanceId, Action.Id));
        }
        /// <summary>把操作提案加入本次激活。</summary>
        /// <precondition>传入已完成来源和容量预留的 OperationRequest。</precondition>
        /// <effects>加入 Requests，订阅其 Resolved，记录并发布 operation_requested。</effects>
        protected void Add(OperationRequest request) { requests.Add(request); request.Resolved += Emit; Emit(new DomainEvent("operation_requested", Actor?.InstanceId, Target?.InstanceId, Action.Id, request)); }
        /// <summary>记录并转发领域事件。</summary>
        /// <precondition>传入需要发布的事件对象。</precondition>
        /// <effects>追加 Events，并同步调用 Emitted 订阅者。</effects>
        protected void Emit(DomainEvent value) { events.Add(value); Emitted?.Invoke(value); }
        /// <summary>检查输入和生命周期。</summary>
        /// <precondition>动作未结束；input 非空；dt 有限且非负。</precondition>
        /// <effects>不修改状态；不满足条件时抛异常。</effects>
        protected void ValidateUpdate(ActionInput input, double dt)
        { if (IsEnded) throw new InvalidOperationException("Action ended."); if (input == null) throw new ArgumentNullException(nameof(input)); Guard.Nonnegative(dt); }
    }
    internal sealed class ImmediateAction : ActiveAction
    {
        internal ImmediateAction(IBarAction action, Item actor, Item target, Hands hands, System.Action execute) : base(action, actor, target, hands, false)
        { execute(); Emit(new DomainEvent("action_performed", actor?.InstanceId, target.InstanceId, action.Id)); End(); }
        public override void Update(ActionInput input, double dt) { ValidateUpdate(input, dt); }
    }
}
