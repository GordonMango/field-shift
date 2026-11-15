/* ============================================================
   Three.js scene: the 3D farm with underground / ground / sky
   layers. Soil colour follows each plot's water, puddles show
   saturation, pests gather on infested plots, crops brown as they
   lose health, and rain, dust and frost come from the NASA weather.
   ============================================================ */
const SceneMgr = (() => {
  const { LANES, COLS } = CONTENT;
  const CELL = 1.15;
  const LAYER_Y = { underground: -0.42, surface: 0.09, ground: 0.05, sky: 0.98 };
  const ROW_Y = { drip: -0.22, drain: -0.68 };
  const PIXEL_SCALE = 0.3;
  const SKY = { base: 0x9fd0ee, drought: 0xf2c58a, heat: 0xffad7d, flood: 0x6f84a3, frost: 0xdfeefb, wind: 0xc9c4b4, pest: 0xb8d38e };
  const SOIL_DRY = new THREE.Color(0xcfa874), SOIL_WET = new THREE.Color(0x4a2d15), SUBSOIL = new THREE.Color(0xc0763e), FROST = new THREE.Color(0xf2f8ff);
  const BROWN = new THREE.Color(0x8a6a3a);

  let renderer, scene, camera, raycaster, pointer, canvas, wrap, hemi, sun;
  const tiles = [], puddles = [], pests = [];
  const dynamic = new Set();
  const covers = new Set();
  let hover = null, hoverFrame = null, onCellClick = null, xray = false;
  const targetSky = new THREE.Color(SKY.base);
  const weather = { rain: 0, wind: 0, frost: false, heat: 0 };
  let rain, dust, snow;

  const colX = col => (col - (COLS - 1) / 2) * CELL;
  const laneZ = lane => (lane - (LANES - 1) / 2) * CELL;

  function mat(color, opts = {}) {
    return new THREE.MeshStandardMaterial(Object.assign({ color, roughness: 0.95, metalness: 0, flatShading: true }, opts));
  }
  function mesh(geo, material, x = 0, y = 0, z = 0) {
    const m = new THREE.Mesh(geo, material); m.position.set(x, y, z); m.castShadow = true; m.receiveShadow = true; return m;
  }

  function init(canvasEl, wrapEl, clickCb) {
    canvas = canvasEl; wrap = wrapEl; onCellClick = clickCb;
    // Pixel-art look: render at a fraction of the CSS size and let the browser upscale with nearest-neighbour.
    renderer = new THREE.WebGLRenderer({ canvas, antialias: false });
    renderer.setPixelRatio(PIXEL_SCALE);
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFShadowMap;

    scene = new THREE.Scene();
    scene.background = new THREE.Color(SKY.base);
    scene.fog = new THREE.Fog(SKY.base, 20, 45);

    camera = new THREE.PerspectiveCamera(38, 1, 0.1, 100);
    camera.position.set(0.3, 9.6, 10.4);
    camera.lookAt(0, 0, 0.2);

    hemi = new THREE.HemisphereLight(0xe8f4ff, 0x5f8a36, 0.9); scene.add(hemi);
    sun = new THREE.DirectionalLight(0xfff1d4, 1.15); sun.position.set(-6, 12, 7); sun.castShadow = true;
    sun.shadow.mapSize.set(1024, 1024);
    sun.shadow.bias = -0.0015; sun.shadow.normalBias = 0.03;
    Object.assign(sun.shadow.camera, { left: -11, right: 11, top: 11, bottom: -11, near: 1, far: 40 });
    scene.add(sun);

    const far = new THREE.Mesh(new THREE.PlaneGeometry(90, 90), mat(0x72ab4d)); far.rotation.x = -Math.PI / 2; far.position.y = -0.96; far.receiveShadow = true; scene.add(far);
    // translucent soil block: the underground layer lives inside it
    const slab = new THREE.Mesh(new THREE.BoxGeometry(COLS * CELL + 0.5, 0.9, LANES * CELL + 0.5), mat(0x5a3a1d, { transparent: true, opacity: 0.5 }));
    slab.position.y = -0.5; slab.receiveShadow = true; scene.add(slab);

    const tg = new THREE.BoxGeometry(CELL - 0.08, 0.1, CELL - 0.08);
    const pg = new THREE.BoxGeometry(CELL - 0.14, 0.02, CELL - 0.14);
    const bug = new THREE.SphereGeometry(0.05, 6, 5);
    for (let lane = 0; lane < LANES; lane++) for (let col = 0; col < COLS; col++) {
      const t = new THREE.Mesh(tg, mat(0x7c5230, { transparent: true, opacity: 0.85 }));
      t.position.set(colX(col), 0, laneZ(lane)); t.receiveShadow = true; t.renderOrder = 2;
      t.userData = { lane, col, odd: (lane + col) % 2 === 1, state: { moist: 0.5, topsoil: 100, frost: false } };
      scene.add(t); tiles.push(t);
      // puddle shown when the plot is saturated
      const pd = new THREE.Mesh(pg, mat(0x3f7fe0, { transparent: true, opacity: 0, roughness: 0.2 }));
      pd.position.set(colX(col), 0.065, laneZ(lane)); pd.visible = false; pd.renderOrder = 3; scene.add(pd); puddles.push(pd);
      // pest specks
      const pgp = new THREE.Group(); pgp.position.set(colX(col), 0.12, laneZ(lane));
      for (let i = 0; i < 8; i++) { const b = mesh(bug, mat(0x2a1a0e)); b.userData.o = { r: 0.15 + Math.random() * 0.3, a: Math.random() * 6.28, s: 1.5 + Math.random() * 2.5, y: Math.random() * 0.5 }; b.visible = false; pgp.add(b); }
      scene.add(pgp); pests.push(pgp);
    }
    // hover frame
    hoverFrame = new THREE.Group();
    const fm = mat(0xffd93d, { emissive: 0xffd93d, emissiveIntensity: 0.8 });
    const L = CELL - 0.06, w = 0.05;
    hoverFrame.add(mesh(new THREE.BoxGeometry(L, 0.04, w), fm, 0, 0, -L / 2), mesh(new THREE.BoxGeometry(L, 0.04, w), fm, 0, 0, L / 2),
      mesh(new THREE.BoxGeometry(w, 0.04, L), fm, -L / 2, 0, 0), mesh(new THREE.BoxGeometry(w, 0.04, L), fm, L / 2, 0, 0));
    hoverFrame.visible = false; hoverFrame.position.y = 0.08; scene.add(hoverFrame);

    buildBarn(); buildRoad(); buildDecor(); buildWeather();

    raycaster = new THREE.Raycaster(); pointer = new THREE.Vector2(-2, -2);
    canvas.addEventListener('pointermove', e => setPointer(e));
    canvas.addEventListener('pointerleave', () => pointer.set(-2, -2));
    canvas.addEventListener('click', e => { setPointer(e); const c = pick(); if (c && onCellClick) onCellClick(c.lane, c.col); });
    window.addEventListener('resize', resize);
    resize();
  }

  function setPointer(e) {
    const r = canvas.getBoundingClientRect();
    pointer.set(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1);
  }
  function pick() {
    raycaster.setFromCamera(pointer, camera);
    const hit = raycaster.intersectObjects(tiles, false)[0];
    return hit ? hit.object.userData : null;
  }
  function resize() {
    const w = wrap.clientWidth || 800, h = wrap.clientHeight || 500;
    camera.aspect = w / h; camera.updateProjectionMatrix();
    renderer.setSize(w, h, false);
  }

  function buildBarn() {
    const x = colX(0) - 1.9;
    const barn = new THREE.Group();
    barn.add(mesh(new THREE.BoxGeometry(1.5, 1.1, 2.4), mat(0xb8382e), 0, 0.55, 0));
    const roof = mesh(new THREE.ConeGeometry(1.55, 0.8, 4), mat(0x5c2d24), 0, 1.5, 0); roof.rotation.y = Math.PI / 4; roof.scale.set(1, 1, 1.6); barn.add(roof);
    barn.add(mesh(new THREE.BoxGeometry(0.5, 0.7, 0.05), mat(0x3b2418), 0.76, 0.35, 0));
    barn.add(mesh(new THREE.CylinderGeometry(0.42, 0.42, 1.9, 16), mat(0xd9d9d9), 0, 0.95, 1.95));
    barn.add(mesh(new THREE.SphereGeometry(0.42, 16, 12, 0, Math.PI * 2, 0, Math.PI / 2), mat(0x9a9a9a), 0, 1.9, 1.95));
    // farm pond (the irrigation reservoir)
    const pond = mesh(new THREE.CylinderGeometry(0.75, 0.75, 0.06, 18), mat(0x3f86d8, { roughness: 0.2 }), 0.1, -0.02, -2.4); barn.add(pond);
    barn.position.set(x, 0, -0.2);
    scene.add(barn);
    const fx = colX(0) - 0.85;
    for (let lane = 0; lane < LANES; lane++) scene.add(mesh(new THREE.BoxGeometry(0.08, 0.6, 0.08), mat(0xd8c3a0), fx, 0.3, laneZ(lane) - 0.5));
    scene.add(mesh(new THREE.BoxGeometry(0.06, 0.06, LANES * CELL + 0.2), mat(0xd8c3a0), fx, 0.45, 0));
  }
  function buildRoad() {
    const road = new THREE.Mesh(new THREE.BoxGeometry(1.8, 0.06, LANES * CELL + 1.2), mat(0x474747)); road.position.set(colX(COLS) + 0.6, -0.03, 0); road.receiveShadow = true; scene.add(road);
    for (let i = 0; i < 6; i++) scene.add(mesh(new THREE.BoxGeometry(0.1, 0.01, 0.5), mat(0xf1e37a), colX(COLS) + 0.6, 0.01, -3 + i * 1.2));
    // wind sock on the windward edge
    scene.add(mesh(new THREE.CylinderGeometry(0.03, 0.03, 1.4, 6), mat(0x9a9a9a), colX(COLS) + 1.5, 0.7, laneZ(0) - 0.9));
    const sock = mesh(new THREE.ConeGeometry(0.12, 0.5, 8, 1, true), mat(0xff7a1a, { side: THREE.DoubleSide }), colX(COLS) + 1.25, 1.32, laneZ(0) - 0.9);
    sock.rotation.z = Math.PI / 2; sock.name = 'sock'; scene.add(sock);
  }
  function buildDecor() {
    [[-7.5, -4.5], [-8, 2.5], [7.5, 4.3], [8.5, -3.8], [0, -6.5], [-4, -6], [4.5, -6.3]].forEach(([x, z]) => scene.add(tree(x, z, 1 + Math.random() * 0.4)));
  }
  function tree(x, z, s = 1) {
    const g = new THREE.Group();
    g.add(mesh(new THREE.CylinderGeometry(0.12, 0.16, 0.8, 7), mat(0x6b4423), 0, 0.4, 0));
    g.add(mesh(new THREE.ConeGeometry(0.55, 1.1, 8), mat(0x2f7a3a), 0, 1.2, 0));
    g.add(mesh(new THREE.ConeGeometry(0.4, 0.9, 8), mat(0x3b8f45), 0, 1.75, 0));
    g.position.set(x, -0.95, z); g.scale.setScalar(s);
    return g;
  }

  // ---------- weather particles ----------
  function buildWeather() {
    const W = COLS * CELL + 4, D = LANES * CELL + 3;
    // rain: short vertical streaks
    const RN = 900, rp = new Float32Array(RN * 6);
    for (let i = 0; i < RN; i++) { const x = (Math.random() - 0.5) * W, y = Math.random() * 7, z = (Math.random() - 0.5) * D; rp.set([x, y, z, x - 0.04, y - 0.28, z], i * 6); }
    const rg = new THREE.BufferGeometry(); rg.setAttribute('position', new THREE.BufferAttribute(rp, 3)); rg.setDrawRange(0, 0);
    rain = new THREE.LineSegments(rg, new THREE.LineBasicMaterial({ color: 0xbfdcff, transparent: true, opacity: 0.8 }));
    rain.userData = { n: RN, W, D }; scene.add(rain);
    // dust: points blowing from the windward (right) edge
    const DN = 600, dp = new Float32Array(DN * 3);
    for (let i = 0; i < DN; i++) dp.set([(Math.random() - 0.5) * W, Math.random() * 1.6, (Math.random() - 0.5) * D], i * 3);
    const dg = new THREE.BufferGeometry(); dg.setAttribute('position', new THREE.BufferAttribute(dp, 3)); dg.setDrawRange(0, 0);
    dust = new THREE.Points(dg, new THREE.PointsMaterial({ color: 0xc9a46b, size: 0.09, transparent: true, opacity: 0.85 }));
    dust.userData = { n: DN, W, D }; scene.add(dust);
    // frost sparkle
    const SN = 300, sp = new Float32Array(SN * 3);
    for (let i = 0; i < SN; i++) sp.set([(Math.random() - 0.5) * W, Math.random() * 6, (Math.random() - 0.5) * D], i * 3);
    const sg = new THREE.BufferGeometry(); sg.setAttribute('position', new THREE.BufferAttribute(sp, 3)); sg.setDrawRange(0, 0);
    snow = new THREE.Points(sg, new THREE.PointsMaterial({ color: 0xffffff, size: 0.08, transparent: true, opacity: 0.9 }));
    snow.userData = { n: SN, W, D }; scene.add(snow);
  }
  function setWeather(w) { Object.assign(weather, w); }

  // ---------- item meshes ----------
  function buildItem(item) {
    const g = new THREE.Group();
    const rnd = (a, b) => a + Math.random() * (b - a);
    switch (item.id) {
      case 'corn':
        [[-0.25, -0.2], [0.2, 0.15], [-0.1, 0.3]].forEach(([x, z]) => {
          g.add(mesh(new THREE.CylinderGeometry(0.035, 0.05, 0.95, 6), mat(0x4e9a2e), x, 0.47, z));
          const leaf = mesh(new THREE.BoxGeometry(0.4, 0.02, 0.1), mat(0x5fb23a), x + 0.15, 0.55, z); leaf.rotation.z = 0.5; g.add(leaf);
          const leaf2 = mesh(new THREE.BoxGeometry(0.4, 0.02, 0.1), mat(0x5fb23a), x - 0.15, 0.35, z); leaf2.rotation.z = -0.5; g.add(leaf2);
          g.add(mesh(new THREE.CylinderGeometry(0.06, 0.07, 0.28, 8), mat(0xf2cf3a), x + 0.09, 0.6, z));
        });
        break;
      case 'wheat':
        for (let i = 0; i < 9; i++) {
          const x = rnd(-0.4, 0.4), z = rnd(-0.4, 0.4);
          g.add(mesh(new THREE.CylinderGeometry(0.015, 0.02, 0.7, 5), mat(0x9cbf4c), x, 0.35, z));
          g.add(mesh(new THREE.CylinderGeometry(0.045, 0.03, 0.2, 6), mat(0xe8c866), x, 0.78, z));
        }
        break;
      case 'soy':
        [[-0.25, -0.2], [0.22, 0.1], [-0.05, 0.3]].forEach(([x, z]) => {
          g.add(mesh(new THREE.SphereGeometry(0.2, 10, 8), mat(0x4d9b3b), x, 0.22, z));
          g.add(mesh(new THREE.SphereGeometry(0.05, 6, 5), mat(0x9fd45c), x + 0.12, 0.3, z + 0.1));
        });
        break;
      case 'tomato':
        [[-0.25, 0], [0.25, 0]].forEach(([x, z]) => {
          g.add(mesh(new THREE.CylinderGeometry(0.02, 0.02, 0.8, 5), mat(0x9b9b9b), x, 0.4, z));
          g.add(mesh(new THREE.SphereGeometry(0.24, 10, 8), mat(0x3f8f3a), x, 0.45, z));
          g.add(mesh(new THREE.SphereGeometry(0.07, 8, 6), mat(0xe53935), x + 0.15, 0.4, z + 0.15));
          g.add(mesh(new THREE.SphereGeometry(0.07, 8, 6), mat(0xe53935), x - 0.12, 0.55, z - 0.1));
        });
        break;
      case 'drip': {
        // one lateral line under the whole row, with an emitter under every plot
        const pipe = mesh(new THREE.CylinderGeometry(0.05, 0.05, COLS * CELL, 10), mat(0x1f6fb8)); pipe.rotation.z = Math.PI / 2; g.add(pipe);
        g.userData.emitters = [];
        for (let c = 0; c < COLS; c++) {
          const e = mesh(new THREE.SphereGeometry(0.075, 8, 6), mat(0x4fc3f7, { emissive: 0x4fc3f7, emissiveIntensity: 0.15 }), colX(c), 0.06, 0);
          g.add(e); g.userData.emitters.push(e);
        }
        break;
      }
      case 'drain': {
        const pipe = mesh(new THREE.CylinderGeometry(0.1, 0.1, COLS * CELL + 0.4, 10), mat(0x2b3550)); pipe.rotation.z = Math.PI / 2; g.add(pipe);
        for (let i = 0; i < COLS * 2; i++) g.add(mesh(new THREE.BoxGeometry(0.05, 0.05, 0.22), mat(0x0b1a33), colX(0) - 0.3 + i * CELL / 2, 0.08, 0));
        // outlet on the farmhouse side
        g.add(mesh(new THREE.CylinderGeometry(0.12, 0.12, 0.2, 10), mat(0x445577), -(COLS * CELL) / 2 - 0.25, 0, 0));
        g.userData.flow = [];
        for (let i = 0; i < 6; i++) { const d = mesh(new THREE.SphereGeometry(0.05, 6, 5), mat(0x6fb0ff, { emissive: 0x3a7bff, emissiveIntensity: 0.6 }), 0, 0, 0); d.visible = false; g.add(d); g.userData.flow.push(d); }
        break;
      }
      case 'mulch':
        g.add(mesh(new THREE.BoxGeometry(0.96, 0.07, 0.96), mat(0xc9a469), 0, 0, 0));
        for (let i = 0; i < 10; i++) { const s = mesh(new THREE.BoxGeometry(0.25, 0.02, 0.04), mat(0xe2c27e), rnd(-0.38, 0.38), 0.045, rnd(-0.4, 0.4)); s.rotation.y = rnd(0, Math.PI); g.add(s); }
        break;
      case 'cover':
        g.add(mesh(new THREE.BoxGeometry(0.96, 0.05, 0.96), mat(0x4e7f2d), 0, 0, 0));
        for (let i = 0; i < 16; i++) { const b = mesh(new THREE.BoxGeometry(0.04, 0.28, 0.04), mat(0x7cc24a), rnd(-0.42, 0.42), 0.15, rnd(-0.42, 0.42)); b.rotation.z = rnd(-0.3, 0.3); g.add(b); }
        break;
      case 'windbreak':
        [[-0.3, -0.3], [0.25, 0.3], [-0.1, 0.05]].forEach(([x, z]) => {
          g.add(mesh(new THREE.CylinderGeometry(0.07, 0.09, 0.6, 7), mat(0x6b4423), x, 0.3, z));
          g.add(mesh(new THREE.ConeGeometry(0.33, 0.8, 8), mat(0x2f7a3a), x, 0.95, z));
          g.add(mesh(new THREE.ConeGeometry(0.24, 0.6, 8), mat(0x3b8f45), x, 1.35, z));
        });
        break;
      case 'rowcover': {
        // hoops plus fabric; colour shows shade cloth (dark) or frost cloth (white)
        const h = new THREE.Group(); h.position.y = -0.93; h.scale.set(1, 2.1, 1); g.add(h);
        for (const x of [-0.42, 0, 0.42]) {
          const hoop = mesh(new THREE.TorusGeometry(0.46, 0.012, 5, 14, Math.PI), mat(0xbdbdbd), x, 0, 0); hoop.rotation.y = Math.PI / 2; h.add(hoop);
        }
        const cloth = mesh(new THREE.CylinderGeometry(0.45, 0.45, 0.94, 14, 1, true, 0, Math.PI), mat(0xffffff, { transparent: true, opacity: 0.5, side: THREE.DoubleSide, roughness: 0.5 }), 0, 0, 0);
        cloth.rotation.z = Math.PI / 2; h.add(cloth);
        g.userData.cloth = cloth; covers.add(g);
        break;
      }
      case 'marigold':
        [[-0.25, -0.2], [0.22, 0.12], [-0.02, 0.3], [0.28, -0.28], [-0.3, 0.25]].forEach(([x, z]) => {
          g.add(mesh(new THREE.CylinderGeometry(0.02, 0.025, 0.4, 5), mat(0x4e9a2e), x, 0.2, z));
          g.add(mesh(new THREE.SphereGeometry(0.12, 9, 7), mat(0xffa726, { emissive: 0xff8f00, emissiveIntensity: 0.25 }), x, 0.42, z));
        });
        break;
      default: g.add(mesh(new THREE.BoxGeometry(0.5, 0.5, 0.5), mat(0xffffff)));
    }
    g.traverse(o => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; o.userData.c0 = o.material.color.clone(); } });
    return g;
  }

  function addItem(item, lane, col) {
    const g = buildItem(item);
    if (item.scope === 'row') g.position.set(0, ROW_Y[item.id], laneZ(lane));
    else g.position.set(colX(col), LAYER_Y[item.layer] || 0, laneZ(lane));
    if (item.layer === 'underground') g.traverse(o => { o.renderOrder = 1; });
    if (item.kind === 'crop') g.scale.setScalar(0.3);
    scene.add(g); dynamic.add(g);
    return g;
  }
  function setGrowth(g, f) { g.scale.setScalar(0.3 + 0.7 * Math.max(0, Math.min(1, f))); }
  function setHealth(g, h) {
    const k = Math.max(0, Math.min(1, 1 - h / 100)) * 0.9;
    g.traverse(o => { if (o.isMesh && o.userData.c0) o.material.color.copy(o.userData.c0).lerp(BROWN, k); });
    g.rotation.z = k * 0.25; // wilting lean
  }
  function setCoverMode(mode) {
    for (const g of covers) g.userData.cloth.material.color.set(mode === 'shade' ? 0x3d4a3a : 0xffffff);
  }
  function pulseRow(g) { if (g && g.userData) g.userData.active = 1; }

  // ---------- plot state (soil water, saturation, frost, pests, topsoil) ----------
  function setPlot(lane, col, s) {
    const t = tiles[lane * COLS + col]; if (!t) return;
    Object.assign(t.userData.state, s);
    const st = t.userData.state;
    const c = SOIL_DRY.clone().lerp(SOIL_WET, Math.max(0, Math.min(1, st.moist)));
    c.lerp(SUBSOIL, Math.max(0, Math.min(0.7, (100 - st.topsoil) / 100 * 1.4)));
    if (st.frost) c.lerp(FROST, 0.65);
    if (t.userData.odd) c.multiplyScalar(0.92);
    t.material.color.copy(c);
    const pd = puddles[lane * COLS + col];
    pd.visible = st.sat > 3; pd.material.opacity = Math.min(0.75, 0.25 + st.sat / 60);
    const pg = pests[lane * COLS + col], n = Math.round(Math.max(0, st.pest - 0.08) * 10);
    pg.children.forEach((b, i) => { b.visible = i < n; });
  }

  function remove(obj) { if (!obj) return; scene.remove(obj); dynamic.delete(obj); covers.delete(obj); }
  function clearDynamic() {
    for (const o of Array.from(dynamic)) remove(o);
    for (let l = 0; l < LANES; l++) for (let c = 0; c < COLS; c++) setPlot(l, c, { moist: 0.5, sat: 0, frost: false, pest: 0, topsoil: 100 });
    weather.rain = 0; weather.wind = 0; weather.frost = false; weather.heat = 0;
  }

  function setXray(on) { xray = on; tiles.forEach(t => { t.material.opacity = on ? 0.22 : 0.85; }); }
  function setMood(type) { targetSky.set(SKY[type] || SKY.base); }
  function flashTile(lane, col, color = 0xffffff) {
    const t = tiles[lane * COLS + col]; if (!t) return;
    t.material.emissive.set(color); t.material.emissiveIntensity = 0.7;
    setTimeout(() => { t.material.emissive.set(0x000000); }, 180);
  }

  function animateParticles(dt) {
    // rain
    const rn = Math.round(Math.min(1, weather.rain / 25) * rain.userData.n);
    rain.geometry.setDrawRange(0, rn * 2);
    if (rn) {
      const a = rain.geometry.attributes.position.array, sp = 9 * dt, drift = weather.wind * 0.02;
      for (let i = 0; i < rn; i++) {
        const o = i * 6; a[o + 1] -= sp; a[o + 4] -= sp; a[o] -= drift; a[o + 3] -= drift;
        if (a[o + 4] < 0) { const x = (Math.random() - 0.5) * rain.userData.W, z = (Math.random() - 0.5) * rain.userData.D; a[o] = x; a[o + 1] = 7; a[o + 2] = z; a[o + 3] = x - 0.04; a[o + 4] = 6.72; a[o + 5] = z; }
      }
      rain.geometry.attributes.position.needsUpdate = true;
    }
    // dust
    const dn = Math.round(Math.min(1, Math.max(0, weather.dust || 0)) * dust.userData.n);
    dust.geometry.setDrawRange(0, dn);
    if (dn) {
      const a = dust.geometry.attributes.position.array, sp = (2 + weather.wind) * dt, half = dust.userData.W / 2;
      for (let i = 0; i < dn; i++) { const o = i * 3; a[o] -= sp * (0.7 + (i % 5) * 0.12); a[o + 1] += Math.sin((a[o] + i) * 3) * dt * 0.4; if (a[o] < -half) { a[o] = half; a[o + 1] = Math.random() * 1.6; } }
      dust.geometry.attributes.position.needsUpdate = true;
    }
    // frost sparkle
    const sn = weather.frost ? snow.userData.n : 0;
    snow.geometry.setDrawRange(0, sn);
    if (sn) {
      const a = snow.geometry.attributes.position.array;
      for (let i = 0; i < sn; i++) { const o = i * 3; a[o + 1] -= dt * 0.6; if (a[o + 1] < 0) a[o + 1] = 6; }
      snow.geometry.attributes.position.needsUpdate = true;
    }
  }

  function update(dt, elapsed) {
    const c = pointer.x > -1.5 ? pick() : null;
    hover = c; hoverFrame.visible = !!c;
    if (c) hoverFrame.position.set(colX(c.col), 0.08, laneZ(c.lane));
    scene.background.lerp(targetSky, Math.min(1, dt * 0.8));
    scene.fog.color.copy(scene.background);
    sun.intensity += ((weather.heat > 0 ? 1.45 : weather.rain > 5 ? 0.7 : 1.15) - sun.intensity) * Math.min(1, dt);
    animateParticles(dt);
    // drip emitters glow on irrigation days; drain tiles show water flowing out
    for (const g of dynamic) {
      const u = g.userData;
      if (u.emitters) { u.active = Math.max(0, (u.active || 0) - dt * 0.6); u.emitters.forEach((e, i) => { e.material.emissiveIntensity = 0.15 + u.active * (0.8 + 0.4 * Math.sin(elapsed * 6 + i)); }); }
      if (u.flow) { u.active = Math.max(0, (u.active || 0) - dt * 0.4); u.flow.forEach((d, i) => { d.visible = u.active > 0.05; d.position.x = ((elapsed * 1.6 + i * 1.5) % (COLS * CELL)) * -1 + (COLS * CELL) / 2; }); }
    }
    for (const pg of pests) for (const b of pg.children) if (b.visible) { const o = b.userData.o; o.a += dt * o.s; b.position.set(Math.cos(o.a) * o.r, o.y + Math.sin(o.a * 2) * 0.08, Math.sin(o.a) * o.r); }
    const sock = scene.getObjectByName('sock'); if (sock) sock.rotation.y = Math.sin(elapsed * (1 + weather.wind)) * 0.2 * Math.min(1, weather.wind / 3);
    renderer.render(scene, camera);
  }

  const _v = new THREE.Vector3();
  function toScreen(lane, xCol, y = 0.6) {
    _v.set(colX(xCol), y, laneZ(lane)).project(camera);
    const r = canvas.getBoundingClientRect();
    return { left: (_v.x + 1) / 2 * r.width, top: (1 - _v.y) / 2 * r.height };
  }

  return { init, addItem, setGrowth, setHealth, setCoverMode, pulseRow, setPlot, setWeather, remove, clearDynamic, setXray, setMood, flashTile, update, toScreen, resize, get hover() { return hover; } };
})();
