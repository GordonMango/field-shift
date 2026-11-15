using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FieldShift
{
    public enum Mode { Menu, Playing, GameOver, Finished }
    public enum Panel { None, Story, Parcel, Report, Journal, Help }
    public enum ViewMode { Plan, Walk, Drive }

    /// <summary>Runs the game: location choice, time, views, field work, the story and the player's choices.</summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        public Catalog cat;
        public LocationDef loc;
        public LocationData data;
        public FarmSim sim;
        public World world;
        public FirstPersonController player;
        public Camera cam;
        public Light sun;

        public Mode mode = Mode.Menu;
        public LocationDef menuSel;
        public Panel panel = Panel.None;
        public ViewMode view = ViewMode.Plan;
        public int speed = 1;                                   // 0 = paused
        public static readonly float[] SecondsPerDay = { 0, 2.5f, 0.8f, 0.15f };
        float dayTimer;

        // story and knowledge
        public readonly Queue<StoryBeat> storyQueue = new Queue<StoryBeat>();
        public StoryBeat currentStory;
        public readonly List<(string date, StoryBeat beat)> journal = new List<(string, StoryBeat)>();
        readonly HashSet<string> seen = new HashSet<string>(), firedThisYear = new HashSet<string>();
        public int visits;
        public DateTime lastVisit;
        public bool techniqueLearned;
        public readonly List<string> log = new List<string>();

        // targeting and planning
        public Parcel target, selected, hover;
        public bool targetOffice, targetTractor;
        public string prompt;
        public string overlayMode = "Crops";
        public bool cabView;
        readonly Dictionary<int, int> fieldNo = new Dictionary<int, int>();
        public Vector3 planFocus;
        public float planHeight = 420, planYaw = 20;
        public static readonly DateTime StoryStart = new DateTime(2021, 3, 1);

        void OnEnable() { I = this; }

        void Awake()
        {
            I = this;
            cat = Catalog.Instance;
            if (GetComponent<GameUI>() == null) gameObject.AddComponent<GameUI>();
            SetupEnvironment();
            SetupPlayer();
        }

        // ------------------------------------------------------------------ setup
        void SetupEnvironment()
        {
            sun = FindFirstObjectByType<Light>();
            if (sun == null || sun.type != LightType.Directional)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.transform.rotation = Quaternion.Euler(42, 150, 0);
            SetupSky();
            RenderSettings.sun = sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 400;
            RenderSettings.fogEndDistance = 9000;
            RenderSettings.fogColor = new Color(0.74f, 0.82f, 0.9f);
            DynamicGI.UpdateEnvironment();
            StartCoroutine(CheckAmbient());

            var vol = new GameObject("Post processing").AddComponent<Volume>();
            vol.isGlobal = true;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var ca = prof.Add<ColorAdjustments>(true); ca.postExposure.Override(0.3f); ca.contrast.Override(14); ca.saturation.Override(10);
            var wb = prof.Add<WhiteBalance>(true); wb.temperature.Override(6);
            var bl = prof.Add<Bloom>(true); bl.intensity.Override(0.3f); bl.threshold.Override(1.05f); bl.scatter.Override(0.65f);
            var vg = prof.Add<Vignette>(true); vg.intensity.Override(0.16f);
            vol.profile = prof;
        }

        /// <summary>Some players (web builds) can't light the scene from the sky photo at runtime; use a sky-to-ground gradient then.</summary>
        System.Collections.IEnumerator CheckAmbient()
        {
            yield return null; yield return null;
            var sh = RenderSettings.ambientProbe;
            float l0 = sh[0, 0] + sh[1, 0] + sh[2, 0];
            Debug.Log($"[FieldShift] lighting: quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, sky ambient {l0:0.00}, sun {(sun != null ? sun.intensity : 0):0.0}");
            if (l0 >= 0.05f) yield break;
            Debug.Log("[FieldShift] sky ambient unavailable, using a gradient");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.56f, 0.6f, 0.58f);
            RenderSettings.ambientGroundColor = new Color(0.27f, 0.25f, 0.2f);
        }

        [Serializable] class SkyInfo { public float sunU, sunV, exposure = 1; }

        /// <summary>
        /// Photographed sky (Poly Haven CC0 HDRI) as the skybox and ambient light, with the sun placed exactly
        /// where it is in the photo so shadows match the clouds.
        /// </summary>
        void SetupSky()
        {
            var mat = Resources.Load<Material>("FieldShift/Materials/Skybox");
            if (mat == null)
            {
                var tex = Resources.Load<Texture>("FieldShift/Sky/Sky");
                var sh = Shader.Find("Skybox/Panoramic");
                if (tex == null || sh == null) return;
                mat = new Material(sh);
                mat.SetTexture("_MainTex", tex);
                mat.SetFloat("_Mapping", 1); mat.SetFloat("_ImageType", 0);
            }
            mat = new Material(mat);
            var info = Resources.Load<TextAsset>("FieldShift/Sky/sky");
            var si = info != null ? JsonUtility.FromJson<SkyInfo>(info.text) : new SkyInfo { sunU = 0.595f, sunV = 0.766f };
            mat.SetFloat("_Exposure", si.exposure);
            mat.SetFloat("_Rotation", 0);
            RenderSettings.skybox = mat;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            // Unity's lat-long lookup: u = 0.5 - atan2(z, x) / 2pi, v = 1 - acos(y) / pi
            float lon = (0.5f - si.sunU) * 2 * Mathf.PI, lat = (1 - si.sunV) * Mathf.PI;
            var toSun = new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon));
            skySunDir = toSun.normalized;
            sun.transform.rotation = Quaternion.LookRotation(-skySunDir);
        }
        Vector3 skySunDir = Vector3.zero;

        void SetupPlayer()
        {
            var go = new GameObject("Player");
            go.AddComponent<CharacterController>();
            player = go.AddComponent<FirstPersonController>();
            cam = Camera.main;
            if (cam == null) cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.transform.SetParent(go.transform, false);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 9000; cam.fieldOfView = 68;
            var cd = cam.GetUniversalAdditionalCameraData();
            cd.renderPostProcessing = true;
            cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            player.cam = cam;
            player.Place(new Vector3(0, 300, 0), 0);
            player.inputEnabled = false;
            player.enabled = false;              // no gravity until a world exists
            if (FindFirstObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
        }

        // ------------------------------------------------------------------ start a location
        public void StartLocation(LocationDef def, bool skipStory = false)
        {
            loc = def;
            data = LocationData.Load(def);
            sim = new FarmSim(cat, def, data.weather);
            sim.BuildParcels(data);
            sim.ChooseStartingFarm();

            // warm up from the previous September so soil water and cover crops are realistic on March 1
            sim.trackMoney = false;
            sim.dayIndex = data.weather.IndexOf(new DateTime(2020, 9, 1));
            int startIdx = data.weather.IndexOf(StoryStart);
            while (sim.dayIndex < startIdx) sim.StepDay();
            sim.events.Clear();
            sim.trackMoney = true;
            sim.money = def.startMoney;
            foreach (var p in sim.Farm()) { p.year = new YearStats { omStart = p.om }; NumberField(p); }

            var wgo = new GameObject("World");
            world = wgo.AddComponent<World>();
            world.player = player.transform;
            world.Build(data, sim);

            var face = world.housePos + new Vector3(30, 0, -12);
            player.enabled = true;
            player.Place(new Vector3(face.x, world.H(face.x, face.z) + 0.2f, face.z), 120);
            mode = Mode.Playing;
            var farm = new List<Parcel>(sim.Farm());
            planFocus = new Vector3(0, 0, 0);
            foreach (var p in farm) planFocus += new Vector3(p.x, 0, p.z) / farm.Count;
            SetView(ViewMode.Plan);
            UpdateSun();
            log.Clear();
            Log($"Welcome to {def.name}. Every day here is real NASA weather.");
            if (!skipStory) { Tell("intro"); Tell("territory"); Tell("howto"); }
        }

        void NumberField(Parcel p) { if (!fieldNo.ContainsKey(p.index)) fieldNo[p.index] = fieldNo.Count + 1; }
        public int FieldNumber(Parcel p) => fieldNo.TryGetValue(p.index, out var n) ? n : 0;

        // ------------------------------------------------------------------ views
        public void SetView(ViewMode v)
        {
            if (world == null) return;
            if (view == ViewMode.Drive && v != ViewMode.Drive)
            {
                world.tractor.driving = false;
                var t = world.tractor.transform;
                var p = t.position - t.right * 3f;
                player.Place(new Vector3(p.x, world.H(p.x, p.z) + 0.2f, p.z), t.eulerAngles.y);
            }
            view = v;
            world.ShowOverlay(v == ViewMode.Plan);
            if (v == ViewMode.Walk)
            {
                cam.transform.SetParent(player.transform, false);
                cam.transform.localPosition = new Vector3(0, 1.68f, 0);
                cam.transform.localRotation = Quaternion.identity;
                player.enabled = true;
            }
            else
            {
                cam.transform.SetParent(null, true);
                player.inputEnabled = false;
            }
            if (v == ViewMode.Drive)
            {
                world.tractor.driving = true;
                world.tractor.ShowImplement(JobUnderTractor());
                player.transform.position = world.tractor.transform.position + Vector3.up * 50;   // keep the walker out of the way
                player.enabled = false;
            }
            if (v == ViewMode.Plan) { RecolourOverlay(); player.enabled = false; }
            world.Highlight(null, Color.white);
        }

        public void DriveTractor(Parcel to = null)
        {
            if (world == null) return;
            if (to != null)
            {
                // park at the field's south-west corner, facing along the field
                var pos = new Vector3(to.x - Parcel.SizeM / 2 + 4, 0, to.z - Parcel.SizeM / 2 - 6);
                world.tractor.Place(pos, 0);
            }
            panel = Panel.None;
            SetView(ViewMode.Drive);
        }

        string JobUnderTractor()
        {
            var p = world.ParcelAt(world.tractor.transform.position);
            if (p != null && p.job != null) return p.job;
            foreach (var q in sim.Farm()) if (q.job != null) return q.job;
            return "plant";
        }

        public void RecolourOverlay()
        {
            if (world == null) return;
            var clear = (Color.clear, Color.clear);
            world.ColourOverlay(p =>
            {
                Color c;
                switch (overlayMode)
                {
                    case "Water":
                        if (!p.IsFarm) return clear;
                        c = Color.Lerp(new Color(0.95f, 0.55f, 0.15f), new Color(0.2f, 0.5f, 1f), sim.SoilMoistureFraction(p));
                        return (new Color(c.r, c.g, c.b, 0.5f), new Color(c.r, c.g, c.b, 0.9f));
                    case "Flood":
                        if (p.status == LandStatus.Water) return clear;
                        float t = Mathf.Clamp01((p.elevation - sim.floodBase) / 8f);
                        c = new Color(0.15f, 0.45f, 1f, 0.6f * (1 - t));
                        return (c, new Color(c.r, c.g, c.b, Mathf.Min(1, c.a * 1.5f)));
                }
                switch (p.status)
                {
                    case LandStatus.Farm:
                        var plan = cat.Crop(p.plan.main);
                        c = plan != null ? Color.Lerp(plan.color, Color.white, 0.3f) : Color.white;
                        var rim = p.job != null ? new Color(1f, 0.82f, 0.2f, 0.95f) : p == selected ? new Color(1f, 0.95f, 0.5f, 0.95f) : new Color(1, 1, 1, 0.8f);
                        return (new Color(c.r, c.g, c.b, p == selected ? 0.45f : 0.28f), rim);
                    case LandStatus.Reserve: return (new Color(0.98f, 0.6f, 0.2f, 0.28f), new Color(0.98f, 0.6f, 0.2f, 0.75f));
                    case LandStatus.Available:
                        bool next = false; foreach (var n in sim.Neighbours4(p)) if (n.IsFarm) next = true;
                        return next ? (new Color(1, 1, 1, 0.03f), new Color(1, 1, 1, 0.35f)) : clear;
                    default: return clear;
                }
            });
        }

        // ------------------------------------------------------------------ story
        public string Fill(string t, float value = 0, string reserve = null)
        {
            if (string.IsNullOrEmpty(t)) return t;
            var tech = loc != null && loc.techniques.Length > 0 ? cat.Technique(loc.techniques[0]) : null;
            return t.Replace("{place}", loc?.name)
                .Replace("{territory}", loc?.territory)
                .Replace("{acknowledgement}", loc?.acknowledgement)
                .Replace("{nation}", loc?.nation)
                .Replace("{value}", value.ToString(value >= 1000 ? "N0" : "0"))
                .Replace("{reserve}", reserve ?? "this land")
                .Replace("{teaching}", tech?.teaching ?? "")
                .Replace("{inGame}", tech != null ? tech.inGame : "")
                .Replace("{review}", tech?.review ?? "");
        }

        public void Tell(string id, float value = 0, string reserve = null)
        {
            var b = cat.Story(id);
            if (b == null) return;
            seen.Add(id);
            var filled = new StoryBeat { id = b.id, title = Fill(b.title, value, reserve), text = Fill(b.text, value, reserve), more = Fill(b.more, value, reserve) };
            storyQueue.Enqueue(filled);
            journal.Add((sim != null ? Fmt(sim.Date) : "", filled));
            if (panel == Panel.None || panel == Panel.Parcel) NextStory();
        }

        public void NextStory()
        {
            if (storyQueue.Count > 0) { currentStory = storyQueue.Dequeue(); panel = Panel.Story; }
            else if (panel == Panel.Story) panel = Panel.None;
        }

        bool Once(string id) => !seen.Contains(id);
        bool OncePerYear(string id) { if (firedThisYear.Contains(id)) return false; firedThisYear.Add(id); return true; }

        public void Log(string msg)
        {
            log.Insert(0, msg);
            if (log.Count > 40) log.RemoveAt(log.Count - 1);
        }

        public static string Fmt(DateTime d) => d.ToString("ddd d MMM yyyy");

        // ------------------------------------------------------------------ main loop
        void Update()
        {
            if (mode == Mode.Menu) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; MenuKeys(); return; }
            HandleKeys();
            bool modal = panel != Panel.None && panel != Panel.Parcel;
            bool timeStops = panel != Panel.None;

            player.inputEnabled = view == ViewMode.Walk && !timeStops && mode == Mode.Playing;
            world.tractor.driving = view == ViewMode.Drive && !timeStops && mode == Mode.Playing;
            bool lockCursor = view == ViewMode.Walk && !timeStops && mode == Mode.Playing;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;

            if (view == ViewMode.Plan) UpdatePlan(modal);
            else if (view == ViewMode.Walk) UpdateWalkTarget();
            else UpdateDrive();

            if (mode == Mode.Playing && !timeStops && speed > 0)
            {
                dayTimer += Time.deltaTime;
                if (dayTimer >= SecondsPerDay[speed]) { dayTimer = 0; AdvanceDay(); }
            }
        }

        void LateUpdate()
        {
            if (world == null || mode == Mode.Menu) return;
            if (view == ViewMode.Plan)
            {
                var rot = Quaternion.Euler(58, planYaw, 0);
                var target = new Vector3(planFocus.x, world.H(planFocus.x, planFocus.z), planFocus.z);
                var pos = target - rot * Vector3.forward * planHeight;
                cam.transform.SetPositionAndRotation(Vector3.Lerp(cam.transform.position, pos, 0.25f), Quaternion.Slerp(cam.transform.rotation, rot, 0.25f));
            }
            else if (view == ViewMode.Drive)
            {
                var anchor = cabView ? world.tractor.cabCam : world.tractor.chaseCam;
                float k = cabView ? 1f : 1 - Mathf.Exp(-8 * Time.deltaTime);
                cam.transform.position = Vector3.Lerp(cam.transform.position, anchor.position, k);
                var look = cabView ? anchor.rotation : Quaternion.LookRotation(world.tractor.transform.position + Vector3.up * 1.5f - anchor.position);
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, look, k);
            }
        }

        Vector2 MousePos()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        void UpdatePlan(bool modal)
        {
            hover = null;
            prompt = null;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (!modal && kb != null)
            {
                var fwd = Quaternion.Euler(0, planYaw, 0) * Vector3.forward; var right = Quaternion.Euler(0, planYaw, 0) * Vector3.right;
                float pan = planHeight * 0.9f * Time.unscaledDeltaTime;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) planFocus += fwd * pan;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) planFocus -= fwd * pan;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) planFocus += right * pan;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) planFocus -= right * pan;
                if (kb.qKey.isPressed) planYaw -= 60 * Time.unscaledDeltaTime;
                if (kb.eKey.isPressed) planYaw += 60 * Time.unscaledDeltaTime;
                float half = FarmSim.Grid * Parcel.SizeM / 2 + 300;
                planFocus.x = Mathf.Clamp(planFocus.x, sim.gridCenter.x - half, sim.gridCenter.x + half);
                planFocus.z = Mathf.Clamp(planFocus.z, sim.gridCenter.y - half, sim.gridCenter.y + half);
            }
            if (!modal && mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) planHeight = Mathf.Clamp(planHeight * (scroll > 0 ? 0.88f : 1.13f), 90, 1100);
                if (mouse.rightButton.isPressed) planYaw += mouse.delta.ReadValue().x * 0.25f;
            }
