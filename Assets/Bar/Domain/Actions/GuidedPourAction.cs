using System;
using Bar.Data;
namespace Bar.Domain
{
    /// <summary>碳酸瓶与吧勺协作；只允许台面高球杯。直倒仍先注册、默认优先。</summary>
    public sealed class GuidedPourAction:IBarAction
    {
        private readonly Controls controls;private readonly BottlePourAction direct;
        public string Id=>"pour.barspoon_guided";
        public string Label=>"沿吧勺引流";
        public InputStyle InputStyle=>InputStyle.HoldTilt;
        public GuidedPourAction(Controls controls){this.controls=controls;direct=new BottlePourAction(controls);}
        public bool CanExecute(Item actor,Item target,Hands hands)
        {
            if(actor is not Bottle bottle||(bottle.IngredientId!="soda"&&bottle.IngredientId!="tonic")||target is not Glass glass||glass.Definition.Id!="glass.highball"||glass.Location.Kind!=LocationKind.Surface)return false;
            if(!direct.CanExecute(actor,target,hands))return false;
            var side=hands.SideOf(actor).Value;
            return hands.Get(side==HandSide.Left?HandSide.Right:HandSide.Left) is BarSpoon;
        }
        public ActiveAction Begin(Item actor,Item target,Hands hands)
        {
            if(!CanExecute(actor,target,hands))throw new InvalidOperationException("Guided pour requires an open carbonated bottle, opposite spoon and reachable highball");
            controls.ValidateGuided();return new BottlePourSession(this,actor,(ILiquidReceiver)target,hands,new TiltFlowModel(controls),controls,true);
        }
    }
}
