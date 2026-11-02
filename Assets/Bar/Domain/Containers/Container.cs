using System;
using System.Collections.Generic;
using Bar.Data;

namespace Bar.Domain
{
    /// <summary>保存核心提供的只读液量、冰块数与气泡状态。</summary>
    public sealed class ContainerSnapshot
    {
        /// <summary>核心返回的液体体积，单位毫升。</summary>
        public double LiquidMl { get; }
        /// <summary>核心冰质量换算得到的等效块数。</summary>
        public double IceCubes { get; }
        /// <summary>核心是否报告可见碳酸气泡。</summary>
        public bool HasBubbles { get; }
        /// <summary>核心报告的冰占位体积，单位毫升。</summary>
        public double IceVolumeMl { get; }
        /// <summary>创建ContainerSnapshot。</summary>
        /// <precondition>液体体积与冰块数有限且非负。</precondition>
        /// <effects>保存只读液量、冰块与气泡状态，不推演物理。</effects>
        public ContainerSnapshot(double liquidMl = 0, double iceCubes = 0, bool hasBubbles = false, double iceVolumeMl = 0)
        { Guard.Nonnegative(liquidMl); Guard.Nonnegative(iceCubes); LiquidMl = liquidMl; IceCubes = iceCubes; HasBubbles = hasBubbles; Guard.Nonnegative(iceVolumeMl); IceVolumeMl = iceVolumeMl; }
    }
    /// <summary>以毫升管理容量、刻度、数值容器标识和未确认请求的预留量。</summary>
    public abstract class Container : Item, ILiquidReceiver, ILiquidSource
    {
        private double reservedIn, reservedOut;
        private int pendingOperations;
        public Container ReceivingContainer => this;
        public double ReceivableMl => IsOpenTop && !HasPending && !IsDiscarded ? AvailableMl : 0;
        public virtual LiquidControl LiquidControl => LiquidControl.Measured;
        public double LiquidAvailableMl => IsOpenTop && !HasPending && !IsDiscarded ? PourableMl : 0;
        public double LiquidCapacityMl => CapacityMl;
        public double? FixedFlowMlPerSecond => null;
        public Container SourceVessel => this;
        public OperationRequest CreateLiquidRequest(Container receiver, double intendedMl,
            double angle, double heldSeconds, double seconds, double pressure)
        {
            double amount = Math.Min(intendedMl, Math.Min(LiquidAvailableMl, receiver.ReceivableMl));
            return new OperationRequest("TransferLiquid", this, receiver, amount, seconds: seconds,
                angle: angle, heldSeconds: heldSeconds, intendedAmount: intendedMl);
        }
        /// <summary>液体容量上限，单位毫升。</summary>
        public double CapacityMl => Definition.CapacityMl;
        /// <summary>只读刻度列表，单位毫升。</summary>
        public IReadOnlyList<double> MarkingsMl => Definition.MarkingsMl;
        /// <summary>玻璃或不锈钢的材质标签。</summary>
        public ContainerMaterial Material => Definition.Material;
        /// <summary>传给核心的容器热学参数，不在领域层计算传热。</summary>
        public VesselThermal Thermal => Definition.Thermal;
        /// <summary>该容器在核心中独立使用的稳定标识。</summary>
        public string CoreVesselId { get; }
        /// <summary>顶部是否允许加料；盖住的壶身、非朝上端或已出杯的杯子会拒绝。</summary>
        public virtual bool IsOpenTop => true;
        /// <summary>倒出液体使用的锚点名称。</summary>
        public string PourLip => "PourLip";
        /// <summary>最后一次核心权威快照，不随未确认提案预先改变。</summary>
        public ContainerSnapshot Contents { get; private set; } = new ContainerSnapshot();
        /// <summary>扣除已预留输入后尚可接收的体积，单位毫升。</summary>
        public double AvailableMl => Math.Max(0, CapacityMl - Contents.LiquidMl - Contents.IceVolumeMl - reservedIn);
        /// <summary>扣除已预留输出后尚可倒出的液体体积，单位毫升。</summary>
        public double PourableMl => Math.Max(0, Contents.LiquidMl - reservedOut);
        /// <summary>创建Container。</summary>
        /// <precondition>id 非空，定义运行时类型匹配当前容器子类。</precondition>
        /// <effects>建立 vessel:id 形式的独立核心标识和空快照，不向核心注册。</effects>
        protected Container(string id, ItemDefinition definition) : base(id, definition) { CoreVesselId = "vessel:" + id; }
        /// <summary>接受容器最新核心快照。</summary>
        /// <precondition>快照非空且液量不超 CapacityMl；该容器没有待确认输入或输出。</precondition>
        /// <effects>替换 Contents，不计算温度、融冰或传热。</effects>
        public void ApplySnapshot(ContainerSnapshot snapshot)
        { ValidateSnapshot(snapshot); if (HasPending) throw new InvalidOperationException("Resolve pending requests first."); Contents = snapshot; }
        internal void ValidateSnapshot(ContainerSnapshot snapshot) { if (snapshot == null || snapshot.LiquidMl + snapshot.IceVolumeMl > CapacityMl + 1e-8) throw new ArgumentException("Snapshot exceeds capacity."); }
        internal bool HasPending => reservedIn > 0 || reservedOut > 0 || pendingOperations > 0;
        internal void ReserveOperation(){if(HasPending)throw new InvalidOperationException("Await acknowledgement");pendingOperations++;}
        internal void ReleaseOperation(){pendingOperations=Math.Max(0,pendingOperations-1);}
        internal void ReserveIn(double ml) { Guard.Range(ml, AvailableMl); reservedIn += ml; }
        internal void ReserveOut(double ml) { Guard.Range(ml, PourableMl); reservedOut += ml; }
        internal void ReleaseIn(double ml) { reservedIn = Math.Max(0, reservedIn - ml); }
        internal void ReleaseOut(double ml) { reservedOut = Math.Max(0, reservedOut - ml); }
        internal void AcceptSnapshot(ContainerSnapshot snapshot) { Contents = snapshot; }
    }
    /// <summary>Coupe 或 Highball 杯型。</summary>
    public enum GlassType { Coupe, Highball }
    /// <summary>表示杯型和出杯状态，已出杯后拒绝继续加料。</summary>
    public sealed class Glass : Container
    {
        /// <summary>由杯子定义选择的杯型。</summary>
        public GlassType GlassType { get; }
        /// <summary>是否已经由宿主确认出杯。</summary>
        public bool IsServed { get; private set; }
        /// <summary>创建Glass。</summary>
        /// <precondition>id 非空，definition.Type 必须为 Glass。</precondition>
        /// <effects>保存指定杯型，建立空容器快照，IsServed 初始为 false。</effects>
        public Glass(string id, ItemDefinition definition, GlassType type) : base(id, definition) { GlassType = type; }
        /// <summary>记录宿主已确认的出杯状态。</summary>
        /// <precondition>杯子没有待确认操作请求。</precondition>
        /// <effects>IsServed=true；IsOpenTop 随之为 false，后续倒入规则拒绝加料。</effects>
        public void MarkServed() { if (HasPending) throw new InvalidOperationException("Pending request."); IsServed = true; }
        /// <summary>顶部是否允许加料；盖住的壶身、非朝上端或已出杯的杯子会拒绝。</summary>
        public override bool IsOpenTop => !IsServed;
    }
    /// <summary>表示雪克壶的数值容器，装配规则由父级雪克壶管理。</summary>
    public sealed class ShakerBody : Container
    {
        /// <summary>创建ShakerBody。</summary>
        /// <precondition>id 非空，definition.Type 必须为 ShakerBody。</precondition>
        /// <effects>建立 ShakerBody 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        public ShakerBody(string id, ItemDefinition definition) : base(id, definition) { }
        /// <summary>顶部是否允许加料；盖住的壶身、非朝上端或已出杯的杯子会拒绝。</summary>
        public override bool IsOpenTop => !(Parent is CobblerShaker shaker) || shaker.State == ShakerState.Open;
    }
    /// <summary>表示量酒器的一端，只有朝上端可接收液体。</summary>
    public sealed class JiggerCup : Container
    {
        /// <summary>量酒杯一端的标称容量，单位毫升。</summary>
        public double NominalMl => MarkingsMl.Count == 0 ? CapacityMl : MarkingsMl[MarkingsMl.Count - 1];
        /// <summary>创建JiggerCup。</summary>
        /// <precondition>id 非空，definition.Type 必须为 JiggerCup。</precondition>
        /// <effects>建立 JiggerCup 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        public JiggerCup(string id, ItemDefinition definition) : base(id, definition) { }
        /// <summary>顶部是否允许加料；盖住的壶身、非朝上端或已出杯的杯子会拒绝。</summary>
        public override bool IsOpenTop => !(Parent is Jigger jigger) || ReferenceEquals(jigger.ActiveCup, this);
    }
}
