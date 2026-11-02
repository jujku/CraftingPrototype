using System;
using System.Linq;
using Bar.Domain;
using Bar.Data;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>先从杯口上方入勺，瓶手随后跟随；复用二阶弹簧，退勺先抬过杯口。</summary>
    public sealed class GuidedPourPose
    {
        private readonly ItemView bottle,spoon,receiver;private readonly Transform tip,grip,camera;
        private readonly ContainerVisual binding;private readonly GuidedPourSettings p;private readonly Controls controls;
        private readonly TiltFlowModel flowModel;private readonly float bottleHandSign;
        private readonly Vector3 startTip,startLip;private readonly Quaternion startSpoon;
        private readonly Vector3 spoonAxis;private readonly SecondOrderVector tipSpring,lipSpring;private readonly SecondOrderFloat turnSpring,tiltSpring;
        private readonly double createdAt;
        private float elapsed,returnTime;private bool returning,returnSample,firstAdvance=true;private Vector3 lastTip,lastLip,exitTip,goalTip,goalLip,returnTip,returnLip;private Quaternion lastSpoon,lastBottle,returnSpoon,returnBottle;
        public Vector3 Contact {get;private set;}public Vector3 FilmEnd {get;private set;}public Vector3 TipPosition=>tip.position;
        public bool Ready=>elapsed>=controls.GuidedPrepareSeconds+controls.GuidedFollowSeconds&&Vector3.Distance(lastTip,goalTip)<.005f&&Vector3.Distance(lastLip,goalLip)<.005f;
        private static float Ease(float t){t=Mathf.Clamp01(t);return t*t*t*(10+t*(-15+6*t));}
        public GuidedPourPose(BarSession session,ItemView bottle,ItemView receiver)
        {
            createdAt=Time.realtimeSinceStartupAsDouble;this.bottle=bottle;this.receiver=receiver;camera=session.Player.ViewCamera.transform;controls=session.GestureControls;controls.ValidateGuided();
            var side=session.Hands.SideOf(bottle.Item).Value;var item=session.Hands.Get(side==HandSide.Left?HandSide.Right:HandSide.Left);
            bottleHandSign=side==HandSide.Right?1:-1;flowModel=new TiltFlowModel(controls);
            spoon=session.Views.Single(v=>ReferenceEquals(v.Item,item));tip=spoon.Anchor("Tip");grip=HoldViewRig.Grip(spoon,session.Hands.SideOf(item).Value);
            if(!tip||!grip)throw new InvalidOperationException("Guided pour: configured spoon Tip and hand Grip required");
            binding=InspectionPolicy.Binding(receiver);if(!binding||binding.Shape==null)throw new InvalidOperationException("Guided pour: highball cavity required");
            p=PourSettings.Load().guided;if(p==null||p.frequency<=0||p.returnSeconds<=0||p.contactFraction<=0||p.contactFraction>=1||p.zeta<=0)throw new InvalidOperationException("Guided pour settings missing/invalid");
            startTip=tip.position;startLip=bottle.PourPoint;startSpoon=spoon.transform.rotation;spoonAxis=(grip.position-tip.position).normalized;
            lastTip=startTip;lastLip=startLip;lastSpoon=startSpoon;lastBottle=bottle.transform.rotation;
            if((grip.position-tip.position).sqrMagnitude<.0001f)throw new InvalidOperationException("Guided pour shaft has invalid length");
            tipSpring=new SecondOrderVector(p.frequency,p.zeta,0,startTip);lipSpring=new SecondOrderVector(p.frequency,p.zeta,0,startLip);turnSpring=new SecondOrderFloat(p.frequency,p.zeta,0,0);tiltSpring=new SecondOrderFloat(p.frequency,p.zeta,0,0);
        }
        public PourStreamInput Step(float dt,float angle,float flow,Container target)
        {
            if(firstAdvance&&dt>0){dt=Mathf.Min(dt,(float)(Time.realtimeSinceStartupAsDouble-createdAt));firstAdvance=false;}
            elapsed+=dt;PourPose.Mouth(receiver,target,out var mouth,out float radius,out float plane);
            var bodySide=Vector3.ProjectOnPlane(camera.right,Vector3.up).normalized*bottleHandSign;
            if(bodySide.sqrMagnitude<.5f)throw new InvalidOperationException("Guided pour requires a horizontal camera right axis");
            var tiltAxis=Vector3.Cross(bodySide,Vector3.up);
            float depth=Mathf.Min(p.tipDepth,Mathf.Max(0,mouth.y-plane)*.65f);
            var axis=Quaternion.AngleAxis(p.tiltDegrees,tiltAxis)*Vector3.up;
            var targetTip=mouth+bodySide*(radius*.55f)-Vector3.up*depth;goalTip=targetTip;var above=mouth+Vector3.up*p.entryClearance;
            float prepare=(float)controls.GuidedPrepareSeconds;
            Vector3 desired=elapsed<prepare*.55f?Vector3.Lerp(startTip,above,Ease(elapsed/(prepare*.55f))):Vector3.Lerp(above,targetTip,Ease((elapsed-prepare*.55f)/(prepare*.45f)));
            Quaternion wanted=Quaternion.FromToRotation(spoonAxis,axis)*startSpoon;
            float rotation=Advance(turnSpring,dt,1);Vector3 shown=Advance(tipSpring,dt,desired);
            spoon.transform.rotation=Quaternion.Slerp(startSpoon,wanted,rotation);spoon.transform.position+=shown-tip.position;
            lastTip=shown;lastSpoon=spoon.transform.rotation;
            Contact=tip.position+(grip.position-tip.position)*p.contactFraction;
            var finalContact=targetTip+axis*(Vector3.Distance(grip.position,tip.position)*p.contactFraction);
            if(finalContact.y<mouth.y+.004f)throw new InvalidOperationException("Guided pour contact is below rim; check shaft geometry/contactFraction");
            float follow=Ease((elapsed-(float)controls.GuidedFollowSeconds)/prepare);
            float displayedAngle=Advance(tiltSpring,dt,angle*follow);var bottleAxis=PourPose.Axis(bottle);var upright=Quaternion.FromToRotation(bottleAxis,Vector3.up)*bottle.transform.rotation;
            bottle.transform.rotation=Quaternion.AngleAxis(displayedAngle,tiltAxis)*upright;
            var source=(Bottle)bottle.Item;
            float entryAngle=(float)flowModel.StartAngle(source.RemainingMl,source.CapacityMl,bottle.DefinitionId);
            float sideWeight=PourPose.EntrySideWeight(displayedAngle,entryAngle);
            float separation=PourPose.EntrySideDistance(bottle,radius);
            var guidedLip=Contact+Vector3.up*p.mouthGap;
            float minimumSide=Mathf.Lerp(Vector3.Dot(guidedLip-mouth,bodySide),separation,sideWeight);
            Vector3 desiredLip=guidedLip+bodySide*Mathf.Max(0,minimumSide-Vector3.Dot(guidedLip-mouth,bodySide));goalLip=desiredLip;
            var lip=Advance(lipSpring,dt,Vector3.Lerp(startLip,desiredLip,follow));
            // The same upright wait-beside clearance must also constrain spring lag when lowering the bottle.
            lip+=bodySide*Mathf.Max(0,minimumSide-Vector3.Dot(lip-mouth,bodySide));
            bottle.transform.position+=lip-bottle.PourPoint;lastLip=lip;lastBottle=bottle.transform.rotation;
            FilmEnd=tip.position;if(FilmEnd.y<plane){float a=(Contact.y-plane)/(Contact.y-FilmEnd.y);FilmEnd=Vector3.Lerp(Contact,FilmEnd,a);}
            var velocity=Vector3.down*.2f;
            if(flow>0){if(!PourTrajectory.PlaneTime(bottle.PourPoint,velocity,Contact.y,PourSettings.Load().gravity,out float flight))throw new InvalidOperationException("Guided bottle mouth must be above shaft contact");var lateral=Vector3.ProjectOnPlane(Contact-bottle.PourPoint,Vector3.up);velocity+=lateral/flight;}
            var input=new PourStreamInput{Outlet=new Pose(bottle.PourPoint,Quaternion.identity),ExitVelocity=velocity,ConfirmedMlPerSecond=flow,LiquidPlaneY=plane,MouthCenter=mouth,MouthRadius=radius,Guided=true,GuideContact=Contact,GuideEnd=FilmEnd};
            PourPose.SetImpactBoundary(ref input,binding);return input;
        }
        private static Vector3 Advance(SecondOrderVector spring,float dt,Vector3 target){while(dt>0){float h=Mathf.Min(dt,1f/240);spring.Update(h,target);dt-=h;}return spring.Value;}
        private static float Advance(SecondOrderFloat spring,float dt,float target){while(dt>0){float h=Mathf.Min(dt,1f/240);spring.Update(h,target);dt-=h;}return spring.Value;}
        public bool Return(float dt)
        {
            if(dt==0&&returnSample){spoon.transform.rotation=returnSpoon;spoon.transform.position+=returnTip-tip.position;bottle.transform.rotation=returnBottle;bottle.transform.position+=returnLip-bottle.PourPoint;return returnTime>=p.returnSeconds;}
            if(!returning){returning=true;exitTip=lastTip+Vector3.up*Mathf.Max(0,binding.transform.TransformPoint(new Vector3(binding.Shape.Axis.x,binding.Shape.Top,binding.Shape.Axis.y)).y+p.entryClearance-lastTip.y);}
            returnTime+=dt;float u=returnTime/p.returnSeconds;Vector3 homeTip=tip.position,homeLip=bottle.PourPoint;Quaternion homeSpoon=spoon.transform.rotation,homeBottle=bottle.transform.rotation;
            Vector3 next=u<.4f?Vector3.Lerp(lastTip,exitTip,Ease(u/.4f)):Vector3.Lerp(exitTip,homeTip,Ease((u-.4f)/.6f));
            spoon.transform.rotation=Quaternion.Slerp(lastSpoon,homeSpoon,Ease((u-.4f)/.6f));spoon.transform.position+=next-tip.position;
            bottle.transform.rotation=Quaternion.Slerp(lastBottle,homeBottle,Ease(u));bottle.transform.position+=Vector3.Lerp(lastLip,homeLip,Ease(u))-bottle.PourPoint;
            returnSample=true;returnTip=tip.position;returnLip=bottle.PourPoint;returnSpoon=spoon.transform.rotation;returnBottle=bottle.transform.rotation;
            return u>=1;
        }
    }
}
