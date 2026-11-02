using System;
using Bar.Data;

namespace Bar.Domain
{
    /// <summary>表面、左手、右手或父物品插槽四种位置。</summary>
    public enum LocationKind { Surface, LeftHand, RightHand, Attached }
    /// <summary>用设施标识、父物品标识和锚点名记录逻辑位置。</summary>
    public sealed class ItemLocation
    {
        /// <summary>当前定义、事件或逻辑位置的类别。</summary>
        public LocationKind Kind { get; }
        /// <summary>所在表面设施或父复合物品的标识。</summary>
        public string OwnerId { get; }
        /// <summary>所在表面锚点或父物品插槽名。</summary>
        public string Anchor { get; }
        /// <summary>创建ItemLocation。</summary>
        /// <precondition>位置枚举有效，ownerId 与 anchor 非空。</precondition>
        /// <effects>创建只读逻辑地址，不移动任何现有物品。</effects>
        public ItemLocation(LocationKind kind, string ownerId = "bar", string anchor = "Surface")
        { if (!Enum.IsDefined(typeof(LocationKind), kind)) throw new ArgumentException("Invalid location."); Guard.Text(ownerId); Guard.Text(anchor); Kind = kind; OwnerId = ownerId; Anchor = anchor; }
    }
    /// <summary>保存实例身份、共享定义、逻辑位置和唯一父物品。</summary>
    public abstract class Item
    {
        /// <summary>由会话工厂保证唯一的实例标识。</summary>
        public string InstanceId { get; }
        /// <summary>废弃食材是否已从交互世界移除。</summary>
        public bool IsDiscarded { get; internal set; }
        /// <summary>共享的不可变物品定义。</summary>
        public ItemDefinition Definition { get; }
        /// <summary>当前逻辑位置，视图坐标由 Unity 层处理。</summary>
        public ItemLocation Location { get; internal set; } = new ItemLocation(LocationKind.Surface);
        /// <summary>唯一父复合物品；不为空时该部件不能独立拿起。</summary>
        public CompositeItem Parent { get; internal set; }
        /// <summary>创建Item。</summary>
        /// <precondition>id 非空，definition 非空且 Type 与实际运行时类名一致。</precondition>
        /// <effects>保存身份与定义，初始化为表面位置、无父物品。</effects>
        protected Item(string instanceId, ItemDefinition definition)
        {
            Guard.Text(instanceId); InstanceId = instanceId; Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (definition.Type != GetType().Name) throw new ArgumentException("Definition/runtime type mismatch.");
        }
    }
    internal static class Guard
    {
        internal static void Text(string x) { if (string.IsNullOrWhiteSpace(x)) throw new ArgumentException("Nonempty ID required."); }
        internal static void Finite(double x) { if (double.IsNaN(x) || double.IsInfinity(x)) throw new ArgumentOutOfRangeException(nameof(x)); }
        internal static void Nonnegative(double x) { Finite(x); if (x < 0) throw new ArgumentOutOfRangeException(nameof(x)); }
        internal static void Range(double x, double max) { Nonnegative(x); if (x > max) throw new ArgumentOutOfRangeException(nameof(x)); }
    }
}
