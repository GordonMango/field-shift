/* ============================================================
   Defend Your Farm — game content
   Weather hazards, crops, farming practices, stages, regions, manual.
   Every number here is a simplified version of a real agronomic
   threshold or practice effect, and the manual explains each one.
   ============================================================ */
const CONTENT = (() => {
  const LANES = 5;   // rows of plots
  const COLS = 8;    // plots per row

  // Layers of the farm (the 3D scene stacks these vertically)
  const LAYERS = {
    underground: { name: 'Underground', desc: 'Drip lines and drainage tile. Each one runs the full length of a row, under the roots.' },
    ground:      { name: 'Ground',      desc: 'Cash crops, windbreak trees and companion flowers. One per plot.' },
    surface:     { name: 'Soil surface', desc: 'Straw mulch or a living cover crop that protects bare soil. One per plot.' },
    sky:         { name: 'Above crop',  desc: 'Hoop covers: shade cloth in hot weather, frost cloth on cold nights.' },
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

  // Weather hazards. They are not creatures: they are conditions computed every day from the
  // NASA POWER record, and each plot feels them through its own soil water, cover and shelter.
  const HAZARDS = {
    drought: {
      id: 'drought', name: 'Drought stress', color: 0xff9a2e,
      desc: 'When the root zone dries below about half of its available water, the crop closes its pores, stops growing and starts to wilt.',
      how: 'The game runs a daily soil water balance for every plot: rain soaks in, the crop transpires water out (FAO-56 Hargreaves evapotranspiration from NASA temperatures), and below 50% available water the crop loses health.',
      fix: 'Drip irrigation tops the root zone back up from your water allocation. Straw mulch cuts evaporation so the water lasts longer.',
      data: 'NASA POWER PRECTOTCORR, T2M_MAX/MIN, GWETROOT (SMAP-style root-zone wetness sets the starting soil moisture)',
    },
    heat: {
      id: 'heat', name: 'Heat stress', color: 0xff3b3b,
      desc: 'Above a crop-specific temperature, pollen dies, fruit drops and grain stops filling. A well-watered crop transpires and runs cooler than the air; a thirsty one cannot.',
      how: 'Each day the NASA maximum temperature becomes a canopy temperature: 3 to 6 °C cooler if the crop has plenty of water (more in dry air), 2 °C hotter if it is drought-stressed. Every degree above the crop threshold (wheat and tomato 32 °C, corn and soybean 35 °C) costs health.',
      fix: 'Shade cloth on a hoop cover lowers canopy temperature by about 3 °C. Mulch keeps the soil cooler. Keeping the crop watered keeps it cooling itself.',
      data: 'NASA POWER T2M_MAX (MERRA-2) · ECOSTRESS measures this plant heat stress from orbit',
    },
    wind: {
      id: 'wind', name: 'Wind erosion', color: 0xb9b9c9,
      desc: 'Strong wind over dry, bare soil lifts the fine topsoil away and sandblasts young seedlings. Lost topsoil holds less water and grows smaller crops for years.',
      how: 'On days when NASA wind speed passes your location\'s erosion threshold and the soil surface is dry, bare plots lose topsoil. Young crops with little canopy are also damaged.',
      fix: 'A windbreak on the windward edge slows the wind for several plots behind it. Cover crops and mulch hold the soil surface in place.',
      data: 'NASA POWER WS2M (MERRA-2) · MODIS sees the bare soil that erodes',
    },
    flood: {
      id: 'flood', name: 'Waterlogging', color: 0x3a7bff,
      desc: 'Heavy rain fills every pore in the soil. Roots cannot breathe, and after two or three saturated days the crop starts to die. Runoff also washes topsoil away.',
      how: 'Rain beyond what the root zone can hold saturates the plot. It drains about 8 mm a day, or only 3 mm a day on the heavy soils of humid regions. After two saturated days the crop loses health every day.',
      fix: 'Drainage tile under the row carries the excess water away in a day. Cover crop roots open channels so more rain soaks in and less runs off.',
      data: 'NASA POWER PRECTOTCORR · GPM IMERG maps the heavy rain from space',
    },
    pest: {
      id: 'pest', name: 'Pest outbreak', color: 0x6b4f2a,
      desc: 'Insects multiply fastest in warm, humid weather, and they build up in the soil when the same crop family is planted in the same place again.',
      how: 'Each plot has a pest population that grows every warm, humid day and spreads to neighbours. Above the economic threshold it eats into crop health.',
      fix: 'Marigold companion strips repel pests and attract predators nearby. Hoop covers keep insects out. Rotating crop families starves soil-borne pests.',
      data: 'NASA POWER T2M & RH2M · MODIS NDVI drops flag outbreaks',
    },
    frost: {
      id: 'frost', name: 'Frost', color: 0xcfefff,
      desc: 'A night below freezing forms ice inside tender leaves and blossoms and kills them in a few hours.',
      how: 'Each night the NASA minimum temperature is compared with the crop\'s frost threshold (tomato 1 °C, soybean -1 °C, corn 0 °C, wheat -4 °C).',
      fix: 'Frost cloth on a hoop cover traps ground heat and keeps the crop about 3 °C warmer. Moist soil also stores and releases more heat overnight.',
      data: 'NASA POWER T2M_MIN (MERRA-2)',
    },
  };

  // Crops and practices
  const ITEMS = {
    // cash crops: value is the harvest at full health over a full stage
    corn:   { id: 'corn', kind: 'crop', name: 'Corn', layer: 'ground', cost: 40, value: 110, family: 'Grass',
              kc: 1.15, heat: 35, frost: 0, droughtSens: 1.0,
              desc: 'Thirsty and heat-sensitive at pollination, but the best earner. Yields more after soybeans.' },
    wheat:  { id: 'wheat', kind: 'crop', name: 'Wheat', layer: 'ground', cost: 25, value: 60, family: 'Grass',
              kc: 0.9, heat: 32, frost: -4, droughtSens: 0.7,
              desc: 'Cheap and uses less water. Tolerates light frost, but grain fill suffers above 32 °C.' },
    soy:    { id: 'soy', kind: 'crop', name: 'Soybean', layer: 'ground', cost: 30, value: 85, family: 'Legume', nitrogen: true,
              kc: 1.0, heat: 35, frost: -1, droughtSens: 0.9,
              desc: 'A legume. Bacteria on its roots fix nitrogen, so the next crop in that row yields more.' },
    tomato: { id: 'tomato', kind: 'crop', name: 'Tomato', layer: 'ground', cost: 55, value: 180, family: 'Nightshade',
              kc: 1.1, heat: 32, frost: 1, droughtSens: 1.2,
              desc: 'High value but fragile: flowers drop above 32 °C and one frost kills it.' },

    // practices
    drip:      { id: 'drip', kind: 'practice', name: 'Drip Line', layer: 'underground', scope: 'row', cost: 120, helps: ['drought'],
                 effect: 'When a planted plot falls below 60% soil water, it is refilled to 85%. Uses 1 water unit per 20 mm on one plot, at 90% efficiency.',
                 desc: 'A buried line with an emitter at every plant. It waters the whole row from your allocation, only when the soil needs it.' },
    mulch:     { id: 'mulch', kind: 'practice', name: 'Straw Mulch', layer: 'surface', scope: 'plot', cost: 15, helps: ['drought', 'heat', 'wind'],
                 effect: 'Evaporation -20% on young crops (-13% under a full canopy). Soil 1 °C cooler. Erosion -60%.',
                 desc: 'A layer of straw over the soil. Water lasts longer, the soil stays cooler, and the wind cannot lift the surface.' },
    cover:     { id: 'cover', kind: 'practice', name: 'Cover Crop (Rye)', layer: 'surface', scope: 'plot', cost: 20, helps: ['wind', 'flood'],
                 effect: 'Erosion -75%. Runoff -50%. Drains 4 mm/day faster. Uses 8% more water.',
                 desc: 'Living rye between the rows. Its roots bind the topsoil and open channels so rain soaks in instead of pooling.' },
    windbreak: { id: 'windbreak', kind: 'practice', name: 'Windbreak Trees', layer: 'ground', scope: 'shelter', cost: 80, helps: ['wind'],
                 effect: 'Wind -60%, -50%, -35%, -20% on the 4 plots downwind in its row. Evaporation -10% there.',
                 desc: 'A row of trees on the windward (right-hand) edge. It takes up a plot, but slows the wind for the plots behind it.' },
    drain:     { id: 'drain', kind: 'practice', name: 'Drainage Tile', layer: 'underground', scope: 'row', cost: 100, helps: ['flood'],
                 effect: 'Saturated water drains at 35 mm/day instead of 3 to 8.',
                 desc: 'Perforated pipe under the whole row. After a storm it carries the excess water out so roots can breathe again.' },
    rowcover:  { id: 'rowcover', kind: 'practice', name: 'Hoop Cover', layer: 'sky', scope: 'plot', cost: 35, helps: ['heat', 'frost', 'pest'],
                 effect: 'Hot days 3 °C cooler (shade cloth). Frost nights 3 °C warmer (frost cloth). Insects -50%.',
                 desc: 'Fabric stretched over hoops. Growers swap shade cloth for frost cloth with the season, and both keep insects off.' },
    marigold:  { id: 'marigold', kind: 'practice', name: 'Marigold Strip', layer: 'ground', scope: 'neighbours', cost: 30, helps: ['pest'],
                 effect: 'Pest growth -45% and new pests -50% in its plot and the 8 plots around it.',
                 desc: 'Companion planting. Marigold roots suppress nematodes and the flowers attract hoverflies and wasps that eat pests.' },

    shovel:    { id: 'shovel', kind: 'tool', name: 'Clear Plot', cost: 0, desc: 'Remove what is on a plot (no refund). Click again to remove the next layer, then the row\'s pipes.' },
  };

  // Stages. The NASA data picks the worst real window for each focus hazard.
  const LEVELS = [
    { id: 1, name: 'Stage 1 · Dry Spell', focus: 'drought', weeks: 6, suggest: 'central_valley', unlock: ['corn', 'drip', 'shovel'],
      intro: 'The NASA soil-moisture record shows the root zone drying out right when my corn needs it most. Plant corn, then lay drip lines under the rows so the soil never drops into the stress zone.',
      tip: 'Plant only as much as your water can carry. Drip shares the pond between every row it serves, so too many rows and the pond runs dry for all of them.' },
    { id: 2, name: 'Stage 2 · Dust in the Wind', focus: 'wind', weeks: 6, sow: 'seed', suggest: 'central_valley', unlock: ['wheat', 'cover', 'windbreak'],
      intro: 'It is sowing time, so the field is mostly bare soil and tiny seedlings. Dry, bare soil plus strong wind is how the Dust Bowl started. Keep the surface covered with rye, and plant windbreak trees on the windward edge so the plots behind them are sheltered.',
      tip: 'A windbreak shelters about 4 plots downwind. Put it in the right-most plot of a row, then plant crops to its left.' },
    { id: 3, name: 'Stage 3 · Washout', focus: 'flood', weeks: 7, suggest: 'mato_grosso', unlock: ['drain'],
      intro: 'The rain record for this window spikes. Saturated soil suffocates roots within days. Lay drainage tile under your rows and keep cover crops where water would pool.',
      tip: 'Heavy-rain weeks also refill your farm pond, so drip lines cost almost nothing to run right after a storm.' },
    { id: 4, name: 'Stage 4 · Heat Dome', focus: 'heat', weeks: 7, suggest: 'central_valley', unlock: ['tomato', 'mulch', 'rowcover'],
      intro: 'Daily highs climb past the point where tomato flowers drop. Shade the tomatoes with hoop covers, mulch the soil, and keep them watered so they can cool themselves.',
      tip: 'A watered crop runs 3 °C cooler than the air and a thirsty one 2 °C hotter. Drip plus mulch plus shade cloth is the full heat package for your most valuable crop.' },
    { id: 5, name: 'Stage 5 · Pest Pressure', focus: 'pest', weeks: 8, suggest: 'mato_grosso', unlock: ['soy', 'marigold'],
      intro: 'Warm, humid weeks breed insects that spread plot to plot. Plant marigold strips among the crops, and learn why soybeans make next season\'s corn stronger.',
      tip: 'One marigold protects the 8 plots around it. A checkerboard of marigolds every third plot covers a whole field.' },
    { id: 6, name: 'Final · A Full Year', focus: null, weeks: 52, rotation: true, sow: 'seed', unlock: [],
      intro: 'Plan a four-season crop rotation for every row within your budget and water allocation, then farm all 52 weeks of real NASA weather for your location.',
      tip: 'Rotate families. Legume then grass is a yield bonus. The same family twice brings pests. A cover-crop season rebuilds the soil.' },
  ];

  const ROTATION_CHOICES = [
    { id: 'corn', name: 'Corn' }, { id: 'wheat', name: 'Wheat' }, { id: 'soy', name: 'Soybean' }, { id: 'tomato', name: 'Tomato' },
    { id: 'cover', name: 'Cover crop (rye)' }, { id: 'fallow', name: 'Fallow (rest)' },
  ];

  // NASA data sources for the manual
  const DATASETS = [
    { name: 'NASA POWER (MERRA‑2 / GEOS‑IT)', used: 'LIVE: every day of weather in this game',
      what: 'Daily air temperature, rainfall, wind speed, humidity and root-zone soil wetness for any point on Earth, derived from NASA’s MERRA‑2 reanalysis and satellite radiation products. Free, no key, agriculture community endpoint.',
      url: 'https://power.larc.nasa.gov/' },
    { name: 'SMAP (Soil Moisture Active Passive)', used: 'Starting soil moisture (through POWER GWETROOT)',
      what: 'An L-band radiometer that maps the top 5 cm of soil moisture globally every 2–3 days. Its Level‑4 product estimates root-zone moisture, the best early-warning signal for agricultural drought.',
      url: 'https://smap.jpl.nasa.gov/' },
    { name: 'GPM IMERG', used: 'Heavy-rain and waterlogging context',
      what: 'Global Precipitation Measurement merges a constellation of satellites into half-hourly rainfall maps, the same kind of totals that drive waterlogging in the game.',
      url: 'https://gpm.nasa.gov/data/imerg' },
    { name: 'MODIS / VIIRS NDVI', used: 'Pest and erosion context',
      what: 'Vegetation greenness every 1–2 days. Sudden drops in NDVI flag pest outbreaks and bare soil that is vulnerable to wind erosion.',
      url: 'https://modis.gsfc.nasa.gov/data/dataprod/mod13.php' },
    { name: 'Landsat 8 / 9', used: 'Field-scale crop health (manual)',
      what: '30 m imagery every 8 days. Fine enough to see individual fields, used for crop-type mapping and irrigation scheduling.',
      url: 'https://landsat.gsfc.nasa.gov/' },
    { name: 'ECOSTRESS', used: 'Heat-stress context',
      what: 'A thermal radiometer on the ISS that measures plant temperature and evapotranspiration, showing which fields are stressed hours before leaves wilt.',
      url: 'https://ecostress.jpl.nasa.gov/' },
  ];

  const ROTATION_RULES = [
    'Same family in the same row two seasons in a row: pests start 3× higher and arrive 1.8× faster, yield ×0.85.',
    'Soybean (legume) followed by any crop: yield ×1.25 from the nitrogen it left in the soil.',
    'Cover crop followed by a cash crop: the soil holds 15% more water and yield ×1.10.',
    'Fallow earns nothing but clears pests from the row.',
    'A cover-crop season costs $10 a plot and protects the whole row from erosion and runoff.',
  ];

  // How the simulation works, for the manual
  const SIM_NOTES = [
    'Every in-game day uses one real day from the NASA POWER record for your location. A stage covers the worst real window for its hazard in the year you chose.',
    'Each plot holds up to 100 mm of plant-available water (less if topsoil erodes). Starting moisture comes from NASA root-zone wetness (GWETROOT).',
    'Water out: reference evapotranspiration from the FAO-56 Hargreaves equation, using NASA max, min and mean temperature and the sun angle for your latitude, adjusted for wind and humidity, times the crop coefficient.',
    'Water in: rain (minus runoff on heavy days) and drip irrigation. Water beyond what the soil holds saturates the plot until it drains.',
    'Topsoil matters: harvest is scaled by 40% plus 60% of the topsoil left, and eroded plots hold less water.',
    'Crop health starts at 100%. Drought, heat, frost, waterlogging, sandblasting and pests each subtract from it on the days they happen. Harvest pays health × topsoil × rotation bonus × the share of the season the crop was in the ground.',
    'Pass a stage by harvesting at least 60% of your potential yield. 75% earns two stars and 90% earns three.',
  ];

  return { LANES, COLS, LAYERS, REGIONS, HAZARDS, ITEMS, LEVELS, ROTATION_CHOICES, DATASETS, ROTATION_RULES, SIM_NOTES };
})();
