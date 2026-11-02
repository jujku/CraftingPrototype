using System;
using System.Collections.Generic;
using Bar.Data;
using Bar.Domain;
using Bar.Logging;
using UnityEngine;
namespace Bar.Unity
{
    public sealed partial class HoldViewRig
    {
        private sealed class InspectionMotion
        {
            public HoldInspectionProfile Profile;public Quaternion Look,Entry,Poise;public Vector3 Pivot,Golden,UpAxis;public Bounds Bounds;
            public float Depth,Duration;public bool Loaded,Carbonated,Sealed,LightRoll,Peek;
            public TongsViewRig Tongs;public SqueezerViewRig Squeezer;public InspectionTipFlex Tip;
        }
        private readonly List<InspectionTipFlex> releasedInspectionTips=new List<InspectionTipFlex>();
        public void InterruptInspections(string reason){if(!ready)return;Interrupt(left,reason);Interrupt(right,reason);}
        public float InspectionElapsed(HandSide side)=>(side==HandSide.Left?left:right)?.Inspect??-1;
        private HoldInspectionProfile ProfileFor(ItemView view)
        {
            var p=settings.inspectAll.For(view.DefinitionId);
            if(view.Item is not CobblerShaker sh||sh.State!=ShakerState.Open||p.openBodyLookSeconds<=p.lookSeconds)return p;
            var copy=JsonUtility.FromJson<HoldInspectionProfile>(JsonUtility.ToJson(p));float extra=p.openBodyLookSeconds-p.lookSeconds;
            copy.lookSeconds=p.openBodyLookSeconds;copy.liftSeconds=p.openBodyLiftSeconds>0?p.openBodyLiftSeconds:p.liftSeconds;
            foreach(var cue in copy.sounds)cue.time=cue.time<p.lookSeconds?cue.time*copy.lookSeconds/p.lookSeconds:cue.time+extra;
            return copy;
        }
        public float InspectionDuration(ItemView v)=>v.Item is Jigger?settings.inspect.tossEnd:v.Item is Squeezer sq&&sq.Produce!=null?settings.inspectAll.For(v.DefinitionId).peekSeconds:ProfileFor(v).Duration;
        public float InspectionTipDisplacement(HandSide side)=>(side==HandSide.Left?left:right)?.Motion?.Tip?.MaximumDisplacement??0;
        private void AdvanceReleasedInspectionMeshes(float dt)
        {for(int i=releasedInspectionTips.Count-1;i>=0;i--)if(releasedInspectionTips[i].AdvanceReturn(dt)){releasedInspectionTips[i].Dispose();releasedInspectionTips.RemoveAt(i);}}
        private void FinishInspection(Hand hand,string outcome,string reason)
        {
            var row=hand.InspectEvent;float duration=hand.Inspect;
            if(outcome=="interrupted"&&(hand.View?.Item is BarSpoon||hand.View?.Item is Jigger))session.Sound?.Pool.StopKeyPrefix("inspect:"+row?.activationId+":");
            hand.Inspect=-1;hand.InspectEvent=null;
            if(hand.Motion!=null)
            {var r=hand.Motion;r.Tongs?.SetInspection(null);r.Squeezer?.SetInspection(null);r.Squeezer?.SetPeekInspection(null);if(r.Tip!=null){r.Tip.BeginReturn();releasedInspectionTips.Add(r.Tip);}hand.Motion=null;}
            if(row!=null)session.Adapter.Publish("view_inspect_ended",new LogRecord{actionId=row.actionId,actorId=row.actorId,hand=row.hand,activationId=row.activationId,outcome=outcome,reason=reason,durationSeconds=Math.Max(0,duration)});
        }
        private void OnDestroy()
        {if(!ready)return;InterruptInspections("scene.unloaded");foreach(var tip in releasedInspectionTips)tip.Dispose();releasedInspectionTips.Clear();session.Adapter.Events.Published-=OnDomainEvent;}
        // V3 shares these original helpers; its jigger choreography is unchanged.
        private static float Ease(float u){u=Mathf.Clamp01(u);return u-Mathf.Sin(2*Mathf.PI*u)/(2*Mathf.PI);}
        private static float Smooth(float u){u=Mathf.Clamp01(u);return u*u*u*(10+u*(-15+6*u));}
        private static float Bump(float u){u=Mathf.Clamp01(u);float s=Mathf.Sin(Mathf.PI*u);return s*s;}
        private static float Give(float velocity,float t)
        {const float w=2*Mathf.PI*6,z=.75f;float wd=w*Mathf.Sqrt(1-z*z);return velocity/wd*Mathf.Exp(-z*w*t)*Mathf.Sin(wd*t);}
        private static float Accent(float u,float bias){u=Mathf.Clamp01(u);return Smooth(u/(bias+(1-bias)*u));}
        private static float CatchGive(float velocity,float t,HoldInspectionProfile p)
        {float w=2*Mathf.PI*p.catchFrequency,z=Mathf.Clamp(p.catchZeta,.05f,.99f),wd=w*Mathf.Sqrt(1-z*z);return velocity/wd*Mathf.Exp(-z*w*t)*Mathf.Sin(wd*t);}
        private static float Arrest(float t,HoldInspectionProfile p,float metres)=>-metres*Bump(t/Mathf.Max(.05f,p.catchDipSeconds));
        private static bool LongTool(Item item)=>item is BarSpoon or IceTongs or IceScoop;
        private InspectionMotion BeginGeneral(Hand hand,ItemView view,Quaternion rest)
        {
            for(int i=releasedInspectionTips.Count-1;i>=0;i--)if(releasedInspectionTips[i].Owner==view){releasedInspectionTips[i].Dispose();releasedInspectionTips.RemoveAt(i);}
            var p=ProfileFor(view);var camera=player.ViewCamera;float sign=hand.Side==HandSide.Left?-1:1;
            var r=new InspectionMotion{Profile=p,Loaded=InspectionPolicy.Loaded(view.Item),Carbonated=InspectionPolicy.Carbonated(view.Item),Sealed=view.Item is CobblerShaker sh&&sh.State==ShakerState.Sealed,Peek=view.Item is Squeezer sq&&sq.Produce!=null,Bounds=GripBounds(view,hand.Side,Quaternion.identity,renderers),Tongs=view.GetComponent<TongsViewRig>(),Squeezer=view.GetComponent<SqueezerViewRig>()};
            r.Duration=r.Peek?p.peekSeconds:p.Duration;r.Pivot=r.Bounds.center;r.Golden=new Vector3(r.Pivot.x,r.Pivot.y,Mathf.Lerp(r.Bounds.min.z,r.Bounds.max.z,p.pivotFraction));
            var grip=Grip(view,hand.Side);var frame=grip?grip:view.transform;var inverse=Quaternion.Inverse(frame.rotation);r.UpAxis=inverse*view.transform.up;
            if(view.Item is Bottle||view.Item is Glass||view.Item is CobblerShaker)
            {
                var face=inverse*view.transform.forward;
                if(view.Item is Bottle){Vector3 sum=Vector3.zero;int n=0;foreach(var renderer in renderers)if(renderer.enabled&&renderer.name.Contains("Label")){sum+=inverse*(renderer.bounds.center-frame.position)-r.Pivot;n++;}if(n>0)face=sum/n;}
                face=Vector3.ProjectOnPlane(face,r.UpAxis).normalized;var up=Quaternion.Inverse(camera.transform.rotation)*Vector3.up;
                var toward=-new Vector3(sign*p.center.x,p.center.y,p.center.z).normalized;toward=Vector3.ProjectOnPlane(toward,up).normalized;
                r.Look=Quaternion.LookRotation(toward,up)*Quaternion.Inverse(Quaternion.LookRotation(face,r.UpAxis));
            }
            else if(LongTool(view.Item))
            {float a=p.diagonalDegrees*Mathf.Deg2Rad,d=p.depthDegrees*Mathf.Deg2Rad;var bowl=new Vector3(-sign*Mathf.Cos(a)*Mathf.Cos(d),Mathf.Sin(a)*Mathf.Cos(d),-Mathf.Sin(d));r.Look=Quaternion.LookRotation(bowl,Vector3.back);}
            else r.Look=Quaternion.Euler(p.euler.x,p.euler.y*sign,p.euler.z*sign);
            r.Entry=r.Look*Quaternion.AngleAxis(sign*p.lookRollDegrees,Vector3.forward);
            var cone=Quaternion.AngleAxis(sign*p.spinPlaneDegrees,Vector3.right)*Vector3.forward;
            var coneEnd=Quaternion.AngleAxis(sign*p.turnDegrees,cone)*r.Look*Quaternion.AngleAxis(sign*360,Vector3.forward);
            var finalBowl=r.Look*Vector3.forward;finalBowl.x=-finalBowl.x;r.Poise=Quaternion.FromToRotation(coneEnd*Vector3.forward,finalBowl)*coneEnd;
            r.LightRoll=view.Item is Glass&&(r.Carbonated||r.Loaded&&InspectionPolicy.MaximumTilt(view,session)<p.swirlMinimumTilt);
            // Fit all moving pivots and near-tip foreshortening once, never chase an AABB.
            r.Depth=p.center.z;float tan=Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad);
            for(int k=0;k<=192;k++)
            {
                float t=r.Duration*k/192;PoseAt(r,view,hand.Side,t,out var offset,out var turn);
                for(int i=0;i<8;i++)
                {var q=offset+turn*(r.Bounds.center+Vector3.Scale(r.Bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));float pad=LongTool(view.Item)?.012f:.004f;
                    r.Depth=Mathf.Max(r.Depth,(Mathf.Abs(q.x)+pad)/(tan*camera.aspect*.94f)-q.z,(Mathf.Abs(q.y)+pad)/(tan*.94f)-q.z,camera.nearClipPlane+.035f-q.z);}
            }
            if(view.Item is BarSpoon)r.Tip=new InspectionTipFlex(view,hand.Side,r.Bounds);return r;
        }
        private Quaternion RotationAt(InspectionMotion r,ItemView view,HandSide side,float t)
        {
            var p=r.Profile;float sign=side==HandSide.Left?-1:1,look=Mathf.Clamp01(t/p.lookSeconds),play=t-p.lookSeconds,u=Mathf.Clamp01(play/p.playSeconds);var q=r.Look;var up=Quaternion.Inverse(player.ViewCamera.transform.rotation)*Vector3.up;
            if(view.Item is BarSpoon)
            {
                if(play<0)q=r.Look*Quaternion.AngleAxis(sign*p.lookRollDegrees*Ease(look),Vector3.forward);
                else
                {float wind=Smooth(play/p.windupSeconds),air=(play-p.windupSeconds)/p.airSeconds,s=Accent(air,p.spinBias);var axis=Quaternion.AngleAxis(sign*p.spinPlaneDegrees,Vector3.right)*Vector3.forward;
                    var spin=Quaternion.AngleAxis(sign*p.turnDegrees*s,axis)*r.Look*Quaternion.AngleAxis(sign*(p.lookRollDegrees+(360-p.lookRollDegrees)*s),Vector3.forward);
                    spin=Quaternion.AngleAxis(-p.windupDegrees*wind*(1-Smooth(air/.2f)),Vector3.right)*spin;q=Quaternion.SlerpUnclamped(spin,r.Poise,Smooth((air-.8f)/.2f));}
            }
            else if(view.Item is Bottle)
            {
                float spin=play<0?0:play<p.windupSeconds?-p.reverseDegrees*Smooth(play/p.windupSeconds):-p.reverseDegrees+(360+p.reverseDegrees)*Accent((play-p.windupSeconds)/p.spinSeconds,p.spinBias);
                float phase=Mathf.Clamp01((play-p.windupSeconds)/p.spinSeconds),precession=p.neckDegrees*Bump(phase),a=2*Mathf.PI*phase;
                q=Quaternion.AngleAxis(sign*(spin+p.glintDegrees*Bump(look)),up)*Quaternion.AngleAxis(-p.tiltDegrees*Bump(look)+precession*Mathf.Cos(a),Vector3.right)*Quaternion.AngleAxis(precession*Mathf.Sin(a),Vector3.forward)*q;
            }
            else if(view.Item is CobblerShaker)
            {
                float axial=sign*p.lookTurnDegrees*(Ease(look)-Ease(u)),mouth=r.Loaded&&!r.Sealed?p.tiltDegrees*Bump(u):0;q=Quaternion.AngleAxis(axial,up)*Quaternion.AngleAxis(-p.tiltDegrees*Bump(look)-mouth,Vector3.right)*q;
                // Bare body only: bring the mouth toward the eye during the existing look beat.
                // Loaded bodies still pass through the unchanged geometric spill limit below.
                if(play<0&&view.Item is CobblerShaker bare&&bare.State==ShakerState.Open&&p.openBodyViewDegrees>0)
                {
                    var axis=q*r.UpAxis;var eye=-new Vector3(sign*p.center.x,p.center.y,p.center.z).normalized;
                    float settle=Smooth(look/.42f)*(1-Smooth((look-.58f)/.42f));
                    float turn=Mathf.Max(0,Vector3.Angle(axis,eye)-p.openBodyViewDegrees)*settle;
                    q=Quaternion.FromToRotation(axis,Vector3.RotateTowards(axis,eye,turn*Mathf.Deg2Rad,0))*q;
                }
                float air=r.Sealed&&r.Loaded?p.sealedAirSeconds:p.airSeconds,s=Accent((play-p.windupSeconds)/air,p.spinBias);
                if(!r.Loaded)q=Quaternion.AngleAxis(p.turnDegrees*s,Quaternion.AngleAxis(p.spinPlaneDegrees,Vector3.forward)*Vector3.right)*q*Quaternion.AngleAxis(sign*p.axialRollDegrees*s,r.UpAxis);
                else if(r.Sealed)q=Quaternion.AngleAxis(sign*360*s,up)*q;
            }
            else if(view.Item is Glass)
            {
                float yaw=play<0?p.glintDegrees*Bump(look):r.LightRoll?sign*p.lightRollDegrees*Bump(u):0;
                float pitch=-p.tiltDegrees*Bump(look)+(r.LightRoll?p.breathDegrees:p.reboundDegrees)*Bump(u)*Mathf.Sin(2*Mathf.PI*(r.LightRoll?1:p.circleCount)*u);
                q=Quaternion.AngleAxis(yaw,up)*Quaternion.AngleAxis(pitch,Vector3.right)*q;
            }
            else if(view.Item is IceTongs||view.Item is IceScoop)
            {
                float air=(play-p.windupSeconds)/p.airSeconds,s=Accent(air,p.spinBias);var axis=Quaternion.AngleAxis(sign*p.spinPlaneDegrees,Vector3.forward)*Vector3.right;
                q=Quaternion.AngleAxis(sign*p.turnDegrees*s,axis)*r.Look*Quaternion.AngleAxis(sign*p.lookRollDegrees*Ease(look),Vector3.forward);
                if(play>=0&&air<0)q=Quaternion.AngleAxis(-p.windupDegrees*Bump(play/p.windupSeconds),Vector3.right)*q;
                if(view.Item is IceTongs)
                {float first=t-p.pinchStart-p.firstPinchSeconds,second=first-p.pinchPauseSeconds-p.secondPinchSeconds;float recoil=p.firstRecoilDegrees*Bump(first/p.recoilSeconds)+p.secondRecoilDegrees*Bump(second/p.recoilSeconds);q=Quaternion.AngleAxis(-recoil,Vector3.right)*q;}
            }
            else if(view.Item is Squeezer)
            {float wrist=r.Peek?p.glintDegrees*Mathf.Sin(Mathf.PI*Mathf.Clamp01(t/r.Duration)):p.wristDegrees*Bump(u);q=Quaternion.AngleAxis(sign*wrist,Vector3.forward)*Quaternion.AngleAxis(p.glintDegrees*Bump(look),Vector3.up)*q;}
            if(r.Loaded&&(view.Item is Bottle||InspectionPolicy.Container(view.Item)!=null))q=InspectionPolicy.LimitRotation(view,side,player.ViewCamera,q,InspectionPolicy.MaximumTilt(view,session));return q;
        }
        private Vector3 OffsetAt(InspectionMotion r,ItemView view,float t)
        {
            var p=r.Profile;float play=t-p.lookSeconds,u=Mathf.Clamp01(play/p.playSeconds),look=Mathf.Clamp01(t/p.lookSeconds);var offset=Vector3.zero;
            if(play<0){float drift=Ease(look),rise=LongTool(view.Item)?p.drawMetres*Mathf.Tan(p.diagonalDegrees*Mathf.Deg2Rad)*drift:p.drawMetres*.55f*Mathf.Sin(Mathf.PI*drift);offset=new Vector3(-p.drawMetres*drift,rise,-.008f*Bump(look));}
            else {offset.x=-p.drawMetres;if(LongTool(view.Item))offset.y=p.drawMetres*Mathf.Tan(p.diagonalDegrees*Mathf.Deg2Rad);}
            bool toss=!r.Peek&&(view.Item is IceTongs||view.Item is IceScoop||view.Item is CobblerShaker&&(!r.Loaded||r.Sealed));
            if(view.Item is BarSpoon&&play>=0)
            {float wind=Smooth(play/p.windupSeconds),air=Mathf.Clamp01((play-p.windupSeconds)/p.airSeconds);offset.z=-p.windupBackMetres*wind*(1-Ease(air));offset.x+=p.drawMetres*.5f*Bump(air);}
            else if(toss&&play>=0)
            {
                float air=r.Sealed&&r.Loaded?p.sealedAirSeconds:p.airSeconds,height=r.Sealed&&r.Loaded?p.sealedTossMetres:p.tossMetres,metres=r.Sealed&&r.Loaded?p.sealedCatchMetres:p.catchMetres,v=4*height/air;
                if(play<p.windupSeconds){float a=play/p.windupSeconds;offset.y+=v*p.windupSeconds*(a*a*a-a*a)-p.windupMetres*Bump(a);offset.z=-p.windupBackMetres*Bump(a);}
                else if(play<p.windupSeconds+air){float a=(play-p.windupSeconds)/air;offset.y+=4*height*a*(1-a);offset.x+=p.drawMetres*Bump(a);offset.z=-p.windupBackMetres*.5f*Bump(a);}
                else {float caught=play-p.windupSeconds-air;offset.y+=CatchGive(-v,caught,p)+Arrest(caught,p,metres);}
            }
            else if(view.Item is CobblerShaker&&r.Loaded&&play>=0)
            {float a=Mathf.PI*Ease(u);offset+=new Vector3(p.circleMetres*(Mathf.Cos(a)-1),p.raiseMetres*Bump(u),p.circleMetres*Mathf.Sin(a));}
            else if(view.Item is Bottle&&play>=0)
            {float spin=Mathf.Clamp01((play-p.windupSeconds)/p.spinSeconds),caught=play-p.windupSeconds-p.spinSeconds;offset.x+=p.drawMetres*.7f*Bump(spin);offset.y=caught>0?Arrest(caught,p,p.catchMetres):p.drawMetres*.45f*Bump(spin);}
            else if(view.Item is Glass&&play>=0)
            {if(r.LightRoll)offset.y=p.raiseMetres*Bump(u);else {float a=2*Mathf.PI*p.circleCount*Ease(u),radius=p.circleMetres*Bump(u);offset+=new Vector3(radius*(Mathf.Cos(a)-1),radius*Mathf.Sin(a),.004f*Bump(u));}}
            else if(view.Item is Squeezer){float phase=r.Peek?Mathf.Clamp01(t/r.Duration):u;offset.y=p.raiseMetres*Bump(phase);offset.x+=p.drawMetres*.5f*Bump(phase);}
            return offset;
        }
        private void PoseAt(InspectionMotion r,ItemView view,HandSide side,float t,out Vector3 position,out Quaternion rotation)
        {
            var p=r.Profile;float sign=side==HandSide.Left?-1:1,play=t-p.lookSeconds;rotation=RotationAt(r,view,side,t);var pivot=r.Pivot;var compensate=Vector3.zero;
            if(view.Item is BarSpoon&&play>=0)
            {
                float shift=Smooth(play/p.windupSeconds);pivot=Vector3.Lerp(r.Pivot,r.Golden,shift);compensate=r.Entry*(pivot-r.Pivot);float caught=play-p.windupSeconds-p.airSeconds;
                if(caught>=0){float recenter=Smooth(caught/p.pivotRecenterSeconds);pivot=Vector3.Lerp(r.Golden,Vector3.zero,recenter);compensate=Vector3.Lerp(r.Entry*(r.Golden-r.Pivot),r.Poise*(-r.Pivot),recenter);}
            }
            else if((view.Item is IceTongs||view.Item is IceScoop)&&play>=0)
            {var target=Vector3.Lerp(r.Pivot,Vector3.zero,p.pivotFraction);float caught=play-p.windupSeconds-p.airSeconds,shift=Smooth(play/p.windupSeconds)*(1-Smooth(caught/p.pivotRecenterSeconds));pivot=Vector3.Lerp(r.Pivot,target,shift);compensate=r.Entry*(pivot-r.Pivot);}
            var offset=OffsetAt(r,view,t);offset.x*=sign;position=new Vector3(sign*p.center.x,p.center.y,0)+offset+compensate-rotation*pivot;
        }
        /// <summary>when 的条件用 "+" 连接，全部满足才放（见 HoldInspectionSound）。r 为空时（量酒器）按空载、开口处理。</summary>
        private static bool SoundMatches(InspectionMotion r,ItemView view,string when)
        {
            var c=InspectionPolicy.Container(view.Item);bool ice=c?.Contents.IceCubes>0,liquid=view.Item is Bottle b?b.RemainingMl>1e-8:c?.Contents.LiquidMl>1e-8;
            bool loaded=r?.Loaded==true,sealedTin=r?.Sealed==true,peek=r?.Peek==true,lightRoll=r?.LightRoll==true;
            foreach(var token in (when??"any").Split('+'))
            {
                bool ok=token switch
                {
                    "any"=>true,"empty"=>!loaded,"loaded"=>loaded,"sealed"=>sealedTin,"open"=>!sealedTin,"ice"=>ice,"dry"=>!ice,"liquid"=>liquid,"peek"=>peek,"toolEmpty"=>!peek,"swirl"=>!lightRoll,"lightRoll"=>lightRoll,
                    "sealedLiquid"=>loaded&&sealedTin&&!ice,"sealedIce"=>loaded&&sealedTin&&ice,"openIce"=>loaded&&!sealedTin&&ice,"openLiquid"=>loaded&&!sealedTin&&!ice,_=>false
                };
                if(!ok)return false;
            }
            return true;
        }
        /// <summary>按内容缩放音量：冰按块数（4 块为满）；瓶里的液体按液面上方的空气（半瓶最响，满瓶留 0.3）；杯/壶里的液体按装了多少。</summary>
        private static float SoundScale(Item item,string scale)
        {
            if(string.IsNullOrEmpty(scale))return 1;var c=InspectionPolicy.Container(item);
            if(scale=="ice")return c==null?0:Mathf.Sqrt(Mathf.Clamp01((float)(c.Contents.IceCubes/4.0)));
            if(scale!="liquid")return 1;
            if(item is Bottle b){float f=(float)(b.RemainingMl/Math.Max(1e-6,b.CapacityMl));return f<=0?0:Mathf.Sqrt(Mathf.Max(.09f,4*f*(1-f)));}
            return c==null?0:Mathf.Sqrt(Mathf.Clamp01((float)(c.Contents.LiquidMl/Math.Max(1e-6,c.CapacityMl))*3));
        }
        private void InspectionSounds(Hand hand,HoldInspectionSound[] sounds,InspectionMotion r,ItemView view,float t,Vector3 position)
        {
            for(int i=0;i<sounds.Length&&i<31;i++)
            {if((hand.SoundMask&(1<<i))!=0)continue;var cue=sounds[i];float cueTime=InspectionContactTiming.Time(cue,settings.inspect,r?.Profile);if(t<cueTime)continue;hand.SoundMask|=1<<i;if(!SoundMatches(r,view,cue.when)||session.Sound?.Gate?.Unlocked!=true)continue;
                float gain=cue.gain*SoundScale(view.Item,cue.scale);if(gain<=0)continue;
                session.Sound.Pool.Play("inspect:"+hand.InspectEvent?.activationId+":"+view.InstanceId+":"+i,cue.bank,view.InstanceId,player.ViewCamera.transform.TransformPoint(position),gain,cue.pitch,group:"inspect",variant:cue.variant,scheduledDsp:Math.Max(AudioSettings.dspTime+.001,hand.SoundDsp+cueTime),applyCooldown:false);}
        }
        private void InspectGeneral(Hand hand,ItemView view,float dt,ref Vector3 position,ref Quaternion rotation)
        {
            if(hand.Motion==null)hand.Motion=BeginGeneral(hand,view,rotation);var r=hand.Motion;var p=r.Profile;hand.Inspect+=dt;float t=hand.Inspect;
            if(t>=r.Duration){hand.Inspect=r.Duration;FinishInspection(hand,"completed",null);return;}
            float exit=r.Peek?r.Duration-p.returnSeconds:p.lookSeconds+p.playSeconds+p.settleSeconds,weight=Smooth(t/p.liftSeconds)*(1-Smooth((t-exit)/p.returnSeconds));
            PoseAt(r,view,hand.Side,t,out var focus,out var target);focus.z+=r.Depth;position=Vector3.LerpUnclamped(position,focus,weight);rotation=Quaternion.SlerpUnclamped(rotation,target,weight);
            if(r.Loaded&&(view.Item is Bottle||InspectionPolicy.Container(view.Item)!=null))rotation=InspectionPolicy.LimitRotation(view,hand.Side,player.ViewCamera,rotation,InspectionPolicy.MaximumTilt(view,session));
            if(r.Tongs)
            {float first=(t-p.pinchStart)/p.firstPinchSeconds,second=(t-p.pinchStart-p.firstPinchSeconds-p.pinchPauseSeconds)/p.secondPinchSeconds;float degrees=p.lightPinchDegrees*Bump(first)+p.deepPinchDegrees*Bump(second);r.Tongs.SetInspection(.5f*degrees*weight);}
            if(r.Squeezer)
            {
                if(r.Peek){float opening=Smooth((t-p.peekOpenStart)/p.peekOpenSeconds)*(1-Smooth((t-p.peekCloseStart)/p.peekCloseSeconds));r.Squeezer.SetPeekInspection(p.peekDegrees*opening*weight);}
                else {float play=t-p.lookSeconds,amount=1;if(play>=0&&play<p.firstClapSeconds)amount=1-Accent(play/p.firstClapSeconds,p.spinBias);
                    else if(play>=p.firstClapSeconds&&play<p.firstClapSeconds+p.halfOpenSeconds)amount=.5f*Smooth((play-p.firstClapSeconds)/p.halfOpenSeconds);
                    else if(play>=p.firstClapSeconds+p.halfOpenSeconds)amount=.5f*(1-Accent((play-p.firstClapSeconds-p.halfOpenSeconds)/p.secondClapSeconds,p.spinBias));r.Squeezer.SetInspection(Mathf.Lerp(1,amount,weight));}
            }
            if(r.Tip!=null)
            {float caught=t-p.lookSeconds-p.windupSeconds-p.airSeconds,a=caught>=0&&caught<p.ringSeconds?p.ringDegrees*Mathf.Exp(-p.ringDecay*caught)*(1-Smooth(caught/p.ringSeconds)):0;r.Tip.Apply(new Vector3(a*Mathf.Sin(2*Mathf.PI*p.ringHz*caught),a*.65f*Mathf.Sin(2*Mathf.PI*p.ringHz*caught+.35f),0));}
            InspectionSounds(hand,r.Profile.sounds,r,view,t,position+rotation*r.Pivot);
        }
    }
}
