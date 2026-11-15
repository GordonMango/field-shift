# Defend Your Farm — A NASA-Powered Climate Survival Game

A farming simulation for the NASA Space Apps Challenge. Every in-game day replays one real day of
NASA POWER weather for the farm location the player chooses. Drought, heat, wind erosion,
waterlogging, pests and frost are not creatures: they are conditions computed from that weather,
and each plot feels them through its own soil water, topsoil, cover and shelter. The player protects
the field with the practices real farmers use: drip irrigation, straw mulch, cover crops, windbreaks,
drainage tile, hoop covers (shade cloth and frost cloth), companion planting and crop rotation.

## Run it

No build step. Serve the folder with any static server and open it in a browser:

```bash
python3 -m http.server 8080
```

Then visit http://localhost:8080. Opening `index.html` directly also works in most browsers.
Internet access is needed for Three.js (CDN) and the live NASA POWER request; if NASA POWER
cannot be reached, the game falls back to a built-in latitude-aware climatology and says so.

Add `?all=1` to the URL to unlock every stage (useful for judging).

## How the simulation works

- **Soil water balance per plot.** Rain soaks in (minus runoff on heavy days). The crop transpires water
  out using FAO-56 Hargreaves reference evapotranspiration from NASA max, min and mean temperature and
  the sun angle for the latitude, adjusted for wind and humidity, times a crop coefficient. Starting
  moisture comes from NASA root-zone wetness (GWETROOT).
- **Drought:** below 50% of plant-available water the crop loses health. Drip lines refill a row's
  planted plots from the per-season water allocation.
- **Heat:** canopy temperature is the NASA daily maximum, 3 to 6 °C cooler if the crop is well watered
  and 2 °C hotter if it is thirsty. Shade cloth and mulch lower it further. Above the crop threshold
  (wheat and tomato 32 °C, corn and soybean 35 °C) health drops.
- **Frost:** the NASA daily minimum against each crop's frost threshold. Frost cloth adds about 3 °C.
- **Waterlogging:** water beyond field capacity saturates the plot and drains slowly (3 to 8 mm a day,
  35 with drainage tile). Roots suffer after two saturated days.
- **Wind erosion:** on days above the location's erosive wind speed, dry bare soil loses topsoil and
  seedlings are sandblasted. Windbreaks slow the wind for 4 plots downwind; cover crops and mulch hold
  the surface. Lost topsoil holds less water and lowers the harvest.
- **Pests:** a population per plot that grows in warm, humid weather, spreads to neighbours and builds up
  when a crop family is repeated. Marigold strips and hoop covers slow it.
- **Harvest** = crop health × topsoil × rotation bonus × share of the season in the ground. A stage passes
  at 60% of potential yield, with stars at 75% and 90%.

## How it maps to the project description

| Criterion | Where it lives |
|---|---|
| Farmers input budget and water availability | Setup screen sliders. Water is a per-season allocation stored in the farm pond and spent by drip lines. The rotation planner estimates each season's irrigation need from the NASA data. |
| Each stage introduces a practice for one issue | `js/content.js` → `LEVELS`. Stage 1 drought → drip line, Stage 2 wind → cover crop and windbreak, Stage 3 waterlogging → drainage tile, Stage 4 heat → mulch and hoop cover, Stage 5 pests → marigold and soybean. |
| In-game farming manual | Manual button: weather hazards, crops and practices with their effects, NASA datasets, and how the simulation works. |
| Final stage: own crop rotation, full in-game year | Stage 6 rotation planner (5 rows × 4 seasons) then 364 real days of weather. |
| Three-dimensional simulation with underground / ground / sky layers | `js/scene.js` (Three.js). Drip lines and drainage tile run under each row inside a translucent soil block, crops and mulch on the surface, hoop covers above. X-ray toggle shows underground. Soil colour follows each plot's moisture, puddles show saturation, pests gather on infested plots, crops brown as they lose health. |
| NASA data in real time | `js/nasa.js` fetches T2M, T2M_MAX, T2M_MIN, PRECTOTCORR, WS2M, WD2M, RH2M and GWETROOT from the NASA POWER API and picks each stage's worst real growing-season window. A 7-day forecast panel shows the coming days. |
| Unpredictable real-world scenarios | Every location and year plays differently. When a hazard is mild at the chosen place, the stage intro says so and offers a region where it is severe. |

## Art style

Retro pixel art. The 3D field is rendered by Three.js at 30% resolution and upscaled with
nearest-neighbour sampling, so it reads as chunky pixel art while staying a real 3D scene with
underground / ground / sky layers. Every icon (crops, practices, hazards, the farmer guide "Hank") is a
hand-drawn character map in `js/pixel.js` rendered to a canvas at runtime, so there are no image
assets to ship. UI uses the Press Start 2P and VT323 fonts with CRT scanlines.

## Tech

- Vanilla HTML / CSS / JavaScript, no framework, no build.
- Three.js r128 for the 3D field (pixelated low-res render).
- NASA POWER API (agriculture community, daily point endpoint). No API key.
- Hazard design modelled on SMAP (soil moisture), GPM IMERG (precipitation), MODIS NDVI, Landsat and ECOSTRESS.

## Files

- `index.html` — screens, HUD, modals.
- `css/style.css` — styling.
- `js/content.js` — hazards, crops, practices, stages, regions, manual text.
- `js/pixel.js` — pixel sprite maps and the canvas renderer that turns them into icons.
- `js/nasa.js` — NASA POWER fetch, weekly indices, FAO-56 evapotranspiration, fallback climatology.
- `js/scene.js` — Three.js scene, low-poly models, soil state and weather particles.
- `js/game.js` — daily field simulation, placement, harvest, rotation planner, UI.
