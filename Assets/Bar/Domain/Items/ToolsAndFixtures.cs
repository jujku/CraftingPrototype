using System;
using Bar.Data;

namespace Bar.Domain
{
    /// <summary>为手持工具提供身份基类，具体技法由独立动作实现。</summary>
    public abstract class Tool : Item
    {
        /// <summary>冰铲或冰夹已经从冰桶取出的完整冰块数。</summary>
        public int LoadedCubes { get; internal set; }
        /// <summary>创建Tool。</summary>
        /// <precondition>id 非空，definition.Type 必须为 Tool。</precondition>
        /// <effects>建立 Tool 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        protected Tool(string id, ItemDefinition definition) : base(id, definition) { }
    }
    /// <summary>表示用于后续搅拌动作的吧勺。</summary>
    public sealed class BarSpoon : Tool
    {
        /// <summary>创建BarSpoon。</summary>
        /// <precondition>id 非空，definition.Type 必须为 BarSpoon。</precondition>
        /// <effects>建立 BarSpoon 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        public BarSpoon(string id, ItemDefinition definition) : base(id, definition) { }
    }
    /// <summary>保存数据定义的一铲冰块数范围。</summary>
    public sealed class IceScoop : Tool
    {
        /// <summary>一铲冰的最少与最多块数，均包含端点。</summary>
        public (int Min, int Max) CubesPerScoop => (Definition.MinCubes, Definition.MaxCubes);
        /// <summary>创建IceScoop。</summary>
        /// <precondition>id 非空，definition.Type 必须为 IceScoop。</precondition>
        /// <effects>建立 IceScoop 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        public IceScoop(string id, ItemDefinition definition) : base(id, definition) { }
    }
    /// <summary>表示每次夹取一块冰的冰夹。</summary>
    public sealed class IceTongs : Tool
    {
        /// <summary>每次夹取一块冰。</summary>
        public int CubesPerGrip => 1;
        /// <summary>创建IceTongs。</summary>
        /// <precondition>id 非空，definition.Type 必须为 IceTongs。</precondition>
        /// <effects>建立 IceTongs 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        public IceTongs(string id, ItemDefinition definition) : base(id, definition) { }
    }
    /// <summary>表示手压榨汁器，并保存已装入的半颗柠檬。</summary>
    public sealed class Squeezer : Tool, IProduceHolder, ILiquidSource
    {
        /// <summary>榨汁器中已装入的半颗柠檬，空载时为空。</summary>
        public LemonHalf LoadedProduce => Produce as LemonHalf;
        public ILoadableProduce Produce { get; private set; }
        public LiquidControl LiquidControl => LiquidControl.Pressure;
        public double LiquidAvailableMl => Produce?.RemainingMl ?? 0;
        public double LiquidCapacityMl => LiquidAvailableMl;
        public double? FixedFlowMlPerSecond => null;
        public Container SourceVessel => null;
        public void UnloadSpent()
        {
            if (Produce == null || !Produce.IsSpent) throw new InvalidOperationException("Produce is not spent.");
            Produce.ProduceItem.IsDiscarded = true;
            Produce = null;
        }
        public bool CanLoad(ILoadableProduce produce)
        {
            return Produce == null && produce != null && produce.RemainingMl > 1e-9 &&
                !produce.ProduceItem.IsDiscarded && produce.ProduceItem.Location.Kind != LocationKind.Attached;
        }
        public void Load(ILoadableProduce produce, Hands hands)
        {
            if (!CanLoad(produce)) throw new InvalidOperationException("Cannot load produce.");
            var item = produce.ProduceItem;
            if (hands.SideOf(item).HasValue) hands.ReleaseItem(item);
            item.Location = new ItemLocation(LocationKind.Attached, InstanceId, "LoadSlot");
            Produce = produce;
        }
        public OperationRequest CreateLiquidRequest(Container receiver, double intendedMl,
            double angle, double heldSeconds, double seconds, double pressure)
        {
            var produce = Produce;
            double amount = Math.Min(intendedMl, Math.Min(LiquidAvailableMl, receiver.ReceivableMl));
            return new OperationRequest("PourSource", null, receiver, amount, seconds: seconds,
                ingredient: produce.IngredientId, accepted: produce.Consume, heldSeconds: heldSeconds,
                pressFraction: pressure, intendedAmount: intendedMl);
        }
        /// <summary>创建Squeezer。</summary>
        /// <precondition>id 非空，definition.Type 必须为 Squeezer。</precondition>
        /// <effects>建立 Squeezer 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        public Squeezer(string id, ItemDefinition definition) : base(id, definition) { }
    }
    /// <summary>为创建时确定产量的实物食材提供身份基类。</summary>
    public abstract class Produce : Item
    {
        /// <summary>创建Produce。</summary>
        /// <precondition>id 非空，definition.Type 必须为 Produce。</precondition>
        /// <effects>建立 Produce 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        protected Produce(string id, ItemDefinition definition) : base(id, definition) { }
    }
    /// <summary>创建时抽取一次柠檬出汁量，记录尚未使用的汁量。</summary>
    public sealed class LemonHalf : Produce, ILoadableProduce
    {
        /// <summary>创建时抽取并固定的总出汁量，单位毫升。</summary>
        public double JuiceYieldMl { get; }
        public Item ProduceItem => this;
        public string IngredientId => "lemon";
        public double RemainingMl => JuiceRemainingMl;
        public void Consume(double actualMl)
        {
            ConsumeJuice(actualMl);
        }
        /// <summary>已经确认消耗后剩余的汁量，单位毫升。</summary>
        public double JuiceRemainingMl { get; private set; }
        /// <summary>剩余汁量是否已经耗尽。</summary>
        public bool IsSpent => JuiceRemainingMl <= 1e-9;
        internal void ConsumeJuice(double ml){Guard.Range(ml,JuiceRemainingMl);JuiceRemainingMl=Math.Max(0,JuiceRemainingMl-ml);}
        /// <summary>创建LemonHalf。</summary>
        /// <precondition>randomUnit 为零到一之间的有限数；定义的出汁范围已校验。</precondition>
        /// <effects>按数据范围抽一次总出汁量，剩余汁量初始等于总量。</effects>
        public LemonHalf(string id, ItemDefinition definition, double randomUnit) : base(id, definition)
        { Guard.Range(randomUnit, 1); JuiceYieldMl = definition.YieldMinMl + (definition.YieldMaxMl - definition.YieldMinMl) * randomUnit; JuiceRemainingMl = JuiceYieldMl; }
    }
    /// <summary>为不能被拿起的固定设施提供身份基类。</summary>
    public abstract class Fixture : Item
    {
        /// <summary>创建Fixture。</summary>
        /// <precondition>id 非空，definition.Type 必须为 Fixture。</precondition>
        /// <effects>建立 Fixture 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        protected Fixture(string id, ItemDefinition definition) : base(id, definition) { }
    }
    /// <summary>提供放下物品的逻辑吧台表面。</summary>
    public sealed class BarCounter : Fixture
    {
        /// <summary>创建BarCounter。</summary>
        /// <precondition>id 非空，definition.Type 必须为 BarCounter。</precondition>
        /// <effects>建立 BarCounter 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        public BarCounter(string id, ItemDefinition definition) : base(id, definition) { }
    }
    /// <summary>保存以块计数的冰库存，取冰动作在后续里程碑接入。</summary>
    public sealed class IceBin : Fixture
    {
        /// <summary>冰桶剩余库存，单位块。</summary>
        public int StockCubes { get; internal set; }
        /// <summary>创建IceBin。</summary>
        /// <precondition>id 非空，definition.Type 必须为 IceBin。</precondition>
        /// <effects>创建固定设施，StockCubes 从已校验定义初始化。</effects>
        public IceBin(string id, ItemDefinition definition) : base(id, definition) { StockCubes = definition.StockCubes; }
    }
    /// <summary>表示废料接收目标，废料账本由物理核心维护。</summary>
    public sealed class Sink : Fixture
    {
        /// <summary>尚未进入核心容器的工具冰废弃库存；与核心容器的 Waste 分开记账，单位整块。</summary>
        public int DiscardedToolIceCubes {get;internal set;}
        /// <summary>创建Sink。</summary>
        /// <precondition>id 非空，definition.Type 必须为 Sink。</precondition>
        /// <effects>建立 Sink 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        public Sink(string id, ItemDefinition definition) : base(id, definition) { }
    }
    /// <summary>表示出杯区域，出杯动作在后续里程碑接入。</summary>
    public sealed class ServeMat : Fixture
    {
        /// <summary>创建ServeMat。</summary>
        /// <precondition>id 非空，definition.Type 必须为 ServeMat。</precondition>
        /// <effects>建立 ServeMat 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        public ServeMat(string id, ItemDefinition definition) : base(id, definition) { }
    }
    /// <summary>表示食材准备表面，本版不实现切割动作。</summary>
    public sealed class CuttingBoard : Fixture
    {
        /// <summary>创建CuttingBoard。</summary>
        /// <precondition>id 非空，definition.Type 必须为 CuttingBoard。</precondition>
        /// <effects>建立 CuttingBoard 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        public CuttingBoard(string id, ItemDefinition definition) : base(id, definition) { }
    }
}
