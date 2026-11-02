using System;
using System.Collections.Generic;

namespace CraftingResearch.CoreV2
{
    // Plain DTOs: load the generated JSON at the host boundary; the numerical assembly needs no engine/JSON library.
    [Serializable] public sealed class Rates
    {
        public double rest, stir, shake;
        public double Get(string mode)
        {
            switch (mode) { case "rest": return rest; case "stir": return stir; case "shake": return shake; }
            throw new ModelException("INVALID_ARGUMENT", "Unknown mode");
        }
    }
    [Serializable] public sealed class VesselDefinition
    {
        public string id, kind;
        public double capacityMl, massGrams, specificHeat, liquidK, environmentK;
    }
    [Serializable] public sealed class IngredientDefinition
    {
        public string id;
        public double density, abv, sugar, acid, co2GL, temperature;
        public double[] flavor;
        [System.Runtime.Serialization.OptionalField] public double[] optical;
    }
    [Serializable] public sealed class FreezingData
    {
        public double[] ethanolWeightPercent, ethanolDepressionC, sucroseGramsPer100gWater, sucroseDepressionC;
        public double[] extendedSucroseGramsPer100gWater, extendedSucroseDepressionC;
    }
    [Serializable] public sealed class ModelData
    {
        public string model, sourceCoreSHA256, sourceModelSHA256, sourceIngredientsSHA256;
        public double specificHeat, latentHeat, step, roomTemperature, environmentK, ethanolDensity, iceDensity, icePortionGrams;
        public Rates conductance, co2LossPerSecond, wallAgitation;
        [System.Runtime.Serialization.OptionalField] public CarbonatedPourData carbonatedPour;
        public FreezingData freezing;
        public VesselDefinition[] vessels;
        public IngredientDefinition[] ingredients;
    }
    [Serializable] public sealed class VesselTemperature { public string id; public double temperature; }
    [Serializable] public sealed class WorldOptions
    {
        public string profile = "v2";
        public bool vesselThermal = true, checkTemperatures = true, recording = true;
        public double? roomTemperature, environmentK, step;
        public VesselDefinition[] vessels;
        public VesselTemperature[] vesselTemperatures;
        public OperationRegistry registry;
    }
    [Serializable] public sealed class Operation
    {
        public string type, from, to, target, ingredient, overflowPolicy;
        [System.Runtime.Serialization.OptionalField] public string pourMethod;
        public string id;
        public VesselDefinition definition;
        public bool discard;
        public double? ml, grams, portions, seconds, intensity, fraction, temperature;
        public Operation Copy() => (Operation)MemberwiseClone();
    }
    public sealed class ModelException : ArgumentException
    {
        public string Code { get; }
        public ModelException(string code, string message) : base(message) { Code = code; }
    }
    public sealed class Packet
    {
        public double m, H, water, ethanol, sugar, acid, gas;
        public double[] flavor = new double[4];
        public Packet Copy() { var p = (Packet)MemberwiseClone(); p.flavor = (double[])flavor.Clone(); return p; }
        public void Add(Packet p)
        {
            m += p.m; H += p.H; water += p.water; ethanol += p.ethanol; sugar += p.sugar; acid += p.acid; gas += p.gas;
            for (int i = 0; i < 4; i++) flavor[i] += p.flavor[i];
        }
        public Packet Scaled(double fraction)
        {
            var p = new Packet { m = m * fraction, H = H * fraction, water = water * fraction, ethanol = ethanol * fraction,
                sugar = sugar * fraction, acid = acid * fraction, gas = gas * fraction };
            for (int i = 0; i < 4; i++) p.flavor[i] = flavor[i] * fraction;
            return p;
        }
        public double Component(int i) { switch (i) { case 0: return water; case 1: return ethanol; case 2: return sugar; case 3: return acid; default: return gas; } }
    }
    public sealed class FreezingPoint
    {
        public double temperature, x, z;
        public bool outside;
    }
    public sealed class LedgerState
    {
        public double massResidual, energyResidual, compositionResidual;
        public double[] components, flavorResidual;
    }
    public sealed class VesselState
    {
        public string id;
        public string carbonatedPourMethod;public double directPourVolumeMl,guidedPourVolumeMl,co2LostDuringPour;
        public double? temperature, vesselTemperature, dilutionPercent, abvPercent, sugarPercent, acidPercent, sugarAcidRatio, co2GL, co2Retention;
        public double liquidGrams, iceGrams, meltGrams, energy, vesselEnergy, volumeMl;
        public double[] flavor, components;
        public double[] optical;
        public FreezingPoint freezingDomain;
        public bool served;
        public bool iceExchangePaused;
    }
    public sealed class WorldState
    {
        public string model, profile;
        public double time;
        public VesselState[] vessels;
        public LedgerState ledger;
    }
    [Serializable] public sealed class CarbonatedPourData { public double directFraction,guidedFraction; }
    public sealed class PourResult { public string carbonatedPourMethod; public double co2InputGrams,co2LostOnEntryGrams;public double acceptedMl, overflowMl; public bool outsideFreezingDomain; }
    public sealed class ServeResult { public double time; public VesselState vessel; }
    public sealed class OperationEvent { public double time; public Operation operation; public object result; }
    public sealed class OperationRegistry
    {
        private readonly Dictionary<string, Func<World, Operation, object>> handlers = new Dictionary<string, Func<World, Operation, object>>(StringComparer.Ordinal);
        public OperationRegistry Register(string name, Func<World, Operation, object> handler)
        {
            if (name == null || handler == null || handlers.ContainsKey(name)) throw new ModelException("INVALID_OPERATOR", "Duplicate or invalid operator");
            handlers.Add(name, handler); return this;
        }
        public object Execute(World world, Operation operation)
        {
            if (operation == null || operation.type == null || !handlers.TryGetValue(operation.type, out var handler))
                throw new ModelException("UNKNOWN_OPERATOR", "Unknown operation");
            return handler(world, operation);
        }
        public IEnumerable<string> Names => handlers.Keys;
    }
}
