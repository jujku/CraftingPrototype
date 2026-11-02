namespace Bar.Domain
{
    /// <summary>直接从瓶子取下瓶盖，无需开瓶器；瓶盖作为独立部件进入一只空手，可以放下再盖回。</summary>
    public sealed class OpenBottleAction : CraftAction
    {
        public override string Id => "bottle.open";
        public override string Label => "取下瓶盖";
        public override InputStyle InputStyle => InputStyle.Menu;
        /// <summary>检查封口、挂起请求、持物归属及接住瓶盖的空手。</summary>
        /// <precondition>允许空施动者；目标必须是未丢弃且未打开的瓶子。</precondition>
        /// <effects>手持瓶子由瓶子施动，另一只手须能接住瓶盖；台面瓶子需要一只能接住瓶盖的空手。不扣库存，不改变核心。</effects>
        public override bool CanExecute(Item actor,Item target,Hands hands)
        {
            if(hands==null||hands.IsBusy||target is not Bottle bottle||bottle.IsOpen||bottle.HasPending||bottle.IsDiscarded)return false;
            var cap=bottle.CapSlot.Content;
            if(cap!=null?!PartTransfer.HasHandFor(cap,hands):hands.Left!=null&&hands.Right!=null&&!hands.SideOf(bottle).HasValue)return false;
            if(hands.SideOf(bottle).HasValue)return ReferenceEquals(actor,bottle)&&Usable(bottle,hands,Id);
            return actor==null&&bottle.Parent==null&&bottle.Location.Kind==LocationKind.Surface;
        }
        /// <summary>取下瓶盖并交到空手；没有瓶盖部件的旧定义只改开口记录。</summary>
        /// <precondition>Begin 已复查 CanExecute。</precondition>
        /// <effects>拆下瓶盖、放入空手并发 picked_up，IsOpen 随插槽变为 true；记录标准动作生命周期，不发送物理操作，不消耗瓶内原料。</effects>
        protected override ActiveAction Create(Item actor,Item target,Hands hands)
            =>new ImmediateAction(this,actor,target,hands,()=>
            {
                var bottle=(Bottle)target;
                if(bottle.CapSlot.Content is BottleCap cap)PartTransfer.Detach(bottle,cap,hands);else bottle.SetOpen(true);
            });
    }
    /// <summary>把单独拿在手里的瓶盖盖回它适配的瓶子；瓶子可以在台面上，也可以在另一只手里。</summary>
    public sealed class CloseBottleAction : CraftAction
    {
        public override string Id => "bottle.close";
        public override string Label => "盖上瓶盖";
        public override InputStyle InputStyle => InputStyle.Menu;
        /// <summary>检查瓶盖持有方式、瓶子位置和瓶盖插槽。</summary>
        /// <precondition>施动者是单独握着的瓶盖；目标是在台面或另一只手里的瓶子。</precondition>
        /// <effects>只返回候选可用性；瓶子有挂起倒料、已丢弃或已盖上时拒绝。</effects>
        public override bool CanExecute(Item actor,Item target,Hands hands)
        {
            if(hands==null||hands.IsBusy||actor is not BottleCap cap||!PartTransfer.HeldAlone(cap,hands)||target is not Bottle bottle||!bottle.CanAttach(cap))return false;
            return hands.SideOf(bottle).HasValue||bottle.Location.Kind==LocationKind.Surface;
        }
        /// <summary>从手中释放瓶盖并装到瓶口。</summary>
        /// <precondition>Begin 已复查 CanExecute。</precondition>
        /// <effects>空出握瓶盖的手，瓶盖改为 Attached，IsOpen 变为 false，此后不能倒酒；记录标准动作生命周期。</effects>
        protected override ActiveAction Create(Item actor,Item target,Hands hands)
            =>new ImmediateAction(this,actor,target,hands,()=>PartTransfer.Attach((Bottle)target,(BottleCap)actor,hands));
    }
}
