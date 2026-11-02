using System.Collections.Generic;
using Bar.Data;
using Bar.Domain;
using Bar.Logging;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>
    /// 手持物的取景与手感。每帧在 BarSession 摆放手持物之前，按物品自己的尺寸和握点求出手锚点的相机局部姿态，
    /// 再叠加转头滞后、走路起伏和拿起/换手过渡。只改左右手锚点；倒酒、摇壶、搅拌仍在锚点之后改物品姿态，领域与物理核心不受影响。
    /// V2（2026-10-28）加展示动作：合盖/装滤网/盖瓶盖时往下一顿、摇完收势端近赏霜、擦干时壶绕自身轴转一圈、拿起时的上抬弧线。都由领域事件触发，只动锚点。
    /// V3（2026-10-28）加空量酒器检视（view_inspect 事件）：端详两端、指间滚转、上抛接住，结束时回到原朝向；左右手镜像沿用按手侧求解。
    /// </summary>
    [DefaultExecutionOrder(-50)]public sealed partial class HoldViewRig:MonoBehaviour
    {
        private sealed class Hand
        {
            public Transform Anchor;public HandSide Side;public ItemView View;public Vector3 FromPosition;public Quaternion FromRotation=Quaternion.identity;
            public Vector3 RestPosition;public Quaternion RestRotation=Quaternion.identity;public SecondOrderFloat Blend;
            public SecondOrderVector Kick;public Vector3 KickTarget;public float KickLeft;public SecondOrderFloat Admire;public float AdmireLeft,Spin=-1,Inspect=-1;public bool Caught;public Quaternion InspectLook=Quaternion.identity;
            public Vector3 DrawnPosition;public Quaternion DrawnRotation=Quaternion.identity;
            public LogRecord InspectEvent;public InspectionMotion Motion;public bool FastReturning;public int SoundMask;public double SoundDsp;
            public SecondOrderFloat ReturnBlend;public Vector3 ReturnFrom;public Quaternion ReturnRotation;
        }
        private BarSession session;private PlayerController player;private HoldViewSettings settings;private Hand left,right;
        private SecondOrderVector sway;private SecondOrderFloat walk,lean;private Quaternion lastCamera;private Vector3 lastPosition;private float bobPhase;private bool ready;
        private readonly List<MeshRenderer> renderers=new List<MeshRenderer>(32);private readonly Dictionary<ItemView,Pose> drawn=new Dictionary<ItemView,Pose>();
        private readonly HashSet<ItemView> inspectionReturns=new HashSet<ItemView>();
        /// <summary>转头滞后（度，相机局部 pitch/yaw）。</summary>
        public Vector3 Sway=>sway!=null?sway.Value:Vector3.zero;
        public void Initialize(BarSession owner)
        {
            session=owner;player=owner.Player;settings=HoldViewSettings.Load();
            left=new Hand{Anchor=player.LeftAnchor,Side=HandSide.Left};right=new Hand{Anchor=player.RightAnchor,Side=HandSide.Right};
            var accent=settings.accent;
            foreach(var hand in new[]{left,right})
            {
                hand.Blend=new SecondOrderFloat(settings.transition.frequency,settings.transition.zeta,settings.transition.response,1);
                hand.Kick=new SecondOrderVector(accent.tapFrequency,accent.tapZeta,0,Vector3.zero);hand.Admire=new SecondOrderFloat(accent.admireFrequency,accent.admireZeta,0,0);
                hand.ReturnBlend=new SecondOrderFloat(settings.inspectAll.interruptFrequency,settings.inspectAll.interruptZeta,0,1);
            }
            session.Adapter.Events.Published+=OnDomainEvent;
            sway=new SecondOrderVector(settings.sway.frequency,settings.sway.zeta,settings.sway.response,Vector3.zero);walk=new SecondOrderFloat(settings.bob.frequency,settings.bob.zeta,0,0);lean=new SecondOrderFloat(settings.observe.frequency,settings.observe.zeta,0,0);
            lastCamera=player.LookRotation;lastPosition=player.transform.position;ready=true;
        }
        private void Update()=>AdvancePresentation(Time.unscaledDeltaTime);
        /// <summary>与实际Update相同的表现步进；允许用30/60/144fps固定步长验证，不推进核心时间。</summary>
        public void AdvancePresentation(float dt)
        {
            if(!ready||!player.ViewCamera||session.Hands==null||dt<0)return;var camera=player.ViewCamera;
            inspectionReturns.RemoveWhere(v=>!v||!session.Hands.SideOf(v.Item).HasValue);
            AdvanceReleasedInspectionMeshes(dt);
            // Look lag: the hand trails the head and springs back; none while an action locks the view (pour, shake, stir).
            var delta=Quaternion.Inverse(lastCamera)*player.LookRotation;lastCamera=player.LookRotation;
            var turn=new Vector3(Mathf.DeltaAngle(0,delta.eulerAngles.x),Mathf.DeltaAngle(0,delta.eulerAngles.y),0);bool calm=!session.LookLocked;
            sway.Update(dt,calm&&dt>0?Vector3.ClampMagnitude(-turn/dt*settings.sway.lagSeconds,settings.sway.maxDegrees):Vector3.zero);
            // Walking: a small figure-of-eight at step rate, scaled by horizontal speed.
            var at=player.transform.position;var step=at-lastPosition;lastPosition=at;float speed=dt>0?new Vector2(step.x,step.z).magnitude/dt:0;
            walk.Update(dt,calm?Mathf.Clamp01(speed/Mathf.Max(.01f,player.MoveSpeed)):0);bobPhase=Mathf.Repeat(bobPhase+dt*2*Mathf.PI*settings.bob.stepHz,2*Mathf.PI*16);
            float amount=Mathf.Max(0,walk.Value);var bob=new Vector3(Mathf.Sin(bobPhase)*settings.bob.sideMetres,(Mathf.Cos(2*bobPhase)-1)*.5f*settings.bob.dropMetres,0)*amount;
            var lag=Quaternion.Euler(sway.Value.x,sway.Value.y,-sway.Value.y*settings.sway.rollFactor);
            // Only the ordinary walking channel changes; all authored inspection/action poses retain priority.
            if(player.Locomotion)
            {
                bool walking=calm&&left.Inspect<0&&right.Inspect<0&&left.Spin<0&&right.Spin<0&&!left.FastReturning&&!right.FastReturning;
                bob=walking?player.Locomotion.HandOffset:Vector3.zero;
            }
            Observe(dt);
            Place(left,camera,dt,lag,bob);Place(right,camera,dt,lag,bob);
        }
        /// <summary>观察镜头当前的低头角度（度）。</summary>
        public float Lean=>lean!=null?lean.Value:0;
        private void Observe(float dt)
        {
            // Pouring into something held: lean in so the line of sight drops into the receiver (inner marks, rising surface).
            // Only while the pour locks the view; when it ends the lean becomes the player's own pitch, so the camera never moves without input.
            var o=settings.observe;var receiver=session.PourTarget;while(receiver?.Parent!=null)receiver=receiver.Parent;
            if(!o.enabled||receiver==null||!session.Hands.SideOf(receiver).HasValue||!session.LookLocked)
            {
                if(lean.Value!=0){player.KeepLean();lean.Reset(0);lastCamera=player.LookRotation;}
                return;
            }
            var held=PourSettings.Load().heldReceiverPosition;float below=Mathf.Atan2(-held.y,Mathf.Max(.01f,held.z))*Mathf.Rad2Deg;
            float target=Mathf.Clamp(o.sightDegrees-player.Pitch-below,0,o.maxDegrees);
            player.Lean(Mathf.Clamp(lean.Update(dt,target),0,o.maxDegrees));
        }
        /// <summary>V2 展示动作的触发：只看领域事件，不改任何领域状态。</summary>
        private void OnDomainEvent(DomainEvent e)
        {
            if(e.Observation is not LogRecord r)return;string id=r.actionId??"";var accent=settings.accent;
            if(e.Kind=="action_began"){left.AdmireLeft=right.AdmireLeft=0;Interrupt(left,"action."+id);Interrupt(right,"action."+id);return;}
            if(e.Kind=="view_inspect"){var hand=HandOf(r.actorId);if(hand!=null&&hand.Inspect<0){hand.Inspect=0;hand.InspectEvent=r;hand.Motion=null;hand.FastReturning=false;hand.Caught=false;hand.AdmireLeft=0;hand.SoundMask=0;hand.SoundDsp=AudioSettings.dspTime+BarAudioCatalog.Load().scheduleSeconds;}return;}
            if(e.Kind=="action_performed")
            {
                if(id=="shaker.attach_cap")Tap(HandOf(r.targetId),accent.capTapMetres);
                else if(id=="shaker.attach_strainer")Tap(HandOf(r.targetId),accent.strainerTapMetres);
                else if(id=="bottle.close")Tap(HandOf(r.targetId),accent.bottleCapTapMetres);
                else if(id=="shaker.wipe"){var hand=HandOf(r.targetId);if(hand!=null){hand.Spin=0;Tap(hand,accent.wipeTapMetres);}}
            }
            else if(e.Kind=="action_ended"&&id=="shaker.shake"){var hand=HandOf(r.actorId);if(hand!=null){Tap(hand,accent.shakeStopMetres);hand.AdmireLeft=accent.admireSeconds;}}
        }
        /// <summary>手里拿着该物品（或其所属整体）的那只手。</summary>
        private Hand HandOf(string instanceId)
        {
            if(instanceId==null)return null;Item item=null;foreach(var v in session.Views)if(v.InstanceId==instanceId){item=v.Item;break;}
            while(item?.Parent!=null)item=item.Parent;var side=item!=null?session.Hands.SideOf(item):null;
            return side==HandSide.Left?left:side==HandSide.Right?right:null;
        }
        /// <summary>检视中途被打断：从当前画出的姿态平滑回到持握位（沿用拿起过渡的弹簧）。</summary>
        private void Interrupt(Hand hand,string reason="hands.busy")
        {
            if(hand==null||hand.Inspect<0)return;
            hand.ReturnFrom=hand.Anchor.localPosition;hand.ReturnRotation=hand.Anchor.localRotation;
            if(hand.View)inspectionReturns.Add(hand.View);
            FinishInspection(hand,"interrupted",reason);hand.FastReturning=true;hand.ReturnBlend.Reset(0);hand.Blend.Reset(1);
        }
        /// <summary>该手是否正在播放检视动作。</summary>
        public bool Inspecting(HandSide side)=>(side==HandSide.Left?left:right)?.Inspect>=0;
        /// <summary>手往下一顿：短时把弹簧目标压低，高刚度、欠阻尼地弹回。</summary>
        private void Tap(Hand hand,float metres){if(hand==null||metres<=0)return;hand.KickTarget=Vector3.down*metres;hand.KickLeft=settings.accent.tapSeconds;}
        private readonly Dictionary<HoldProfile,HoldProfile> admired=new Dictionary<HoldProfile,HoldProfile>();
        /// <summary>收势赏霜用的取景：在原配置上往画面中央、上方挪，放大一点。</summary>
        private HoldProfile Admired(HoldProfile profile)
        {
            if(admired.TryGetValue(profile,out var a))return a;var accent=settings.accent;a=JsonUtility.FromJson<HoldProfile>(JsonUtility.ToJson(profile));
            a.center+=accent.admireShift;a.topLimit+=accent.admireShift.y;a.screenHeight*=accent.admireScale;admired[profile]=a;return a;
        }
        private void LateUpdate()
        {
            if(!ready)return;drawn.Clear();foreach(var view in session.Views)if(view)drawn[view]=new Pose(view.transform.position,view.transform.rotation);
        }
        private void Place(Hand hand,Camera camera,float dt,Quaternion lag,Vector3 bob)
        {
            if(!hand.Anchor)return;var item=session.Hands.Get(hand.Side);ItemView view=null;
            if(item!=null)foreach(var v in session.Views)if(v.Item==item){view=v;break;}
            if(view!=hand.View)
            {
                if(hand.Inspect>=0&&(view==null||hand.InspectEvent?.actorId!=view.InstanceId))Interrupt(hand,"held_item_changed");
                // Start from where the item is now (on the counter, or in the other hand), in camera space.
                hand.View=view;
                if(view)
                {
                    // BarSession may already have parented it to the hand this frame, so use where it was drawn last frame.
                    var grip=Grip(view,hand.Side);var root=view.transform;Vector3 rootPosition=root.position;Quaternion rootRotation=root.rotation;
                    if(!inspectionReturns.Contains(view)&&drawn.TryGetValue(view,out var last)){rootPosition=last.position;rootRotation=last.rotation;}
                    Vector3 fromPosition=grip?rootPosition+rootRotation*(Quaternion.Inverse(root.rotation)*(grip.position-root.position)):rootPosition;
                    Quaternion fromRotation=grip?rootRotation*(Quaternion.Inverse(root.rotation)*grip.rotation):rootRotation;
                    hand.FromPosition=camera.transform.InverseTransformPoint(fromPosition);hand.FromRotation=Quaternion.Inverse(camera.transform.rotation)*fromRotation;hand.Blend.Reset(0);
                }
                hand.AdmireLeft=0;hand.Admire.Reset(0);hand.Spin=-1;hand.FastReturning=false;
                if(view&&inspectionReturns.Contains(view)){hand.ReturnFrom=hand.FromPosition;hand.ReturnRotation=hand.FromRotation;hand.ReturnBlend.Reset(0);hand.FastReturning=true;hand.Blend.Reset(1);}
                if(hand.InspectEvent==null||view==null||hand.InspectEvent.actorId!=view.InstanceId)hand.Inspect=-1;
            }
            if(session.Hands.IsBusy)Interrupt(hand);
            var accent=settings.accent;hand.KickLeft-=dt;var kick=hand.Kick.Update(dt,hand.KickLeft>0?hand.KickTarget:Vector3.zero);
            if(!view){hand.Anchor.localPosition=Mirror(settings.emptyHand,hand.Side);hand.Anchor.localRotation=Quaternion.identity;hand.Blend.Reset(1);return;}
            var profile=settings.For(view.DefinitionId);Solve(view,hand.Side,camera,profile,out hand.RestPosition,out hand.RestRotation);
            // After a shake: bring the tin closer for a moment to show the frost; any new action, a busy hand or a different item ends it.
            if(session.Hands.IsBusy)hand.AdmireLeft=0;hand.AdmireLeft-=dt;float admire=hand.Admire.Update(dt,hand.AdmireLeft>0?1:0);
            if(admire>1e-3f){Solve(view,hand.Side,camera,Admired(profile),out var near,out var nearRotation);hand.RestPosition=Vector3.LerpUnclamped(hand.RestPosition,near,admire);hand.RestRotation=Quaternion.SlerpUnclamped(hand.RestRotation,nearRotation,admire);}
            float b=hand.Blend.Update(dt,1);
            // Pick-up path: a small lift in the middle of the move, so things come up off the counter rather than slide.
            var position=Vector3.LerpUnclamped(hand.FromPosition,hand.RestPosition,b)+Vector3.up*settings.transition.arcMetres*Mathf.Sin(Mathf.PI*Mathf.Clamp01(b));
            var rotation=Quaternion.SlerpUnclamped(hand.FromRotation,hand.RestRotation,b);
            if(hand.Spin>=0)
            {
                // Wiping: the tin turns once about its own axis inside the towel.
                hand.Spin+=dt;float t=hand.Spin/Mathf.Max(.01f,accent.wipeSeconds);if(t>=1)hand.Spin=-1;
                else
                {
                    var pivot=GripBounds(view,hand.Side,Quaternion.identity,renderers).center;var turned=rotation*Quaternion.AngleAxis(accent.wipeDegrees*Mathf.SmoothStep(0,1,t),Vector3.up);
                    position+=rotation*pivot-turned*pivot;rotation=turned;
                }
            }
            if(hand.Inspect>=0)InspectPose(hand,view,dt,ref position,ref rotation);
            if(hand.FastReturning)
            {
                float remaining=dt,returned=hand.ReturnBlend.Value;while(remaining>0){float h=Mathf.Min(remaining,1f/240);returned=hand.ReturnBlend.Update(h,1);remaining-=h;}
                position=Vector3.LerpUnclamped(hand.ReturnFrom,position,returned);rotation=Quaternion.SlerpUnclamped(hand.ReturnRotation,rotation,returned);
                if(returned>.9999f){hand.FastReturning=false;inspectionReturns.Remove(view);}
            }
            hand.DrawnPosition=position;hand.DrawnRotation=rotation;
            hand.Anchor.localPosition=lag*position+bob+kick;hand.Anchor.localRotation=lag*rotation;
        }
        /// <summary>
        /// V3 空量酒器检视（相机局部空间）。第一段把量酒器提到眼前，大杯口斜对镜头、刻度所在的内壁在对面，看清大杯的三道刻度；
        /// 再翻 180° 用同样的角度看小杯的两道刻度。第二段绕镜头前向在指间转两圈（快-慢-快-停）；第三段上抛翻一圈半接住，往下一顿。
        /// 翻转合计 720 度，结束时回到原来的朝向和持握位，不改领域里的朝上端。
        /// 2026-10-28 用户试玩后修正：第一段改为按刻度实际位置求朝向（原来只是相对持握姿态后仰 35°，刻度常在侧面或背面），拉近并加长。
        /// </summary>
        private void InspectPose(Hand hand,ItemView view,float dt,ref Vector3 position,ref Quaternion rotation)
        {
            if(view.Item is not Jigger){InspectGeneral(hand,view,dt,ref position,ref rotation);return;}
            var p=settings.inspect;if(hand.Inspect==0)hand.InspectLook=MarksFacing(view,hand.Side);
            hand.Inspect+=dt;float t=hand.Inspect;if(t>=p.tossEnd){hand.Inspect=p.tossEnd;FinishInspection(hand,"completed",null);return;}
            float sign=hand.Side==HandSide.Left?-1:1;var pivot=GripBounds(view,hand.Side,Quaternion.identity,renderers).center;
            float w=Mathf.SmoothStep(0,1,t/Mathf.Max(.01f,p.liftSeconds))*(1-Mathf.SmoothStep(0,1,(t-(p.tossEnd-p.returnSeconds))/Mathf.Max(.01f,p.returnSeconds)));
            // Stage 1: the big cup's mouth turned toward the eye so the marked wall is the far wall, then over to the small cup the same way.
            float flip=p.flipDegrees*Mathf.SmoothStep(0,1,(t-p.flipStart)/Mathf.Max(.01f,p.flipSeconds));
            // Stage 2: two turns in the fingers; angular speed 1-cos(4πu) gives fast, slow, fast, stop.
            float u=Mathf.Clamp01((t-p.lookEnd)/Mathf.Max(.01f,p.twirlEnd-p.lookEnd)),twirl=sign*p.twirlDegrees*(u-Mathf.Sin(4*Mathf.PI*u)/(4*Mathf.PI));
            // Stage 3: toss, one and a half turns in the air, catch with a dip.
            float air=Mathf.Clamp01((t-p.twirlEnd)/Mathf.Max(.01f,p.tossAirSeconds)),lift=p.tossMetres*4*air*(1-air),toss=p.tossFlipDegrees*Mathf.SmoothStep(0,1,air);
            if(air>=1)
            {
                // Retain the incoming downward velocity instead of kicking a
                // stationary 14 Hz spring (which exceeded 170 m/s² at 144 fps).
                hand.Caught=true;float caught=t-p.twirlEnd-p.tossAirSeconds;
                float settle=Mathf.Max(.01f,p.tossEnd-p.twirlEnd-p.tossAirSeconds);
                lift=Give(-4*p.tossMetres/p.tossAirSeconds,caught)*(1-Smooth(caught/settle))-p.catchMetres*Bump(caught/settle);
            }
            var rest=position+rotation*pivot;var target=new Vector3(sign*p.center.x,p.center.y,p.center.z);var center=Vector3.LerpUnclamped(rest,target,w)+Vector3.up*lift;
            var spin=Quaternion.AngleAxis(twirl,Vector3.forward);var turn=spin*Quaternion.AngleAxis(flip+toss,Vector3.right);
            // The twirl turns about a point just outside the item, as if rolling over a finger.
            var finger=center+new Vector3(sign*p.twirlPivotMetres,0,0);center=finger+spin*(center-finger);
            rotation=turn*Quaternion.SlerpUnclamped(rotation,hand.InspectLook,w);position=center-rotation*pivot;
            InspectionSounds(hand,p.sounds,null,view,t,position+rotation*pivot);
        }
        /// <summary>
        /// 看清大杯刻度的手锚点朝向：大杯口朝向镜头上方、与视线成 viewDegrees，刻度所在的内壁转到对面。
        /// 杯口方向取“量酒器中心 → 大杯中心”，刻度方向取刻度网格中心相对杯中心、垂直于杯轴的分量，都在握点坐标系里量。绕镜头横轴再转 180° 就是小杯的同样视角。
        /// </summary>
        private Quaternion MarksFacing(ItemView view,HandSide side)
        {
            var grip=Grip(view,side);var frame=grip?grip:view.transform;var inverse=Quaternion.Inverse(frame.rotation);
            Transform big=null;float size=0;
            foreach(var cup in view.GetComponentsInChildren<Transform>())if(cup.name=="CupA"||cup.name=="CupB")
            {foreach(var r in cup.GetComponentsInChildren<MeshRenderer>())if(r.enabled&&r.name=="Shell"&&r.bounds.size.sqrMagnitude>size){size=r.bounds.size.sqrMagnitude;big=cup;}}
            if(!big)return Quaternion.Euler(-35,0,0);
            Vector3 shell=Vector3.zero,marks=Vector3.zero;int count=0;var all=GripBounds(view,side,Quaternion.identity,renderers).center;
            foreach(var r in big.GetComponentsInChildren<MeshRenderer>()){if(!r.enabled)continue;var c=inverse*(r.bounds.center-frame.position);if(r.name=="Shell")shell=c;else if(r.name.StartsWith("Geometry_Mark")){marks+=c;count++;}}
            var open=(shell-all).normalized;if(count==0||open.sqrMagnitude<1e-6f)return Quaternion.Euler(-35,0,0);
            var wall=Vector3.ProjectOnPlane(marks/count-shell,open).normalized;
            float a=settings.inspect.viewDegrees*Mathf.Deg2Rad;var openCamera=new Vector3(0,Mathf.Sin(a),-Mathf.Cos(a));var wallCamera=new Vector3(0,Mathf.Cos(a),Mathf.Sin(a));
            return Quaternion.LookRotation(wallCamera,openCamera)*Quaternion.Inverse(Quaternion.LookRotation(wall,open));
        }
        private static Vector3 Mirror(Vector3 v,HandSide side)=>side==HandSide.Left?new Vector3(-v.x,v.y,v.z):v;
        /// <summary>BarSession 用来对齐手锚点的握点：本侧握点，否则另一侧，否则物品原点。</summary>
        public static Transform Grip(ItemView view,HandSide side)
        {var grip=view.Anchor(side==HandSide.Left?"Grip_L":"Grip_R");return grip?grip:view.Anchor(side==HandSide.Left?"Grip_R":"Grip_L");}
        /// <summary>物品（含已装上的部件）全部网格在握点坐标系里、再按 rotation 转过之后的包围盒；没有网格时为零。</summary>
        public static Bounds GripBounds(ItemView view,HandSide side,Quaternion rotation,List<MeshRenderer> buffer=null)
        {
            var grip=Grip(view,side);var frame=grip?grip:view.transform;var inverse=Quaternion.Inverse(frame.rotation);buffer??=new List<MeshRenderer>();
            Vector3 min=Vector3.positiveInfinity,max=Vector3.negativeInfinity;view.GetComponentsInChildren(false,buffer);
            foreach(var r in buffer)
            {
                if(!r.enabled)continue;var b=r.localBounds;
                for(int i=0;i<8;i++)
                {
                    var corner=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    var p=rotation*(inverse*(r.transform.TransformPoint(corner)-frame.position));min=Vector3.Min(min,p);max=Vector3.Max(max,p);
                }
            }
            if(float.IsInfinity(min.x))return default;var box=new Bounds();box.SetMinMax(min,max);return box;
        }
        private readonly List<Vector3> faces=new List<Vector3>(16);
        /// <summary>绕握点竖轴转动，让朝向特征（标签、刻度）的中心正对相机看向物品的视线，再向画面中央多转 faceYaw 度。</summary>
        private Quaternion Face(ItemView view,HandSide side,HoldProfile profile,Quaternion rotation,float sign,float tan,float aspect)
        {
            var grip=Grip(view,side);var frame=grip?grip:view.transform;var inverse=Quaternion.Inverse(frame.rotation);var keys=profile.face.Split(',');
            var all=GripBounds(view,side,Quaternion.identity,renderers);faces.Clear();
            foreach(var r in renderers)
            {
                if(!r.enabled)continue;bool match=false;foreach(var key in keys)if(key.Length>0&&r.name.Contains(key.Trim())){match=true;break;}
                if(match)faces.Add(inverse*(r.transform.TransformPoint(r.localBounds.center)-frame.position));
            }
            if(faces.Count==0)return rotation;
            float meanY=0;foreach(var f in faces)meanY+=f.y;meanY/=faces.Count;
            Vector3 sum=Vector3.zero;int count=0;foreach(var f in faces)if(!profile.faceUpper||f.y>=meanY){sum+=f;count++;}
            var offset=sum/count-all.center;offset.y=0;if(offset.sqrMagnitude<1e-8f)return rotation;
            // Facing the camera means facing back along the ray to where the item will sit on screen.
            var ray=new Vector3(sign*profile.center.x*tan*aspect,profile.center.y*tan,1).normalized;
            var toward=Quaternion.AngleAxis(sign*profile.faceYaw,Vector3.up)*-ray;var local=Quaternion.Inverse(rotation)*toward;local.y=0;
            return rotation*Quaternion.AngleAxis(Vector3.SignedAngle(offset,local,Vector3.up),Vector3.up);
        }
        /// <summary>
        /// 求手锚点的静止姿态：把物品（含已装上的部件）的网格包围盒转到握点坐标系，按取景配置的朝向旋转，
        /// 由目标占屏高度定距离，再把包围盒中心放到目标屏幕位置；顶端、露出比例和内侧边按配置收紧。
        /// </summary>
        public void Solve(ItemView view,HandSide side,Camera camera,out Vector3 position,out Quaternion rotation)=>Solve(view,side,camera,settings.For(view.DefinitionId),out position,out rotation);
        /// <summary>按指定取景配置求解（收势赏霜用）。</summary>
        public void Solve(ItemView view,HandSide side,Camera camera,HoldProfile profile,out Vector3 position,out Quaternion rotation)
        {
            float sign=side==HandSide.Left?-1:1;
            float tan=Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad),aspect=Mathf.Max(.1f,camera.aspect);
            rotation=Quaternion.Euler(profile.euler.x,profile.euler.y*sign,profile.euler.z*sign);
            if(!string.IsNullOrEmpty(profile.face))rotation=Face(view,side,profile,rotation,sign,tan,aspect);
            if(InspectionPolicy.Loaded(view.Item))rotation=InspectionPolicy.LimitRotation(view,side,camera,rotation,InspectionPolicy.MaximumTilt(view,session));
            var box=GripBounds(view,side,rotation,renderers);if(box.size==Vector3.zero){position=Mirror(settings.emptyHand,side);return;}var min=box.min;var max=box.max;
            float height=max.y-min.y,width=max.x-min.x;var middle=(min+max)*.5f;
            float distance=Mathf.Clamp(height/(2*tan*Mathf.Max(.05f,profile.screenHeight)),profile.minDistance,profile.maxDistance);
            // Keep at least minVisible of the height above the bottom edge with the top at topLimit; back off if needed.
            float needed=height*profile.minVisible/(tan*Mathf.Max(.05f,profile.topLimit+1));if(distance<needed)distance=Mathf.Min(profile.maxDistance,needed);
            float tall=height/(distance*tan),wide=width/(distance*tan*aspect);
            float v=Mathf.Min(profile.center.y,profile.topLimit-tall*.5f),u=Mathf.Max(profile.center.x,profile.innerLimit+wide*.5f);
            var target=new Vector3(sign*u*distance*tan*aspect,v*distance*tan,distance);position=target-middle;
        }
    }
}
