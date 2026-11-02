using System;
using System.Collections.Generic;
using System.Linq;
using Bar.Domain;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>装冰的表现时序：整件工具先越过杯口，再倾勺/松夹、逐块滑落，最后回到实时持握位。数字为未标定的画面参数。</summary>
    public sealed class IceDeliveryMotion
    {
        [Serializable] public sealed class Settings
        {
            public float reachSeconds=.62f, tipSeconds=.26f, blockIntervalSeconds=.09f, returnSeconds=.52f;
            public float rimClearanceMetres=.008f, travelLiftMetres=.07f, scoopTipDegrees=52, tongsReleaseSeconds=.08f;
            public float sideArcMetres=.018f, distanceLiftGain=.08f, wristLagFraction=.10f, wristRollDegrees=9;
            public float preTipDegrees=6, partialTipDegrees=36, arrivalSpeed=.055f, settleFrequency=4, settleZeta=.9f;
            public float untipDelaySeconds=.035f, withdrawTipFraction=.15f, returnLiftMetres=.025f, followFrequency=5, followZeta=1;
            public static Settings Load()=>JsonUtility.FromJson<Settings>(Resources.Load<TextAsset>("BarIceDeliverySettings").text);
        }
        private readonly Settings p=Settings.Load();
        private ItemView tool,receiver;private ContainerVisual binding;private Transform camera;
        private Vector3 releaseLocal,gripLocal,fromGripCamera,scoopForward,sideArc,rollAxis,returnPosition,returnVelocity,returnOmega;
        private Vector3 receiverReturnPosition,receiverReturnVelocity,receiverReturnOmega;
        private Quaternion fromRotationCamera,restRotation,returnRotation,receiverReturnRotation;
        private Vector3 lastGrip,lastVelocity,lastOmega,lastReceiverPosition,lastReceiverVelocity,lastReceiverOmega;
        private Quaternion lastRotation,lastReceiverRotation;
        private float reachAge,phaseAge,returnAge,clearance,holdSeconds,lastRelease,arcHeight,tipTarget,tipFraction;
        private SecondOrderVector mouthFollow;
        private bool committed,returning,heldReceiver;private readonly List<Vector3> metal=new List<Vector3>();
        public bool Busy=>tool;
        public bool Ready=>Busy&&!committed&&!returning&&reachAge>=p.reachSeconds;
        public ItemView Tool=>tool;
        public float TipFraction=>committed&&!returning?tipFraction:0;
        public string PhaseName=>!Busy?"Idle":returning?"Return":committed?(phaseAge>lastRelease?"FollowThrough":"Release"):Ready?"Arrive":"Reach";
        public Vector3 ReleasePosition=>tool?tool.transform.TransformPoint(releaseLocal):Vector3.zero;
        public bool Begin(ItemView source,ItemView target,Container vessel,Transform camera)
        {
            var cavity=ItemView.StirBinding(target,vessel);if(!source||!cavity||Busy)return false;
            tool=source;receiver=target;binding=cavity;this.camera=camera;reachAge=phaseAge=returnAge=tipFraction=0;committed=returning=false;metal.Clear();tipTarget=p.scoopTipDegrees;
            heldReceiver=target.Item.Location.Kind==LocationKind.LeftHand||target.Item.Location.Kind==LocationKind.RightHand;
            var root=source.transform;var ice=source.GetComponentInChildren<IceVisual>();
            var grip=source.Anchor(source.Item.Location.Kind==LocationKind.LeftHand?"Grip_L":"Grip_R");
            if(!grip)grip=source.Anchor("Grip_R");gripLocal=grip?root.InverseTransformPoint(grip.position):Vector3.zero;
            var cubes=ice?ice.Cubes.Where(c=>c.gameObject.activeSelf).ToArray():Array.Empty<Transform>();
            releaseLocal=root.InverseTransformPoint(source.LoadedIceOrigin);
            Vector3 localForward=Vector3.forward,localUp=Vector3.up;
            foreach(var filter in source.GetComponentsInChildren<MeshFilter>())
            {
                if(filter.name!="ScoopBowl"&&filter.name!="Handle"&&!filter.name.StartsWith("Arm")&&!filter.name.StartsWith("Tooth")&&filter.name!="Spring")continue;
                foreach(var v in filter.sharedMesh.vertices)metal.Add(root.InverseTransformPoint(filter.transform.TransformPoint(v)));
            }
            if(source.Item is IceScoop)
            {
                var bowl=source.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="ScoopBowl");
                var handle=source.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Handle");
                var b=CavityShape.BoundsIn(bowl,root);var h=CavityShape.BoundsIn(handle,root);
                localForward=Vector3.forward*(b.center.z>=h.center.z?1:-1);
                scoopForward=localForward;
                // The leading cube centre must pass beyond the metal lip before dropping.
                releaseLocal=new Vector3(b.center.x,releaseLocal.y,b.center.z+localForward.z*(b.extents.z+IceAppearance.ConfiguredSide*.5f));
            }
            else
            {
                var spring=source.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Spring");
                localForward=(releaseLocal-CavityShape.BoundsIn(spring,root).center).normalized;
                var arms=source.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Arm")).ToArray();
                var opening=CavityShape.BoundsIn(arms[0],root).center-CavityShape.BoundsIn(arms[1],root).center;
                localUp=Vector3.Cross(localForward,opening).normalized;
                if(Vector3.Dot(localUp,Vector3.up)<0)localUp=-localUp;
            }
            foreach(var cube in cubes)
            {
                var surface=cube.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="IceSurface");var b=CavityShape.BoundsIn(surface,root);
                for(int i=0;i<8;i++)metal.Add(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
            }
            var forward=Vector3.ProjectOnPlane(Mouth()-camera.position,Vector3.up).normalized;
            if(forward.sqrMagnitude<.01f)forward=Vector3.ProjectOnPlane(camera.forward,Vector3.up).normalized;
            restRotation=Quaternion.LookRotation(forward,Vector3.up)*Quaternion.Inverse(Quaternion.LookRotation(localForward,localUp));
            fromRotationCamera=Quaternion.Inverse(camera.rotation)*root.rotation;fromGripCamera=camera.InverseTransformPoint(root.TransformPoint(gripLocal));
            float sign=source.Item.Location.Kind==LocationKind.LeftHand?-1:1;
            sideArc=Vector3.ProjectOnPlane(camera.right,Vector3.up).normalized*(sign*p.sideArcMetres);
            rollAxis=forward*sign;
            clearance=IceAppearance.ConfiguredSide*.9f+p.rimClearanceMetres;
            // Scan the actual tool/ice envelope over the whole tip, not an assumed FBX axis or point-sized mouth.
            for(int i=0;i<=12;i++)foreach(var v in metal)
                clearance=Mathf.Max(clearance,-(Rotation(p.scoopTipDegrees*i/12f)*(v-releaseLocal)).y+p.rimClearanceMetres);
            var mouth=Mouth();mouthFollow=new SecondOrderVector(p.followFrequency,p.followZeta,0,mouth);
            arcHeight=p.travelLiftMetres+Vector3.Distance(root.TransformPoint(gripLocal),mouth)*p.distanceLiftGain;
            // Plan one continuous arc high enough for the actual tool envelope.
            // No frame-by-frame collision correction or pose teleport is added.
            float required=arcHeight,radius=binding.Shape.RadiusAt(binding.Shape.Top)+p.rimClearanceMetres;
            for(int i=1;i<48;i++)
            {
                float u=i/48f;var pose=ReachPose(u,mouth+Vector3.up*clearance,0);float envelope=Arc(u);
                foreach(var point in metal)
                {
                    var world=pose.position+pose.rotation*(point-gripLocal);
                    if(new Vector2(world.x-mouth.x,world.z-mouth.z).sqrMagnitude>radius*radius)continue;
                    required=Mathf.Max(required,(mouth.y+p.rimClearanceMetres-world.y)/Mathf.Max(.0001f,envelope));
                }
            }
            arcHeight=required;lastGrip=root.TransformPoint(gripLocal);lastRotation=root.rotation;lastVelocity=lastOmega=Vector3.zero;
            lastReceiverPosition=receiver.transform.position;lastReceiverRotation=receiver.transform.rotation;lastReceiverVelocity=lastReceiverOmega=Vector3.zero;
            return true;
        }
        private Vector3 Mouth()=>binding.transform.TransformPoint(new Vector3(binding.Shape.Axis.x,binding.Shape.Top,binding.Shape.Axis.y));
        private Quaternion Rotation(float degrees)
        {
            if(tool.Item is not IceScoop)return restRotation;
            return Quaternion.AngleAxis(degrees,restRotation*Vector3.Cross(Vector3.up,scoopForward))*restRotation;
        }
        /// <summary>在原 AddIce 回执时采样仍显示在工具上的冰，交给接收容器接力绘制；不造冰、不改回执。</summary>
        public IceDeliveryBatch Commit(int count,float fallSeconds)
        {
            var batch=new IceDeliveryBatch(tool,releaseLocal,count,tool.Item is IceScoop?p.tipSeconds:p.tongsReleaseSeconds,p.blockIntervalSeconds);
            if(tool.Item is IceScoop scoop&&scoop.LoadedCubes>0)tipTarget=p.partialTipDegrees;
            committed=true;phaseAge=0;lastRelease=batch.LastReleaseSeconds;holdSeconds=lastRelease+fallSeconds+.06f;return batch;
        }
        public void Cancel()
        {
            if(!Busy||returning||committed&&phaseAge<holdSeconds)return;
            StartReturn();
        }
        private void StartReturn()
        {
            returning=true;returnAge=0;returnPosition=lastGrip;returnRotation=lastRotation;returnVelocity=lastVelocity;returnOmega=lastOmega;
            receiverReturnPosition=lastReceiverPosition;receiverReturnRotation=lastReceiverRotation;receiverReturnVelocity=lastReceiverVelocity;receiverReturnOmega=lastReceiverOmega;
        }
        // Like the shake's catch, the arriving hand gives with its velocity.
        // This is an analytic damped response, stable at 30/60/144 Hz.
        private float Give(float t)
        {float w=2*Mathf.PI*p.settleFrequency,z=Mathf.Clamp(p.settleZeta,0,.99f),wd=w*Mathf.Sqrt(1-z*z);return Mathf.Exp(-z*w*t)*Mathf.Sin(wd*t)/wd;}
        private Vector3 ArrivalVelocity=>Vector3.down*p.arrivalSpeed;
        private Pose ReachPose(float u,Vector3 mouth,float height)
        {
            float t=p.reachSeconds,u2=u*u,u3=u2*u;
            var from=camera.TransformPoint(fromGripCamera);var goalRotation=Rotation(p.preTipDegrees);
            var goal=mouth+goalRotation*(gripLocal-releaseLocal);
            var acceleration=-2*p.settleZeta*(2*Mathf.PI*p.settleFrequency)*ArrivalVelocity;
            var position=Vector3.Lerp(from,goal,Ease(u))+ArrivalVelocity*t*(u3*(-4+7*u-3*u2))+acceleration*t*t*(.5f*u3*(1-u)*(1-u))+(Vector3.up*height+sideArc)*Arc(u);
            float wrist=Ease((u-p.wristLagFraction)/(1-p.wristLagFraction));
            var rotation=Quaternion.AngleAxis(p.wristRollDegrees*Arc(u),rollAxis)*Quaternion.Slerp(camera.rotation*fromRotationCamera,goalRotation,wrist);
            return new Pose(position,rotation);
        }
        private static float Arc(float u){u=Mathf.Clamp01(u);return 64*u*u*u*(1-u)*(1-u)*(1-u);}
        private static float StartVelocity(float u)=>u-6*u*u*u+8*u*u*u*u-3*u*u*u*u*u;
        private static Vector3 AngularVelocity(Quaternion before,Quaternion after,float dt)
        {
            var q=after*Quaternion.Inverse(before);if(q.w<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
            q.ToAngleAxis(out float angle,out var axis);return angle<.0001f||!float.IsFinite(axis.x)?Vector3.zero:axis*(Mathf.DeltaAngle(0,angle)/dt);
        }
        private static Quaternion ReturnRotation(Quaternion from,Quaternion to,Vector3 omega,float seconds,float u)
        {
            float magnitude=omega.magnitude;
            return Quaternion.AngleAxis(magnitude*seconds*StartVelocity(u),magnitude>.0001f?omega/magnitude:Vector3.up)*Quaternion.Slerp(from,to,Ease(u));
        }
        private void Track(float dt)
        {
            var point=tool.transform.TransformPoint(gripLocal);var rotation=tool.transform.rotation;
            if(dt>.0001f){lastVelocity=(point-lastGrip)/dt;lastOmega=AngularVelocity(lastRotation,rotation,dt);}
            lastGrip=point;lastRotation=rotation;
            if(!receiver)return;
            if(dt>.0001f){lastReceiverVelocity=(receiver.transform.position-lastReceiverPosition)/dt;lastReceiverOmega=AngularVelocity(lastReceiverRotation,receiver.transform.rotation,dt);}
            lastReceiverPosition=receiver.transform.position;lastReceiverRotation=receiver.transform.rotation;
        }
        /// <summary>必须在 BarSession 每帧复位手持根节点之后调用；回程追随本帧的真实持握姿态。</summary>
        public void Advance(float dt)
        {
            if(!Busy)return;if(!receiver||!binding){committed=false;Cancel();}
            dt=Mathf.Clamp(dt,0,.1f);var root=tool.transform;var home=new Pose(root.position,root.rotation);
            var receiverHome=receiver?new Pose(receiver.transform.position,receiver.transform.rotation):default;
            if(!returning)
            {
                float spent=committed?Mathf.Min(dt,Mathf.Max(0,holdSeconds-phaseAge)):dt;
                AdvanceWork(spent);Track(spent);dt-=spent;
                if(committed&&phaseAge>=holdSeconds-.00001f)StartReturn();else return;
            }
            returnAge+=dt;float u=Mathf.Clamp01(returnAge/p.returnSeconds);var goal=home.position+home.rotation*gripLocal;
            var position=Vector3.Lerp(returnPosition,goal,Ease(u))+returnVelocity*p.returnSeconds*StartVelocity(u)+Vector3.up*p.returnLiftMetres*Arc(u);
            var rotation=ReturnRotation(returnRotation,home.rotation,returnOmega,p.returnSeconds,u);
            root.SetPositionAndRotation(position-rotation*gripLocal,rotation);
            if(heldReceiver&&receiver)
                receiver.transform.SetPositionAndRotation(Vector3.Lerp(receiverReturnPosition,receiverHome.position,Ease(u))+receiverReturnVelocity*p.returnSeconds*StartVelocity(u),ReturnRotation(receiverReturnRotation,receiverHome.rotation,receiverReturnOmega,p.returnSeconds,u));
            Track(dt);if(u>=1){tool=receiver=null;binding=null;}
        }
        private void AdvanceWork(float dt)
        {
            reachAge+=dt;if(committed)phaseAge+=dt;
            if(heldReceiver)
            {
                // A tilted off-hand shaker must present its mouth before the other hand releases ice.
                // Rotate about the actual mouth, preserving its live hand position; both hands return together.
                var before=Mouth();var upright=Quaternion.FromToRotation(binding.transform.up,Vector3.up)*receiver.transform.rotation;
                receiver.transform.rotation=Quaternion.Slerp(receiver.transform.rotation,upright,committed?1:Ease(reachAge/(p.reachSeconds*.78f)));
                receiver.transform.position+=before-Mouth();
            }
            var raw=Mouth();var followed=mouthFollow.Update(dt,raw);float progress=Mathf.Clamp01(reachAge/p.reachSeconds);
            var mouth=Vector3.Lerp(followed,raw,Ease(progress))+Vector3.up*clearance;
            if(reachAge<p.reachSeconds)
            {var pose=ReachPose(progress,mouth,arcHeight);tool.transform.SetPositionAndRotation(pose.position-pose.rotation*gripLocal,pose.rotation);return;}
            float degrees=p.preTipDegrees;tipFraction=committed?Ease(phaseAge/(tool.Item is IceScoop?p.tipSeconds:p.tongsReleaseSeconds)):0;
            if(committed&&tool.Item is IceScoop)
            {
                float withdraw=Ease((phaseAge-lastRelease-p.untipDelaySeconds)/Mathf.Max(.01f,holdSeconds-lastRelease-p.untipDelaySeconds));
                tipFraction=Mathf.Lerp(tipFraction,p.withdrawTipFraction,withdraw);degrees=Mathf.Lerp(p.preTipDegrees,tipTarget,tipFraction);
            }
            var rotation=Rotation(degrees);var pivot=mouth+ArrivalVelocity*Give(reachAge-p.reachSeconds);
            tool.transform.SetPositionAndRotation(pivot-rotation*releaseLocal,rotation);
        }
        internal static float Ease(float u){u=Mathf.Clamp01(u);return u*u*u*(u*(u*6-15)+10);}
    }
    /// <summary>工具与容器之间的一批视觉接力。前一块出勺后下一块才滑出；核心的 AddIce 仍只有原来的一次。</summary>
    public sealed class IceDeliveryBatch
    {
        private readonly Transform tool;private readonly Vector3[] from;private readonly Quaternion[] rotations;
        private readonly Vector3 lip;private readonly float slide,interval;
        public float LastReleaseSeconds=>slide+Mathf.Max(0,from.Length-1)*interval;
        public IceDeliveryBatch(ItemView source,Vector3 release,int count,float slideSeconds,float spacing)
        {
            tool=source.transform;lip=release;slide=slideSeconds;interval=spacing;
            var ice=source.GetComponentInChildren<IceVisual>();
            var cubes=ice.Cubes.Where(c=>c.gameObject.activeSelf).OrderBy(c=>(tool.InverseTransformPoint(c.position)-lip).sqrMagnitude).Take(count).ToArray();
            from=cubes.Select(c=>tool.InverseTransformPoint(c.position)).ToArray();rotations=cubes.Select(c=>Quaternion.Inverse(tool.rotation)*c.rotation).ToArray();
        }
        public float ReleaseAfter(int index)=>slide+index*interval;
        public Vector3 ReleasePosition=>tool.TransformPoint(lip);
        public Pose Sliding(int index,float time)
        {
            index=Mathf.Min(index,from.Length-1);float u=IceDeliveryMotion.Ease((time-index*interval)/slide);
            return new Pose(tool.TransformPoint(Vector3.Lerp(from[index],lip,u)),tool.rotation*rotations[index]);
        }
    }
}
