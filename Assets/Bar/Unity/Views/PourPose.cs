using System.Linq;
using Bar.Data;
using Bar.Domain;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>所有倒液动作共用的站位：固定时间靠近、世界向上间距、绕出口旋转、按弹道水平瞄准。</summary>
    public sealed class PourPose
    {
        private ItemView source,receiver;private Transform camera;private PourSettings settings;
        private Vector3 sourceStart,receiverStart;private Quaternion sourceRotation,receiverRotation,receiverUpright;
        private float elapsed,shownAngle;private bool receiverHeld;private Vector3 aimedLip;
        private TiltFlowModel flowModel;
        public float Progress=>Mathf.Clamp01(elapsed/settings.alignSeconds);
        public static ContainerVisual Binding(ItemView view,Container container=null)=>view.GetComponentsInChildren<ContainerVisual>().FirstOrDefault(v=>container==null||v.DefinitionId==container.Definition.Id);
        public void Begin(ItemView actor,ItemView target,Transform viewCamera,bool targetHeld)
        {
            source=actor;receiver=target;camera=viewCamera;receiverHeld=targetHeld;settings=PourSettings.Load();elapsed=shownAngle=0;
            sourceStart=source.transform.position;receiverStart=receiver.transform.position;
            sourceRotation=source.transform.rotation;receiverRotation=receiver.transform.rotation;aimedLip=source.PourPoint;
            // a held receiver is brought upright (keeping its facing) as it moves in front of the camera, so the player looks down into it.
            receiverUpright=Quaternion.FromToRotation(Axis(receiver),Vector3.up)*receiverRotation;
            // The scene owns controls; resolve them once, only to read its unchanged start-angle rule.
            var session=Object.FindFirstObjectByType<BarSession>();
            if(session)flowModel=new TiltFlowModel(ItemCatalog.LoadControls(session.ControlsJson.text));
        }
        public static Vector3 Axis(ItemView view)
        {
            if(view.Item is Squeezer)return -view.transform.up;
            Container c=view.Item is Jigger j?j.ActiveCup:view.Item is CobblerShaker s?s.Body:view.Item as Container;
            var binding=Binding(view,c);return binding?binding.transform.up:view.transform.up;
        }
        public static float EntrySideWeight(float shownAngle,float entryAngle)=>1-Mathf.SmoothStep(0,1,Mathf.Clamp01(shownAngle/Mathf.Max(1,entryAngle)));
        public static float EntrySideDistance(ItemView source,float mouthRadius)=>mouthRadius+Mathf.Max(source.Size.x,source.Size.z)*.5f+PourSettings.Load().entrySideGap;
        public static float OutletRadius(ItemView view)
        {
            var bottle=view.GetComponent<BottleLiquidView>();if(bottle&&bottle.Geometry!=null)return bottle.Shape.RadiusAt(bottle.Shape.Top);
            Container c=view.Item is Jigger j?j.ActiveCup:view.Item is CobblerShaker s?s.Body:view.Item as Container;
            var binding=Binding(view,c);var p=PourSettings.Load();float radius=binding?binding.Shape.RadiusAt(binding.Shape.Top):p.outletFallbackRadius;
            // The cavity mouth is not a full pipe: only a narrow lip sector / strainer outlet
            // feeds the jet. Using the whole shaker diameter made v=Q/A almost zero and
            // inflated the first stream ring into a wide sheet. Author PourLip stays untouched.
            if(view.Item is CobblerShaker shaker)radius=Mathf.Min(radius,shaker.StrainerSlot.IsEmpty?p.shakerOpenStreamRadius:p.shakerStrainerStreamRadius);
            if(view.Item is Jigger)radius=Mathf.Min(radius,p.jiggerStreamRadius);
            return radius;
        }
        public static void Mouth(ItemView view,Container c,out Vector3 center,out float radius,out float floor)
        {
            if(view.Item is Sink)
            {
                var basin=view.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r=>r.name=="BasinShell");
                var drain=view.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r=>r.name=="Drain");
                if(basin)
                {
                    var bounds=basin.bounds;center=new Vector3(bounds.center.x,bounds.max.y,bounds.center.z);
                    radius=Mathf.Min(bounds.extents.x,bounds.extents.z)-PourSettings.Load().bottleWallMetres;
                    floor=drain?drain.bounds.max.y:bounds.min.y;return;
                }
            }
            var binding=Binding(view,c);
            if(binding)
            {
                var shape=binding.Shape;center=binding.transform.TransformPoint(new Vector3(shape.Axis.x,shape.Top,shape.Axis.y));radius=shape.RadiusAt(shape.Top);
                double ml=c==null?0:c.Contents.LiquidMl;float h=binding.Profile?binding.HeightForLiquid(ml):shape.Bottom;
                floor=binding.transform.TransformPoint(new Vector3(shape.Axis.x,h,shape.Axis.y)).y;
            }
            else{center=view.Anchor("Drain")?view.Anchor("Drain").position:view.transform.position;radius=view.Size.x*.35f;floor=center.y;center.y+=view.Size.y;}
        }
        /// <summary>壶和量酒器是杯沿倾泻，不是沿轴加压的喷嘴；维持Q/A速率，重分配为向下为主。</summary>
        public static Vector3 ExitVelocity(ItemView view,Vector3 axis,float flow)
        {
            float radius=OutletRadius(view),speed=flow*1e-6f/(Mathf.PI*radius*radius);
            if(view.Item is not CobblerShaker&&view.Item is not Jigger)return axis*speed;
            var p=PourSettings.Load();var horizontal=Vector3.ProjectOnPlane(axis,Vector3.up).normalized;
            bool jigger=view.Item is Jigger;
            float lateral=Mathf.Min(speed*Mathf.Clamp01(jigger?p.jiggerLateralSpeedFraction:p.shakerLateralSpeedFraction),Mathf.Max(0,jigger?p.jiggerMaximumLateralSpeed:p.shakerMaximumLateralSpeed));
            return horizontal*lateral+Vector3.down*Mathf.Sqrt(Mathf.Max(0,speed*speed-lateral*lateral));
        }
        public PourStreamInput Step(float dt,float angle,float flow,Container targetContainer=null)
        {
            elapsed+=dt;float t=Mathf.SmoothStep(0,1,Progress);
            float entryAngle=settings.clickTiltDegrees;
            if(source.Item is Bottle bottle&&flowModel!=null)
                entryAngle=(float)flowModel.StartAngle(bottle.RemainingMl,bottle.CapacityMl,source.DefinitionId);
            float requestedAngle=angle*t;
            if(source.Item is Bottle&&Progress>=1)
            {
                // A receipt can outlive a fast mouse reversal by one core tick. Keep its tail
                // in a valid pouring pose, then lower at a bounded visual rate. No input is delayed.
                if(flow>0)requestedAngle=Mathf.Max(requestedAngle,entryAngle);
                shownAngle=requestedAngle>=shownAngle?requestedAngle:Mathf.MoveTowards(shownAngle,requestedAngle,settings.uprightDegreesPerSecond*dt);
            }
            else shownAngle=requestedAngle;
            // Hand roots are set by BarSession before this call; record that pose for a smooth return there.
            if(receiverHeld){receiver.transform.rotation=Quaternion.Slerp(receiverRotation,receiverUpright,t);receiver.transform.position=Vector3.Lerp(receiverStart,camera.TransformPoint(settings.heldReceiverPosition),t);}
            Mouth(receiver,targetContainer,out var mouth,out var mouthRadius,out float plane);
            source.transform.rotation=sourceRotation;
            var tiltAxis=Vector3.ProjectOnPlane(camera.forward,Vector3.up).normalized;
            if(tiltAxis.sqrMagnitude<.5f)tiltAxis=Vector3.forward;
            var initialAxis=Axis(source);var upright=Quaternion.FromToRotation(initialAxis,source.Item is Squeezer?Vector3.down:Vector3.up)*sourceRotation;
            if(source.Item is Jigger)
            {
                source.transform.rotation=upright;
                var orientation=source.transform.Find("Orientation");
                if(orientation)orientation.rotation=Quaternion.AngleAxis(shownAngle,tiltAxis)*orientation.rotation;
            }
            else source.transform.rotation=source.Item is Squeezer?upright:Quaternion.AngleAxis(shownAngle,tiltAxis)*upright;
            // The root offset is recomputed after rotation, so the final mouth stays fixed while tilting.
            var axis=Axis(source).normalized;
            Vector3 velocity=ExitVelocity(source,axis,flow);
            var desiredLip=mouth+Vector3.up*settings.mouthClearance;
            // An upright source must wait BESIDE the receiver, not with its body underneath the mouth.
            // Blend this lateral clearance away as the same input rotation approaches the start angle.
            // Pressing tools never tilt: their outlet must stay over the receiver even at angle=0.
            float sideWeight=source.Item is Squeezer?0:EntrySideWeight(shownAngle,entryAngle);
            var bodySide=Vector3.Cross(Vector3.up,tiltAxis).normalized;
            float sideDistance=EntrySideDistance(source,mouthRadius);
            if(PourTrajectory.PlaneTime(desiredLip,velocity,mouth.y,settings.gravity,out float crossTime))
            {var drift=PourTrajectory.Point(desiredLip,velocity,crossTime,settings.gravity)-mouth;desiredLip-=new Vector3(drift.x,0,drift.z);}
            desiredLip+=bodySide*(sideDistance*sideWeight);
            aimedLip=Vector3.Lerp(aimedLip,desiredLip,1-Mathf.Exp(-settings.aimResponse*dt));
            if(Progress>=1)aimedLip=desiredLip; // exact ballistic alignment once the fixed approach has completed
            var baseLip=sourceStart+(source.PourPoint-source.transform.position);
            var lip=Vector3.Lerp(baseLip,aimedLip,t);source.transform.position+=lip-source.PourPoint;
            var input=new PourStreamInput{Outlet=new Pose(source.PourPoint,Quaternion.LookRotation(axis)),ExitVelocity=velocity,ConfirmedMlPerSecond=flow,LiquidPlaneY=plane,MouthCenter=mouth,MouthRadius=mouthRadius};
            SetImpactBoundary(ref input,Binding(receiver,targetContainer));return input;
        }
        /// <summary>读取实际内腔在当前液位的半径；接收容器在既有站位中保持口朝上。</summary>
        public static void SetImpactBoundary(ref PourStreamInput input,ContainerVisual binding)
        {
            if(!binding)return;
            var shape=binding.Shape;var frame=binding.transform;
            var local=frame.InverseTransformPoint(new Vector3(input.MouthCenter.x,input.LiquidPlaneY,input.MouthCenter.z));
            input.ImpactCenter=frame.TransformPoint(new Vector3(shape.Axis.x,local.y,shape.Axis.y));input.ImpactCenter.y=input.LiquidPlaneY;
            var scale=frame.lossyScale;input.ImpactRadius=shape.RadiusAt(local.y)*Mathf.Min(Mathf.Abs(scale.x),Mathf.Abs(scale.z));input.HasImpactBoundary=true;
        }
    }
}
