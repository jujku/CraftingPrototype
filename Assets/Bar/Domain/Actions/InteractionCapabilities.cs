using System;
namespace Bar.Domain
{
    /// <summary>把接液能力与具体物品类型分离；容器自行管理顶部和余量。</summary>
    public interface ILiquidReceiver
    {
        Container ReceivingContainer { get; }
        double ReceivableMl { get; }
    }
    /// <summary>液体来源的控制方式，不参与物理运算。</summary>
    public enum LiquidControl { BottleTilt, Measured, VesselTilt, Pressure }
    /// <summary>来源负责库存与核心请求；统一转移入口负责限幅。</summary>
    public interface ILiquidSource
    {
        LiquidControl LiquidControl { get; }
        double LiquidAvailableMl { get; }
        double LiquidCapacityMl { get; }
        double? FixedFlowMlPerSecond { get; }
        Container SourceVessel { get; }
        OperationRequest CreateLiquidRequest(Container receiver, double intendedMl,
            double angle, double heldSeconds, double seconds, double pressure);
    }
    /// <summary>可装入工具的实物原料，支持以后新增水果。</summary>
    public interface ILoadableProduce
    {
        Item ProduceItem { get; }
        bool IsSpent { get; }
        string IngredientId { get; }
        double RemainingMl { get; }
        void Consume(double actualMl);
    }
    /// <summary>接收原料的能力；兼容性及所有权转移由物品负责。</summary>
    public interface IProduceHolder
    {
        ILoadableProduce Produce { get; }
        bool CanLoad(ILoadableProduce produce);
        void Load(ILoadableProduce produce, Hands hands);
        void UnloadSpent();
    }
    /// <summary>所有动作共用的可接近性及接收容量入口。</summary>
    public static class InteractionAccess
    {
        public static bool Reachable(Item item, Hands hands)
        {
            if (item == null || hands == null || item.IsDiscarded) return false;
            while (item.Parent != null) item = item.Parent;
            return item.Location.Kind == LocationKind.Surface || hands.SideOf(item).HasValue;
        }
        public static double ReceiveSpace(Item target, Hands hands)
        {
            if (!Reachable(target, hands)) return 0;
            return (target as ILiquidReceiver)?.ReceivableMl ?? 0;
        }
    }
    /// <summary>连续倒入与压汁共享的限幅及请求生成通道。</summary>
    public static class LiquidTransfer
    {
        public static bool CanTransfer(ILiquidSource source, Item target, Hands hands, double minimumMl = 0)
        {
            var receiver = target as ILiquidReceiver;
            return source != null && source.LiquidAvailableMl > 1e-8 && receiver != null &&
                !ReferenceEquals(source.SourceVessel, receiver.ReceivingContainer) &&
                InteractionAccess.ReceiveSpace(target, hands) > 1e-8 &&
                receiver.ReceivableMl + 1e-8 >= minimumMl;
        }
        public static OperationRequest Request(ILiquidSource source, ILiquidReceiver receiver,
            double intendedMl, double angle, double heldSeconds, double seconds, double pressure = 0)
        {
            Guard.Nonnegative(intendedMl);
            double available = Math.Min(source.LiquidAvailableMl, receiver.ReceivableMl);
            if (available <= 1e-8 || intendedMl <= 1e-9) return null;
            return source.CreateLiquidRequest(receiver.ReceivingContainer, intendedMl,
                angle, heldSeconds, seconds, pressure);
        }
    }
    /// <summary>将两个能力角色的任一施动方向归一化，保留实际菜单方向。</summary>
    public abstract class PairedCraftAction<TFirst, TSecond> : CraftAction
        where TFirst : class where TSecond : class
    {
        protected bool Pair(Item actor, Item target, Hands hands, out TFirst first, out TSecond second)
        {
            first = actor as TFirst;
            second = target as TSecond;
            if (first == null || second == null)
            {
                first = target as TFirst;
                second = actor as TSecond;
            }
            return !ReferenceEquals(actor, target) && first != null && second != null &&
                Usable(actor, hands, Id) && InteractionAccess.Reachable(target, hands);
        }
    }
    /// <summary>仅空量酒器允许翻转，保留两端独立库存。</summary>
    public sealed class FlipJiggerAction : CraftAction
    {
        public override string Id => "jigger.flip";
        public override string Label => "翻转量酒器";
        public override bool CanExecute(Item actor, Item target, Hands hands)
        {
            return ReferenceEquals(actor, target) && Usable(actor, hands, Id) && actor is Jigger jigger &&
                jigger.CupA.Contents.LiquidMl == 0 && jigger.CupB.Contents.LiquidMl == 0 &&
                jigger.CupA.Contents.IceCubes == 0 && jigger.CupB.Contents.IceCubes == 0;
        }
        protected override ActiveAction Create(Item actor, Item target, Hands hands)
        {
            return new ImmediateAction(this, actor, target, hands, () => ((Jigger)actor).Flip());
        }
    }
}
