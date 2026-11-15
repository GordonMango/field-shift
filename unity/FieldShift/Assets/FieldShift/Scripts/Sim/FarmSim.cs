using System;
using System.Collections.Generic;
using UnityEngine;

namespace FieldShift
{
    public enum LandStatus { Farm, Available, Reserve, Water, Steep }
    public enum IrrigationType { None, Sprinkler, Drip }

    public class ParcelPlan
    {
        public string main;                     // cash crop or Indigenous land use
        public string cover;                    // cover crop sown after harvest (may be null)
        public IrrigationType irrigation;
        public bool crew = true;                // true: hire a crew for planting and harvest; false: do it yourself on the tractor
        public string fert = "synthetic";       // none, synthetic, manure, compost
        public ParcelPlan Clone() => (ParcelPlan)MemberwiseClone();
    }

    public class Planting
    {
        public CropDef def;
        public float gdd, health = 100, nStored, nFixed, nShort;
        public int days, harvests, waterlog;
        public int years;                       // seasons since planting (perennials come into full yield over time)
        public bool dead;                       // winter-killed cover crop left as mulch
        public bool harvestedThisYear, fedThisYear;
        public readonly Dictionary<string, float> loss = new Dictionary<string, float>();

        public float Canopy
        {
            get
            {
                if (dead) return 0;
                if (def.perennial && harvests > 0) return Mathf.Clamp(0.3f + gdd / Mathf.Max(1, def.regrowGdd > 0 ? def.regrowGdd : def.gddToMaturity), 0.3f, 1);
                return Mathf.Clamp(gdd / (0.45f * Mathf.Max(1, def.gddToMaturity)), 0.05f, 1);
            }
        }

        public Planting Clone()
        {
            var l = new Planting { def = def, gdd = gdd, health = health, nStored = nStored, nFixed = nFixed, nShort = nShort, days = days, harvests = harvests, waterlog = waterlog, dead = dead, harvestedThisYear = harvestedThisYear, years = years };
            foreach (var kv in loss) l.loss[kv.Key] = kv.Value;
            return l;
        }
    }

    /// <summary>Season totals per parcel (per hectare, since a parcel is one hectare).</summary>
    public class YearStats
    {
        public float revenue, costs, irrigationMm, nLeached, erosion, floodDays, omStart, omEnd, nFixed;
        // nutrients (kg/ha): N, P2O5 and K2O applied, phosphorus washed off with eroded soil
        public float nApplied, pApplied, kApplied, pLost, fertCost, dieselL;
        // greenhouse gases (kg CO2e/ha)
        public float ghgN2O, ghgFert, ghgFuel, ghgIrr, ghgCH4;
        public float GhgFlowsKg => ghgN2O + ghgFert + ghgFuel + ghgIrr + ghgCH4;
        /// <summary>Soil carbon: organic matter gained is CO2 taken out of the air (negative), lost is released.</summary>
        public float SoilCarbonKg(EmissionFactors ef, float omEndNow) => -(omEndNow - omStart) * ef.soilCO2PerPercentOM * 1000f;
        public float lossDrought, lossHeat, lossFrost, lossFlood, lossPest;
        public int harvests, cropsLost;
        public float Net => revenue - costs;
        public void Add(YearStats o)
        {
            revenue += o.revenue; costs += o.costs; irrigationMm += o.irrigationMm; nLeached += o.nLeached; erosion += o.erosion;
            floodDays += o.floodDays; omStart += o.omStart; omEnd += o.omEnd; nFixed += o.nFixed;
            lossDrought += o.lossDrought; lossHeat += o.lossHeat; lossFrost += o.lossFrost; lossFlood += o.lossFlood; lossPest += o.lossPest;
            harvests += o.harvests; cropsLost += o.cropsLost;
            nApplied += o.nApplied; pApplied += o.pApplied; kApplied += o.kApplied; pLost += o.pLost; fertCost += o.fertCost; dieselL += o.dieselL;
            ghgN2O += o.ghgN2O; ghgFert += o.ghgFert; ghgFuel += o.ghgFuel; ghgIrr += o.ghgIrr; ghgCH4 += o.ghgCH4;
        }
    }

    public class Parcel
    {
        public const float SizeM = 100f;         // one hectare
        public int ix, iz;
        public float x, z, elevation, slopeDeg;
        public bool lowGround, edge;
        public int reserveId;
        public LandStatus status;
        public bool leased;                      // farm parcels the player leases (vs. inherited)

        // soil state
        public float soilWater, sat, om, nitrogen, topsoil = 100, pest = 0.02f, floodDepth;
        public float phosphorus, potassium, slowN;   // kg/ha: P2O5, K2O, and organic N still to be released
        public int dryDays;
        public Planting current;
        public string lastFamily;
        public IrrigationType installed;
        public ParcelPlan plan = new ParcelPlan();
        public YearStats year = new YearStats();
        public int index;

        // field work waiting for the player's tractor
        public string job;                       // "plant", "harvest" or null
        public CropDef jobCrop;
        public float jobFraction = 1;
        public int jobWaitDays;

        public bool IsFarm => status == LandStatus.Farm;

        public Parcel CloneForSim()
        {
            var p = (Parcel)MemberwiseClone();
            p.plan = plan.Clone();
            p.current = current?.Clone();
            p.year = new YearStats();
            return p;
        }
    }

