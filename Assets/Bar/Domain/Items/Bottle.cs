using System;
using Bar.Data;

namespace Bar.Domain
{
    /// <summary>记录原料标识、批次温度、瓶盖插槽和已确认的瓶内库存。</summary>
    public sealed class Bottle : CompositeItem, ILiquidSource, IAttachmentHost
    {
        private double reserved;
        private bool open;
        public LiquidControl LiquidControl => LiquidControl.BottleTilt;
        public double LiquidAvailableMl => IsOpen && !HasPending && !IsDiscarded ? AvailableMl : 0;
        public double LiquidCapacityMl => CapacityMl;
        public double? FixedFlowMlPerSecond => Spout?.FlowMlPerSecond;
        public Container SourceVessel => null;
        public OperationRequest CreateLiquidRequest(Container receiver, double intendedMl,
            double angle, double heldSeconds, double seconds, double pressure)
        {
            return new OperationRequest(this, null, receiver, intendedMl, angle, heldSeconds, seconds);
        }
        /// <summary>现有核心原料标识，不复制其成分或密度。</summary>
        public string IngredientId => Definition.IngredientId;
        /// <summary>液体容量上限，单位毫升。</summary>
        public double CapacityMl => Definition.CapacityMl;
        /// <summary>已经确认消耗后瓶中剩余体积，单位毫升。</summary>
        public double RemainingMl { get; private set; }
        /// <summary>扣除已预留输入后尚可接收的体积，单位毫升。</summary>
        public double AvailableMl => Math.Max(0, RemainingMl - reserved);
        /// <summary>本瓶自带的瓶盖；定义没有瓶盖部件时为空。</summary>
        public BottleCap Cap { get; }
        /// <summary>瓶盖插槽，瓶盖装上时非空。</summary>
        public Slot<BottleCap> CapSlot { get; } = new Slot<BottleCap>("Slot_BottleCap");
        /// <summary>瓶口是否打开：有瓶盖时由插槽推导，不能单独赋值；无瓶盖的定义沿用开口记录。</summary>
        public bool IsOpen => Cap != null ? CapSlot.IsEmpty : open;
        /// <summary>预留的酒嘴部件，没有安装时为空。</summary>
        public PourSpout Spout { get; private set; }
        /// <summary>由核心原料或批次条件注入的供料温度，单位摄氏度。</summary>
        public double ServeTemperatureC { get; }
        /// <summary>创建Bottle。</summary>
        /// <precondition>批次温度有限；id 非空；definition.Type=Bottle；cap 可为空，非空时须无父物品、位于表面且适配本瓶。</precondition>
        /// <effects>以定义容量创建满瓶库存并保存批次温度；有瓶盖且定义未标初始打开时装上瓶盖。</effects>
        public Bottle(string id, ItemDefinition definition, double serveTemperatureC, BottleCap cap = null) : base(id, definition)
        {
            Guard.Finite(serveTemperatureC); ServeTemperatureC = serveTemperatureC; RemainingMl = CapacityMl; open = definition.InitiallyOpen;
            Cap = cap; if (cap != null && !definition.InitiallyOpen) Attach(cap);
        }
        /// <summary>直接设置酒瓶开口状态，供数据初始化和测试使用；玩家通过取下、盖上瓶盖动作改变开口。</summary>
        /// <precondition>酒瓶没有等待确认的倒料预留；有瓶盖时，关闭需要自带瓶盖位于逻辑表面且不在手中。</precondition>
        /// <effects>无瓶盖时更新开口记录；有瓶盖时把瓶盖拆到逻辑表面或装回；有预留时抛异常，不释放或消耗预留。</effects>
        public void SetOpen(bool open)
        {
            if (reserved > 0) throw new InvalidOperationException("Pending pour.");
            if (Cap == null) { this.open = open; return; }
            if (open == IsOpen) return;
            if (open) Detach(Cap); else Attach(Cap);
        }
        /// <summary>检查瓶盖能否盖上。</summary>
        /// <precondition>part 可为空；瓶子没有挂起倒料且未丢弃。</precondition>
        /// <effects>检查空插槽、瓶盖类型、无父物品和 FitsOn；不释放手持瓶盖。</effects>
        public bool CanAttach(Attachment part) => !HasPending && !IsDiscarded && part is BottleCap && part.Parent == null && part.Fits(this) && CapSlot.IsEmpty;
        /// <summary>把逻辑表面上的瓶盖装到瓶口。</summary>
        /// <precondition>CanAttach 为 true，瓶盖位于逻辑表面。</precondition>
        /// <effects>设置 Parent 和 Attached 位置，写入瓶盖插槽，IsOpen 随之变为 false。</effects>
        public void Attach(Attachment part)
        {
            if (!CanAttach(part) || part.Location.Kind != LocationKind.Surface) throw new InvalidOperationException("Cannot cap bottle.");
            Adopt(part, CapSlot.Name); CapSlot.Content = (BottleCap)part;
        }
        /// <summary>检查瓶盖能否取下。</summary>
        /// <precondition>part 可为空；瓶子没有挂起倒料。</precondition>
        /// <effects>只检查 part 是否为当前瓶盖，不改变归属。</effects>
        public bool CanDetach(Attachment part) => !HasPending && part != null && ReferenceEquals(part, CapSlot.Content);
        /// <summary>把瓶盖拆到逻辑表面。</summary>
        /// <precondition>CanDetach 为 true。</precondition>
        /// <effects>清空瓶盖插槽，Parent=null，位置改为 Surface，IsOpen 变为 true；取下动作随后把瓶盖放入空手。</effects>
        public void Detach(Attachment part)
        {
            if (!CanDetach(part)) throw new InvalidOperationException("Cannot uncap bottle.");
            CapSlot.Content = null; Release(part);
        }
        internal void Reserve(double ml) { Guard.Range(ml, AvailableMl); reserved += ml; }
        internal bool HasPending => reserved > 0;
        internal void Release(double ml) { reserved = Math.Max(0, reserved - ml); }
        internal void Consume(double ml) { Guard.Range(ml, RemainingMl); RemainingMl -= ml; }
    }
    /// <summary>通过数据中的父物品标识判断部件适配关系。</summary>
    public abstract class Attachment : Item
    {
        /// <summary>兼容的父物品定义标识列表。</summary>
        public System.Collections.Generic.IReadOnlyList<string> FitsOn => Definition.FitsOn;
        /// <summary>创建Attachment。</summary>
        /// <precondition>id 非空，definition.Type 必须为 Attachment。</precondition>
        /// <effects>建立 Attachment 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        protected Attachment(string id, ItemDefinition definition) : base(id, definition) { }
        /// <summary>检查父物品是否与部件兼容。</summary>
        /// <precondition>parent 可为空，非空时按定义 Id 比较。</precondition>
        /// <effects>返回 FitsOn 包含关系，不装卸部件。</effects>
        public bool Fits(Item parent) { if (parent == null) return false; foreach (string id in FitsOn) if (id == parent.Definition.Id) return true; return false; }
    }
    /// <summary>表示雪克壶第二段隔冰器，装上后阻止冰块随液体倒出。</summary>
    public sealed class ShakerStrainer : Attachment
    {
        /// <summary>创建ShakerStrainer。</summary>
        /// <precondition>id 非空，definition.Type 必须为 ShakerStrainer。</precondition>
        /// <effects>建立 ShakerStrainer 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        public ShakerStrainer(string id, ItemDefinition definition) : base(id, definition) { }
    }
    /// <summary>表示装在隔冰器上的雪克壶第三段上盖。</summary>
    public sealed class ShakerCap : Attachment
    {
        /// <summary>创建ShakerCap。</summary>
        /// <precondition>id 非空，definition.Type 必须为 ShakerCap。</precondition>
        /// <effects>建立 ShakerCap 实例，保存定义，初始化表面位置且 Parent 为空。</effects>
        public ShakerCap(string id, ItemDefinition definition) : base(id, definition) { }
    }
    /// <summary>可从酒瓶取下、拿在手里、放到台面并盖回同一种酒瓶的瓶盖。</summary>
    public sealed class BottleCap : Attachment
    {
        /// <summary>创建BottleCap。</summary>
        /// <precondition>id 非空，definition.Type 必须为 BottleCap，FitsOn 列出所属酒瓶定义。</precondition>
        /// <effects>建立 BottleCap 实例，初始化表面位置且 Parent 为空；由酒瓶构造时装上。</effects>
        public BottleCap(string id, ItemDefinition definition) : base(id, definition) { }
    }
    /// <summary>预留固定流量酒嘴的数据形状，本轮没有安装动作。</summary>
    public sealed class PourSpout : Attachment
    {
        /// <summary>酒嘴数据提供的固定流量，单位 ml/s。</summary>
        public double FlowMlPerSecond { get; }
        /// <summary>创建PourSpout。</summary>
        /// <precondition>固定流量有限且非负；定义类型为 PourSpout。</precondition>
        /// <effects>保存流量和附件身份，不安装到瓶子。</effects>
        public PourSpout(string id, ItemDefinition definition, double flowMlPerSecond) : base(id, definition) { Guard.Nonnegative(flowMlPerSecond); FlowMlPerSecond = flowMlPerSecond; }
    }
}
