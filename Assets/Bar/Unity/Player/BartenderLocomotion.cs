using System;
using System.Collections.Generic;
using Bar.Domain;
using Bar.Logging;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>Ginza movement coordinator. Reads vessel snapshots; never edits the crafting model.</summary>
    public sealed class BartenderLocomotion:MonoBehaviour
    {
        public TextAsset Configuration;public BartenderSpatialLayout Layout;public BartenderFootsteps Footsteps;
        private BartenderLocomotionSettings p;private BartenderLocomotionModel model;private readonly BartenderStride stride=new BartenderStride();
        private BarSession session;private PlayerController player;private CharacterController body;private HoldViewRig holdRig;private readonly HashSet<Collider> obstacles=new HashSet<Collider>();private readonly RaycastHit[] hits=new RaycastHit[32];private int mask;
        private SecondOrderFloat bobAmount,gaze;private SecondOrderVector handBob;private float vertical,gazeHold;private bool initialized,wasCareful,wasLounge;private ActiveAction stationAction;private Vector3 actionOrigin;private bool cancelQueued;
        public Vector3 CameraOffset{get;private set;}public Vector3 HandOffset{get;private set;}public float ActualSpeed{get;private set;}public bool Careful=>model.Careful;public float LoungeBlend=>model.LoungeBlend;public float StepPhase=>stride.Phase;public int FootstepsEmitted{get;private set;}
        public void Initialize(BarSession owner,CharacterController controller)
        {
            if(initialized)throw new InvalidOperationException("Locomotion initialized twice");if(!Configuration||!Layout||!Footsteps||!controller)throw new InvalidOperationException("Locomotion references must be assigned in scene");
            p=JsonUtility.FromJson<BartenderLocomotionSettings>(Configuration.text);p.Validate();foreach(var f in typeof(BartenderLocomotionSettings).GetFields())if(!Configuration.text.Contains("\""+f.Name+"\""))throw new InvalidOperationException("Missing locomotion parameter "+f.Name);
            Layout.Validate();session=owner;player=owner.Player;body=controller;holdRig=owner.GetComponent<HoldViewRig>();if(!holdRig)throw new InvalidOperationException("Held-item presentation must initialize before locomotion");model=new BartenderLocomotionModel(p);
            foreach(var c in Layout.Furniture)obstacles.Add(c);foreach(var view in owner.Views)if(view.Item is Fixture)foreach(var c in view.GetComponentsInChildren<Collider>())if(!c.isTrigger)obstacles.Add(c);
            mask=~PlayerEnvironmentSettings.Load().Mask;bobAmount=new SecondOrderFloat(5,1,0,0);gaze=new SecondOrderFloat(5,1,0,1);handBob=new SecondOrderVector(6,1,0,Vector3.zero);
            if(p.footstepsEnabled)Footsteps.Initialize(owner,p);initialized=true;
        }
        private static Container OpenContainer(Item item)=>item is Jigger j?j.ActiveCup:item is CobblerShaker s&&s.State==ShakerState.Open?s.Body:item as Container;
        private float Fill(Item item){var c=OpenContainer(item);return c!=null&&c.IsOpenTop?(float)(c.Contents.LiquidMl/c.CapacityMl):0;}
        public void Step(Vector2 input,float dt)
        {
            if(!initialized)throw new InvalidOperationException("Locomotion used before explicit initialization");if(dt==0)return;if(dt<0||!float.IsFinite(dt))throw new ArgumentException("Invalid locomotion delta");
            bool blocked=session.OrderInputBlocked||session.HudSuspended||!session.Sound.Gate.Unlocked;
            bool action=session.StationShuffleAllowed;bool stopped=blocked||(session.LookLocked&&!action);if(stopped)input=Vector2.zero;
            model.SetCareful(Mathf.Max(Fill(session.Hands.Left),Fill(session.Hands.Right)));bool intention=input.sqrMagnitude>.0001f;
            var heading=Quaternion.Euler(0,player.LookRotation.eulerAngles.y,0);var before=transform.position;
            var velocity=model.Advance(input,heading,Layout.LoungeWeight(before),action,dt);
            if(stopped){model.Stop();velocity=Vector3.zero;}
            // CharacterController owns contact and wall sliding; no pre-contact velocity veto.
            var env=PlayerEnvironmentSettings.Load();if(body.isGrounded&&vertical<0)vertical=-.5f;vertical=Mathf.Max(-15,vertical-env.gravity*dt);
            var flags=body.Move((velocity+Vector3.up*vertical)*dt);var delta=transform.position-before;delta.y=0;ActualSpeed=delta.magnitude/dt;
            if((flags&CollisionFlags.Sides)!=0&&ActualSpeed<velocity.magnitude*.3f)model.CollisionVelocity(delta/dt);
            bool grounded=body.isGrounded||(flags&CollisionFlags.Below)!=0;
            stride.Advance(delta.magnitude,intention&&!stopped,grounded,ActualSpeed,Mathf.Lerp(p.workStride,p.loungeStride,model.LoungeBlend),dt,p);
            if(p.footstepsEnabled){Footsteps.BeginFrame();if(session.Sound.Gate.Unlocked){for(int i=0;i<stride.Steps;i++){Footsteps.Play(Layout.OnRubber(transform.position),false,transform.position);FootstepsEmitted++;}if(stride.Shuffle)Footsteps.Play(Layout.OnRubber(transform.position),true,transform.position);}Footsteps.EndFrame();}
            bool aimed=session.Target&&session.Target.Item is not Fixture&&Vector3.Distance(player.ViewCamera.transform.position,session.HudTargetPoint)<=p.gazeDistance;
            if(aimed||(model.LoungeBlend<.1f&&player.Pitch>p.gazePitch))gazeHold=.14f;else gazeHold=Mathf.Max(0,gazeHold-dt);
            bool inspecting=holdRig.Inspecting(Bar.Data.HandSide.Left)||holdRig.Inspecting(Bar.Data.HandSide.Right);
            float attention=gaze.Update(dt,gazeHold>0?p.gazeBobScale:1),amount=bobAmount.Update(dt,!stopped&&!action&&!inspecting?Mathf.Clamp01(ActualSpeed/.6f):0);
            var wave=new Vector3(Mathf.Sin(stride.Phase),-.5f*(1+Mathf.Cos(2*stride.Phase)),0);
            var cameraBob=Vector3.up*(wave.y*p.cameraVertical*amount*attention);
            var offset=SafeEyeOffset(cameraBob,heading);
            CameraOffset=offset;player.SetLocomotionPose(offset,0);
            handBob.Reconfigure(6,model.Careful?p.carryZeta:1,0);var fullHand=new Vector3(wave.x*p.handSide,wave.y*p.handVertical,0)*amount;
            HandOffset=handBob.Update(dt,fullHand)-Quaternion.Inverse(player.ViewCamera.transform.rotation)*(heading*cameraBob);
            HandleStationLeash(action);LogTransitions();
        }
        private bool Probe(Vector3 origin,Vector3 direction,float radius,float range,out RaycastHit nearest)
        {
            int n=Physics.SphereCastNonAlloc(origin,radius,direction,hits,range,mask,QueryTriggerInteraction.Ignore);if(n==hits.Length)throw new InvalidOperationException("Locomotion feeler buffer exhausted");float d=range+1;nearest=default;bool found=false;
            for(int i=0;i<n;i++)if(obstacles.Contains(hits[i].collider)&&Mathf.Abs(hits[i].normal.y)<.8f&&hits[i].distance<d){d=hits[i].distance;nearest=hits[i];found=true;}return found;
        }
        private Vector3 SafeEyeOffset(Vector3 offset,Quaternion heading)
        {
            if(offset.sqrMagnitude<.0000001f)return offset;var world=heading*offset;
            if(Probe(player.BaseEyeWorld,world.normalized,.06f,world.magnitude,out var hit))return offset*Mathf.Max(0,(hit.distance-.005f)/world.magnitude);return offset;
        }
        private void HandleStationLeash(bool allowed)
        {
            var active=session.Clock.Active;if(!allowed){stationAction=null;cancelQueued=false;return;}
            if(!ReferenceEquals(active,stationAction)){stationAction=active;actionOrigin=transform.position;cancelQueued=false;}
            if(!cancelQueued&&BartenderLocomotionModel.LeftStation(actionOrigin,transform.position,p.actionLeash)){cancelQueued=true;session.LeaveStation(active,session.EventTime(Time.realtimeSinceStartupAsDouble));}
        }
        private void LogTransitions()
        {
            bool lounge=model.LoungeBlend>=.5f;if(wasCareful!=model.Careful||wasLounge!=lounge){session.Adapter.Publish("view_locomotion_state",new LogRecord{reason=p.version,outcome=(lounge?"lounge":"work")+":"+(model.Careful?"careful":"normal")});wasCareful=model.Careful;wasLounge=lounge;}
        }
    }
}
