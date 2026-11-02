using System;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Unity;
using Bar.Logging;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
namespace Bar.Tests
{
    [Serializable] public sealed class GuidedOp
    {
        public string type,from,to,target,ingredient,pourMethod,overflowPolicy;
        public double ml,grams,seconds,fraction,intensity;public bool hasMl,hasGrams,hasSeconds,hasFraction,hasIntensity;
        public Operation Create()=>new Operation{type=type,from=from,to=to,target=target,ingredient=ingredient,pourMethod=pourMethod,overflowPolicy=overflowPolicy,ml=hasMl?ml:(double?)null,grams=hasGrams?grams:(double?)null,seconds=hasSeconds?seconds:(double?)null,fraction=hasFraction?fraction:(double?)null,intensity=hasIntensity?intensity:(double?)null};
    }
    [Serializable] public sealed class GuidedCheckpoint{public GuidedOp op;public string error;public double[] values;}
    [Serializable] public sealed class GuidedCase{public string name;public GuidedCheckpoint[] actions;}
    [Serializable] public sealed class GuidedCases{public GuidedCase[] cases;}
    public sealed class GuidedPourTests
    {
        private static string Read(string file)=>File.ReadAllText(Path.Combine(Application.dataPath,"Bar",file));
        private static ModelData Model()=>JsonUtility.FromJson<ModelData>(Read("Core/Data/model-v2.json"));
        private static double[] Flatten(World w)=>w.Vessels.OrderBy(v=>v.id).SelectMany(v=>new[]{v.m,v.E,v.gas,v.gasInput,v.volume,v.ice,v.melt,v.directPourVolumeMl,v.guidedPourVolumeMl,v.co2LostDuringPour}).Concat(new[]{w.Ledger().massResidual,w.Ledger().energyResidual,w.GasOut.gas,w.GasOut.H}).ToArray();
        [Test]public void GuidedPhysicsMatchesJsIncludingRollbackOverflowAndLineage()
        {
            var fixtures=JsonUtility.FromJson<GuidedCases>(Read("Tests/Editor/guided-parity.json"));
            foreach(var test in fixtures.cases){var w=new World(Model());foreach(var step in test.actions){string code="";try{w.Apply(step.op.Create());}catch(ModelException e){code=e.Code;}Assert.That(code,Is.EqualTo(step.error),test.name);var actual=Flatten(w);Assert.That(actual.Length,Is.EqualTo(step.values.Length));for(int i=0;i<actual.Length;i++)Assert.That(actual[i],Is.EqualTo(step.values[i]).Within(i%10==1||i==actual.Length-1?1e-7:1e-9),test.name+" field "+i);}}
        }
        [Test]public void EntryLossIsIndependentOfReceiptSliceSizeAndConservesMassEnergy()
        {
            foreach(var method in new[]{"Direct","BarspoonGuided"}){var whole=new World(Model());var split=new World(Model());whole.PourCarbonated("glass","tonic",100,method);for(int i=0;i<100;i++)split.PourCarbonated("glass","tonic",1,method);var a=Flatten(whole);var b=Flatten(split);for(int i=0;i<a.Length;i++)Assert.That(a[i],Is.EqualTo(b[i]).Within(1e-7));Assert.That(split.Ledger().massResidual,Is.EqualTo(0).Within(1e-9));Assert.That(split.Ledger().energyResidual,Is.EqualTo(0).Within(1e-7));}
            var data=Model();data.carbonatedPour=null;Assert.Throws<ModelException>(()=>new World(data).PourCarbonated("glass","soda",10,"Direct"));
        }
        [Test]public void BothHandsKeepDirectFirstAndGuidedReceiptsUseSlowerConfirmedFlow()
        {
            foreach(bool spoonMain in new[]{false,true})foreach(string method in new[]{"pour.bottle_to_container","pour.barspoon_guided"})
            {
                var data=Model();var controls=ItemCatalog.LoadControls(Read("Data/Json/controls.json"));var factory=new ItemFactory(ItemCatalog.Load(Read("Data/Json/items.json"),data),id=>data.ingredients.Single(x=>x.id==id).temperature,()=>.5);
                var b=(Bottle)factory.Create("bottle.tonic","b");b.SetOpen(true);var g=(Glass)factory.Create("glass.highball","g");var spoon=factory.Create("tool.spoon","spoon");var hands=new Hands();hands.TryPickUp(b);hands.TryPickUp(spoon);hands.SelectMain(hands.SideOf(spoonMain?spoon:b).Value);
                var adapter=new WorldAdapter(data);adapter.Register(g);var resolver=ActionResolver.CreateU4(controls,data.icePortionGrams,data.iceDensity,()=>.5);
                var menu=resolver.Resolve(g,hands).OrderBy(o=>HudText.Priority(o,g,hands)).ToArray();Assert.That(menu[0].Action.Id,Is.EqualTo("pour.bottle_to_container"));Assert.That(menu[1].Action.Id,Is.EqualTo("pour.barspoon_guided"));
                var clock=new SimulationClock(adapter,hands);clock.Enqueue(0,c=>c.Begin(menu.Single(x=>x.Action.Id==method)));clock.Enqueue(0,c=>c.SetTilt(160));clock.Enqueue(.1,c=>c.Stop());clock.AdvanceFrame(.1);
                Assert.That(clock.Error,Is.Null);var r=adapter.LastRequest;Assert.That(r.ActualMl,Is.EqualTo(method=="pour.barspoon_guided"?2.8:4).Within(1e-8));Assert.That(r.PourMethod,Is.EqualTo(method=="pour.barspoon_guided"?"BarspoonGuided":"Direct"));Assert.That(r.Co2LostOnEntryGrams,Is.GreaterThan(0));Assert.That(hands.IsBusy,Is.False);
                var guide=new GuidedPourAction(controls);hands.TryPlace(hands.SideOf(spoon).Value,(BarCounter)factory.Create("fixture.counter","counter"));Assert.That(guide.CanExecute(b,g,hands),Is.False);
            }
        }
        [Test]public void ExistingOrderRangesAcceptBothMethodsWithoutExtraMeltOperator()
        {
            var data=Model();var orders=OrderCatalog.Load();foreach(var order in orders.orders)foreach(string method in new[]{"Direct","BarspoonGuided"}){var w=new World(data,new WorldOptions{vessels=Array.Empty<VesselDefinition>()});var plan=OrderReference.Plan(data,Read("Data/Json/items.json"),order);foreach(var op in plan){if(op.type=="PourCarbonated")op.pourMethod=method;w.Apply(op);}var served=w.Vessels.Single(v=>v.served).Snapshot();Assert.That(orders.Evaluate(order,served,order.glass).All(x=>x.inside),Is.True,order.id+" "+method);}
        }
        [Test]public void RecordedCoreReceiptsAndFinalMixtureReplayWithCurrentSchema()
        {
            var data=Model();var world=new World(data,new WorldOptions{vessels=Array.Empty<VesselDefinition>()});var rows=new System.Collections.Generic.List<string>();var process=new ProcessTracker();long sequence=0;
            rows.Add(SessionJournal.Encode(new LogRecord{kind="session_header",sequence=sequence++,header=new SessionHeader{model=data}}));
            var ops=new[]{new Operation{type="RegisterVessel",definition=data.vessels.Single(x=>x.id=="glass"),id="glass"},new Operation{type="RegisterVessel",definition=data.vessels.Single(x=>x.id=="shaker"),id="shaker"},new Operation{type="PourCarbonated",to="glass",ingredient="tonic",ml=60,pourMethod="Direct"},new Operation{type="PourCarbonated",to="glass",ingredient="soda",ml=40,pourMethod="BarspoonGuided"},new Operation{type="TransferLiquid",from="glass",to="shaker",fraction=.3},new Operation{type="DiscardLiquid",from="shaker"},new Operation{type="Serve",from="glass"}};
            foreach(var op in ops){var row=new LogRecord{kind="core_operation",sequence=sequence++,operation=op,timeBefore=world.Time,before=world.Vessels.Select(v=>v.Snapshot()).ToArray()};var result=world.Apply(op);row.time=world.Time;row.after=world.Vessels.Select(v=>v.Snapshot()).ToArray();if(result is PourResult r){row.carbonatedPourMethod=r.carbonatedPourMethod;row.acceptedVolumeMl=r.acceptedMl;row.co2InputGrams=r.co2InputGrams;row.co2LostOnEntryGrams=r.co2LostOnEntryGrams;}process.Observe(row);if(op.type=="Serve"){row.served=world.Vessels.Single(v=>v.id=="glass").Snapshot();row.process=process.Describe("glass",world.Time);row.attemptDirectPourVolumeMl=60;row.attemptGuidedPourVolumeMl=40;row.attemptCo2LostDuringPour=ops.Where(x=>x.type=="PourCarbonated").Sum(x=>x.ml.Value*data.ingredients.Single(i=>i.id==x.ingredient).co2GL/1000*(x.pourMethod=="Direct"?.08:.025));}rows.Add(SessionJournal.Encode(row));}
            var replay=new LogReplay();replay.Run(rows);Assert.That(replay.ServedCount,Is.EqualTo(1));Assert.That(world.Vessels.Single(v=>v.id=="glass").Snapshot().carbonatedPourMethod,Is.EqualTo("Mixed"));Assert.That(world.Vessels.Single(v=>v.id=="glass").directPourVolumeMl,Is.EqualTo(42).Within(1e-10));
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../GuidedPourReports"));Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"mixed-lineage.jsonl"),rows);
        }
    }
}
