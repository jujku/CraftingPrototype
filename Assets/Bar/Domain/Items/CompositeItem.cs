using System;
using System.Collections.Generic;
using Bar.Data;

namespace Bar.Domain
{
    /// <summary>集中维护永久部件与可拆部件的唯一父物品关系。</summary>
    public abstract class CompositeItem : Item
    {
        private readonly List<Item> parts = new List<Item>();
        /// <summary>当前部件列表；在定义中保存永久部件的标识。</summary>
        public IReadOnlyList<Item> Parts => parts.AsReadOnly();
        /// <summary>创建CompositeItem。</summary>
        /// <precondition>id 非空，definition.Type 必须为 CompositeItem。</precondition>
        /// <effects>建立 CompositeItem 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        protected CompositeItem(string id, ItemDefinition definition) : base(id, definition) { }
        internal void Adopt(Item child, string slot)
        {
            if (child == null || child.Parent != null || child.Location.Kind != LocationKind.Surface || ReferenceEquals(child, this)) throw new InvalidOperationException("Part already owned or held.");
            for (Item p = this; p != null; p = p.Parent) if (ReferenceEquals(p, child)) throw new InvalidOperationException("Cyclic ownership.");
            child.Parent = this; child.Location = new ItemLocation(LocationKind.Attached, InstanceId, slot); parts.Add(child);
        }
        internal void Release(Item child)
        { if (!parts.Remove(child)) throw new InvalidOperationException("Unknown part."); child.Parent = null; child.Location = new ItemLocation(LocationKind.Surface); }
    }
    /// <summary>提供有类型约束的只读部件插槽，写入由复合物品控制。</summary>
    public sealed class Slot<T> where T : Attachment
    {
        /// <summary>稳定的插槽或锚点名称。</summary>
        public string Name { get; }
        /// <summary>当前附着的有类型约束部件，空槽时为空。</summary>
        public T Content { get; internal set; }
        /// <summary>插槽是否没有部件。</summary>
        public bool IsEmpty => Content == null;
        /// <summary>创建Slot。</summary>
        /// <precondition>插槽名称非空。</precondition>
        /// <effects>创建空的有类型约束插槽，不收养部件。</effects>
        public Slot(string name) { Guard.Text(name); Name = name; }
    }
    /// <summary>可装卸部件的宿主；雪克壶和带盖酒瓶共用同一套装上、取下及双手规则。</summary>
    public interface IAttachmentHost
    {
        /// <summary>检查部件能否装入宿主。</summary>
        /// <precondition>part 可为空；只查询，不释放手持部件。</precondition>
        /// <effects>返回插槽、顺序和适配检查结果，不改变归属。</effects>
        bool CanAttach(Attachment part);
        /// <summary>把逻辑表面上的独立部件装入宿主。</summary>
        /// <precondition>CanAttach 为 true，部件位于逻辑表面。</precondition>
        /// <effects>设置 Parent、Attached 位置和对应插槽。</effects>
        void Attach(Attachment part);
        /// <summary>检查部件能否从宿主取下。</summary>
        /// <precondition>part 可为空。</precondition>
        /// <effects>只返回检查结果，不改变归属。</effects>
        bool CanDetach(Attachment part);
        /// <summary>把部件拆到逻辑表面。</summary>
        /// <precondition>CanDetach 为 true。</precondition>
        /// <effects>清空插槽，Parent=null，位置改为 Surface。</effects>
        void Detach(Attachment part);
    }
    /// <summary>开放、已装隔冰器或已封盖的三种装配状态。</summary>
    public enum ShakerState { Open, StrainerOn, Sealed }
    /// <summary>管理壶身、隔冰器和上盖，并由插槽推导装配状态。</summary>
    public sealed class CobblerShaker : CompositeItem, ILiquidReceiver, ILiquidSource, IAttachmentHost
    {
        /// <summary>雪克壶永久拥有的壶身数值容器。</summary>
        public ShakerBody Body { get; }
        public Container ReceivingContainer => Body;
        public double ReceivableMl => Body.ReceivableMl;
        public LiquidControl LiquidControl => LiquidControl.VesselTilt;
        public double LiquidAvailableMl => State != ShakerState.Sealed && !Body.HasPending ? Body.PourableMl : 0;
        public double LiquidCapacityMl => Body.CapacityMl;
        public double? FixedFlowMlPerSecond => null;
        public Container SourceVessel => Body;
        public OperationRequest CreateLiquidRequest(Container receiver, double intendedMl,
            double angle, double heldSeconds, double seconds, double pressure)
        {
            double amount = Math.Min(intendedMl, Math.Min(LiquidAvailableMl, receiver.ReceivableMl));
            return new OperationRequest(IceCanPassOut ? "TransferWithIce" : "StrainTransfer", Body,
                receiver, amount, seconds: seconds, angle: angle, heldSeconds: heldSeconds,
                intendedAmount: intendedMl);
        }
        /// <summary>用于安装隔冰器的插槽。</summary>
        public Slot<ShakerStrainer> StrainerSlot { get; } = new Slot<ShakerStrainer>("Slot_Strainer");
        /// <summary>已有隔冰器支持的上盖插槽。</summary>
        public Slot<ShakerCap> CapSlot { get; } = new Slot<ShakerCap>("Slot_Cap");
        /// <summary>从隔冰器和上盖插槽推导的装配状态，不能单独赋值。</summary>
        public ShakerState State => StrainerSlot.IsEmpty ? ShakerState.Open : CapSlot.IsEmpty ? ShakerState.StrainerOn : ShakerState.Sealed;
        /// <summary>仅在装配状态为 Sealed 时为真。</summary>
        public bool CanShake => State == ShakerState.Sealed;
        /// <summary>只有未装隔冰器时，冰才能随倒出液体一起转移。</summary>
        public bool IceCanPassOut => StrainerSlot.IsEmpty;
        /// <summary>创建CobblerShaker。</summary>
        /// <precondition>壶身非空且不属于任何父物品，也未被手持；定义类型正确。</precondition>
        /// <effects>永久收养壶身并创建两个空插槽，初始 State=Open。</effects>
        public CobblerShaker(string id, ItemDefinition definition, ShakerBody body) : base(id, definition)
        { Body = body ?? throw new ArgumentNullException(nameof(body)); Adopt(body, "Body"); }
        /// <summary>检查雪克壶能否安装部件。</summary>
        /// <precondition>壶身无挂起请求；部件非空、无父物品且 FitsOn 包含本壶。</precondition>
        /// <effects>查询空槽及隔冰器先于上盖的顺序；不释放手持部件，也不改插槽。</effects>
        public bool CanAttach(Attachment part) => !Body.HasPending && part != null && part.Parent == null && part.Fits(this) &&
            (part is ShakerStrainer && StrainerSlot.IsEmpty || part is ShakerCap && !StrainerSlot.IsEmpty && CapSlot.IsEmpty);
        /// <summary>把独立表面部件装入雪克壶。</summary>
        /// <precondition>CanAttach 为 true，部件在逻辑表面；安装上盖前隔冰器已装且上盖槽为空。</precondition>
        /// <effects>设置 Parent 和 Attached 位置，加入 Parts，写入对应槽，State 随槽改变；本方法不直接发事件，装配动作发布 action_performed。</effects>
        public void Attach(Attachment part)
        {
            if (!CanAttach(part) || part.Location.Kind != LocationKind.Surface) throw new InvalidOperationException("Cannot attach part.");
            Adopt(part, part is ShakerCap ? CapSlot.Name : StrainerSlot.Name);
            if (part is ShakerCap cap) CapSlot.Content = cap; else StrainerSlot.Content = (ShakerStrainer)part;
        }
        /// <summary>检查部件能否从雪克壶取下。</summary>
        /// <precondition>壶身无挂起请求；part 是本壶上盖或未被上盖覆盖的隔冰器。</precondition>
        /// <effects>只返回装卸顺序检查结果，不改变部件归属。</effects>
        public bool CanDetach(Attachment part) => !Body.HasPending && part != null &&
            (ReferenceEquals(part, CapSlot.Content) || ReferenceEquals(part, StrainerSlot.Content) && CapSlot.IsEmpty);
        /// <summary>将上盖或裸露隔冰器拆到逻辑表面。</summary>
        /// <precondition>CanDetach 为 true；隔冰器必须先取下上盖。</precondition>
        /// <effects>清空对应槽，移出 Parts，Parent=null，位置改为 Surface；取下动作随后把部件放入空手。</effects>
        public void Detach(Attachment part)
        {
            if (!CanDetach(part)) throw new InvalidOperationException("Remove cap first.");
            if (part is ShakerCap) CapSlot.Content = null; else StrainerSlot.Content = null;
            Release(part);
        }
    }
    /// <summary>量酒器朝上的 A 端或 B 端。</summary>
    public enum JiggerEnd { A, B }
    /// <summary>管理两个独立数值容器及其朝上端。</summary>
    public sealed class Jigger : CompositeItem, ILiquidReceiver, ILiquidSource
    {
        /// <summary>量酒器的 A 端，参考容量三十毫升。</summary>
        public JiggerCup CupA { get; }
        /// <summary>量酒器的 B 端，参考容量四十五毫升。</summary>
        public JiggerCup CupB { get; }
        /// <summary>当前朝上的量酒器端。</summary>
        public JiggerEnd ActiveEnd { get; private set; }
        /// <summary>当前可以接收或倒出液体的量酒杯。</summary>
        public JiggerCup ActiveCup => ActiveEnd == JiggerEnd.A ? CupA : CupB;
        public Container ReceivingContainer => ActiveCup;
        public double ReceivableMl => ActiveCup.ReceivableMl;
        public LiquidControl LiquidControl => LiquidControl.Measured;
        public double LiquidAvailableMl => ActiveCup.LiquidAvailableMl;
        public double LiquidCapacityMl => ActiveCup.CapacityMl;
        public double? FixedFlowMlPerSecond => null;
        public Container SourceVessel => ActiveCup;
        public OperationRequest CreateLiquidRequest(Container receiver, double intendedMl,
            double angle, double heldSeconds, double seconds, double pressure)
        {
            return ActiveCup.CreateLiquidRequest(receiver, intendedMl, angle, heldSeconds, seconds, pressure);
        }
        /// <summary>创建Jigger。</summary>
        /// <precondition>两个杯非空且为不同实例，均没有父物品或手持归属。</precondition>
        /// <effects>永久收养两端并建立各自核心标识，初始 A 端朝上。</effects>
        public Jigger(string id, ItemDefinition definition, JiggerCup cupA, JiggerCup cupB) : base(id, definition)
        { CupA = cupA ?? throw new ArgumentNullException(nameof(cupA)); CupB = cupB ?? throw new ArgumentNullException(nameof(cupB)); if (ReferenceEquals(cupA, cupB)) throw new ArgumentException("Distinct cups required."); Adopt(cupA, "CupA"); Adopt(cupB, "CupB"); }
        /// <summary>切换量酒器朝上端。</summary>
        /// <precondition>两个杯均无液体、无冰且无挂起请求。</precondition>
        /// <effects>在 A 与 B 间切换 ActiveEnd；不丢弃内容，不转移液体。</effects>
        public void Flip()
        { if (CupA.Contents.LiquidMl > 0 || CupB.Contents.LiquidMl > 0 || CupA.Contents.IceCubes > 0 || CupB.Contents.IceCubes > 0 || CupA.HasPending || CupB.HasPending) throw new InvalidOperationException("Empty both cups first."); ActiveEnd = ActiveEnd == JiggerEnd.A ? JiggerEnd.B : JiggerEnd.A; }
    }
}
