using System;
using System.Collections.Generic;
using Bar.Domain;
using Bar.Logging;
using CraftingResearch.CoreV2;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>只读碰撞/Domain/已显示水柱。预绑定组件和播放键，冲程及碰撞排期驱动录音。</summary>
    public sealed class BSoundDirector:MonoBehaviour
    {
        private sealed class Binding{internal ItemView View;internal Container Container;internal ContainerVisual Visual;internal Vessel Core;internal BubbleStreams Bubbles;internal string Id,ImpactWeak,ImpactStrong,Bright,Dull,Slosh,Stir,Fizz,StrokeBright,StrokeDull;internal double DspAnchor=double.NaN,LastLanding=double.NegativeInfinity;internal int Reversal,Catch,Clink;}
        private readonly List<Binding> bindings=new List<Binding>();private readonly Dictionary<string,ItemView> views=new Dictionary<string,ItemView>();private readonly Queue<(Binding binding,IceImpact hit)> pending=new Queue<(Binding,IceImpact)>();private readonly HashSet<string> emitted=new HashSet<string>();
        private BarSession session;private BarAudioCatalog p;private BubbleDisplaySettings bubbles;private bool wasPouring;private Vector3 lastLanding;private string lastTarget;
        private readonly string[,] pourKeys=new string[3,5],pourBanks=new string[3,5],metalPourKeys=new string[3,5],metalPourBanks=new string[3,5];private bool lastMetal;private static readonly string[] PlaceKey={"action:place","action:cap","action:hiss","action:discard"};
        public SampledAudioPool Pool{get;private set;}public AudioStartGate Gate{get;private set;}public bool PourAudible{get;private set;}public float PourPitch{get;private set;}
        public void Initialize(BarSession owner)
        {
            session=owner;p=BarAudioCatalog.Load();bubbles=BPresentationSettings.Load().bubbles;Gate=gameObject.AddComponent<AudioStartGate>();
            Pool=new SampledAudioPool(transform,owner.Player.ViewCamera.transform,p,(vessel,sound)=>{emitted.Add(vessel??"");session.Adapter.Publish("presentation_sound",new LogRecord{targetId=vessel,sound=sound});});
            for(int f=0;f<3;f++)for(int k=0;k<5;k++){pourKeys[f,k]="pour:"+f+":"+k;pourBanks[f,k]="pour"+f+"Fill"+k;metalPourKeys[f,k]="pourMetal:"+f+":"+k;metalPourBanks[f,k]="pourMetal"+f+"Fill"+k;}
            foreach(var view in session.Views){views[view.InstanceId]=view;foreach(var visual in view.GetComponentsInChildren<ContainerVisual>())
            {
                var c=view.Item is CobblerShaker sh?sh.Body:view.Item is Jigger j?(visual.DefinitionId==j.CupA.Definition.Id?j.CupA:j.CupB):view.Item as Container;if(c==null)continue;
                var b=new Binding{View=view,Container=c,Visual=visual,Id=c.CoreVesselId,Bubbles=visual.GetComponent<BubbleStreams>(),Core=session.Adapter.World.GetVessel(c.CoreVesselId)};
                b.ImpactWeak=b.Id+":impactWeak";b.ImpactStrong=b.Id+":impactStrong";b.Bright=b.Id+":bright";b.Dull=b.Id+":dull";b.Slosh=b.Id+":slosh";b.Stir=b.Id+":stir";b.Fizz=b.Id+":fizz";b.StrokeBright=b.Id+":strokeBright";b.StrokeDull=b.Id+":strokeDull";bindings.Add(b);
                var rig=visual.GetComponent<LocalIceWorld>();if(rig)rig.Impacts.Emitted+=hit=>{pending.Enqueue((b,hit));while(pending.Count>p.poolSize*p.iceConcurrency)pending.Dequeue();};
            }}session.Adapter.Events.Published+=Observe;
        }
        private void Observe(DomainEvent e)
        {
            if(!Gate.Unlocked||e.Observation is not LogRecord r||e.Kind!="action_performed"&&e.Kind!="tool_ice_discarded"||r.actorId==null||!views.TryGetValue(r.actorId,out var view))return;
            string action=r.actionId??"",bank=null,key=null;float gain=p.actionGain;
            if(action=="item.place"){bank="place";key=PlaceKey[0];gain=p.placeGain;}
            else if(action=="bottle.open"&&view.Item is Bottle bottle&&(bottle.IngredientId=="tonic"||bottle.IngredientId=="soda")){bank="bottleHiss";key=PlaceKey[2];gain=.4f;}
            else if(action=="shaker.detach_strainer"){bank="shakerRelease";key=PlaceKey[1];}
            else if(action.Contains("attach")||action.Contains("detach")||action.EndsWith(".open")||action.EndsWith(".close")){bank="metal";key=PlaceKey[1];}
            else if(e.Kind=="tool_ice_discarded"){bank="iceMetal";key=PlaceKey[3];}
            if(bank!=null)Pool.Play(key,bank,r.actorId,view.transform.position,gain,scheduledDsp:AudioSettings.dspTime+p.scheduleSeconds);
        }
        public bool Emitted(string vessel)=>emitted.Contains(vessel);
        public static bool IceAudible(BarAudioCatalog p,bool sealedShaker,bool shaking,bool landing)=>sealedShaker?!p.sealedIceOnlyWhileShaking||shaking:p.openIceImpactSounds||p.iceLandingSounds&&landing;
        public static float ImpactGain(float impulse,float minimum,float full)=>Mathf.Pow(10,(-30+30*Mathf.Clamp01(Mathf.Log(Mathf.Max(minimum,impulse)/minimum)/Mathf.Log(full/minimum)))/20);
        public void Present()
        {
            if(Pool==null)return;emitted.Clear();Pool.BeginFrame();PourAudible=false;if(!Gate.Unlocked){pending.Clear();Pool.Stop();wasPouring=false;return;}
            double now=AudioSettings.dspTime;
            // Align each batch's earliest late event once, then preserve hit.Time differences.
            foreach(var b in bindings){float earliest=float.MaxValue;foreach(var e in pending)if(e.binding==b)earliest=Mathf.Min(earliest,e.hit.Time);if(earliest<float.MaxValue){if(double.IsNaN(b.DspAnchor))b.DspAnchor=now+p.scheduleSeconds-earliest;else b.DspAnchor+=Math.Max(0,now+p.scheduleSeconds-(b.DspAnchor+earliest));}}
            while(pending.Count>0)
            {
                var e=pending.Dequeue();var b=e.binding;var hit=e.hit;float gain=ImpactGain(hit.Impulse,p.impactMinimum,p.impactFull),mix=Mathf.Clamp01((hit.Impulse-p.impactMinimum)/(p.impactFull-p.impactMinimum));
                bool metal=b.Container is ShakerBody;bool sealedShaker=b.View.Item is CobblerShaker s&&s.State==ShakerState.Sealed;
                // User 2026-10-27: only two ice sounds stay: each cube's landing when it is put into an open vessel,
                // and the hits inside the sealed shaker while it is actually being shaken (not when the cap goes on or the tin is carried).
                if(!IceAudible(p,sealedShaker,sealedShaker&&session.Adapter.Agitation(b.Id,.15)>0,hit.Landing))continue;if(!sealedShaker)gain*=p.iceLandingGain;
                string bank=hit.Kind==IceImpactKind.Ice&&!hit.Landing?"iceIce":metal?(sealedShaker?"iceSealed":"iceMetal"):"ice";
                float pitch=metal?1:Mathf.Lerp(p.smallIcePitch,p.largeIcePitch,Mathf.Clamp01((float)b.Core.ice/150));double start=b.DspAnchor+hit.Time;
                // cubes landing in the same vessel
                // within landingSpacingSeconds make one sound, and each hit is one voice (weak or heavy sample by impulse) instead of a weak+strong pair,
                // so a scoop of ice no longer runs out of voices and cuts the earlier hits off.
                if(hit.Landing){if(start-b.LastLanding<p.landingSpacingSeconds)continue;b.LastLanding=start;}
                Pool.Play(b.ImpactWeak,bank=="ice"&&mix>=.5f?"iceHeavy":bank,b.Id,hit.Position,gain,pitch,group:"ice",scheduledDsp:start,applyCooldown:false);
            }
            var presentation=session.PourDisplay?.Presentation;bool pouring=presentation?.Visible==true&&presentation.LastInput.ConfirmedMlPerSecond>0;
            if(pouring)
            {
                var input=presentation.LastInput;var target=(presentation.DisplayReceiver?.Item as ILiquidReceiver)?.ReceivingContainer;Binding binding=null;foreach(var b in bindings)if(b.Container==target){binding=b;break;}
                string id=binding?.Id??"sink";float fill=binding?.Visual.Profile?(float)(binding.Visual.Profile.VolumeBelowHeight(binding.Visual.HeightForLiquid(target.Contents.LiquidMl))/binding.Visual.Profile.CapacityMl):0;
                float ff=Mathf.Clamp01(fill)*4;int low=Mathf.FloorToInt(ff),high=Mathf.Min(4,low+1);float blend=ff-low,q=Mathf.Clamp01(input.ConfirmedMlPerSecond/p.flowFullMlPerSecond)*2;
                lastLanding=input.HasImpactBoundary?input.ImpactCenter:new Vector3(input.MouthCenter.x,input.LiquidPlaneY,input.MouthCenter.z);lastTarget=id;PourPitch=Mathf.Lerp(p.pourPitchLow,p.pourPitchHigh,fill);
                lastMetal=target is ShakerBody||target is JiggerCup;var keys=lastMetal?metalPourKeys:pourKeys;var banks=lastMetal?metalPourBanks:pourBanks;
                if(!wasPouring)Pool.Play("pour:start",lastMetal?"pourMetalStart":"pourStart",id,lastLanding,.4f,scheduledDsp:now+p.scheduleSeconds);
                for(int f=0;f<3;f++){float flowGain=Mathf.Sqrt(Mathf.Clamp01(1-Mathf.Abs(q-f)));if(flowGain<=0)continue;float baseGain=Mathf.Lerp(p.pourLowFlowGain,p.pourThickGain,Mathf.Sqrt(q*.5f))*flowGain;
                    PourAudible|=Pool.Play(keys[f,low],banks[f,low],id,lastLanding,baseGain*Mathf.Cos(blend*Mathf.PI*.5f),PourPitch,true,"pour",phase:.25f);
                    if(high!=low)PourAudible|=Pool.Play(keys[f,high],banks[f,high],id,lastLanding,baseGain*Mathf.Sin(blend*Mathf.PI*.5f),PourPitch,true,"pour",phase:.25f);}
                if(presentation.EntryLossPerSecond>0){var g=PourSettings.Load().guided;float gain=g.directFizzGain*Mathf.Sqrt(Mathf.Clamp01((float)presentation.EntryLossPerSecond/g.fullFizzLossGramsPerSecond));Pool.Play("pour:entry-fizz",target.Contents.IceCubes>0?"sodaIce":"soda",id,lastLanding,gain,1,true,"fizz");}
            }
            else if(wasPouring)Pool.Play("pour:drip",lastMetal?"pourMetalDrip":"pourDrip",lastTarget,lastLanding,.35f,scheduledDsp:now+p.scheduleSeconds);
            wasPouring=pouring;
            foreach(var b in bindings)
            {
                var v=b.Core;float intensity=(float)session.Adapter.Agitation(b.Id,.15),mass=Mathf.Clamp01((float)v.ice/p.iceMassFullGrams);var position=b.View.transform.position;
                if(b.View.Item is CobblerShaker)
                {
                    float dilution=Mathf.Clamp01(v.m>0?(float)(100*v.melt/v.m)/p.dilutionFullPercent:0),bright=Mathf.Cos(dilution*Mathf.PI*.5f),dull=Mathf.Sin(dilution*Mathf.PI*.5f);var pose=session.ShakeDisplay;bool posed=pose!=null&&pose.Amplitude>0;int sequence=pose?.SlamSequence??0,caught=pose?.CatchSequence??0;
                    // 2026-10-28: one stroke sound per slam of the ice on the designed path, not per mouse turn, so the rhythm is even.
                    // Ice hitting the cap (far end) is brighter, hitting the bottom (near end) heavier; louder when the stroke is long and fast.
                    if(sequence!=b.Reversal&&intensity>0&&pose!=null){int variant=sequence%4;double start=now+p.scheduleSeconds;float gain=p.strokeGain*mass*pose.SlamStrength,capEnd=pose.SlamAtCap?1:p.strokeBottomBright,bottom=pose.SlamAtCap?p.strokeCapDull:1;
                        Pool.Play(b.StrokeBright,"strokeBright",b.Id,position,gain*bright*capEnd,group:"stroke",variant:variant,scheduledDsp:start);Pool.Play(b.StrokeDull,"strokeDull",b.Id,position,gain*dull*bottom,group:"stroke",variant:variant,scheduledDsp:start);}b.Reversal=sequence;
                    // The catch after the toss: the ice landing on the bottom of the tin (2026-10-28: the extra synthetic "metal" clank is gone; it rang like a bell).
                    if(caught!=b.Catch&&mass>0){double start=now+p.scheduleSeconds;Pool.Play(b.StrokeDull,"strokeDull",b.Id,position,p.catchGain*mass,group:"stroke",variant:caught%4,scheduledDsp:start);}b.Catch=caught;
                    // The rattle bed swells toward the ends of the stroke, where the ice turns, and dips in mid-flight.
                    float bed=intensity*mass*p.shakerBedGain*(posed?Mathf.Lerp(p.bedMidStroke,1,Mathf.Abs(Mathf.Sin(pose.Phase))):1);
                    Pool.Play(b.Bright,"shakerBright",b.Id,position,bed*bright,1,true,"ice",variant:0,phase:.2f);Pool.Play(b.Dull,"shakerDull",b.Id,position,bed*dull,1,true,"ice",variant:0,phase:.2f);
                    if(v.ice<=0&&v.volume>0)Pool.Play(b.Slosh,"shakerDry",b.Id,position,intensity*p.sloshGain,1,true,"liquid");
                }
                // 2026-10-28: the stir bed follows the spoon's own speed (it fades while the spoon coasts to a stop);
                // with ice in the glass the spoon taps it softly every clinkTurns of a turn, brighter and a little higher when faster.
                var stir=session.StirDisplay;bool stirred=stir!=null&&stir.Target==b.Container;
                if(session.Clock.Active is {IsEnded:false} action&&action.Action.Id=="spoon.stir"&&action.Target==b.Container)Pool.Play(b.Stir,"stir",b.Id,position,intensity*p.stirGain*(stirred?Mathf.Lerp(p.stirBedStill,1,stir.Speed01):1),1,true,"ice");
                if(stirred){int clink=stir.ClinkSequence;if(clink!=b.Clink&&p.stirClinks&&v.ice>0)Pool.Play(b.Id+":clink",b.Container is ShakerBody?"iceMetal":"ice",b.Id,position,p.stirClinkGain*stir.Speed01*Mathf.Sqrt(mass),Mathf.Lerp(p.stirClinkPitchLow,p.stirClinkPitchHigh,stir.Speed01),group:"stroke",variant:clink%4,scheduledDsp:now+p.scheduleSeconds);b.Clink=clink;}
                float co2=v.volume>0?(float)(1000*v.gas/v.volume):0,charge=Mathf.Clamp01((co2-bubbles.criticalCo2)/(bubbles.fullCo2-bubbles.criticalCo2));if(v.volume>0)Pool.Play(b.Fizz,"soda",b.Id,position,charge*p.fizzGain,1,true,"fizz");
            }Pool.EndFrame();
        }
        private void OnDestroy(){if(session?.Adapter!=null)session.Adapter.Events.Published-=Observe;Pool?.Dispose();}
    }
}