    public struct SimEvent
    {
        public string kind;     // harvest, lost, flood, heat, frost, dry, restriction, wintekill, planted
        public string text;
        public Parcel parcel;
        public float value;
    }

    /// <summary>
    /// Daily farm simulation driven by real NASA POWER weather. Used both by the live game and, on cloned
    /// parcels, by the Rotation Report to replay a year under different strategies.
    /// </summary>
    public class FarmSim
    {
        public const int Grid = 20;               // 20 x 20 parcels of 1 ha around the farm
        public const float FertiliserPerKgN = 1.8f, PumpingPerMm = 0.5f, InterestRate = 0.065f;
        public const float CrewPlantPerHa = 90f, CrewHarvestPerHa = 140f, FuelPerHa = 25f;
        public static readonly float[] IrrigationInstallCost = { 0, 2000, 3800 };
        public static readonly float[] IrrigationEfficiency = { 0, 0.72f, 0.9f };

        public readonly Catalog cat;
        public readonly LocationDef loc;
        public readonly WeatherSeries weather;
        public Parcel[] parcels;                  // Grid * Grid, index = iz * Grid + ix
        public float money, floodLevel, floodBase, allocationUsedMm, restriction = 1;
        public Vector2 gridCenter;
        public int dayIndex;
        public bool trackMoney = true;
        public bool autoAll;                      // the Rotation Report runs every field with a crew
        public readonly List<SimEvent> events = new List<SimEvent>();
        public float livingCostsThisYear, leaseThisYear, interestThisYear;

        public FarmSim(Catalog cat, LocationDef loc, WeatherSeries weather)
        {
            this.cat = cat; this.loc = loc; this.weather = weather;
        }

        public DateTime Date => weather.start.AddDays(dayIndex);
        public Parcel At(int ix, int iz) => ix < 0 || iz < 0 || ix >= Grid || iz >= Grid ? null : parcels[iz * Grid + ix];

        public IEnumerable<Parcel> Farm()
        {
            foreach (var p in parcels) if (p.IsFarm) yield return p;
        }

        public int FarmCount { get { int n = 0; foreach (var p in parcels) if (p.IsFarm) n++; return n; } }

        // ------------------------------------------------------------------ setup
        public void BuildParcels(LocationData ld)
        {
            parcels = new Parcel[Grid * Grid];
            gridCenter = ld.gridCenter;
            float half = Grid * Parcel.SizeM * 0.5f;
            var elevations = new List<float>();
            for (int iz = 0; iz < Grid; iz++)
                for (int ix = 0; ix < Grid; ix++)
                {
                    float x = gridCenter.x - half + (ix + 0.5f) * Parcel.SizeM, z = gridCenter.y - half + (iz + 0.5f) * Parcel.SizeM;
                    var p = new Parcel { ix = ix, iz = iz, x = x, z = z, index = iz * Grid + ix };
                    // sample elevation, slope, reserve and water across the parcel
                    float hMin = float.MaxValue, hMax = float.MinValue, hSum = 0; int reserveHits = 0, waterHits = 0, n = 0, rid = 0;
                    for (int sz = 0; sz < 5; sz++)
                        for (int sx = 0; sx < 5; sx++)
                        {
                            float px = x - 40 + sx * 20, pz = z - 40 + sz * 20;
                            float h = ld.ElevationAt(px, pz);
                            hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h); hSum += h; n++;
                            int r = ld.ReserveAt(px, pz);
                            if (r > 0) { reserveHits++; rid = r; }
                            if (ld.WaterAt(px, pz)) waterHits++;
                        }
                    p.elevation = hSum / n;
                    p.slopeDeg = Mathf.Atan((hMax - hMin) / 80f) * Mathf.Rad2Deg;
                    p.reserveId = rid;
                    if (reserveHits >= 3) p.status = LandStatus.Reserve;
                    else if (waterHits >= 8) p.status = LandStatus.Water;
                    else if (p.slopeDeg > 9) p.status = LandStatus.Steep;
                    else p.status = LandStatus.Available;
                    p.edge = ix == 0 || iz == 0 || ix == Grid - 1 || iz == Grid - 1;
                    ResetSoil(p);
                    parcels[p.index] = p;
                    if (p.status == LandStatus.Available) elevations.Add(p.elevation);
                }
            elevations.Sort();
            floodBase = elevations.Count > 0 ? elevations[Mathf.Clamp(elevations.Count / 10, 0, elevations.Count - 1)] : 0;
            foreach (var p in parcels) p.lowGround = p.elevation < floodBase + 1.2f;
        }

        public void ResetSoil(Parcel p)
        {
            p.om = loc.soil.organicMatter;
            p.nitrogen = loc.soil.nitrogen;
            p.phosphorus = loc.soil.phosphorus;
            p.potassium = loc.soil.potassium;
            p.slowN = 0;
            p.soilWater = Taw(p) * 0.8f;
            p.sat = 0; p.topsoil = 100; p.pest = 0.02f;
        }