#endif
            if (modal) return;
            var mp = MousePos();
            if (GameUI.PointerOverUI(mp)) { world.Highlight(selected, new Color(1f, 0.9f, 0.3f), planHeight / 140f); return; }
            if (Physics.Raycast(cam.ScreenPointToRay(mp), out var hit, 6000)) hover = world.ParcelAt(hit.point);
            var show = hover ?? selected;
            world.Highlight(show, hover != null ? Color.white : new Color(1f, 0.9f, 0.3f), planHeight / 140f);
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && hover != null) SelectField(hover);
#endif
        }

        public void SelectField(Parcel p)
        {
            selected = p;
            if (p.status == LandStatus.Reserve && Once("reserve")) { Tell("reserve", 0, data.Reserve(p.reserveId)?.name); return; }
            panel = Panel.Parcel;
            RecolourOverlay();
        }

        void UpdateWalkTarget()
        {
            target = null; targetOffice = false; targetTractor = false; prompt = null;
            if (panel != Panel.None) { world.Highlight(null, Color.white); return; }
            if ((world.tractor.transform.position - player.transform.position).magnitude < 7f) { targetTractor = true; prompt = "E   Drive the tractor"; world.Highlight(null, Color.white); return; }
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            if (Physics.Raycast(ray, out var hit, 220))
            {
                if (world.office != null && hit.collider.gameObject == world.office && hit.distance < 40) { targetOffice = true; prompt = "E   Visit the community office"; }
                else target = world.ParcelAt(hit.point);
            }
            if (target == null && !targetOffice)
            {
                var fwd = player.transform.forward; fwd.y = 0;
                target = world.ParcelAt(player.transform.position + fwd.normalized * 8f);
            }
            if (target == null) { world.Highlight(null, Color.white); return; }
            // on foot the prompt says which field you are in; a ground outline would just clutter the view
            world.Highlight(null, Color.white);
            switch (target.status)
            {
                case LandStatus.Farm:
                    prompt = target.job != null ? $"Field {FieldNumber(target)}: ready to {target.job}.   T  Drive the tractor" : $"Field {FieldNumber(target)}   E  Plan";
                    break;
                case LandStatus.Available: prompt = $"For lease: ${loc.leasePerHa:N0} a year   E  Details"; break;
                case LandStatus.Reserve: prompt = $"{loc.nation} land. Not for sale.   E  Learn more"; break;
            }
        }

        void UpdateDrive()
        {
            var t = world.tractor;
            var p = world.ParcelAt(t.ImplementCentre);
            prompt = null;
            if (p != null && p.IsFarm && p.job != null)
            {
                t.ShowImplement(p.job);
                if (Mathf.Abs(t.speed) > 0.8f && panel == Panel.None && world.MarkCoverage(p, t.ImplementCentre, t.transform.right))
                {
                    world.RefreshParcel(p);
                    if (world.CoverageOf(p) >= 0.9f) CompleteJob(p);
                }
                if (p.job != null) prompt = $"{(p.job == "plant" ? "Planting" : "Harvesting")} field {FieldNumber(p)}: {world.CoverageOf(p) * 100:0}%";
            }
            else if (p != null && p.IsFarm) prompt = $"Field {FieldNumber(p)}: nothing to do here right now";
            world.Highlight(p != null && p.IsFarm && p.job != null ? p : null, new Color(1f, 0.85f, 0.2f), 0.3f);
        }

        void CompleteJob(Parcel p)
        {
            string what = p.job == "plant" ? $"Field {FieldNumber(p)} planted with {p.jobCrop?.name.ToLower()}. You saved the crew fee." : $"Field {FieldNumber(p)} harvested.";
            if (p.job == "plant") sim.PlayerPlant(p); else sim.PlayerHarvest(p);
            world.ClearCoverage(p);
            world.RefreshParcel(p);
            Log(what);
            ProcessEvents(sim.Today);
        }

        void MenuKeys()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;
            var locs = cat.root.locations;
            Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6 };
            for (int i = 0; i < Mathf.Min(locs.Length, digits.Length); i++) if (kb[digits[i]].wasPressedThisFrame) menuSel = locs[i];
            if (menuSel != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) StartLocation(menuSel);
