using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
namespace Bar.Tests
{
    internal sealed class U6ObservationTests
    {
        private CueSettings Settings()=>JsonUtility.FromJson<CueSettings>(File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Data/Json/BarCues.json")));
        private CueFrame Cue(VesselState s,string kind="shaker",double strength=1,bool closed=false,bool debug=false)=>new CueEvaluator(Settings()).Evaluate(s,kind,20,strength,20,closed,debug,true,1);
        private VesselState State()=>new VesselState{id="v",volumeMl=100,iceGrams=40,vesselTemperature=-5,co2GL=4,dilutionPercent=0};
        [Test] public void FrostFadesWhenWallWarms(){var s=State();var cold=Cue(s);s.vesselTemperature=15;Assert.That(cold.frost,Is.GreaterThan(Cue(s).frost));Assert.That(Cue(s).frost,Is.Zero);}
        [Test] public void CondensationUsesWallRatherThanLiquidTemperature(){var s=State();s.temperature=20;s.vesselTemperature=0;var cold=Cue(s,"glass");s.vesselTemperature=20;s.temperature=-5;Assert.That(cold.condensation,Is.GreaterThan(0));Assert.That(Cue(s,"glass").condensation,Is.Zero);}
        [Test] public void CarbonDioxideControlsCountAndSpeed(){var s=State();var high=Cue(s);s.co2GL=.5;var low=Cue(s);Assert.That(high.bubbleCount,Is.GreaterThan(low.bubbleCount));Assert.That(high.bubbleSpeed,Is.GreaterThan(low.bubbleSpeed));s.co2GL=0;Assert.That(Cue(s).bubbleCount,Is.Zero);}
        [Test] public void NoIceOrAgitationMeansNoCollisionSound(){var s=State();Assert.That(Cue(s).iceSound,Is.GreaterThan(0));s.iceGrams=0;Assert.That(Cue(s).iceSound,Is.Zero);s.iceGrams=40;Assert.That(Cue(s,strength:0).iceSound,Is.Zero);}
        [Test] public void DilutionLightensRgbaAccordingToJson(){var s=State();var a=Cue(s);s.dilutionPercent=40;var b=Cue(s);Assert.That(b.dilution,Is.GreaterThan(a.dilution));Assert.That(b.liquidRgba[0],Is.GreaterThan(a.liquidRgba[0]));Assert.That(b.liquidRgba[3],Is.LessThan(a.liquidRgba[3]));}
        [Test] public void ClosedSteelMasksInteriorAndDebugCanReveal(){var s=State();var a=Cue(s,closed:true);Assert.That(a.liquidVisible,Is.False);Assert.That(a.iceVisible,Is.False);Assert.That(a.bubbleCount,Is.Zero);Assert.That(Cue(s,closed:true,debug:true).liquidVisible,Is.True);}
        [Test] public void ObservationDoesNotMutateCoreSnapshot()
        {
            var model=JsonUtility.FromJson<ModelData>(File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Core/Data/model-v2.json")));
            var w=new World(model);var v=w.Vessels.First();var s=v.Snapshot();string before=LogReplay.SnapshotHash(w);
            for(int i=0;i<50;i++)Cue(s);Assert.That(LogReplay.SnapshotHash(w),Is.EqualTo(before));
        }
        [Test] public void JournalOnlySubscribesDomainAndRoundTripsDoublesAndNulls()
        {
            var bus=new DomainEventStream();var rows=new List<string>();
            using(var journal=new SessionJournal(bus,rows.Add))bus.Publish(new DomainEvent("interaction_deferred","actor","target","flip",observation:new LogRecord{deferred=true,reason="view_animation",intendedAmount=45.12345678912345,actualAmount=0}));
            var row=SessionJournal.Decode(rows.Single());Assert.That(row.kind,Is.EqualTo("interaction_deferred"));Assert.That(row.deferred,Is.True);Assert.That(row.intendedAmount,Is.EqualTo(45.12345678912345));Assert.That(row.requestedAmount,Is.Null);
            Assert.That(typeof(SessionJournal).Assembly.GetReferencedAssemblies().Any(a=>a.Name.StartsWith("UnityEngine")),Is.False);
        }
        [Test] public void MovementUsesCapsuleAndHasNoCoordinateBounds()
        {
            var go=new GameObject("U6 movement");try{go.transform.position=new Vector3(100,0,100);var p=go.AddComponent<PlayerController>();p.Move(Vector2.up,.1f);Assert.That(go.transform.position.z,Is.GreaterThan(100));Assert.That(go.GetComponent<CharacterController>(),Is.Not.Null);Assert.That(typeof(PlayerController).GetField("MovementMin"),Is.Null);Assert.That(typeof(PlayerController).GetField("MovementMax"),Is.Null);}finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test] public void ProcessFieldsUseC2StageDefinitions()
        {
            var p=new ProcessTracker();
            void Op(string type,double start,double end,string target=null,string from=null,string to=null,double intensity=1,double ice=0)
            {p.Observe(new LogRecord{kind="core_operation",timeBefore=start,time=end,operation=new Operation{type=type,target=target,from=from,to=to,seconds=end-start,intensity=intensity},before=new[]{new VesselState{id=from,iceGrams=ice}}});}
            Op("Shake",0,12,"shaker",intensity:.8);Op("Rest",12,15);Op("StrainTransfer",15,15,from:"shaker",to:"glass",ice:25);Op("Stir",15,17,"glass",intensity:.5);Op("Rest",17,21);
            var d=p.Describe("glass",21);Assert.That(d.actionSeconds,Is.EqualTo(14));Assert.That(d.totalSeconds,Is.EqualTo(21));Assert.That(d.meanIntensity,Is.EqualTo((12*.8+2*.5)/14).Within(1e-12));Assert.That(d.remainingIce,Is.EqualTo(25));Assert.That(d.restFraction,Is.EqualTo(7d/21).Within(1e-12));
        }
        [Test] public void ReplayRejectsMissingSequenceAndAcceptsExactOperationStream()
        {
            var model=JsonUtility.FromJson<ModelData>(File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Core/Data/model-v2.json")));
            var adapter=new WorldAdapter(model);var rows=new List<string>();using(var j=new SessionJournal(adapter.Events,rows.Add))
            {adapter.Publish("session_header",new LogRecord{header=new SessionHeader{model=model}});adapter.Advance(0);}
            var replay=new LogReplay();replay.Run(rows);var broken=SessionJournal.Decode(rows[0]);broken.sequence=3;Assert.Throws<InvalidOperationException>(()=>replay.Run(new[]{SessionJournal.Encode(broken)}));
        }
        [Test] public void ReplayDetectsAlteredServedDescriptorAndMissingPayload()
        {
            var model=JsonUtility.FromJson<ModelData>(File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Core/Data/model-v2.json")));
            var bus=new DomainEventStream();var lines=new List<string>();var w=new World(model,new WorldOptions{vessels=Array.Empty<VesselDefinition>(),recording=false});var p=new ProcessTracker();
            using(var j=new SessionJournal(bus,lines.Add))
            {
                bus.Publish(new DomainEvent("session_header",null,null,null,observation:new LogRecord{header=new SessionHeader{model=model}}));
                var definition=new VesselDefinition{id="glass",kind="glass",capacityMl=400,massGrams=180,specificHeat=.84,liquidK=6,environmentK=.2};
                foreach(var o in new[]{new Operation{type="RegisterVessel",id="glass",definition=definition},new Operation{type="PourSource",to="glass",ingredient="tonic",ml=90},new Operation{type="Serve",from="glass"}})
                {
                    var r=new LogRecord{kind="core_operation",timeBefore=w.Time,operation=o};w.Apply(o);r.time=w.Time;p.Observe(r);
                    if(o.type=="Serve"){r.served=w.Vessels.Single().Snapshot();r.process=p.Describe("glass",w.Time);r.attemptDirectPourVolumeMl=0;r.attemptGuidedPourVolumeMl=0;r.attemptCo2LostDuringPour=0;}
                    bus.Publish(new DomainEvent("core_operation",null,null,null,observation:r));
                }
            }
            var replay=new LogReplay();replay.Run(lines);Assert.That(replay.ServedCount,Is.EqualTo(1));
            var changed=SessionJournal.Decode(lines.Last());changed.served.abvPercent=1;lines[lines.Count-1]=SessionJournal.Encode(changed);Assert.Throws<InvalidOperationException>(()=>replay.Run(lines));
            changed.served=null;lines[lines.Count-1]=SessionJournal.Encode(changed);Assert.Throws<InvalidOperationException>(()=>replay.Run(lines));
        }
    }
}
