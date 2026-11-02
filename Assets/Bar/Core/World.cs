using System;
using System.Collections.Generic;
using System.Linq;

namespace CraftingResearch.CoreV2
{
    public sealed class World
    {
        public const string Model = "cocktail-proxy-2.3-guided-pour";
        public ModelData Data { get; }
        public string Profile { get; }
        public double Room { get; }
        public double EnvironmentK { get; }
        public double Step { get; }
        public double Time { get; private set; }
        public double EnvironmentEnergy { get; private set; }
        public double MinimumInputTemperature { get; private set; }
        public double MaximumInputTemperature { get; private set; }
        public double MinimumLiquidus { get; private set; }
        public long TemperatureChecks { get; private set; }
        public bool CheckTemperatures { get; }
        public bool Recording { get; }
        private readonly bool thermal;
        public Packet Inputs { get; private set; } = new Packet();
        public Packet GasOut { get; private set; } = new Packet();
        public Packet Waste { get; private set; } = new Packet();
        public Packet Overflow { get; private set; } = new Packet();
        public OperationRegistry Registry { get; }
        public IReadOnlyList<OperationEvent> Events => events;
        public IReadOnlyList<Vessel> Vessels => vessels;
        public Action<World> StepCompleted;
        private readonly List<OperationEvent> events = new List<OperationEvent>();
        private readonly List<Vessel> vessels = new List<Vessel>();
        private readonly Dictionary<string, Vessel> byId = new Dictionary<string, Vessel>(StringComparer.Ordinal);
        private readonly Dictionary<string, IngredientDefinition> ingredients = new Dictionary<string, IngredientDefinition>(StringComparer.Ordinal);
        private readonly double[] ethanolFit, sugarFit;

