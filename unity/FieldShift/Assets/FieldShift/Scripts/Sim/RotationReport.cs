using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FieldShift
{
    public class StrategyResult
    {
        public string key, name, plan, why;
        public bool locked;
        public string lockedReason;
        public YearStats total = new YearStats();
        public int fields;
        public float omChange, avgIrrigationMm, avgLeached;
        public float avgGhgT, avgN2OT, avgFertT, avgFuelT, avgCH4T, avgSoilT, avgIrrT;   // t CO2e per ha
        public float avgN, avgP, avgK, avgPLost;                                          // kg per ha
        public List<string> benefits = new List<string>(), risks = new List<string>();
        public Dictionary<string, int> landUse = new Dictionary<string, int>();
    }

    public class WeatherYearSummary
    {
        public int year;
        public float rain, et0, maxT, maxRain3, minT, summerRain;
        public string maxTDate, maxRain3Date, minTDate;
        public int hotDays, frostFreeDays, longestDrySpell;
        public float winterRain;
    }

    /// <summary>
    /// The challenge's core output: compare the existing rotation with at least two alternatives on the same
    /// fields and the same year of NASA weather, and say why, what evidence, what benefits, what gaps and risks.
    /// </summary>
    public static class RotationReport
    {
        public static WeatherYearSummary Summarise(WeatherSeries w, int year)
        {
            var s = new WeatherYearSummary { year = year, maxT = -99, minT = 99 };
            int i0 = w.IndexOf(new DateTime(year, 1, 1)), i1 = w.IndexOf(new DateTime(year, 12, 31));
            int dry = 0, lastFrostSpring = 0, firstFrostFall = 366;
            for (int i = Mathf.Max(0, i0); i <= Mathf.Min(w.days - 1, i1); i++)
            {
                var d = w.Day(i);
                s.rain += d.precip; s.et0 += d.et0;
                if (d.tmax > s.maxT) { s.maxT = d.tmax; s.maxTDate = d.date.ToString("MMM d"); }
                if (d.tmin < s.minT) { s.minT = d.tmin; s.minTDate = d.date.ToString("MMM d"); }
                if (d.tmax >= 30) s.hotDays++;
                if (d.doy >= 152 && d.doy <= 243) s.summerRain += d.precip;
                if (d.doy <= 90 || d.doy >= 305) s.winterRain += d.precip;
                float r3 = w.Sum(w.precip, i - 2, i);
                if (r3 > s.maxRain3) { s.maxRain3 = r3; s.maxRain3Date = d.date.ToString("MMM d"); }
                dry = d.precip < 1 ? dry + 1 : 0;
                if (d.doy > 90 && d.doy < 300) s.longestDrySpell = Mathf.Max(s.longestDrySpell, dry);
                if (d.tmin < 0 && d.doy < 182) lastFrostSpring = d.doy;
                if (d.tmin < 0 && d.doy >= 182 && d.doy < firstFrostFall) firstFrostFall = d.doy;
            }
            s.frostFreeDays = Mathf.Max(0, firstFrostFall - lastFrostSpring);
            return s;
        }

        /// <summary>Best alternative cash crop: different family from the usual crop, highest margin per hectare.</summary>
        static CropDef BreakCrop(Catalog cat, LocationDef loc)
        {
            var usual = cat.Crop(loc.existingMain);
            CropDef best = null; float bestScore = float.MinValue;
            foreach (var id in loc.crops)
            {
                var c = cat.Crop(id);
                if (c == null || c == usual || (usual != null && c.family == usual.family) || c.overwinters || c.perennial) continue;   // a break crop is a one-season crop
                float score = c.GrossPerHa - c.costPerHa - c.harvestCostPerHa;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best ?? usual;
        }

        /// <summary>Which of the farm's fields the Indigenous land use goes on in the comparison.</summary>
        static HashSet<int> TechniqueFields(CropDef tech, FarmSim sim)
        {
            var farm = sim.Farm().ToList();
            var chosen = new HashSet<int>();
            if (tech == null || farm.Count == 0) return chosen;
            if (tech.needsLowGround)
            {
                foreach (var p in farm) if (p.lowGround) chosen.Add(p.index);
                if (chosen.Count == 0) chosen.Add(farm.OrderBy(p => p.elevation).First().index);
            }
            else if (tech.sheltersNeighbours || tech.reducesNeighbourPests)
                chosen.Add(farm.OrderBy(p => p.ix).ThenBy(p => p.iz).First().index);          // the windward (west) edge
            else
                foreach (var p in farm.OrderByDescending(p => p.slopeDeg + (p.elevation - sim.floodBase) * 0.2f).Take(Mathf.Max(1, farm.Count / 4))) chosen.Add(p.index);
            if (chosen.Count > farm.Count / 2 && farm.Count > 1) chosen = new HashSet<int>(chosen.Take(farm.Count / 2));
            return chosen;
        }

        public static List<StrategyResult> Run(FarmSim live, int year, bool techniqueLearned)
        {
            var cat = live.cat; var loc = live.loc; var w = live.weather;
            var usual = cat.Crop(loc.existingMain);
            var brk = BreakCrop(cat, loc);
            string cover1 = loc.covers.Length > 0 ? loc.covers[0] : null;
            string legume = loc.covers.FirstOrDefault(c => cat.Crop(c)?.nFix > 0) ?? cover1;
            var techId = loc.techniques.Length > 0 ? loc.techniques[0] : null;
            var tech = cat.Crop(techId);
            var bestIrr = loc.irrigationAvailable ? IrrigationType.Drip : IrrigationType.None;
            var techFields = TechniqueFields(tech, live);
            var usualIrr = loc.irrigationAvailable ? IrrigationType.Sprinkler : IrrigationType.None;

            var results = new List<StrategyResult>();

            results.Add(Simulate(live, year, "existing", "Common local rotation", loc.existingLabel,
                $"This is how most farms around {loc.name} are run today. It is the baseline the others are measured against.",
                p => { p.plan.main = loc.existingMain; p.plan.cover = string.IsNullOrEmpty(loc.existingCover) ? null : loc.existingCover; p.plan.irrigation = usualIrr; p.plan.fert = loc.existingFert; p.lastFamily = usual?.family; }));

            results.Add(Simulate(live, year, "yours", "Your plan", "What you have planned for each field right now",
                "Your own choices, replayed on the same weather so you can see how they compare.",
                p => { var src = live.parcels[p.index]; p.plan = src.plan.Clone(); p.lastFamily = usual?.family; }));

            results.Add(Simulate(live, year, "cover", "Break crop plus cover crop",
                $"{brk?.name} instead of {usual?.name}, then {cat.Crop(legume)?.name ?? "a cover crop"} over winter{(loc.irrigationAvailable ? ", drip irrigation" : "")}",
                $"Switching crop family breaks the pest cycle of {usual?.name.ToLower()}. A cover crop keeps living roots in the soil after harvest, so less nitrogen washes out and less topsoil moves in winter storms.",
                p => { p.plan.main = brk?.id; p.plan.cover = legume; p.plan.irrigation = bestIrr; p.plan.fert = "synthetic"; p.lastFamily = usual?.family; }));

            var ind = Simulate(live, year, "indigenous", "With Indigenous knowledge",
                tech != null ? $"{tech.name} on the fields that suit it, break crop and cover crop on the rest" : "Not available here",
                tech != null ? $"Uses what the {loc.nation} shared: {tech.description}" : "",
                p =>
                {
                    p.lastFamily = usual?.family;
                    if (techFields.Contains(p.index)) { p.plan.main = tech.id; p.plan.cover = null; p.plan.irrigation = IrrigationType.None; p.plan.fert = "none"; }
                    else { p.plan.main = brk?.id; p.plan.cover = legume; p.plan.irrigation = bestIrr; p.plan.fert = "compost"; }
                });
            if (!techniqueLearned || tech == null)
            {
                ind.locked = true;
                ind.lockedReason = tech == null ? "No technique for this location yet." : $"Visit the {loc.nation} community office twice to learn this. Knowledge is shared, not taken.";
            }
            results.Add(ind);

            Explain(results, Summarise(w, year), loc, cat);
            return results;
        }

        static StrategyResult Simulate(FarmSim live, int year, string key, string name, string plan, string why, Action<Parcel> setPlan)
        {
            var r = new StrategyResult { key = key, name = name, plan = plan, why = why };
            var sim = new FarmSim(live.cat, live.loc, live.weather) { trackMoney = false, floodBase = live.floodBase, autoAll = true };
            sim.parcels = live.parcels.Select(p => p.CloneForSim()).ToArray();
            foreach (var p in sim.parcels)
            {
                if (!p.IsFarm) { p.current = null; continue; }
                sim.ResetSoil(p);
                p.current = null;
                p.job = null;
                p.installed = IrrigationType.Drip;     // compare practices, not past investments
                setPlan(p);
            }
            // warm up from the previous September so winter cover crops are in the ground on January 1
            var start = new DateTime(year - 1, 9, 1);
            if (!live.weather.Has(start)) start = new DateTime(year, 1, 1);
            sim.dayIndex = live.weather.IndexOf(start);
            int end = live.weather.IndexOf(new DateTime(year, 12, 31));
            if (end >= live.weather.days) end = live.weather.days - 1;
            while (sim.dayIndex <= end) sim.StepDay();

            foreach (var p in sim.parcels)
            {
                if (!p.IsFarm) continue;
                p.year.omEnd = p.om;
                if (p.year.omStart == 0) p.year.omStart = live.loc.soil.organicMatter;
                // installation cost spread over 10 years
                p.year.costs += FarmSim.IrrigationInstallCost[(int)p.plan.irrigation] / 10f;
                r.total.Add(p.year);
                r.fields++;
                string lu = live.cat.Crop(p.plan.main)?.name ?? "Fallow";
                r.landUse[lu] = (r.landUse.TryGetValue(lu, out var n) ? n : 0) + 1;
            }
            if (r.fields > 0)
            {
                var ef = live.cat.EF;
                float n = r.fields;
                r.omChange = (r.total.omEnd - r.total.omStart) / n;
                r.avgIrrigationMm = r.total.irrigationMm / n;
                r.avgLeached = r.total.nLeached / n;
                r.avgN2OT = r.total.ghgN2O / n / 1000; r.avgFertT = r.total.ghgFert / n / 1000; r.avgFuelT = r.total.ghgFuel / n / 1000;
                r.avgCH4T = r.total.ghgCH4 / n / 1000; r.avgIrrT = r.total.ghgIrr / n / 1000;
                r.avgSoilT = -r.omChange * ef.soilCO2PerPercentOM;
                r.avgGhgT = r.avgN2OT + r.avgFertT + r.avgFuelT + r.avgCH4T + r.avgIrrT + r.avgSoilT;
                r.avgN = r.total.nApplied / n; r.avgP = r.total.pApplied / n; r.avgK = r.total.kApplied / n; r.avgPLost = r.total.pLost / n;
            }
            return r;
        }

        static void Explain(List<StrategyResult> rs, WeatherYearSummary s, LocationDef loc, Catalog cat)
        {
            var b = rs[0];
            foreach (var r in rs)
            {
                if (r == b) continue;
                float dNet = r.total.Net - b.total.Net;
                if (dNet > 50) r.benefits.Add($"${dNet:N0} more net income than the common rotation on your {r.fields} ha.");
                else if (dNet < -50) r.risks.Add($"${-dNet:N0} less net income than the common rotation this year.");
                if (r.avgLeached < b.avgLeached - 2) r.benefits.Add($"{b.avgLeached - r.avgLeached:0} kg/ha less nitrogen washed toward the groundwater ({s.winterRain:0} mm of winter rain this year, NASA POWER).");
                if (r.omChange > b.omChange + 0.02f) r.benefits.Add($"Soil organic matter {r.omChange:+0.00;-0.00}% vs {b.omChange:+0.00;-0.00}%: more water held next year.");
                if (r.avgIrrigationMm < b.avgIrrigationMm - 10) r.benefits.Add($"{b.avgIrrigationMm - r.avgIrrigationMm:0} mm less irrigation water per hectare in a year with {s.summerRain:0} mm of summer rain.");
                if (r.total.lossFlood < b.total.lossFlood - 100) r.benefits.Add($"${b.total.lossFlood - r.total.lossFlood:N0} less flood damage (wettest 3 days: {s.maxRain3:0} mm around {s.maxRain3Date}).");
                if (r.total.lossPest < b.total.lossPest - 100) r.benefits.Add($"${b.total.lossPest - r.total.lossPest:N0} less pest damage: the crop family changed, so pests from last year had nothing to eat.");
                if (r.total.erosion < b.total.erosion - 0.2f) r.benefits.Add("Less topsoil lost in storms because the ground stayed covered.");
                if (r.avgGhgT < b.avgGhgT - 0.2f) r.benefits.Add($"{b.avgGhgT - r.avgGhgT:0.0} t CO2e per ha less greenhouse gas (less nitrous oxide from fertiliser, more carbon kept in the soil).");
                else if (r.avgGhgT > b.avgGhgT + 0.2f) r.risks.Add($"{r.avgGhgT - b.avgGhgT:0.0} t CO2e per ha more greenhouse gas{(r.avgCH4T > b.avgCH4T + 0.3f ? ", mostly methane from waterlogged ground" : "")}.");
                if (r.avgPLost < b.avgPLost - 0.3f) r.benefits.Add($"{b.avgPLost - r.avgPLost:0.0} kg/ha less phosphorus washed into streams, where it feeds algae.");
                if (r.total.lossHeat + r.total.lossDrought > b.total.lossHeat + b.total.lossDrought + 100) r.risks.Add($"More heat and drought losses: {s.hotDays} days reached 30 °C (hottest {s.maxT:0} °C on {s.maxTDate}).");
            }
            foreach (var r in rs)
            {
                if (r.key == "indigenous") r.risks.Add($"Markets for these foods are small, and the knowledge belongs to the {loc.nation}. Any commercial use needs their agreement.");
                if (r.key == "cover") r.risks.Add("A wet spring can delay turning the cover crop in, which delays planting.");
                if ((r.key == "cover" || r.key == "indigenous") && r.landUse.Keys.Any(k => k.Contains("Squash") || k.Contains("Potato")))
                    r.risks.Add("Vegetables need buyers lined up before planting. If every farm in the valley switched, prices would fall.");
                if (r.key == "existing" && r.avgLeached > 15) r.risks.Add($"{r.avgLeached:0} kg/ha of nitrogen lost to drainage. Over many farms, this is how aquifers get contaminated.");
                if (r.avgP > 0 && loc.soil.phosphorus > FarmSim.PTarget * 1.4f && (r.key == "existing" || r.key == "yours") && loc.existingFert == "manure")
                    r.risks.Add("This soil already tests high in phosphorus. More manure keeps adding P the crops do not need.");
                if (r.benefits.Count == 0 && r != b) r.benefits.Add("No clear advantage this year. Try another year: one season is not a trend.");
            }
        }

        public static readonly string[] Gaps =
        {
            "NASA POWER is a 0.5 x 0.625 degree grid (about 50 km). Your fields can be warmer, cooler or wetter than the grid cell.",
            "Soil wetness in NASA POWER comes from the MERRA-2 model. SMAP satellite soil moisture (9-36 km) would be a better check and is not in this build yet.",
            "Soil properties, prices, costs and yields are teaching estimates. A local agronomist and the BC Soil Information Finder Tool should check them.",
            "The flood model fills low ground in proportion to heavy rain. It does not model dikes, pumps or river levels.",
            "Greenhouse gases use IPCC 2019 nitrous oxide factors and AR6 warming potentials. Fertiliser manufacturing, field methane and the soil-carbon conversion are simplified estimates, and carbon brought in with manure or compost was taken from somewhere else, so it is not all a true removal.",
            "Soil N, P and K start from typical values for each soil, not a real soil test. Test your own fields before deciding on fertiliser.",
        };
    }
}
