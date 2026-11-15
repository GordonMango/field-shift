using UnityEngine;

namespace FieldShift
{
    /// <summary>Agronomy formulas. Ported from the web version (js/nasa.js) so both games agree.</summary>
    public static class Agro
    {
        /// <summary>FAO-56 extraterrestrial radiation (eq. 21), as mm/day of evaporation equivalent.</summary>
        public static float ExtraterrestrialMm(float latDeg, int doy)
        {
            float phi = latDeg * Mathf.Deg2Rad;
            float dr = 1 + 0.033f * Mathf.Cos(2 * Mathf.PI * doy / 365f);
            float dec = 0.409f * Mathf.Sin(2 * Mathf.PI * doy / 365f - 1.39f);
            float ws = Mathf.Acos(Mathf.Clamp(-Mathf.Tan(phi) * Mathf.Tan(dec), -1, 1));
            float ra = (24 * 60 / Mathf.PI) * 0.082f * dr * (ws * Mathf.Sin(phi) * Mathf.Sin(dec) + Mathf.Cos(phi) * Mathf.Cos(dec) * Mathf.Sin(ws));
            return Mathf.Max(0, ra) * 0.408f;
        }

        /// <summary>FAO-56 Hargreaves reference evapotranspiration (mm/day), nudged for wind and humidity.</summary>
        public static float ReferenceET(float latDeg, int doy, float tmax, float tmin, float tmean, float wind, float rh)
        {
            float ra = ExtraterrestrialMm(latDeg, doy);
            float range = Mathf.Max(1, tmax - tmin);
            float e = 0.0023f * ra * (tmean + 17.8f) * Mathf.Sqrt(range);
            e *= Mathf.Clamp(1 + 0.05f * (wind - 2), 0.85f, 1.4f);
            e *= Mathf.Clamp(1 + (50 - rh) * 0.006f, 0.8f, 1.25f);
            return Mathf.Clamp(e, 0.05f, 12);
        }

        public static bool InWindow(int doy, int from, int to) => from <= to ? doy >= from && doy <= to : doy >= from || doy <= to;
    }
}
