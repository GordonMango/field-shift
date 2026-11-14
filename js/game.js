/* ============================================================
   Defend Your Farm — game state, loop, UI
   ============================================================ */
const Game = (() => {
  const { LANES, COLS, THREATS, ITEMS, LEVELS, REGIONS, ROTATION_CHOICES, DATASETS, ROTATION_RULES, LAYERS } = CONTENT;
  const WEEK_SECONDS = 9;
  const DIFF = { easy: 0.7, normal: 1.0, hard: 1.4 };
  const SLOTS = ['underground', 'ground', 'surface', 'sky'];
  const $ = s => document.querySelector(s);
  const $$ = s => Array.from(document.querySelectorAll(s));
  const fmt$ = v => '$' + Math.round(v).toLocaleString();
  let uid = 1;

  const S = {
    setup: { regionId: REGIONS[0].id, lat: REGIONS[0].lat, lon: REGIONS[0].lon, budget: 1500, water: 200, year: 2025, difficulty: 'normal', unlockAll: false },
    data: null, levelStarts: [],
    levelIdx: 0, maxUnlocked: 0, unlocked: new Set(),
    level: null, levelWeekStart: 0,
    budget: 0, water: 0, waterCap: 0, health: 5,
    week: -1, weekTimer: 0, time: 0, running: false, speed: 1, over: false, started: false,
    grid: [], threats: [], projectiles: [], spawnQueue: [],
    selected: null, xray: false,
    stats: null, rotation: null, laneMods: [], log: [], weekData: null,
    lowWaterWarned: 0,
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
      b.innerHTML = `<span class="stage-icon">${lv.focus ? THREATS[lv.focus].icon : Pixel.img('calendar')}</span><span><strong>${lv.name}</strong><br><small>${lv.weeks} weeks · ${lv.focus ? 'teaches ' + THREATS[lv.focus].name : 'all threats, crop rotation'}</small></span>${locked ? '<span class="lock">🔒</span>' : ''}`;
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
    // choose each stage's real-world window from the data
    S.levelStarts = LEVELS.map(lv => lv.focus ? NASA.bestWindow(data.weeks, lv.focus, lv.weeks).start : 0);
    startLevel(S.levelIdx);
  }

  // ============ level lifecycle ============
  function computeUnlocked(idx) {
    const set = new Set(['shovel']);
    LEVELS.forEach((lv, i) => { if (S.setup.unlockAll || i <= idx) lv.unlock.forEach(u => set.add(u)); });
    if (LEVELS[idx].rotation) Object.keys(ITEMS).forEach(k => set.add(k));
    return set;
  }

  function startLevel(idx) {
    S.levelIdx = idx; S.level = LEVELS[idx]; S.levelWeekStart = S.levelStarts[idx] || 0;
    S.unlocked = computeUnlocked(idx);
    S.budget = S.setup.budget; S.water = S.setup.water; S.waterCap = Math.max(S.setup.water, 100); S.health = 5;
    S.week = -1; S.weekTimer = 0; S.time = 0; S.running = false; S.over = false; S.started = false; S.speed = 1;
    S.grid = Array.from({ length: LANES }, () => Array.from({ length: COLS }, () => ({ underground: null, ground: null, surface: null, sky: null })));
    S.threats = []; S.projectiles = []; S.spawnQueue = []; S.selected = null; S.log = [];
    S.stats = { earned: 0, spent: 0, waterUsed: 0, cropsLost: 0, threatsDefeated: 0, breaches: 0 };
    S.laneMods = Array.from({ length: LANES }, () => ({ pestMult: 1, yieldMult: 1, hpMult: 1 }));
    S.rotation = null;
    SceneMgr.clearDynamic(); SceneMgr.setMood(null);
    showScreen('screen-game');
    renderToolbar(); renderHUD(); renderLog();
    $('#float-layer').innerHTML = '';
    if (S.level.rotation) openRotationPlanner(); else openLevelIntro();
  }

  function openLevelIntro() {
    const lv = S.level, w0 = S.data.weeks[S.levelWeekStart], w1 = S.data.weeks[(S.levelWeekStart + lv.weeks - 1) % 52];
    $('#intro-title').textContent = lv.name;
    $('#intro-text').textContent = lv.intro;
    $('#intro-tip').textContent = lv.tip;
    const win = `${NASA.dateLabel(w0.start)} → ${NASA.dateLabel(w1.end, true)}`;
    $('#intro-window').innerHTML = lv.focus
      ? `${Pixel.img('satellite')} NASA POWER scanned ${S.setup.year} at your location and found the worst ${lv.weeks}-week <b>${THREATS[lv.focus].name.toLowerCase()}</b> window: <b>${win}</b>.`
      : `All 52 weeks of ${S.setup.year}: <b>${win}</b>.`;
    const cards = $('#intro-unlocks'); cards.innerHTML = '';
    const newItems = S.level.rotation ? [] : lv.unlock.filter(u => u !== 'shovel');
    if (lv.focus) cards.appendChild(threatCard(THREATS[lv.focus]));
    newItems.forEach(id => cards.appendChild(itemCard(ITEMS[id])));
    openModal('modal-intro');
  }
  function threatCard(t) {
    const d = document.createElement('div'); d.className = 'card threat';
    d.innerHTML = `<div class="card-icon">${t.icon}</div><div><strong>New threat: ${t.name}</strong><p>${t.desc}</p><p class="small"><b>Counter:</b> ${t.counterText}</p><p class="small data">📡 ${t.data}</p></div>`;
    return d;
  }
  function itemCard(it) {
    const d = document.createElement('div'); d.className = 'card';
    const extra = it.kind === 'crop' ? `Harvest ${fmt$(it.value)} every ${it.growTime}s · drinks ${it.waterPerWeek} water/week · family: ${it.family}` : `Counters: ${it.counters.map(c => THREATS[c].icon + ' ' + THREATS[c].name).join(', ')} · ${it.range === 'lane' ? 'protects the whole lane' : 'protects one cell'}${it.wall ? ' · blocks' : ''}${it.waterPerShot ? ' · ' + it.waterPerShot + ' water/shot' : ''}`;
    d.innerHTML = `<div class="card-icon">${it.icon}</div><div><strong>${it.name}</strong> <span class="badge">${LAYERS[it.layer].name}</span> <span class="badge cost">${fmt$(it.cost)}</span><p>${it.desc}</p><p class="small">${extra}</p></div>`;
    return d;
  }

  function beginPlay() {
    closeModal('modal-intro'); closeModal('modal-rotation');
    S.started = true; S.running = true;
    advanceWeek();
    log(`${S.level.name} begins. Budget ${fmt$(S.budget)}, water ${Math.round(S.water)} units.`);
  }

  // ============ weekly step (NASA data → threats) ============
  function advanceWeek() {
    S.week++;
    if (S.week >= S.level.weeks) return winLevel();
    S.weekTimer = 0;
    const gi = (S.levelWeekStart + S.week) % 52;
    const wk = S.weekData = S.data.weeks[gi];
    // rain refills the allocation; crops drink from it
    if (S.week > 0) {
      const rain = wk.precip * 0.8;
      S.water = Math.min(S.waterCap, S.water + rain);
      let need = 0; forEachPlaced(p => { if (p.item.kind === 'crop') need += p.item.waterPerWeek; });
      if (need > 0) {
        if (S.water >= need) { S.water -= need; S.stats.waterUsed += need; }
        else {
          const ratio = S.water / need; S.stats.waterUsed += S.water; S.water = 0;
          forEachPlaced(p => { if (p.item.kind === 'crop') p.hp -= (1 - ratio) * 20; });
          log(`${Pixel.img('water')} Water shortage: crops only got ${Math.round(ratio * 100)}% of what they need. They are wilting.`, 'warn');
        }
      }
      if (rain > 1) log(`${Pixel.img('water')} ${wk.precip.toFixed(0)} mm of rain refilled ${Math.round(rain)} water units.`);
      cullDead();
    }
    if (S.level.rotation && S.week % 13 === 0) plantRotation(S.week / 13);
    scheduleSpawns(wk);
    SceneMgr.setMood(NASA.dominant(wk));
    renderWeekPanel(wk, gi);
    renderHUD();
  }

  function scheduleSpawns(wk) {
    const diff = DIFF[S.setup.difficulty] || 1;
    const hpScale = 1 + S.levelIdx * 0.15 + S.week * (S.level.rotation ? 0.012 : 0.04);
    const summary = [];
    for (const [type, mult] of Object.entries(S.level.threats)) {
      const idx = wk.index[type] || 0;
      let n = idx * 2.6 * mult * diff;
      if (type === S.level.focus) n = Math.max(n, 1.4 * diff);
      if (S.level.rotation && idx > 0.2) n = Math.max(n, 0.8);
      const count = Math.floor(n) + (Math.random() < (n % 1) ? 1 : 0);
      for (let i = 0; i < count; i++) {
        const at = S.time + (S.week === 0 ? 4 : 0.8) + Math.random() * (WEEK_SECONDS - 2);
        S.spawnQueue.push({ at, type, lane: pickLane(type), hpScale });
      }
      if (count) summary.push(`${THREATS[type].icon}×${count}`);
    }
    S.spawnQueue.sort((a, b) => a.at - b.at);
    log(`Week ${S.week + 1}/${S.level.weeks} · ${NASA.dateLabel(wk.start)}: ${summary.length ? 'incoming ' + summary.join(' ') : 'calm week'}.`);
  }
  function pickLane(type) {
    if (type !== 'pest') return Math.floor(Math.random() * LANES);
    const w = S.laneMods.map(m => m.pestMult); const tot = w.reduce((a, b) => a + b, 0);
    let r = Math.random() * tot; for (let i = 0; i < LANES; i++) { r -= w[i]; if (r <= 0) return i; } return LANES - 1;
  }

  // ============ placement ============
  function forEachPlaced(fn) { for (let l = 0; l < LANES; l++) for (let c = 0; c < COLS; c++) for (const s of SLOTS) { const p = S.grid[l][c][s]; if (p) fn(p, l, c, s); } }

  function onCellClick(lane, col) {
    if (!S.started || S.over || anyModalOpen()) return;
    const cell = S.grid[lane][col];
    if (!S.selected) { describeCell(lane, col); return; }
    if (S.selected === 'shovel') {
      const order = ['ground', 'surface', 'sky', 'underground'];
      for (const s of order) if (cell[s]) { removePlaced(cell[s]); log(`Cleared ${ITEMS[cell[s] ? cell[s].item.id : ''] ? '' : ''}cell L${lane + 1}C${col + 1}.`); SceneMgr.flashTile(lane, col, 0xffffff); renderHUD(); return; }
      return;
    }
    const item = ITEMS[S.selected];
    if (cell[item.layer]) { toast(`${LAYERS[item.layer].name} slot already has ${cell[item.layer].item.name}.`); SceneMgr.flashTile(lane, col, 0xff4444); return; }
    if (S.budget < item.cost) { toast(`Not enough budget for ${item.name} (${fmt$(item.cost)}).`); return; }
    place(item, lane, col, item.cost);
    renderHUD(); renderToolbar();
  }
  function place(item, lane, col, cost) {
    S.budget -= cost; S.stats.spent += cost;
    const hpMult = item.kind === 'crop' ? S.laneMods[lane].hpMult : 1;
    const p = { uid: uid++, item, lane, col, hp: (item.hp || 100) * hpMult, maxHp: (item.hp || 100) * hpMult, growth: 0, cd: 0.4, mesh: SceneMgr.addItem(item, lane, col) };
    S.grid[lane][col][item.layer] = p;
    SceneMgr.flashTile(lane, col, 0x88ff88);
    return p;
  }
  function removePlaced(p) {
    if (!p) return;
    const cell = S.grid[p.lane][p.col];
    if (cell[p.item.layer] === p) cell[p.item.layer] = null;
    SceneMgr.remove(p.mesh);
  }
  function describeCell(lane, col) {
    const cell = S.grid[lane][col];
    const parts = SLOTS.filter(s => cell[s]).map(s => `${cell[s].item.icon} ${cell[s].item.name}${cell[s].item.kind === 'crop' ? ' ' + Math.round(cell[s].growth * 100) + '%' : ''} (${Math.round(cell[s].hp)} hp)`);
    toast(parts.length ? `Lane ${lane + 1}, cell ${col + 1}: ${parts.join(' · ')}` : `Lane ${lane + 1}, cell ${col + 1} is empty. Pick a card below, then click a cell.`);
  }

  // ============ rotation planner (final stage) ============
  function openRotationPlanner() {
    const wrap = $('#rotation-grid'); wrap.innerHTML = '';
    const seasons = [0, 13, 26, 39].map(w => { const a = S.data.weeks[w], b = S.data.weeks[w + 12]; return `${NASA.dateLabel(a.start)} – ${NASA.dateLabel(b.end)}`; });
    const defaults = [['corn', 'soy', 'wheat', 'cover'], ['soy', 'corn', 'cover', 'wheat'], ['wheat', 'tomato', 'soy', 'cover'], ['tomato', 'cover', 'corn', 'soy'], ['cover', 'wheat', 'tomato', 'fallow']];
    const table = document.createElement('table');
    table.innerHTML = `<thead><tr><th>Lane</th>${seasons.map((s, i) => `<th>Season ${i + 1}<br><small>${s}</small></th>`).join('')}</tr></thead>`;
    const tb = document.createElement('tbody');
    S.rotation = defaults.map(r => r.slice());
    for (let l = 0; l < LANES; l++) {
      const tr = document.createElement('tr'); tr.innerHTML = `<td>Lane ${l + 1}</td>`;
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
      if (curCrop && prevCrop && curCrop.family === prevCrop.family) { notes.push(`S${s + 1}: ${curCrop.family} after ${prevCrop.family} → pests ×1.8`); score -= 2; }
      else if (curCrop && prevCrop && prevCrop.nitrogen) { notes.push(`S${s + 1}: ${curCrop.name} after soybean → yield ×1.25`); score += 2; }
      else if (curCrop && prev === 'cover') { notes.push(`S${s + 1}: after cover crop → +20% health, yield ×1.1`); score += 1; }
    }
    if (!seq.includes('cover') && !seq.includes('fallow')) { notes.push('No rest season: soil never recovers'); score -= 1; }
    return { notes, score };
  }
  function renderRotationAnalysis() {
    let cost = 0, waterNeed = 0, total = 0;
    const lines = S.rotation.map((seq, l) => {
      const first = ITEMS[seq[0]];
      if (first && first.kind === 'crop') { cost += first.cost * COLS; waterNeed += first.waterPerWeek * COLS; }
      else if (seq[0] === 'cover') cost += 10 * COLS;
      const a = analyseLane(seq); total += a.score;
      return `<li><b>Lane ${l + 1}</b> ${a.score >= 2 ? '✅' : a.score < 0 ? '⚠️' : '•'} ${a.notes.length ? a.notes.join('; ') : 'nothing special'}</li>`;
    });
    const over = cost > S.setup.budget;
    $('#rotation-summary').innerHTML = `Season 1 planting cost: <b class="${over ? 'bad' : ''}">${fmt$(cost)}</b> of ${fmt$(S.setup.budget)} budget · crops will drink <b>${waterNeed.toFixed(1)}</b> water/week from ${S.setup.water} units (rain refills it) · rotation score <b>${total}</b>`;
    $('#rotation-analysis').innerHTML = lines.join('');
    $('#btn-rotation-start').disabled = over;
    $('#btn-rotation-start').textContent = over ? 'Season 1 exceeds your budget' : 'Start the year';
  }
  function plantRotation(season) {
    const names = [];
    for (let l = 0; l < LANES; l++) {
      const seq = S.rotation[l], choice = seq[season], prev = season > 0 ? seq[season - 1] : null;
      const cur = ITEMS[choice], prevCrop = ITEMS[prev] && ITEMS[prev].kind === 'crop' ? ITEMS[prev] : null;
      const mods = S.laneMods[l] = { pestMult: 1, yieldMult: 1, hpMult: 1 };
      if (cur && cur.kind === 'crop' && prevCrop) {
        if (cur.family === prevCrop.family) { mods.pestMult = 1.8; mods.yieldMult = 0.85; }
        else if (prevCrop.nitrogen) mods.yieldMult = 1.25;
      }
      if (prev === 'cover') { mods.hpMult = 1.2; mods.yieldMult *= 1.1; }
      // end last season's crops
      for (let c = 0; c < COLS; c++) { const g = S.grid[l][c].ground; if (g && g.item.kind === 'crop') removePlaced(g); }
      let planted = 0;
      if (cur && cur.kind === 'crop') { for (let c = 0; c < COLS; c++) if (!S.grid[l][c].ground && S.budget >= cur.cost) { place(cur, l, c, cur.cost); planted++; } }
      else if (choice === 'cover') { for (let c = 0; c < COLS; c++) if (!S.grid[l][c].surface && S.budget >= 10) { place(ITEMS.cover, l, c, 10); planted++; } }
      names.push(`L${l + 1} ${cur ? cur.icon : choice === 'cover' ? ITEMS.cover.icon : 'rest'}${planted ? planted : ''}`);
    }
    log(`${Pixel.img('calendar')} Season ${season + 1} planted: ${names.join(' · ')}`);
    renderToolbar();
  }

  // ============ game loop ============
  function tick(dt) {
    if (!S.running || S.over || anyModalOpen()) return;
    dt *= S.speed;
    S.time += dt; S.weekTimer += dt;
    if (S.weekTimer >= WEEK_SECONDS) { advanceWeek(); if (S.over || !S.running) return; }
    while (S.spawnQueue.length && S.spawnQueue[0].at <= S.time) spawnThreat(S.spawnQueue.shift());
    updateCrops(dt); updateDefenses(dt); updateProjectiles(dt); updateThreats(dt); cullDead();
    hudTimer += dt; if (hudTimer > 0.25) { hudTimer = 0; renderHUD(); }
  }
  let hudTimer = 0;

  function spawnThreat(q) {
    const def = THREATS[q.type];
    const hp = def.hp * q.hpScale;
    const t = { uid: uid++, def, lane: q.lane, x: COLS + 0.9, hp, maxHp: hp, mesh: SceneMgr.addThreat(def, q.lane, COLS + 0.9) };
    S.threats.push(t);
  }

  function updateCrops(dt) {
    forEachPlaced((p, l) => {
      if (p.item.kind !== 'crop') return;
      p.growth += dt / p.item.growTime;
      const sc = 0.3 + 0.7 * Math.min(1, p.growth); p.mesh.scale.set(sc, sc, sc);
      if (p.growth >= 1) {
        const v = p.item.value * S.laneMods[l].yieldMult;
        S.budget += v; S.stats.earned += v; p.growth = 0;
        floatLabel(l, p.col, `${Pixel.img('coin')} +${fmt$(v)}`, 'gain');
      }
    });
  }

  function updateDefenses(dt) {
    forEachPlaced((p, lane, col) => {
      const it = p.item; if (it.kind !== 'defense' || it.range !== 'lane') return;
      p.cd -= dt; if (p.cd > 0) return;
      let target = null;
      for (const t of S.threats) if (t.lane === lane && it.counters.includes(t.def.id) && t.x > col - 0.3 && (!target || t.x < target.x)) target = t;
      if (!target) return;
      if (it.waterPerShot) {
        if (S.water < it.waterPerShot) { if (S.time - S.lowWaterWarned > 6) { S.lowWaterWarned = S.time; log(Pixel.img('water') + ' Water allocation empty: drip irrigation cannot fire. Wait for rain.', 'warn'); } return; }
        S.water -= it.waterPerShot; S.stats.waterUsed += it.waterPerShot;
      }
      p.cd = 1 / it.fireRate;
      const y = SceneMgr.THREAT_Y[target.def.layer];
      S.projectiles.push({ lane, x: col + 0.2, y, dmg: it.dmg, target, mesh: SceneMgr.addProjectile(it.projColor || 0xffffff, lane, col + 0.2, y) });
    });
  }

  function updateProjectiles(dt) {
    for (const pr of S.projectiles) {
      if (pr.target.hp <= 0 || pr.target.dead) { pr.dead = true; continue; }
      pr.x += 7 * dt; SceneMgr.moveProjectile(pr.mesh, pr.x);
      if (pr.x >= pr.target.x - 0.2) { pr.target.hp -= pr.dmg; pr.dead = true; }
      if (pr.x > COLS + 2) pr.dead = true;
    }
    S.projectiles = S.projectiles.filter(p => { if (p.dead) SceneMgr.remove(p.mesh); return !p.dead; });
  }

  function wallAt(lane, col, type) {
    if (col < 0 || col >= COLS) return null;
    const cell = S.grid[lane][col];
    for (const s of ['ground', 'surface']) { const p = cell[s]; if (p && p.item.wall && p.item.counters.includes(type)) return p; }
    return null;
  }

  function updateThreats(dt) {
    for (const t of S.threats) {
      if (t.hp <= 0) continue;
      let newX = t.x - t.def.speed * dt;
      const near = Math.round(t.x);
      let blocked = null;
      for (const c of [near, near - 1]) {
        const w = wallAt(t.lane, c, t.def.id);
        if (w && newX < c + 0.48 && t.x >= c + 0.47) { newX = c + 0.48; blocked = w; break; }
      }
      t.x = newX; SceneMgr.moveThreat(t.mesh, t.x, t.hp / t.maxHp);
      if (blocked) { blocked.hp -= t.def.dps * dt; t.hp -= (blocked.item.dps || 0) * dt; }
      const c = Math.round(t.x);
      if (c >= 0 && c < COLS) {
        const cell = S.grid[t.lane][c];
        let protectedCell = false;
        for (const s of SLOTS) { const p = cell[s]; if (p && p.item.counters && p.item.counters.includes(t.def.id)) { protectedCell = true; if (p.item.range === 'cell') t.hp -= p.item.dps * dt; } }
        const crop = cell.ground;
        if (!protectedCell && crop && crop.item.kind === 'crop') crop.hp -= t.def.dps * dt;
      }
      if (t.x < -1.1) {
        t.dead = true; S.health--; S.stats.breaches++;
        log(`${t.def.icon} ${t.def.name} reached the farmhouse in lane ${t.lane + 1}! Farm health ${S.health}/5.`, 'bad');
        $('#hud-top').classList.add('shake'); setTimeout(() => $('#hud-top').classList.remove('shake'), 400);
        if (S.health <= 0) loseLevel(t.def);
      }
    }
    S.threats = S.threats.filter(t => {
      if (t.hp <= 0 && !t.dead) { t.dead = true; S.stats.threatsDefeated++; floatLabel(t.lane, t.x, `${t.def.icon} stopped`, 'win'); }
      if (t.dead) SceneMgr.remove(t.mesh);
      return !t.dead;
    });
  }

  function cullDead() {
    forEachPlaced((p, l, c) => {
      if (p.hp <= 0) {
        removePlaced(p);
        if (p.item.kind === 'crop') { S.stats.cropsLost++; floatLabel(l, c, `${p.item.icon} lost`, 'loss'); }
        else log(`${p.item.icon} ${p.item.name} in lane ${l + 1} was destroyed.`, 'warn');
      }
    });
  }

  // ============ win / lose ============
  function winLevel() {
    S.running = false; S.over = true;
    S.maxUnlocked = Math.max(S.maxUnlocked, Math.min(LEVELS.length - 1, S.levelIdx + 1));
    const st = S.stats;
    const score = Math.round(st.earned + S.budget + S.health * 100 + st.threatsDefeated * 10 - st.cropsLost * 20);
    $('#result-title').textContent = S.level.rotation ? 'YOU SURVIVED THE WHOLE YEAR!' : `${S.level.name} — SURVIVED`;
    $('#result-body').innerHTML = statsHtml(score);
    $('#result-lesson').textContent = S.level.tip;
    $('#btn-next').classList.toggle('hidden', S.levelIdx >= LEVELS.length - 1);
    openModal('modal-result');
  }
  function loseLevel(def) {
    S.running = false; S.over = true;
    $('#result-title').innerHTML = `${def.icon} The farm fell to ${def.name.toLowerCase()}`;
    $('#result-body').innerHTML = `<p>${def.desc}</p><p><b>How to counter it:</b> ${def.counterText}</p>` + statsHtml(null);
    $('#result-lesson').textContent = `Check the forecast panel each week: when the ${def.name.toLowerCase()} bar is high, build its counter before the week starts.`;
    $('#btn-next').classList.add('hidden');
    openModal('modal-result');
  }
  function statsHtml(score) {
    const st = S.stats;
    return `<table class="stats"><tr><td>Harvest income</td><td>${fmt$(st.earned)}</td></tr><tr><td>Spent on seed & tools</td><td>${fmt$(st.spent)}</td></tr><tr><td>Budget left</td><td>${fmt$(S.budget)}</td></tr><tr><td>Water used</td><td>${Math.round(st.waterUsed)} units</td></tr><tr><td>Threats stopped</td><td>${st.threatsDefeated}</td></tr><tr><td>Crops lost</td><td>${st.cropsLost}</td></tr><tr><td>Farm health</td><td>${S.health}/5</td></tr>${score !== null ? `<tr class="total"><td>Score</td><td>${score.toLocaleString()}</td></tr>` : ''}</table>`;
  }

  // ============ HUD ============
  function renderToolbar() {
    const bar = $('#toolbar'); bar.innerHTML = '';
    const order = Object.values(ITEMS).filter(i => S.unlocked.has(i.id)).sort((a, b) => ({ crop: 0, defense: 1, tool: 2 }[a.kind] - { crop: 0, defense: 1, tool: 2 }[b.kind]));
    order.forEach((it, i) => {
      const b = document.createElement('button');
      b.className = 'tool' + (S.selected === it.id ? ' selected' : '') + (it.cost > S.budget ? ' poor' : '');
      b.dataset.id = it.id;
      const layer = it.layer ? `<span class="layer ${it.layer}">${it.layer === 'surface' ? 'ground' : it.layer}</span>` : '';
      const sub = it.kind === 'crop' ? `${fmt$(it.value)} / ${it.growTime}s` : it.kind === 'defense' ? it.counters.map(c => THREATS[c].icon).join('') : '';
      b.innerHTML = `<span class="key">${i + 1}</span><span class="icon">${it.icon}</span><span class="name">${it.name}</span><span class="cost">${it.cost ? fmt$(it.cost) : 'free'}</span><span class="sub">${sub}</span>${layer}`;
      b.title = it.desc;
      b.addEventListener('click', () => selectItem(it.id));
      bar.appendChild(b);
    });
  }
  function selectItem(id) {
    S.selected = S.selected === id ? null : id;
    $$('#toolbar .tool').forEach(b => b.classList.toggle('selected', b.dataset.id === S.selected));
    $('#canvas-wrap').classList.toggle('placing', !!S.selected);
  }
  function renderHUD() {
    $('#hud-budget').textContent = fmt$(S.budget);
    $('#hud-water').textContent = `${Math.round(S.water)} / ${S.waterCap}`;
    $('#hud-water-bar').style.width = `${Math.max(0, Math.min(100, S.water / S.waterCap * 100))}%`;
    $('#hud-health').innerHTML = Pixel.img('heart').repeat(Math.max(0, S.health)) + Pixel.img('heart_off').repeat(Math.max(0, 5 - S.health));
    $('#hud-stage').textContent = S.level ? S.level.name : '';
    const wk = S.weekData;
    $('#hud-week').textContent = wk ? `Week ${S.week + 1} / ${S.level.weeks} · ${NASA.dateLabel(wk.start, true)}` : '';
    $('#hud-week-bar').style.width = `${Math.min(100, S.weekTimer / WEEK_SECONDS * 100)}%`;
    $$('#toolbar .tool').forEach(b => { const it = ITEMS[b.dataset.id]; b.classList.toggle('poor', it.cost > S.budget); });
    $('#btn-pause').textContent = S.running ? 'II Pause' : '▶ Resume';
  }
  function renderWeekPanel(wk, gi) {
    const next = S.week + 1 < S.level.weeks ? S.data.weeks[(gi + 1) % 52] : null;
    const src = S.data.source === 'live' ? '<span class="live">● LIVE NASA POWER</span>' : '<span class="offline">● OFFLINE CLIMATOLOGY</span>';
    $('#panel-source').innerHTML = `${src} · ${S.setup.lat.toFixed(2)}, ${S.setup.lon.toFixed(2)}`;
    $('#panel-week').innerHTML = `
      <div class="row"><span>Week of</span><b>${NASA.dateLabel(wk.start, true)}</b></div>
      <div class="row"><span>Rain (PRECTOTCORR)</span><b>${wk.precip.toFixed(1)} mm</b></div>
      <div class="row"><span>Max temp (T2M_MAX)</span><b>${wk.tmax.toFixed(1)} °C</b></div>
      <div class="row"><span>Min temp (T2M_MIN)</span><b>${wk.tmin.toFixed(1)} °C</b></div>
      <div class="row"><span>Peak wind (WS2M)</span><b>${wk.windMax.toFixed(1)} m/s</b></div>
      <div class="row"><span>Humidity (RH2M)</span><b>${wk.rh.toFixed(0)} %</b></div>
      <div class="row"><span>Root-zone soil wetness (GWETROOT)</span><b>${(wk.soil * 100).toFixed(0)} %</b></div>`;
    const bars = type => {
      const v = wk.index[type] || 0, nv = next ? (next.index[type] || 0) : 0; const t = THREATS[type];
      const active = type in S.level.threats;
      return `<div class="risk ${active ? '' : 'inactive'}"><span class="ricon">${t.icon}</span><span class="rname">${t.name}</span><span class="rbar"><i style="width:${Math.round(v * 100)}%;background:#${t.color.toString(16).padStart(6, '0')}"></i></span><span class="rnext" title="next week">${next ? '→ ' + Math.round(nv * 100) + '%' : ''}</span></div>`;
    };
    $('#panel-risk').innerHTML = Object.keys(THREATS).map(bars).join('');
  }
  function log(msg, cls = '') { S.log.unshift({ msg, cls }); S.log = S.log.slice(0, 40); renderLog(); }
  function renderLog() { $('#panel-log').innerHTML = S.log.slice(0, 10).map(e => `<div class="${e.cls}">${e.msg}</div>`).join(''); }
  function toast(msg) { const el = $('#toast'); el.innerHTML = msg; el.classList.add('show'); clearTimeout(toast._t); toast._t = setTimeout(() => el.classList.remove('show'), 2200); }
  function floatLabel(lane, xCol, text, cls) {
    const pos = SceneMgr.toScreen(lane, xCol, 0.8);
    const el = document.createElement('div'); el.className = 'float ' + cls; el.innerHTML = text;
    el.style.left = pos.left + 'px'; el.style.top = pos.top + 'px';
    $('#float-layer').appendChild(el); setTimeout(() => el.remove(), 1300);
  }

  // ============ manual ============
  function renderManual() {
    const tabs = {
      threats: Object.values(THREATS).map(t => `<div class="entry"><div class="card-icon">${t.icon}</div><div><h4>${t.name}</h4><p>${t.desc}</p><p><b>Counter:</b> ${t.counterText}</p><p class="small">📡 <b>Data:</b> ${t.data}</p><p class="small"><b>Game rule:</b> ${t.rule}</p></div></div>`).join(''),
      items: Object.values(ITEMS).filter(i => i.kind !== 'tool').map(it => {
        const extra = it.kind === 'crop'
          ? `<p class="small">Cost ${fmt$(it.cost)} · harvest ${fmt$(it.value)} every ${it.growTime}s · ${it.waterPerWeek} water/week · family: ${it.family}${it.nitrogen ? ' · fixes nitrogen' : ''}</p>`
          : `<p class="small">Cost ${fmt$(it.cost)} · ${LAYERS[it.layer].name} layer · counters ${it.counters.map(c => THREATS[c].icon + ' ' + THREATS[c].name).join(', ')} · ${it.range === 'lane' ? 'whole lane' : 'one cell'}${it.wall ? ' · blocks threats' : ''}${it.waterPerShot ? ' · ' + it.waterPerShot + ' water per shot' : ''}</p>`;
        return `<div class="entry"><div class="card-icon">${it.icon}</div><div><h4>${it.name} <span class="badge">${LAYERS[it.layer].name}</span></h4><p>${it.desc}</p>${extra}</div></div>`;
      }).join(''),
      nasa: `<p class="lead">${Pixel.img('satellite')} This game turns NASA Earth-observation data into decisions a farmer can act on. One feed is live; the rest are the missions whose measurements the threats are modelled on.</p>` + DATASETS.map(d => `<div class="entry"><div class="card-icon">🛰️</div><div><h4>${d.name}</h4><p class="small used">${d.used}</p><p>${d.what}</p><p class="small"><a href="${d.url}" target="_blank" rel="noopener">${d.url}</a></p></div></div>`).join('') +
        `<h4>How a week of data becomes a wave of threats</h4><ul class="small">${Object.values(THREATS).map(t => `<li>${t.icon} <b>${t.name}:</b> ${t.rule}</li>`).join('')}</ul><p class="small">The API request the game makes (no key needed): <code>${S.data ? S.data.url : NASA.buildUrl(36.74, -119.78, 2025)}</code></p>`,
      rotation: `<p class="lead">Rotation is the cheapest defense there is. The final stage lets you plan four seasons per lane; these are the rules the simulation applies.</p><ul>${ROTATION_RULES.map(r => `<li>${r}</li>`).join('')}</ul><h4>The three layers</h4><ul>${Object.entries(LAYERS).map(([k, v]) => `<li><b>${v.name}:</b> ${v.desc}</li>`).join('')}</ul><h4>Controls</h4><ul class="small"><li>Click a card (or press 1–9), then click a cell to build. Click the card again to deselect.</li><li>Click an empty-handed cell to inspect it. 🧹 Remove clears a cell.</li><li>Space pauses. X toggles underground X-ray view. 2× speed is in the top bar.</li></ul>`,
    };
    $$('#manual-tabs button').forEach(b => b.onclick = () => { $$('#manual-tabs button').forEach(x => x.classList.toggle('active', x === b)); $('#manual-body').innerHTML = tabs[b.dataset.tab]; });
    $('#manual-body').innerHTML = tabs.threats;
    $$('#manual-tabs button').forEach(x => x.classList.toggle('active', x.dataset.tab === 'threats'));
  }

  // ============ wiring ============
  function init() {
    SceneMgr.init($('#c'), $('#canvas-wrap'), onCellClick);
    initSetup();
    $('#title-farmer').innerHTML = Pixel.img('farmer', 'big'); $('#intro-farmer').innerHTML = Pixel.img('farmer', 'big');
    $('#panel-sat').innerHTML = Pixel.img('satellite'); $('#hud-coin').innerHTML = Pixel.img('coin'); $('#hud-drop').innerHTML = Pixel.img('water');
    $('#btn-start').addEventListener('click', () => showScreen('screen-setup'));
    $('#btn-manual-title').addEventListener('click', () => { renderManual(); openModal('modal-manual'); });
    $('#btn-manual').addEventListener('click', () => { renderManual(); openModal('modal-manual'); });
    $('#btn-intro-start').addEventListener('click', beginPlay);
    $('#btn-rotation-start').addEventListener('click', () => { closeModal('modal-rotation'); openLevelIntro(); });
    $('#btn-pause').addEventListener('click', () => { if (!S.over && S.started) { S.running = !S.running; renderHUD(); } });
    $('#btn-speed').addEventListener('click', () => { S.speed = S.speed === 1 ? 2 : 1; $('#btn-speed').textContent = S.speed + '×'; });
    $('#btn-xray').addEventListener('click', () => { S.xray = !S.xray; SceneMgr.setXray(S.xray); $('#btn-xray').classList.toggle('on', S.xray); });
    $('#btn-quit').addEventListener('click', () => { S.running = false; S.started = false; showScreen('screen-setup'); renderStageList(); });
    $('#btn-next').addEventListener('click', () => { closeModal('modal-result'); startLevel(Math.min(LEVELS.length - 1, S.levelIdx + 1)); });
    $('#btn-retry').addEventListener('click', () => { closeModal('modal-result'); startLevel(S.levelIdx); });
    $('#btn-stages').addEventListener('click', () => { closeModal('modal-result'); S.started = false; showScreen('screen-setup'); renderStageList(); });
    $$('[data-close]').forEach(b => b.addEventListener('click', () => closeModal(b.dataset.close)));
    window.addEventListener('keydown', e => {
      if (!$('#screen-game').classList.contains('active') || anyModalOpen()) return;
      if (e.code === 'Space') { e.preventDefault(); $('#btn-pause').click(); }
      else if (e.key.toLowerCase() === 'x') $('#btn-xray').click();
      else if (e.key === 'Escape') selectItem(S.selected);
      else if (/^[1-9]$/.test(e.key)) { const b = $$('#toolbar .tool')[+e.key - 1]; if (b) b.click(); }
    });
    let last = performance.now();
    function frame(now) {
      const dt = Math.min(0.05, (now - last) / 1000); last = now;
      if ($('#screen-game').classList.contains('active')) { tick(dt); SceneMgr.update(dt, now / 1000); }
      requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
    if (new URLSearchParams(location.search).get('all') === '1') { $('#chk-unlock').checked = true; renderStageList(); }
  }

  return { init, S, _tick: tick, _startLevel: startLevel, _cell: onCellClick, _select: selectItem };
})();

window.addEventListener('DOMContentLoaded', Game.init);
