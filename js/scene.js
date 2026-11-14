/* ============================================================
   Three.js scene: the 3D farm with underground / ground / sky
   layers, lane grid, low-poly crops, defenses and threats.
   ============================================================ */
const SceneMgr = (() => {
  const { LANES, COLS } = CONTENT;
  const CELL = 1.15;
  const LAYER_Y = { underground: -0.42, surface: 0.09, ground: 0.05, sky: 0.98 };
  const THREAT_Y = { sky: 1.75, ground: 0.4 };
  const PIXEL_SCALE = 0.3;
  const SKY = { base: 0x9fd0ee, drought: 0xf2c58a, heat: 0xffad7d, flood: 0x6f84a3, frost: 0xdfeefb, wind: 0xc9c4b4, pest: 0xb8d38e };

  let renderer, scene, camera, raycaster, pointer, canvas, wrap, hemi, sun;
  const tiles = [];
  const dynamic = new Set();      // meshes removed between stages
  const animated = new Set();
  let hover = null, onCellClick = null, xray = false;
  const targetSky = new THREE.Color(SKY.base);

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
    sun.shadow.bias = -0.0015; sun.shadow.normalBias = 0.03; // avoids shadow acne on the flat tiles
    Object.assign(sun.shadow.camera, { left: -11, right: 11, top: 11, bottom: -11, near: 1, far: 40 });
    scene.add(sun);

    // surrounding grass
    const far = new THREE.Mesh(new THREE.PlaneGeometry(90, 90), mat(0x72ab4d)); far.rotation.x = -Math.PI / 2; far.position.y = -0.96; far.receiveShadow = true; scene.add(far);
    // the translucent soil block — underground layer lives inside it
    const slab = new THREE.Mesh(new THREE.BoxGeometry(COLS * CELL + 0.5, 0.9, LANES * CELL + 0.5), mat(0x5a3a1d, { transparent: true, opacity: 0.5 }));
    slab.position.y = -0.5; slab.receiveShadow = true; scene.add(slab);
    // tiles
    const tg = new THREE.BoxGeometry(CELL - 0.08, 0.1, CELL - 0.08);
    for (let lane = 0; lane < LANES; lane++) for (let col = 0; col < COLS; col++) {
      const base = (lane + col) % 2 ? 0x6e4727 : 0x7c5230;
      const t = new THREE.Mesh(tg, mat(base, { transparent: true, opacity: 0.82 }));
      t.position.set(colX(col), 0, laneZ(lane)); t.receiveShadow = true; t.renderOrder = 2; // draw after the translucent soil slab
      t.userData = { lane, col, base };
      scene.add(t); tiles.push(t);
    }
    buildBarn();
    buildRoad();
    buildDecor();

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
    const silo = mesh(new THREE.CylinderGeometry(0.42, 0.42, 1.9, 16), mat(0xd9d9d9), 0, 0.95, 1.95); barn.add(silo);
    barn.add(mesh(new THREE.SphereGeometry(0.42, 16, 12, 0, Math.PI * 2, 0, Math.PI / 2), mat(0x9a9a9a), 0, 1.9, 1.95));
    barn.position.set(x, 0, -0.6);
    scene.add(barn);
    // fence along left edge
    const fx = colX(0) - 0.85;
    for (let lane = 0; lane < LANES; lane++) {
      scene.add(mesh(new THREE.BoxGeometry(0.08, 0.6, 0.08), mat(0xd8c3a0), fx, 0.3, laneZ(lane) - 0.5));
    }
    scene.add(mesh(new THREE.BoxGeometry(0.06, 0.06, LANES * CELL + 0.2), mat(0xd8c3a0), fx, 0.45, 0));
  }
  function buildRoad() {
    const road = new THREE.Mesh(new THREE.BoxGeometry(1.8, 0.06, LANES * CELL + 1.2), mat(0x474747)); road.position.set(colX(COLS) + 0.6, -0.03, 0); road.receiveShadow = true; scene.add(road);
    for (let i = 0; i < 6; i++) scene.add(mesh(new THREE.BoxGeometry(0.1, 0.01, 0.5), mat(0xf1e37a), colX(COLS) + 0.6, 0.01, -3 + i * 1.2));
    // storm horizon marker
    const sign = mesh(new THREE.BoxGeometry(0.6, 0.4, 0.05), mat(0xf7f7f7), colX(COLS) + 1.4, 0.8, laneZ(0) - 0.9);
    sign.rotation.y = -0.4; scene.add(sign);
    scene.add(mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.8, 6), mat(0x5a3a1d), colX(COLS) + 1.4, 0.4, laneZ(0) - 0.9));
  }
  function buildDecor() {
    const positions = [[-7.5, -4.5], [-8, 2.5], [7.5, 4.3], [8.5, -3.8], [0, -6.5], [-4, -6], [4.5, -6.3]];
    positions.forEach(([x, z]) => scene.add(tree(x, z, 1 + Math.random() * 0.4)));
  }
  function tree(x, z, s = 1) {
    const g = new THREE.Group();
    g.add(mesh(new THREE.CylinderGeometry(0.12, 0.16, 0.8, 7), mat(0x6b4423), 0, 0.4, 0));
    g.add(mesh(new THREE.ConeGeometry(0.55, 1.1, 8), mat(0x2f7a3a), 0, 1.2, 0));
    g.add(mesh(new THREE.ConeGeometry(0.4, 0.9, 8), mat(0x3b8f45), 0, 1.75, 0));
    g.position.set(x, -0.95, z); g.scale.setScalar(s);
    return g;
  }

  // ---------- item meshes ----------
  function buildItem(item) {
    const g = new THREE.Group();
    const rnd = (a, b) => a + Math.random() * (b - a);
    switch (item.id) {
      case 'corn': {
        [[-0.25, -0.2], [0.2, 0.15], [-0.1, 0.3]].forEach(([x, z]) => {
          g.add(mesh(new THREE.CylinderGeometry(0.035, 0.05, 0.95, 6), mat(0x4e9a2e), x, 0.47, z));
          const leaf = mesh(new THREE.BoxGeometry(0.4, 0.02, 0.1), mat(0x5fb23a), x + 0.15, 0.55, z); leaf.rotation.z = 0.5; g.add(leaf);
          const leaf2 = mesh(new THREE.BoxGeometry(0.4, 0.02, 0.1), mat(0x5fb23a), x - 0.15, 0.35, z); leaf2.rotation.z = -0.5; g.add(leaf2);
          g.add(mesh(new THREE.CylinderGeometry(0.06, 0.07, 0.28, 8), mat(0xf2cf3a), x + 0.09, 0.6, z));
        });
        break;
      }
      case 'wheat': {
        for (let i = 0; i < 9; i++) {
          const x = rnd(-0.4, 0.4), z = rnd(-0.4, 0.4);
          g.add(mesh(new THREE.CylinderGeometry(0.015, 0.02, 0.7, 5), mat(0xd7b24c), x, 0.35, z));
          g.add(mesh(new THREE.CylinderGeometry(0.045, 0.03, 0.2, 6), mat(0xe8c866), x, 0.78, z));
        }
        break;
      }
      case 'soy': {
        [[-0.25, -0.2], [0.22, 0.1], [-0.05, 0.3]].forEach(([x, z]) => {
          g.add(mesh(new THREE.SphereGeometry(0.2, 10, 8), mat(0x4d9b3b), x, 0.22, z));
          g.add(mesh(new THREE.SphereGeometry(0.05, 6, 5), mat(0x9fd45c), x + 0.12, 0.3, z + 0.1));
        });
        break;
      }
      case 'tomato': {
        [[-0.25, 0], [0.25, 0]].forEach(([x, z]) => {
          g.add(mesh(new THREE.CylinderGeometry(0.02, 0.02, 0.8, 5), mat(0x9b9b9b), x, 0.4, z));
          g.add(mesh(new THREE.SphereGeometry(0.24, 10, 8), mat(0x3f8f3a), x, 0.45, z));
          g.add(mesh(new THREE.SphereGeometry(0.07, 8, 6), mat(0xe53935), x + 0.15, 0.4, z + 0.15));
          g.add(mesh(new THREE.SphereGeometry(0.07, 8, 6), mat(0xe53935), x - 0.12, 0.55, z - 0.1));
        });
        break;
      }
      case 'drip': {
        const pipe = mesh(new THREE.CylinderGeometry(0.06, 0.06, CELL * 0.96, 10), mat(0x2d7fc4, { emissive: 0x1c5a99, emissiveIntensity: 0.6 })); pipe.rotation.z = Math.PI / 2; g.add(pipe);
        [-0.3, 0, 0.3].forEach(x => g.add(mesh(new THREE.SphereGeometry(0.07, 8, 6), mat(0x4fc3f7, { emissive: 0x4fc3f7, emissiveIntensity: 0.8 }), x, 0.08, 0)));
        // emitter heads poking above the soil so the player can see where pipes are
        [-0.3, 0, 0.3].forEach(x => { g.add(mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.55, 6), mat(0x2d7fc4), x, 0.3, 0)); g.add(mesh(new THREE.SphereGeometry(0.06, 8, 6), mat(0x4fc3f7, { emissive: 0x4fc3f7, emissiveIntensity: 0.6 }), x, 0.58, 0)); });
        break;
      }
      case 'drain': {
        const pipe = mesh(new THREE.CylinderGeometry(0.11, 0.11, CELL * 0.96, 10), mat(0x1f2f4f, { emissive: 0x3a62b8, emissiveIntensity: 0.5 })); pipe.rotation.z = Math.PI / 2; g.add(pipe);
        for (let i = 0; i < 4; i++) g.add(mesh(new THREE.BoxGeometry(0.05, 0.05, 0.26), mat(0x0b1a33), -0.35 + i * 0.23, 0.05, 0));
        // inspection grate at the surface
        g.add(mesh(new THREE.BoxGeometry(0.36, 0.04, 0.36), mat(0x3b4a63), 0, 0.55, 0));
        g.add(mesh(new THREE.BoxGeometry(0.26, 0.05, 0.26), mat(0x101c33), 0, 0.56, 0));
        break;
      }
      case 'mulch': {
        g.add(mesh(new THREE.BoxGeometry(0.96, 0.07, 0.96), mat(0xc9a469), 0, 0, 0));
        for (let i = 0; i < 10; i++) { const s = mesh(new THREE.BoxGeometry(0.25, 0.02, 0.04), mat(0xe2c27e), rnd(-0.38, 0.38), 0.045, rnd(-0.4, 0.4)); s.rotation.y = rnd(0, Math.PI); g.add(s); }
        break;
      }
      case 'cover': {
        g.add(mesh(new THREE.BoxGeometry(0.96, 0.05, 0.96), mat(0x4e7f2d), 0, 0, 0));
        for (let i = 0; i < 16; i++) { const b = mesh(new THREE.BoxGeometry(0.04, 0.28, 0.04), mat(0x7cc24a), rnd(-0.42, 0.42), 0.15, rnd(-0.42, 0.42)); b.rotation.z = rnd(-0.3, 0.3); g.add(b); }
        break;
      }
      case 'windbreak': {
        [[-0.3, -0.3], [0.25, 0.3], [-0.1, 0.05]].forEach(([x, z]) => {
          g.add(mesh(new THREE.CylinderGeometry(0.07, 0.09, 0.6, 7), mat(0x6b4423), x, 0.3, z));
          g.add(mesh(new THREE.ConeGeometry(0.33, 0.8, 8), mat(0x2f7a3a), x, 0.95, z));
          g.add(mesh(new THREE.ConeGeometry(0.24, 0.6, 8), mat(0x3b8f45), x, 1.35, z));
        });
        break;
      }
      case 'rowcover': {
        const cloth = mesh(new THREE.BoxGeometry(0.98, 0.03, 0.98), mat(0xffffff, { transparent: true, opacity: 0.6, roughness: 0.4 })); g.add(cloth);
        [[-0.44, -0.44], [0.44, -0.44], [-0.44, 0.44], [0.44, 0.44]].forEach(([x, z]) => g.add(mesh(new THREE.CylinderGeometry(0.02, 0.02, 0.95, 5), mat(0xbdbdbd), x, -0.47, z)));
        break;
      }
      case 'marigold': {
        [[-0.25, -0.2], [0.22, 0.12], [-0.02, 0.3], [0.28, -0.28]].forEach(([x, z]) => {
          g.add(mesh(new THREE.CylinderGeometry(0.02, 0.025, 0.4, 5), mat(0x4e9a2e), x, 0.2, z));
          g.add(mesh(new THREE.SphereGeometry(0.12, 9, 7), mat(0xffa726, { emissive: 0xff8f00, emissiveIntensity: 0.25 }), x, 0.42, z));
        });
        break;
      }
      default: g.add(mesh(new THREE.BoxGeometry(0.5, 0.5, 0.5), mat(0xffffff)));
    }
    g.traverse(o => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; } });
    return g;
  }

  function addItem(item, lane, col) {
    const g = buildItem(item);
    g.position.set(colX(col), LAYER_Y[item.layer] || 0, laneZ(lane));
    if (item.layer === 'underground') g.traverse(o => { o.renderOrder = 1; });
    if (item.kind === 'crop') g.scale.set(0.3, 0.3, 0.3);
    scene.add(g); dynamic.add(g);
    return g;
  }

  // ---------- threat meshes ----------
  function buildThreat(def) {
    const g = new THREE.Group();
    g.userData.anim = { type: def.id, t: Math.random() * 10, parts: [] };
    switch (def.id) {
      case 'drought': {
        g.add(mesh(new THREE.SphereGeometry(0.34, 16, 12), mat(0xffb347, { emissive: 0xff7a00, emissiveIntensity: 0.9 })));
        const ring = mesh(new THREE.TorusGeometry(0.5, 0.045, 8, 24), mat(0xffd27a, { emissive: 0xffa000, emissiveIntensity: 0.7 })); ring.rotation.x = Math.PI / 2; g.add(ring); g.userData.anim.parts.push(ring);
        break;
      }
      case 'heat': {
        g.add(mesh(new THREE.SphereGeometry(0.3, 16, 12), mat(0xff5252, { emissive: 0xff1744, emissiveIntensity: 1 })));
        const halo = mesh(new THREE.SphereGeometry(0.48, 16, 12), mat(0xff8a65, { transparent: true, opacity: 0.3, emissive: 0xff3d00, emissiveIntensity: 0.6 })); g.add(halo); g.userData.anim.parts.push(halo);
        break;
      }
      case 'wind': {
        const cone = mesh(new THREE.ConeGeometry(0.4, 1.1, 10, 1, true), mat(0xbfc3cf, { transparent: true, opacity: 0.7, side: THREE.DoubleSide })); cone.position.y = 0.3; g.add(cone); g.userData.anim.parts.push(cone);
        const r1 = mesh(new THREE.TorusGeometry(0.42, 0.03, 6, 20), mat(0xe0e0e0, { transparent: true, opacity: 0.8 })); r1.rotation.x = Math.PI / 2; r1.position.y = 0.75; g.add(r1); g.userData.anim.parts.push(r1);
        const r2 = mesh(new THREE.TorusGeometry(0.25, 0.03, 6, 20), mat(0xe0e0e0, { transparent: true, opacity: 0.8 })); r2.rotation.x = Math.PI / 2; r2.position.y = 0.2; g.add(r2); g.userData.anim.parts.push(r2);
        break;
      }
      case 'flood': {
        g.add(mesh(new THREE.BoxGeometry(0.95, 0.5, 1.0), mat(0x2f6fe0, { transparent: true, opacity: 0.75 }), 0, -0.1, 0));
        const foam = mesh(new THREE.BoxGeometry(0.98, 0.12, 1.04), mat(0xbfe3ff, { transparent: true, opacity: 0.85 }), 0, 0.2, 0); g.add(foam); g.userData.anim.parts.push(foam);
        break;
      }
      case 'pest': {
        for (let i = 0; i < 7; i++) {
          const b = mesh(new THREE.SphereGeometry(0.09, 7, 6), mat(0x4a3320)); b.userData.orbit = { r: 0.2 + Math.random() * 0.25, a: Math.random() * Math.PI * 2, s: 2 + Math.random() * 3, y: Math.random() * 0.4 };
          g.add(b); g.userData.anim.parts.push(b);
        }
        break;
      }
      case 'frost': {
        const ico = mesh(new THREE.IcosahedronGeometry(0.34, 0), mat(0xe3f4ff, { emissive: 0x9fd6ff, emissiveIntensity: 0.6, transparent: true, opacity: 0.9, flatShading: true })); g.add(ico); g.userData.anim.parts.push(ico);
        break;
      }
    }
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  }

  function addThreat(def, lane, xCol) {
    const g = buildThreat(def);
    g.position.set(colX(xCol), THREAT_Y[def.layer] || 0.4, laneZ(lane));
    scene.add(g); dynamic.add(g); animated.add(g);
    return g;
  }
  function moveThreat(g, xCol, hpFrac) {
    g.position.x = colX(xCol);
    const s = 0.65 + 0.35 * Math.max(0, hpFrac);
    g.userData.hpScale = s;
  }

  function addProjectile(color, lane, xCol, y) {
    const m = mesh(new THREE.SphereGeometry(0.09, 8, 6), mat(color, { emissive: color, emissiveIntensity: 0.9 }), colX(xCol), y, laneZ(lane));
    m.castShadow = false; scene.add(m); dynamic.add(m); return m;
  }
  function moveProjectile(m, xCol) { m.position.x = colX(xCol); }

  function remove(obj) { if (!obj) return; scene.remove(obj); dynamic.delete(obj); animated.delete(obj); }
  function clearDynamic() { for (const o of Array.from(dynamic)) remove(o); }

  function setXray(on) {
    xray = on;
    tiles.forEach(t => { t.material.opacity = on ? 0.22 : 0.82; });
  }
  function setMood(type) {
    targetSky.set(SKY[type] || SKY.base);
  }
  function flashTile(lane, col, color = 0xffffff) {
    const t = tiles[lane * COLS + col]; if (!t) return;
    t.material.emissive.set(color); t.material.emissiveIntensity = 0.7;
    setTimeout(() => { t.material.emissive.set(0x000000); }, 180);
  }

  function update(dt, elapsed) {
    // hover highlight
    const c = pointer.x > -1.5 ? pick() : null;
    if (hover && (!c || hover.userData !== c)) { hover.material.color.set(hover.userData.base); hover = null; }
    if (c && !hover) { hover = tiles[c.lane * COLS + c.col]; hover.material.color.set(0xd9a066); }
    // sky mood
    scene.background.lerp(targetSky, Math.min(1, dt * 0.8));
    scene.fog.color.copy(scene.background);
    // animations
    for (const g of animated) {
      const a = g.userData.anim; a.t += dt;
      const s = g.userData.hpScale || 1;
      switch (a.type) {
        case 'drought': a.parts[0].rotation.z += dt * 1.5; g.position.y = THREAT_Y.sky + Math.sin(a.t * 2) * 0.08; g.scale.setScalar(s); break;
        case 'heat': { const p = 1 + Math.sin(a.t * 6) * 0.15; a.parts[0].scale.setScalar(p); g.scale.setScalar(s); break; }
        case 'wind': g.rotation.y += dt * 7; a.parts[1].rotation.z += dt * 4; a.parts[2].rotation.z -= dt * 5; g.scale.setScalar(s); break;
        case 'flood': a.parts[0].position.y = 0.2 + Math.sin(a.t * 5) * 0.05; g.scale.set(1, s, 1); break;
        case 'pest': for (const b of a.parts) { const o = b.userData.orbit; o.a += dt * o.s; b.position.set(Math.cos(o.a) * o.r, o.y + Math.sin(o.a * 2) * 0.1, Math.sin(o.a) * o.r); } g.scale.setScalar(s); break;
        case 'frost': a.parts[0].rotation.y += dt; a.parts[0].rotation.x += dt * 0.6; g.scale.setScalar(s); break;
      }
    }
    renderer.render(scene, camera);
  }

  const _v = new THREE.Vector3();
  function toScreen(lane, xCol, y = 0.6) {
    _v.set(colX(xCol), y, laneZ(lane)).project(camera);
    const r = canvas.getBoundingClientRect();
    return { left: (_v.x + 1) / 2 * r.width, top: (1 - _v.y) / 2 * r.height };
  }

  return { init, addItem, addThreat, moveThreat, addProjectile, moveProjectile, remove, clearDynamic, setXray, setMood, flashTile, update, toScreen, resize, THREAT_Y, LAYER_Y, get scene() { return scene; }, get renderer() { return renderer; } };
})();
