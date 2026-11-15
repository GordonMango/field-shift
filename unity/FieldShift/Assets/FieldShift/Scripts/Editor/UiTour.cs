using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FieldShift.EditorTools
{
    /// <summary>
    /// Plays through the main screens in the open editor and saves Game-view screenshots (UI included).
    /// Menu: Field Shift > Run UI Tour. From a terminal (editor window, not batch mode):
    ///   Unity -projectPath . -executeMethod FieldShift.EditorTools.UiTour.Run -fsOut /tmp/ui
    /// The editor stays open when the tour ends.
    /// </summary>
    public static class UiTour
    {
        static string outDir;
        static int step, frames;
        static bool hadRunInBackground;

        [MenuItem("Field Shift/Run UI Tour")]
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fsOut");
            outDir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.dataPath, "../Logs/ui-tour");
            step = 0; frames = 0;
            // keep the game ticking when the editor is behind other windows or on another desktop
            hadRunInBackground = PlayerSettings.runInBackground;
            PlayerSettings.runInBackground = true;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorSceneManager.OpenScene(FieldShiftTools.ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Shot(GameManager gm, string name)
        {
            var st = gm.GetComponent<ShotTaker>() ?? gm.gameObject.AddComponent<ShotTaker>();
            st.Take(Path.Combine(outDir, name + ".png"));
        }

        static void ClearStories(GameManager gm) { int guard = 0; while (gm.panel == Panel.Story && guard++ < 20) gm.NextStory(); }

        static void Tick()
        {
            frames++;
            if (EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
            if (!EditorApplication.isPlaying) { if (frames > 3000) Stop("play mode never started"); return; }
            var gm = GameManager.I;
            if (gm == null) return;
            var st = gm.GetComponent<ShotTaker>();
            if (st != null && st.busy) return;
            try
            {
                switch (step)
                {
                    case 0: if (frames < 60) return; gm.menuSel = gm.cat.Location("sumas"); Next(); return;
                    case 1: if (frames < 40) return; Shot(gm, "ui-1-menu"); Next(); return;
                    case 2: gm.StartLocation(gm.cat.Location("sumas")); Next(); return;
                    case 3: if (frames < 40) return; Shot(gm, "ui-2-story"); Next(); return;
                    case 4: ClearStories(gm); Next(); return;
                    case 5: if (frames < 60) return; Shot(gm, "ui-3-plan"); Next(); return;
                    case 6: gm.SelectField(gm.sim.Farm().First()); Next(); return;
                    case 7: if (frames < 40) return; Shot(gm, "ui-4-field-card"); Next(); return;
                    case 8:
                        gm.panel = Panel.None;
                        foreach (var p in gm.sim.Farm()) { p.plan.crew = false; p.plan.main = "potato"; }
                        gm.speed = 0;
                        for (int d = 0; d < 80 && !gm.sim.Farm().Any(p => p.job != null); d++) { gm.AdvanceDay(); ClearStories(gm); }
                        ClearStories(gm);
                        gm.panel = Panel.None;
                        gm.RecolourOverlay();
                        Next(); return;
                    case 9: if (frames < 60) return; Shot(gm, "ui-5-plan-with-jobs"); Next(); return;
                    case 10: gm.Toggle(Panel.Report); Next(); return;
                    case 11: if (frames < 60) return; Shot(gm, "ui-6-report"); Next(); return;
                    case 12:
                        gm.panel = Panel.None;
                        var job = gm.sim.Farm().FirstOrDefault(p => p.job != null) ?? gm.sim.Farm().First();
                        gm.DriveTractor(job);
                        Next(); return;
                    case 13: if (frames < 80) return; Shot(gm, "ui-7-tractor"); Next(); return;
                    case 14: gm.SetView(ViewMode.Walk); Next(); return;
                    case 15: if (frames < 60) return; Shot(gm, "ui-8-walk"); Next(); return;
                    case 16:
                    {
                        // mid-summer: crops grown, so the field-level shots show the foliage
                        string[] crops = { "silage_corn", "potato", "spring_barley", "grass_hay" };
                        int k = 0;
                        foreach (var p in gm.sim.Farm()) { p.plan.crew = true; p.plan.main = crops[k++ % crops.Length]; p.job = null; }
                        while (gm.sim.Date < new DateTime(2021, 7, 22)) { gm.AdvanceDay(); ClearStories(gm); }
                        ClearStories(gm); gm.panel = Panel.None; gm.speed = 0;
                        gm.SetView(ViewMode.Walk);
                        var farm = gm.sim.Farm().ToList();
                        var c = new Vector3(farm.Average(p => p.x), 0, farm.Average(p => p.z));
                        var pos = c + new Vector3(104, 0, 30);
                        gm.player.Place(new Vector3(pos.x, gm.world.H(pos.x, pos.z) + 0.1f, pos.z), 245);
                        gm.cam.transform.localRotation = Quaternion.Euler(6, 0, 0);
                        Next(); return;
                    }
                    case 17: if (frames < 90) return; Shot(gm, "ui-9-july-fields"); Next(); return;
                    case 18:
                    {
                        var h = gm.world.housePos + new Vector3(38, 0, -30);
                        gm.player.Place(new Vector3(h.x, gm.world.H(h.x, h.z) + 0.1f, h.z), -60);
                        gm.cam.transform.localRotation = Quaternion.Euler(-4, 0, 0);
                        Next(); return;
                    }
                    case 19: if (frames < 90) return; Shot(gm, "ui-10-farmyard"); Next(); return;
                    case 20:
                    {
                        var farm = gm.sim.Farm().ToList();
                        gm.DriveTractor(farm[2]);
                        gm.world.tractor.transform.position += gm.world.tractor.transform.forward * 30;
                        gm.world.tractor.ShowImplement("harvest");
                        Next(); return;
                    }
                    case 21: if (frames < 90) return; Shot(gm, "ui-11-tractor-july"); Next(); return;
                    case 22: if (frames < 20) return; Stop("UI tour saved to " + outDir); return;
                }
            }
            catch (Exception ex) { Stop("UI tour failed: " + ex); }
        }

        static void Next() { step++; frames = 0; }

        // the tour was stopped by hand: put the project setting back
        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.update -= Tick;
            PlayerSettings.runInBackground = hadRunInBackground;
        }

        static void Stop(string msg)
        {
            Debug.Log("[FieldShift] " + msg);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
            PlayerSettings.runInBackground = hadRunInBackground;
        }
    }
}
