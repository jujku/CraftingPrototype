using System;
using Bar.Data;

namespace Bar.Domain
{
    /// <summary>维护左右手所有权、主手、换手和连续动作期间的操作锁。</summary>
    public sealed class Hands
    {
        private Item left, right;
        private object actionOwner;
        /// <summary>左手当前持物，空手时为空。</summary>
        public Item Left => left;
        /// <summary>右手当前持物，空手时为空。</summary>
        public Item Right => right;
        /// <summary>当前优先选择动作的主手。</summary>
        public HandSide MainHand { get; private set; } = HandSide.Right;
        /// <summary>惯用手：物品数据里的偏好手按右手习惯写，左手习惯时拿起镜像到另一只手。</summary>
        public HandSide Dominant { get; private set; } = HandSide.Right;
        /// <summary>连续动作是否尚未释放双手操作锁。</summary>
        public bool IsBusy => actionOwner != null;
        /// <summary>拿起、放下、换手或主手改变后发出的状态事件。</summary>
        public event Action<DomainEvent> Changed;
        /// <summary>读取指定手的持物。</summary>
        /// <precondition>side 为有效的 Left 或 Right。</precondition>
        /// <effects>返回该手物品或空值，不移动物品；非法枚举值抛异常。</effects>
        public Item Get(HandSide side) { CheckSide(side); return side == HandSide.Left ? left : right; }
        /// <summary>找到物品或永久部件所属持物根的手。</summary>
        /// <precondition>可传空值；对部件沿 Parent 向上查找。</precondition>
        /// <effects>返回持物根所在手或空值，不修改 Parent。</effects>
        public HandSide? SideOf(Item item)
        {
            if (item == null) return null;
            while (item.Parent != null) item = item.Parent;
            return ReferenceEquals(left, item) ? HandSide.Left : ReferenceEquals(right, item) ? HandSide.Right : (HandSide?)null;
        }
        /// <summary>检查表面物品是否能拿起。</summary>
        /// <precondition>物品可为空；正在连续动作时拒绝。</precondition>
        /// <effects>只检查固定设施、部件归属、逻辑表面、请求预留和可用手，不改变位置。</effects>
        public bool CanPickUp(Item item) => !IsBusy && item != null && !item.IsDiscarded && !(item is Glass glass && glass.IsServed) && !(item is Fixture) && item.Parent == null && item.Location.Kind == LocationKind.Surface && !ItemAccess.Pending(item) && PickupSide(item).HasValue;
        /// <summary>按偏好手和回退规则拿起物品。</summary>
        /// <precondition>CanPickUp 为 true；PreferredHand 优先，必要时选择另一允许的空手。</precondition>
        /// <effects>将物品放入所选手并改 Location，设为主手，触发 picked_up；失败返回 false 且不改状态。</effects>
        public bool TryPickUp(Item item)
        {
            if (!CanPickUp(item)) return false;
            var side = PickupSide(item).Value; Set(side, item); MainHand = side; Emit("picked_up", item, side); return true;
        }
        /// <summary>检查指定手是否能放下。</summary>
        /// <precondition>side 为有效手标识。</precondition>
        /// <effects>检查该手持物、动作锁及挂起请求，不做几何重叠计算。</effects>
        public bool CanPlace(HandSide side) => !IsBusy && Get(side) != null && !ItemAccess.Pending(Get(side));
        /// <summary>把指定手物品放到吧台逻辑表面。</summary>
        /// <precondition>锚点名非空；surface 非空；该手可放下且物品无挂起请求。</precondition>
        /// <effects>清空该手，更新物品表面位置，必要时切主手，触发 placed；几何放置由 Unity 层负责。</effects>
        public bool TryPlace(HandSide side, BarCounter surface, string anchor = "Surface")
        {
            Guard.Text(anchor); if (surface == null || !CanPlace(side)) return false;
            var item = Get(side); Clear(side); item.Location = new ItemLocation(LocationKind.Surface, surface.InstanceId, anchor);
            SelectOccupiedMain(); Emit("placed", item, side); return true;
        }
        /// <summary>将一个物品换到另一手或交换双手物品。</summary>
        /// <precondition>至少一只手有物品；双手未锁定且物品无挂起请求；两件物品均允许目标手。</precondition>
        /// <effects>交换持物与 Location；主手跟随原主物品，触发 swapped；条件失败保持原状态。</effects>
        public bool TrySwap()
        {
            if (IsBusy || left == null && right == null || ItemAccess.Pending(left) || ItemAccess.Pending(right)) return false;
            if (left != null && !left.Definition.HandRule.Allows(HandSide.Right) || right != null && !right.Definition.HandRule.Allows(HandSide.Left)) return false;
            var mainItem = Get(MainHand); var oldLeft = left; left = right; right = oldLeft;
            if (left != null) Set(HandSide.Left, left); if (right != null) Set(HandSide.Right, right);
            if (mainItem != null) MainHand = ReferenceEquals(left, mainItem) ? HandSide.Left : HandSide.Right;
            Emit("swapped", mainItem, MainHand); return true;
        }
        /// <summary>把指定持物手设为主手。</summary>
        /// <precondition>双手未锁定，指定手有物品。</precondition>
        /// <effects>更新 MainHand 并触发 main_hand_changed；条件失败返回 false。</effects>
        public bool SelectMain(HandSide side) { if (IsBusy || Get(side) == null) return false; MainHand = side; Emit("main_hand_changed", Get(side), side); return true; }
        /// <summary>尝试选择另一只手为主手。</summary>
        /// <precondition>另一手有物品且双手未锁定。</precondition>
        /// <effects>委托 SelectMain；成功改变主手并发事件，失败不变。</effects>
        public bool ToggleMain() => SelectMain(MainHand == HandSide.Left ? HandSide.Right : HandSide.Left);
        /// <summary>设置惯用手。</summary>
        /// <precondition>双手未锁定；手里有物品时它们必须能交换到另一只手。</precondition>
        /// <effects>改变以后拿起时的默认手；手里有物品时交换左右手持物（画面随之镜像，触发 swapped），空手时主手设为惯用手；触发 handedness_changed。失败不改状态。</effects>
        public bool SetDominant(HandSide side)
        {
            CheckSide(side); if (IsBusy) return false; if (side == Dominant) return true;
            if ((left != null || right != null) && !TrySwap()) return false;
            Dominant = side; if (left == null && right == null) MainHand = side;
            Emit("handedness_changed", Get(MainHand), side); return true;
        }
        /// <summary>检查施动者持物及双手动作约束。</summary>
        /// <precondition>施动者由当前 Hands 持有；双手未锁定。</precondition>
        /// <effects>当定义将 actionId 标为双手动作时还检查另一手为空；只查询，不锁手。</effects>
        public bool CanUse(Item actor, string actionId)
        { var side = SideOf(actor); return !IsBusy && side.HasValue && (!actor.Definition.HandRule.RequiresBoth(actionId) || Get(Other(side.Value)) == null); }
        internal bool Acquire(object owner) { if (IsBusy) return false; actionOwner = owner; return true; }
        internal void Release(object owner) { if (ReferenceEquals(actionOwner, owner)) actionOwner = null; }
        internal void ReleaseForAttachment(Attachment part)
        { var side = SideOf(part); if (!side.HasValue || !ReferenceEquals(Get(side.Value), part)) throw new InvalidOperationException("Part not independently held."); Clear(side.Value); part.Location = new ItemLocation(LocationKind.Surface); SelectOccupiedMain(); }
        internal void ReleaseItem(Item item)
        {var side=SideOf(item);if(!side.HasValue)throw new InvalidOperationException("Item is not held");Clear(side.Value);SelectOccupiedMain();Emit("released",item,side.Value);}
        private HandSide? PickupSide(Item item)
        { var preferred = Dominant == HandSide.Right ? item.Definition.HandRule.PreferredHand : Other(item.Definition.HandRule.PreferredHand); if (Get(preferred) == null && item.Definition.HandRule.Allows(preferred)) return preferred; var other = Other(preferred); return Get(other) == null && item.Definition.HandRule.Allows(other) ? other : (HandSide?)null; }
        private static HandSide Other(HandSide side) => side == HandSide.Left ? HandSide.Right : HandSide.Left;
        private void Set(HandSide side, Item item) { if (side == HandSide.Left) left = item; else right = item; item.Location = new ItemLocation(side == HandSide.Left ? LocationKind.LeftHand : LocationKind.RightHand, "hands", side.ToString()); }
        private void Clear(HandSide side) { if (side == HandSide.Left) left = null; else right = null; }
        private void SelectOccupiedMain() { if (Get(MainHand) == null && Get(Other(MainHand)) != null) MainHand = Other(MainHand); }
        private void Emit(string kind, Item item, HandSide side) => Changed?.Invoke(new DomainEvent(kind, item?.InstanceId, null, side.ToString()));
        private static void CheckSide(HandSide side) { if (!Enum.IsDefined(typeof(HandSide), side)) throw new ArgumentException("Unknown hand."); }
    }
    internal static class ItemAccess
    {
        internal static bool Pending(Item item)
        {
            if (item is Container c) return c.HasPending;
            if (item is Bottle b) return b.HasPending;
            if (item is CompositeItem composite) { foreach (var part in composite.Parts) if (Pending(part)) return true; }
            return false;
        }
        internal static Container TargetContainer(Item item) => (item as ILiquidReceiver)?.ReceivingContainer;
        internal static JiggerCup SourceCup(Item item) => item is Jigger j ? j.ActiveCup : item as JiggerCup;
    }
}