        public World(ModelData data, WorldOptions options = null)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data)); options = options ?? new WorldOptions();
            Profile = options.profile ?? "v2";
            thermal = Profile == "v2" && options.vesselThermal;
            if (Profile != "v2" && Profile != "v1-water") Fail("INVALID_PROFILE", "Unknown profile");
            Room = Number(options.roomTemperature ?? data.roomTemperature, "room", 0, 50);
            EnvironmentK = Number(options.environmentK ?? data.environmentK, "environment K", 0, 100);
            Step = Number(options.step ?? data.step, "step", .0001, data.step);
            MinimumInputTemperature = MaximumInputTemperature = Room;
            foreach (var d in options.vessels ?? data.vessels)
            {
                Number(d.capacityMl, "capacity", 0); Number(d.massGrams, "vessel mass", 0); Number(d.specificHeat, "specific heat", 0);
                Number(d.liquidK, "liquid K", 0); Number(d.environmentK, "vessel environment K", 0);
                double temperature = Room;
                if (options.vesselTemperatures != null) foreach (var t in options.vesselTemperatures) if (t.id == d.id) temperature = t.temperature;
                Number(temperature, "vessel T", 0, 50);
                var v = new Vessel(this, d, temperature, Profile == "v2" && options.vesselThermal);
                vessels.Add(v); byId.Add(v.id, v); Inputs.H += v.vesselEnergy;
                if (v.heatCapacity != 0) { MinimumInputTemperature = Math.Min(MinimumInputTemperature, temperature); MaximumInputTemperature = Math.Max(MaximumInputTemperature, temperature); }
            }
            foreach (var d in data.ingredients) ingredients.Add(d.id, d);
            ethanolFit = Quadratic(data.freezing.ethanolWeightPercent, data.freezing.ethanolDepressionC);
            sugarFit = Quadratic(data.freezing.sucroseGramsPer100gWater, data.freezing.sucroseDepressionC);
            Registry = options.registry ?? CreateRegistry(); Recording = options.recording;
            CheckTemperatures = Profile == "v2" && options.checkTemperatures;
        }
        private static void Fail(string code, string message) => throw new ModelException(code, message);
        private static double Number(double x, string name, double min = double.NegativeInfinity, double max = double.PositiveInfinity)
        { if (double.IsNaN(x) || double.IsInfinity(x) || x < min || x > max) Fail("INVALID_ARGUMENT", name); return x; }
        public static double[] Quadratic(double[] x, double[] y)
        {
            double a = 0, b = 0, c = 0, d = 0, e = 0;
            for (int i = 0; i < x.Length; i++) { a += Math.Pow(x[i], 2); b += Math.Pow(x[i], 3); c += Math.Pow(x[i], 4); d += x[i] * y[i]; e += Math.Pow(x[i], 2) * y[i]; }
            return new[] { (d * c - e * b) / (a * c - b * b), (a * e - b * d) / (a * c - b * b) };
        }
        // Unity's .NET profile lacks Math.Expm1. This is the same scalar function, not a mass-dependent model special case.
        public static double Expm1(double x)
        {
            if (Math.Abs(x) >= .5) return Math.Exp(x) - 1;
            double term = x, sum = x;
            for (int n = 2; n <= 40; n++) { term *= x / n; double next = sum + term; if (next == sum) break; sum = next; }
            return sum;
        }
        public static double Relaxation(double t1, double t2, double c1, double c2, double k, double dt)
        {
            double inverse = 1 / c1 + (double.IsPositiveInfinity(c2) ? 0 : 1 / c2);
            return (t1 - t2) * (-Expm1(-k * dt * inverse)) / inverse;
        }
        private double Fp(double ethanol, double sugar, double mass)
        {
            double x = 100 * ethanol / (mass - sugar), z = 100 * sugar / (mass - ethanol - sugar);
            double depression = sugarFit[0] * z + sugarFit[1] * z * z;
            if (z > 30)
            {
                var xs = Data.freezing.extendedSucroseGramsPer100gWater; var ys = Data.freezing.extendedSucroseDepressionC;
                int i = 1; while (i < xs.Length - 1 && xs[i] < z) i++;
                double y0 = i == 1 ? sugarFit[0] * 30 + sugarFit[1] * 30 * 30 : ys[i - 1];
                depression = y0 + (ys[i] - y0) * (z - xs[i - 1]) / (xs[i] - xs[i - 1]);
            }
            return -(ethanolFit[0] * x + ethanolFit[1] * x * x + depression);
        }
        public FreezingPoint Freezing(double ethanol, double sugar, double mass, bool strict = true)
        {
            if (double.IsNaN(mass) || double.IsInfinity(mass) || mass <= 0 || ethanol < 0 || sugar < 0 || ethanol + sugar >= mass) Fail("INVALID_COMPOSITION", "Invalid liquidus composition");
            double x = 100 * ethanol / (mass - sugar), z = 100 * sugar / (mass - ethanol - sugar);
            bool outside = x > 46.3 + 1e-10 || z > 30 + 1e-10;
            if (strict && outside) Fail("FREEZING_DOMAIN", "Liquidus domain exceeded");
            return new FreezingPoint { temperature = -(ethanolFit[0] * x + ethanolFit[1] * x * x + sugarFit[0] * z + sugarFit[1] * z * z), x = x, z = z, outside = outside };
        }
        /// <summary>读取扩展糖浓度范围的冰点；超域时 outside=true，temperature 不可使用。</summary>
        public FreezingPoint IceFreezing(double ethanol, double sugar, double mass)
        {
            var f = Freezing(ethanol, sugar, mass, false); var xs = Data.freezing.extendedSucroseGramsPer100gWater;
            f.outside = f.x > 46.3 + 1e-10 || f.z > xs[xs.Length - 1] + 1e-10; f.temperature = f.outside ? 0 : Fp(ethanol, sugar, mass); return f;
        }
        /// <summary>注册独立容器并把初始容器热能记入输入账本；ID 必须未使用，容量正且参数有限。</summary>
        public Vessel RegisterVessel(VesselDefinition definition, double? initialTemperature = null)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.id) || byId.ContainsKey(definition.id) || string.IsNullOrWhiteSpace(definition.kind)) Fail("INVALID_VESSEL", "Invalid or duplicate vessel");
            Number(definition.capacityMl, "capacity", double.Epsilon); Number(definition.massGrams, "mass", 0); Number(definition.specificHeat, "specific heat", 0); Number(definition.liquidK, "liquid K", 0); Number(definition.environmentK, "environment K", 0);
            double t = Number(initialTemperature ?? Room, "vessel T", 0, 50);
            var d = new VesselDefinition { id = definition.id, kind = definition.kind, capacityMl = definition.capacityMl, massGrams = definition.massGrams, specificHeat = definition.specificHeat, liquidK = definition.liquidK, environmentK = definition.environmentK };
            var v = new Vessel(this, d, t, thermal); vessels.Add(v); byId.Add(v.id, v); Inputs.H += v.vesselEnergy;
            if (v.heatCapacity != 0) { MinimumInputTemperature = Math.Min(MinimumInputTemperature, t); MaximumInputTemperature = Math.Max(MaximumInputTemperature, t); } return v;
        }
        /// <summary>注销空容器；非空时须显式 discard，内容和容器剩余热能进入废料账本。</summary>
        public void UnregisterVessel(string id, bool discard = false)
        {
            var v = GetVessel(id); if (!discard && (v.m > 0 || v.ice > 0)) Fail("VESSEL_NOT_EMPTY", "Empty or explicitly discard first");
            if (discard) { DiscardLiquid(id); DiscardIce(id); } Waste.H += v.vesselEnergy; vessels.Remove(v); byId.Remove(id);
        }
        public Vessel GetVessel(string id)
        {
            if (id == null || !byId.TryGetValue(id, out var v)) { Fail("UNKNOWN_VESSEL", id); return null; }
            if (v.served) Fail("ALREADY_SERVED", id); return v;
        }
        private void Capacity(Vessel v, double extra) { if (v.OccupiedVolume + extra > v.capacity + 1e-8) Fail("CAPACITY", v.id); }
        private void CheckMix(Vessel v, Packet p, double fraction)
        {
            double mass = v.m + p.m * fraction; if (mass == 0 || Profile == "v1-water") return;
            var f = Freezing(v.ethanol + p.ethanol * fraction, v.sugar + p.sugar * fraction, mass, false);
            if (!f.outside && (v.E + p.H * fraction) / (Data.specificHeat * mass) < f.temperature - 1e-8) Fail("REFREEZING_UNSUPPORTED", "Mix requires refreezing");
        }
        public PourResult Pour(string id, string ingredientId, double ml, double? temperature = null, string policy = "reject")
        {
            var v = GetVessel(id);
            if (ingredientId == null || !ingredients.TryGetValue(ingredientId, out var x)) { Fail("UNKNOWN_INGREDIENT", ingredientId); return null; }
            Number(ml, "pour mL", 0, 5000); double t = Number(temperature ?? x.temperature, "batch temperature", -30, 50); policy = policy ?? "reject";
            if (policy != "reject" && policy != "overflow") Fail("INVALID_ARGUMENT", "Overflow policy");
            if (Profile == "v1-water" && (x.abv != 0 || x.co2GL != 0)) Fail("LEGACY_COMPONENT", "Legacy profile excludes ethanol/CO2");
            if (ml == 0) return null;
            double mass = ml * x.density;
            var p = new Packet { m = mass, H = Data.specificHeat * mass * t, ethanol = ml * x.abv * Data.ethanolDensity,
                sugar = mass * x.sugar, acid = mass * x.acid, gas = ml * x.co2GL / 1000 };
            p.water = mass - p.ethanol - p.sugar - p.acid - p.gas; if (p.water < 0) Fail("INVALID_COMPOSITION", "Negative water");
            for (int i = 0; i < 4; i++) p.flavor[i] = mass * x.flavor[i];
            double accepted = policy == "overflow" ? Math.Min(ml, Math.Max(0, v.capacity - v.OccupiedVolume)) : ml;
            if (policy == "reject") Capacity(v, ml);
            double a = accepted / ml; CheckMix(v, p, a);
            if (accepted != 0) { MinimumInputTemperature = Math.Min(MinimumInputTemperature, t); MaximumInputTemperature = Math.Max(MaximumInputTemperature, t); }
            Inputs.Add(p); v.m += p.m * a; v.E += p.H * a; v.water += p.water * a; v.ethanol += p.ethanol * a;
            v.sugar += p.sugar * a; v.acid += p.acid * a; v.gas += p.gas * a; v.volume += accepted; v.gasInput += p.gas * a;
            for (int i = 0; i < 4; i++) v.flavor[i] += p.flavor[i] * a;
            for(int i=0;i<7;i++)v.optical[i]+=mass*(x.optical==null?0:x.optical[i])*a;
            if (a < 1) Overflow.Add(p.Scaled(1 - a));
            return new PourResult { acceptedMl = accepted, overflowMl = ml - accepted, outsideFreezingDomain = v.m != 0 && Freezing(v.ethanol, v.sugar, v.m, false).outside };
        }
        /// <summary>显式现代碳酸加料；旧 PourSource/TopUp 保留冻结的无入杯失气语义。</summary>
        public PourResult PourCarbonated(string id,string ingredientId,double ml,string method,double? temperature=null,string policy="reject")
        {
            if(method!="Direct"&&method!="BarspoonGuided")Fail("INVALID_POUR_METHOD","Explicit carbonated method required");
            Number(ml,"carbonated pour mL",double.Epsilon,5000);
            if(!ingredients.TryGetValue(ingredientId,out var x)||x.co2GL<=0)Fail("CARBONATED_REQUIRED","Carbonated ingredient required");
            if(Data.carbonatedPour==null)Fail("POUR_CONFIG_REQUIRED","carbonatedPour config required");
            var config=Data.carbonatedPour;Number(config.directFraction,"directFraction",0,1);Number(config.guidedFraction,"guidedFraction",0,1);
            var result=Pour(id,ingredientId,ml,temperature,policy);var v=GetVessel(id);
            double initial=result.acceptedMl*x.co2GL/1000,loss=initial*(method=="Direct"?config.directFraction:config.guidedFraction),t=temperature.HasValue?temperature.Value:x.temperature,h=Data.specificHeat*loss*t;
            v.gas-=loss;v.m-=loss;v.E-=h;v.volume-=loss;GasOut.gas+=loss;GasOut.m+=loss;GasOut.H+=h;
            if(method=="Direct")v.directPourVolumeMl+=result.acceptedMl;else v.guidedPourVolumeMl+=result.acceptedMl;v.co2LostDuringPour+=loss;
            result.carbonatedPourMethod=method;result.co2InputGrams=initial;result.co2LostOnEntryGrams=loss;return result;
        }
        public void AddLegacy(string id, double mass, double temperature, double sugar, double acid)
        {
            if (Profile != "v1-water") Fail("INVALID_PROFILE", "Legacy mass-based addition only");
            var v = GetVessel(id); Number(mass, "mass", double.Epsilon); Number(temperature, "T", 0); Number(sugar, "sugar", 0); Number(acid, "acid", 0);
            if (sugar + acid > mass) Fail("INVALID_COMPOSITION", "Solutes exceed mass"); Capacity(v, mass);
            var p = new Packet { m = mass, H = mass * Data.specificHeat * temperature, water = mass - sugar - acid, sugar = sugar, acid = acid };
            Inputs.Add(p); v.m += mass; v.water += p.water; v.sugar += sugar; v.acid += acid; v.E += p.H; v.volume += mass;
        }
        public void AddIce(string id, double mass)
        {
            var v = GetVessel(id); Number(mass, "ice mass", 0, 5000); Capacity(v, mass / Data.iceDensity);
            v.ice += mass; Inputs.m += mass; Inputs.water += mass; Inputs.H -= Data.latentHeat * mass;
        }
        public void Transfer(string from, string to, double fraction = 1)
        {
            var a = GetVessel(from); var b = GetVessel(to); if (a == b) Fail("INVALID_TRANSFER", "Same source and destination"); Number(fraction, "fraction", 0, 1);
            Capacity(b, a.volume * fraction); CheckMix(b, new Packet { m = a.m, H = a.E, ethanol = a.ethanol, sugar = a.sugar }, fraction);
            double x=a.directPourVolumeMl*fraction;a.directPourVolumeMl-=x;b.directPourVolumeMl+=x;x=a.guidedPourVolumeMl*fraction;a.guidedPourVolumeMl-=x;b.guidedPourVolumeMl+=x;x=a.co2LostDuringPour*fraction;a.co2LostDuringPour-=x;b.co2LostDuringPour+=x;
            x = a.m * fraction; a.m -= x; b.m += x; x = a.E * fraction; a.E -= x; b.E += x;
            x = a.water * fraction; a.water -= x; b.water += x; x = a.ethanol * fraction; a.ethanol -= x; b.ethanol += x;
            x = a.sugar * fraction; a.sugar -= x; b.sugar += x; x = a.acid * fraction; a.acid -= x; b.acid += x;
            x = a.gas * fraction; a.gas -= x; b.gas += x; x = a.melt * fraction; a.melt -= x; b.melt += x;
            x = a.volume * fraction; a.volume -= x; b.volume += x; x = a.gasInput * fraction; a.gasInput -= x; b.gasInput += x;
            for (int i = 0; i < 4; i++) { x = a.flavor[i] * fraction; a.flavor[i] -= x; b.flavor[i] += x; }
            for(int i=0;i<7;i++){var opticalAmount=a.optical[i]*fraction;a.optical[i]-=opticalAmount;b.optical[i]+=opticalAmount;}
        }
        public double DiscardIce(string id)
        {
            var v = GetVessel(id); double m = v.ice; v.ice = 0; Waste.m += m; Waste.water += m; Waste.H -= Data.latentHeat * m; return m;
        }
        /// <summary>按同一比例转移液体及冰；检查总占位容量，容器本身热量不转移。</summary>
        public void TransferWithIce(string from, string to, double fraction = 1)
        {
            var a = GetVessel(from); var b = GetVessel(to); Number(fraction, "fraction", 0, 1); if (a == b) Fail("INVALID_TRANSFER", "Same endpoints");
            Capacity(b, a.OccupiedVolume * fraction); CheckMix(b, new Packet { m = a.m, H = a.E, ethanol = a.ethanol, sugar = a.sugar }, fraction);
            Transfer(from, to, fraction); double ice = a.ice * fraction; a.ice -= ice; b.ice += ice;
        }
        public void DiscardLiquid(string id)
        {
            var v = GetVessel(id); Waste.Add(new Packet { m = v.m, H = v.E, water = v.water, ethanol = v.ethanol, sugar = v.sugar, acid = v.acid, gas = v.gas, flavor = (double[])v.flavor.Clone() });
            v.directPourVolumeMl=v.guidedPourVolumeMl=v.co2LostDuringPour=0;
            v.m = v.E = v.water = v.ethanol = v.sugar = v.acid = v.gas = v.melt = v.volume = v.gasInput = 0; Array.Clear(v.flavor, 0, 4); Array.Clear(v.optical,0,7);
        }
        public void Advance(double seconds, string target = "shaker", string mode = "rest", double intensity = 1, double? legacyK = null, double? step = null)
        {
            double h = step ?? Step; Number(seconds, "duration", 0, 3600); Number(intensity, "intensity", 0, 1); Number(h, "step", .0001, Profile == "v1-water" ? 3600 : Data.step);
            Data.conductance.Get(mode); var active = GetVessel(target);
            if (mode == "shake" && active.kind != "shaker") Fail("TOOL_REQUIRED", "Shake requires shaker");
            if (legacyK.HasValue && (Profile != "v1-water" || double.IsNaN(legacyK.Value) || double.IsInfinity(legacyK.Value) || legacyK.Value < 0)) Fail("INVALID_ARGUMENT", "Legacy K");
            var vs = vessels.Where(v => !v.served).ToArray();
            if (Profile == "v2") foreach (var v in vs) { v.iceExchangePaused = false; if (v.m != 0 && v.ice != 0) { var f = IceFreezing(v.ethanol, v.sugar, v.m); v.iceExchangePaused = f.outside; if (!f.outside) { MinimumLiquidus = Math.Min(MinimumLiquidus, f.temperature); if (v.Temperature.Value < f.temperature - 1e-8) Fail("REFREEZING_UNSUPPORTED", "Below liquidus"); } } }
            int count = (int)Math.Ceiling(seconds / h);
            for (int n = 0; n < count; n++)
            {
                double dt = Math.Min(h, seconds - n * h); if (dt <= 0) break;
                foreach (var v in vs)
                {
                    string action = v == active ? mode : "rest";
                    double k = legacyK.HasValue && v == active ? legacyK.Value : Data.conductance.Get(action) * (action == "rest" ? 1 : intensity);
                    var d = v.definition;
                    if (v.heatCapacity != 0) { double q = Relaxation(Room, v.VesselTemperature.Value, v.heatCapacity, double.PositiveInfinity, d.environmentK, dt); v.vesselEnergy += q; EnvironmentEnergy += q; }
                    if (v.m == 0)
                    {
                        if (v.heatCapacity != 0 && v.ice != 0 && v.vesselEnergy > 0)
                        {
                            double q = Relaxation(v.VesselTemperature.Value, 0, v.heatCapacity, double.PositiveInfinity, d.liquidK, dt), dm = Math.Min(v.ice, q / Data.latentHeat);
                            v.vesselEnergy -= Data.latentHeat * dm; v.ice -= dm; v.m += dm; v.water += dm; v.melt += dm; v.volume += dm;
                        }
                        if (CheckTemperatures) AssertTemperatureBounds(v); continue;
                    }
                    double t = v.Temperature.Value, qv = 0;
                    // 2.2 (2026-10-28, see Tools/WallFrost): agitation also drives the liquid against the wall.
                    double wall = action == "rest" ? 1 : 1 + (Data.wallAgitation.Get(action) - 1) * intensity;
                    if (v.heatCapacity != 0) qv = Relaxation(v.VesselTemperature.Value, t, v.heatCapacity, Data.specificHeat * v.m, d.liquidK * wall, dt);
                    double qe = Profile == "v1-water" ? EnvironmentK * (Room - t) * dt : v.heatCapacity != 0 ? 0 : Relaxation(Room, t, Data.specificHeat * v.m, double.PositiveInfinity, EnvironmentK, dt);
                    double melted;
                    if (Profile == "v1-water")
                    {
                        if (v.E + qe < 0) Fail("UNSTABLE_STEP", "Legacy negative energy");
                        double q = Math.Min(Math.Min(k * t * dt, v.E + qe), Data.latentHeat * v.ice); melted = Math.Min(v.ice, q / Data.latentHeat); q = melted * Data.latentHeat; v.E = Math.Max(0, v.E + qe - q);
                    }
                    else if (v.ice == 0 || v.iceExchangePaused) { melted = 0; v.E += qe + qv; }
                    else
                    {
                        double energy = v.E + qe + qv, f = Fp(v.ethanol, v.sugar, v.m), afterExchange = energy / (Data.specificHeat * v.m);
                        double proposal = Math.Min(v.ice, Math.Max(0, Relaxation(afterExchange, f, Data.specificHeat * v.m, double.PositiveInfinity, k, dt) / Data.latentHeat));
                        Func<double, double> gap = dm => (energy - Data.latentHeat * dm) / (Data.specificHeat * (v.m + dm)) - Fp(v.ethanol, v.sugar, v.m + dm);
                        if (gap(0) < -1e-8) Fail("REFREEZING_UNSUPPORTED", "Heat exchange requires refreezing");
                        melted = proposal;
                        if (gap(proposal) < 0) { double lo = 0, hi = proposal; for (int j = 0; j < 48; j++) { double mid = (lo + hi) / 2; if (gap(mid) >= 0) lo = mid; else hi = mid; } melted = lo; }
                        v.E = energy - Data.latentHeat * melted;
                    }
                    v.vesselEnergy -= qv; EnvironmentEnergy += qe;
                    v.ice = Math.Max(0, v.ice - melted); v.m += melted; v.water += melted; v.melt += melted; v.volume += melted;
                    double rate = Data.co2LossPerSecond.Get(action), loss = v.gas * (1 - Math.Exp(-rate * dt)), energyLoss = Data.specificHeat * loss * v.Temperature.Value;
                    v.gas -= loss; v.m -= loss; v.E -= energyLoss; v.volume -= loss; GasOut.m += loss; GasOut.gas += loss; GasOut.H += energyLoss;
                    if (CheckTemperatures) AssertTemperatureBounds(v);
                }
                StepCompleted?.Invoke(this);
            }
            Time += seconds;
        }
        public void AssertTemperatureBounds(Vessel v)
        {
            double lower = Math.Min(Math.Min(MinimumInputTemperature, MinimumLiquidus), 0), upper = MaximumInputTemperature;
            foreach (double? t in new[] { v.Temperature, v.VesselTemperature }) if (t.HasValue)
            { TemperatureChecks++; if (double.IsNaN(t.Value) || double.IsInfinity(t.Value) || t.Value < lower - 1e-8 || t.Value > upper + 1e-8) Fail("TEMPERATURE_INVARIANT", "Temperature outside maximum-principle bounds"); }
        }
        public object Apply(Operation operation)
        {
            double time = Time, env = EnvironmentEnergy, min = MinimumInputTemperature, max = MaximumInputTemperature, liquidus = MinimumLiquidus;
            var input = Inputs.Copy(); var gas = GasOut.Copy(); var waste = Waste.Copy(); var overflow = Overflow.Copy(); var saved = vessels.Select(v => v.Copy()).ToArray();
            try
            {
                var result = Registry.Execute(this, operation);
                if (Recording) events.Add(new OperationEvent { time = Time, operation = operation.Copy(), result = result }); return result;
            }
            catch
            {
                Time = time; EnvironmentEnergy = env; MinimumInputTemperature = min; MaximumInputTemperature = max; MinimumLiquidus = liquidus;
                Inputs = input; GasOut = gas; Waste = waste; Overflow = overflow;
                vessels.Clear(); byId.Clear(); foreach (var v in saved) { vessels.Add(v); byId.Add(v.id, v); } throw;
            }
        }
        public LedgerState Ledger()
        {
            var outs = new[] { GasOut, Waste, Overflow };
            var result = new LedgerState { massResidual = vessels.Sum(v => v.m + v.ice) + outs.Sum(p => p.m) - Inputs.m,
                energyResidual = vessels.Sum(v => v.E - Data.latentHeat * v.ice + v.vesselEnergy) + outs.Sum(p => p.H) - Inputs.H - EnvironmentEnergy,
                components = new double[5], flavorResidual = new double[4] };
            for (int i = 0; i < 5; i++) result.components[i] = vessels.Sum(v => v.Component(i) + (i == 0 ? v.ice : 0)) + outs.Sum(p => p.Component(i)) - Inputs.Component(i);
            for (int i = 0; i < 4; i++) result.flavorResidual[i] = vessels.Sum(v => v.flavor[i]) + outs.Sum(p => p.flavor[i]) - Inputs.flavor[i];
            result.compositionResidual = vessels.Count == 0 ? 0 : vessels.Max(v => Math.Abs(v.m - (v.water + v.ethanol + v.sugar + v.acid + v.gas))); return result;
        }
        public WorldState Snapshot() => new WorldState { model = Model, profile = Profile, time = Time, vessels = vessels.Select(v => v.Snapshot()).ToArray(), ledger = Ledger() };
        public static OperationRegistry CreateRegistry()
        {
            return new OperationRegistry()
                .Register("PourCarbonated",(w,o)=>w.PourCarbonated(o.to,o.ingredient,o.ml.Value,o.pourMethod,o.temperature,o.overflowPolicy))
                .Register("PourSource", (w, o) => w.Pour(o.to, o.ingredient, o.ml ?? double.NaN, o.temperature, o.overflowPolicy))
                .Register("TopUp", (w, o) => w.Pour(o.to, o.ingredient, o.ml ?? double.NaN, o.temperature, o.overflowPolicy))
                .Register("TransferLiquid", (w, o) => { w.Transfer(o.from, o.to, o.fraction ?? 1); return null; })
                .Register("TransferWithIce", (w, o) => { w.TransferWithIce(o.from, o.to, o.fraction ?? 1); return null; })
                .Register("RegisterVessel", (w, o) => w.RegisterVessel(o.definition, o.temperature))
                .Register("UnregisterVessel", (w, o) => { w.UnregisterVessel(o.from, o.discard); return null; })
                .Register("StrainTransfer", (w, o) => { if (w.GetVessel(o.from).kind != "shaker") Fail("TOOL_REQUIRED", "Straining requires shaker"); w.Transfer(o.from, o.to, o.fraction ?? 1); return null; })
                .Register("AddIce", (w, o) => { w.AddIce(o.to, o.grams ?? Number(o.portions ?? double.NaN, "portions", 0) * w.Data.icePortionGrams); return null; })
                .Register("DiscardIce", (w, o) => w.DiscardIce(o.from))
                .Register("DiscardLiquid", (w, o) => { w.DiscardLiquid(o.from); return null; })
                .Register("Shake", (w, o) => { w.Advance(o.seconds ?? double.NaN, o.target, "shake", o.intensity ?? 1); return null; })
                .Register("Stir", (w, o) => { w.Advance(o.seconds ?? double.NaN, o.target, "stir", o.intensity ?? 1); return null; })
                .Register("Rest", (w, o) => { w.Advance(o.seconds ?? double.NaN, o.target ?? "shaker", "rest"); return null; })
                .Register("Serve", (w, o) => { var v = w.GetVessel(o.from); if (v.kind != "glass" || v.m == 0) Fail("INVALID_SERVE", "Serve requires nonempty glass"); var state = v.Snapshot(); v.served = true; return new ServeResult { time = w.Time, vessel = state }; });
        }
    }
}
