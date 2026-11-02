using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;

namespace CraftingResearch.TestsV2
{
    [Serializable] public sealed class Checkpoint { public double[] values; public int[] nullIndices; }
    [Serializable] public sealed class FixtureOptions
    {
        public string profile;
        public bool vesselThermal, checkTemperatures, recording;
        public double roomTemperature, environmentK, step;
        public VesselTemperature[] vesselTemperatures;
        public WorldOptions Create() => new WorldOptions { profile = profile, vesselThermal = vesselThermal, checkTemperatures = checkTemperatures,
            recording = recording, roomTemperature = roomTemperature, environmentK = environmentK, step = step, vesselTemperatures = vesselTemperatures };
    }
    [Serializable] public sealed class FixtureOperation
    {
        public string type, from, to, target, ingredient, overflowPolicy;
        public double ml, grams, portions, seconds, intensity, fraction, temperature, mass, sugar, acid, conductance, step;
        public bool hasMl, hasGrams, hasPortions, hasSeconds, hasIntensity, hasFraction, hasTemperature;
        public Operation Create() => new Operation { type = type, from = from, to = to, target = target, ingredient = ingredient, overflowPolicy = overflowPolicy,
            ml = hasMl ? ml : (double?)null, grams = hasGrams ? grams : (double?)null, portions = hasPortions ? portions : (double?)null,
            seconds = hasSeconds ? seconds : (double?)null, intensity = hasIntensity ? intensity : (double?)null,
            fraction = hasFraction ? fraction : (double?)null, temperature = hasTemperature ? temperature : (double?)null };
    }
    [Serializable] public sealed class FixtureAction { public FixtureOperation operation; public string error; public Checkpoint[] steps; public Checkpoint expected; }
    [Serializable] public sealed class FixtureCase { public string name; public FixtureOptions options; public Checkpoint initial; public FixtureAction[] actions; }
    [Serializable] public sealed class FixtureRoot
    {
        public string schema, model, sourceCoreSHA256, sourceModelSHA256, sourceIngredientsSHA256;
        public string[] valueNames, valueKinds;
        public FixtureCase[] cases;
    }
    [Serializable] public sealed class ErrorSummary
    {
        public int comparedCheckpoints, comparedSteps, comparedValues, rejectedOperations;
        public long temperatureNodeChecks;
        public double maxTemperatureDifference, maxMassDifference, maxEnergyDifference, maxOtherDifference;
    }
    [TestFixture] public sealed class V2ParityTests
    {
        private static FixtureRoot cache;
        private static ModelData model;
        private static readonly ErrorSummary errors = new ErrorSummary();
        private static string Asset(string suffix) => Path.Combine(Application.dataPath, "Bar/" + suffix.Replace("SimulationV2/", "Core/").Replace("Tests/V2EditMode/", "Tests/Core/"));
        private static ModelData Data => model ?? (model = JsonUtility.FromJson<ModelData>(File.ReadAllText(Asset("SimulationV2/Data/model-v2.json"))));
        private static FixtureRoot Fixtures
        {
            get
            {
                if (cache != null) return cache;
                using (var file = File.OpenRead(Asset("Tests/V2EditMode/Fixtures/js-v2-parity.json.gz")))
                using (var zip = new GZipStream(file, CompressionMode.Decompress))
                using (var reader = new StreamReader(zip)) cache = JsonUtility.FromJson<FixtureRoot>(reader.ReadToEnd());
                return cache;
            }
        }
        private static string Workspace()
        {
            for (var dir = new DirectoryInfo(Application.dataPath); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Web_Prototype/simulation-core-v2.js"))) return dir.FullName;
            throw new DirectoryNotFoundException("Shared reference JS not found");
        }
        private static string Hash(string file)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", "").ToLowerInvariant();
        }
        public static IEnumerable<TestCaseData> Cases()
        { foreach (var c in Fixtures.cases) yield return new TestCaseData(c).SetName("V2 JS parity :: " + c.name); }
        private static Checkpoint Flatten(World w)
        {
            var values = new List<double>(); var nulls = new List<int>();
            Action<double?> add = x => { if (!x.HasValue) nulls.Add(values.Count); values.Add(x ?? 0); };
            add(w.Time); add(w.EnvironmentEnergy); add(w.MinimumInputTemperature); add(w.MaximumInputTemperature); add(w.MinimumLiquidus);
            foreach (var p in new[] { w.Inputs, w.GasOut, w.Waste, w.Overflow })
            { foreach (var x in new[] { p.m, p.H, p.water, p.ethanol, p.sugar, p.acid, p.gas }) add(x); foreach (var f in p.flavor) add(f); }
            foreach (var v in w.Vessels)
            {
                foreach (var x in new[] { v.m, v.ice, v.E, v.water, v.ethanol, v.sugar, v.acid, v.gas, v.melt, v.volume, v.gasInput, v.vesselEnergy, v.heatCapacity, v.capacity }) add(x);
                foreach (var x in v.flavor) add(x);
                var s = v.Snapshot();
                foreach (var x in new[] { s.temperature, s.vesselTemperature, s.dilutionPercent, s.abvPercent, s.sugarPercent, s.acidPercent, s.sugarAcidRatio, s.co2GL, s.co2Retention }) add(x);
                foreach (var x in s.flavor) add(x); add(v.served ? 1 : 0);
                add(s.freezingDomain?.temperature); add(s.freezingDomain?.x); add(s.freezingDomain?.z); add(s.freezingDomain == null ? (double?)null : s.freezingDomain.outside ? 1 : 0);
            }
            var ledger = w.Ledger(); add(ledger.massResidual); add(ledger.energyResidual); add(ledger.compositionResidual);
            foreach (var x in ledger.components) add(x); foreach (var x in ledger.flavorResidual) add(x);
            return new Checkpoint { values = values.ToArray(), nullIndices = nulls.ToArray() };
        }
        private static void Same(World w, Checkpoint expected)
        {
            var actual = Flatten(w); Assert.That(actual.values.Length, Is.EqualTo(expected.values.Length)); Assert.That(actual.nullIndices, Is.EqualTo(expected.nullIndices));
            for (int i = 0; i < actual.values.Length; i++)
            {
                double error = Math.Abs(actual.values[i] - expected.values[i]); string kind = Fixtures.valueKinds[i];
                switch (kind)
                {
                    case "temperature": errors.maxTemperatureDifference = Math.Max(errors.maxTemperatureDifference, error); break;
                    case "mass": errors.maxMassDifference = Math.Max(errors.maxMassDifference, error); break;
                    case "energy": errors.maxEnergyDifference = Math.Max(errors.maxEnergyDifference, error); break;
                    default: errors.maxOtherDifference = Math.Max(errors.maxOtherDifference, error); break;
                }
                if (double.IsNaN(actual.values[i]) || double.IsInfinity(actual.values[i]) || error > (kind == "energy" ? 1e-7 : 1e-9))
                    Assert.Fail(Fixtures.valueNames[i] + " JS=" + expected.values[i].ToString("R") + " C#=" + actual.values[i].ToString("R") + " delta=" + error.ToString("R"));
            }
            var ledger = w.Ledger();
            Assert.That(Math.Abs(ledger.massResidual), Is.LessThan(1e-7)); Assert.That(Math.Abs(ledger.energyResidual), Is.LessThan(1e-6));
            Assert.That(ledger.components.All(x => Math.Abs(x) < 1e-7), Is.True); Assert.That(ledger.flavorResidual.All(x => Math.Abs(x) < 1e-7), Is.True);
            errors.comparedCheckpoints++; errors.comparedValues += actual.values.Length;
        }
        [TestCaseSource(nameof(Cases))]
        public void EveryIntegrationStepAndOperationMatchesJs(FixtureCase c)
        {
            var w = new World(Data, c.options.Create()); Same(w, c.initial);
            foreach (var action in c.actions)
            {
                int index = 0;
                w.StepCompleted = c.options.profile == "v1-water" ? null : new Action<World>(world => {
                    Assert.That(index, Is.LessThan(action.steps.Length), "Too many integration steps"); Same(world, action.steps[index++]); errors.comparedSteps++;
                });
                ModelException failure = null;
                try
                {
                    var op = action.operation;
                    if (op.type == "LegacyAdd") w.AddLegacy("shaker", op.mass, op.temperature, op.sugar, op.acid);
                    else if (op.type == "LegacyAdvance") w.Advance(op.seconds, "shaker", "rest", 1, op.conductance, op.step);
                    else w.Apply(op.Create());
                }
                catch (ModelException e) { failure = e; }
                Assert.That(failure?.Code ?? "", Is.EqualTo(action.error), "operation rejection code");
                if (failure != null) errors.rejectedOperations++;
                Assert.That(index, Is.EqualTo(action.steps.Length)); Same(w, action.expected);
            }
            errors.temperatureNodeChecks += w.TemperatureChecks;
        }
        [Test] public void FixturesAndRuntimeDataMatchFrozenJsSources()
        {
            Assert.That(Fixtures.schema, Is.EqualTo("js-csharp-v2-parity-1")); Assert.That(Fixtures.model, Is.EqualTo(World.Model));
            Assert.That(Data.model, Is.EqualTo(World.Model));
            string root = Workspace();
            Assert.That(Hash(Path.Combine(root, "Web_Prototype/simulation-core-v2.js")), Is.EqualTo(Fixtures.sourceCoreSHA256));
            Assert.That(Data.sourceCoreSHA256, Is.EqualTo(Fixtures.sourceCoreSHA256));
            Assert.That(Hash(Path.Combine(root, "Web_Prototype/data/model-v2.json")), Is.EqualTo(Fixtures.sourceModelSHA256));
            Assert.That(Hash(Path.Combine(root, "Web_Prototype/data/ingredients-v2.json")), Is.EqualTo(Fixtures.sourceIngredientsSHA256));
        }
        [Test] public void PureCoreAssemblyHasNoEngineReference()
        { Assert.That(typeof(World).Assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("Unity", StringComparison.Ordinal)), Is.False); }
        [Test] public void TemperatureInvariantDetectsCorruptedEnergy()
        {
            var w = new World(Data); w.Pour("shaker", "lemon", 1); w.GetVessel("shaker").E *= 10000;
            Assert.That(Assert.Throws<ModelException>(() => w.AssertTemperatureBounds(w.GetVessel("shaker"))).Code, Is.EqualTo("TEMPERATURE_INVARIANT"));
        }
        [Test] public void RegistryExtensionAndAtomicRollbackCoverAllPackets()
        {
            var registry = World.CreateRegistry().Register("ExtensionNoOp", (world, operation) => null)
                .Register("FailAfterPour", (world, operation) => { world.Pour("glass", "tonic", 10, 4); throw new ModelException("EXTENSION_FAILURE", "test"); });
            var w = new World(Data, new WorldOptions { registry = registry }); var before = Flatten(w);
            w.Apply(new Operation { type = "ExtensionNoOp" }); Same(w, before);
            Assert.That(Assert.Throws<ModelException>(() => w.Apply(new Operation { type = "FailAfterPour" })).Code, Is.EqualTo("EXTENSION_FAILURE")); Same(w, before);
            Assert.That(w.Events.Count, Is.EqualTo(1));
            Assert.That(Assert.Throws<ModelException>(() => registry.Register("Rest", (world, op) => null)).Code, Is.EqualTo("INVALID_OPERATOR"));
        }
        [Test] public void WorldIsDeterministic()
        {
            var a = new World(Data); var b = new World(Data);
            foreach (var w in new[] { a, b }) { w.Pour("shaker", "spirit", 40); w.Pour("shaker", "lemon", 20); w.AddIce("shaker", 100); w.Advance(10, "shaker", "shake"); }
            Assert.That(Flatten(a).values, Is.EqualTo(Flatten(b).values));
        }
        [Test] public void RuntimeIngredientSetExcludesAnalysisOnlyControls()
        {
            Assert.That(Data.ingredients.Select(x => x.id), Is.EquivalentTo(new[] { "spirit", "syrup", "lemon", "soda", "tonic" }));
        }
        [Test] public void AllFourFlavorMarkersSurviveTransferAndExplicitWaste()
        {
            // Test-only synthetic vector, not a new ingredient in the Unity prototype data.
            var synthetic = JsonUtility.FromJson<ModelData>(JsonUtility.ToJson(Data));
            synthetic.ingredients.First(x => x.id == "spirit").flavor = new[] { .1, .2, .3, .4 };
            var w = new World(synthetic); w.Pour("shaker", "spirit", 40); w.Transfer("shaker", "glass", .25); w.DiscardLiquid("shaker");
            for (int i = 0; i < 4; i++)
            {
                Assert.That(w.GetVessel("glass").flavor[i], Is.EqualTo(w.Inputs.flavor[i] * .25).Within(1e-12));
                Assert.That(w.Waste.flavor[i], Is.EqualTo(w.Inputs.flavor[i] * .75).Within(1e-12));
                Assert.That(w.Ledger().flavorResidual[i], Is.EqualTo(0).Within(1e-12));
            }
        }
        [TestCase(-1)] [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)]
        public void InvalidActualPourDoesNotMutateWorld(double ml)
        {
            var w = new World(Data); var before = Flatten(w);
            Assert.That(Assert.Throws<ModelException>(() => w.Apply(new Operation { type = "PourSource", to = "shaker", ingredient = "spirit", ml = ml })).Code, Is.EqualTo("INVALID_ARGUMENT")); Same(w, before);
        }
        [Test] public void IcePointDomainAndNullSemanticsAreExplicit()
        {
            var w = new World(Data); Assert.That(w.GetVessel("glass").Snapshot().temperature, Is.Null);
            Assert.That(w.Freezing(40, 0, 100).outside, Is.False);
            Assert.That(Assert.Throws<ModelException>(() => w.Freezing(50, 0, 100)).Code, Is.EqualTo("FREEZING_DOMAIN"));
            Assert.That(w.Freezing(0, 50, 100, false).outside, Is.True);
        }
        [OneTimeTearDown] public void WriteNumericalParityReport()
        {
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "C3Reports"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "parity-errors.json"), JsonUtility.ToJson(errors, true));
        }
    }
}