        /// <summary>Pick the starting farm: four flat, available parcels as close to the centre as possible.</summary>
        public void ChooseStartingFarm(int count = 4)
        {
            Parcel best = null; float bestScore = float.MaxValue;
            for (int iz = 0; iz < Grid - 1; iz++)
                for (int ix = 0; ix < Grid - 1; ix++)
                {
                    var block = new[] { At(ix, iz), At(ix + 1, iz), At(ix, iz + 1), At(ix + 1, iz + 1) };
                    if (Array.Exists(block, b => b.status != LandStatus.Available)) continue;
                    // close to the grid centre, and with free land around it to grow into
                    float d = Mathf.Abs(ix + 0.5f - Grid / 2f) + Mathf.Abs(iz + 0.5f - Grid / 2f);
                    int free = 0;
                    for (int dz = -2; dz <= 3; dz++) for (int dx = -2; dx <= 3; dx++) { var q = At(ix + dx, iz + dz); if (q != null && q.status == LandStatus.Available) free++; }
                    d -= free * 0.15f;
                    if (d < bestScore) { bestScore = d; best = block[0]; }
                }
            if (best == null) best = Array.Find(parcels, q => q.status == LandStatus.Available);
            var start = new[] { best, At(best.ix + 1, best.iz), At(best.ix, best.iz + 1), At(best.ix + 1, best.iz + 1) };
            foreach (var p in start)
                if (p != null && p.status == LandStatus.Available)
                {
                    p.status = LandStatus.Farm;
                    p.plan.main = loc.existingMain;
                    p.plan.cover = string.IsNullOrEmpty(loc.existingCover) ? null : loc.existingCover;
                    p.plan.irrigation = loc.irrigationAvailable ? IrrigationType.Sprinkler : IrrigationType.None;
                    p.installed = p.plan.irrigation;
                    p.plan.fert = loc.existingFert;
                    p.lastFamily = cat.Crop(loc.existingMain)?.family;
                }
        }

        public float Taw(Parcel p) => loc.soil.tawMm * (0.85f + 0.035f * p.om) * (0.7f + 0.3f * p.topsoil / 100f);

        // ------------------------------------------------------------------ land
        public bool CanLease(Parcel p, out string why)
        {
            why = null;
            switch (p.status)
            {
                case LandStatus.Farm: why = "This field is already yours."; return false;
                case LandStatus.Reserve: why = "Reserve land of the " + loc.nation + ". Not for sale or lease."; return false;
                case LandStatus.Water: why = "Open water. It cannot be farmed."; return false;
                case LandStatus.Steep: why = $"Too steep to farm ({p.slopeDeg:0}° slope)."; return false;
            }
            bool touches = false;
            foreach (var n in Neighbours4(p)) if (n.IsFarm) touches = true;
            if (!touches) { why = "You can only lease land next to your farm."; return false; }
            if (money - loc.leasePerHa < -loc.creditLimit) { why = "Not enough money or credit left."; return false; }
            return true;
        }

        public void Lease(Parcel p)
        {
            p.status = LandStatus.Farm;
            p.leased = true;
            p.plan.main = loc.existingMain;
            p.plan.fert = loc.existingFert;
            p.lastFamily = cat.Crop(loc.existingMain)?.family;
            Spend(loc.leasePerHa, ref leaseThisYear);
        }

        public IEnumerable<Parcel> Neighbours4(Parcel p)
        {
            var a = At(p.ix - 1, p.iz); if (a != null) yield return a;
            a = At(p.ix + 1, p.iz); if (a != null) yield return a;
            a = At(p.ix, p.iz - 1); if (a != null) yield return a;
            a = At(p.ix, p.iz + 1); if (a != null) yield return a;
        }

        public bool InstallIrrigation(Parcel p, IrrigationType t)
        {
            if (!loc.irrigationAvailable && t != IrrigationType.None) return false;
            if (t > p.installed)
            {
                float cost = IrrigationInstallCost[(int)t];
                p.year.costs += cost;
                float dummy = 0; Spend(cost, ref dummy);
                p.installed = t;
            }
            p.plan.irrigation = t;
            return true;
        }

        void Spend(float amount, ref float bucket) { if (trackMoney) money -= amount; bucket += amount; }

        // ------------------------------------------------------------------ the day
        public DayWeather Today => weather.Day(dayIndex);

        public void StepDay()
        {
            var w = weather.Day(dayIndex);
            if (w.doy == 1) NewYear();
            UpdateWater(w);
            foreach (var p in parcels)
            {
                if (!p.IsFarm) continue;
                Manage(p, w);
                Physics(p, w);
            }
            if (trackMoney)
            {
                money -= loc.livingCostPerDay; livingCostsThisYear += loc.livingCostPerDay;
                if (money < 0) { float i = -money * InterestRate / 365f; money -= i; interestThisYear += i; }
            }
            dayIndex++;
        }

        void NewYear()
        {
            allocationUsedMm = 0;
            livingCostsThisYear = leaseThisYear = interestThisYear = 0;
            foreach (var p in parcels)
            {
                if (!p.IsFarm) continue;
                p.year = new YearStats { omStart = p.om };
                if (p.current != null)
                {
                    p.current.harvestedThisYear = false;
                    p.current.fedThisYear = false;
                    p.current.years++;
                    if (p.current.def.perennial)
                    {
                        p.current.gdd = 0;
                        // pruning, mowing and upkeep of perennial crops
                        float up = p.current.def.annualCostPerHa;
                        if (up > 0) { p.year.costs += up; float d0 = 0; Spend(up, ref d0); }
                    }
                }
                if (p.leased) { float d = 0; Spend(loc.leasePerHa, ref d); leaseThisYear += loc.leasePerHa; }
            }
        }

