using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FieldShift.EditorTools
{
    /// <summary>
    /// Builds the browser version into Builds/WebGL (upload that folder to itch.io or GitHub Pages).
    /// Menu: Field Shift > Build for Web. From a terminal, with the editor closed:
    ///   Unity -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod FieldShift.EditorTools.WebBuild.Run -logFile Logs/webgl-build.log
    /// </summary>
    public static class WebBuild
    {
        public const string OutDir = "Builds/WebGL";
        const string VariantDir = "Assets/FieldShift/Resources/FieldShift/Materials/Variants";

        [MenuItem("Field Shift/Build for Web")]
        public static void Menu() => Build();

        public static void Run() => EditorApplication.Exit(Build() ? 0 : 1);

        static bool Build()
        {
            KeepShaderVariants();
            UseWebQuality();

            PlayerSettings.productName = "Field Shift";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;   // plays on GitHub Pages and itch.io without special server headers
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:FieldShift";      // Assets/WebGLTemplates/FieldShift: fills the browser window
            PlayerSettings.runInBackground = false;
            AssetDatabase.SaveAssets();

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { FieldShiftTools.ScenePath },
                locationPathName = OutDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            string msg = $"[FieldShift] WebGL build {sum.result}: {sum.totalSize / 1048576f:0.0} MB in {sum.totalTime.TotalMinutes:0.0} min, " +
                         $"{sum.totalErrors} errors, {sum.totalWarnings} warnings -> {Path.GetFullPath(OutDir)}";
            Debug.Log(msg);
            File.WriteAllText("Logs/fieldshift-webgl.txt", msg + "\n" + string.Join("\n",
                report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content)));
            return sum.result == BuildResult.Succeeded;
        }

        /// <summary>
        /// The game turns on normal maps and emission on cloned materials at runtime. Builds only keep shader variants that some
        /// material in the build uses, so these small materials in Resources make sure those variants ship.
        /// </summary>
        static void KeepShaderVariants()
        {
            Directory.CreateDirectory(VariantDir);
            Variant("LitNormal", "Universal Render Pipeline/Lit", "_NORMALMAP");
            Variant("LitEmission", "Universal Render Pipeline/Lit", "_EMISSION");
            Variant("FoliageNormal", "Universal Render Pipeline/Lit", "_ALPHATEST_ON", "_NORMALMAP");
            Variant("TerrainNormal", "Universal Render Pipeline/Terrain/Lit", "_NORMALMAP");
            Variant("TerrainPixelNormal", "Universal Render Pipeline/Terrain/Lit", "_NORMALMAP", "_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            // World.cs draws the terrain instanced, so the base terrain material must keep its instancing variants
            var terrain = AssetDatabase.LoadAssetAtPath<Material>("Assets/FieldShift/Resources/FieldShift/Materials/TerrainLit.mat");
            if (terrain != null) { terrain.enableInstancing = true; EditorUtility.SetDirty(terrain); }
            AssetDatabase.SaveAssets();
        }

        static void Variant(string name, string shader, params string[] keywords)
        {
            string path = $"{VariantDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            m.enableInstancing = true;
            foreach (var k in keywords) m.EnableKeyword(k);
            if (keywords.Contains("_ALPHATEST_ON")) m.SetFloat("_AlphaClip", 1);
            EditorUtility.SetDirty(m);
        }

        /// <summary>
        /// Web builds use the Mobile level (forward rendering, no SSAO): the PC renderer's Forward+ and SSAO passes render the
        /// scene dark in browsers. Both levels stay in the build so the post-processing shaders are kept.
        /// </summary>
        static void UseWebQuality()
        {
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
            if (qs == null) return;
            var so = new SerializedObject(qs);
            var names = QualitySettings.names;
            int pc = System.Array.IndexOf(names, "PC"), mobile = System.Array.IndexOf(names, "Mobile");
            if (pc < 0 || mobile < 0) return;
            // the template project leaves WebGL out of the PC level; without it the build strips the camera's post-processing shaders
            var level = so.FindProperty("m_QualitySettings").GetArrayElementAtIndex(pc).FindPropertyRelative("excludedTargetPlatforms");
            for (int i = level.arraySize - 1; i >= 0; i--)
                if (level.GetArrayElementAtIndex(i).stringValue == "WebGL") level.DeleteArrayElementAtIndex(i);
            var map = so.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < map.arraySize; i++)
            {
                var e = map.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("first").stringValue == "WebGL") e.FindPropertyRelative("second").intValue = mobile;
            }
            so.ApplyModifiedProperties();
            // the Mobile level is only used by the web build: draw at full resolution
            if (QualitySettings.GetRenderPipelineAssetAt(mobile) is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = 1f;
                EditorUtility.SetDirty(urp);
            }
        }
    }
}
