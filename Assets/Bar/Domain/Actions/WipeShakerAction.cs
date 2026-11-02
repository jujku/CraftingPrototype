namespace Bar.Domain
{
    /// <summary>
    /// 用空手把雪克壶外壁擦干。只是展示动作：不发送物理操作，不改变核心或库存；
    /// 表现层收到 action_performed 后把壶上的雾、霜和水擦掉，壶若仍冷，会按原规律重新起雾结霜。记录标准动作生命周期，以便回放时知道画面为何变化。
    /// </summary>
    public sealed class WipeShakerAction : CraftAction
    {
        public override string Id => "shaker.wipe";
        public override string Label => "擦干";
        public override InputStyle InputStyle => InputStyle.Menu;
        /// <summary>检查目标是雪克壶且有一只空手拿吧台巾。</summary>
        /// <precondition>手持的壶由壶自身施动，另一只手须空；台面上的壶无施动者，需要至少一只空手。</precondition>
        /// <effects>只查询条件；壶有挂起倒料、已丢弃或双手被连续动作锁定时拒绝。</effects>
        public override bool CanExecute(Item actor,Item target,Hands hands)
        {
            if(hands==null||hands.IsBusy||target is not CobblerShaker shaker||shaker.IsDiscarded||ItemAccess.Pending(shaker))return false;
            var side=hands.SideOf(shaker);
            if(side.HasValue)return ReferenceEquals(actor,shaker)&&hands.Get(side.Value==Bar.Data.HandSide.Left?Bar.Data.HandSide.Right:Bar.Data.HandSide.Left)==null;
            return actor==null&&shaker.Parent==null&&shaker.Location.Kind==LocationKind.Surface&&(hands.Left==null||hands.Right==null);
        }
        /// <summary>立即完成，只留下动作记录。</summary>
        /// <precondition>Begin 已复查 CanExecute。</precondition>
        /// <effects>记录 action_began / action_performed / action_ended；不改变任何领域或核心状态。</effects>
        protected override ActiveAction Create(Item actor,Item target,Hands hands)=>new ImmediateAction(this,actor,target,hands,()=>{});
    }
}
