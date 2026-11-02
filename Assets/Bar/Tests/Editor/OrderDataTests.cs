using System;
using System.IO;
using System.Linq;
using Bar.Logging;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
namespace Bar.Tests
{
    public sealed class OrderDataTests
    {
        [Test] public void GenerateReferencesFromUnchangedCore()
        {
            var model=JsonUtility.FromJson<ModelData>(File.ReadAllText("Assets/Bar/Core/Data/model-v2.json"));var catalog=OrderCatalog.Load();
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../U7Reports"));Directory.CreateDirectory(output);
            foreach(var order in catalog.orders)
            {
                var w=new World(model,new WorldOptions{vessels=Array.Empty<VesselDefinition>(),recording=false});var ops=OrderReference.Plan(model,File.ReadAllText("Assets/Bar/Data/Json/items.json"),order);
                foreach(var op in ops)w.Apply(op);var s=w.Vessels.Single(v=>v.id==ops.Last().from).Snapshot();
                File.WriteAllText(Path.Combine(output,order.id+"-reference.json"),SessionJournal.Encode(new LogRecord{served=s}));
                Assert.That(w.Ledger().massResidual,Is.EqualTo(0).Within(1e-8));Assert.That(w.Ledger().energyResidual,Is.EqualTo(0).Within(1e-6));
            }
        }
        [Test] public void FeedbackUsesBoundsRanksSeverityAndNeverChecksSteps()
        {
            var c=OrderCatalog.Load();Assert.That(c.note,Does.Contain("人为设定、未标定"));Assert.That(c.maxSentences,Is.EqualTo(2));
            var sour=c.orders[0];var s=new VesselState{abvPercent=17,sugarAcidRatio=10,temperature=-3,dilutionPercent=25};
            Assert.That(c.Displayed(c.Evaluate(sour,s,"coupe")),Is.EqualTo(new[]{c.satisfiedId}));
            s.abvPercent=12;s.sugarAcidRatio=6;s.temperature=0;s.dilutionPercent=15;
            Assert.That(c.Evaluate(sour,s,"coupe").All(d=>d.inside),Is.True,"区间边界包含端点");
            s.abvPercent=40;s.sugarAcidRatio=0;s.temperature=5;s.dilutionPercent=90;
            var dims=c.Evaluate(sour,s,"highball");var displayed=c.Displayed(dims);Assert.That(displayed.Length,Is.EqualTo(2));
            Assert.That(displayed[0],Is.EqualTo("dilutionPercent.high.strong"));Assert.That(displayed[1],Is.EqualTo("abvPercent.high.strong"));
            s.sugarAcidRatio=null;Assert.That(c.Evaluate(sour,s,"coupe").Single(d=>d.dimension=="sugarAcidRatio").direction,Is.EqualTo("missing"));
            foreach(var id in displayed)Assert.That(c.Text(id).Any(char.IsDigit),Is.False);
        }
    }
}
