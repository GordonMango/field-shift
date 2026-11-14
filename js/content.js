/* ============================================================
   Defend Your Farm — game content
   Crops, defenses, climate threats, stages, regions, manual.
   ============================================================ */
const CONTENT = (() => {
  const LANES = 5;
  const COLS = 8;

  // Layers of the farm (the 3D scene stacks these vertically)
  const LAYERS = {
    underground: { name: 'Underground', desc: 'Irrigation and drainage. Works on water that the crop never sees.' },
    ground:      { name: 'Ground',      desc: 'Cash crops, windbreaks and companion plants.' },
    surface:     { name: 'Ground (soil surface)', desc: 'Mulch and cover crops that shield bare soil.' },
    sky:         { name: 'Sky',         desc: 'Row covers that sit above the crop and block heat and frost.' },
  };

  // Preset farming regions (lat/lon feed the NASA POWER request)
  const REGIONS = [
    { id: 'central_valley', name: 'Central Valley, California (USA)', lat: 36.74, lon: -119.78, note: 'Irrigated, drought-prone, long heat waves.' },
    { id: 'iowa',           name: 'Corn Belt, Iowa (USA)',            lat: 41.88, lon: -93.10,  note: 'Rain-fed corn and soy, spring floods, summer storms.' },
    { id: 'punjab',         name: 'Punjab, India',                    lat: 30.90, lon: 75.85,   note: 'Wheat–rice rotation, monsoon floods, pre-monsoon heat.' },
    { id: 'kenya',          name: 'Rift Valley, Kenya',               lat: -0.30, lon: 36.10,   note: 'Two rainy seasons, drought in between.' },
    { id: 'murray',         name: 'Murray–Darling Basin, Australia',  lat: -34.50, lon: 146.00, note: 'Irrigated, severe drought cycles.' },
    { id: 'sahel',          name: 'Sahel, Niger',                     lat: 13.50, lon: 2.10,    note: 'Wind erosion, short rainy season, extreme heat.' },
    { id: 'mato_grosso',    name: 'Mato Grosso, Brazil',              lat: -12.60, lon: -55.70, note: 'Soy and corn double-cropping, pest pressure.' },
    { id: 'flevoland',      name: 'Flevoland, Netherlands',           lat: 52.50, lon: 5.50,    note: 'Below sea level, wet winters, frost.' },
  ];

  // Climate threats = the "zombies"
  const THREATS = {
    drought: {
      id: 'drought', name: 'Drought', icon: '☀️', layer: 'sky', speed: 0.42, hp: 70, dps: 9, color: 0xff9a2e,
      counters: ['drip', 'mulch'],
      desc: 'A dry spell that sucks moisture out of the root zone. Unprotected crops wilt as it passes.',
      counterText: 'Drip irrigation (underground) sprays it from anywhere in the lane but spends your water. Mulch (surface) holds soil moisture in a single cell.',
      data: 'NASA SMAP root-zone soil moisture · POWER GWETROOT & PRECTOTCORR',
      rule: 'Spawns when root-zone soil wetness drops below ~45% and the week brings under 5 mm of rain.',
    },
    heat: {
      id: 'heat', name: 'Heatwave', icon: '🌡️', layer: 'sky', speed: 0.5, hp: 60, dps: 10, color: 0xff3b3b,
      counters: ['rowcover', 'mulch'],
      desc: 'Days above 32 °C that scorch leaves and shut down pollination.',
      counterText: 'Row cover (sky) shades the lane. Mulch keeps the soil surface cool in one cell.',
      data: 'NASA POWER T2M_MAX (MERRA-2) · ECOSTRESS plant stress',
      rule: 'Spawns as the weekly mean daily maximum temperature climbs past 28 °C.',
    },
    wind: {
      id: 'wind', name: 'Wind Erosion', icon: '🌪️', layer: 'ground', speed: 0.8, hp: 50, dps: 8, color: 0xb9b9c9,
      counters: ['windbreak', 'cover'],
      desc: 'Fast, dry wind that strips topsoil off bare fields. Moves quickly down the lane.',
      counterText: 'Windbreak trees (ground) stop it dead and slow every gust behind them. Cover crop (surface) anchors soil and absorbs the blow.',
      data: 'NASA POWER WS2M (MERRA-2) · MODIS bare-soil fraction',
      rule: 'Spawns when peak wind speed exceeds ~5 m/s, worse when soil is dry.',
    },
    flood: {
      id: 'flood', name: 'Flash Flood', icon: '🌊', layer: 'ground', speed: 0.38, hp: 90, dps: 12, color: 0x3a7bff,
      counters: ['drain', 'cover'],
      desc: 'A wall of water from a heavy storm week. Slow but tough, it drowns roots and washes seed away.',
      counterText: 'Drainage tile (underground) pulls water out of the whole lane. Cover crop (surface) soaks up and slows the flow in one cell.',
      data: 'NASA GPM IMERG precipitation · POWER PRECTOTCORR',
      rule: 'Spawns once a single week delivers more than ~30 mm of rain.',
    },
    pest: {
      id: 'pest', name: 'Pest Swarm', icon: '🐛', layer: 'ground', speed: 0.65, hp: 55, dps: 7, color: 0x6b4f2a,
      counters: ['marigold'],
      desc: 'Insects that explode in warm, humid weeks and when the same crop is planted in the same lane twice in a row.',
      counterText: 'Marigold companion planting (ground) repels pests down the lane. Rotating crop families starves them.',
      data: 'NASA POWER T2M & RH2M · MODIS NDVI anomaly',
      rule: 'Spawns in warm (>20 °C) and humid (>50% RH) weeks. Doubles in lanes with a broken rotation.',
    },
    frost: {
      id: 'frost', name: 'Frost', icon: '❄️', layer: 'sky', speed: 0.55, hp: 55, dps: 11, color: 0xcfefff,
      counters: ['rowcover'],
      desc: 'A cold snap below 0 °C. Kills tender seedlings and blossoms overnight.',
      counterText: 'Row cover (sky) traps ground heat and keeps the lane a few degrees warmer.',
      data: 'NASA POWER T2M_MIN (MERRA-2)',
      rule: 'Spawns in any week where the minimum temperature falls below freezing.',
    },
  };

  // Plants & tools = the "plants"
  const ITEMS = {
    // cash crops
    corn:   { id: 'corn',   kind: 'crop', name: 'Corn',     icon: '🌽', layer: 'ground', cost: 40, waterPerWeek: 0.8, growTime: 28, value: 110, hp: 100, family: 'Grass',
              desc: 'Hungry for water and nitrogen but the best earner early on. Follows soybeans beautifully.' },
    wheat:  { id: 'wheat',  kind: 'crop', name: 'Wheat',    icon: '🌾', layer: 'ground', cost: 25, waterPerWeek: 0.5, growTime: 20, value: 60,  hp: 90,  family: 'Grass',
              desc: 'Cheap, fast and frugal with water. A safe filler for dry seasons.' },
    soy:    { id: 'soy',    kind: 'crop', name: 'Soybean',  icon: '🫘', layer: 'ground', cost: 30, waterPerWeek: 0.7, growTime: 24, value: 85,  hp: 90,  family: 'Legume', nitrogen: true,
              desc: 'A legume: it fixes nitrogen into the soil, so whatever you plant after it yields more.' },
    tomato: { id: 'tomato', kind: 'crop', name: 'Tomato',   icon: '🍅', layer: 'ground', cost: 55, waterPerWeek: 1.2, growTime: 34, value: 180, hp: 80,  family: 'Nightshade',
              desc: 'High value, high water, fragile in heat and frost. Protect it or lose it.' },

    // defenses
    drip:      { id: 'drip', kind: 'defense', name: 'Drip Irrigation', icon: '💧', layer: 'underground', cost: 60, counters: ['drought'], range: 'lane', dmg: 22, fireRate: 1.2, waterPerShot: 0.5, projColor: 0x4fc3f7,
                 desc: 'Buried emitters that deliver water straight to roots. Fires at any drought in the lane, but every shot spends half a water unit from your allocation.' },
    mulch:     { id: 'mulch', kind: 'defense', name: 'Straw Mulch', icon: '🟫', layer: 'surface', cost: 15, counters: ['drought', 'heat'], range: 'cell', dps: 30, hp: 60,
                 desc: 'A blanket of straw that keeps soil moist and cool. Protects only the cell it covers, but it is cheap.' },
    cover:     { id: 'cover', kind: 'defense', name: 'Cover Crop (Rye)', icon: '🌱', layer: 'surface', cost: 20, counters: ['wind', 'flood'], range: 'cell', wall: true, hp: 160, dps: 25,
                 desc: 'Living roots hold soil in place. Blocks wind erosion and floods in its cell and takes the beating instead of your crop.' },
    windbreak: { id: 'windbreak', kind: 'defense', name: 'Windbreak Trees', icon: '🌲', layer: 'ground', cost: 80, counters: ['wind'], range: 'lane', dmg: 20, fireRate: 1.0, wall: true, hp: 300, projColor: 0x7bd36a,
                 desc: 'A row of trees. Shelters the whole lane behind it and physically stops wind erosion. Uses a ground cell, so plant it at the edge.' },
    drain:     { id: 'drain', kind: 'defense', name: 'Drainage Tile', icon: '🕳️', layer: 'underground', cost: 50, counters: ['flood'], range: 'lane', dmg: 25, fireRate: 1.1, projColor: 0x2f6fe0,
                 desc: 'Perforated pipe under the field that carries flood water away. Drains the whole lane.' },
    rowcover:  { id: 'rowcover', kind: 'defense', name: 'Row Cover', icon: '⛺', layer: 'sky', cost: 35, counters: ['heat', 'frost'], range: 'lane', dmg: 18, fireRate: 1.0, projColor: 0xffffff,
                 desc: 'Light fabric stretched over hoops. Shades crops in a heatwave and traps warmth during frost.' },
    marigold:  { id: 'marigold', kind: 'defense', name: 'Marigold (Companion)', icon: '🌼', layer: 'ground', cost: 30, counters: ['pest'], range: 'lane', dmg: 20, fireRate: 0.9, projColor: 0xffc400,
                 desc: 'Companion planting: marigold roots and scent repel nematodes and many insects down the lane.' },

    shovel:    { id: 'shovel', kind: 'tool', name: 'Remove', icon: '🧹', cost: 0, desc: 'Clear a cell (no refund).' },
  };

  // Stages. weekStart = null means "let the NASA data pick the worst window for this threat".
  const LEVELS = [
    { id: 1, name: 'Stage 1 · Dry Spell', focus: 'drought', weeks: 6,
      threats: { drought: 1.6 }, unlock: ['corn', 'drip', 'shovel'],
      intro: 'Your first season. The NASA soil-moisture feed shows the root zone drying out. Plant corn to earn money, then bury drip irrigation so droughts never reach it.',
      tip: 'Drip irrigation shoots from anywhere in the lane, but each shot spends water. Watch the tank.' },
    { id: 2, name: 'Stage 2 · Dust in the Wind', focus: 'wind', weeks: 6,
      threats: { drought: 0.7, wind: 1.6 }, unlock: ['wheat', 'cover', 'windbreak'],
      intro: 'Dry, bare soil plus wind equals a dust storm. Anchor the surface with a cover crop or plant a windbreak at the edge of each lane.',
      tip: 'Windbreaks are walls: wind stops at them. Put them in the right-most column so everything behind is sheltered.' },
    { id: 3, name: 'Stage 3 · Washout', focus: 'flood', weeks: 7,
      threats: { wind: 0.7, flood: 1.6, drought: 0.4 }, unlock: ['drain'],
      intro: 'GPM-style rain totals spike this window. Floods are slow but tough. Lay drainage tile underground and keep cover crops where water pools.',
      tip: 'Flood weeks also refill your water tank, so this is a good time to let drip irrigation rest.' },
    { id: 4, name: 'Stage 4 · Heat Dome', focus: 'heat', weeks: 7,
      threats: { heat: 1.6, drought: 1.0, flood: 0.4 }, unlock: ['tomato', 'mulch', 'rowcover'],
      intro: 'Daily highs climb past 32 °C. Tomatoes pay well but cook in the heat. Shade them with row covers and keep the soil cool with mulch.',
      tip: 'Mulch counters BOTH drought and heat but only in one cell. Cheap insurance for your priciest crops.' },
    { id: 5, name: 'Stage 5 · Pest Pressure', focus: 'pest', weeks: 8,
      threats: { pest: 1.6, heat: 0.7, wind: 0.6, flood: 0.5, drought: 0.6, frost: 0.5 }, unlock: ['soy', 'marigold'],
      intro: 'Warm, humid weeks breed insects. Interplant marigolds, and learn why soybeans make next season’s corn stronger.',
      tip: 'Everything you have learned is in play now. Check the forecast panel every week.' },
    { id: 6, name: 'Final · A Full Year', focus: null, weeks: 52, rotation: true,
      threats: { drought: 1, heat: 1, wind: 1, flood: 1, pest: 1, frost: 1 }, unlock: [],
      intro: 'Plan a four-season crop rotation for every lane within your budget and water allocation, then survive all 52 weeks of real NASA weather for your location.',
      tip: 'Rotate families. Legume → grass is a yield bonus. Same family twice is a pest magnet. Cover crops in the off-season protect the soil.' },
  ];

  const ROTATION_CHOICES = [
    { id: 'corn', name: 'Corn' }, { id: 'wheat', name: 'Wheat' }, { id: 'soy', name: 'Soybean' }, { id: 'tomato', name: 'Tomato' },
    { id: 'cover', name: 'Cover crop (rye)' }, { id: 'fallow', name: 'Fallow (rest)' },
  ];

  // NASA data sources for the manual
  const DATASETS = [
    { name: 'NASA POWER (MERRA‑2 / GEOS‑IT)', used: 'LIVE — drives every week of weather in this game',
      what: 'Daily air temperature, rainfall, wind speed, humidity and root-zone soil wetness for any point on Earth, derived from NASA’s MERRA‑2 reanalysis and satellite radiation products. Free, no key, agriculture community endpoint.',
      url: 'https://power.larc.nasa.gov/' },
    { name: 'SMAP (Soil Moisture Active Passive)', used: 'Modelled through POWER’s GWETROOT; the drought threat',
      what: 'An L-band radiometer that maps the top 5 cm of soil moisture globally every 2–3 days. Its Level‑4 product estimates root-zone moisture, the single best early-warning signal for agricultural drought.',
      url: 'https://smap.jpl.nasa.gov/' },
    { name: 'GPM IMERG', used: 'The flood threat',
      what: 'Global Precipitation Measurement merges a constellation of satellites into half-hourly rainfall maps. Weekly totals above ~30 mm trigger floods in the game.',
      url: 'https://gpm.nasa.gov/data/imerg' },
    { name: 'MODIS / VIIRS NDVI', used: 'Pest and erosion risk context',
      what: 'Vegetation greenness every 1–2 days. Sudden drops in NDVI flag pest outbreaks and bare soil that is vulnerable to wind erosion.',
      url: 'https://modis.gsfc.nasa.gov/data/dataprod/mod13.php' },
    { name: 'Landsat 8 / 9', used: 'Field-scale crop health (manual)',
      what: '30 m imagery every 8 days. Fine enough to see individual fields, used for crop-type mapping and irrigation scheduling.',
      url: 'https://landsat.gsfc.nasa.gov/' },
    { name: 'ECOSTRESS', used: 'The heat threat',
      what: 'A thermal radiometer on the ISS that measures plant temperature and evapotranspiration, showing which fields are stressed hours before leaves wilt.',
      url: 'https://ecostress.jpl.nasa.gov/' },
  ];

  const ROTATION_RULES = [
    'Same family in the same lane two seasons in a row → pests ×1.8 and yield ×0.85 (disease and insects build up in the soil).',
    'Legume (soybean) followed by any crop → yield ×1.25 (nitrogen fixed in the soil).',
    'Cover crop followed by a cash crop → crop health +20% and yield ×1.10 (protected, structured soil).',
    'Fallow → nothing earned, but the lane resets (no pest carry-over).',
    'Cover crop seasons are cheap and fill the surface layer of the whole lane with rye, blocking wind and floods.',
  ];

  return { LANES, COLS, LAYERS, REGIONS, THREATS, ITEMS, LEVELS, ROTATION_CHOICES, DATASETS, ROTATION_RULES };
})();