        void UpdateWater(DayWeather w)
        {
            // Valley flooding: only where the land is a drained lake bed / floodplain.
            if (loc.floodProne)
            {
                float p3 = weather.Sum(weather.precip, dayIndex - 2, dayIndex);
                float wet = weather.Sum(weather.precip, dayIndex - 20, dayIndex);
                float target = p3 > 60 && wet > 120 ? (p3 - 55) * 0.04f : 0;
                int wetland = 0; foreach (var p in parcels) if (p.IsFarm && p.current != null && p.current.def.storesFloodwater) wetland++;
                target *= 1 - Mathf.Min(0.25f, wetland * 0.02f);   // wetland fields hold water back
                float level = floodLevel > 0 ? floodLevel - floodBase : 0;
                level = target > level ? target : Mathf.Max(0, level - (w.precip < 5 ? 0.12f : 0.03f));
                bool wasDry = floodLevel <= 0;
                floodLevel = level > 0.01f ? floodBase + level : 0;
                if (wasDry && floodLevel > 0) events.Add(new SimEvent { kind = "flood", value = p3, text = $"Flood: {p3:0} mm of rain in three days. Low fields are under water." });
            }
            // River restrictions: dry summers cut irrigation in half
            if (loc.riverRestrictions)
            {
                float p30 = weather.Sum(weather.precip, dayIndex - 29, dayIndex);
                bool restrict = w.doy >= 182 && w.doy <= 273 && p30 < 12;
                if (restrict && restriction >= 1) events.Add(new SimEvent { kind = "restriction", value = p30, text = $"River low after {p30:0} mm of rain in 30 days. Irrigation cut by half." });
                restriction = restrict ? 0.5f : 1f;
            }
        }

        // ---- the crew follows each field's plan
        void Manage(Parcel p, DayWeather w)
        {
            var main = cat.Crop(p.plan.main);
            var cover = cat.Crop(p.plan.cover);
            var cur = p.current;
            bool workable = p.sat < 5 && p.floodDepth <= 0;

            // turn a cover crop in when the main crop's window opens
            if (cur != null && cur.def.IsCover && main != null && !main.overwinters && Agro.InWindow(w.doy, main.plantFrom, main.plantTo) && workable)
            {
                Terminate(p);
                cur = null;
            }
            // switching land use: plan changed away from a perennial
            if (cur != null && cur.def.perennial && main != cur.def && main != null && Agro.InWindow(w.doy, main.plantFrom, main.plantTo) && workable)
            {
                Terminate(p);
                cur = null;
            }

            // perennial crops (hay, berries, vines) are fed every spring as growth starts
            if (cur != null && !cur.dead && cur.def.perennial && !cur.fedThisYear && cur.days > 30 && w.doy >= 85 && w.doy <= 160 && w.tmean > cur.def.baseTempC)
            {
                cur.fedThisYear = true;
                float fc = ApplyFertiliser(p, cur.def);
                if (fc > 0) { p.year.costs += fc; float d1 = 0; Spend(fc, ref d1); }
            }

            if (cur == null)
            {
                CropDef want = null;
                if (main != null && Agro.InWindow(w.doy, main.plantFrom, main.plantTo) && workable && !(p.year.harvests > 0 && !main.overwinters && !main.perennial))
                    want = main;
                else if (cover != null && (main == null || !main.overwinters) && Agro.InWindow(w.doy, cover.plantFrom, cover.plantTo) && workable && (p.year.harvests > 0 || main == null || w.doy > main.plantTo))
                    want = cover;
                if (want != null)
                {
                    if (p.plan.crew || autoAll) { Plant(p, want); Fee(p, CrewPlantPerHa); }
                    else if (p.job != "plant" || p.jobCrop != want)
                    {
                        p.job = "plant"; p.jobCrop = want; p.jobWaitDays = 0;
                        events.Add(new SimEvent { kind = "job", parcel = p, text = $"Ready to plant {want.name.ToLower()}. Take the tractor out." });
                    }
                }
                else if (p.job == "plant")
                {
                    if (!Agro.InWindow(w.doy, p.jobCrop.plantFrom, p.jobCrop.plantTo))
                        events.Add(new SimEvent { kind = "missed", parcel = p, text = $"Missed the planting window for {p.jobCrop.name.ToLower()}." });
                    if (!workable) return;                     // keep the job while the field is too wet to work
                    p.job = null; p.jobCrop = null;
                }
            }
            else if (p.job == "plant") { p.job = null; p.jobCrop = null; }
        }

        void Fee(Parcel p, float amount)
        {
            if (!trackMoney) return;
            p.year.costs += amount;
            money -= amount;
        }

        /// <summary>The player finished planting with the tractor: no crew fee, just fuel.</summary>
        public void PlayerPlant(Parcel p)
        {
            if (p.job != "plant" || p.jobCrop == null) return;
            if (p.current != null) Terminate(p);
            Plant(p, p.jobCrop);
            Fee(p, FuelPerHa);
            p.job = null; p.jobCrop = null;
        }

        /// <summary>The player finished harvesting with the tractor.</summary>
        public void PlayerHarvest(Parcel p)
        {
            if (p.job != "harvest" || p.current == null) return;
            Fee(p, FuelPerHa);
            float f = p.jobFraction;
            p.job = null;
            Harvest(p, f, f < 1);
        }

