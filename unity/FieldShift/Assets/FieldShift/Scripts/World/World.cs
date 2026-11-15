using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldShift
{
    /// <summary>
    /// Builds the 8 x 8 km world for a location from real data: NASADEM terrain, reserve and water masks,
    /// and the 2 x 2 km parcel grid the farm lives on. Draws trees, crops and grass with GPU instancing.
    /// </summary>
    public class World : MonoBehaviour
    {
        public LocationData data;
        public FarmSim sim;
        public Terrain terrain;
        public Transform player;
        public Vector3 housePos, officePos, barnPos;
        public Tractor tractor;
        public readonly Dictionary<int, bool[]> coverage = new Dictionary<int, bool[]>();
        GameObject overlay;
        Mesh overlayMesh;
        Color[] overlayColors;
        public GameObject office;
        public GameObject floodPlane;

        TerrainData td;
        float[,,] baseAlpha;
        int alphaRes;
        readonly List<InstanceSet> staticSets = new List<InstanceSet>();
        InstanceSet fence, boundaryPosts;
        NearField near;
        LineRenderer highlight;

        // ------------------------------------------------------------------ build
        public void Build(LocationData d, FarmSim s)
        {
            data = d; sim = s;
            BuildTerrain();
            PaintBase();
            BuildWater();
            BuildTrees();
            BuildFarmyard();
            BuildReserveMarkers();
            BuildHighlight();
            BuildOverlay();
            RefreshFences();
            tractor = Tractor.Build(this, barnPos + new Vector3(14, 0, -10), 90);
            near = new NearField(this);
            RefreshParcels();
        }

        public float H(float x, float z) => terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;

        void BuildTerrain()
        {
            var meta = data.heightMeta;
            td = new TerrainData { heightmapResolution = meta.resolution };
            td.size = new Vector3(meta.sizeMeters, meta.heightRangeMeters, meta.sizeMeters);
            td.SetHeights(0, 0, data.heights01);
            alphaRes = 1024;
            td.alphamapResolution = alphaRes;
            td.baseMapResolution = 1024;

            Color grassA = Color.Lerp(new Color(0.24f, 0.36f, 0.12f), new Color(0.55f, 0.5f, 0.3f), data.def.dryGrass);
            Color grassB = Color.Lerp(new Color(0.36f, 0.46f, 0.18f), new Color(0.68f, 0.6f, 0.38f), data.def.dryGrass);
            td.terrainLayers = new[]
            {
                Layer("Grass", grassA, grassB, 4, 1, 6f),
                Layer("Soil", new Color(0.24f, 0.18f, 0.12f), new Color(0.36f, 0.27f, 0.18f), 3, 2, 4f),
                Layer("Rock", new Color(0.36f, 0.35f, 0.33f), new Color(0.52f, 0.5f, 0.47f), 6, 3, 12f),
                Layer("ForestFloor", new Color(0.16f, 0.17f, 0.09f), new Color(0.28f, 0.24f, 0.14f), 5, 4, 5f),
            };
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain (NASADEM)";
            go.transform.position = new Vector3(-meta.sizeMeters / 2, meta.datumMeters, -meta.sizeMeters / 2);
            terrain = go.GetComponent<Terrain>();
            var tm = Mats.Terrain();
            if (tm != null) terrain.materialTemplate = tm;
            terrain.heightmapPixelError = 3;
            terrain.basemapDistance = 1200;
            // instanced terrain reads its normals from a texture generated at runtime, which renders the ground black in browsers
            terrain.drawInstanced = Application.platform != RuntimePlatform.WebGLPlayer;
        }

        TerrainLayer Layer(string name, Color a, Color b, float scale, int seed, float tile)
        {
            var photo = TexGen.Load(name + "_Color");
            var color = photo != null ? photo : TexGen.Noise(name, a, b, scale, seed);
            var l = new TerrainLayer { name = name, diffuseTexture = color, tileSize = new Vector2(tile, tile) };
            var normal = TexGen.Load(name + "_Normal");
            if (normal != null) { l.normalMapTexture = normal; l.normalScale = 1; }
            if (name == "Grass" && TexGen.Load(name + "_Color") != null)
                l.diffuseRemapMax = Color.Lerp(new Vector4(1, 1, 1, 1), new Vector4(1.25f, 1.05f, 0.7f, 1), data.def.dryGrass);
            // URP terrain smoothness = layer smoothness x diffuse alpha. The photos have no alpha (reads as 1), the noise
            // fallback stores 0.06 in alpha. Nothing reads pixels on the CPU, which compressed textures in web builds can't do.
            l.smoothness = photo != null ? 0.06f : 1f;
            l.metallic = 0;
            return l;
        }

        Vector2 AlphaToLocal(int ax, int az)
        {
            float size = data.heightMeta.sizeMeters;
            return new Vector2(-size / 2 + (ax + 0.5f) / alphaRes * size, -size / 2 + (az + 0.5f) / alphaRes * size);
        }

        void PaintBase()
        {
            baseAlpha = new float[alphaRes, alphaRes, 4];
            float lowland = sim.floodBase;
            for (int az = 0; az < alphaRes; az++)
                for (int ax = 0; ax < alphaRes; ax++)
                {
                    var p = AlphaToLocal(ax, az);
                    float steep = td.GetSteepness((ax + 0.5f) / alphaRes, (az + 0.5f) / alphaRes);
                    float h = data.ElevationAt(p.x, p.y);
                    float forest = ForestValue(p.x, p.y, steep, h, lowland);
                    float rock = Mathf.Clamp01((steep - 32) / 10f);
                    float soil = data.WaterAt(p.x, p.y) ? 0.8f : Mathf.Clamp01(Mathf.PerlinNoise(p.x * 0.01f, p.y * 0.01f) - 0.75f) * 2;
                    if (steep < 5 && forest < 0.05f && !data.WaterAt(p.x, p.y)) soil = Mathf.Max(soil, NeighbourFieldSoil(p.x, p.y));
                    float fo = (1 - rock) * forest;
                    float g = Mathf.Max(0, 1 - rock - fo - soil);
                    float sum = g + soil + rock + fo;
                    baseAlpha[az, ax, 0] = g / sum; baseAlpha[az, ax, 1] = soil / sum; baseAlpha[az, ax, 2] = rock / sum; baseAlpha[az, ax, 3] = fo / sum;
                }
            td.SetAlphamaps(0, 0, baseAlpha);
        }

        /// <summary>Other farms' fields: a patchwork of tilled soil, crops and hay in 100 m to 200 m blocks.</summary>
        float NeighbourFieldSoil(float x, float z)
        {
            int fx = Mathf.FloorToInt(x / 160f), fz = Mathf.FloorToInt(z / 110f);
            uint h = (uint)(fx * 73856093 ^ fz * 19349663 ^ data.def.id.GetHashCode());
            float kind = (h & 255) / 255f;
            float season = sim != null ? Mathf.Sin((sim.Today.doy - 100) / 365f * Mathf.PI * 2) : 0.5f;   // greener in summer
            if (kind < 0.35f) return 0.85f - 0.5f * Mathf.Clamp01(season);       // tilled field, crop later in summer
            if (kind < 0.5f) return 0.55f;                                         // stubble
            return 0.08f;                                                          // hay and pasture
        }

        public float ForestValue(float x, float z, float steep, float h, float lowland)
        {
            if (InsideGrid(x, z, 0) && !SteepOrReserveAt(x, z)) return 0;
            if (data.WaterAt(x, z)) return 0;
            float n = Mathf.PerlinNoise(x * 0.004f + 31, z * 0.004f + 17);
            float up = Mathf.Clamp01((h - lowland - 25) / 60f);
            float sl = Mathf.Clamp01((steep - 6) / 14f);
            float hills = Mathf.Max(up, sl);
            // valley floors are farmland: only occasional woodlots, no trees scattered across fields
            float woodlot = n > 0.7f ? (n - 0.7f) * 5f : 0;
            float v = hills > 0.05f ? hills * 0.95f + (n - 0.5f) * 0.6f : woodlot;
            return Mathf.Clamp01(v * (0.4f + 0.6f * data.def.treeDensity));
        }

        bool InsideGrid(float x, float z, float margin)
        {
            float half = FarmSim.Grid * Parcel.SizeM / 2 + margin;
            return Mathf.Abs(x - sim.gridCenter.x) < half && Mathf.Abs(z - sim.gridCenter.y) < half;
        }

        bool SteepOrReserveAt(float x, float z)
        {
            var p = ParcelAt(new Vector3(x, 0, z));
            return p != null && (p.status == LandStatus.Steep || (p.status == LandStatus.Reserve && Mathf.PerlinNoise(x * 0.01f, z * 0.01f) > 0.6f));
        }

        public Parcel ParcelAt(Vector3 world)
        {
            float half = FarmSim.Grid * Parcel.SizeM / 2;
            int ix = Mathf.FloorToInt((world.x - sim.gridCenter.x + half) / Parcel.SizeM), iz = Mathf.FloorToInt((world.z - sim.gridCenter.y + half) / Parcel.SizeM);
            return sim.At(ix, iz);
        }

        // ---------------------------------------------------------------- parcels painted by crop state
        public void RefreshParcels() => Repaint(null);
        public void RefreshParcel(Parcel only) => Repaint(only);

        void Repaint(Parcel only)
        {
            float size = data.heightMeta.sizeMeters;
            float half = FarmSim.Grid * Parcel.SizeM / 2;
            float x0 = only != null ? only.x - Parcel.SizeM / 2 : sim.gridCenter.x - half;
            float z0 = only != null ? only.z - Parcel.SizeM / 2 : sim.gridCenter.y - half;
            float span = only != null ? Parcel.SizeM : 2 * half;
            int ax0 = Mathf.Clamp(Mathf.FloorToInt((size / 2 + x0) / size * alphaRes), 0, alphaRes - 1);
            int az0 = Mathf.Clamp(Mathf.FloorToInt((size / 2 + z0) / size * alphaRes), 0, alphaRes - 1);
            int n = Mathf.Min(Mathf.CeilToInt(span / size * alphaRes) + 1, alphaRes - Mathf.Max(ax0, az0));
            var alpha = new float[n, n, 4];
            for (int az = 0; az < n; az++)
                for (int ax = 0; ax < n; ax++)
                {
                    var lp = AlphaToLocal(ax0 + ax, az0 + az);
                    var p = ParcelAt(new Vector3(lp.x, 0, lp.y));
                    for (int k = 0; k < 4; k++) alpha[az, ax, k] = baseAlpha[az0 + az, ax0 + ax, k];
                    if (p == null) continue;
                    float lx = lp.x - (p.x - Parcel.SizeM / 2), lz = lp.y - (p.z - Parcel.SizeM / 2);
                    bool border = lx < 5 || lz < 5 || lx > Parcel.SizeM - 5 || lz > Parcel.SizeM - 5;
                    float g = -1, soil = 0;
                    if (border && (p.IsFarm || p.status == LandStatus.Available)) { g = 0.88f; soil = 0.12f; }        // grass verges
                    else if (p.IsFarm)
                    {
                        var c = p.current;
                        float green = c == null ? 0 : c.dead ? 0.15f : Mathf.Clamp01(c.Canopy) * (c.def.perennial || c.def.IsCover ? 1f : 0.85f);
                        if (p.job != null && Covered(p, lp.x, lp.y)) green = p.job == "plant" ? 0f : 0.35f;         // freshly worked strips
                        g = green; soil = 1 - green;
                    }
                    else if (p.status == LandStatus.Available) { soil = NeighbourFieldSoil(p.x, p.z); g = 1 - soil; }
                    if (g >= 0) { alpha[az, ax, 0] = g; alpha[az, ax, 1] = soil; alpha[az, ax, 2] = 0; alpha[az, ax, 3] = 0; }
                }
            td.SetAlphamaps(ax0, az0, alpha);
            if (only != null) { near?.MarkDirty(); return; }
            if (floodPlane != null)
            {
                bool flooded = sim.floodLevel > 0;
                floodPlane.SetActive(flooded);
                if (flooded) floodPlane.transform.position = new Vector3(sim.gridCenter.x, sim.floodLevel, sim.gridCenter.y);
            }
            near?.MarkDirty();
        }

        // ---------------------------------------------------------------- tractor coverage
        int CellOf(Parcel p, float x, float z)
        {
            int cx = Mathf.FloorToInt((x - (p.x - Parcel.SizeM / 2)) / (Parcel.SizeM / Tractor.Cells));
            int cz = Mathf.FloorToInt((z - (p.z - Parcel.SizeM / 2)) / (Parcel.SizeM / Tractor.Cells));
            if (cx < 0 || cz < 0 || cx >= Tractor.Cells || cz >= Tractor.Cells) return -1;
            return cz * Tractor.Cells + cx;
        }

        public bool Covered(Parcel p, float x, float z)
        {
            if (!coverage.TryGetValue(p.index, out var cov)) return false;
            int c = CellOf(p, x, z);
            return c >= 0 && cov[c];
        }

        /// <summary>Mark the cells under the implement. Returns true if anything new was covered.</summary>
        public bool MarkCoverage(Parcel p, Vector3 centre, Vector3 right)
        {
            if (!coverage.TryGetValue(p.index, out var cov)) coverage[p.index] = cov = new bool[Tractor.Cells * Tractor.Cells];
            bool any = false;
            for (int i = 0; i <= 4; i++)
            {
                var q = centre + right * Mathf.Lerp(-Tractor.ImplementWidth / 2, Tractor.ImplementWidth / 2, i / 4f);
                int c = CellOf(p, q.x, q.z);
                if (c >= 0 && !cov[c]) { cov[c] = true; any = true; }
            }
            return any;
        }

        public float CoverageOf(Parcel p)
        {
            if (!coverage.TryGetValue(p.index, out var cov)) return 0;
            int n = 0; foreach (var b in cov) if (b) n++;
            return n / (float)cov.Length;
        }

        public void ClearCoverage(Parcel p) => coverage.Remove(p.index);

        // ---------------------------------------------------------------- bird's-eye planning overlay
        void BuildOverlay()
        {
            overlay = new GameObject("Plan overlay");
            const int V = OverlayV;                             // vertices per side, draped on the terrain
            var verts = new List<Vector3>(); var tris = new List<int>();
            foreach (var p in sim.parcels)
            {
                int start = verts.Count;
                float inset = 3f, w = Parcel.SizeM - 2 * inset;
                for (int j = 0; j < V; j++)
                    for (int i = 0; i < V; i++)
                    {
                        float x = p.x - w / 2 + w * OverlayT[i], z = p.z - w / 2 + w * OverlayT[j];
                        verts.Add(new Vector3(x, H(x, z) + 0.8f, z));
                    }
                for (int j = 0; j < V - 1; j++)
                    for (int i = 0; i < V - 1; i++)
                    {
                        int a = start + j * V + i;
                        tris.AddRange(new[] { a, a + V, a + V + 1, a, a + V + 1, a + 1 });
                    }
            }
            overlayMesh = new Mesh { name = "PlanOverlay", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            overlayMesh.SetVertices(verts); overlayMesh.SetTriangles(tris, 0); overlayMesh.RecalculateBounds();
            overlayColors = new Color[verts.Count];
            overlay.AddComponent<MeshFilter>().sharedMesh = overlayMesh;
            var mr = overlay.AddComponent<MeshRenderer>();
            var mat = Resources.Load<Material>("FieldShift/Materials/Overlay");
            mr.sharedMaterial = mat != null ? new Material(mat) : new Material(Shader.Find("Sprites/Default"));
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            overlay.SetActive(false);
        }

        // uneven spacing: a thin outer ring makes a crisp rim, the rest follows the ground
        const int OverlayV = 7;
        static readonly float[] OverlayT = { 0f, 0.035f, 0.25f, 0.5f, 0.75f, 0.965f, 1f };

        public void ShowOverlay(bool on) { if (overlay != null) overlay.SetActive(on); }

        /// <summary>Colour each field: a soft fill and a brighter rim so field edges read clearly from above.</summary>
        public void ColourOverlay(System.Func<Parcel, (Color fill, Color rim)> colour)
        {
            if (overlayMesh == null) return;
            const int V = OverlayV;
            foreach (var p in sim.parcels)
            {
                var (fill, rim) = colour(p);
                for (int j = 0; j < V; j++)
                    for (int i = 0; i < V; i++)
                    {
                        bool edge = i == 0 || j == 0 || i == V - 1 || j == V - 1;
                        overlayColors[p.index * V * V + j * V + i] = edge ? rim : fill;
                    }
            }
            overlayMesh.colors = overlayColors;
        }

        // ---------------------------------------------------------------- water
        void BuildWater()
        {
            var cells = new List<Vector3>();
            int g = data.maskMeta.grid; float cell = data.maskMeta.cellMeters, half = data.HalfSize;
            for (int iz = 0; iz < g; iz++)
                for (int ix = 0; ix < g; ix++)
                {
                    if (data.waterGrid[iz * g + ix] == 0) continue;
                    float x = -half + (ix + 0.5f) * cell, z = -half + (iz + 0.5f) * cell;
                    float h = Mathf.Min(H(x - 8, z - 8), H(x + 8, z + 8), H(x + 8, z - 8), H(x - 8, z + 8));
                    cells.Add(new Vector3(x, h + 0.35f, z));
                }
            var waterMat = Mats.Water(new Color(0.16f, 0.27f, 0.3f, 0.82f));
            for (int start = 0; start < cells.Count; start += 4000)
            {
                var go = new GameObject("Water (OpenStreetMap)");
                go.AddComponent<MeshFilter>().sharedMesh = ProcMesh.WaterCells(cells.GetRange(start, Mathf.Min(4000, cells.Count - start)), cell + 1);
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = waterMat; mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            floodPlane = new GameObject("Flood water");
            floodPlane.AddComponent<MeshFilter>().sharedMesh = ProcMesh.Plane(FarmSim.Grid * Parcel.SizeM + 3000);
            var fr = floodPlane.AddComponent<MeshRenderer>(); fr.sharedMaterial = Mats.Water(new Color(0.33f, 0.3f, 0.22f, 0.86f)); fr.shadowCastingMode = ShadowCastingMode.Off;
            floodPlane.SetActive(false);
        }

        // ---------------------------------------------------------------- trees
        /// <summary>Trees grouped in 600 m cells; near cells draw detailed meshes with shadows, far cells cheap cones.</summary>
        class TreeCell
        {
            public Vector3 centre;
            public InstanceSet[] hi, lo;
        }
        readonly List<TreeCell> treeCells = new List<TreeCell>();

        void BuildTrees()
        {
            var trunk = Mats.Lit(new Color(0.24f, 0.17f, 0.12f));
            var conMat = Mats.Lit(new Color(0.11f, 0.2f, 0.11f), 0.1f);
            var broadMat = Mats.Lit(Color.Lerp(new Color(0.22f, 0.34f, 0.14f), new Color(0.4f, 0.42f, 0.2f), data.def.dryGrass), 0.1f);
            var bark = Mats.Bark();
            var firTex = TexGen.Load("Foliage_FirBranch"); var leafTex = TexGen.Load("Foliage_LeafCluster");
            bool cards = firTex != null && leafTex != null;
            var firCards = Mats.Foliage(firTex, new Color(0.62f, 0.78f, 0.62f));
            var leafCards = Mats.Foliage(leafTex, Color.Lerp(new Color(0.85f, 0.95f, 0.8f), new Color(0.95f, 0.9f, 0.6f), data.def.dryGrass));
            Mesh[] hiMesh = cards ? new[] { ProcMesh.FirTree(1), ProcMesh.FirTree(2), ProcMesh.LeafTree(3) } : new[] { ProcMesh.Conifer(1), ProcMesh.Conifer(2), ProcMesh.Broadleaf(3) };
            Mesh[] loMesh = { ProcMesh.ConiferLow(), ProcMesh.ConiferLow(), ProcMesh.BroadleafLow() };
            Material[][] mats = { new[] { trunk, conMat }, new[] { trunk, conMat }, new[] { trunk, broadMat } };
            Material[][] hiMats = cards ? new[] { new[] { bark, firCards }, new[] { bark, firCards }, new[] { bark, leafCards } } : mats;
            var cells = new Dictionary<Vector2Int, TreeCell>();
            TreeCell CellAt(Vector3 p)
            {
                var k = new Vector2Int(Mathf.FloorToInt(p.x / 600f), Mathf.FloorToInt(p.z / 600f));
                if (!cells.TryGetValue(k, out var c))
                {
                    c = new TreeCell { centre = new Vector3(k.x * 600 + 300, p.y, k.y * 600 + 300), hi = new InstanceSet[3], lo = new InstanceSet[3] };
                    for (int i = 0; i < 3; i++)
                    {
                        c.hi[i] = new InstanceSet(hiMesh[i], hiMats[i]);
                        c.lo[i] = new InstanceSet(loMesh[i], mats[i]) { shadows = ShadowCastingMode.Off };
                    }
                    cells[k] = c;
                }
                return c;
            }
            var shrubs = cards ? new InstanceSet(ProcMesh.Cards(3, 1.6f, 1.1f, true, 4), leafCards) { shadows = ShadowCastingMode.Off }
                               : new InstanceSet(ProcMesh.Shrub(4), broadMat) { shadows = ShadowCastingMode.Off };
            float size = data.heightMeta.sizeMeters, half = size / 2, lowland = sim.floodBase;
            float step = 13f;
            float broadShare = data.def.id == "peace" ? 0.5f : data.def.id == "oliver" ? 0.1f : 0.22f;
            var rnd = new System.Random(data.def.id.GetHashCode());
            int count = 0;
            for (float z = -half + 20; z < half - 20; z += step)
                for (float x = -half + 20; x < half - 20; x += step)
                {
                    float jx = x + (float)(rnd.NextDouble() - 0.5) * step, jz = z + (float)(rnd.NextDouble() - 0.5) * step;
                    float steep = td.GetSteepness((jx + half) / size, (jz + half) / size);
                    if (steep > 38) continue;
                    float h = H(jx, jz);
                    float f = ForestValue(jx, jz, steep, h, lowland);
                    if (rnd.NextDouble() > f * 0.85f) continue;
                    float height = 14 + (float)rnd.NextDouble() * 22 * Mathf.Clamp01(0.5f + f);
                    float yaw = (float)rnd.NextDouble() * 360;
                    var pos = new Vector3(jx, h - 0.3f, jz);
                    var cell = CellAt(pos);
                    int kind = rnd.NextDouble() < broadShare ? 2 : (count & 1);
                    var scale = kind == 2 ? Vector3.one * height * 0.75f : Vector3.one * height;
                    cell.hi[kind].Add(pos, yaw, scale);
                    cell.lo[kind].Add(pos, yaw, scale);
                    if (rnd.NextDouble() < 0.15 && (pos - new Vector3(sim.gridCenter.x, pos.y, sim.gridCenter.y)).magnitude < 2500) shrubs.Add(pos + new Vector3(3, 0, 2), yaw, Vector3.one * (1.2f + (float)rnd.NextDouble()));
                    count++;
                }
            treeCells.AddRange(cells.Values);
            staticSets.Add(shrubs);
            Debug.Log($"[FieldShift] trees placed: {count} in {cells.Count} cells");
        }

        void DrawTrees()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var cp = cam.transform.position;
            foreach (var c in treeCells)
            {
                float d = Vector2.Distance(new Vector2(cp.x, cp.z), new Vector2(c.centre.x, c.centre.z));
                var sets = d < 700 ? c.hi : c.lo;
                foreach (var s in sets) s.Draw();
            }
        }

        // ---------------------------------------------------------------- buildings and markers
        void BuildFarmyard()
        {
            Parcel first = null;
            foreach (var p in sim.parcels) if (p.IsFarm && (first == null || p.ix + p.iz < first.ix + first.iz)) first = p;
            var corner = new Vector3(first.x - Parcel.SizeM / 2, 0, first.z - Parcel.SizeM / 2);
            housePos = corner + new Vector3(-18, 0, 26);
            barnPos = corner + new Vector3(-26, 0, 66);
            if (!PlaceModel("Farmhouse", "Door", housePos, 90))
                Spawn("Farmhouse", ProcMesh.House(), housePos, 90, Mats.Lit(new Color(0.86f, 0.84f, 0.78f)), Mats.Lit(new Color(0.25f, 0.24f, 0.26f), 0.3f), Mats.Lit(new Color(0.18f, 0.2f, 0.22f), 0.6f));
            if (!PlaceModel("Barn", null, barnPos, 90))
                Spawn("Barn", ProcMesh.Barn(), barnPos, 90, Mats.Lit(new Color(0.48f, 0.15f, 0.11f)), Mats.Lit(new Color(0.32f, 0.33f, 0.34f), 0.4f), Mats.Lit(new Color(0.85f, 0.84f, 0.8f)));
            PlaceModel("GrainBin", null, corner + new Vector3(-46, 0, 52), 0);
            PlaceModel("GrainBin", null, corner + new Vector3(-46, 0, 64), 0);

            // community office on the nearest reserve land, or by the grid edge where no reserve is on the map
            Parcel target = null; float best = float.MaxValue;
            foreach (var p in sim.parcels)
            {
                if (p.status != LandStatus.Reserve) continue;
                float d = (new Vector2(p.x, p.z) - new Vector2(first.x, first.z)).sqrMagnitude;
                if (d < best) { best = d; target = p; }
            }
            if (target != null) officePos = new Vector3(target.x, 0, target.z);
            else officePos = new Vector3(first.x + 420, 0, first.z + 60);
            office = Models.Has("Farmhouse") ? SpawnModel("Farmhouse", "Door", officePos, 0, new Dictionary<string, string> { { "HouseSiding", "OfficeSiding" }, { "Shingles", "Roof" } }, 0.85f) : null;
            if (office == null)
                office = Spawn("Community office", ProcMesh.House(14, 10, 4.5f, 2.5f), officePos, 0,
                    Mats.Lit(new Color(0.55f, 0.42f, 0.3f)), Mats.Lit(new Color(0.2f, 0.27f, 0.26f), 0.3f), Mats.Lit(new Color(0.15f, 0.15f, 0.16f), 0.5f));
            office.name = "Community office";
            var sign = Spawn("Office sign", ProcMesh.Sign(), officePos + new Vector3(0, 0, -11), 0, Mats.Lit(new Color(0.3f, 0.22f, 0.16f)), Mats.Lit(new Color(0.92f, 0.9f, 0.84f)));
            sign.transform.localScale = Vector3.one * 1.6f;
            Label(sign.transform, new Vector3(0, 1.85f, -0.06f), target != null ? data.def.nation + "\nCommunity office" : data.def.nation + "\nGathering place", 0.032f);
        }

        bool PlaceModel(string name, string front, Vector3 pos, float yaw) => SpawnModel(name, front, pos, yaw, null, 1f) != null;

        GameObject SpawnModel(string name, string front, Vector3 pos, float yaw, Dictionary<string, string> overrides, float scale)
        {
            var go = Models.Spawn(name, front, overrides);
            if (go == null) return null;
            var b = go.GetComponent<BoxCollider>();
            float r = b != null ? Mathf.Max(b.size.x, b.size.z) * 0.5f * scale : 6;
            pos.y = Mathf.Min(H(pos.x - r, pos.z - r), H(pos.x + r, pos.z + r), H(pos.x + r, pos.z - r), H(pos.x - r, pos.z + r), H(pos.x, pos.z)) - 0.15f;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        GameObject Spawn(string name, Mesh mesh, Vector3 pos, float yaw, params Material[] mats)
        {
            var go = new GameObject(name);
            pos.y = Mathf.Min(H(pos.x - 6, pos.z - 6), H(pos.x + 6, pos.z + 6), H(pos.x, pos.z)) - 0.2f;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            var col = go.AddComponent<BoxCollider>();
            col.center = mesh.bounds.center; col.size = mesh.bounds.size;
            return go;
        }

        static void Label(Transform parent, Vector3 local, string text, float charSize)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0, 0, 0);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text; tm.characterSize = charSize; tm.fontSize = 48; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.05f, 0.05f, 0.05f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font; go.GetComponent<MeshRenderer>().sharedMaterial = font.material;   // readable from the south (-z) side
        }

        void BuildReserveMarkers()
        {
            boundaryPosts = new InstanceSet(ProcMesh.FencePost(), Mats.Lit(new Color(0.75f, 0.45f, 0.2f)));
            staticSets.Add(boundaryPosts);
            var signs = new List<Vector3>();
            foreach (var p in sim.parcels)
            {
                if (p.status != LandStatus.Reserve) continue;
                foreach (var n in sim.Neighbours4(p))
                {
                    if (n.status == LandStatus.Reserve) continue;
                    var mid = new Vector3((p.x + n.x) / 2, 0, (p.z + n.z) / 2);
                    var along = Mathf.Abs(p.x - n.x) > 1 ? Vector3.forward : Vector3.right;
                    for (int i = -12; i < 12; i++)
                    {
                        var q = mid + along * (i * 4.1f);
                        boundaryPosts.Add(new Vector3(q.x, H(q.x, q.z), q.z), along == Vector3.forward ? -90 : 0, new Vector3(1, 0.7f, 1));
                    }
                    signs.Add(mid);
                }
            }
            // one sign at the boundary closest to the farm
            Vector3 farm = housePos; Vector3 bestSign = Vector3.zero; float bd = float.MaxValue;
            foreach (var s in signs) { float d = (s - farm).sqrMagnitude; if (d < bd) { bd = d; bestSign = s; } }
            if (bd < float.MaxValue)
            {
                var res = data.Reserve(sim.parcels[0].reserveId) ?? (data.maskMeta.reserves != null && data.maskMeta.reserves.Length > 0 ? data.maskMeta.reserves[0] : null);
                foreach (var p in sim.parcels) if (p.status == LandStatus.Reserve && (new Vector3(p.x, 0, p.z) - bestSign).sqrMagnitude < 80 * 80) res = data.Reserve(p.reserveId) ?? res;
                var go = Spawn("Reserve sign", ProcMesh.Sign(), bestSign + new Vector3(3, 0, 3), 0, Mats.Lit(new Color(0.3f, 0.22f, 0.16f)), Mats.Lit(new Color(0.95f, 0.93f, 0.88f)));
                go.transform.localScale = Vector3.one * 1.6f;
                Label(go.transform, new Vector3(0, 1.85f, -0.06f), (res != null ? res.name + "\n" : "") + data.def.nation, 0.032f);
            }
        }

        void BuildHighlight()
        {
            var go = new GameObject("Parcel highlight");
            highlight = go.AddComponent<LineRenderer>();
            // a soft, see-through outline (the overlay material blends; plain unlit was a loud opaque ribbon)
            var hm = Resources.Load<Material>("FieldShift/Materials/Overlay");
            highlight.material = hm != null ? new Material(hm) : Mats.Unlit(Color.white);
            highlight.widthMultiplier = 0.12f;
            highlight.positionCount = 41;
            highlight.loop = false;
            highlight.shadowCastingMode = ShadowCastingMode.Off;
            highlight.enabled = false;
        }

        public void Highlight(Parcel p, Color c, float width = 0.07f)
        {
            if (p == null) { highlight.enabled = false; return; }
            highlight.widthMultiplier = width;
            highlight.enabled = true;
            c.a = width < 0.2f ? 0.45f : 0.9f;
            highlight.startColor = highlight.endColor = c;
            highlight.material.SetColor("_BaseColor", c);
            float h = Parcel.SizeM / 2;
            var corners = new[] { new Vector3(p.x - h, 0, p.z - h), new Vector3(p.x + h, 0, p.z - h), new Vector3(p.x + h, 0, p.z + h), new Vector3(p.x - h, 0, p.z + h) };
            int k = 0;
            for (int e = 0; e < 4; e++)
                for (int i = 0; i < 10; i++, k++)
                {
                    var q = Vector3.Lerp(corners[e], corners[(e + 1) % 4], i / 10f);
                    highlight.SetPosition(k, new Vector3(q.x, H(q.x, q.z) + 0.4f, q.z));
                }
            var last = corners[0];
            highlight.SetPosition(40, new Vector3(last.x, H(last.x, last.z) + 0.4f, last.z));
        }

        public void RefreshFences()
        {
            if (fence == null) { fence = new InstanceSet(ProcMesh.FencePost(), Mats.Lit(new Color(0.42f, 0.34f, 0.26f))); staticSets.Add(fence); }
            fence.Clear();
            foreach (var p in sim.parcels)
            {
                if (!p.IsFarm) continue;
                float h = Parcel.SizeM / 2;
                // only outer edges of the farm get a fence
                var edges = new (Parcel n, Vector3 a, Vector3 dir, float yaw)[]
                {
                    (sim.At(p.ix, p.iz - 1), new Vector3(p.x - h, 0, p.z - h), Vector3.right, 0),
                    (sim.At(p.ix, p.iz + 1), new Vector3(p.x - h, 0, p.z + h), Vector3.right, 0),
                    (sim.At(p.ix - 1, p.iz), new Vector3(p.x - h, 0, p.z - h), Vector3.forward, -90),
                    (sim.At(p.ix + 1, p.iz), new Vector3(p.x + h, 0, p.z - h), Vector3.forward, -90),
                };
                foreach (var e in edges)
                {
                    if (e.n != null && e.n.IsFarm) continue;
                    for (int i = 0; i < 25; i++)
                    {
                        var q = e.a + e.dir * (i * 4f);
                        fence.Add(new Vector3(q.x, H(q.x, q.z), q.z), e.yaw, Vector3.one);
                    }
                }
            }
        }

        void Update()
        {
            DrawTrees();
            foreach (var s in staticSets) s.Draw();
            near?.Draw();
        }

        // ================================================================= near-field crops and grass
        class NearField
        {
            readonly World w;
            readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
            readonly Dictionary<string, InstanceSet> sets = new Dictionary<string, InstanceSet>();
            readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
            readonly bool textured;
            Vector3 lastPos = new Vector3(9999, 0, 9999);
            bool dirty = true;
            const float Radius = 70f;

            public NearField(World w)
            {
                this.w = w;
                var plain = Mats.Lit(Color.white, 0.12f, true);
                var grass = TexGen.Load("Foliage_GrassTuft"); var leaf = TexGen.Load("Foliage_LeafCluster"); var wheat = TexGen.Load("Foliage_Wheat");
                textured = grass != null && leaf != null && wheat != null;
                meshes["corn"] = ProcMesh.Corn(1); mats["corn"] = plain;
                if (textured)
                {
                    // alpha-tested photo cards: dense and soft, like a real field
                    meshes["grain"] = ProcMesh.Cards(3, 0.6f, 1f, false, 7); mats["grain"] = Mats.Foliage(wheat, Color.white);
                    meshes["tuft"] = ProcMesh.Cards(3, 0.9f, 1f, false, 8); mats["tuft"] = Mats.Foliage(grass, Color.white);
                    meshes["grass"] = ProcMesh.Cards(2, 1.1f, 1f, false, 9); mats["grass"] = mats["tuft"];
                    meshes["leafy"] = ProcMesh.Cards(3, 1.2f, 1f, true, 10); mats["leafy"] = Mats.Foliage(leaf, Color.white);
                    meshes["low"] = ProcMesh.Cards(3, 1.0f, 1f, true, 11); mats["low"] = mats["leafy"];
                    meshes["shrub"] = ProcMesh.Cards(4, 1.4f, 1f, true, 12); mats["shrub"] = mats["leafy"];
                }
                else
                {
                    meshes["grain"] = ProcMesh.Tuft(13); meshes["tuft"] = ProcMesh.Tuft(4); meshes["grass"] = ProcMesh.Tuft(6, 12, 0.18f);
                    meshes["leafy"] = ProcMesh.LeafyClump(2); meshes["low"] = ProcMesh.LeafyClump(3, 18, 0.22f); meshes["shrub"] = ProcMesh.Shrub(5);
                    foreach (var k in new[] { "grain", "tuft", "grass", "leafy", "low", "shrub" }) mats[k] = plain;
                }
            }

            public void MarkDirty() => dirty = true;

            static string MeshFor(CropDef d)
            {
                switch (d.id)
                {
                    case "silage_corn": return "corn";
                    case "potato": case "squash": return "leafy";
                    case "crimson_clover": case "camas_meadow": return "low";
                    case "native_margin": return "shrub";
                    case "spring_barley": case "winter_wheat": case "canola": return "grain";
                    default: return "tuft";
                }
            }

            static float SpacingFor(string mesh)
            {
                switch (mesh)
                {
                    case "corn": return 0.8f;
                    case "grain": return 0.42f;
                    case "leafy": return 0.85f;
                    case "shrub": return 2.0f;
                    case "grass": return 0.42f;
                    default: return 0.5f;
                }
            }

            InstanceSet Set(string mesh, Color c)
            {
                // quantise colour so plants share batches
                var q = new Color(Mathf.Round(c.r * 20) / 20, Mathf.Round(c.g * 20) / 20, Mathf.Round(c.b * 20) / 20);
                string key = mesh + q;
                if (!sets.TryGetValue(key, out var s))
                {
                    s = new InstanceSet(meshes[mesh], mats[mesh]) { props = new MaterialPropertyBlock() };
                    s.props.SetColor("_BaseColor", q);
                    if (mesh == "grass") s.shadows = ShadowCastingMode.Off;
                    sets[key] = s;
                }
                return s;
            }

            void Rebuild(Vector3 pos)
            {
                foreach (var s in sets.Values) s.Clear();
                var sim = w.sim;
                foreach (var p in sim.parcels)
                {
                    if ((new Vector2(p.x, p.z) - new Vector2(pos.x, pos.z)).magnitude > Radius + 72) continue;
                    var c = p.IsFarm ? p.current : null;
                    string mesh; float spacing, height; Color col;
                    if (c != null && !c.dead && c.Canopy > 0.06f)
                    {
                        mesh = MeshFor(c.def); spacing = SpacingFor(mesh);
                        float growth = Mathf.Clamp01(c.Canopy);
                        height = c.def.height * Mathf.Lerp(0.15f, 1f, growth);
                        float health = Mathf.Clamp01(c.health / 100f);
                        if (textured && mesh != "corn")
                        {
                            // photo textures carry the colour; tint for ripeness, stress and winter
                            float ripe = mesh == "grain" ? Mathf.Clamp01((c.gdd / Mathf.Max(1, c.def.gddToMaturity) - 0.55f) / 0.4f) : 0;
                            col = mesh == "grain" ? Color.Lerp(new Color(0.55f, 0.95f, 0.42f), Color.white, ripe) : Color.white;
                            if (c.def.id == "canola") col = Color.Lerp(col, new Color(1f, 1f, 0.35f), 0.5f);
                            col = Color.Lerp(new Color(0.95f, 0.78f, 0.45f), col, health);
                        }
                        else col = Color.Lerp(new Color(0.55f, 0.48f, 0.25f), c.def.color, health);
                        if (c.def.overwinters && sim.Today.tmean < 3) col = Color.Lerp(col, new Color(0.7f, 0.68f, 0.55f), 0.4f);
                    }
                    else if (!p.IsFarm && (p.status == LandStatus.Available || p.status == LandStatus.Reserve))
                    {
                        mesh = "grass"; spacing = SpacingFor("grass"); height = 0.42f;
                        col = textured ? Color.Lerp(Color.white, new Color(1.25f, 1.05f, 0.62f), w.data.def.dryGrass)
                                       : Color.Lerp(new Color(0.3f, 0.45f, 0.18f), new Color(0.62f, 0.56f, 0.32f), w.data.def.dryGrass);
                    }
                    else continue;

                    var set = Set(mesh, col);
                    float h = Parcel.SizeM / 2 - 3;
                    float x0 = Mathf.Max(p.x - h, pos.x - Radius), x1 = Mathf.Min(p.x + h, pos.x + Radius);
                    float z0 = Mathf.Max(p.z - h, pos.z - Radius), z1 = Mathf.Min(p.z + h, pos.z + Radius);
                    // snap to the field's row grid so rows stay put as you walk
                    float sx = Mathf.Ceil((x0 - (p.x - h)) / spacing) * spacing + (p.x - h);
                    float sz = Mathf.Ceil((z0 - (p.z - h)) / spacing) * spacing + (p.z - h);
                    for (float z = sz; z < z1; z += spacing)
                        for (float x = sx; x < x1; x += spacing)
                        {
                            float dx = x - pos.x, dz = z - pos.z;
                            if (dx * dx + dz * dz > Radius * Radius) continue;
                            uint hsh = (uint)((int)(x * 73.1f) * 73856093 ^ (int)(z * 51.7f) * 19349663);
                            float r1 = (hsh & 1023) / 1023f, r2 = ((hsh >> 10) & 1023) / 1023f;
                            if (mesh == "grass" && r1 > 0.9f) continue;
                            if (p.job == "harvest" && w.Covered(p, x, z)) continue;
                            float jx = x + (r1 - 0.5f) * spacing * (mesh == "corn" ? 0.15f : 0.6f), jz = z + (r2 - 0.5f) * spacing * 0.6f;
                            float s = height * (0.8f + 0.4f * r2);
                            set.Add(new Vector3(jx, w.H(jx, jz) - 0.02f, jz), r1 * 360f, new Vector3(s * (mesh == "corn" ? 1 : 1.1f), s, s));
                        }
                }
            }

            public void Draw()
            {
                if (w.player == null) return;
                var pos = w.player.position;
                if (dirty || (pos - lastPos).sqrMagnitude > 64)
                {
                    Rebuild(pos); lastPos = pos; dirty = false;
                }
                foreach (var s in sets.Values) s.Draw();
            }
        }
    }
}
