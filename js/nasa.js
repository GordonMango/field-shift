/* ============================================================
   NASA POWER data module
   Pulls a year of daily weather for a lat/lon from the NASA POWER
   API (MERRA-2 / GEOS), turns it into 52 weekly "threat indices",
   and falls back to a seeded climatology if the request fails.
   ============================================================ */
const NASA = (() => {
  const BASE = 'https://power.larc.nasa.gov/api/temporal/daily/point';
  const PARAMS = ['T2M', 'T2M_MAX', 'T2M_MIN', 'PRECTOTCORR', 'WS2M', 'RH2M', 'GWETROOT'];
  const FILL = -999;

  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const avg = arr => arr.reduce((s, v) => s + v, 0) / (arr.length || 1);
  const sum = arr => arr.reduce((s, v) => s + v, 0);

  function buildUrl(lat, lon, year) {
    const q = new URLSearchParams({
      parameters: PARAMS.join(','),
      community: 'AG',
      longitude: lon.toFixed(3),
      latitude: lat.toFixed(3),
      start: `${year}0101`,
      end: `${year}1231`,
      format: 'JSON',
    });
    return `${BASE}?${q.toString()}`;
  }

  // Replace POWER fill values with the nearest valid neighbour.
  function fillGaps(days, key) {
    let last = null;
    for (const d of days) { if (d[key] !== null && d[key] > FILL + 1) last = d[key]; else d[key] = last; }
    last = null;
    for (let i = days.length - 1; i >= 0; i--) { const d = days[i]; if (d[key] !== null) last = d[key]; else d[key] = last; }
    for (const d of days) if (d[key] === null) d[key] = 0;
  }

  async function fetchYear(lat, lon, year, onStatus = () => {}) {
    const url = buildUrl(lat, lon, year);
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), 20000);
    onStatus(`Requesting NASA POWER daily data for ${lat.toFixed(2)}, ${lon.toFixed(2)} (${year})…`);
    try {
      const res = await fetch(url, { signal: ctrl.signal });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const json = await res.json();
      const p = json.properties && json.properties.parameter;
      if (!p || !p.T2M) throw new Error('Unexpected response shape');
      const dates = Object.keys(p.T2M).sort();
      const days = dates.map(d => ({
        date: d,
        t: num(p.T2M[d]), tmax: num(p.T2M_MAX[d]), tmin: num(p.T2M_MIN[d]),
        precip: num(p.PRECTOTCORR[d]), wind: num(p.WS2M[d]), rh: num(p.RH2M[d]), soil: num(p.GWETROOT[d]),
      }));
      ['t', 'tmax', 'tmin', 'precip', 'wind', 'rh', 'soil'].forEach(k => fillGaps(days, k));
      const valid = days.filter(d => d.t !== null).length;
      if (days.length < 300 || valid < 300) throw new Error('Year incomplete in POWER archive');
      onStatus(`Received ${days.length} days of NASA POWER data (MERRA-2).`);
      return { days, weeks: weekly(days), source: 'live', url, lat, lon, year };
    } catch (err) {
      onStatus(`NASA POWER unavailable (${err.message}). Using built-in climatology for this location.`);
      const days = synthetic(lat, lon, year);
      return { days, weeks: weekly(days), source: 'fallback', url, lat, lon, year, error: err.message };
    } finally {
      clearTimeout(timer);
    }
  }

  function num(v) { return (v === undefined || v === null || v <= FILL + 1) ? null : v; }

  // ---------- weekly aggregation + threat indices ----------
  function weekly(days) {
    const weeks = [];
    for (let w = 0; w < 52; w++) {
      const slice = days.slice(w * 7, w * 7 + 7);
      if (!slice.length) break;
      const mean = k => avg(slice.map(d => d[k]));
      const precip = sum(slice.map(d => d.precip));
      const tmax = mean('tmax');
      const tmin = Math.min(...slice.map(d => d.tmin));
      const t = mean('t');
      const wind = mean('wind');
      const windMax = Math.max(...slice.map(d => d.wind));
      const rh = mean('rh');
      const soil = mean('soil');
      weeks.push({ w, start: slice[0].date, end: slice[slice.length - 1].date, precip, tmax, tmin, t, wind, windMax, rh, soil });
    }
    // Indices blend absolute agronomic thresholds with "relative to this location's own year",
    // so every farm has a real dry season, windy season, wet season and hot season.
    const stat = k => { const v = weeks.map(w => w[k]).sort((a, b) => a - b); return { min: v[0], max: v[v.length - 1], med: v[Math.floor(v.length / 2)] }; };
    const sS = stat('soil'), sW = stat('windMax'), sT = stat('tmax'), sP = stat('precip');
    const rel = (v, lo, hi) => hi > lo ? clamp((v - lo) / (hi - lo), 0, 1) : 0;
    for (const wk of weeks) {
      const rainFactor = wk.precip < 5 ? 1 : wk.precip < 15 ? 0.5 : 0.15;
      const dryRel = rel(sS.max - wk.soil, 0, sS.max - sS.min);
      wk.index = {
        drought: Math.max(clamp(1 - wk.soil / 0.45, 0, 1), Math.pow(dryRel, 1.5)) * rainFactor,
        heat:    Math.max(clamp((wk.tmax - 28) / 10, 0, 1), Math.pow(rel(wk.tmax, sT.med, sT.max), 2) * (wk.tmax > 24 ? 1 : 0.3)),
        wind:    Math.max(clamp((wk.windMax - 4.5) / 5, 0, 1), Math.pow(rel(wk.windMax, sW.med, sW.max), 2)) * (wk.soil < sS.med ? 1 : 0.6),
        flood:   Math.max(clamp((wk.precip - 30) / 50, 0, 1), Math.pow(rel(wk.precip, sP.med, sP.max), 2) * (wk.precip > 12 ? 1 : 0)),
        frost:   wk.tmin < 0 ? clamp(0.3 + (-wk.tmin) / 8, 0, 1) : 0,
        pest:    clamp((wk.t - 20) / 10, 0, 1) * clamp((wk.rh - 50) / 30, 0, 1),
      };
    }
    return weeks;
  }

  // Find the start week (wrapping) whose `len`-week window has the highest summed index for `type`.
  function bestWindow(weeks, type, len) {
    let best = 0, bestScore = -1;
    for (let s = 0; s < weeks.length; s++) {
      let score = 0;
      for (let i = 0; i < len; i++) score += weeks[(s + i) % weeks.length].index[type];
      if (score > bestScore) { bestScore = score; best = s; }
    }
    return { start: best, score: bestScore };
  }

  // Dominant threat for a week (for sky colour / forecast text)
  function dominant(week) {
    let top = null, v = 0.12;
    for (const [k, val] of Object.entries(week.index)) if (val > v) { v = val; top = k; }
    return top;
  }

  // ---------- fallback climatology (seeded, latitude-aware) ----------
  function mulberry32(a) {
    return function () {
      a |= 0; a = a + 0x6D2B79F5 | 0;
      let t = Math.imul(a ^ a >>> 15, 1 | a);
      t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
      return ((t ^ t >>> 14) >>> 0) / 4294967296;
    };
  }

  function synthetic(lat, lon, year) {
    const rnd = mulberry32(Math.floor(lat * 1000) * 31 + Math.floor(lon * 1000) + year);
    const nh = lat >= 0, absLat = Math.abs(lat);
    const tMean = 28 - absLat * 0.42;
    const amp = Math.min(18, absLat * 0.42);
    const peakDay = nh ? 200 : 20;
    const wetPeak = absLat < 15 ? 110 : (nh ? 185 : 5);
    const annualRain = absLat < 10 ? 1400 : absLat < 20 ? 600 : absLat < 35 ? 480 : 750;
    const days = [];
    let soil = 0.4;
    const start = new Date(Date.UTC(year, 0, 1));
    for (let doy = 0; doy < 365; doy++) {
      const season = Math.cos((doy - peakDay) / 365 * Math.PI * 2);
      const t = tMean + amp * season + (rnd() - 0.5) * 6;
      const tmax = t + 6 + rnd() * 4;
      const tmin = t - 6 - rnd() * 4;
      const rainProb = 0.12 + 0.3 * (0.5 + 0.5 * Math.cos((doy - wetPeak) / 365 * Math.PI * 2));
      const precip = rnd() < rainProb ? -Math.log(1 - rnd() * 0.98) * (annualRain / 110) : 0;
      const wind = 1.8 + rnd() * 3 + (rnd() < 0.08 ? 3.5 : 0) + (soil < 0.3 ? 0.8 : 0);
      const rh = clamp(45 + (precip > 0 ? 30 : 0) + (rnd() - 0.5) * 20 + (absLat < 15 ? 10 : 0), 18, 98);
      soil = clamp(soil * 0.93 + precip * 0.012 + 0.015 - (tmax > 30 ? 0.01 : 0), 0.05, 0.95);
      const d = new Date(start.getTime() + doy * 86400000);
      const date = d.toISOString().slice(0, 10).replace(/-/g, '');
      days.push({ date, t, tmax, tmin, precip, wind, rh, soil });
    }
    return days;
  }

  // ---------- formatting helpers ----------
  const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  function dateLabel(yyyymmdd, withYear = false) {
    const y = yyyymmdd.slice(0, 4), m = +yyyymmdd.slice(4, 6) - 1, d = +yyyymmdd.slice(6, 8);
    return `${MONTHS[m]} ${d}${withYear ? ', ' + y : ''}`;
  }

  return { fetchYear, weekly, bestWindow, dominant, dateLabel, buildUrl, PARAMS };
})();