        public const float PTarget = 80, KTarget = 220;           // kg/ha P2O5 and K2O a crop needs in the soil for full yield
        public const float PricePerKgP = 1.9f, PricePerKgK = 1.1f;

        public static float ExpectedYield(CropDef d) => d.yieldPerHa * (d.regrowGdd > 0 ? 3 : 1);

        /// <summary>What a fertiliser choice would put on this field for this crop: N, P2O5, K2O (kg/ha), cost, and emissions.</summary>
        public (float n, float p, float k, float cost, float co2e, float tonnes) FertiliserFor(Parcel p, CropDef def, string fertId)
        {
            var f = cat.Fertilizer(fertId); var ef = cat.EF;
            if (f == null || f.id == "none" || def == null || def.IsCover || (def.IsIndigenous && def.nDemand <= 20)) return (0, 0, 0, 0, 0, 0);
            int cuts = def.regrowGdd > 0 ? 3 : 1;              // hay takes up nitrogen for every cutting
            float needN = def.nDemand > 0 ? Mathf.Max(0, def.nDemand * cuts - p.nitrogen * 0.7f) : 0;
            float y = ExpectedYield(def);
            float needP = Mathf.Max(0, def.pRemoval * y + (PTarget - p.phosphorus) * 0.3f);
            float needK = Mathf.Max(0, def.kRemoval * y + (KTarget - p.potassium) * 0.3f);
            float ef1Syn = loc.wetClimate ? ef.ef1SyntheticWet : ef.ef1Dry, ef1Org = loc.wetClimate ? ef.ef1OrganicWet : ef.ef1Dry, ef4 = loc.wetClimate ? ef.ef4Wet : ef.ef4Dry;
            if (f.mix)
            {
                float cost = needN * FertiliserPerKgN + needP * PricePerKgP + needK * PricePerKgK;
                float make = needN * (ef.makeN + ef.ureaCO2PerKgN) + needP * ef.makeP + needK * ef.makeK;
                float n2o = needN * (ef1Syn + ef.fracGasSynthetic * ef4) * EmissionFactors.N2OPerN * ef.gwpN2O;
                return (needN, needP, needK, cost, make + n2o, 0);
            }
            float t = f.n > 0 && f.nAvail > 0 ? needN / (f.n * f.nAvail) : 0;
            if (f.id == "compost") t = Mathf.Max(t, needP / Mathf.Max(0.1f, f.p));
            t = Mathf.Clamp(Mathf.Max(t, 10), 0, f.id == "compost" ? 30 : 60);
            float on = t * f.n;
            float co2 = on * (ef1Org + ef.fracGasOrganic * ef4) * EmissionFactors.N2OPerN * ef.gwpN2O + t * 0.5f * ef.dieselKgCO2PerL;   // + hauling and spreading
            return (on, t * f.p, t * f.k, t * f.pricePerT, co2, t);
        }

        float ApplyFertiliser(Parcel p, CropDef def)
        {
            var f = cat.Fertilizer(p.plan.fert); var ef = cat.EF;
            var a = FertiliserFor(p, def, p.plan.fert);
            if (a.n + a.p + a.k <= 0) return 0;
            if (f.mix) p.nitrogen += a.n;
            else
            {
                p.nitrogen += a.n * f.nAvail;
                p.slowN += a.n * (1 - f.nAvail);         // the rest is released over the season
                p.om += a.tonnes * f.omPerT;
            }
            p.phosphorus += a.p; p.potassium += a.k;
            p.year.nApplied += a.n; p.year.pApplied += a.p; p.year.kApplied += a.k; p.year.fertCost += a.cost;
            // split the emissions into nitrous oxide and the rest (manufacturing, urea CO2, hauling)
            float ef1 = f.mix ? (loc.wetClimate ? ef.ef1SyntheticWet : ef.ef1Dry) : (loc.wetClimate ? ef.ef1OrganicWet : ef.ef1Dry);
            float gas = (f.mix ? ef.fracGasSynthetic : ef.fracGasOrganic) * (loc.wetClimate ? ef.ef4Wet : ef.ef4Dry);
            float n2o = a.n * (ef1 + gas) * EmissionFactors.N2OPerN * ef.gwpN2O;
            p.year.ghgN2O += n2o;
            p.year.ghgFert += Mathf.Max(0, a.co2e - n2o);
            return a.cost;
        }

        void Diesel(Parcel p, float litres)
        {
            p.year.dieselL += litres;
            p.year.ghgFuel += litres * cat.EF.dieselKgCO2PerL;
        }

        public void Plant(Parcel p, CropDef def)
        {
            var pl = new Planting { def = def, fedThisYear = true };
            Diesel(p, def.dieselPlant);
            float cost = def.costPerHa + ApplyFertiliser(p, def);
            p.year.costs += cost;
            float d = 0; Spend(cost, ref d);
            // planting the same crop family again lets its pests and diseases carry over
            p.pest = (def.IsCash && def.family == p.lastFamily) ? 0.07f : 0.015f;
            if (def.IsCash) p.lastFamily = def.family;
            p.current = pl;
        }

