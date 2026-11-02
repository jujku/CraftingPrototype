using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Bar.Data
{
    /// <summary>物品数据类别。</summary>
    public enum ItemKind { Container, Bottle, Composite, Attachment, Tool, Produce, Fixture }
    /// <summary>左手或右手。</summary>
    public enum HandSide { Left, Right }
    /// <summary>允许拿取物品的手的位标记。</summary>
    [Flags] public enum AllowedHands { None = 0, Left = 1, Right = 2, Both = 3 }
    /// <summary>用于容器数据的材质类别。</summary>
    public enum ContainerMaterial { Glass, StainlessSteel }
    /// <summary>保存以厘米计的物品参考宽、高、深。</summary>
    [DataContract] public sealed class ItemSize
    {
        /// <summary>参考宽度，单位厘米。</summary>
        [DataMember(Name = "widthCm")] public double WidthCm { get; private set; }
        /// <summary>参考高度，单位厘米。</summary>
        [DataMember(Name = "heightCm")] public double HeightCm { get; private set; }
        /// <summary>参考深度，单位厘米。</summary>
        [DataMember(Name = "depthCm")] public double DepthCm { get; private set; }
        /// <summary>创建ItemSize。</summary>
        /// <precondition>三个尺寸均为有限正数。</precondition>
        /// <effects>保存宽、高、深并立即校验。</effects>
        public ItemSize(double widthCm, double heightCm, double depthCm) { WidthCm = widthCm; HeightCm = heightCm; DepthCm = depthCm; Validate(); }
        internal void Validate() { DataGuard.Positive(WidthCm); DataGuard.Positive(HeightCm); DataGuard.Positive(DepthCm); }
    }
    /// <summary>定义偏好手、允许的手以及需要双手的动作标识。</summary>
    [DataContract] public sealed class HandRule
    {
        /// <summary>拿起时优先尝试的手。</summary>
        [DataMember(Name = "preferredHand")] public HandSide PreferredHand { get; private set; }
        /// <summary>可拿取该物品的手集合。</summary>
        [DataMember(Name = "allowedHands")] public AllowedHands AllowedHands { get; private set; }
        [DataMember(Name = "twoHandedActions")] private string[] twoHandedActions;
        /// <summary>需要另一只手空闲的动作标识列表。</summary>
        public IReadOnlyList<string> TwoHandedActions => Array.AsReadOnly(twoHandedActions);
        /// <summary>创建HandRule。</summary>
        /// <precondition>偏好手有效且属于允许集合；双手动作标识非空且不重复。</precondition>
        /// <effects>复制动作标识数组，保存偏好与允许手并校验。</effects>
        public HandRule(HandSide preferredHand, AllowedHands allowedHands, params string[] actions)
        { PreferredHand = preferredHand; AllowedHands = allowedHands; twoHandedActions = (string[])(actions ?? Array.Empty<string>()).Clone(); Validate(); }
        /// <summary>判断某只手是否允许持有该定义。</summary>
        /// <precondition>传入左手或右手标识。</precondition>
        /// <effects>只读取 AllowedHands 位标记，不改变双手或定义。</effects>
        public bool Allows(HandSide side) => (AllowedHands & (side == HandSide.Left ? AllowedHands.Left : AllowedHands.Right)) != 0;
        /// <summary>判断某动作是否需要另一只手空闲。</summary>
        /// <precondition>动作标识可与 TwoHandedActions 中的完整字符串比较。</precondition>
        /// <effects>返回列表包含关系，不锁定双手。</effects>
        public bool RequiresBoth(string actionId) => Array.IndexOf(twoHandedActions, actionId) >= 0;
        internal void Validate()
        {
            if (!Enum.IsDefined(typeof(HandSide), PreferredHand) || (int)AllowedHands < 0 || (int)AllowedHands > 3) throw new ArgumentException("Invalid hand rule.");
            if (AllowedHands != AllowedHands.None && !Allows(PreferredHand)) throw new ArgumentException("Preferred hand must be allowed.");
            if (twoHandedActions == null || twoHandedActions.Any(string.IsNullOrWhiteSpace) || twoHandedActions.Distinct().Count() != twoHandedActions.Length) throw new ArgumentException("Invalid two-hand action IDs.");
        }
    }
    /// <summary>保存容器质量、比热和换热系数，供唯一物理核心使用。</summary>
    [DataContract] public sealed class VesselThermal
    {
        /// <summary>容器质量，单位克。</summary>
        [DataMember(Name = "massGrams")] public double MassGrams { get; private set; }
        /// <summary>容器比热，单位 J/(g·°C)。</summary>
        [DataMember(Name = "specificHeat")] public double SpecificHeat { get; private set; }
        /// <summary>容器与液体的换热系数，单位 W/°C。</summary>
        [DataMember(Name = "liquidK")] public double LiquidK { get; private set; }
        /// <summary>容器与环境的换热系数，单位 W/°C。</summary>
        [DataMember(Name = "environmentK")] public double EnvironmentK { get; private set; }
        internal void Validate() { DataGuard.Positive(MassGrams); DataGuard.Positive(SpecificHeat); DataGuard.Nonnegative(LiquidK); DataGuard.Nonnegative(EnvironmentK); }
    }
    /// <summary>保存一类物品的不可变定义和资源标识。</summary>
    [DataContract] public sealed class ItemDefinition
    {
        /// <summary>稳定标识，用于定义或动作查询。</summary>
        [DataMember(Name = "id")] public string Id { get; private set; }
        /// <summary>物品的中文显示名。</summary>
        [DataMember(Name = "displayName")] public string DisplayName { get; private set; }
        /// <summary>当前定义、事件或逻辑位置的类别。</summary>
        [DataMember(Name = "kind")] public ItemKind Kind { get; private set; }
        /// <summary>工厂应创建的运行时类型名。</summary>
        [DataMember(Name = "type")] public string Type { get; private set; }
        /// <summary>拿取偏好与双手动作规则。</summary>
        [DataMember(Name = "handRule")] public HandRule HandRule { get; private set; }
        /// <summary>后续视图使用的预制体资源键。</summary>
        [DataMember(Name = "prefabKey")] public string PrefabKey { get; private set; }
        /// <summary>以厘米计的参考尺寸。</summary>
        [DataMember(Name = "size")] public ItemSize Size { get; private set; }
        /// <summary>液体容量上限，单位毫升。</summary>
        [DataMember(Name = "capacityMl")] public double CapacityMl { get; private set; }
        [DataMember(Name = "markingsMl")] private double[] markingsMl;
        /// <summary>只读刻度列表，单位毫升。</summary>
        public IReadOnlyList<double> MarkingsMl => Array.AsReadOnly(markingsMl);
        /// <summary>玻璃或不锈钢的材质标签。</summary>
        [DataMember(Name = "material")] public ContainerMaterial Material { get; private set; }
        /// <summary>传给核心的容器热学参数，不在领域层计算传热。</summary>
        [DataMember(Name = "thermal")] public VesselThermal Thermal { get; private set; }
        /// <summary>现有核心原料标识，不复制其成分或密度。</summary>
        [DataMember(Name = "ingredientId")] public string IngredientId { get; private set; }
        /// <summary>瓶子创建时的开口状态。</summary>
        [DataMember(Name = "initiallyOpen")] public bool InitiallyOpen { get; private set; }
        /// <summary>一铲冰的最少块数。</summary>
        [DataMember(Name = "minCubes")] public int MinCubes { get; private set; }
        /// <summary>一铲冰的最多块数。</summary>
        [DataMember(Name = "maxCubes")] public int MaxCubes { get; private set; }
        /// <summary>冰桶剩余库存，单位块。</summary>
        [DataMember(Name = "stockCubes")] public int StockCubes { get; private set; }
        /// <summary>半颗柠檬创建时的最少出汁量，单位毫升。</summary>
        [DataMember(Name = "yieldMinMl")] public double YieldMinMl { get; private set; }
        /// <summary>半颗柠檬创建时的最多出汁量，单位毫升。</summary>
        [DataMember(Name = "yieldMaxMl")] public double YieldMaxMl { get; private set; }
        [DataMember(Name = "parts")] private string[] parts;
        /// <summary>当前部件列表；在定义中保存永久部件的标识。</summary>
        public IReadOnlyList<string> Parts => Array.AsReadOnly(parts);
        [DataMember(Name = "fitsOn")] private string[] fitsOn;
        /// <summary>兼容的父物品定义标识列表。</summary>
        public IReadOnlyList<string> FitsOn => Array.AsReadOnly(fitsOn);
        [DataMember(Name = "anchors")] private string[] anchors;
        /// <summary>预制体必须提供的锚点名称。</summary>
        public IReadOnlyList<string> Anchors => Array.AsReadOnly(anchors);
        internal void Validate()
        {
            DataGuard.Text(Id); DataGuard.Text(DisplayName); DataGuard.Text(Type); DataGuard.Text(PrefabKey);
            if (HandRule == null || Size == null) throw new ArgumentException("Size and hand rule required.");
            HandRule.Validate(); Size.Validate();
            if (!Enum.IsDefined(typeof(ItemKind), Kind) || !Enum.IsDefined(typeof(ContainerMaterial), Material)) throw new ArgumentException("Invalid enum.");
            if ((Kind == ItemKind.Fixture) != (HandRule.AllowedHands == AllowedHands.None)) throw new ArgumentException("Only fixtures are immovable.");
            DataGuard.Nonnegative(CapacityMl);
            if (markingsMl == null || parts == null || fitsOn == null || anchors == null) throw new ArgumentException("All list fields required.");
            double previous = 0;
            foreach (double mark in markingsMl) { DataGuard.Positive(mark); if (mark <= previous || mark > CapacityMl) throw new ArgumentException("Invalid markings."); previous = mark; }
            if (Kind == ItemKind.Container) { DataGuard.Positive(CapacityMl); if (Thermal == null) throw new ArgumentException("Thermal data required."); Thermal.Validate(); }
            if (Kind == ItemKind.Bottle) { DataGuard.Positive(CapacityMl); DataGuard.Text(IngredientId); }
            if (Type == "JiggerCup" && markingsMl.Length == 0) throw new ArgumentException("Jigger markings required.");
            if (MinCubes < 0 || MaxCubes < MinCubes || StockCubes < 0 || Type == "IceScoop" && MinCubes == 0) throw new ArgumentException("Invalid cube counts.");
            DataGuard.Nonnegative(YieldMinMl); DataGuard.Nonnegative(YieldMaxMl);
            if (YieldMaxMl < YieldMinMl || Type == "LemonHalf" && YieldMaxMl == 0) throw new ArgumentException("Invalid juice range.");
            foreach (var list in new[] { parts, fitsOn, anchors }) if (list.Any(string.IsNullOrWhiteSpace) || list.Distinct().Count() != list.Length) throw new ArgumentException("Invalid ID/anchor list.");
        }
    }
    internal static class DataGuard
    {
        internal static void Text(string x) { if (string.IsNullOrWhiteSpace(x)) throw new ArgumentException("Nonempty text required."); }
        internal static void Nonnegative(double x) { if (double.IsNaN(x) || double.IsInfinity(x) || x < 0) throw new ArgumentOutOfRangeException(nameof(x)); }
        internal static void Positive(double x) { Nonnegative(x); if (x == 0) throw new ArgumentOutOfRangeException(nameof(x)); }
    }
}
