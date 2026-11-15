# Field Shift (Unity)

First-person farming game for the NASA Space Apps 2026 challenge **Field Shift: Adapting Farms with NASA Data**.
You choose a real place in British Columbia, inherit your grandmother's farm, and live through real NASA weather
from March 2021 (heat dome, atmospheric river) onward. You grow the farm by leasing neighbouring land, but
**reserve land is never for sale or lease**. Indigenous farming techniques are learned by visiting the local
First Nation's community office and listening, not taken.

## Open and play

1. Unity Hub → **Add project from disk** → this folder (`unity/FieldShift`). Editor: Unity 6 (6000.6.9f1).
2. Open `Assets/FieldShift/Scenes/FieldShift.unity` and press **Play**.
   If the scene is missing, run **Field Shift → Set Up Project** from the menu bar.

**How it plays (kept simple on purpose):**
- **Pick a place on the NASA Blue Marble map of BC** (click a pin or press 1-4, then Enter).
- **Plan from above.** The bird's-eye view shows your fields with their crop names. Click a field to choose what to grow, the cover crop for winter, irrigation, and who does the field work.
- **Work the land.** When a field is ready, a yellow PLANT or HARVEST tag appears. Hire a crew ($90 to plant, $140 to harvest per ha), or press T and drive the tractor over the field yourself: the seeder or harvester covers it strip by strip, and you save the fee.
- **Walk the farm** in first person (Tab) to see the crops, the river and the reserve boundary up close.
- **Compare (R)** shows four plans side by side as profit, water, nitrate lost and soil health. "Show details" has the evidence, risks and data gaps.

Controls: Tab plan/walk · click a field · T tractor (W/S drive, A/D steer, C cab view, E get out) · R compare · J notebook · Space pause · 1/2/3 speed.

## Working on it as a team

- Install **exactly Unity 6000.6.9f1** with the **WebGL Build Support** module (Unity Hub → Installs → Add modules).
  A different editor version will upgrade the project and cause merge conflicts.
- Clone the repo, then Unity Hub → **Add project from disk** → `unity/FieldShift`. The first open takes a few minutes
  while Unity rebuilds its `Library` cache (not in git).
- Most content lives in `Assets/FieldShift/Resources/FieldShift/catalog.json` and the C# scripts, which merge cleanly.
  Avoid editing the scene at the same time as someone else; the game builds its world from code, so you rarely need to.

## Build and host the web version

1. In Unity: **Field Shift → Build for Web** (or, with the editor closed:
   `Unity -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod FieldShift.EditorTools.WebBuild.Run -logFile Logs/webgl-build.log`).
   The playable site lands in `Builds/WebGL` (about 40 MB, not committed).
2. Try it locally: serve the folder with any static server (for example `python3 -m http.server -d Builds/WebGL 8000`)
   and open http://localhost:8000. Opening `index.html` straight from disk does not work.
