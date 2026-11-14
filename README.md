# Defend Your Farm — A NASA-Powered Climate Survival Game

A Plants-vs-Zombies-style lane-defense game for the NASA Space Apps Challenge.
The zombies are climate threats (drought, heatwave, wind erosion, flash flood, pests, frost).
The plants are real farming practices (drip irrigation, cover crops, windbreaks, drainage tile,
mulch, row covers, companion planting, crop rotation). Every week of in-game weather comes from
NASA POWER daily data for the farm location the player chooses.

## Run it

No build step. Serve the folder with any static server and open it in a browser:

```bash
python3 -m http.server 8080
```

Then visit http://localhost:8080. Opening `index.html` directly also works in most browsers.
Internet access is needed for Three.js (CDN) and the live NASA POWER request; if NASA POWER
cannot be reached, the game falls back to a built-in latitude-aware climatology and says so.

Add `?all=1` to the URL to unlock every stage (useful for judging).

## How it maps to the project description

| Criterion | Where it lives |
|---|---|
| Farmers input budget and water availability | Setup screen sliders. Each stage starts with those values; the final stage plans rotation against them. |
| Each stage introduces a plant or tool for one issue | `js/content.js` → `LEVELS`. Stage 1 drought → drip irrigation, Stage 2 wind → cover crop & windbreak, Stage 3 flood → drainage, Stage 4 heat → mulch & row cover, Stage 5 pests → marigold & soybean. |
| In-game farming manual | 📖 Manual button: threats, plants & tools, NASA datasets, rotation rules. |
| Final stage: own crop rotation, full in-game year | Stage 6 rotation planner (5 lanes × 4 seasons) then 52 weeks of real weather. |
| Three-dimensional simulation with underground / ground / sky layers | `js/scene.js` (Three.js). Irrigation and drainage sit inside a translucent soil block, crops on the tiles, row covers above. X-ray toggle shows underground. |
| NASA data in real time | `js/nasa.js` fetches T2M, T2M_MAX, T2M_MIN, PRECTOTCORR, WS2M, RH2M, GWETROOT from the NASA POWER API, aggregates to weeks, converts to threat indices and picks each stage's real-world window (worst drought weeks, windiest weeks, etc.). |
| Unpredictable real-world scenarios | Spawns are drawn from the weekly indices, so a wet year in Iowa plays differently from a dry year in Kenya. |

## Art style

Retro pixel art. The 3D field is rendered by Three.js at 30% resolution and upscaled with
nearest-neighbour sampling, so it reads as chunky pixel art while staying a real 3D scene with
underground / ground / sky layers. Every icon (crops, tools, threats, the farmer guide "Hank") is a
hand-drawn character map in `js/pixel.js` rendered to a canvas at runtime, so there are no image
assets to ship. UI uses the Press Start 2P and VT323 fonts with CRT scanlines.

## Tech

- Vanilla HTML / CSS / JavaScript, no framework, no build.
- Three.js r128 for the 3D field (pixelated low-res render).
- NASA POWER API (agriculture community, daily point endpoint). No API key.
- Threat design modelled on SMAP (soil moisture), GPM IMERG (precipitation), MODIS NDVI, Landsat and ECOSTRESS.

## Files

- `index.html` — screens, HUD, modals.
- `css/style.css` — styling.
- `js/content.js` — crops, defenses, threats, stages, regions, manual text. Tune balance here.
- `js/pixel.js` — pixel sprite maps and the canvas renderer that turns them into icons.
- `js/nasa.js` — NASA POWER fetch, weekly indices, fallback climatology.
- `js/scene.js` — Three.js scene and low-poly models.
- `js/game.js` — game loop, placement, spawning, rotation planner, UI.