        void Terminate(Parcel p)
        {
            var c = p.current;
            if (c == null) return;
            // a turned-in cover crop or meadow hands its nitrogen and organic matter to the soil
            p.nitrogen += c.nStored * 0.6f + c.nFixed * 0.7f;
            p.om += c.def.omPerYear * 0.15f;
            p.current = null;
        }

        void Harvest(Parcel p, float fraction, bool forced)
        {
            var c = p.current; var def = c.def;
            float nFactor = Mathf.Clamp(1 - c.nShort / Mathf.Max(1, def.nDemand) * 0.6f, 0.45f, 1);
            float pFactor = Mathf.Clamp(0.55f + 0.45f * p.phosphorus / (0.6f * PTarget), 0.55f, 1);
            float kFactor = Mathf.Clamp(0.55f + 0.45f * p.potassium / (0.6f * KTarget), 0.55f, 1);
            float nutrient = Mathf.Min(nFactor, Mathf.Min(pFactor, kFactor));     // the scarcest nutrient sets the limit
            string limit = nutrient >= 0.95f ? "" : nutrient == nFactor ? " (short of nitrogen)" : nutrient == pFactor ? " (short of phosphorus)" : " (short of potassium)";
            float omFactor = Mathf.Clamp(0.88f + 0.035f * p.om, 0.88f, 1.08f);
            float soilFactor = 0.6f + 0.4f * p.topsoil / 100f;
            float establishing = def.perennial && def.yearsToFullYield > 1 ? Mathf.Clamp01((c.years + 1f) / def.yearsToFullYield) : 1;
            float yield = def.yieldPerHa * fraction * Mathf.Clamp01(c.health / 100f) * nutrient * omFactor * soilFactor * establishing;
            p.phosphorus = Mathf.Max(5, p.phosphorus - def.pRemoval * yield);
            p.potassium = Mathf.Max(10, p.potassium - def.kRemoval * yield);
            Diesel(p, def.dieselHarvest * Mathf.Max(0.3f, fraction));
            float gross = yield * def.pricePerUnit;
            float cost = def.harvestCostPerHa * fraction;
            p.year.revenue += gross; p.year.costs += cost; p.year.harvests++;
            if (trackMoney) money += gross - cost;
            events.Add(new SimEvent { kind = "harvest", parcel = p, value = gross - cost,
                text = $"{def.name} harvested{(forced ? " early" : "")}: {yield:0.0} {def.unit}/ha, ${gross - cost:0} after harvest costs{limit}." });
            c.harvests++;
            c.harvestedThisYear = true;
            if (def.perennial)
            {
                c.gdd = 0;
                c.nShort = 0;
                p.pest *= 0.4f;          // cutting removes most of the insects' habitat
            }
            else
            {
                p.om += def.omPerYear * 0.3f;   // stubble and roots left behind (negative for crops that remove everything)
                p.current = null;
            }
        }

