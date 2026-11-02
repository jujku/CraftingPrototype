using System;
using Bar.Data;
namespace Bar.Domain
{
    /// <summary>为 U4 制作动作提供统一的重复校验入口。</summary>
    public abstract class CraftAction : IBarAction
    {
        /// <summary>稳定动作标识。</summary>
        public abstract string Id { get; }
        /// <summary>中文制作名称。</summary>
        public abstract string Label { get; }
        /// <summary>动作输入方式。</summary>
        public virtual InputStyle InputStyle => InputStyle.Click;
        /// <summary>只检查当前物品、目标及双手能否制作。</summary>
        /// <precondition>允许空施动者和空目标；不得依赖 Unity 场景。</precondition>
        /// <effects>只返回候选可用性，不扣料或锁手。</effects>
        public abstract bool CanExecute(Item actor,Item target,Hands hands);
        /// <summary>复查前置条件后建立独立激活。</summary>
        /// <precondition>当前 CanExecute 仍为 true；旧菜单候选不能绕过规则。</precondition>
        /// <effects>调用具体创建逻辑，立即动作改变所有权，连续动作锁手；失败不执行。</effects>
        public ActiveAction Begin(Item actor,Item target,Hands hands)
        {if(!CanExecute(actor,target,hands))throw new InvalidOperationException("动作条件已变化："+Label);return Create(actor,target,hands);}
        /// <summary>建立具体动作激活。</summary>
        /// <precondition>CanExecute 已通过；参数保留本次施动者和目标。</precondition>
        /// <effects>由具体动作产生请求或立即完成工具状态改变。</effects>
        protected abstract ActiveAction Create(Item actor,Item target,Hands hands);
        internal static bool Usable(Item actor,Hands hands,string id)=>actor!=null&&hands!=null&&hands.CanUse(actor,id)&&!ItemAccess.Pending(actor)&&!actor.IsDiscarded;
        internal static bool Receiver(Item target,Hands hands)=>InteractionAccess.ReceiveSpace(target, hands) > 1e-8;
    }
    /// <summary>冰铲按数据随机取一铲，冰夹取一块；取出的冰暂存工具中。</summary>
    public sealed class TakeIceAction : CraftAction
    {
        private readonly Func<double> random;
        /// <summary>取冰标识。</summary>
        public override string Id=>"ice.take";
        /// <summary>菜单名称。</summary>
        public override string Label=>"取冰";
        /// <summary>创建可复现随机取冰规则。</summary>
        /// <precondition>random 返回零到一之间的数。</precondition>
        /// <effects>保存随机源，不抽取或消费冰库存。</effects>
        public TakeIceAction(Func<double> random){this.random=random??throw new ArgumentNullException(nameof(random));}
        /// <summary>检查取冰的工具、目标和持物条件。</summary>
        /// <precondition>手持空冰铲或空冰夹，冰桶有库存，双手空闲。</precondition>
        /// <effects>只查询本动作条件，不改变库存、容器或双手。</effects>
        public override bool CanExecute(Item actor,Item target,Hands hands)=>Usable(actor,hands,Id)&&(actor is IceScoop||actor is IceTongs)&&((Tool)actor).LoadedCubes==0&&target is IceBin bin&&bin.StockCubes>0;
        /// <summary>创建取冰激活。</summary>
        /// <precondition>手持空冰铲或空冰夹，冰桶有库存，双手空闲，且已通过 Begin 的重复校验。</precondition>
        /// <effects>按数据范围抽取一铲或固定一块；从冰桶扣块并装入工具，不进入核心。</effects>
        protected override ActiveAction Create(Item actor,Item target,Hands hands)=>new ImmediateAction(this,actor,target,hands,()=>{var tool=(Tool)actor;var bin=(IceBin)target;double unit=random();Guard.Range(unit,1);int cubes=actor is IceScoop scoop?scoop.CubesPerScoop.Min+Math.Min(scoop.CubesPerScoop.Max-scoop.CubesPerScoop.Min,(int)(unit*(scoop.CubesPerScoop.Max-scoop.CubesPerScoop.Min+1))):1;tool.LoadedCubes=Math.Min(cubes,bin.StockCubes);bin.StockCubes-=tool.LoadedCubes;});
    }
    /// <summary>只把目标能接收的整块冰交给 AddIce，按实际确认块数扣工具库存。</summary>
    public sealed class AddToolIceAction : CraftAction
    {
        private readonly double grams,density;
        private readonly Func<Container,int> maximumCubes;
        /// <summary>加冰标识。</summary>
        public override string Id=>"ice.add";
        /// <summary>菜单名称。</summary>
        public override string Label=>"放入冰块";
        /// <summary>读取核心每块克数及冰密度。</summary>
        /// <precondition>grams、density 为核心数据中的正值。</precondition>
        /// <effects>只保存单位换算参数，不推演融冰。</effects>
        public AddToolIceAction(double grams,double density,Func<Container,int> maximumCubes=null){Guard.Nonnegative(grams);Guard.Nonnegative(density);if(grams==0||density==0)throw new ArgumentException();this.grams=grams;this.density=density;this.maximumCubes=maximumCubes;}
        private int Count(Tool tool,Item target,Hands hands)
        {
            if(target is not ILiquidReceiver receiver)return 0;
            int volume=(int)Math.Min(int.MaxValue,Math.Floor((InteractionAccess.ReceiveSpace(target,hands)*density+1e-8)/grams));
            int geometry=maximumCubes==null?int.MaxValue:Math.Max(0,maximumCubes(receiver.ReceivingContainer)-(int)Math.Ceiling(receiver.ReceivingContainer.Contents.IceCubes));
            return Math.Max(0,Math.Min(tool.LoadedCubes,Math.Min(volume,geometry)));
        }
        /// <summary>检查放入冰块的工具、目标和持物条件。</summary>
        /// <precondition>手持带冰的冰铲或冰夹，目标敞口且剩余占位容量足够。</precondition>
        /// <effects>只查询本动作条件，不改变库存、容器或双手。</effects>
        public override bool CanExecute(Item actor, Item target, Hands hands)
        {
            return Usable(actor, hands, Id) && (actor is IceScoop || actor is IceTongs) &&
                ((Tool)actor).LoadedCubes > 0 && Count((Tool)actor,target,hands)>0;
        }
        protected override ActiveAction Create(Item actor, Item target, Hands hands)
        {
            var tool = (Tool)actor;
            var request = new OperationRequest("AddIce", null, ((ILiquidReceiver)target).ReceivingContainer,
                Count(tool,target,hands) * grams, intendedAmount:tool.LoadedCubes*grams,accepted: amount =>
                {
                    tool.LoadedCubes=Math.Max(0,tool.LoadedCubes-(int)Math.Round(amount/grams));
                });
            return new RequestAction(this, actor, target, hands, request);
        }
    }
    /// <summary>把手中半颗柠檬装入空榨汁器，保留唯一所有权。</summary>
    public sealed class LoadLemonAction : PairedCraftAction<IProduceHolder, ILoadableProduce>
    {
        public override string Id => "lemon.load";
        public override string Label => "装入柠檬";
        public override bool CanExecute(Item actor, Item target, Hands hands)
        {
            return Pair(actor, target, hands, out var holder, out var produce) && holder.CanLoad(produce);
        }
        protected override ActiveAction Create(Item actor, Item target, Hands hands)
        {
            Pair(actor, target, hands, out var holder, out var produce);
            return new ImmediateAction(this, actor, target, hands, () => holder.Load(produce, hands));
        }
    }
    /// <summary>通过液体来源能力把下压进度交给统一倒入通道。</summary>
    public sealed class PressLemonAction : CraftAction
    {
        public override string Id => "lemon.press";
        public override string Label => "压汁";
        public override InputStyle InputStyle => InputStyle.HoldPress;
        public override bool CanExecute(Item actor, Item target, Hands hands)
        {
            var source = actor as ILiquidSource;
            return Usable(actor, hands, Id) && source?.LiquidControl == LiquidControl.Pressure &&
                LiquidTransfer.CanTransfer(source, target, hands);
        }
        protected override ActiveAction Create(Item actor, Item target, Hands hands)
        {
            return new PressSession(this, actor, (ILiquidReceiver)target, hands);
        }
    }
    /// <summary>盖好雪克壶且另一手空闲时，往复手势驱动唯一核心 Shake。</summary>
    public sealed class ShakeAction : CraftAction
    {
        private readonly Controls controls;
        /// <summary>摇壶标识，与 HandRule 双手规则相同。</summary>
        public override string Id=>"shaker.shake";
        /// <summary>菜单名称。</summary>
        public override string Label=>"双手摇壶";
        /// <summary>按住并上下往复。</summary>
        public override InputStyle InputStyle=>InputStyle.HoldOscillate;
        /// <summary>保存手势控制参数。</summary>
        /// <precondition>controls 已通过数据校验。</precondition>
        /// <effects>保存参数，不立即锁手。</effects>
        public ShakeAction(Controls controls){this.controls=controls;}
        /// <summary>检查双手摇壶的工具、目标和持物条件。</summary>
        /// <precondition>手持有液体的 Sealed 雪克壶，目标为自身，另一手必须空闲。</precondition>
        /// <effects>只查询本动作条件，不改变库存、容器或双手。</effects>
        public override bool CanExecute(Item actor,Item target,Hands hands)=>Usable(actor,hands,Id)&&actor is CobblerShaker shaker&&ReferenceEquals(actor,target)&&shaker.CanShake&&shaker.Body.Contents.LiquidMl>0&&(hands.Left==null||hands.Right==null);
        /// <summary>创建双手摇壶激活。</summary>
        /// <precondition>手持有液体的 Sealed 雪克壶，目标为自身，另一手必须空闲，且已通过 Begin 的重复校验。</precondition>
        /// <effects>锁定双手，统计往复换向，提出 Shake 时间和强度；模型时间只计一次。</effects>
        protected override ActiveAction Create(Item actor,Item target,Hands hands)=>new AgitateSession(this,actor,((CobblerShaker)actor).Body,hands,controls,"Shake");
    }
    /// <summary>手持吧勺对敞口容器画圈，强度由圆周手势统计得到。</summary>
    public sealed class StirAction : CraftAction
    {
        private readonly Controls controls;
        /// <summary>搅拌标识。</summary>
        public override string Id=>"spoon.stir";
        /// <summary>菜单名称。</summary>
        public override string Label=>"搅拌";
        /// <summary>按住并画圈。</summary>
        public override InputStyle InputStyle=>InputStyle.HoldCircle;
        /// <summary>保存圆周手势参数。</summary>
        /// <precondition>controls 已校验。</precondition>
        /// <effects>保存参数，不立即推进世界。</effects>
        public StirAction(Controls controls){this.controls=controls;}
        /// <summary>检查搅拌的工具、目标和持物条件。</summary>
        /// <precondition>手持吧勺，目标敞口且有液体，仍有可操作空间。</precondition>
        /// <effects>只查询本动作条件，不改变库存、容器或双手。</effects>
        public override bool CanExecute(Item actor,Item target,Hands hands)=>Usable(actor,hands,Id)&&actor is BarSpoon&&Receiver(target,hands)&&ItemAccess.TargetContainer(target).Contents.LiquidMl>0;
        /// <summary>创建搅拌激活。</summary>
        /// <precondition>手持吧勺，目标敞口且有液体，仍有可操作空间，且已通过 Begin 的重复校验。</precondition>
        /// <effects>按圆周角速率提出 Stir 时间和强度；停止鼠标超过 0.3 s 后强度为零。</effects>
        protected override ActiveAction Create(Item actor,Item target,Hands hands)=>new AgitateSession(this,actor,ItemAccess.TargetContainer(target),hands,controls,"Stir");
    }
    /// <summary>从未盖上盖的手持雪克壶倾倒；隔冰器决定 StrainTransfer 或 TransferWithIce。</summary>
    public sealed class ShakerPourAction : CraftAction
    {
        private readonly Controls controls;private readonly TiltFlowModel flow;
        /// <summary>壶倒出标识。</summary>
        public override string Id=>"shaker.pour";
        /// <summary>菜单名称。</summary>
        public override string Label=>"壶倒出";
        /// <summary>按住并倾斜。</summary>
        public override InputStyle InputStyle=>InputStyle.HoldTilt;
        /// <summary>沿用同一倾斜流量曲线。</summary>
        /// <precondition>controls 已校验。</precondition>
        /// <effects>创建输入流量模型，不执行转移。</effects>
        public ShakerPourAction(Controls controls){this.controls=controls;flow=new TiltFlowModel(controls);}
        /// <summary>检查壶倒出的工具、目标和持物条件。</summary>
        /// <precondition>手持有液体且未盖上盖的壶，目标为另一个敞口有容量的容器。</precondition>
        /// <effects>只查询本动作条件，不改变库存、容器或双手。</effects>
        public override bool CanExecute(Item actor, Item target, Hands hands)
        {
            var source = actor as ILiquidSource;
            return Usable(actor, hands, Id) && source?.LiquidControl == LiquidControl.VesselTilt &&
                LiquidTransfer.CanTransfer(source, target, hands);
        }
        protected override ActiveAction Create(Item actor, Item target, Hands hands)
        {
            return new ShakerPourSession(this, actor, (ILiquidReceiver)target, hands, flow);
        }
    }
    /// <summary>把杯、量酒器或敞口壶中的冰与液体倒入水槽废料账本。</summary>
    public sealed class EmptyIntoSinkAction : CraftAction
    {
        private readonly double iceGrams;
        /// <summary>保存核心每块冰克数用于废料请求计量。</summary>
        /// <precondition>iceGrams 来自核心冰块定义且为正。</precondition>
        /// <effects>只保存克数，不直接消耗冰。</effects>
        public EmptyIntoSinkAction(double iceGrams){this.iceGrams=iceGrams;}
        /// <summary>清空标识。</summary>
        public override string Id=>"sink.empty";
        /// <summary>菜单名称。</summary>
        public override string Label=>"倒掉冰和液体";
        /// <summary>检查倒掉冰和液体的工具、目标和持物条件。</summary>
        /// <precondition>手持未出杯且有内容的杯、量酒器或未封盖壶，目标为水槽。</precondition>
        /// <effects>只查询本动作条件，不改变库存、容器或双手。</effects>
        public override bool CanExecute(Item actor,Item target,Hands hands)
        {var c=ItemAccess.TargetContainer(actor);return Usable(actor,hands,Id)&&target is Sink&&c!=null&&!(c is Glass g&&g.IsServed)&&(!(actor is CobblerShaker s)||s.State!=ShakerState.Sealed)&&(c.Contents.LiquidMl>0||c.Contents.IceCubes>0);}
        /// <summary>创建倒掉冰和液体激活。</summary>
        /// <precondition>手持未出杯且有内容的杯、量酒器或未封盖壶，目标为水槽，且已通过 Begin 的重复校验。</precondition>
        /// <effects>依次请求 DiscardIce 与 DiscardLiquid，全部进核心废料账本。</effects>
        protected override ActiveAction Create(Item actor,Item target,Hands hands)=>new EmptySession(this,actor,target,hands,ItemAccess.TargetContainer(actor),iceGrams);
    }
    /// <summary>把用完的柠檬从手或榨汁器投入水槽，不丢弃未压出的原料。</summary>
    public sealed class DiscardSpentLemonAction : CraftAction
    {
        /// <summary>弃柠檬标识。</summary>
        public override string Id=>"lemon.discard";
        /// <summary>菜单名称。</summary>
        public override string Label=>"丢弃用完的柠檬";
        /// <summary>检查丢弃用完的柠檬的工具、目标和持物条件。</summary>
        /// <precondition>目标是水槽，手中柠檬或榨汁器内柠檬已用完。</precondition>
        /// <effects>只查询本动作条件，不改变库存、容器或双手。</effects>
        public override bool CanExecute(Item actor, Item target, Hands hands)
        {
            var produce = (actor as IProduceHolder)?.Produce ?? actor as ILoadableProduce;
            return Usable(actor, hands, Id) && target is Sink && produce != null && produce.RemainingMl <= 1e-9;
        }
        protected override ActiveAction Create(Item actor, Item target, Hands hands)
        {
            return new ImmediateAction(this, actor, target, hands, () =>
            {
                var holder = actor as IProduceHolder;
                var produce = (actor as IProduceHolder)?.Produce ?? (ILoadableProduce)actor;
                produce.ProduceItem.IsDiscarded = true;
                if (holder != null) holder.UnloadSpent();
                else hands.ReleaseItem(produce.ProduceItem);
            });
        }
    }
    /// <summary>把有液体的手持杯放到出杯垫，核心确认后冻结并释放持物。</summary>
    public sealed class ServeAction : CraftAction
    {
        /// <summary>出杯标识。</summary>
        public override string Id=>"glass.serve";
        /// <summary>菜单名称。</summary>
        public override string Label=>"放到出杯垫";
        /// <summary>检查放到出杯垫的工具、目标和持物条件。</summary>
        /// <precondition>手持未出杯、有液体的玻璃杯，目标为出杯垫。</precondition>
        /// <effects>只查询本动作条件，不改变库存、容器或双手。</effects>
        public override bool CanExecute(Item actor,Item target,Hands hands)=>Usable(actor,hands,Id)&&actor is Glass glass&&!glass.IsServed&&glass.Contents.LiquidMl>0&&target is ServeMat;
        /// <summary>创建放到出杯垫激活。</summary>
        /// <precondition>手持未出杯、有液体的玻璃杯，目标为出杯垫，且已通过 Begin 的重复校验。</precondition>
        /// <effects>提出 Serve；核心确认后冻结杯子、释放该手并把位置记到出杯垫。</effects>
        protected override ActiveAction Create(Item actor,Item target,Hands hands)=>new RequestAction(this,actor,target,hands,new OperationRequest("Serve",(Glass)actor,null,1,accepted:n=>{if(n<=0)return;((Glass)actor).MarkServed();hands.ReleaseItem(actor);actor.Location=new ItemLocation(LocationKind.Surface,target.InstanceId,"Surface");}));
    }
    internal sealed class RequestAction : ActiveAction
    {
        internal RequestAction(IBarAction action,Item actor,Item target,Hands hands,OperationRequest request):base(action,actor,target,hands,true){Add(request);}
        public override void Update(ActionInput input,double dt){ValidateUpdate(input,dt);End();}
    }
    internal sealed class PressSession : ActiveAction
    {
        private readonly ILiquidSource source;
        private readonly ILiquidReceiver receiver;
        private readonly double initial;
        private double confirmed;
        private double maxPress;
        private double elapsed;
        internal PressSession(IBarAction action, Item actor, ILiquidReceiver receiver, Hands hands)
            : base(action, actor, (Item)receiver, hands, true)
        {
            source = (ILiquidSource)actor;
            this.receiver = receiver;
            initial = source.LiquidAvailableMl;
        }
        public override void Update(ActionInput input, double dt)
        {
            ValidateUpdate(input, dt);
            elapsed += dt;
            maxPress = Math.Max(maxPress, input.PressFraction);
            var request = LiquidTransfer.Request(source, receiver, Math.Max(0, initial * maxPress - confirmed),
                0, elapsed, dt, maxPress);
            if (request == null) return;
            request.Resolved += e => confirmed += request.ActualAmount ?? 0;
            Add(request);
        }
    }
    internal sealed class AgitateSession : ActiveAction
    {
        private readonly GestureAnalyzer gesture;private readonly Container vessel;private readonly string mode;private double lastX,lastY,idle,elapsed;
        internal AgitateSession(IBarAction action,Item actor,Container vessel,Hands hands,Controls controls,string mode):base(action,actor,vessel,hands,true){this.vessel=vessel;this.mode=mode;gesture=new GestureAnalyzer(controls);}
        public override void Update(ActionInput input,double dt)
        {ValidateUpdate(input,dt);elapsed+=dt;if(input.GestureX==lastX&&input.GestureY==lastY)idle+=dt;else idle=0;lastX=input.GestureX;lastY=input.GestureY;gesture.Update(lastX,lastY,dt);double strength=mode=="Shake"?gesture.OscillationIntensity:gesture.CircleIntensity;if(dt<=0||idle>.3||strength<=0||vessel.HasPending)return;Add(new OperationRequest(mode,vessel,null,dt,seconds:dt,intensity:strength,heldSeconds:elapsed));}
    }
    internal sealed class ShakerPourSession : ActiveAction
    {
        private readonly ILiquidSource source;
        private readonly ILiquidReceiver receiver;
        private readonly TiltFlowModel flow;
        private double elapsed;
        internal ShakerPourSession(IBarAction action, Item actor, ILiquidReceiver receiver, Hands hands,
            TiltFlowModel flow) : base(action, actor, (Item)receiver, hands, true)
        {
            source = (ILiquidSource)actor;
            this.receiver = receiver;
            this.flow = flow;
        }
        public override void Update(ActionInput input, double dt)
        {
            ValidateUpdate(input, dt);
            elapsed += dt;
            if (dt <= 0) return;
            double intended = flow.Flow(input.TiltDegrees, source.LiquidAvailableMl, source.LiquidCapacityMl) * dt;
            var request = LiquidTransfer.Request(source, receiver, intended, input.TiltDegrees, elapsed, dt);
            if (request != null) Add(request);
        }
    }
    internal sealed class EmptySession : ActiveAction
    {
        private readonly Container vessel;private int stage;
        internal EmptySession(IBarAction action,Item actor,Item target,Hands hands,Container vessel,double iceGrams):base(action,actor,target,hands,true){this.vessel=vessel;Add(new OperationRequest("DiscardIce",vessel,null,vessel.Contents.IceCubes*iceGrams));}
        public override void Update(ActionInput input,double dt){ValidateUpdate(input,dt);if(vessel.HasPending)return;if(stage++==0)Add(new OperationRequest("DiscardLiquid",vessel,null,vessel.Contents.LiquidMl));else End();}
    }
}
