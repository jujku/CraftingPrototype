using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;

namespace Bar.Tests
{
    [TestFixture] internal sealed class ExtensionParityTests
    {
        [Serializable] public sealed class Checkpoint { public double[] values; public string[] ids; }
        [Serializable] public sealed class ExtensionOp
        {
            public string type, from, to, target, ingredient, id;
            public double ml, grams, fraction, seconds;
            public bool discard;
            public VesselDefinition definition;
            public Operation Create() => new Operation { type = type, from = from, to = to, target = target, ingredient = ingredient, id = id, ml = ml, grams = grams, fraction = fraction, seconds = seconds, discard = discard, definition = definition };
        }
        [Serializable] public sealed class ActionData { public ExtensionOp op; public string error; public Checkpoint[] steps; public Checkpoint expected; }
        [Serializable] public sealed class CaseData { public string name; public double syrupSugar; public Checkpoint initial; public ActionData[] actions; }
        [Serializable] public sealed class Root { public string sourceCoreSHA256, sourceModelSHA256; public CaseData[] cases; }
        private static string Read(string relative) => File.ReadAllText(Path.Combine(Application.dataPath, "Bar", relative));
        public static IEnumerable<TestCaseData> Cases()
        { foreach (var c in JsonUtility.FromJson<Root>(Read("Tests/Extensions/Fixtures/extensions.json")).cases) yield return new TestCaseData(c).SetName("U2 step parity :: " + c.name); }
        private static Checkpoint Flatten(World w)
        {
            var values = new List<double>(); Action<double> add = x => values.Add(x);
            foreach (double x in new[] { w.Time, w.EnvironmentEnergy, w.MinimumInputTemperature, w.MaximumInputTemperature, w.MinimumLiquidus }) add(x);
            foreach (var p in new[] { w.Inputs, w.GasOut, w.Waste, w.Overflow }) { foreach (double x in new[] { p.m, p.H, p.water, p.ethanol, p.sugar, p.acid, p.gas }) add(x); foreach (double x in p.flavor) add(x); }
            add(w.Vessels.Count);
            foreach (var v in w.Vessels)
            {
                foreach (double x in new[] { v.m, v.ice, v.E, v.water, v.ethanol, v.sugar, v.acid, v.gas, v.melt, v.volume, v.gasInput, v.vesselEnergy, v.heatCapacity, v.capacity }) add(x);
                foreach (double x in v.flavor) add(x); add(v.Temperature ?? 999999); add(v.VesselTemperature ?? 999999); add(v.iceExchangePaused ? 1 : 0); add(v.served ? 1 : 0);
            }
            var l = w.Ledger(); add(l.massResidual); add(l.energyResidual); add(l.compositionResidual); foreach (double x in l.components) add(x); foreach (double x in l.flavorResidual) add(x);
            return new Checkpoint { values = values.ToArray(), ids = w.Vessels.Select(v => v.id).ToArray() };
        }
        private static void Same(World w, Checkpoint expected)
        {
            var actual = Flatten(w); Assert.That(actual.ids, Is.EqualTo(expected.ids)); Assert.That(actual.values.Length, Is.EqualTo(expected.values.Length));
            for (int i = 0; i < actual.values.Length; i++) Assert.That(actual.values[i], Is.EqualTo(expected.values[i]).Within(1e-7), "flattened index " + i);
            Assert.That(w.Ledger().massResidual, Is.EqualTo(0).Within(1e-8)); Assert.That(w.Ledger().energyResidual, Is.EqualTo(0).Within(1e-6));
        }
        [TestCaseSource(nameof(Cases))] public void EveryStepAndAtomicOperationMatchesJs(CaseData data)
        {
            var model = JsonUtility.FromJson<ModelData>(Read("Core/Data/model-v2.json")); model.ingredients.Single(x => x.id == "syrup").sugar = data.syrupSugar;
            var fixture=JsonUtility.FromJson<Root>(Read("Tests/Extensions/Fixtures/extensions.json"));Assert.That(model.sourceCoreSHA256,Is.EqualTo(fixture.sourceCoreSHA256));Assert.That(model.sourceModelSHA256,Is.EqualTo(fixture.sourceModelSHA256));
            var world = new World(model); Same(world, data.initial);
            foreach (var action in data.actions)
            {
                int step = 0; world.StepCompleted = w => { Assert.That(step, Is.LessThan(action.steps.Length)); Same(w, action.steps[step++]); };
                if (string.IsNullOrEmpty(action.error)) world.Apply(action.op.Create());
                else Assert.That(Assert.Throws<ModelException>(() => world.Apply(action.op.Create())).Code, Is.EqualTo(action.error));
                Assert.That(step, Is.EqualTo(action.steps.Length)); Same(world, action.expected);
            }
        }
        [Test] public void RegistrationRollbackRestoresTheWholeRegistryAndThermalLedger()
        {
            var model = JsonUtility.FromJson<ModelData>(Read("Core/Data/model-v2.json"));
            var registry = World.CreateRegistry().Register("FailAfterRegistryChange", (w, op) => { w.UnregisterVessel("jigger"); w.RegisterVessel(new VesselDefinition { id = "new", kind = "glass", capacityMl = 100, massGrams = 100, specificHeat = .84, liquidK = 2, environmentK = .1 }); throw new ModelException("TEST_FAILURE", "expected"); });
            var world = new World(model, new WorldOptions { registry = registry }); var before = Flatten(world);
            Assert.Throws<ModelException>(() => world.Apply(new Operation { type = "FailAfterRegistryChange" })); Same(world, before);
        }
    }
}