3. Host it:
   - **itch.io** (simplest): zip the *contents* of `Builds/WebGL` (so `index.html` is at the top of the zip), create a
     project of kind HTML, upload the zip, tick "This file will be played in the browser", set the viewport to
     1280 x 720 and turn on the fullscreen button.
   - **GitHub Pages** (the live link, https://gordonmango.github.io/field-shift/): Pages serves the `gh-pages`
     branch, which holds only the contents of `Builds/WebGL` plus an empty `.nojekyll` file. To update it, replace
     that branch's files with a new build and push.
   The build uses gzip with decompression fallback, so it plays on both without special server settings.

The build script makes web builds use the Mobile quality level (the PC renderer's Forward+ and SSAO passes fail in
browsers) and keeps the shader variants the game enables at runtime. The game itself draws the terrain without
instancing on the web, where instanced terrain renders black. See `Assets/FieldShift/Scripts/Editor/WebBuild.cs`.

## What is real data

| What | Source | Where it is used |
|---|---|---|
| Daily max/min/mean temperature, rain, sun, humidity, wind, soil wetness (2019-2023) | NASA POWER daily API, AG community | Every simulated day, ET₀, story events, Rotation Report |
| Terrain (30 m) | NASADEM v001 (NASA/JPL reprocessed SRTM) via Microsoft Planetary Computer | 8 x 8 km terrain, field elevation, flood risk, slope |
| Reserve boundaries | Natural Resources Canada Aboriginal Lands (GeoBase import in OpenStreetMap) | Land you can never lease, signs, map |
| Water | OpenStreetMap | Rivers, ditches, ponds |
| Ground textures | ambientCG, CC0 | Terrain |

NASA POWER describes a ~50 km grid cell whose average height can be far above the valley floor (Sumas: 677 m
cell vs. ~5 m fields). Temperatures are shifted to each farm's NASADEM elevation at 4 °C/km. With that
correction the Sumas data peaks at 43 °C on 28 June 2021, close to what Abbotsford recorded in the heat dome.

**Teaching estimates, not data:** crop prices, costs, yields, lease rates and soil properties
(`Assets/FieldShift/Resources/FieldShift/catalog.json`). Replace them with Statistics Canada prices, BC Ministry
of Agriculture enterprise budgets, FCC farmland values and the BC Soil Information Finder Tool.

**Indigenous content is a draft.** Every technique and acknowledgement in `catalog.json` is marked
`DRAFT: not yet reviewed`. It is based on public sources and must be reviewed and approved by the Semá:th First
Nation, Osoyoos Indian Band / Okanagan Nation Alliance, Cowichan Tribes and Treaty 8 First Nations before release.

## Art pipeline (all free, CC0 or built here)

| Asset | Where it comes from | Rebuild |
|---|---|---|
| Tractor, seeder, harvester, barn, farmhouse, grain bins | Modelled by script in Blender 5 (generic designs, no brand liveries) | `/Applications/Blender.app/Contents/MacOS/Blender -b -P Tools/blender/build_models.py -- "$PWD/Assets/FieldShift/Resources/FieldShift/Models"` (add `tractor` etc. to rebuild one) |
| Sky and ambient light | Poly Haven HDRI *Kloofendal 48d Partly Cloudy (Pure Sky)*, CC0. The sun light is placed where the sun is in the photo. | `Resources/FieldShift/Sky/` |
| Grass, crops, tree foliage | Cards composited from ambientCG atlases (Foliage001, LeafSet019, LeafSet024) plus drawn wheat | `uv run Tools/build_foliage.py <unzipped ambientCG folder>` |
| Ground, bark, siding, roof steel, shingles | ambientCG CC0 photo textures | see `Resources/FieldShift/Textures/LICENSE-ambientCG.txt` |

The game assigns URP materials to model parts by name (Paint, Glass, Tire, Siding, Roof...), so a modeller can replace any FBX in `Resources/FieldShift/Models` with a higher-detail one that uses the same part and material names.

## Rebuild the location data

```bash
uv run Tools/build_location_data.py            # all four locations
uv run Tools/build_location_data.py "" sumas --mask-only
```

Add a location by appending to `LOCATIONS` in `Tools/build_location_data.py` and to `locations` in `catalog.json`.

## Code map

| File | Role |
|---|---|
| `Scripts/Sim/FarmSim.cs` | Daily soil-water balance (FAO-56 Hargreaves), nitrogen, organic matter, erosion, pests, floods, irrigation, money |
| `Scripts/Sim/RotationReport.cs` | Replays a year under the common rotation, your plan, break crop + cover crop, and Indigenous knowledge |
| `Scripts/Data/LocationData.cs` | Loads the data, picks the farm grid beside reserve land, applies the elevation correction |
| `Scripts/World/World.cs` | Terrain, patchwork fields, trees (LOD cells), water, flood, buildings, signs, near-field crops |
| `Scripts/Game/GameManager.cs` | Location choice, time, story beats, leasing, community visits |
| `Scripts/Game/GameUI.cs` | All screens (IMGUI): BC map menu, HUD, field card, report |
| `Scripts/Game/Tractor.cs` | Drivable tractor with seeder and harvester; coverage completes field jobs |
| `Scripts/Editor/FieldShiftTools.cs` | Setup, headless simulation test, batch-mode screenshots |

UI check inside the editor: **Field Shift → Run UI Tour** plays through the screens and saves screenshots to `Logs/ui-tour`.

Headless checks (close the project in the editor first):

```bash
Unity -batchmode -nographics -projectPath . -executeMethod FieldShift.EditorTools.FieldShiftTools.BatchSimTest -quit
Unity -batchmode -projectPath . -executeMethod FieldShift.EditorTools.FieldShiftTools.BatchCapture -fsLocation sumas -fsOut Logs/shots
```

## Built with AI assistance

Our team chose the challenge and set the concept and design rules: a crop-rotation game in British Columbia, a real
location you choose, reserve land that can never be leased, Indigenous techniques that are learned rather than taken,
cover crops, soil moisture and irrigation, a farmer who must stay solvent, and June's story.

Much of the code in this repository was then written with **Claude (Anthropic) through Claude Code**, following those
rules, and reviewed and play-tested by the team. For Field Shift that includes the Unity C# game and simulation, the
Python data scripts in `unity/FieldShift/Tools`, the Blender Python scripts that build the 3D models, the web build,
and parts of this documentation.

No image, video or audio generation model was used. The 3D models are built by Blender scripts, and the textures and
sky are CC0 photographs (ambientCG, Poly Haven). This note is here because the NASA Space Apps rules ask projects to
identify SI-assisted code.

## Claude Code bridge

The project includes the `MCP for Unity` package. On first open it switches the bridge to stdio so Claude Code's
`UnityMCP` server can drive the editor.
