/* ============================================================
   Defend Your Farm — game state, daily field simulation, UI
   Each in-game day replays one real day of NASA POWER weather.
   Every plot runs its own soil water balance, heat and frost
   check, erosion, waterlogging and pest population, and the
   farming practices change those numbers the way they do in a
   real field.
   ============================================================ */
const Game = (() => {
  const { LANES, COLS, HAZARDS, ITEMS, LEVELS, REGIONS, ROTATION_CHOICES, DATASETS, ROTATION_RULES, LAYERS, SIM_NOTES } = CONTENT;
  const DAY_SECONDS = 1.25;
  const TAW = 100;            // mm of plant-available water a full-depth plot can hold
  const SEASON_DAYS = 91;     // final stage: 4 seasons of 13 weeks
  const DIFF = { easy: 0.7, normal: 1.0, hard: 1.3 };
  const SHELTER = [0.4, 0.5, 0.65, 0.8];   // wind multiplier 1..4 plots downwind of a windbreak
  const HZ = Object.keys(HAZARDS);
  const $ = s => document.querySelector(s);
  const $$ = s => Array.from(document.querySelectorAll(s));
  const fmt$ = v => (v < -0.5 ? '-$' : '$') + Math.abs(Math.round(v)).toLocaleString();
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  let uid = 1;

  const S = {
    setup: { regionId: REGIONS[0].id, lat: REGIONS[0].lat, lon: REGIONS[0].lon, budget: 1500, water: 300, year: 2025, difficulty: 'normal', unlockAll: false },
    data: null, levelStarts: [],
    levelIdx: 0, maxUnlocked: 0, unlocked: new Set(),
    level: null, levelWeekStart: 0,
    budget: 0, water: 0, waterCap: 0,
    phase: 'idle', day: 0, dayTimer: 0, running: false, speed: 1,
    grid: [], rows: [], laneMods: [],
    selected: null, inspect: null, xray: false,
    stats: null, rotation: null, log: [], today: null, seen: new Set(),
  };

  // ============ screens & modals ============
  function showScreen(id) { $$('.screen').forEach(s => s.classList.toggle('active', s.id === id)); if (id === 'screen-game') setTimeout(SceneMgr.resize, 30); }
  function openModal(id) { $('#' + id).classList.add('open'); }
  function closeModal(id) { $('#' + id).classList.remove('open'); }
  function anyModalOpen() { return $$('.modal.open').length > 0; }

  // ============ setup screen ============
  function initSetup() {
    const sel = $('#sel-region');
    REGIONS.forEach(r => { const o = document.createElement('option'); o.value = r.id; o.textContent = r.name; sel.appendChild(o); });
    const custom = document.createElement('option'); custom.value = 'custom'; custom.textContent = 'Custom coordinates…'; sel.appendChild(custom);
    sel.addEventListener('change', () => {
      const r = REGIONS.find(x => x.id === sel.value);
      $('#custom-coords').classList.toggle('hidden', sel.value !== 'custom');
      if (r) { $('#inp-lat').value = r.lat; $('#inp-lon').value = r.lon; $('#region-note').textContent = r.note; }
      else $('#region-note').textContent = 'Enter any latitude / longitude on Earth. NASA POWER covers the whole globe at 0.5° resolution.';
    });
    $('#inp-lat').value = REGIONS[0].lat; $('#inp-lon').value = REGIONS[0].lon; $('#region-note').textContent = REGIONS[0].note;
    $('#btn-geo').addEventListener('click', () => {
      if (!navigator.geolocation) return setStatus('Geolocation not available in this browser.');
      setStatus('Asking the browser for your location…');
      navigator.geolocation.getCurrentPosition(p => {
        sel.value = 'custom'; $('#custom-coords').classList.remove('hidden');
        $('#inp-lat').value = p.coords.latitude.toFixed(3); $('#inp-lon').value = p.coords.longitude.toFixed(3);
        setStatus('Using your current location.');
      }, () => setStatus('Location permission denied. Pick a region instead.'));
    });
    const budget = $('#inp-budget'), water = $('#inp-water');
    const syncLabels = () => { $('#lbl-budget').textContent = fmt$(+budget.value); $('#lbl-water').textContent = water.value + ' units'; };
    budget.addEventListener('input', syncLabels); water.addEventListener('input', syncLabels); syncLabels();
    renderStageList();
    $('#chk-unlock').addEventListener('change', renderStageList);
    $('#btn-fetch').addEventListener('click', startFromSetup);
  }
  function setStatus(msg, cls = '') { const el = $('#data-status'); el.textContent = msg; el.className = 'status ' + cls; }

  function renderStageList() {
    const wrap = $('#stage-list'); wrap.innerHTML = '';
    const all = $('#chk-unlock').checked;
    LEVELS.forEach((lv, i) => {
      const b = document.createElement('button');
      const locked = !all && i > S.maxUnlocked;
      b.className = 'stage-btn' + (locked ? ' locked' : '') + (i === S.levelIdx ? ' chosen' : '');
      b.innerHTML = `<span class="stage-icon">${lv.focus ? HAZARDS[lv.focus].icon : Pixel.img('calendar')}</span><span><strong>${lv.name}</strong><br><small>${lv.weeks} weeks · ${lv.focus ? 'teaches ' + HAZARDS[lv.focus].name.toLowerCase() : 'all hazards, crop rotation'}</small></span>${locked ? '<span class="lock">LOCKED</span>' : ''}`;
      b.disabled = locked;
      b.addEventListener('click', () => { S.levelIdx = i; renderStageList(); });
      wrap.appendChild(b);
    });
  }

  async function startFromSetup() {
    const sel = $('#sel-region');
    const lat = parseFloat($('#inp-lat').value), lon = parseFloat($('#inp-lon').value);
    if (isNaN(lat) || isNaN(lon) || Math.abs(lat) > 90 || Math.abs(lon) > 180) return setStatus('Latitude must be -90…90 and longitude -180…180.', 'err');
    Object.assign(S.setup, { regionId: sel.value, lat, lon, budget: +$('#inp-budget').value, water: +$('#inp-water').value, year: +$('#sel-year').value, difficulty: $('#sel-diff').value, unlockAll: $('#chk-unlock').checked });
    $('#btn-fetch').disabled = true;
    const data = await NASA.fetchYear(lat, lon, S.setup.year, m => setStatus(m, 'busy'));
    $('#btn-fetch').disabled = false;
    S.data = data;
    setStatus(data.source === 'live' ? `Live NASA POWER data loaded: ${data.days.length} days for ${S.setup.year}.` : `Offline climatology in use (NASA POWER request failed: ${data.error}).`, data.source === 'live' ? 'ok' : 'warn');
    S.levelStarts = LEVELS.map(lv => lv.focus ? NASA.bestWindow(data.weeks, lv.focus, lv.weeks, lv.focus === 'frost' ? -99 : 12).start : 0);
    startLevel(S.levelIdx);
  }

  // ============ level lifecycle ============
  function computeUnlocked(idx) {
    const set = new Set(['shovel']);
    LEVELS.forEach((lv, i) => { if (S.setup.unlockAll || i <= idx) lv.unlock.forEach(u => set.add(u)); });
    if (LEVELS[idx].rotation) Object.keys(ITEMS).forEach(k => set.add(k));
    return set;
  }
  const totalDays = () => S.level.weeks * 7;
  const seasonDays = () => S.level.rotation ? SEASON_DAYS : totalDays();
  const seasonEnd = d => S.level.rotation ? (Math.floor(d / SEASON_DAYS) + 1) * SEASON_DAYS : totalDays();
  const dayData = d => S.data.days[(S.levelWeekStart * 7 + d) % S.data.days.length];

  function startLevel(idx) {
    S.levelIdx = idx; S.level = LEVELS[idx]; S.levelWeekStart = S.levelStarts[idx] || 0;
    S.unlocked = computeUnlocked(idx);
    S.budget = S.setup.budget; S.water = S.setup.water; S.waterCap = Math.max(S.setup.water, 100);
    S.phase = 'idle'; S.day = 0; S.dayTimer = 0; S.running = false; S.speed = 1; $('#btn-speed').textContent = '1×';
    // starting soil moisture from NASA root-zone wetness (GWETROOT, SMAP-style)
    const d0 = dayData(0);
    const m0 = clamp((d0.soil - 0.12) / 0.6, 0.08, 1) * TAW;
    S.grid = Array.from({ length: LANES }, () => Array.from({ length: COLS }, () => ({
      ground: null, surface: null, sky: null,
      soil: { m: m0, taw: TAW, sat: 0, topsoil: 100, pest: 0.02, wl: 0, dry: 3, frost: false, irrigated: 0, today: {} },
    })));
    S.rows = Array.from({ length: LANES }, () => ({ drip: null, drain: null }));
    S.laneMods = Array.from({ length: LANES }, () => ({ pestMult: 1, yieldMult: 1, tawMult: 1 }));
    S.naturalDrain = avg(S.data.days.map(d => d.soil)) > 0.5 ? 4 : 8;
    S.selected = null; S.inspect = null; S.log = []; S.rotation = null; S.today = d0; S.seen = new Set();
    S.stats = { earned: 0, potential: 0, spent: 0, waterUsed: 0, cropsLost: 0, loss: Object.fromEntries(HZ.map(h => [h, 0])), heatThirsty: 0 };
    SceneMgr.clearDynamic(); SceneMgr.setMood(null); SceneMgr.setWeather({ rain: 0, wind: 0, dust: 0, frost: false, heat: 0 });
    showScreen('screen-game');
    $('#lane-hint-right').textContent = S.data.prevailing ? `windward edge · prevailing wind from ${S.data.prevailing}` : 'windward edge';
    refreshPlots(); renderToolbar(); renderHUD(); renderLog(); renderWeather(); renderInspector();
    $('#float-layer').innerHTML = '';
    if (S.level.rotation) openRotationPlanner(); else openLevelIntro();
  }

  function openLevelIntro() {
    const lv = S.level, w0 = S.data.weeks[S.levelWeekStart], w1 = S.data.weeks[(S.levelWeekStart + lv.weeks - 1) % 52];
    $('#intro-title').textContent = lv.name;
    $('#intro-text').textContent = lv.intro;
    $('#intro-tip').textContent = lv.tip;
    const win = `${NASA.dateLabel(w0.start)} to ${NASA.dateLabel(w1.end, true)}`;
    $('#intro-window').innerHTML = lv.focus
      ? `${Pixel.img('satellite')} NASA POWER scanned ${S.setup.year} at your location and found the worst ${lv.weeks}-week <b>${HAZARDS[lv.focus].name.toLowerCase()}</b> window: <b>${win}</b>. Every day you play is a real day from that window.`
      : `${Pixel.img('satellite')} All 52 weeks of ${S.setup.year} at your location: <b>${win}</b>.`;
    // be honest when this place and year barely have the hazard, and offer a region that does
    const sev = lv.focus ? avg(Array.from({ length: lv.weeks }, (_, i) => S.data.weeks[(S.levelWeekStart + i) % 52].index[lv.focus])) : 1;
    const alt = lv.suggest && REGIONS.find(r => r.id === lv.suggest);
    const mild = lv.focus && sev < 0.3 && alt && S.setup.regionId !== alt.id;
    $('#intro-mild').classList.toggle('hidden', !mild);
    if (mild) {
      $('#intro-mild-text').textContent = `${HAZARDS[lv.focus].name} is mild at your location in ${S.setup.year}, so other weather may matter more in this stage. That is real: not every farm faces every hazard. For the full lesson, try ${alt.name}.`;
      $('#btn-intro-alt').textContent = `Play in ${alt.name.split(',')[0]}`;
      $('#btn-intro-alt').onclick = () => switchRegion(alt);
    }
    const cards = $('#intro-unlocks'); cards.innerHTML = '';
    if (lv.focus) cards.appendChild(hazardCard(HAZARDS[lv.focus]));
    (lv.rotation ? [] : lv.unlock.filter(u => u !== 'shovel')).forEach(id => cards.appendChild(itemCard(ITEMS[id])));
    $('#btn-intro-start').textContent = 'Go to the field';
    openModal('modal-intro');
  }
  function hazardCard(h) {
    const d = document.createElement('div'); d.className = 'card threat';
    d.innerHTML = `<div class="card-icon">${h.icon}</div><div><strong>Hazard: ${h.name}</strong><p>${h.desc}</p><p class="small"><b>What helps:</b> ${h.fix}</p><p class="small data">NASA data: ${h.data}</p></div>`;
    return d;
  }
  function scopeText(it) { return { row: 'whole row', plot: 'one plot', shelter: '4 plots downwind', neighbours: 'its plot and the 8 around it' }[it.scope] || ''; }
  function itemCard(it) {
    const d = document.createElement('div'); d.className = 'card';
    const extra = it.kind === 'crop'
      ? `Harvest up to ${fmt$(it.value)} · heat limit ${it.heat} °C · frost limit ${it.frost} °C · family: ${it.family}`
      : `${it.effect} · Covers ${scopeText(it)} · Helps with: ${it.helps.map(h => HAZARDS[h].name.toLowerCase()).join(', ')}`;
    d.innerHTML = `<div class="card-icon">${it.icon}</div><div><strong>${it.name}</strong> <span class="badge">${LAYERS[it.layer].name}</span> <span class="badge cost">${fmt$(it.cost)}</span><p>${it.desc}</p><p class="small">${extra}</p></div>`;
    return d;
  }

  async function switchRegion(r) {
    closeModal('modal-intro');
    $('#sel-region').value = r.id; $('#inp-lat').value = r.lat; $('#inp-lon').value = r.lon; $('#region-note').textContent = r.note;
    toast(`${Pixel.img('satellite')} Fetching NASA POWER data for ${r.name}…`);
    await startFromSetup();
  }

  // Intro closed: the planting window. The clock is stopped until the player starts the season.
  function enterPrep() {
    closeModal('modal-intro'); closeModal('modal-rotation');
    S.phase = 'prep'; S.running = false; S.day = 0; S.dayTimer = 0;
    if (S.level.rotation) plantRotation(0);
    showForecast(0);
    log(`${Pixel.img('sprout')} Planting window. Budget ${fmt$(S.budget)}, water ${Math.round(S.water)} units. Place crops and practices, then press START SEASON.`);
    hank('Plant first, then protect. Check the 7-day forecast on the right before you spend: it is real NASA data for the days ahead.');
    renderHUD();
  }
  function startSeason() {
    if (S.phase !== 'prep') return;
    S.phase = 'season'; S.running = true; S.dayTimer = 0;
    beginDay(0);
    renderHUD();
  }

  // ============ the daily simulation ============
  function beginDay(d) {
    const w = S.today = dayData(d);
    // weather visuals
    const thr = S.data.windErode;
    const dusty = w.wind > thr ? clamp((w.wind - thr + 0.6) / 3, 0, 1) : 0;
    SceneMgr.setWeather({ rain: w.precip, wind: w.wind, dust: dusty, frost: w.tmin < 0, heat: w.tmax > 32 ? 1 : 0 });
    SceneMgr.setMood(moodOf(w));
    SceneMgr.setCoverMode(w.tmin < 4 ? 'frost' : 'shade');
    if (d % 7 === 0) weeklyOutlook(d);
    renderWeather(); showForecast(d);
  }
  function moodOf(w) {
    if (w.tmin < 0) return 'frost';
    if (w.precip > 20) return 'flood';
    if (w.wind > S.data.windErode) return 'wind';
    if (w.tmax > 33) return 'heat';
    if (w.precip < 1 && w.soil < 0.35) return 'drought';
    return null;
  }

  function neighbourMarigold(l, c) {
    for (let dl = -1; dl <= 1; dl++) for (let dc = -1; dc <= 1; dc++) {
      const ll = l + dl, cc = c + dc;
      if (ll >= 0 && ll < LANES && cc >= 0 && cc < COLS) { const g = S.grid[ll][cc].ground; if (g && g.item.id === 'marigold') return true; }
    }
    return false;
  }
  function shelterAt(l, c) {
    for (let k = 1; k <= 4; k++) { const cc = c + k; if (cc >= COLS) break; const g = S.grid[l][cc].ground; if (g && g.item.id === 'windbreak') return SHELTER[k - 1]; }
    return 1;
  }
  function neighbourPest(l, c) {
    let s = 0, n = 0;
    for (const [dl, dc] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) { const ll = l + dl, cc = c + dc; if (ll >= 0 && ll < LANES && cc >= 0 && cc < COLS) { s += S.grid[ll][cc].soil.pest; n++; } }
    return n ? s / n : 0;
  }

  function simulateDay(d) {
    const w = dayData(d), D = DIFF[S.setup.difficulty] || 1, thr = S.data.windErode;
    // the farm pond catches some of the rain
    if (w.precip > 1) S.water = Math.min(S.waterCap, S.water + w.precip * 0.35);
    const pestBefore = S.grid.map(r => r.map(c => c.soil.pest));
    let floats = 0;
    const firstHit = {};
    for (let l = 0; l < LANES; l++) for (let c = 0; c < COLS; c++) {
      const cell = S.grid[l][c], soil = cell.soil, row = S.rows[l];
      const crop = cell.ground && cell.ground.item.kind === 'crop' ? cell.ground : null;
      const surf = cell.surface ? cell.surface.item.id : null;
      const hoop = !!cell.sky;
      const shelter = shelterAt(l, c);
      const wind = w.wind * shelter;
      const taw = soil.taw = TAW * S.laneMods[l].tawMult * (0.7 + 0.3 * soil.topsoil / 100);
      const canopy = crop ? clamp(crop.age / 28, 0.05, 1) : 0;
      const today = soil.today = { et: 0, irr: 0, erosion: 0, dmg: {} };

      // ---- water out: crop (or bare soil) evapotranspiration ----
      let kc = crop ? crop.item.kc * (0.35 + 0.65 * canopy) : surf === 'cover' ? 0.8 : 0.3;
      if (surf === 'mulch') kc *= 0.8 + 0.07 * canopy;   // mulch mostly saves soil evaporation, which matters most before the canopy closes
      if (surf === 'cover' && crop) kc *= 1.08;
      if (hoop && w.tmax > 25) kc *= 0.9;
      if (shelter < 1) kc *= 0.9;
      const etc = w.et0 * kc;
      const ks = crop ? (soil.m >= 0.5 * taw ? 1 : soil.m / (0.5 * taw)) : (soil.m >= 0.3 * taw ? 1 : soil.m / (0.3 * taw));
      const aet = etc * ks;
      today.et = aet;

      // ---- water in: rain, runoff, saturation, drainage ----
      const P = w.precip;
      let runoff = P > 20 ? (P - 20) * 0.4 : 0;
      if (surf === 'cover') runoff *= 0.5; else if (surf === 'mulch') runoff *= 0.8;
      if (soil.sat > 0) runoff += Math.min(P - runoff, P * 0.3);
      // a saturated plot loses its excess water to the crop first, then the root zone
      const fromSat = Math.min(soil.sat, aet);
      soil.sat -= fromSat;
      soil.m += P - runoff - (aet - fromSat);
      if (soil.m > taw) { soil.sat += soil.m - taw; soil.m = taw; }
      soil.m = Math.max(0, soil.m);
      // heavy soils in humid regions drain slowly (that is why tile drainage is common there)
      const drainRate = (row.drain ? 35 : S.naturalDrain) + (surf === 'cover' ? 4 : 0);
      if (soil.sat > 0 && row.drain) SceneMgr.pulseRow(row.drain.mesh);
      soil.sat = Math.max(0, soil.sat - drainRate);
      soil.dry = P > 2 ? 0 : soil.dry + 1;
      if (crop || cell.surface || cell.ground) soil.used = true;

      // ---- drip irrigation: refill to 85% when a planted plot drops below 60% ----
      if (row.drip && crop && soil.sat === 0 && soil.m < 0.6 * taw) {
        const need = 0.85 * taw - soil.m;
        const units = need / 20 / 0.9;
        const used = Math.min(units, S.water);
        if (used > 0.01) {
          S.water -= used; S.stats.waterUsed += used;
          soil.m += used * 20 * 0.9; today.irr = used * 20 * 0.9; soil.irrigated += used;
          SceneMgr.pulseRow(row.drip.mesh);
        }
        if (used < units - 0.01 && !S.dripDryWarned) { S.dripDryWarned = true; log(`${Pixel.img('water')} The pond is empty: drip lines cannot irrigate until it rains. Plots will start to dry out.`, 'bad'); }
      }
      if (S.water > 5) S.dripDryWarned = false;

      // ---- erosion: wind on dry bare soil, runoff on heavy rain ----
      const bare = (1 - 0.85 * canopy) * (surf === 'cover' ? 0.25 : surf === 'mulch' ? 0.4 : 1) * (cell.ground && !crop ? 0.3 : 1);
      const surfaceDry = soil.dry >= 2 ? clamp(1.2 - soil.m / taw, 0.2, 1) : 0;
      let eroded = 0;
      if (wind > thr && soil.sat === 0) eroded += Math.pow(wind - thr + 0.5, 1.5) * 1.2 * surfaceDry * bare;
      eroded += runoff * 0.05 * bare;
      eroded *= D;
      if (eroded > 0) { const before = soil.topsoil; soil.topsoil = Math.max(40, soil.topsoil - eroded); today.erosion = before - soil.topsoil; }

      // ---- pests ----
      const warm = clamp((w.t - 15) / 12, 0, 1) * (w.tmax > 38 ? 0.6 : 1);
      const humid = clamp((w.rh - 40) / 40, 0, 1);
      if (crop) {
        let r = 0.12 * warm * (0.35 + 0.65 * humid);
        let imm = 0.0015 * warm * humid * S.laneMods[l].pestMult;
        const np = pestBefore[l][c] > 0 ? neighbourPest(l, c) : 0;
        if (np > 0.4) imm += 0.01 * (np - 0.4);
        if (neighbourMarigold(l, c)) { r *= 0.5; imm *= 0.4; }
        if (hoop) imm *= 0.5;
        soil.pest = clamp(soil.pest + r * soil.pest * (1 - soil.pest) + imm, 0, 1);
      } else soil.pest *= 0.85;
      if (w.t < 10) soil.pest *= 0.9;

      // ---- crop damage ----
      soil.frost = w.tmin < 0;
      if (crop) {
        const it = crop.item, dmg = today.dmg;
        if (ks < 1) dmg.drought = (1 - ks) * 2.4 * it.droughtSens;
        // transpiration cooling: a watered canopy runs 3-6 °C below air temperature (more in dry air), a thirsty one 2 °C above
        const cooling = ks >= 0.95 ? 3 + clamp((50 - w.rh) / 10, 0, 3) : ks < 0.6 ? -2 : 0;
        const canopyT = w.tmax - (hoop ? 3 : 0) - (surf === 'mulch' ? 1 : 0) - cooling;
        if (canopyT > it.heat) { dmg.heat = Math.min(4, (canopyT - it.heat) * 0.6); if (ks < 0.95) S.stats.heatThirsty += dmg.heat * D / 100 * crop.potential; }
        const nightT = w.tmin + (hoop ? 3 : 0) + (soil.m / taw > 0.6 ? 1 : 0);
        if (nightT < it.frost) dmg.frost = Math.min(45, (it.frost - nightT) * 10 + 5);
        // roots run out of oxygen after about two saturated days
        if (soil.sat > 5) { soil.wl = Math.min(soil.wl + 1, 5); if (soil.wl >= 2) dmg.flood = Math.min(6, 1.5 * (soil.wl - 1)) * clamp(soil.sat / 25, 0.5, 1.5); } else soil.wl = Math.max(0, soil.wl - 2);
        // sandblasting: blowing soil shreds seedlings before their canopy closes
        if (wind > thr && canopy < 0.6 && surfaceDry > 0) dmg.wind = (wind - thr + 0.3) * 8 * (1 - canopy) * surfaceDry * (surf ? 0.4 : 1);
        if (soil.pest > 0.4) dmg.pest = (soil.pest - 0.4) * 4;
        let total = 0;
        for (const h of Object.keys(dmg)) {
          dmg[h] *= D; total += dmg[h];
          crop.loss[h] = (crop.loss[h] || 0) + dmg[h];
          S.stats.loss[h] += dmg[h] / 100 * crop.potential;
          if (dmg[h] > 1.5 && !S.seen.has(h) && !firstHit[h]) firstHit[h] = [l, c];
        }
        crop.hp -= total;
        crop.age++;
        if (total > 4 && floats < 4) { const top = Object.keys(dmg).sort((a, b) => dmg[b] - dmg[a])[0]; floatLabel(l, c, `${HAZARDS[top].icon} -${Math.round(total)}%`, 'loss'); floats++; }
        if (crop.hp <= 0) {
          const cause = Object.keys(crop.loss).sort((a, b) => crop.loss[b] - crop.loss[a])[0];
          S.stats.potential += crop.potential; S.stats.cropsLost++;
          log(`${crop.item.icon} ${crop.item.name} in row ${l + 1}, plot ${c + 1} died. Main cause: ${HAZARDS[cause].name.toLowerCase()}.`, 'bad');
          floatLabel(l, c, `${crop.item.icon} lost`, 'loss');
          removePlaced(crop);
        } else { SceneMgr.setHealth(crop.mesh, crop.hp); SceneMgr.setGrowth(crop.mesh, canopy); }
      }
    }
    // explain each hazard the first time it actually hurts a crop
    for (const [h, [l, c]] of Object.entries(firstHit)) {
      S.seen.add(h);
      log(`${HAZARDS[h].icon} <b>${HAZARDS[h].name}</b> hit row ${l + 1}, plot ${c + 1}. ${HAZARDS[h].fix}`, 'warn');
      hank(`${HAZARDS[h].name}! ${HAZARDS[h].desc} ${HAZARDS[h].fix}`);
      if (S.speed > 1) { S.running = false; S.autoPaused = true; }
    }
    refreshPlots();
  }

  function weeklyOutlook(d) {
    const days = []; for (let i = 0; i < 7 && d + i < totalDays(); i++) days.push(dayData(d + i));
    if (!days.length) return;
    const rain = days.reduce((s, x) => s + x.precip, 0), hi = Math.max(...days.map(x => x.tmax)), lo = Math.min(...days.map(x => x.tmin));
    const wind = Math.max(...days.map(x => x.wind)), et = days.reduce((s, x) => s + x.et0, 0), maxRain = Math.max(...days.map(x => x.precip));
    const risks = [];
    if (rain < et * 0.4) risks.push('drought');
    if (hi > 32) risks.push('heat');
    if (wind > S.data.windErode) risks.push('wind');
    if (maxRain > 30 || rain > 60) risks.push('flood');
    if (lo < 1) risks.push('frost');
    if (days.some(x => x.t > 20 && x.rh > 60)) risks.push('pest');
    const week = Math.floor(d / 7) + 1;
    log(`<b>Week ${week}/${S.level.weeks}</b> (${NASA.dateLabel(days[0].date)}): ${rain.toFixed(0)} mm rain vs ${et.toFixed(0)} mm evaporation demand, highs to ${hi.toFixed(0)} °C, lows to ${lo.toFixed(0)} °C. ${risks.length ? 'Watch for: ' + risks.map(r => HAZARDS[r].icon + ' ' + HAZARDS[r].name.toLowerCase()).join(', ') + '.' : 'Easy week.'}`);
  }

  // ============ harvest & seasons ============
  function harvestAll(label) {
    let total = 0, n = 0;
    forEachPlaced((p, l, c) => {
      if (p.item.kind !== 'crop') return;
      const soil = S.grid[l][c].soil;
      const v = p.potential * Math.max(0, p.hp) / 100 * (0.4 + 0.6 * soil.topsoil / 100);
      S.budget += v; S.stats.earned += v; S.stats.potential += p.potential; total += v; n++;
      floatLabel(l, c, `${Pixel.img('coin')} +${fmt$(v)}`, 'gain');
      removePlaced(p);
    });
    if (n) log(`${Pixel.img('coin')} ${label}: harvested ${n} plots for ${fmt$(total)}.`);
  }

  // ============ placement ============
  function forEachPlaced(fn) { for (let l = 0; l < LANES; l++) for (let c = 0; c < COLS; c++) for (const s of ['ground', 'surface', 'sky']) { const p = S.grid[l][c][s]; if (p) fn(p, l, c, s); } }

  function onCellClick(lane, col) {
    if (S.phase === 'idle' || S.phase === 'done' || anyModalOpen()) return;
    S.inspect = { lane, col };
    const cell = S.grid[lane][col];
    if (!S.selected) { renderInspector(); return; }
    if (S.selected === 'shovel') {
      for (const s of ['ground', 'surface', 'sky']) if (cell[s]) {
        const p = cell[s];
        if (p.item.kind === 'crop') { S.stats.potential += p.potential * Math.max(0, p.hp) / 100; }
        removePlaced(p); log(`Cleared ${p.item.name} from row ${lane + 1}, plot ${col + 1}.`); SceneMgr.flashTile(lane, col, 0xffffff); renderHUD(); renderInspector(); return;
      }
      for (const k of ['drip', 'drain']) if (S.rows[lane][k]) { SceneMgr.remove(S.rows[lane][k].mesh); S.rows[lane][k] = null; log(`Removed the ${ITEMS[k].name.toLowerCase()} from row ${lane + 1}.`); renderInspector(); return; }
      return;
    }
    const item = ITEMS[S.selected];
    if (item.scope === 'row') {
      if (S.rows[lane][item.id]) return toast(`Row ${lane + 1} already has a ${item.name.toLowerCase()}.`);
    } else if (cell[item.layer]) { toast(`${LAYERS[item.layer].name} of this plot already has ${cell[item.layer].item.name}.`); SceneMgr.flashTile(lane, col, 0xff4444); return; }
    if (S.budget < item.cost) return toast(`Not enough budget for ${item.name} (${fmt$(item.cost)}).`);
    place(item, lane, col, item.cost);
    renderHUD(); renderToolbar(); renderInspector();
  }
  function place(item, lane, col, cost) {
    S.budget -= cost; S.stats.spent += cost;
    const p = { uid: uid++, item, lane, col, hp: 100, age: 0, loss: {}, mesh: SceneMgr.addItem(item, lane, col) };
    if (item.scope === 'row') { S.rows[lane][item.id] = p; for (let c = 0; c < COLS; c++) SceneMgr.flashTile(lane, c, 0x4fc3f7); return p; }
    S.grid[lane][col][item.layer] = p;
    if (item.kind === 'crop') {
      // crops placed in the planting window of a mid-season stage are an established stand; otherwise they start as seedlings
      const established = S.phase === 'prep' && S.level.sow !== 'seed';
      p.age = established ? 30 : 0;
      const remaining = S.phase === 'prep' ? seasonDays() : Math.max(0, seasonEnd(S.day) - S.day);
      p.potential = item.value * S.laneMods[lane].yieldMult * remaining / seasonDays();
      SceneMgr.setGrowth(p.mesh, clamp(p.age / 28, 0.05, 1));
    }
    SceneMgr.flashTile(lane, col, 0x88ff88);
    return p;
  }
  function removePlaced(p) {
    if (!p) return;
    const cell = S.grid[p.lane][p.col];
    if (cell[p.item.layer] === p) cell[p.item.layer] = null;
    SceneMgr.remove(p.mesh);
  }

  // ============ rotation planner (final stage) ============
  function openRotationPlanner() {
    const wrap = $('#rotation-grid'); wrap.innerHTML = '';
    const seasons = [0, 13, 26, 39].map(w => { const a = S.data.weeks[w], b = S.data.weeks[w + 12]; return `${NASA.dateLabel(a.start)} – ${NASA.dateLabel(b.end)}`; });
    const defaults = [['corn', 'soy', 'wheat', 'cover'], ['soy', 'corn', 'cover', 'wheat'], ['wheat', 'tomato', 'soy', 'cover'], ['tomato', 'cover', 'corn', 'soy'], ['cover', 'wheat', 'tomato', 'fallow']];
    const table = document.createElement('table');
    table.innerHTML = `<thead><tr><th>Row</th>${seasons.map((s, i) => `<th>Season ${i + 1}<br><small>${s}</small></th>`).join('')}</tr></thead>`;
    const tb = document.createElement('tbody');
    S.rotation = defaults.map(r => r.slice());
    for (let l = 0; l < LANES; l++) {
      const tr = document.createElement('tr'); tr.innerHTML = `<td>Row ${l + 1}</td>`;
      for (let s = 0; s < 4; s++) {
        const td = document.createElement('td'); const sel = document.createElement('select');
        ROTATION_CHOICES.forEach(c => { const o = document.createElement('option'); o.value = c.id; o.textContent = c.name; sel.appendChild(o); });
        sel.value = S.rotation[l][s];
        sel.addEventListener('change', () => { S.rotation[l][s] = sel.value; renderRotationAnalysis(); });
        td.appendChild(sel); tr.appendChild(td);
      }
      tb.appendChild(tr);
    }
    table.appendChild(tb); wrap.appendChild(table);
    $('#rotation-rules').innerHTML = ROTATION_RULES.map(r => `<li>${r}</li>`).join('');
    renderRotationAnalysis();
    openModal('modal-rotation');
  }
  function analyseLane(seq) {
    const notes = []; let score = 0;
    for (let s = 0; s < 4; s++) {
      const cur = seq[s], prev = s > 0 ? seq[s - 1] : null;
      const curCrop = ITEMS[cur] && ITEMS[cur].kind === 'crop' ? ITEMS[cur] : null;
      const prevCrop = ITEMS[prev] && ITEMS[prev].kind === 'crop' ? ITEMS[prev] : null;
      if (curCrop && prevCrop && curCrop.family === prevCrop.family) { notes.push(`S${s + 1}: ${curCrop.family} after ${prevCrop.family}, pests build up`); score -= 2; }
      else if (curCrop && prevCrop && prevCrop.nitrogen) { notes.push(`S${s + 1}: ${curCrop.name} after soybean, yield ×1.25`); score += 2; }
      else if (curCrop && prev === 'cover') { notes.push(`S${s + 1}: after cover crop, +15% soil water, yield ×1.1`); score += 1; }
    }
    if (!seq.includes('cover') && !seq.includes('fallow')) { notes.push('No rest season: the soil never recovers'); score -= 1; }
    return { notes, score };
  }
  function renderRotationAnalysis() {
    let cost = 0, total = 0;
    const lines = S.rotation.map((seq, l) => {
      const first = ITEMS[seq[0]];
      if (first && first.kind === 'crop') cost += first.cost * COLS; else if (seq[0] === 'cover') cost += 10 * COLS;
      const a = analyseLane(seq); total += a.score;
      return `<li><b>Row ${l + 1}</b> ${a.score >= 2 ? '<span class="good">GOOD</span>' : a.score < 0 ? '<span class="bad">RISK</span>' : ''} ${a.notes.length ? a.notes.join('; ') : 'nothing special'}</li>`;
    });
    const over = cost > S.setup.budget;
    // irrigation need per season from the real NASA days: crop water use beyond the rain, if every crop row were on drip
    const need = [0, 1, 2, 3].map(s => {
      let units = 0;
      for (let l = 0; l < LANES; l++) {
        const it = ITEMS[S.rotation[l][s]]; if (!it || it.kind !== 'crop') continue;
        let deficit = 0;
        for (let d = s * SEASON_DAYS; d < (s + 1) * SEASON_DAYS; d++) { const x = S.data.days[d % S.data.days.length]; deficit += it.kc * clamp(d % SEASON_DAYS / 28, 0.05, 1) * x.et0 * 0.9 - x.precip * 0.8; }
        units += Math.max(0, deficit - 50) / 20 / 0.9 * COLS;
      }
      return units;
    });
    const needHtml = need.map((u, i) => `<span class="${u > S.setup.water ? 'bad' : ''}">S${i + 1} ~${Math.round(u)}</span>`).join(' · ');
    $('#rotation-summary').innerHTML = `Season 1 planting cost: <b class="${over ? 'bad' : ''}">${fmt$(cost)}</b> of ${fmt$(S.setup.budget)} budget · rotation score <b>${total}</b><br>
      Irrigation each season would need, if every crop row had a drip line (water units, from NASA rain and evaporation): ${needHtml}. Your allocation is <b>${S.setup.water}</b> units per season. Where a season needs more, plant fewer rows, choose thriftier crops, or rest rows under a cover crop.`;
    $('#rotation-analysis').innerHTML = lines.join('');
    $('#btn-rotation-start').disabled = over;
    $('#btn-rotation-start').textContent = over ? 'Season 1 exceeds your budget' : 'Plant season 1';
  }
  function plantRotation(season) {
    const names = [];
    for (let l = 0; l < LANES; l++) {
      const seq = S.rotation[l], choice = seq[season], prev = season > 0 ? seq[season - 1] : null;
      const cur = ITEMS[choice], prevCrop = ITEMS[prev] && ITEMS[prev].kind === 'crop' ? ITEMS[prev] : null;
      const mods = S.laneMods[l] = { pestMult: 1, yieldMult: 1, tawMult: 1 };
      let startPest = 0.02;
      if (cur && cur.kind === 'crop' && prevCrop) {
        if (cur.family === prevCrop.family) { mods.pestMult = 1.8; mods.yieldMult = 0.85; startPest = 0.06; }
        else if (prevCrop.nitrogen) mods.yieldMult = 1.25;
      }
      if (prev === 'cover') { mods.tawMult = 1.15; mods.yieldMult *= 1.1; }
      if (prev === 'fallow') startPest = 0;
      // terminate last season's rotation cover crop
      for (let c = 0; c < COLS; c++) { const s = S.grid[l][c].surface; if (s && s.auto) removePlaced(s); S.grid[l][c].soil.pest = startPest; }
      let planted = 0;
      if (cur && cur.kind === 'crop') { for (let c = 0; c < COLS; c++) if (!S.grid[l][c].ground && S.budget >= cur.cost) { place(cur, l, c, cur.cost); planted++; } }
      else if (choice === 'cover') { for (let c = 0; c < COLS; c++) if (!S.grid[l][c].surface && S.budget >= 10) { const p = place(ITEMS.cover, l, c, 10); p.auto = true; planted++; } }
      names.push(`R${l + 1} ${cur && cur.kind === 'crop' ? cur.name : choice === 'cover' ? 'rye' : 'fallow'}${planted ? ' ×' + planted : ''}`);
    }
    log(`${Pixel.img('calendar')} Season ${season + 1} planted: ${names.join(' · ')}`);
    refreshPlots(); renderToolbar(); renderHUD();
  }

  // ============ game loop ============
  function tick(dt) {
    if (S.phase !== 'season' || !S.running || anyModalOpen()) return;
    S.dayTimer += dt * S.speed;
    if (S.dayTimer < DAY_SECONDS) { hudTimer += dt; if (hudTimer > 0.25) { hudTimer = 0; renderHUD(); } return; }
    S.dayTimer = 0;
    simulateDay(S.day);
    S.day++;
    if (S.level.rotation && S.day % SEASON_DAYS === 0 && S.day < totalDays()) {
      const season = S.day / SEASON_DAYS;
      harvestAll(`Season ${season} harvest`);
      const before = S.water; S.water = Math.min(S.waterCap, S.water + S.setup.water);
      log(`${Pixel.img('water')} This season's water allocation arrived: +${Math.round(S.water - before)} units in the pond.`);
      plantRotation(season);
    }
    if (S.day >= totalDays()) return endStage();
    beginDay(S.day);
    renderHUD(); renderInspector();
    if (S.autoPaused) { S.autoPaused = false; renderHUD(); }
  }
  let hudTimer = 0;

  function refreshPlots() {
    for (let l = 0; l < LANES; l++) for (let c = 0; c < COLS; c++) {
      const s = S.grid[l][c].soil;
      SceneMgr.setPlot(l, c, { moist: s.m / s.taw, sat: s.sat, frost: s.frost, pest: S.grid[l][c].ground && S.grid[l][c].ground.item.kind === 'crop' ? s.pest : 0, topsoil: s.topsoil });
    }
  }

  // ============ end of stage ============
  function endStage() {
    S.running = false; S.phase = 'done';
    harvestAll('Final harvest');
    const st = S.stats;
    const pct = st.potential > 0 ? st.earned / st.potential : 0;
    const stars = pct >= 0.9 ? 3 : pct >= 0.75 ? 2 : pct >= 0.6 ? 1 : 0;
    const pass = st.potential > 0 && stars > 0;
    if (pass) S.maxUnlocked = Math.max(S.maxUnlocked, Math.min(LEVELS.length - 1, S.levelIdx + 1));
    const usedPlots = S.grid.flat().filter(c => c.soil.used);
    const topsoil = avg((usedPlots.length ? usedPlots : S.grid.flat()).map(c => c.soil.topsoil));
    const lossRank = HZ.filter(h => st.loss[h] > 0.5).sort((a, b) => st.loss[b] - st.loss[a]);
    $('#result-title').innerHTML = !st.potential ? 'Nothing was planted'
      : pass ? `${S.level.rotation ? 'A full year farmed' : S.level.name + ' complete'} <span class="stars">${'★'.repeat(stars)}${'☆'.repeat(3 - stars)}</span>`
      : 'The harvest fell short';
    const lossRows = lossRank.map(h => `<tr><td>${HAZARDS[h].icon} Lost to ${HAZARDS[h].name.toLowerCase()}</td><td>${fmt$(st.loss[h])}</td></tr>`).join('');
    $('#result-body').innerHTML = `
      <p>You harvested <b>${Math.round(pct * 100)}%</b> of the yield your crops could have made. ${pass ? '' : 'You need at least 60% to pass.'}</p>
      <table class="stats">
        <tr><td>Harvest income</td><td>${fmt$(st.earned)}</td></tr>
        <tr><td>Spent on seed and practices</td><td>${fmt$(st.spent)}</td></tr>
        <tr><td>Profit</td><td>${fmt$(st.earned - st.spent)}</td></tr>
        <tr><td>Irrigation water used</td><td>${Math.round(st.waterUsed)} units</td></tr>
        <tr><td>Topsoil kept</td><td>${Math.round(topsoil)}%</td></tr>
        <tr><td>Crops lost</td><td>${st.cropsLost}</td></tr>
        ${lossRows}
      </table>`;
    const top = lossRank[0];
    let lesson = S.level.tip;
    if (top === 'heat' && st.heatThirsty > st.loss.heat * 0.5) lesson = 'Your biggest loss was heat stress, and most of it hit plots that were short of water. A watered crop transpires and runs 3 to 6 °C cooler than the air; a thirsty one runs hotter. Keep the root zone above half full.';
    else if (top) {
      const tools = Object.values(ITEMS).filter(it => it.helps && it.helps.includes(top) && S.unlocked.has(it.id)).map(it => it.name.toLowerCase());
      lesson = `Your biggest loss was ${HAZARDS[top].name.toLowerCase()}. ${HAZARDS[top].fix}${tools.length ? ` You have: ${tools.join(', ')}.` : ' The tools for it unlock in a later stage.'}`;
    }
    $('#result-lesson').textContent = lesson;
    $('#btn-next').classList.toggle('hidden', !pass || S.levelIdx >= LEVELS.length - 1);
    openModal('modal-result');
  }
  const avg = a => a.reduce((s, v) => s + v, 0) / (a.length || 1);

  // ============ HUD & panels ============
  function renderToolbar() {
    const bar = $('#toolbar'); bar.innerHTML = '';
    const order = Object.values(ITEMS).filter(i => S.unlocked.has(i.id)).sort((a, b) => ({ crop: 0, practice: 1, tool: 2 }[a.kind] - { crop: 0, practice: 1, tool: 2 }[b.kind]));
    order.forEach((it, i) => {
      const b = document.createElement('button');
      b.className = 'tool' + (S.selected === it.id ? ' selected' : '') + (it.cost > S.budget ? ' poor' : '');
      b.dataset.id = it.id;
      const layer = it.layer ? `<span class="layer ${it.layer}">${it.layer === 'surface' ? 'surface' : it.layer === 'sky' ? 'above' : it.layer}</span>` : '';
      const sub = it.kind === 'crop' ? `up to ${fmt$(it.value)}` : it.kind === 'practice' ? it.helps.map(h => HAZARDS[h].icon).join('') : '';
      b.innerHTML = `<span class="key">${i + 1}</span><span class="icon">${it.icon}</span><span class="name">${it.name}</span><span class="cost">${it.cost ? fmt$(it.cost) : 'free'}${it.scope === 'row' ? '/row' : ''}</span><span class="sub">${sub}</span>${layer}`;
      b.title = `${it.desc}${it.effect ? '\n' + it.effect : ''}`;
      b.addEventListener('click', () => selectItem(it.id));
      bar.appendChild(b);
    });
  }
  function selectItem(id) {
    S.selected = S.selected === id ? null : id;
    $$('#toolbar .tool').forEach(b => b.classList.toggle('selected', b.dataset.id === S.selected));
    $('#canvas-wrap').classList.toggle('placing', !!S.selected);
    if (S.selected) { const it = ITEMS[S.selected]; toast(`${it.icon} ${it.name}: ${it.kind === 'crop' ? it.desc : it.effect || it.desc}`); }
  }
  function renderHUD() {
    $('#hud-budget').textContent = fmt$(S.budget);
    $('#hud-water').textContent = `${Math.round(S.water)} / ${S.waterCap}`;
    $('#hud-water-bar').style.width = `${clamp(S.water / S.waterCap * 100, 0, 100)}%`;
    const crops = []; forEachPlaced(p => { if (p.item.kind === 'crop') crops.push(p.hp); });
    $('#hud-health').textContent = crops.length ? `${Math.round(avg(crops))}%` : '–';
    $('#hud-health-bar').style.width = `${crops.length ? clamp(avg(crops), 0, 100) : 0}%`;
    const usedPlots = S.grid.flat().filter(c => c.soil.used || c.ground || c.surface);
    const top = avg((usedPlots.length ? usedPlots : S.grid.flat()).map(c => c.soil.topsoil));
    $('#hud-topsoil').textContent = `${Math.round(top)}%`;
    $('#hud-topsoil-bar').style.width = `${clamp((top - 40) / 60 * 100, 0, 100)}%`;
    $('#hud-stage').textContent = S.level ? S.level.name : '';
    const w = S.today;
    if (S.phase === 'prep') $('#hud-week').textContent = 'Planting window · clock stopped';
    else if (w && S.level) $('#hud-week').textContent = `Week ${Math.floor(S.day / 7) + 1}/${S.level.weeks} · Day ${S.day % 7 + 1} · ${NASA.dateLabel(w.date, true)}`;
    $('#hud-week-bar').style.width = `${S.level ? clamp((S.day + S.dayTimer / DAY_SECONDS) / totalDays() * 100, 0, 100) : 0}%`;
    $$('#toolbar .tool').forEach(b => { const it = ITEMS[b.dataset.id]; b.classList.toggle('poor', it.cost > S.budget); });
    const btn = $('#btn-pause');
    btn.textContent = S.phase === 'prep' ? '▶ Start season' : S.running ? 'II Pause' : '▶ Resume';
    btn.classList.toggle('primary', S.phase === 'prep' || (S.phase === 'season' && !S.running));
    $('#prep-banner').classList.toggle('hidden', S.phase !== 'prep');
  }

  function renderWeather() {
    const w = S.today; if (!w) return;
    const src = S.data.source === 'live' ? '<span class="live">● LIVE NASA POWER</span>' : '<span class="offline">● OFFLINE CLIMATOLOGY</span>';
    $('#panel-source').innerHTML = `${src} · ${S.setup.lat.toFixed(2)}, ${S.setup.lon.toFixed(2)}`;
    $('#panel-week').innerHTML = `
      <div class="row"><span>${S.phase === 'prep' ? 'First day' : 'Today'}</span><b>${NASA.dateLabel(w.date, true)}</b></div>
      <div class="row"><span>Rain (PRECTOTCORR)</span><b>${w.precip.toFixed(1)} mm</b></div>
      <div class="row"><span>High / low (T2M_MAX/MIN)</span><b>${w.tmax.toFixed(1)} / ${w.tmin.toFixed(1)} °C</b></div>
      <div class="row"><span>Wind (WS2M)</span><b>${w.wind.toFixed(1)} m/s${w.wind > S.data.windErode ? ' <em class="hot">erosive</em>' : ''}</b></div>
      <div class="row"><span>Humidity (RH2M)</span><b>${w.rh.toFixed(0)} %</b></div>
      <div class="row"><span>Evaporation demand (ET0)</span><b>${w.et0.toFixed(1)} mm</b></div>
      <div class="row"><span>Root-zone wetness (GWETROOT)</span><b>${(w.soil * 100).toFixed(0)} %</b></div>`;
  }
  // 7-day forecast from the NASA record. Like a real forecast, it gets fuzzier further out.
  function showForecast(d) {
    const rows = [];
    for (let i = 1; i <= 7; i++) {
      const dd = d + (S.phase === 'prep' ? i - 1 : i); if (dd >= totalDays()) break;
      const x = dayData(dd), lead = S.phase === 'prep' ? i : i;
      const n = (Math.sin((dd + 1) * 12.9898 + S.levelIdx * 78.233) * 43758.5453) % 1;
      const fz = lead * 0.45 * n;
      const hi = x.tmax + fz, lo = x.tmin + fz * 0.8, rain = Math.max(0, x.precip * (1 + n * lead * 0.1));
      const hz = rain > 30 ? 'flood' : lo < 0.5 ? 'frost' : hi > 33 ? 'heat' : x.wind > S.data.windErode ? 'wind' : rain < 0.5 && x.et0 > 5 ? 'drought' : null;
      rows.push(`<div class="fc"><span class="fd">${NASA.dateLabel(x.date)}</span><span class="fi">${hz ? HAZARDS[hz].icon : ''}</span><span>${hi.toFixed(0)}° / ${lo.toFixed(0)}°</span><span class="fr">${rain < 0.5 ? 'dry' : rain.toFixed(0) + ' mm'}</span><span class="fw">${x.wind.toFixed(1)} m/s</span></div>`);
    }
    $('#panel-risk').innerHTML = rows.join('') || '<div class="muted">End of the stage window.</div>';
  }

  function bar(frac, cls = '', mark = null) {
    return `<span class="ibar ${cls}"><i style="width:${clamp(frac * 100, 0, 100)}%"></i>${mark !== null ? `<u style="left:${mark * 100}%"></u>` : ''}</span>`;
  }
  function renderInspector() {
    const el = $('#panel-inspect'); if (!el) return;
    const h = SceneMgr.hover;
    const t = S.inspect || (h ? { lane: h.lane, col: h.col } : null);
    if (!t || !S.grid.length) { el.innerHTML = '<div class="muted">Click a plot to inspect its soil, crop and protection.</div>'; return; }
    const cell = S.grid[t.lane][t.col], s = cell.soil, row = S.rows[t.lane];
    const crop = cell.ground && cell.ground.item.kind === 'crop' ? cell.ground : null;
    const pr = [];
    if (row.drip) pr.push('drip line'); if (row.drain) pr.push('drainage tile');
    if (cell.surface) pr.push(cell.surface.item.name.toLowerCase());
    if (cell.sky) pr.push('hoop cover');
    if (cell.ground && !crop) pr.push(cell.ground.item.name.toLowerCase());
    const sh = shelterAt(t.lane, t.col); if (sh < 1) pr.push(`wind -${Math.round((1 - sh) * 100)}% from windbreak`);
    if (neighbourMarigold(t.lane, t.col)) pr.push('marigold nearby');
    const lossTxt = crop && Object.keys(crop.loss).length ? Object.entries(crop.loss).filter(([, v]) => v > 0.5).sort((a, b) => b[1] - a[1]).map(([k, v]) => `${HAZARDS[k].icon}${Math.round(v)}`).join(' ') : '';
    el.innerHTML = `
      <div class="row"><span>Row ${t.lane + 1}, plot ${t.col + 1}</span><b>${crop ? crop.item.name : cell.ground ? cell.ground.item.name : 'Bare soil'}</b></div>
      ${crop ? `<div class="irow"><span>Crop health</span>${bar(crop.hp / 100, crop.hp < 50 ? 'bad' : 'ok')}<b>${Math.round(crop.hp)}%</b></div>` : ''}
      <div class="irow"><span>Soil water</span>${bar(s.m / s.taw, s.m < 0.5 * s.taw ? 'dry' : 'wet', 0.5)}<b>${Math.round(s.m)}/${Math.round(s.taw)} mm</b></div>
      ${s.sat > 0 ? `<div class="irow"><span>Saturated</span>${bar(s.sat / 60, 'flood')}<b>${Math.round(s.sat)} mm</b></div>` : ''}
      <div class="irow"><span>Topsoil</span>${bar((s.topsoil - 40) / 60, 'soil')}<b>${Math.round(s.topsoil)}%</b></div>
      ${crop ? `<div class="irow"><span>Pests</span>${bar(s.pest, 'pest', 0.35)}<b>${Math.round(s.pest * 100)}%</b></div>` : ''}
      <div class="small muted">${s.today.et ? `Yesterday: used ${s.today.et.toFixed(1)} mm${s.today.irr ? `, drip added ${s.today.irr.toFixed(0)} mm` : ''}${s.today.erosion > 0.05 ? `, lost ${s.today.erosion.toFixed(1)}% topsoil` : ''}.` : ''}</div>
      <div class="small">${pr.length ? 'Protection: ' + pr.join(', ') : '<span class="muted">No protection on this plot.</span>'}</div>
      ${lossTxt ? `<div class="small">Health lost to: ${lossTxt}</div>` : ''}`;
  }

  function log(msg, cls = '') { S.log.unshift({ msg, cls }); S.log = S.log.slice(0, 40); renderLog(); }
  function renderLog() { $('#panel-log').innerHTML = S.log.slice(0, 12).map(e => `<div class="${e.cls}">${e.msg}</div>`).join(''); }
  function toast(msg) { const el = $('#toast'); el.innerHTML = msg; el.classList.add('show'); clearTimeout(toast._t); toast._t = setTimeout(() => el.classList.remove('show'), 2600); }
  function hank(msg) {
    const el = $('#hank'); $('#hank-text').textContent = msg; el.classList.add('show');
    clearTimeout(hank._t); hank._t = setTimeout(() => el.classList.remove('show'), 9000);
  }
  function floatLabel(lane, xCol, text, cls) {
    const pos = SceneMgr.toScreen(lane, xCol, 0.8);
    const el = document.createElement('div'); el.className = 'float ' + cls; el.innerHTML = text;
    el.style.left = pos.left + 'px'; el.style.top = pos.top + 'px';
    $('#float-layer').appendChild(el); setTimeout(() => el.remove(), 1300);
  }

  // ============ manual ============
  function renderManual() {
    const tabs = {
      threats: `<p class="lead">Weather does not walk down the field like a monster. It hits every plot at once, and what decides the damage is the state of each plot: how much water is in the soil, whether it is covered, sheltered or drained.</p>` +
        Object.values(HAZARDS).map(h => `<div class="entry"><div class="card-icon">${h.icon}</div><div><h4>${h.name}</h4><p>${h.desc}</p><p><b>What helps:</b> ${h.fix}</p><p class="small"><b>In the simulation:</b> ${h.how}</p><p class="small data"><b>NASA data:</b> ${h.data}</p></div></div>`).join(''),
      items: Object.values(ITEMS).filter(i => i.kind !== 'tool').map(it => {
        const extra = it.kind === 'crop'
          ? `<p class="small">Seed ${fmt$(it.cost)} · harvest up to ${fmt$(it.value)} · heat limit ${it.heat} °C · frost limit ${it.frost} °C · water use ×${it.kc} · family: ${it.family}${it.nitrogen ? ' · fixes nitrogen' : ''}</p>`
          : `<p class="small">Cost ${fmt$(it.cost)}${it.scope === 'row' ? ' per row' : ''} · ${LAYERS[it.layer].name} · covers ${scopeText(it)}</p><p class="small"><b>Effect:</b> ${it.effect}</p>`;
        return `<div class="entry"><div class="card-icon">${it.icon}</div><div><h4>${it.name} <span class="badge">${LAYERS[it.layer].name}</span></h4><p>${it.desc}</p>${extra}</div></div>`;
      }).join(''),
      nasa: `<p class="lead">${Pixel.img('satellite')} This game turns NASA Earth-observation data into decisions a farmer can act on. One feed is live; the rest are the missions whose measurements the hazards are modelled on.</p>` + DATASETS.map(d => `<div class="entry"><div class="card-icon">${Pixel.img('satellite')}</div><div><h4>${d.name}</h4><p class="small used">${d.used}</p><p>${d.what}</p><p class="small"><a href="${d.url}" target="_blank" rel="noopener">${d.url}</a></p></div></div>`).join('') +
        `<p class="small">The API request the game makes (no key needed): <code>${S.data ? S.data.url : NASA.buildUrl(36.74, -119.78, 2025)}</code></p>`,
      rotation: `<h4>How the simulation works</h4><ul>${SIM_NOTES.map(n => `<li>${n}</li>`).join('')}</ul><h4>Crop rotation rules (final stage)</h4><ul>${ROTATION_RULES.map(r => `<li>${r}</li>`).join('')}</ul><h4>The layers</h4><ul>${Object.entries(LAYERS).map(([k, v]) => `<li><b>${v.name}:</b> ${v.desc}</li>`).join('')}</ul><h4>Controls</h4><ul class="small"><li>Click a card (or press 1–9), then click a plot to place it. Click the card again or press Esc to deselect.</li><li>With nothing selected, click a plot to inspect its soil water, topsoil, pests and protection.</li><li>Space starts the season and pauses. X shows the underground layer. The speed button cycles 1×, 2× and 4×.</li></ul>`,
    };
    $$('#manual-tabs button').forEach(b => b.onclick = () => { $$('#manual-tabs button').forEach(x => x.classList.toggle('active', x === b)); $('#manual-body').innerHTML = tabs[b.dataset.tab]; });
    $('#manual-body').innerHTML = tabs.threats;
    $$('#manual-tabs button').forEach(x => x.classList.toggle('active', x.dataset.tab === 'threats'));
  }

  // ============ wiring ============
  function init() {
    SceneMgr.init($('#c'), $('#canvas-wrap'), onCellClick);
    initSetup();
    $('#title-farmer').innerHTML = Pixel.img('farmer', 'big'); $('#intro-farmer').innerHTML = Pixel.img('farmer', 'big'); $('#hank-farmer').innerHTML = Pixel.img('farmer', 'big');
    $('#panel-sat').innerHTML = Pixel.img('satellite'); $('#hud-coin').innerHTML = Pixel.img('coin'); $('#hud-drop').innerHTML = Pixel.img('water');
    $('#hud-leaf').innerHTML = Pixel.img('sprout'); $('#hud-soil').innerHTML = Pixel.img('soil');
    $('#btn-start').addEventListener('click', () => showScreen('screen-setup'));
    $('#btn-manual-title').addEventListener('click', () => { renderManual(); openModal('modal-manual'); });
    $('#btn-manual').addEventListener('click', () => { renderManual(); openModal('modal-manual'); });
    $('#btn-intro-start').addEventListener('click', enterPrep);
    $('#btn-rotation-start').addEventListener('click', () => { closeModal('modal-rotation'); openLevelIntro(); });
    $('#btn-pause').addEventListener('click', () => {
      if (S.phase === 'prep') return startSeason();
      if (S.phase === 'season') { S.running = !S.running; renderHUD(); }
    });
    $('#btn-speed').addEventListener('click', () => { S.speed = S.speed === 1 ? 2 : S.speed === 2 ? 4 : 1; $('#btn-speed').textContent = S.speed + '×'; });
    $('#btn-xray').addEventListener('click', () => { S.xray = !S.xray; SceneMgr.setXray(S.xray); $('#btn-xray').classList.toggle('on', S.xray); });
    $('#btn-quit').addEventListener('click', () => { S.running = false; S.phase = 'idle'; showScreen('screen-setup'); renderStageList(); });
    $('#btn-next').addEventListener('click', () => { closeModal('modal-result'); startLevel(Math.min(LEVELS.length - 1, S.levelIdx + 1)); });
    $('#btn-retry').addEventListener('click', () => { closeModal('modal-result'); startLevel(S.levelIdx); });
    $('#btn-stages').addEventListener('click', () => { closeModal('modal-result'); S.phase = 'idle'; showScreen('screen-setup'); renderStageList(); });
    $('#hank').addEventListener('click', () => $('#hank').classList.remove('show'));
    $$('[data-close]').forEach(b => b.addEventListener('click', () => closeModal(b.dataset.close)));
    window.addEventListener('keydown', e => {
      if (!$('#screen-game').classList.contains('active') || anyModalOpen()) return;
      if (e.code === 'Space') { e.preventDefault(); $('#btn-pause').click(); }
      else if (e.key.toLowerCase() === 'x') $('#btn-xray').click();
      else if (e.key === 'Escape') { if (S.selected) selectItem(S.selected); S.inspect = null; renderInspector(); }
      else if (/^[1-9]$/.test(e.key)) { const b = $$('#toolbar .tool')[+e.key - 1]; if (b) b.click(); }
    });
    let last = performance.now(), inspT = 0;
    function frame(now) {
      const dt = Math.min(0.05, (now - last) / 1000); last = now;
      if ($('#screen-game').classList.contains('active')) {
        tick(dt); SceneMgr.update(dt, now / 1000);
        inspT += dt; if (inspT > 0.3 && !S.inspect) { inspT = 0; renderInspector(); }
      }
      requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
    if (new URLSearchParams(location.search).get('all') === '1') { $('#chk-unlock').checked = true; renderStageList(); }
  }

  // test hooks: run days instantly and place items from the console
  function _runDays(n) { if (S.phase === 'prep') startSeason(); for (let i = 0; i < n && S.phase === 'season'; i++) { S.running = true; S.dayTimer = DAY_SECONDS; tick(0); } }
  return { init, S, _tick: tick, _startLevel: startLevel, _cell: onCellClick, _select: selectItem, _runDays, _enterPrep: enterPrep };
})();

window.addEventListener('DOMContentLoaded', Game.init);
