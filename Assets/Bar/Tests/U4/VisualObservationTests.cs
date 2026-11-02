using System;
using System.Linq;
using Bar.Logging;
using Bar.Unity;
using NUnit.Framework;
namespace Bar.Tests
{
    internal sealed class VisualObservationTests
    {
        private string[] Samples(int fps)
        {var sampler=new CueSampler();var output=new System.Collections.Generic.List<string>();for(int i=0;i<=fps*3;i++){double time=Math.Floor((i/(double)fps+1e-8)*10)/10;var c=new[]{new CueFrame{vesselId="v",frost=time/10}};if(sampler.Sample(time,c))output.Add(time.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+":"+c[0].frost.ToString("R",System.Globalization.CultureInfo.InvariantCulture));}return output.ToArray();}
        [Serializable]private sealed class OpticalFixture {public OpticalCase[] cases;}
        [Serializable]private sealed class OpticalCase {public CraftingResearch.CoreV2.Operation operation;public OpticalVessel[] vessels;}
        [Serializable]private sealed class OpticalVessel {public string id;public double[] optical;}
        [Test]public void PassiveOpticsMatchJsAcrossMixMeltTransferAndDiscard()
        {
            var root=UnityEngine.Application.dataPath+"/Bar/";var model=UnityEngine.JsonUtility.FromJson<CraftingResearch.CoreV2.ModelData>(System.IO.File.ReadAllText(root+"Core/Data/model-v2.json"));var world=new CraftingResearch.CoreV2.World(model);
            // JsonUtility cannot deserialize nullable Operation scalars: restore these known fixture parameters explicitly.
            var fixture=UnityEngine.JsonUtility.FromJson<OpticalFixture>(System.IO.File.ReadAllText(root+"Tests/U4/optical-fixture.json"));
            var ops=new[]{new CraftingResearch.CoreV2.Operation{type="PourSource",to="shaker",ingredient="spirit",ml=45},new CraftingResearch.CoreV2.Operation{type="PourSource",to="shaker",ingredient="lemon",ml=25},new CraftingResearch.CoreV2.Operation{type="PourSource",to="shaker",ingredient="syrup",ml=20},new CraftingResearch.CoreV2.Operation{type="AddIce",to="shaker",grams=100},new CraftingResearch.CoreV2.Operation{type="Shake",target="shaker",seconds=12,intensity=.8},new CraftingResearch.CoreV2.Operation{type="StrainTransfer",from="shaker",to="glass"},new CraftingResearch.CoreV2.Operation{type="Rest",target="glass",seconds=10},new CraftingResearch.CoreV2.Operation{type="DiscardLiquid",from="glass"}};
            for(int n=0;n<ops.Length;n++){world.Apply(ops[n]);foreach(var expected in fixture.cases[n].vessels){var actual=world.Vessels.Single(v=>v.id==expected.id).Snapshot().optical;for(int i=0;i<7;i++)Assert.That(actual[i],Is.EqualTo(expected.optical[i]).Within(1e-9));}}
        }
        [Test]public void CueSamplesAreIdenticalAtThirtyAnd144Fps()=>Assert.That(Samples(30),Is.EqualTo(Samples(144)));
        [Test]public void CueSwitchAndThresholdTriggerWithoutWaitingForTick(){var s=new CueSampler();Assert.That(s.Sample(0,new[]{new CueFrame()}),Is.True);Assert.That(s.Sample(.01,new[]{new CueFrame()}),Is.False);Assert.That(s.Sample(.02,new[]{new CueFrame{onScreen=true}}),Is.True);Assert.That(s.Sample(.03,new[]{new CueFrame{onScreen=true,frost=.5}}),Is.True);}
    }
}
