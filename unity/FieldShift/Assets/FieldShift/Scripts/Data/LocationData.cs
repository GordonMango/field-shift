using System;
using UnityEngine;

namespace FieldShift
{
    /// <summary>One day of NASA POWER data plus the derived reference evapotranspiration.</summary>
    public struct DayWeather
    {
        public DateTime date;
        public int doy;
        public float tmax, tmin, tmean, precip, solar, rh, wind, gwetTop, gwetRoot, et0;
    }

    [Serializable]
    public class WeatherSeries
    {
        public string source, startDate;
        public float lat, lon, powerCellElevation;
        public int days;
        public float[] tmax, tmin, tmean, precip, solar, rh, wind, gwetTop, gwetRoot;

        [NonSerialized] public DateTime start;
        [NonSerialized] public float[] et0;
        [NonSerialized] public float elevationAdjustC, targetElevation;

        /// <summary>
        /// NASA POWER values describe a ~50 km grid cell whose average height can be hundreds of metres above
        /// the valley floor. Shift temperatures to the farm's NASADEM elevation with a conservative lapse rate.
        /// </summary>
        public void AdjustForElevation(float farmElevation, float lapsePerKm = 4f)
        {
            targetElevation = farmElevation;
            elevationAdjustC = (powerCellElevation - farmElevation) / 1000f * lapsePerKm;
            for (int i = 0; i < days; i++) { tmax[i] += elevationAdjustC; tmin[i] += elevationAdjustC; tmean[i] += elevationAdjustC; }
            Prepare();
        }

        public void Prepare()
        {
            start = new DateTime(int.Parse(startDate.Substring(0, 4)), int.Parse(startDate.Substring(4, 2)), int.Parse(startDate.Substring(6, 2)));
            et0 = new float[days];
            for (int i = 0; i < days; i++)
            {
                var d = start.AddDays(i);
                et0[i] = Agro.ReferenceET(lat, d.DayOfYear, tmax[i], tmin[i], tmean[i], wind[i], rh[i]);
            }
        }

        public DateTime End => start.AddDays(days - 1);
        public int IndexOf(DateTime d) => (int)(d.Date - start).TotalDays;
        public bool Has(DateTime d) { int i = IndexOf(d); return i >= 0 && i < days; }

        public DayWeather Day(int i)
        {
            i = Mathf.Clamp(i, 0, days - 1);
            var d = start.AddDays(i);
            return new DayWeather
            {
                date = d, doy = d.DayOfYear, tmax = tmax[i], tmin = tmin[i], tmean = tmean[i], precip = precip[i],
                solar = solar[i], rh = rh[i], wind = wind[i], gwetTop = gwetTop[i], gwetRoot = gwetRoot[i], et0 = et0[i]
            };
        }

        public float Sum(float[] arr, int fromInclusive, int toInclusive)
        {
            float s = 0;
            for (int i = Mathf.Max(0, fromInclusive); i <= Mathf.Min(days - 1, toInclusive); i++) s += arr[i];
            return s;
        }
    }

    [Serializable]
    public class HeightmapMeta
    {
        public string source, layout;
        public float sizeMeters, south, north, west, east, datumMeters, heightRangeMeters, minElevation, maxElevation;
        public int resolution;
    }

    [Serializable]
    public class ReserveInfo
    {
        public int id, cells, osmId;
        public string name, wikidata;
        public float areaKm2;
    }

    [Serializable]
    public class LandMaskMeta
    {
        public string source, layout;
        public int grid, waterCells;
        public float cellMeters;
        public ReserveInfo[] reserves;
    }

    /// <summary>Everything loaded from Resources for one location.</summary>
    public class LocationData
    {
        public LocationDef def;
        public WeatherSeries weather;
        public HeightmapMeta heightMeta;
        public float[,] heights01;      // [z, x], row 0 = south
        public LandMaskMeta maskMeta;
        public byte[] reserveGrid;      // grid*grid, row 0 = south
        public byte[] waterGrid;
        public Vector2 gridCenter;      // centre of the 2 x 2 km farm grid, local metres

        public static LocationData Load(LocationDef def)
        {
            string root = "FieldShift/Locations/" + def.id + "/";
            var ld = new LocationData { def = def };

            var w = Resources.Load<TextAsset>(root + "weather");
            ld.weather = JsonUtility.FromJson<WeatherSeries>(w.text);
            ld.weather.Prepare();

            ld.heightMeta = JsonUtility.FromJson<HeightmapMeta>(Resources.Load<TextAsset>(root + "heightmap").text);
            var hb = Resources.Load<TextAsset>(root + "heightmap_raw").bytes;
            int n = ld.heightMeta.resolution;
            ld.heights01 = new float[n, n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int o = (z * n + x) * 2;
                    ld.heights01[z, x] = (hb[o] | (hb[o + 1] << 8)) / 65535f;
                }

            ld.maskMeta = JsonUtility.FromJson<LandMaskMeta>(Resources.Load<TextAsset>(root + "reserves").text);
            var mb = Resources.Load<TextAsset>(root + "landmask").bytes;
            int g = ld.maskMeta.grid;
            ld.reserveGrid = new byte[g * g];
            ld.waterGrid = new byte[g * g];
            Array.Copy(mb, 0, ld.reserveGrid, 0, g * g);
            Array.Copy(mb, g * g, ld.waterGrid, 0, g * g);
            ld.gridCenter = ld.ChooseGridCenter(FarmSim.Grid * Parcel.SizeM);
            ld.weather.AdjustForElevation(ld.MedianFarmElevation());
            return ld;
        }