        // ---- soil water, nitrogen, damage
        void Physics(Parcel p, DayWeather w)
        {
            var c = p.current;
            var def = c?.def;
            bool alive = c != null && !c.dead;
            float taw = Taw(p);

            // growth (growing degree days)
            float gddToday = 0;
            if (alive)
            {
                gddToday = Mathf.Max(0, Mathf.Min(w.tmean, 30) - def.baseTempC);
                c.gdd += gddToday; c.days++;
                if (def.nFix > 0) { float f = def.nFix * gddToday / Mathf.Max(1, def.gddToMaturity); c.nFixed += f; p.year.nFixed += f; }
            }
            bool dormant = alive && w.tmean < def.baseTempC;
            float canopy = alive ? c.Canopy : 0;
            float cover = alive ? def.erosionCover * Mathf.Max(canopy, def.perennial ? 0.6f : 0) : (c != null && c.dead ? 0.6f : 0);

            // water out
            float kc = alive ? def.kcMid * (0.35f + 0.65f * canopy) : (c != null && c.dead ? 0.24f : 0.3f);
            if (dormant) kc = Mathf.Min(kc, 0.4f);
            if (alive && ShelteredByNeighbour(p)) kc *= 0.9f;
            float etc = w.et0 * kc;
            float ks = alive ? (p.soilWater >= 0.5f * taw ? 1 : p.soilWater / (0.5f * taw)) : (p.soilWater >= 0.3f * taw ? 1 : p.soilWater / (0.3f * taw));
            float aet = etc * ks;

            // water in
            float P = w.precip;
            float runoff = P > 20 ? (P - 20) * 0.4f : 0;
            runoff *= (1 - 0.5f * cover) * (1 + p.slopeDeg / 10f);
            if (p.sat > 0) runoff += Mathf.Min(P - runoff, P * 0.3f);
            runoff = Mathf.Clamp(runoff, 0, P);
            float fromSat = Mathf.Min(p.sat, aet);
            p.sat -= fromSat;
            p.soilWater += P - runoff - (aet - fromSat);
            if (p.soilWater > taw) { p.sat += p.soilWater - taw; p.soilWater = taw; }
            p.soilWater = Mathf.Max(0, p.soilWater);
            float drained = Mathf.Min(p.sat, loc.soil.drainMmPerDay + (cover > 0.5f ? 3 : 0));
            p.sat -= drained;
            p.dryDays = P > 2 ? 0 : p.dryDays + 1;

            // flood water from the valley
            p.floodDepth = floodLevel > 0 ? Mathf.Max(0, floodLevel - p.elevation) : 0;
            if (p.floodDepth > 0.02f) { p.sat = Mathf.Max(p.sat, 60); p.year.floodDays++; }

            // irrigation: refill to 85% when a crop drops below 55%
            if (alive && !def.noIrrigation && !def.IsCover && p.installed != IrrigationType.None && p.plan.irrigation != IrrigationType.None
                && p.sat <= 0 && p.soilWater < 0.55f * taw && !dormant)
            {
                int t = (int)Mathf.Min((int)p.installed, (int)p.plan.irrigation);
                float eff = IrrigationEfficiency[t];
                float need = 0.85f * taw - p.soilWater;
                float gross = need / eff;
                float allowance = Mathf.Max(0, loc.irrigationAllocationMm * restriction - allocationUsedMm / Mathf.Max(1, FarmCount));
                gross = Mathf.Min(gross, allowance, 25);
                if (gross > 0.5f)
                {
                    p.soilWater += gross * eff;
                    allocationUsedMm += gross;
                    p.year.irrigationMm += gross;
                    p.year.ghgIrr += gross * cat.EF.pumpKWhPerMmHa * cat.EF.gridKgCO2PerKWh;
                    float cost = gross * PumpingPerMm;
                    p.year.costs += cost;
                    float d = 0; Spend(cost, ref d);
                }
            }

            // nitrogen: mineralisation in, leaching out with drainage, uptake by crops
            p.nitrogen += p.om * 0.022f * Mathf.Clamp01(w.tmean / 20f);
            float uptakeShield = alive ? (def.scavengesN ? 0.25f : Mathf.Lerp(1, 0.45f, canopy)) : 1;
            if (p.slowN > 0) { float rel = p.slowN * 0.006f * Mathf.Clamp01(w.tmean / 20f); p.slowN -= rel; p.nitrogen += rel; }
            float leach = drained * p.nitrogen * 0.0045f * uptakeShield;
            p.nitrogen -= leach; p.year.nLeached += leach;
            var ef = cat.EF;
            p.year.ghgN2O += leach * ef.ef5Leach * EmissionFactors.N2OPerN * ef.gwpN2O;      // indirect N2O from leached nitrate
            bool wetland = alive && def.storesFloodwater;
            if ((p.sat > 20 || p.floodDepth > 0.02f || wetland) && w.tmean > 6) p.year.ghgCH4 += ef.ch4KgPerWetDay * ef.gwpCH4;   // waterlogged soil makes methane
            if (alive && def.scavengesN && !dormant) { float s = Mathf.Min(p.nitrogen * 0.01f, 0.4f); p.nitrogen -= s; c.nStored += s; }
            if (alive && def.nDemand > 0)
            {
                float need = def.nDemand * gddToday / Mathf.Max(1, def.gddToMaturity);
                float got = Mathf.Min(need, p.nitrogen);
                p.nitrogen -= got; c.nShort += need - got;
            }
            if (alive && def.perennial && def.nFix > 0) p.nitrogen += c.nFixed * 0.002f;   // legume hay feeds itself
            p.nitrogen = Mathf.Clamp(p.nitrogen, 0, 400);

            // organic matter
            float omRate = alive ? def.omPerYear : (c != null && c.dead ? 0.02f : -0.12f);
            p.om = Mathf.Clamp(p.om + omRate / 365f * (w.tmean > 5 ? 1.2f : 0.4f), 0.5f, 12);

            // erosion: heavy rain and wind on bare soil
            float bare = 1 - cover;
            float eroded = runoff * 0.05f * bare * (1 + p.slopeDeg / 8f);
            if (w.wind > 6 && p.dryDays > 3 && p.sat <= 0) eroded += (w.wind - 6) * 0.25f * bare;
            if (eroded > 0)
            {
                float before = p.topsoil; p.topsoil = Mathf.Max(50, p.topsoil - eroded * 0.08f); p.year.erosion += before - p.topsoil;
                // phosphorus sticks to soil particles, so eroded soil carries it into ditches and streams
                float pl = (before - p.topsoil) * p.phosphorus * 0.02f;
                p.phosphorus -= pl; p.year.pLost += pl;
            }

            // pests
            float warm = Mathf.Clamp01((w.tmean - 12) / 12f) * (w.tmax > 36 ? 0.6f : 1);
            float humid = Mathf.Clamp01((w.rh - 40) / 40f);
            if (alive && def.IsCash && !dormant)
            {
                float r = 0.045f * warm * (0.35f + 0.65f * humid) * (def.perennial ? 0.35f : 1f);
                if (PestBufferNearby(p)) r *= 0.55f;
                p.pest = Mathf.Clamp01(p.pest + r * p.pest * (1 - p.pest) + 0.001f * warm);
            }
            else p.pest *= 0.9f;
            if (w.tmean < 8) p.pest *= 0.92f;

            if (!alive) { p.dryDays = P > 2 ? 0 : p.dryDays; return; }

            // winter-kill of non-hardy cover crops: the dead plants stay as mulch
            if (def.IsCover && w.tmin < def.frostC) { if (!def.overwinters) { c.dead = true; return; } }

            // damage
            float dDrought = 0, dHeat = 0, dFrost = 0, dFlood = 0, dPest = 0;
            if (!dormant && canopy > 0.1f && ks < 1) dDrought = (1 - ks) * 2.2f * def.droughtSens;
            float cooling = ks >= 0.95f ? 3 + Mathf.Clamp((50 - w.rh) / 10f, 0, 3) : ks < 0.6f ? -2 : 0;
            float canopyT = w.tmax - cooling;
            if (canopyT > def.heatC) dHeat = Mathf.Min(4, (canopyT - def.heatC) * 0.6f);
            float frostLimit = def.frostC - (dormant && (def.overwinters || def.perennial) ? 12 : 0);   // snow cover and dormancy
            if (w.tmin < frostLimit) dFrost = Mathf.Min(45, (frostLimit - w.tmin) * 10 + 5);
            if (p.sat > 5 || p.floodDepth > 0.02f)
            {
                c.waterlog = Mathf.Min(c.waterlog + 1, 30);
                if (c.waterlog > def.floodTolDays) dFlood = Mathf.Min(8, 1.5f * (c.waterlog - def.floodTolDays)) * (p.floodDepth > 0.02f ? 1.5f : 1) * (dormant ? 0.4f : 1);
            }
            else c.waterlog = Mathf.Max(0, c.waterlog - 2);
            if (def.IsCash && p.pest > 0.45f) dPest = (p.pest - 0.45f) * 3;

            float total = dDrought + dHeat + dFrost + dFlood + dPest;
            if (total > 0 && !def.IsCover)
            {
                float pot = def.GrossPerHa / 100f;
                p.year.lossDrought += dDrought * pot; p.year.lossHeat += dHeat * pot; p.year.lossFrost += dFrost * pot; p.year.lossFlood += dFlood * pot; p.year.lossPest += dPest * pot;
                Note(c, "drought", dDrought); Note(c, "heat", dHeat); Note(c, "frost", dFrost); Note(c, "flood", dFlood); Note(c, "pests", dPest);
            }
            c.health -= def.IsCover ? total * 0.5f : total;
            if (c.health <= 0)
            {
                string cause = "weather"; float worst = 0;
                foreach (var kv in c.loss) if (kv.Value > worst) { worst = kv.Value; cause = kv.Key; }
                if (!def.IsCover) { p.year.cropsLost++; events.Add(new SimEvent { kind = "lost", parcel = p, text = $"{def.name} died. Main cause: {cause}." }); }
                if (def.IsCover) c.dead = true; else p.current = null;
                return;
            }

            // harvest
            if (def.IsCover || c.harvestedThisYear && !def.perennial) return;
            float target = def.perennial && c.harvests > 0 && def.regrowGdd > 0 ? def.regrowGdd : def.gddToMaturity;
            bool late = def.lastHarvestDoy > 0 && w.doy >= def.lastHarvestDoy && w.doy < 340 && !(def.overwinters && c.days < 200);
            if (p.job == "harvest")
            {
                // a ripe crop waiting in the field loses a little every day
                p.jobWaitDays++;
                c.health -= 0.6f;
                if (p.jobWaitDays > 30) { p.job = null; p.year.cropsLost++; events.Add(new SimEvent { kind = "lost", parcel = p, text = $"{def.name} rotted in the field. It waited too long for harvest." }); if (!def.perennial) p.current = null; else c.gdd = 0; }
                return;
            }
            float frac = 1; bool ready = false, forced = false;
            if (c.gdd >= target)
            {
                if (def.perennial && (def.regrowGdd <= 0 && c.harvestedThisYear)) return;   // one harvest a year
                if (def.perennial && w.doy > def.lastHarvestDoy) return;
                ready = true;
            }
            else if (late && !c.harvestedThisYear && !def.perennial)
            {
                frac = Mathf.Clamp01(c.gdd / target);
                if (frac > 0.5f) { ready = true; forced = true; frac *= frac; }
                else { p.year.cropsLost++; events.Add(new SimEvent { kind = "lost", parcel = p, text = $"{def.name} never matured: the season was too short." }); p.current = null; }
            }
            if (!ready) return;
            if (p.plan.crew || autoAll) { Fee(p, CrewHarvestPerHa * frac); Harvest(p, frac, forced); }
            else
            {
                p.job = "harvest"; p.jobFraction = frac; p.jobWaitDays = 0;
                events.Add(new SimEvent { kind = "job", parcel = p, text = $"{def.name} is ready to harvest. Take the tractor out." });
            }
        }

        static void Note(Planting c, string k, float v) { if (v > 0) c.loss[k] = (c.loss.TryGetValue(k, out var o) ? o : 0) + v; }

        bool ShelteredByNeighbour(Parcel p)
        {
            foreach (var n in Neighbours4(p)) if (n.IsFarm && n.current != null && !n.current.dead && n.current.def.sheltersNeighbours) return true;
            return false;
        }

        bool PestBufferNearby(Parcel p)
        {
            foreach (var n in Neighbours4(p))
                if (n.IsFarm && n.current != null && !n.current.dead && (n.current.def.reducesNeighbourPests || n.current.def.sheltersNeighbours)) return true;
            return false;
        }

        // ------------------------------------------------------------------ helpers for the UI
        public float SoilMoistureFraction(Parcel p) => Mathf.Clamp01((p.soilWater + Mathf.Min(p.sat, 30)) / Taw(p));
        public float AllocationLeftMm => Mathf.Max(0, loc.irrigationAllocationMm * restriction - allocationUsedMm / Mathf.Max(1, FarmCount));
    }
}
