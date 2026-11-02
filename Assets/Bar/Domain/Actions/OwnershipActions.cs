using System;
using Bar.Data;

namespace Bar.Domain
{
    /// <summary>从逻辑表面拿起符合双手规则的物品。</summary>
    public sealed class PickUpAction : IBarAction
    {
        /// <summary>稳定标识，用于定义或动作查询。</summary>
        public string Id => "item.pick_up";
        /// <summary>交互菜单中的中文动作名称。</summary>
        public string Label => "拿起";
        /// <summary>动作需要的鼠标或菜单输入方式。</summary>
        public InputStyle InputStyle => InputStyle.Click;
        /// <summary>判断当前施动者和目标是否满足该动作规则。</summary>
        /// <precondition>actor 为空；target 是可拿起的表面根物品；有允许的空手。</precondition>
        /// <effects>仅返回是否可执行，不改变手或物品。</effects>
        public bool CanExecute(Item actor, Item target, Hands hands) => actor == null && hands != null && hands.CanPickUp(target);
        /// <summary>重新验证候选并开始该动作。</summary>
        /// <precondition>actor 为空；target 是可拿起的表面根物品；有允许的空手。</precondition>
        /// <effects>Hands.TryPickUp 改变持物并发 picked_up；激活记录开始、执行、结束事件。</effects>
        public ActiveAction Begin(Item actor, Item target, Hands hands)
        { if (!CanExecute(actor, target, hands)) throw new InvalidOperationException("Cannot pick up."); return new ImmediateAction(this, actor, target, hands, () => hands.TryPickUp(target)); }
    }
    /// <summary>把指定手中的根物品放到吧台逻辑表面。</summary>
    public sealed class PlaceAction : IBarAction
    {
        /// <summary>稳定标识，用于定义或动作查询。</summary>
        public string Id => "item.place";
        /// <summary>交互菜单中的中文动作名称。</summary>
        public string Label => "放下";
        /// <summary>动作需要的鼠标或菜单输入方式。</summary>
        public InputStyle InputStyle => InputStyle.Menu;
        /// <summary>判断当前施动者和目标是否满足该动作规则。</summary>
        /// <precondition>actor 是当前手持根物品，target 是 BarCounter；无动作锁和挂起请求。</precondition>
        /// <effects>仅返回是否可执行，不改变手或物品。</effects>
        public bool CanExecute(Item actor, Item target, Hands hands)
        { var side = hands?.SideOf(actor); return side.HasValue && ReferenceEquals(hands.Get(side.Value), actor) && target is BarCounter && hands.CanPlace(side.Value); }
        /// <summary>重新验证候选并开始该动作。</summary>
        /// <precondition>actor 是当前手持根物品，target 是 BarCounter；无动作锁和挂起请求。</precondition>
        /// <effects>Hands.TryPlace 清空该手并更新表面归属，发布 placed 和动作生命周期事件。</effects>
        public ActiveAction Begin(Item actor, Item target, Hands hands)
        { if (!CanExecute(actor, target, hands)) throw new InvalidOperationException("Cannot place."); return new ImmediateAction(this, actor, target, hands, () => hands.TryPlace(hands.SideOf(actor).Value, (BarCounter)target)); }
    }
    /// <summary>部件在手与宿主之间转移的共用规则：取下的部件进入规则允许的空手，装上前先从手中释放。</summary>
    internal static class PartTransfer
    {
        /// <summary>是否有一只规则允许的空手能接住取下的部件。</summary>
        internal static bool HasHandFor(Item part, Hands hands)
        {
            foreach (HandSide side in new[] { HandSide.Left, HandSide.Right }) if (hands.Get(side) == null && part.Definition.HandRule.Allows(side)) return true;
            return false;
        }
        /// <summary>拆下部件并放入空手；拿不到手时抛异常。</summary>
        internal static void Detach(IAttachmentHost host, Attachment part, Hands hands)
        { host.Detach(part); if (!hands.TryPickUp(part)) throw new InvalidOperationException("Pickup precondition changed."); }
        /// <summary>从手中释放独立持有的部件并装入宿主。</summary>
        internal static void Attach(IAttachmentHost host, Attachment part, Hands hands)
        { hands.ReleaseForAttachment(part); host.Attach(part); }
        /// <summary>部件是否单独握在某只手里（而不是随宿主一起被拿着）。</summary>
        internal static bool HeldAlone(Attachment part, Hands hands)
        { var side = hands.SideOf(part); return side.HasValue && part.Parent == null && ReferenceEquals(hands.Get(side.Value), part); }
    }
    /// <summary>统一执行隔冰器和上盖的装卸顺序与部件所有权转移。</summary>
    public abstract class AssemblyAction : IBarAction
    {
        private readonly bool attach, cap;
        /// <summary>稳定标识，用于定义或动作查询。</summary>
        public string Id => (attach ? "shaker.attach_" : "shaker.detach_") + (cap ? "cap" : "strainer");
        /// <summary>交互菜单中的中文动作名称。</summary>
        public string Label => (attach ? "装上" : "取下") + (cap ? "上盖" : "隔冰器");
        /// <summary>动作需要的鼠标或菜单输入方式。</summary>
        public InputStyle InputStyle => InputStyle.Menu;
        /// <summary>创建AssemblyAction。</summary>
        /// <precondition>attach 与 cap 布尔参数指定唯一的装卸规则。</precondition>
        /// <effects>保存规则类型，派生稳定动作标识与中文标签，不改变壶或双手。</effects>
        protected AssemblyAction(bool attach, bool cap) { this.attach = attach; this.cap = cap; }
        private CobblerShaker Shaker(Item target) => target as CobblerShaker ?? target?.Parent as CobblerShaker;
        private Attachment Part(CobblerShaker shaker) => cap ? (Attachment)shaker.CapSlot.Content : shaker.StrainerSlot.Content;
        /// <summary>判断当前施动者和目标是否满足该动作规则。</summary>
        /// <precondition>安装时持有兼容部件且槽位顺序合法；拆卸时有允许的空手且上盖顺序允许；壶身无挂起请求。</precondition>
        /// <effects>仅返回是否可执行，不改变手或物品。</effects>
        public bool CanExecute(Item actor, Item target, Hands hands)
        {
            var shaker = Shaker(target); if (hands == null || hands.IsBusy || shaker == null || ItemAccess.Pending(shaker)) return false;
            if (attach) return actor is Attachment part && (cap ? part is ShakerCap : part is ShakerStrainer) && hands.SideOf(part).HasValue && part.Parent == null && shaker.CanAttach(part);
            if (hands.SideOf(shaker).HasValue ? !ReferenceEquals(actor, shaker) : actor != null) return false;
            var removable = Part(shaker); return shaker.CanDetach(removable) && PartTransfer.HasHandFor(removable, hands);
        }
        /// <summary>重新验证候选并开始该动作。</summary>
        /// <precondition>安装时持有兼容部件且槽位顺序合法；拆卸时有允许的空手且上盖顺序允许；壶身无挂起请求。</precondition>
        /// <effects>安装时从手中释放部件再写入插槽；拆卸时清槽并拿到空手；发布动作生命周期事件。</effects>
        public ActiveAction Begin(Item actor, Item target, Hands hands)
        {
            if (!CanExecute(actor, target, hands)) throw new InvalidOperationException("Cannot change assembly.");
            return new ImmediateAction(this, actor, target, hands, () =>
            {
                var shaker = Shaker(target);
                if (attach) PartTransfer.Attach(shaker, (Attachment)actor, hands);
                else PartTransfer.Detach(shaker, Part(shaker), hands);
            });
        }
    }
    /// <summary>将手持隔冰器装到开放雪克壶。</summary>
    public sealed class AttachStrainerAction : AssemblyAction
    {
        /// <summary>创建AttachStrainerAction。</summary>
        /// <precondition>无运行时物品或双手前置条件；本构造只创建规则。</precondition>
        /// <effects>固定该动作的装卸类型和标识；不立即改变物品。</effects>
        public AttachStrainerAction() : base(true, false) { }
    }
    /// <summary>在隔冰器已安装时装上上盖。</summary>
    public sealed class AttachCapAction : AssemblyAction
    {
        /// <summary>创建AttachCapAction。</summary>
        /// <precondition>无运行时物品或双手前置条件；本构造只创建规则。</precondition>
        /// <effects>固定该动作的装卸类型和标识；不立即改变物品。</effects>
        public AttachCapAction() : base(true, true) { }
    }
    /// <summary>在上盖已取下且有空手时取下隔冰器。</summary>
    public sealed class DetachStrainerAction : AssemblyAction
    {
        /// <summary>创建DetachStrainerAction。</summary>
        /// <precondition>无运行时物品或双手前置条件；本构造只创建规则。</precondition>
        /// <effects>固定该动作的装卸类型和标识；不立即改变物品。</effects>
        public DetachStrainerAction() : base(false, false) { }
    }
    /// <summary>将上盖从雪克壶取下并放入允许的空手。</summary>
    public sealed class DetachCapAction : AssemblyAction
    {
        /// <summary>创建DetachCapAction。</summary>
        /// <precondition>无运行时物品或双手前置条件；本构造只创建规则。</precondition>
        /// <effects>固定该动作的装卸类型和标识；不立即改变物品。</effects>
        public DetachCapAction() : base(false, true) { }
    }
}