        /// <summary>
        /// Place the 2 km farm grid where there is the most flat, open farmland, preferring spots that border
        /// reserve land so the player meets the land question, and staying near the map centre.
        /// </summary>
        public Vector2 ChooseGridCenter(float gridSize)
        {
            float half = HalfSize, best = float.MinValue;
            Vector2 bestC = Vector2.zero;
            int n = FarmSim.Grid;
            float cell = gridSize / n;
            for (float cz = -half + gridSize / 2 + 400; cz <= half - gridSize / 2 - 400; cz += 250)
                for (float cx = -half + gridSize / 2 + 400; cx <= half - gridSize / 2 - 400; cx += 250)
                {
                    int farmable = 0, reserve = 0;
                    for (int iz = 0; iz < n; iz++)
                        for (int ix = 0; ix < n; ix++)
                        {
                            float x = cx - gridSize / 2 + (ix + 0.5f) * cell, z = cz - gridSize / 2 + (iz + 0.5f) * cell;
                            if (ReserveAt(x, z) > 0) { reserve++; continue; }
                            if (WaterAt(x, z)) continue;
                            float dx = ElevationAt(x + 40, z) - ElevationAt(x - 40, z), dz = ElevationAt(x, z + 40) - ElevationAt(x, z - 40);
                            if (Mathf.Sqrt(dx * dx + dz * dz) / 80f < 0.12f) farmable++;
                        }
                    float total = n * n;
                    float score = farmable / total
                                  + (reserve >= 6 && reserve <= total * 0.35f ? 0.5f : 0)
                                  - new Vector2(cx, cz).magnitude / (half * 6);
                    if (score > best) { best = score; bestC = new Vector2(cx, cz); }
                }
            return bestC;
        }

        public float MedianFarmElevation()
        {
            var list = new System.Collections.Generic.List<float>();
            for (int iz = -9; iz <= 9; iz += 2)
                for (int ix = -9; ix <= 9; ix += 2)
                {
                    float x = gridCenter.x + ix * 100, z = gridCenter.y + iz * 100;
                    if (ReserveAt(x, z) == 0 && !WaterAt(x, z)) list.Add(ElevationAt(x, z));
                }
            if (list.Count == 0) return ElevationAt(gridCenter.x, gridCenter.y);
            list.Sort();
            return list[list.Count / 2];
        }

        public float HalfSize => heightMeta.sizeMeters * 0.5f;

        /// <summary>Real elevation in metres at local map coordinates (x east, z north, origin = map centre).</summary>
        public float ElevationAt(float x, float z)
        {
            int n = heightMeta.resolution;
            float fx = Mathf.Clamp01((x + HalfSize) / heightMeta.sizeMeters) * (n - 1);
            float fz = Mathf.Clamp01((z + HalfSize) / heightMeta.sizeMeters) * (n - 1);
            int x0 = Mathf.Min((int)fx, n - 2), z0 = Mathf.Min((int)fz, n - 2);
            float tx = fx - x0, tz = fz - z0;
            float h = Mathf.Lerp(Mathf.Lerp(heights01[z0, x0], heights01[z0, x0 + 1], tx), Mathf.Lerp(heights01[z0 + 1, x0], heights01[z0 + 1, x0 + 1], tx), tz);
            return heightMeta.datumMeters + h * heightMeta.heightRangeMeters;
        }

        int MaskIndex(float x, float z)
        {
            int g = maskMeta.grid;
            int ix = Mathf.FloorToInt((x + HalfSize) / maskMeta.cellMeters);
            int iz = Mathf.FloorToInt((z + HalfSize) / maskMeta.cellMeters);
            if (ix < 0 || iz < 0 || ix >= g || iz >= g) return -1;
            return iz * g + ix;
        }

        public int ReserveAt(float x, float z) { int i = MaskIndex(x, z); return i < 0 ? 0 : reserveGrid[i]; }
        public bool WaterAt(float x, float z) { int i = MaskIndex(x, z); return i >= 0 && waterGrid[i] != 0; }
        public ReserveInfo Reserve(int id) => maskMeta.reserves != null && id > 0 && id <= maskMeta.reserves.Length ? maskMeta.reserves[id - 1] : null;
    }
}
