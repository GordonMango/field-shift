using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldShift.EditorTools
{
    /// <summary>
    /// Menu: Field Shift > Set Up Project creates the shipped materials and the game scene.
    /// Batch mode (CI or a terminal):
    ///   Unity -batchmode -projectPath . -executeMethod FieldShift.EditorTools.FieldShiftTools.BatchSetup -quit
    ///   Unity -batchmode -projectPath . -executeMethod FieldShift.EditorTools.FieldShiftTools.BatchSimTest -quit
    ///   Unity -batchmode -projectPath . -executeMethod FieldShift.EditorTools.FieldShiftTools.BatchCapture -fsLocation sumas -fsOut /tmp/shots
    /// </summary>
    public static class FieldShiftTools
    {
        public const string ScenePath = "Assets/FieldShift/Scenes/FieldShift.unity";
        const string MatDir = "Assets/FieldShift/Resources/FieldShift/Materials";

        [MenuItem("Field Shift/Set Up Project")]
        public static void SetUp()
        {
            // make sure textures and the sky use the import rules in TextureImport.cs
            foreach (var dir in new[] { "Assets/FieldShift/Resources/FieldShift/Textures", "Assets/FieldShift/Resources/FieldShift/Sky" })
                if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.ImportAsset(dir, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
            CreateMaterials();
            CreateScene();
            PlayerSettings.productName = "Field Shift";
            PlayerSettings.companyName = "Field Shift team";
            Debug.Log("[FieldShift] Project set up. Open " + ScenePath + " and press Play.");
        }

        [MenuItem("Field Shift/Run Simulation Test")]
        public static void SimTestMenu() => BatchSimTest();

        static void CreateMaterials()
        {
            Directory.CreateDirectory(MatDir);
            Make("Lit", "Universal Render Pipeline/Lit", m => { m.enableInstancing = true; });
            Make("Unlit", "Universal Render Pipeline/Unlit", null);
            Make("TerrainLit", "Universal Render Pipeline/Terrain/Lit", m => { m.enableInstancing = true; });   // terrain is drawn instanced
            Make("Overlay", "Sprites/Default", null);
            Make("Foliage", "Universal Render Pipeline/Lit", m =>
            {
                m.enableInstancing = true;
                m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", 0.42f); m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0); m.doubleSidedGI = true; m.SetFloat("_Smoothness", 0.12f);
                m.renderQueue = (int)RenderQueue.AlphaTest;
            });
            Make("Skybox", "Skybox/Panoramic", m =>
            {
                var sky = AssetDatabase.LoadAssetAtPath<Texture>("Assets/FieldShift/Resources/FieldShift/Sky/Sky.hdr");
                if (sky != null) m.SetTexture("_MainTex", sky);
                m.SetFloat("_Mapping", 1); m.SetFloat("_ImageType", 0); m.SetFloat("_Exposure", 1);
            });
            TuneRenderPipeline();
            Make("Water", "Universal Render Pipeline/Lit", m =>
            {
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetFloat("_Smoothness", 0.92f);
                m.SetColor("_BaseColor", new Color(0.16f, 0.27f, 0.3f, 0.82f));
            });
            AssetDatabase.SaveAssets();
        }

        /// <summary>Longer, sharper shadows and anti-aliased edges (foliage cards look much better with MSAA).</summary>
        static void TuneRenderPipeline()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var a = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (a == null) continue;
                a.shadowDistance = 180;
                a.shadowCascadeCount = 4;
                a.msaaSampleCount = 4;
                EditorUtility.SetDirty(a);
            }
        }

        static void Make(string name, string shader, Action<Material> setup)
        {
            string path = $"{MatDir}/{name}.mat";
            var sh = Shader.Find(shader);
            if (sh == null) { Debug.LogError("[FieldShift] Missing shader " + shader); return; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            setup?.Invoke(m);
            EditorUtility.SetDirty(m);
        }

        static void CreateScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var go = new GameObject("Field Shift");
            go.AddComponent<GameManager>();
            go.AddComponent<GameUI>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        public static void BatchSetup() { SetUp(); }

        public static void BatchSimTest()
        {
            var text = SimTest();
            Debug.Log(text);
            File.WriteAllText(Path.Combine(Application.dataPath, "../Logs/fieldshift-simtest.txt"), text);
        }

        /// <summary>Plays every location through 2021 with the usual local practice, then prints the Rotation Report.</summary>
        public static string SimTest()
        {
            var sb = new StringBuilder();
            var cat = Catalog.Load();
            foreach (var def in cat.root.locations)
            {
                var data = LocationData.Load(def);
                var sim = new FarmSim(cat, def, data.weather);
                sim.BuildParcels(data);
                sim.ChooseStartingFarm();
                int reserve = sim.parcels.Count(p => p.status == LandStatus.Reserve), water = sim.parcels.Count(p => p.status == LandStatus.Water), steep = sim.parcels.Count(p => p.status == LandStatus.Steep);
                var farm = sim.Farm().First();
                sb.AppendLine($"=== {def.name}: parcels reserve={reserve} water={water} steep={steep} low={sim.parcels.Count(p => p.lowGround && p.status == LandStatus.Available)} floodBase={sim.floodBase:0.0} m; farm at ({farm.ix},{farm.iz}) elev {farm.elevation:0.0} m");
                sim.trackMoney = false;
                sim.dayIndex = data.weather.IndexOf(new DateTime(2020, 9, 1));
                while (sim.dayIndex < data.weather.IndexOf(GameManager.StoryStart)) sim.StepDay();
                sim.trackMoney = true; sim.money = def.startMoney; sim.events.Clear();
                while (sim.Date < new DateTime(2022, 1, 1)) sim.StepDay();
                sb.AppendLine($"  Live 2021 with usual practice on 4 ha: money {def.startMoney:N0} -> {sim.money:N0}");
                foreach (var e in sim.events.Where(e => e.kind != "harvest").Take(6)) sb.AppendLine("    event: " + e.text);
                sb.AppendLine("    harvests: " + string.Join(" | ", sim.events.Where(e => e.kind == "harvest").Take(4).Select(e => e.text)));
                foreach (int year in new[] { 2021, 2022 })
                {
                    var rs = RotationReport.Run(sim, year, true);
                    var ws = RotationReport.Summarise(data.weather, year);
                    sb.AppendLine($"  Report {year}: rain {ws.rain:0} mm, max {ws.maxT:0} C ({ws.maxTDate}), wettest 3 days {ws.maxRain3:0} mm ({ws.maxRain3Date})");
                    foreach (var r in rs)
                        sb.AppendLine($"    {r.name,-28} net {r.total.Net,9:N0}  irr {r.avgIrrigationMm,4:0} mm  leach {r.avgLeached,5:0.0}  OM {r.omChange,6:+0.00;-0.00}  CO2e {r.avgGhgT,5:0.0} t (N2O {r.avgN2OT:0.0} fert {r.avgFertT:0.0} fuel {r.avgFuelT:0.0} CH4 {r.avgCH4T:0.0} soil {r.avgSoilT:+0.0;-0.0})  NPK {r.avgN:0}/{r.avgP:0}/{r.avgK:0}  Plost {r.avgPLost:0.0}  lost {r.total.cropsLost}  [{string.Join(", ", r.landUse.Select(kv => kv.Key + " x" + kv.Value))}]");
                }
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- screenshots in batch mode
        const string CaptureKey = "fieldshift_capture";

        public static void BatchCapture()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string k, string d) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; }
            SessionState.SetString(CaptureKey, Arg("-fsLocation", "sumas") + "|" + Arg("-fsOut", Path.Combine(Application.dataPath, "../Logs/shots")));
            EditorSceneManager.OpenScene(ScenePath);
            // projects can enter Play mode without a domain reload, so register the tick here as well
            EditorApplication.update -= CaptureTick;
            EditorApplication.update += CaptureTick;
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        static void ResumeCapture()
        {
            var job = SessionState.GetString(CaptureKey, "");
            if (string.IsNullOrEmpty(job)) return;
            EditorApplication.update -= CaptureTick;
            EditorApplication.update += CaptureTick;
        }

        static int frame, step, totalFrames;
        static string outDir, locId;
        static RenderTexture rt;

        static void CaptureTick()
        {
            if (++totalFrames > 20000) { Finish(1, "timed out"); return; }
            if (!EditorApplication.isPlaying) return;
            var job = SessionState.GetString(CaptureKey, "").Split('|');
            locId = job[0]; outDir = job[1];
            frame++;
            try
            {
                var gm = GameManager.I;
                if (gm == null) { if (frame > 600) Finish(1, "GameManager never appeared"); return; }
                switch (step)
                {
                    case 0:
                        if (frame < 5) return;
                        Directory.CreateDirectory(outDir);
                        gm.StartLocation(gm.cat.Location(locId), true);
                        gm.panel = Panel.None;
                        // give the farm some variety for the picture
                        int k = 0;
                        foreach (var p in gm.sim.Farm())
                        {
                            p.plan.main = gm.loc.crops[k % gm.loc.crops.Length];
                            p.plan.cover = gm.loc.covers.Length > 0 ? gm.loc.covers[0] : null;
                            k++;
                        }
                        while (gm.sim.Date < new DateTime(2021, 7, 20)) gm.AdvanceDay();
                        gm.panel = Panel.None;
                        rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
                        gm.cam.targetTexture = rt;
                        View(gm, 0);
                        step = 1; frame = 0; return;
                    case 1: if (frame < 8) return; Save(gm, "1-field-level"); View(gm, 1); step = 2; frame = 0; return;
                    case 2: if (frame < 40) return; Save(gm, "2-plan-view"); View(gm, 2); step = 3; frame = 0; return;
                    case 3: if (frame < 8) return; Save(gm, "3-community-office"); View(gm, 3); step = 4; frame = 0; return;
                    case 4: if (frame < 40) return; Save(gm, "4-tractor"); step = 5; frame = 0;
                        if (gm.loc.floodProne) { while (gm.sim.Date < new DateTime(2021, 11, 18)) gm.AdvanceDay(); gm.panel = Panel.None; gm.overlayMode = "Crops"; View(gm, 1); }
                        return;
                    case 5: if (frame < 40) return; if (gm.loc.floodProne) Save(gm, "5-flood-nov-2021"); Finish(0, "captures written to " + outDir); return;
                }
            }
            catch (Exception ex) { Finish(1, ex.ToString()); }
        }

        static void View(GameManager gm, int which)
        {
            var farm = gm.sim.Farm().ToList();
            var c = new Vector3(farm.Average(p => p.x), 0, farm.Average(p => p.z));
            switch (which)
            {
                case 0:
                {
                    // east edge of the farm, looking west across the fields toward the farmhouse
                    gm.SetView(ViewMode.Walk);
                    var pos = c + new Vector3(104, 0, 30);
                    gm.player.Place(new Vector3(pos.x, gm.world.H(pos.x, pos.z) + 0.1f, pos.z), 245);
                    gm.cam.transform.localRotation = Quaternion.Euler(6, 0, 0);
                    break;
                }
                case 1:
                    gm.SetView(ViewMode.Plan);
                    gm.planFocus = c; gm.planHeight = 380;
                    gm.selected = farm[0];
                    gm.RecolourOverlay();
                    break;
                case 2:
                {
                    gm.SetView(ViewMode.Walk);
                    var o = gm.world.officePos + new Vector3(-6, 0, -34);
                    gm.player.Place(new Vector3(o.x, gm.world.H(o.x, o.z) + 0.1f, o.z), 10);
                    gm.cam.transform.localRotation = Quaternion.Euler(2, 0, 0);
                    break;
                }
                case 3:
                    gm.DriveTractor(farm[1]);
                    gm.world.tractor.transform.position += new Vector3(0, 0, 30);
                    gm.world.tractor.ShowImplement("harvest");
                    break;
            }
        }

        static void Save(GameManager gm, string name)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var path = Path.Combine(outDir, $"{locId}-{name}.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("[FieldShift] wrote " + path);
        }

        static void Finish(int code, string msg)
        {
            Debug.Log("[FieldShift] capture: " + msg);
            SessionState.EraseString(CaptureKey);
            EditorApplication.update -= CaptureTick;
            EditorApplication.Exit(code);
        }
    }
}