#endif
        }

        void HandleKeys()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;
            bool Down(Key k) => kb[k].wasPressedThisFrame;
            if (panel == Panel.Story)
            {
                if (Down(Key.Enter) || Down(Key.Space)) NextStory();
                return;
            }
            if (Down(Key.Escape)) { panel = panel == Panel.None ? Panel.Help : Panel.None; return; }
            if (Down(Key.Tab) || Down(Key.M)) { panel = Panel.None; SetView(view == ViewMode.Plan ? ViewMode.Walk : ViewMode.Plan); }
            if (Down(Key.R)) Toggle(Panel.Report);
            if (Down(Key.J)) Toggle(Panel.Journal);
            if (Down(Key.H)) Toggle(Panel.Help);
            if (Down(Key.T) && view != ViewMode.Drive) { DriveTractor(); return; }
            if (panel != Panel.None && panel != Panel.Parcel) return;
            if (Down(Key.Space)) speed = speed == 0 ? 1 : 0;
            if (Down(Key.Digit1)) speed = 1;
            if (Down(Key.Digit2)) speed = 2;
            if (Down(Key.Digit3)) speed = 3;
            if (view == ViewMode.Walk && Down(Key.E)) Interact();
            else if (view == ViewMode.Drive && (Down(Key.E) || Down(Key.Enter))) SetView(ViewMode.Walk);
            if (view == ViewMode.Drive && Down(Key.C)) cabView = !cabView;
