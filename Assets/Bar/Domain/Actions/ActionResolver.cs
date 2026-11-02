using System;
using System.Collections.Generic;
using System.Linq;
using Bar.Data;

namespace Bar.Domain
{
    /// <summary>保存一条当前可执行的施动者、目标和动作组合。</summary>
    public sealed class ActionOption
    {
        /// <summary>创建本次激活或菜单项的动作规则。</summary>
        public IBarAction Action { get; }
        /// <summary>手持施动者；空手拿取或表面拆装时可为空。</summary>
        public Item Actor { get; }
        /// <summary>逻辑目标，可以是另一只手里的物品。</summary>
        public Item Target { get; }
        /// <summary>当前候选施动者所在的手，空手候选时为空。</summary>
        public HandSide? Hand { get; }
        internal ActionOption(IBarAction action, Item actor, Item target, HandSide? hand) { Action = action; Actor = actor; Target = target; Hand = hand; }
        /// <summary>重新验证候选并开始该动作。</summary>
        /// <precondition>对应规则的 CanExecute 在当前 Hands 上仍为 true。</precondition>
        /// <effects>委托保存的动作规则重新 Begin；失效候选抛异常，不执行旧菜单状态。</effects>
        public ActiveAction Begin(Hands hands) => Action.Begin(Actor, Target, hands);
    }
    /// <summary>枚举所有可用动作，并按主手、另一手、空手确定顺序。</summary>
    public sealed class ActionResolver
    {
        private readonly List<IBarAction> actions = new List<IBarAction>();
        /// <summary>注册动作规则。</summary>
        /// <precondition>规则非空，Id 非空且未注册过。</precondition>
        /// <effects>按注册顺序加入规则列表；重复标识抛异常。</effects>
        public ActionResolver Register(IBarAction action)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.Id) || actions.Any(x => x.Id == action.Id)) throw new ArgumentException("Unique action ID required.");
            actions.Add(action); return this;
        }
        /// <summary>创建基础交互动作集合。</summary>
        /// <precondition>Controls 非空并能通过参数校验。</precondition>
        /// <effects>创建新的解析器，注册倒料、取下与盖上瓶盖、拿放和雪克壶拆装；不注册加冰、摇搅和出杯。</effects>
        public static ActionResolver CreateU1(Controls controls) => new ActionResolver().Register(new BottlePourAction(controls)).Register(new GuidedPourAction(controls)).Register(new JiggerPourAction(controls))
            .Register(new OpenBottleAction()).Register(new CloseBottleAction()).Register(new AttachStrainerAction()).Register(new AttachCapAction()).Register(new DetachCapAction()).Register(new DetachStrainerAction()).Register(new PlaceAction()).Register(new PickUpAction());
        /// <summary>在已审 U3 动作后注册全部 U4 制作技法。</summary>
        /// <precondition>控制参数已校验；每块冰克数和密度来自唯一核心数据。</precondition>
        /// <effects>返回独立解析器，保留原动作优先级并加入冰、压汁、摇搅、壶倒出、清空和出杯。</effects>
        public static ActionResolver CreateU4(Controls controls,double iceGrams,double iceDensity,Func<double> random,Func<Container,int> maximumIceCubes=null)=>CreateU1(controls)
            .Register(new TakeIceAction(random)).Register(new AddToolIceAction(iceGrams,iceDensity,maximumIceCubes)).Register(new DiscardToolIceAction(iceGrams)).Register(new LoadLemonAction()).Register(new PressLemonAction())
            .Register(new ShakeAction(controls)).Register(new StirAction(controls)).Register(new ShakerPourAction(controls))
            .Register(new FlipJiggerAction()).Register(new EmptyIntoSinkAction(iceGrams)).Register(new DiscardSpentLemonAction()).Register(new ServeAction()).Register(new WipeShakerAction());
        /// <summary>列出目标的全部当前可执行动作。</summary>
        /// <precondition>Hands 非空；空目标或双手被连续动作锁定时返回空列表。</precondition>
        /// <effects>按主手、另一手、空手查询所有规则；返回只读列表，不执行动作。</effects>
        public IReadOnlyList<ActionOption> Resolve(Item target, Hands hands)
        {
            if (hands == null) throw new ArgumentNullException(nameof(hands));
            var result = new List<ActionOption>(); if (target == null || hands.IsBusy) return result.AsReadOnly();
            var other = hands.MainHand == HandSide.Left ? HandSide.Right : HandSide.Left;
            foreach (var side in new[] { hands.MainHand, other })
            { var actor = hands.Get(side); if (actor == null) continue; foreach (var action in actions) if (action.CanExecute(actor, target, hands)) result.Add(new ActionOption(action, actor, target, side)); }
            foreach (var action in actions) if (action.CanExecute(null, target, hands)) result.Add(new ActionOption(action, null, target, null));
            return result.AsReadOnly();
        }
        /// <summary>取得目标的主动作候选。</summary>
        /// <precondition>Hands 非空；目标可以没有可执行动作。</precondition>
        /// <effects>调用 Resolve 并返回首项或空值，不改变主手。</effects>
        public ActionOption Primary(Item target, Hands hands) => Resolve(target, hands).FirstOrDefault();
    }
}
