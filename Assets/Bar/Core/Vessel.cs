using System;

namespace CraftingResearch.CoreV2
{
    public sealed class Vessel
    {
        public readonly string id, kind;
        public readonly VesselDefinition definition;
        public readonly double capacity, heatCapacity;
        private readonly World world;
        public double m, ice, E, ethanol, sugar, acid, gas, water, melt, volume, gasInput, vesselEnergy;
        public double directPourVolumeMl,guidedPourVolumeMl,co2LostDuringPour;
        public double[] flavor = new double[4];
        public double[] optical = new double[7];
        public bool served;
        public bool iceExchangePaused;
        internal Vessel(World world, VesselDefinition definition, double temperature, bool thermal)
        {
            this.world = world; this.definition = definition; id = definition.id; kind = definition.kind;
            capacity = definition.capacityMl; heatCapacity = thermal ? definition.massGrams * definition.specificHeat : 0;
            vesselEnergy = heatCapacity * temperature;
        }
        public double? Temperature => m != 0 ? E / (world.Data.specificHeat * m) : (double?)null;
        public double? VesselTemperature => heatCapacity != 0 ? vesselEnergy / heatCapacity : (double?)null;
        public double OccupiedVolume => volume + ice / world.Data.iceDensity;
        public double Component(int i) { switch (i) { case 0: return water; case 1: return ethanol; case 2: return sugar; case 3: return acid; default: return gas; } }
        internal Vessel Copy() { var v = (Vessel)MemberwiseClone(); v.flavor = (double[])flavor.Clone(); v.optical=(double[])optical.Clone(); return v; }
        internal void Restore(Vessel v)
        {
            directPourVolumeMl=v.directPourVolumeMl;guidedPourVolumeMl=v.guidedPourVolumeMl;co2LostDuringPour=v.co2LostDuringPour;
            m = v.m; ice = v.ice; E = v.E; ethanol = v.ethanol; sugar = v.sugar; acid = v.acid; gas = v.gas; water = v.water;
            melt = v.melt; volume = v.volume; gasInput = v.gasInput; vesselEnergy = v.vesselEnergy; served = v.served; iceExchangePaused = v.iceExchangePaused; flavor = (double[])v.flavor.Clone(); optical=(double[])v.optical.Clone();
        }
        public VesselState Snapshot()
        {
            var f = new double[4]; for (int i = 0; i < 4; i++) f[i] = m != 0 ? flavor[i] / m : 0;
            return new VesselState { carbonatedPourMethod=directPourVolumeMl>0?(guidedPourVolumeMl>0?"Mixed":"Direct"):(guidedPourVolumeMl>0?"BarspoonGuided":"None"),directPourVolumeMl=directPourVolumeMl,guidedPourVolumeMl=guidedPourVolumeMl,co2LostDuringPour=co2LostDuringPour,id = id, temperature = Temperature, vesselTemperature = VesselTemperature,
                liquidGrams = m, iceGrams = ice, meltGrams = melt, energy = E, vesselEnergy = vesselEnergy,
                dilutionPercent = m != 0 ? 100 * melt / m : (double?)null,
                abvPercent = volume != 0 ? 100 * ethanol / world.Data.ethanolDensity / volume : (double?)null,
                sugarPercent = m != 0 ? 100 * sugar / m : (double?)null, acidPercent = m != 0 ? 100 * acid / m : (double?)null,
                sugarAcidRatio = acid != 0 ? sugar / acid : (double?)null, co2GL = volume != 0 ? 1000 * gas / volume : (double?)null,
                co2Retention = gasInput != 0 ? gas / gasInput : (double?)null, volumeMl = volume,
                optical = Array.ConvertAll(optical,n=>m!=0?n/m:0), flavor = f, components = new[] { water, ethanol, sugar, acid, gas },
                freezingDomain = m != 0 ? world.Freezing(ethanol, sugar, m, false) : null, iceExchangePaused = iceExchangePaused, served = served };
        }
    }
}