#endif
        }

        public void Toggle(Panel p) { panel = panel == p ? Panel.None : p; if (panel == Panel.Report) GameUI.InvalidateReport(); }

        void Interact()
        {
            if (targetTractor) { DriveTractor(); return; }
            if (targetOffice) { Visit(); return; }
            if (target == null) return;
            SelectField(target);
        }

        public void Visit()
        {
            if (visits == 0)
            {
                visits = 1; lastVisit = sim.Date;
                Tell("visit1");
                AdvanceDay();
            }
            else if (!techniqueLearned)
            {
                int wait = 10 - (int)(sim.Date - lastVisit).TotalDays;
                if (wait > 0) Log($"The knowledge keeper is away. Come back in {wait} days.");
                else
                {
                    visits = 2; techniqueLearned = true;
                    Tell("visit2");
                    AdvanceDay();
                }
            }
            else Log("You are welcome back. What was shared is in your journal (J).");
        }

        public void AdvanceDay()
        {
            if (sim.dayIndex >= data.weather.days - 1) { mode = Mode.Finished; panel = Panel.Report; return; }
            var w = sim.Today;
            if (w.doy == 1) firedThisYear.Clear();
            sim.StepDay();
            ProcessEvents(w);
            CheckWeatherBeats(w);
            world.RefreshParcels();
            if (view == ViewMode.Plan) RecolourOverlay();
            UpdateSun();
            if (sim.money < -loc.creditLimit && mode == Mode.Playing) { mode = Mode.GameOver; Tell("broke"); }
            if (w.date.Month == 12 && w.date.Day == 31) Tell("yearend");
        }

        void ProcessEvents(DayWeather w)
        {
            foreach (var e in sim.events)
            {
                string field = e.parcel != null && FieldNumber(e.parcel) > 0 ? $"Field {FieldNumber(e.parcel)}: " : "";
                Log(field + e.text);
                if (e.kind == "harvest" && Once("harvest")) Tell("harvest", e.value);
                if (e.kind == "job" && Once("job")) Tell("job");
                if (e.kind == "flood" && OncePerYear("flood")) Tell("flood", e.value);
                if (e.kind == "restriction" && OncePerYear("restriction")) Tell("restriction");
            }
            sim.events.Clear();
        }

        void CheckWeatherBeats(DayWeather w)
        {
            // the November 2021 atmospheric river refilling the old Sumas Lake bed
            if (loc.id == "sumas" && w.date.Year == 2021 && w.date.Month == 11 && sim.floodLevel - sim.floodBase > 1.5f && Once("flood_sumas"))
                Tell("flood_sumas", sim.weather.Sum(sim.weather.precip, sim.dayIndex - 3, sim.dayIndex - 1));
            bool cash = false; foreach (var p in sim.Farm()) if (p.current != null && p.current.def.IsCash && !p.current.dead) cash = true;
            if (w.tmax >= 32 && cash && OncePerYear("heat")) Tell("heat", w.tmax);
            if (w.tmin < -1 && w.doy > 100 && w.doy < 170 && cash && OncePerYear("frost")) Tell("frost", w.tmin);
            int dry = 0, n = 0; foreach (var p in sim.Farm()) { n++; if (p.dryDays >= 21) dry++; }
            if (n > 0 && dry * 2 > n && w.doy > 150 && w.doy < 260 && OncePerYear("dry")) Tell("dry", w.gwetRoot);
        }

        void UpdateSun()
        {
            if (sim == null) return;
            var d = sim.Today;
            if (skySunDir == Vector3.zero)
            {
                float decl = 23.44f * Mathf.Sin(2 * Mathf.PI * (d.doy - 81) / 365f);
                float noon = 90 - loc.lat + decl;
                sun.transform.rotation = Quaternion.Euler(Mathf.Clamp(noon * 0.82f, 8, 70), 155, 0);
            }
            // rainy days are duller: softer sun, greyer haze (the weather is NASA POWER's)
            bool wet = d.precip > 3;
            sun.intensity = wet ? 0.75f : 1.6f;
            sun.shadowStrength = wet ? 0.45f : 0.85f;
            RenderSettings.fogColor = wet ? new Color(0.62f, 0.66f, 0.7f) : new Color(0.74f, 0.82f, 0.9f);
            RenderSettings.fogEndDistance = wet ? 4500 : 9000;
        }

        // ------------------------------------------------------------------ UI actions
        public void TeleportTo(Parcel p)
        {
            var pos = new Vector3(p.x - Parcel.SizeM / 2 - 4, 0, p.z - Parcel.SizeM / 2 - 4);
            panel = Panel.None;
            SetView(ViewMode.Walk);
            player.Place(new Vector3(pos.x, world.H(pos.x, pos.z) + 0.2f, pos.z), 45);
        }

        public bool TechniqueSuits(CropDef tech, Parcel p) => tech != null && (!tech.needsLowGround || p.lowGround);

        public void LeaseSelected()
        {
            if (selected == null) return;
            if (!sim.CanLease(selected, out var why)) { Log(why); return; }
            sim.Lease(selected);
            NumberField(selected);
            Log($"Leased field {FieldNumber(selected)} for ${loc.leasePerHa:N0} a year. Your farm is now {sim.FarmCount} ha.");
            world.RefreshFences();
            world.RefreshParcels();
            RecolourOverlay();
        }
    }
}
