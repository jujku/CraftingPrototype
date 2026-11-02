using System;
using System.Runtime.CompilerServices;
using Bar.Domain;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>V4 检视的只读能力与几何安全边界。只读库存/内腔，不调用核心操作。</summary>
    public static class InspectionPolicy
    {
        private static readonly ConditionalWeakTable<BarSession,TiltFlowModel> flows=new ConditionalWeakTable<BarSession,TiltFlowModel>();
        public static bool Supported(Item item)=>item is Jigger or BarSpoon or IceTongs or IceScoop or Squeezer or CobblerShaker or Bottle or Glass;
        public static bool CanInspect(Item item)=>Supported(item)&&!item.IsDiscarded&&
            (item is not Jigger j||Empty(j.CupA)&&Empty(j.CupB))&&
            (item is not IceTongs&&item is not IceScoop||((Tool)item).LoadedCubes==0);
        public static string Rejection(Item item)=>item is Jigger?"量酒器倒空后才能把玩":(item?.Definition.DisplayName??"工具")+"空着才能把玩";
        public static string ActionId(Item item)=>item switch
        {Jigger=>"jigger.inspect",BarSpoon=>"spoon.inspect",IceTongs=>"tongs.inspect",IceScoop=>"scoop.inspect",Squeezer=>"squeezer.inspect",CobblerShaker=>"shaker.inspect",Bottle=>"bottle.inspect",Glass=>"glass.inspect",_=>""};
        public static Container Container(Item item)=>item is CobblerShaker s?s.Body:item as Container;
        public static bool Empty(Container c)=>c==null||c.Contents.LiquidMl<=1e-8&&c.Contents.IceCubes<=0;
        public static bool Loaded(Item item)=>item is Bottle b?b.RemainingMl>1e-8:item is Squeezer s?s.Produce!=null:!Empty(Container(item));
        public static bool Carbonated(Item item)=>Container(item)?.Contents.HasBubbles==true;
        /// <summary>IsOpenTop是“可加料”接口；滤网装着但顶帽未盖时仍能出液，检视应按暴露液面保护。</summary>
        public static bool LiquidExposed(Item item)=>item is CobblerShaker sh?sh.State!=ShakerState.Sealed:Container(item)?.IsOpenTop==true;
        public static ContainerVisual Binding(ItemView view)=>ItemView.StirBinding(view,Container(view.Item));
        /// <summary>水平液面经过显示高度时，tan(倾角)=口沿净空/口沿半径；再保留20%。冰按全体积计入，偏保守。</summary>
        public static float MaximumTilt(ItemView view,BarSession session)
        {
            if(view.Item is Bottle b)return !b.IsOpen?80:Mathf.Max(0,(float)flows.GetValue(session,s=>new TiltFlowModel(s.GestureControls)).StartAngle(b.RemainingMl,b.CapacityMl,b.Definition.Id)-10);
            var c=Container(view.Item);if(Empty(c)||!LiquidExposed(view.Item))return 80;
            var v=Binding(view);if(!v||!v.Profile)return 0;
            var shape=v.Shape;double occupied=c.Contents.LiquidMl+c.Contents.IceVolumeMl;
            float level=v.Profile.HeightForMl(Math.Min(v.Profile.CapacityMl,occupied));
            return .8f*Mathf.Atan2(Mathf.Max(0,shape.Top-level),Mathf.Max(.0001f,shape.RadiusAt(shape.Top)))*Mathf.Rad2Deg;
        }
        /// <summary>把手锚点朝向限制在实际物品竖轴相对世界竖直的安全圆锥内。</summary>
        public static Quaternion LimitRotation(ItemView view,Bar.Data.HandSide side,Camera camera,Quaternion rotation,float maximum)
        {
            var grip=HoldViewRig.Grip(view,side);var frame=grip?grip:view.transform;
            var axis=Quaternion.Inverse(frame.rotation)*view.transform.up;var up=camera.transform.rotation*rotation*axis;
            float angle=Vector3.Angle(Vector3.up,up);if(angle<=maximum+.0001f)return rotation;
            var allowed=Vector3.Slerp(Vector3.up,up,maximum/Mathf.Max(.0001f,angle));
            var correction=Quaternion.Inverse(camera.transform.rotation)*Quaternion.FromToRotation(up,allowed)*camera.transform.rotation;
            return correction*rotation;
        }
        /// <summary>只给手持开口容器的原LiquidMotion留剩余口沿净空；满杯不凭空画出洒出。</summary>
        public static float? LiquidWaveLimit(BarSession session,ItemView view,ContainerVisual visual,float localHeight)
        {
            var c=Container(view.Item);if(c==null||!LiquidExposed(view.Item)||!session.Hands.SideOf(view.Item).HasValue)return null;
            var shape=visual.Shape;float total=Mathf.Atan2(Mathf.Max(0,shape.Top-localHeight),Mathf.Max(.0001f,shape.RadiusAt(shape.Top)))*Mathf.Rad2Deg;
            return Mathf.Max(0,total-Vector3.Angle(visual.transform.up,Vector3.up))*.8f;
        }
    }
}
