using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FieldShift
{
    /// <summary>
    /// All screens, drawn with IMGUI. Design rule: show numbers and pictures first, and keep explanations one
    /// click away ("Learn more", "Show details") so beginners are not buried in text.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        GameManager G => GameManager.I;
        const float VH = 900f;
        float W, scale = 1;

        static List<StrategyResult> report;
        static WeatherYearSummary reportWeather;
        static int reportYear = 2021;
        static bool reportDirty = true;
        public static void InvalidateReport() => reportDirty = true;

        // rectangles drawn this frame, so clicks on UI do not also click the field underneath
        static readonly List<Rect> uiRects = new List<Rect>(), uiRectsBuilding = new List<Rect>();
        static float uiScale = 1;
        public static bool PointerOverUI(Vector2 screenPos)
        {
            var p = new Vector2(screenPos.x / uiScale, (Screen.height - screenPos.y) / uiScale);
            foreach (var r in uiRects) if (r.Contains(p)) return true;
            return false;
        }

        GUIStyle sPanel, sTitle, sBig, sH2, sBody, sSmall, sButton, sButtonSel, sChip, sTile, sTileSel, sTileWarm, sCenter, sPrompt, sWarn, sCell, sLabel, sCard, sGold;
        Texture2D tWhite, tDark, tSolid, tMap, tSun, tRain, tCoin, tDrop, tLeaf, tSoil, tPin, tRing, tLock, tCloud, tDot;
        Vector2 scroll, scroll2;
        bool learnMore, reportDetails, planHintDone;
        LocationDef menuSel { get => G.menuSel; set => G.menuSel = value; }
        readonly Dictionary<string, WeatherYearSummary> menuStats = new Dictionary<string, WeatherYearSummary>();
        BcMapMeta mapMeta;

        [Serializable] class BcMapMeta { public float south, north, west, east; public string source; }

        static readonly Color Ink = new Color(0.94f, 0.95f, 0.92f), Muted = new Color(0.68f, 0.72f, 0.68f), Accent = new Color(0.56f, 0.84f, 0.42f),
            Warm = new Color(0.97f, 0.66f, 0.3f), Bad = new Color(0.95f, 0.45f, 0.4f), Blue = new Color(0.45f, 0.72f, 0.98f);

        // ================================================================ styles and icons
        static Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        /// <summary>Small procedural icons so the HUD can use pictures instead of words.</summary>
        static Texture2D Icon(Func<float, float, Color> f)
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    px[y * N + x] = f((x + 0.5f) / N * 2 - 1, (y + 0.5f) / N * 2 - 1);   // -1..1, y up
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        static float Edge(float d) => Mathf.Clamp01(0.5f - d * 24f);                       // anti-aliased inside test
        static Color A(Color c, float a) { c.a *= Mathf.Clamp01(a); return c; }

        void Styles()
        {
            if (sPanel != null) return;
            tWhite = Tex(Color.white);
            tDark = Tex(new Color(0.06f, 0.08f, 0.09f, 0.9f));
            tSolid = Tex(new Color(0.03f, 0.05f, 0.07f, 1f));
            tMap = Resources.Load<Texture2D>("FieldShift/UI/bc_map");
            var mm = Resources.Load<TextAsset>("FieldShift/UI/bc_map");
            mapMeta = mm != null ? JsonUtility.FromJson<BcMapMeta>(mm.text) : new BcMapMeta { south = 47.6f, north = 60.6f, west = -140.2f, east = -112.6f };

            tSun = Icon((x, y) => { float r = Mathf.Sqrt(x * x + y * y); float ray = Mathf.Abs(Mathf.Sin(Mathf.Atan2(y, x) * 4)) > 0.85f && r < 0.95f && r > 0.62f ? 1 : 0; return A(new Color(1f, 0.82f, 0.25f), Mathf.Max(Edge(r - 0.48f), ray)); });
            tRain = Icon((x, y) => { float c = Edge(Mathf.Sqrt(x * x * 0.7f + (y - 0.25f) * (y - 0.25f) * 1.6f) - 0.55f); float drops = (Mathf.Abs(x + 0.35f) < 0.07f || Mathf.Abs(x) < 0.07f || Mathf.Abs(x - 0.35f) < 0.07f) && y < -0.3f && y > -0.85f ? 1 : 0; return c > 0 ? A(new Color(0.85f, 0.88f, 0.92f), c) : A(new Color(0.45f, 0.72f, 0.98f), drops); });
            tCoin = Icon((x, y) => { float r = Mathf.Sqrt(x * x + y * y); var c = A(new Color(0.98f, 0.8f, 0.25f), Edge(r - 0.8f)); if (Edge(Mathf.Abs(r - 0.55f) - 0.06f) > 0.5f && r < 0.8f) c = new Color(0.8f, 0.55f, 0.1f); return c; });
            tDrop = Icon((x, y) => { float yy = y + 0.25f; float body = Edge(Mathf.Sqrt(x * x + yy * yy) - 0.55f); float tip = y > 0 && Mathf.Abs(x) < (0.95f - y) * 0.6f ? 1 : 0; return A(new Color(0.45f, 0.72f, 0.98f), Mathf.Max(body, tip)); });
            tLeaf = Icon((x, y) => { float u = (x + y) * 0.707f, v = (y - x) * 0.707f; float d = (u * u) / 0.8f + (v * v) / 0.12f; return A(new Color(0.5f, 0.82f, 0.38f), d < 1f ? 1 : 0); });
            tSoil = Icon((x, y) => { if (y > 0.2f && y < 0.75f && Mathf.Abs(x) < 0.07f) return new Color(0.5f, 0.82f, 0.38f); return A(new Color(0.62f, 0.45f, 0.28f), Mathf.Abs(x) < 0.8f && y > -0.65f && y < 0.25f ? 1 : 0); });
            tPin = Icon((x, y) => { float r = Mathf.Sqrt(x * x + y * y); if (r < 0.62f) return new Color(0.97f, 0.66f, 0.3f); return A(Color.white, Edge(r - 0.95f)); });
            tRing = Icon((x, y) => { float r = Mathf.Sqrt(x * x + y * y); return A(new Color(1f, 0.8f, 0.45f), Edge(Mathf.Abs(r - 0.85f) - 0.06f)); });
            tCloud = Icon((x, y) => { float c1 = Mathf.Sqrt((x + 0.35f) * (x + 0.35f) + (y + 0.1f) * (y + 0.1f)), c2 = Mathf.Sqrt((x - 0.05f) * (x - 0.05f) + (y - 0.2f) * (y - 0.2f)), c3 = Mathf.Sqrt((x - 0.45f) * (x - 0.45f) + (y + 0.1f) * (y + 0.1f)); bool body = c1 < 0.38f || c2 < 0.45f || c3 < 0.35f || (Mathf.Abs(x) < 0.7f && y > -0.45f && y < -0.05f); return A(new Color(0.75f, 0.78f, 0.82f), body ? 1 : 0); });
            tDot = Icon((x, y) => A(Color.white, Edge(Mathf.Sqrt(x * x + y * y) - 0.92f)));
            tLock = Icon((x, y) => { bool body = Mathf.Abs(x) < 0.55f && y > -0.7f && y < 0.1f; float rr = Mathf.Sqrt(x * x + (y - 0.15f) * (y - 0.15f)); bool shackle = y > 0.1f && Mathf.Abs(rr - 0.35f) < 0.1f; return A(new Color(0.97f, 0.66f, 0.3f), body || shackle ? 1 : 0); });

            sPanel = new GUIStyle { normal = { background = tDark }, padding = new RectOffset(20, 20, 16, 16) };
            sCard = new GUIStyle { normal = { background = tDark }, padding = new RectOffset(14, 14, 12, 12), margin = new RectOffset(4, 4, 0, 0) };
            sTitle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, wordWrap = true, richText = true, normal = { textColor = Ink } };
            sBig = new GUIStyle(sTitle) { fontSize = 46 };
            sH2 = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Muted }, margin = new RectOffset(0, 0, 12, 4) };
            sBody = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, richText = true, normal = { textColor = Ink } };
            sSmall = new GUIStyle(sBody) { fontSize = 13, normal = { textColor = Muted } };
            sWarn = new GUIStyle(sBody) { fontSize = 15, normal = { textColor = Warm } };
            sLabel = new GUIStyle(sBody) { fontSize = 15, wordWrap = false, alignment = TextAnchor.MiddleLeft };
            sCenter = new GUIStyle(sBody) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            sPrompt = new GUIStyle(sCenter) { fontSize = 17, normal = { background = Tex(new Color(0, 0, 0, 0.6f)), textColor = Ink }, padding = new RectOffset(16, 16, 8, 8) };
            sChip = new GUIStyle(sCenter) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { background = Tex(new Color(0.98f, 0.82f, 0.3f, 0.95f)), textColor = new Color(0.1f, 0.1f, 0.1f) }, padding = new RectOffset(8, 8, 3, 3) };
            sButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15, wordWrap = true, richText = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(10, 10, 7, 7), margin = new RectOffset(3, 3, 3, 3),
                normal = { background = Tex(new Color(0.17f, 0.21f, 0.22f)), textColor = Ink },
                hover = { background = Tex(new Color(0.24f, 0.31f, 0.3f)), textColor = Color.white },
                active = { background = Tex(new Color(0.3f, 0.45f, 0.3f)), textColor = Color.white },
            };
            sButtonSel = new GUIStyle(sButton) { normal = { background = Tex(new Color(0.3f, 0.5f, 0.24f)), textColor = Color.white }, hover = { background = Tex(new Color(0.34f, 0.56f, 0.27f)), textColor = Color.white } };
            sGold = new GUIStyle(sButtonSel) { normal = { background = Tex(new Color(0.75f, 0.56f, 0.1f)), textColor = Color.white }, hover = { background = Tex(new Color(0.82f, 0.62f, 0.14f)), textColor = Color.white } };
            sTile = new GUIStyle(sButton) { alignment = TextAnchor.MiddleLeft, fontSize = 15, padding = new RectOffset(36, 8, 8, 8) };
            sTileSel = new GUIStyle(sButtonSel) { alignment = TextAnchor.MiddleLeft, fontSize = 15, padding = new RectOffset(36, 8, 8, 8) };
            sTileWarm = new GUIStyle(sTile) { normal = { background = Tex(new Color(0.36f, 0.24f, 0.12f)), textColor = Ink } };
            sCell = new GUIStyle { normal = { background = tWhite } };
        }

        void OnGUI()
        {
            if (G == null) return;
            Styles();
            scale = Screen.height / VH;
            uiScale = scale;
            W = Screen.width / scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            if (Event.current.type == EventType.Layout) uiRectsBuilding.Clear();

            if (G.mode == Mode.Menu) DrawMenu();
            else
            {
                if (G.view == ViewMode.Plan && G.panel != Panel.Report && G.panel != Panel.Journal) DrawPlanLabels();
                DrawHUD();
                switch (G.panel)
                {
                    case Panel.Story: DrawStory(); break;
                    case Panel.Parcel: DrawFieldCard(); break;
                    case Panel.Report: DrawReport(); break;
                    case Panel.Journal: DrawJournal(); break;
                    case Panel.Help: DrawHelp(); break;
                }
            }
            if (Event.current.type == EventType.Repaint) { uiRects.Clear(); uiRects.AddRange(uiRectsBuilding); }
        }

        Rect Block(Rect r) { if (Event.current.type == EventType.Layout) uiRectsBuilding.Add(r); return r; }
        void BeginPanel(Rect r) { GUILayout.BeginArea(Block(r), sPanel); }

        static string Money(float v) => (v < 0 ? "-$" : "$") + Mathf.Abs(v).ToString("N0");
        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        static string DoyDate(int doy) => new DateTime(2021, 1, 1).AddDays(Mathf.Clamp(doy, 1, 365) - 1).ToString("MMM d");
        static string Short(CropDef c) => c.name.Replace(" cover crop", "").Replace(" and pumpkins", "")
            .Replace("Native shrub and plant margin", "Native plant margin").Replace("Fire-renewed grass and berry margin", "Fire-renewed margin");

        void IconLabel(Texture2D icon, string text, GUIStyle style, float w = 0)
        {
            if (w > 0) GUILayout.BeginHorizontal(GUILayout.Width(w)); else GUILayout.BeginHorizontal();
            var r = GUILayoutUtility.GetRect(22, 22, GUILayout.Width(22), GUILayout.Height(22));
            GUI.DrawTexture(r, icon);
            GUILayout.Label(text, style);
            GUILayout.EndHorizontal();
        }

        void Bar(Texture2D icon, string name, float frac, Color c, string value)
        {
            GUILayout.BeginHorizontal();
            var r = GUILayoutUtility.GetRect(22, 22, GUILayout.Width(22), GUILayout.Height(22));
            GUI.DrawTexture(r, icon);
            GUILayout.Label(name, sLabel, GUILayout.Width(100));
            var b = GUILayoutUtility.GetRect(150, 22, GUILayout.Width(150));
            b.y += 6; b.height = 10;
            GUI.color = new Color(1, 1, 1, 0.12f); GUI.DrawTexture(b, tWhite);
            GUI.color = c; GUI.DrawTexture(new Rect(b.x, b.y, b.width * Mathf.Clamp01(frac), b.height), tWhite);
            GUI.color = Color.white;
            GUILayout.Label(value, sLabel, GUILayout.Width(70));
            GUILayout.EndHorizontal();
        }

        bool Btn(string text, bool selected = false, params GUILayoutOption[] opts) => GUILayout.Button(text, selected ? sButtonSel : sButton, opts);

        /// <summary>A nutrient bar: coloured letter badge (N, P, K), the level against what crops need, and a word.</summary>
        void NutrientBar(string letter, string name, float value, float target, Color badge)
        {
            GUILayout.BeginHorizontal();
            var r = GUILayoutUtility.GetRect(22, 22, GUILayout.Width(22), GUILayout.Height(22));
            GUI.color = badge; GUI.DrawTexture(r, tDot); GUI.color = Color.white;
            GUI.Label(r, $"<b>{letter}</b>", new GUIStyle(sCenter) { fontSize = 13, normal = { textColor = new Color(0.08f, 0.08f, 0.08f) } });
            GUILayout.Label(name, sLabel, GUILayout.Width(100));
            var b = GUILayoutUtility.GetRect(150, 22, GUILayout.Width(150));
            b.y += 6; b.height = 10;
            float frac = value / (target * 1.6f);
            string word = value < target * 0.6f ? "low" : value > target * 1.4f ? "high" : "good";
            var c = word == "low" ? Warm : word == "high" ? Blue : Accent;
            GUI.color = new Color(1, 1, 1, 0.12f); GUI.DrawTexture(b, tWhite);
            GUI.color = c; GUI.DrawTexture(new Rect(b.x, b.y, b.width * Mathf.Clamp01(frac), b.height), tWhite);
            GUI.color = new Color(1, 1, 1, 0.5f); GUI.DrawTexture(new Rect(b.x + b.width * (1 / 1.6f), b.y - 3, 2, b.height + 6), tWhite);   // what crops need
            GUI.color = Color.white;
            GUILayout.Label($"{value:0} kg <color=#{Hex(c)}>{word}</color>", sLabel, GUILayout.Width(95));
            GUILayout.EndHorizontal();
        }

        // ================================================================ menu: map of BC
        void DrawMenu()
        {
            GUI.DrawTexture(new Rect(0, 0, W, VH), tSolid);
            float aspect = tMap != null ? tMap.width / (float)tMap.height : 1.25f;
            float mh = VH, mw = mh * aspect;
            if (mw > W) { mw = W; mh = mw / aspect; }
            var map = new Rect(W - mw, (VH - mh) / 2, mw, mh);
            if (tMap != null) GUI.DrawTexture(map, tMap);
            Vector2 ToMap(float lat, float lon) => new Vector2(map.x + (lon - mapMeta.west) / (mapMeta.east - mapMeta.west) * map.width, map.y + (mapMeta.north - lat) / (mapMeta.north - mapMeta.south) * map.height);

            // a few towns for orientation
            foreach (var (n, lat, lon) in new[] { ("Vancouver", 49.28f, -123.12f), ("Victoria", 48.43f, -123.37f), ("Kelowna", 49.88f, -119.49f), ("Kamloops", 50.67f, -120.33f), ("Prince George", 53.92f, -122.75f), ("Fort St. John", 56.25f, -120.85f) })
            {
                var p = ToMap(lat, lon);
                GUI.color = new Color(1, 1, 1, 0.7f); GUI.DrawTexture(new Rect(p.x - 2, p.y - 2, 4, 4), tWhite); GUI.color = Color.white;
                GUI.Label(new Rect(p.x + 6, p.y - 10, 160, 20), n, new GUIStyle(sSmall) { normal = { textColor = new Color(1, 1, 1, 0.72f) } });
            }

            // farm locations
            var locs = G.cat.root.locations;
            var mouse = Event.current.mousePosition;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3);
            foreach (var l in locs)
            {
                var p = ToMap(l.lat, l.lon);
                bool hot = (mouse - p).magnitude < 24 || menuSel == l;
                float s = hot ? 30 : 22;
                GUI.color = new Color(1, 1, 1, 0.35f + 0.5f * pulse);
                GUI.DrawTexture(new Rect(p.x - s, p.y - s, s * 2, s * 2), tRing);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(p.x - s * 0.55f, p.y - s * 0.55f, s * 1.1f, s * 1.1f), tPin);
                var ls = new GUIStyle(sBody) { fontSize = hot ? 20 : 17, wordWrap = false, fontStyle = FontStyle.Bold };
                var size = ls.CalcSize(new GUIContent(l.name));
                Rect lr = l.label == "above" ? new Rect(p.x - size.x / 2, p.y - 30 - size.y, size.x, size.y)
                        : l.label == "below" ? new Rect(p.x - size.x / 2, p.y + 26, size.x, size.y)
                        : l.label == "left" ? new Rect(p.x - 26 - size.x, p.y - size.y / 2, size.x, size.y)
                        : new Rect(p.x + 26, p.y - size.y / 2, size.x, size.y);
                GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(new Rect(lr.x - 6, lr.y - 1, lr.width + 12, lr.height + 2), tWhite); GUI.color = Color.white;
                GUI.Label(lr, l.name, ls);
                if (GUI.Button(new Rect(p.x - 24, p.y - 24, 48, 48), GUIContent.none, GUIStyle.none) || GUI.Button(lr, GUIContent.none, GUIStyle.none)) menuSel = l;
            }

            // title
            BeginPanel(new Rect(24, 24, Mathf.Min(430, W - 48), 160));
            GUILayout.Label("<b>FIELD SHIFT</b>", sBig);
            GUILayout.Label("Run a farm in British Columbia through real NASA weather.", sBody);
            GUILayout.Label(menuSel == null ? "Pick a place on the map (or press 1-4)." : "Press Enter to start.", new GUIStyle(sSmall) { normal = { textColor = Warm } });
            GUILayout.EndArea();

            if (menuSel != null) LocationCard(menuSel, ToMap(menuSel.lat, menuSel.lon), map.x);
            GUI.Label(new Rect(24, VH - 30, W - 48, 22), "Map: NASA Blue Marble (NASA GIBS)  ·  Weather: NASA POWER  ·  Terrain: NASADEM", sSmall);
        }

        void LocationCard(LocationDef l, Vector2 pin, float mapLeft)
        {
            if (!menuStats.TryGetValue(l.id, out var st))
            {
                try { st = RotationReport.Summarise(LocationData.Load(l).weather, 2021); } catch (Exception) { st = null; }
                menuStats[l.id] = st;
            }
            // use the open space left of the map when there is room, so the card never hides the pins
            float w = 360, h = 340, x, y;
            if (mapLeft - 48 >= w) { w = Mathf.Min(430, mapLeft - 48); x = 24; y = 210; }
            else { x = pin.x - w - 40 > 20 ? pin.x - w - 40 : pin.x + 40; y = Mathf.Clamp(pin.y - h / 2, 200, VH - h - 40); }
            BeginPanel(new Rect(x, y, w, h));
            GUILayout.Label($"<b>{l.name}</b>", sTitle);
            GUILayout.Label(l.region, sSmall);
            GUILayout.Space(6);
            if (st != null)
            {
                IconLabel(tRain, $"{st.rain:N0} mm of rain in 2021", sLabel);
                IconLabel(tSun, $"Hottest day {st.maxT:0} °C", sLabel);
            }
            IconLabel(tDrop, l.irrigationAvailable ? $"{l.irrigationAllocationMm:0} mm of irrigation water" : "No irrigation: rain only", sLabel);
            GUILayout.Space(6);
            GUILayout.Label(l.blurb, sSmall);
            GUILayout.Label($"<color=#{Hex(Warm)}>{l.nation}</color>", sSmall);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("<b>Farm here</b>", sButtonSel, GUILayout.Height(44))) G.StartLocation(l);
            GUILayout.EndArea();
        }

        // ================================================================ HUD
        void DrawHUD()
        {
            var sim = G.sim; var d = sim.Today; var loc = G.loc;
            // top centre: date, weather, speed
            float tw = 620, tx = (W - tw) / 2;
            BeginPanel(new Rect(tx, 10, tw, 56));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{d.date:ddd d MMM yyyy}</b>", sLabel, GUILayout.Width(155));
            IconLabel(d.precip > 2 ? tRain : tSun, $"{d.tmax:0}° / {d.tmin:0}°", sLabel, 110);
            IconLabel(tDrop, $"{d.precip:0.#} mm", sLabel, 90);
            GUILayout.FlexibleSpace();
            string[] sp = { "II", ">", ">>", ">>>" };
            for (int i = 0; i < 4; i++) if (GUILayout.Button(sp[i], G.speed == i ? sButtonSel : sButton, GUILayout.Width(i == 3 ? 50 : 40), GUILayout.Height(28))) G.speed = i;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // top right: money, farm, water
            BeginPanel(new Rect(W - 250, 10, 236, 116));
            IconLabel(tCoin, $"<b>{Money(sim.money)}</b>", new GUIStyle(sLabel) { fontSize = 20, normal = { textColor = sim.money < 0 ? Bad : Ink } });
            GUILayout.BeginHorizontal();
            IconLabel(tSoil, $"{sim.FarmCount} ha", sLabel, 90);
            if (loc.irrigationAvailable) IconLabel(tDrop, $"{sim.AllocationLeftMm:0} mm", sLabel);
            GUILayout.EndHorizontal();
            float farmCo2 = 0; foreach (var fp in sim.Farm()) farmCo2 += fp.year.GhgFlowsKg + fp.year.SoilCarbonKg(G.cat.EF, fp.om);
            IconLabel(tCloud, $"{farmCo2 / 1000f:0.0} t CO2e this year", sLabel);
            GUILayout.EndArea();

            // alerts
            string alert = sim.floodLevel > 0 ? "FLOOD" : sim.restriction < 1 ? "WATER CUT 50%" : null;
            if (alert != null) GUI.Label(new Rect(W / 2 - 80, 72, 160, 24), alert, sChip);

            // field work waiting for the tractor
            var jobs = sim.Farm().Where(p => p.job != null).Take(3).ToList();
            if (jobs.Count > 0 && G.panel != Panel.Report)
            {
                BeginPanel(new Rect(14, 10, 270, 56 + jobs.Count * 46));
                GUILayout.Label("FIELD WORK WAITING", sH2);
                foreach (var p in jobs)
                    if (GUILayout.Button($"Field {G.FieldNumber(p)}: {p.job}  →  drive there", sGold, GUILayout.Height(38))) G.DriveTractor(p);
                GUILayout.EndArea();
            }

            if (G.panel == Panel.Story || G.panel == Panel.Report || G.panel == Panel.Journal) return;

            // bottom: views
            float bw = G.view == ViewMode.Plan ? 720 : 440, bx = (W - bw) / 2;
            BeginPanel(new Rect(bx, VH - 74, bw, 60));
            GUILayout.BeginHorizontal();
            if (Btn("Plan  (Tab)", G.view == ViewMode.Plan, GUILayout.Height(30))) { G.panel = Panel.None; G.SetView(ViewMode.Plan); }
            if (Btn("Walk", G.view == ViewMode.Walk, GUILayout.Height(30))) { G.panel = Panel.None; G.SetView(ViewMode.Walk); }
            if (Btn("Tractor  (T)", G.view == ViewMode.Drive, GUILayout.Height(30))) G.DriveTractor();
            if (G.view == ViewMode.Plan)
            {
                GUILayout.Space(14);
                foreach (var (key, label) in new[] { ("Crops", "Crops"), ("Water", "Soil water"), ("Flood", "Flood risk") })
                    if (Btn(label, G.overlayMode == key, GUILayout.Height(30))) { G.overlayMode = key; G.RecolourOverlay(); }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // prompt
            string prompt = G.prompt;
            if (G.view == ViewMode.Plan && !planHintDone && G.panel == Panel.None) prompt = "Click one of your fields to choose what to grow";
            if (G.panel == Panel.Parcel) planHintDone = true;
            if (!string.IsNullOrEmpty(prompt))
            {
                var size = sPrompt.CalcSize(new GUIContent(prompt));
                GUI.Label(new Rect((W - size.x) / 2, VH - 124, size.x, 38), prompt, sPrompt);
            }
            if (G.view == ViewMode.Drive) GUI.Label(new Rect(W - 380, VH - 40, 366, 24), "W/S drive · A/D steer · C cab view · E get out", new GUIStyle(sSmall) { alignment = TextAnchor.MiddleRight });
            if (G.view == ViewMode.Plan) GUI.Label(new Rect(W - 380, VH - 40, 366, 24), "WASD move · scroll zoom · right-drag turn", new GUIStyle(sSmall) { alignment = TextAnchor.MiddleRight });
            if (G.view == ViewMode.Walk && G.panel == Panel.None) { GUI.color = new Color(1, 1, 1, 0.8f); GUI.DrawTexture(new Rect(W / 2 - 2, VH / 2 - 2, 4, 4), tWhite); GUI.color = Color.white; }

            // last messages
            for (int i = 0; i < Mathf.Min(2, G.log.Count); i++)
                GUI.Label(new Rect(16, VH - 170 + i * 24, Mathf.Max(200, Mathf.Min(620, bx - 30)), 24), G.log[i], new GUIStyle(sSmall) { fontSize = 14, wordWrap = false, normal = { textColor = new Color(1, 1, 1, 1 - i * 0.4f) } });
        }

        // field names floating over the farm in the bird's-eye view
        void DrawPlanLabels()
        {
            var cam = G.cam;
            if (cam == null || G.world == null || G.sim == null) return;
            var label = new GUIStyle(sCenter) { fontSize = 15, fontStyle = FontStyle.Bold };
            var small = new GUIStyle(sCenter) { fontSize = 13 };
            foreach (var p in G.sim.parcels)
            {
                bool lease = p.status == LandStatus.Available && p == G.hover;
                bool reserve = p.status == LandStatus.Reserve && p == G.hover;
                if (!p.IsFarm && !lease && !reserve) continue;
                var sp = cam.WorldToScreenPoint(new Vector3(p.x, G.world.H(p.x, p.z) + 2, p.z));
                if (sp.z < 0) continue;
                var gp = new Vector2(sp.x / scale, (Screen.height - sp.y) / scale);
                if (p.IsFarm)
                {
                    var c = G.cat.Crop(p.plan.main);
                    GUI.Label(new Rect(gp.x - 90, gp.y - 22, 180, 22), $"{G.FieldNumber(p)}  {(c != null ? Short(c) : "Empty")}", label);
                    if (p.job != null) GUI.Label(new Rect(gp.x - 46, gp.y + 2, 92, 20), p.job == "plant" ? "PLANT" : "HARVEST", sChip);
                }
                else if (lease) GUI.Label(new Rect(gp.x - 90, gp.y - 11, 180, 22), $"For lease  ${G.loc.leasePerHa:N0}/yr", small);
                else GUI.Label(new Rect(gp.x - 130, gp.y - 11, 260, 22), $"<color=#{Hex(Warm)}>{G.loc.nation} land</color>", small);
            }
        }

        // ================================================================ story
        void DrawStory()
        {
            var b = G.currentStory;
            if (b == null) { G.NextStory(); return; }
            float w = Mathf.Min(560, W - 40);
            string body = learnMore && !string.IsNullOrEmpty(b.more) ? b.more : b.text;
            float h = Mathf.Clamp(sBody.CalcHeight(new GUIContent(body), w - 40) + 150, 220, VH - 120);
            BeginPanel(new Rect((W - w) / 2, (VH - h) / 2, w, h));
            GUILayout.Label(b.title, sTitle);
            GUILayout.Space(6);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(body, sBody);
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(b.more) && Btn(learnMore ? "Less" : "Learn more", false, GUILayout.Width(130), GUILayout.Height(38))) learnMore = !learnMore;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("<b>Continue</b>", sButtonSel, GUILayout.Width(170), GUILayout.Height(38))) { scroll = Vector2.zero; learnMore = false; G.NextStory(); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ================================================================ field card (bird's-eye planning)
        void DrawFieldCard()
        {
            var p = G.selected; var sim = G.sim; var loc = G.loc;
            if (p == null) { G.panel = Panel.None; return; }
            float w = 450, top = 110;
            BeginPanel(new Rect(W - w - 14, top, w, VH - top - 90));
            GUILayout.BeginHorizontal();
            string title = p.IsFarm ? $"Field {G.FieldNumber(p)}" : p.status == LandStatus.Available ? "Land for lease" : p.status == LandStatus.Reserve ? (G.data.Reserve(p.reserveId)?.name ?? "Reserve land") : p.status == LandStatus.Water ? "Water" : "Hillside forest";
            GUILayout.Label(title, sTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", sButton, GUILayout.Width(36), GUILayout.Height(32))) { G.panel = Panel.None; G.selected = null; G.RecolourOverlay(); }
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);

            if (p.status == LandStatus.Available)
            {
                IconLabel(tCoin, $"${loc.leasePerHa:N0} a year", sBody);
                GUILayout.Space(8);
                bool ok = sim.CanLease(p, out var why);
                GUI.enabled = ok;
                if (GUILayout.Button("<b>Lease this field</b>", sButtonSel, GUILayout.Height(42))) G.LeaseSelected();
                GUI.enabled = true;
                if (!ok) GUILayout.Label(why, sWarn);
            }
            else if (p.status == LandStatus.Reserve)
            {
                GUILayout.Label($"<color=#{Hex(Warm)}>Land of the {loc.nation}.</color>\nNot for sale or lease.", sBody);
                GUILayout.Space(8);
                GUILayout.Label(G.techniqueLearned ? "You have learned from the community." : "You can visit and learn how this land was cared for.", sSmall);
                if (!G.techniqueLearned && GUILayout.Button("<b>Visit the community office</b>", sButtonSel, GUILayout.Height(40))) { G.panel = Panel.None; G.Visit(); }
            }
            else if (!p.IsFarm) GUILayout.Label(p.status == LandStatus.Water ? "Open water." : "Too steep to farm.", sBody);
            else FarmFieldCard(p);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void FarmFieldCard(Parcel p)
        {
            var sim = G.sim; var loc = G.loc; var cat = G.cat; var d = sim.Today;
            var c = p.current;
            string status = c == null ? "Bare soil" : c.dead ? "Cover crop mulch" : $"{Short(c.def)}  ·  {Mathf.Clamp01(c.gdd / Mathf.Max(1, c.def.gddToMaturity)) * 100:0}% grown  ·  health {c.health:0}%";
            GUILayout.Label(status, sSmall);

            if (p.job != null)
            {
                GUILayout.Space(6);
                if (GUILayout.Button($"<b>Ready to {p.job}! Drive the tractor there</b>", sGold, GUILayout.Height(42))) G.DriveTractor(p);
            }

            GUILayout.Label($"SOIL TODAY  ·  {loc.soil.name.Split('(')[0].Trim().ToLower()}", sH2);
            float m = sim.SoilMoistureFraction(p);
            Bar(tDrop, "Water", m, m > 0.98f ? Blue : m < 0.45f ? Warm : Accent, $"{m * 100:0}%");
            NutrientBar("N", "Nitrogen", p.nitrogen, 100, new Color(0.56f, 0.84f, 0.42f));
            NutrientBar("P", "Phosphorus", p.phosphorus, FarmSim.PTarget, new Color(0.98f, 0.7f, 0.35f));
            NutrientBar("K", "Potassium", p.potassium, FarmSim.KTarget, new Color(0.7f, 0.6f, 0.95f));
            Bar(tSoil, "Soil health", p.om / 8f, new Color(0.75f, 0.55f, 0.35f), $"{p.om:0.0}% OM");
            GUILayout.Label($"NASA soil wetness nearby: {d.gwetRoot:0.00}  ·  tick = what crops need", sSmall);
            float co2 = (p.year.GhgFlowsKg + p.year.SoilCarbonKg(G.cat.EF, p.om)) / 1000f;
            IconLabel(tCloud, $"Greenhouse gas this year: <b>{co2:0.0} t CO2e/ha</b>", sLabel);

            GUILayout.Label("WHAT TO GROW", sH2);
            var options = loc.crops.Select(id => cat.Crop(id)).Where(x => x != null).ToList();
            var tech = loc.techniques.Length > 0 ? cat.Crop(loc.techniques[0]) : null;
            for (int i = 0; i < options.Count; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 2, options.Count); j++) CropTile(p, options[j]);
                GUILayout.EndHorizontal();
            }
            if (tech != null)
            {
                if (G.techniqueLearned && G.TechniqueSuits(tech, p)) CropTile(p, tech, true);
                else if (!G.techniqueLearned)
                {
                    GUILayout.BeginHorizontal();
                    var r = GUILayoutUtility.GetRect(22, 22, GUILayout.Width(22)); GUI.DrawTexture(r, tLock);
                    GUILayout.Label($"Learn another way from the {loc.nation}", sSmall);
                    GUILayout.EndHorizontal();
                }
            }
            var sel = cat.Crop(p.plan.main);
            if (sel != null)
            {
                GUILayout.Label(sel.description, sSmall);
                float y = FarmSim.ExpectedYield(sel);
                GUILayout.Label($"Each season it takes N {(sel.nDemand > 0 ? sel.nDemand.ToString("0") : "0 (fixes its own)")} · P2O5 {sel.pRemoval * y:0} · K2O {sel.kRemoval * y:0} kg/ha", sSmall);
            }

            if (sel == null || !sel.perennial)
            {
                GUILayout.Label("AFTER HARVEST", sH2);
                GUILayout.BeginHorizontal();
                if (Btn("Bare soil", string.IsNullOrEmpty(p.plan.cover))) { p.plan.cover = null; Changed(); }
                foreach (var id in loc.covers) { var cc = cat.Crop(id); if (cc != null && Btn(Short(cc), p.plan.cover == id)) { p.plan.cover = id; Changed(); } }
                GUILayout.EndHorizontal();
            }

            if (sel != null && !sel.IsCover && !(sel.IsIndigenous && sel.nDemand <= 20))
            {
                GUILayout.Label("FERTILIZER", sH2);
                GUILayout.BeginHorizontal();
                foreach (var f in cat.root.fertilizers)
                {
                    var a = sim.FertiliserFor(p, sel, f.id);
                    string sub = f.id == "none" ? "free\n0 t CO2e" : $"${a.cost:N0}\n{a.co2e / 1000f:0.0} t CO2e";
                    if (Btn($"{f.name.Replace("Synthetic NPK", "Synthetic")}\n<size=11>{sub}</size>", p.plan.fert == f.id, GUILayout.Width(88))) { p.plan.fert = f.id; Changed(); }
                }
                GUILayout.EndHorizontal();
                var chosen = cat.Fertilizer(p.plan.fert);
                var add = sim.FertiliserFor(p, sel, p.plan.fert);
                if (chosen != null && chosen.id != "none")
                    GUILayout.Label($"Adds N {add.n:0} · P2O5 {add.p:0} · K2O {add.k:0} kg/ha{(add.tonnes > 0 ? $" ({add.tonnes:0} t/ha)" : "")}. {chosen.desc}", sSmall);
                else if (chosen != null) GUILayout.Label(chosen.desc, sSmall);
            }

            if (loc.irrigationAvailable && (sel == null || !sel.noIrrigation))
            {
                GUILayout.Label("WATER", sH2);
                GUILayout.BeginHorizontal();
                if (Btn("Rain only", p.plan.irrigation == IrrigationType.None)) { sim.InstallIrrigation(p, IrrigationType.None); Changed(); }
                if (Btn(p.installed >= IrrigationType.Sprinkler ? "Sprinkler" : $"Sprinkler\n<size=12>${FarmSim.IrrigationInstallCost[1]:N0}</size>", p.plan.irrigation == IrrigationType.Sprinkler)) { sim.InstallIrrigation(p, IrrigationType.Sprinkler); Changed(); }
                if (Btn(p.installed >= IrrigationType.Drip ? "Drip" : $"Drip\n<size=12>${FarmSim.IrrigationInstallCost[2]:N0}</size>", p.plan.irrigation == IrrigationType.Drip)) { sim.InstallIrrigation(p, IrrigationType.Drip); Changed(); }
                GUILayout.EndHorizontal();
            }

            GUILayout.Label("FIELD WORK", sH2);
            GUILayout.BeginHorizontal();
            if (Btn($"Hire a crew\n<size=12>${FarmSim.CrewPlantPerHa + FarmSim.CrewHarvestPerHa:0}/ha a season</size>", p.plan.crew)) { p.plan.crew = true; Changed(); }
            if (Btn("My tractor\n<size=12>you drive, you save</size>", !p.plan.crew)) { p.plan.crew = false; Changed(); }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            if (Btn("Same plan for all my fields"))
                foreach (var q in sim.Farm())
                {
                    if (q == p) continue;
                    var mdef = cat.Crop(p.plan.main);
                    if (mdef != null && mdef.IsIndigenous && !G.TechniqueSuits(mdef, q)) continue;
                    q.plan.main = p.plan.main; q.plan.cover = p.plan.cover; q.plan.crew = p.plan.crew; q.plan.fert = p.plan.fert; sim.InstallIrrigation(q, p.plan.irrigation);
                    Changed();
                }
        }

        void Changed() { InvalidateReport(); G.RecolourOverlay(); }

        void CropTile(Parcel p, CropDef c, bool indigenous = false)
        {
            bool sel = p.plan.main == c.id;
            int cuts = c.regrowGdd > 0 ? 3 : 1;
            // perennials: a normal year once established (planting cost is paid once)
            float perHa = (c.GrossPerHa - c.harvestCostPerHa) * cuts - (c.perennial && c.annualCostPerHa > 0 ? c.annualCostPerHa : c.costPerHa);
            var style = sel ? sTileSel : indigenous ? sTileWarm : sTile;
            string when = c.perennial && c.yearsToFullYield > 1 ? $"from year {c.yearsToFullYield}" : $"· plant {DoyDate(c.plantFrom)}";
            var content = new GUIContent($"<b>{Short(c)}</b>\n<size=12>{Money(perHa)}/ha {when}</size>");
            var r = GUILayoutUtility.GetRect(content, style, GUILayout.Height(54), GUILayout.MinWidth(170), GUILayout.MaxWidth(200));
            if (GUI.Button(r, content, style)) { p.plan.main = c.id; Changed(); }
            GUI.color = c.color; GUI.DrawTexture(new Rect(r.x + 10, r.y + 15, 16, 24), tWhite); GUI.color = Color.white;
        }

        // ================================================================ rotation report
        void DrawReport()
        {
            var sim = G.sim;
            if (reportDirty || report == null)
            {
                report = RotationReport.Run(sim, reportYear, G.techniqueLearned);
                reportWeather = RotationReport.Summarise(sim.weather, reportYear);
                reportDirty = false;
            }
            float w = Mathf.Min(1180, W - 30), h = VH - 40;
            BeginPanel(new Rect((W - w) / 2, 20, w, h));
            GUILayout.BeginHorizontal();
            GUILayout.Label(G.mode == Mode.GameOver ? "Out of money. What would have worked?" : G.mode == Mode.Finished ? "Five years of NASA data: how did you do?" : "Compare ways to farm", sTitle);
            GUILayout.FlexibleSpace();
            for (int y = 2019; y <= 2023; y++) if (Btn(y.ToString(), y == reportYear, GUILayout.Width(64))) { reportYear = y; reportDirty = true; }
            GUILayout.EndHorizontal();
            var s = reportWeather;
            GUILayout.BeginHorizontal();
            IconLabel(tRain, $"{s.rain:N0} mm rain", sLabel, 170);
            IconLabel(tSun, $"hottest {s.maxT:0} °C", sLabel, 160);
            IconLabel(tDrop, $"wettest 3 days {s.maxRain3:0} mm", sLabel, 240);
            GUILayout.Label($"<color=#{Hex(Muted)}>Your {sim.FarmCount} ha · real {s.year} NASA weather · four plans</color>", sLabel);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            scroll2 = GUILayout.BeginScrollView(scroll2);
            int bestNet = Best(r => r.total.Net, true), bestWater = Best(r => r.avgIrrigationMm, false), bestLeach = Best(r => r.avgLeached, false), bestSoil = Best(r => r.omChange, true), bestGhg = Best(r => r.avgGhgT, false);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < report.Count; i++)
            {
                var r = report[i];
                GUILayout.BeginVertical(sCard, GUILayout.Width((w - 80) / report.Count));
                GUILayout.Label($"<b>{r.name}</b>", new GUIStyle(sBody) { fontSize = 18 });
                if (r.locked)
                {
                    GUILayout.Space(10);
                    var lr = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40)); GUI.DrawTexture(lr, tLock);
                    GUILayout.Label(r.lockedReason, sWarn);
                }
                else
                {
                    GUILayout.Label(r.plan, sSmall, GUILayout.Height(56));
                    GUILayout.Label($"<size=32><b><color=#{Hex(r.total.Net >= 0 ? Accent : Bad)}>{Money(r.total.Net)}</color></b></size>{(i == bestNet ? "  ★" : "")}", sBody);
                    GUILayout.Label("profit this year", sSmall);
                    GUILayout.Space(8);
                    Metric(tDrop, $"{r.avgIrrigationMm:0} mm water", i == bestWater);
                    Metric(tLeaf, $"{r.avgLeached:0} kg nitrate lost", i == bestLeach);
                    Metric(tSoil, $"soil health {r.omChange:+0.00;-0.00}", i == bestSoil);
                    Metric(tCloud, $"{r.avgGhgT:0.0} t CO2e/ha", i == bestGhg);
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (Btn(reportDetails ? "Hide details" : "Show details: why, evidence, risks", reportDetails, GUILayout.Width(320))) reportDetails = !reportDetails;
            GUILayout.FlexibleSpace();
            GUILayout.Label("★ best in each row · supports farmers' judgement, doesn't replace it", sSmall);
            GUILayout.EndHorizontal();

            if (reportDetails)
            {
                foreach (var r in report)
                {
                    if (r.locked) continue;
                    GUILayout.Label(r.name.ToUpper(), sH2);
                    GUILayout.Label($"<b>Why:</b> {r.why}", sBody);
                    if (r.benefits.Count > 0) GUILayout.Label("<b>Evidence and benefits:</b> " + string.Join(" ", r.benefits), sBody);
                    if (r.risks.Count > 0) GUILayout.Label("<b>Risks to check locally:</b> " + string.Join(" ", r.risks), sBody);
                    GUILayout.Label($"Lost to weather: drought {Money(r.total.lossDrought)} · heat {Money(r.total.lossHeat)} · flood {Money(r.total.lossFlood)} · frost {Money(r.total.lossFrost)} · pests {Money(r.total.lossPest)}", sSmall);
                    GUILayout.Label($"Greenhouse gas per ha (t CO2e): nitrous oxide {r.avgN2OT:0.00} · making fertiliser {r.avgFertT:0.00} · diesel {r.avgFuelT:0.00} · methane {r.avgCH4T:0.00} · pumping {r.avgIrrT:0.00} · soil carbon {r.avgSoilT:+0.00;-0.00}", sSmall);
                    GUILayout.Label($"Nutrients applied per ha: N {r.avgN:0} · P2O5 {r.avgP:0} · K2O {r.avgK:0} kg · phosphorus washed off {r.avgPLost:0.0} kg", sSmall);
                }
                GUILayout.Label("GAPS IN THE DATA", sH2);
                foreach (var g in RotationReport.Gaps) GUILayout.Label("· " + g, sSmall);
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (G.mode == Mode.GameOver || G.mode == Mode.Finished) { if (GUILayout.Button("<b>Play again</b>", sButtonSel, GUILayout.Width(200), GUILayout.Height(40))) RestartGame(); }
            GUILayout.FlexibleSpace();
            if (G.mode == Mode.Playing && Btn("Close  (R)", false, GUILayout.Width(160), GUILayout.Height(36))) G.panel = Panel.None;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void Metric(Texture2D icon, string text, bool best)
        {
            GUILayout.BeginHorizontal();
            var r = GUILayoutUtility.GetRect(20, 20, GUILayout.Width(20)); GUI.DrawTexture(r, icon);
            GUILayout.Label(best ? $"<color=#{Hex(Accent)}><b>{text}  ★</b></color>" : text, sLabel);
            GUILayout.EndHorizontal();
        }

        int Best(Func<StrategyResult, float> f, bool higher)
        {
            int best = -1; float bv = 0;
            for (int i = 0; i < report.Count; i++)
            {
                if (report[i].locked) continue;
                float v = f(report[i]);
                if (best < 0 || (higher ? v > bv : v < bv)) { best = i; bv = v; }
            }
            return best;
        }

        void RestartGame() => UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);

        // ================================================================ journal and help
        void DrawJournal()
        {
            float w = Mathf.Min(760, W - 40);
            BeginPanel(new Rect((W - w) / 2, 40, w, VH - 80));
            GUILayout.Label("June's notebook", sTitle);
            scroll = GUILayout.BeginScrollView(scroll);
            for (int i = G.journal.Count - 1; i >= 0; i--)
            {
                var (date, b) = G.journal[i];
                GUILayout.Label($"{b.title.ToUpper()}  ·  {date}", sH2);
                GUILayout.Label(string.IsNullOrEmpty(b.more) ? b.text : b.more, sBody);
            }
            GUILayout.Label("DATA AND SOURCES", sH2);
            foreach (var s in G.cat.root.sources) GUILayout.Label("· " + s, sSmall);
            GUILayout.Label($"· NASA POWER cell elevation {G.data.weather.powerCellElevation:0} m, your fields about {G.data.weather.targetElevation:0} m: temperatures shifted {G.data.weather.elevationAdjustC:+0.0;-0.0} °C (4 °C per km).", sSmall);
            GUILayout.EndScrollView();
            if (Btn("Close  (J)")) G.panel = Panel.None;
            GUILayout.EndArea();
        }

        void DrawHelp()
        {
            float w = Mathf.Min(520, W - 40);
            BeginPanel(new Rect((W - w) / 2, 140, w, 420));
            GUILayout.Label("Controls", sTitle);
            GUILayout.Label("<b>Tab</b>  bird's-eye plan view / walk\n<b>Click</b>  a field to plan it\n<b>T</b>  drive the tractor\n<b>WASD</b>  move · <b>mouse</b> look · <b>Shift</b> run\n<b>R</b>  compare ways to farm\n<b>J</b>  June's notebook and sources\n<b>Space</b>  pause · <b>1 2 3</b> speed", sBody);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (Btn("Back to the farm")) G.panel = Panel.None;
            if (Btn("Choose another place")) RestartGame();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
