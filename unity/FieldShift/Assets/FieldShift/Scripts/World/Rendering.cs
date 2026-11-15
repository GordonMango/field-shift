using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldShift
{
    /// <summary>Material factory. Base materials live in Resources so they ship in builds; Shader.Find is the fallback.</summary>
    public static class Mats
    {
        static Material baseLit, baseUnlit, baseWater, baseTerrain, baseFoliage;

        /// <summary>Alpha-clipped, two-sided foliage card material (grass, leaves, branches, grain).</summary>
        public static Material Foliage(Texture2D tex, Color tint)
        {
            baseFoliage ??= LoadOr("Foliage", "Universal Render Pipeline/Lit");
            var m = new Material(baseFoliage) { enableInstancing = true };
            m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", 0.42f); m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", (float)CullMode.Off); m.doubleSidedGI = true;
            m.SetFloat("_Smoothness", 0.12f); m.SetFloat("_Metallic", 0);
            m.renderQueue = (int)RenderQueue.AlphaTest;
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            return m;
        }

        public static Material Bark()
        {
            var m = Lit(new Color(0.75f, 0.7f, 0.65f), 0.1f);
            var c = TexGen.Load("Bark_Color"); var n = TexGen.Load("Bark_Normal");
            if (c != null) { m.SetTexture("_BaseMap", c); m.SetTextureScale("_BaseMap", new Vector2(2, 6)); }
            if (n != null) { m.SetTexture("_BumpMap", n); m.SetTextureScale("_BumpMap", new Vector2(2, 6)); m.EnableKeyword("_NORMALMAP"); }
            return m;
        }

        static Material LoadOr(string res, string shader)
        {
            var m = Resources.Load<Material>("FieldShift/Materials/" + res);
            if (m != null) return m;
            var sh = Shader.Find(shader);
            return sh != null ? new Material(sh) : null;
        }

        public static Material Lit(Color c, float smoothness = 0.15f, bool twoSided = false)
        {
            baseLit ??= LoadOr("Lit", "Universal Render Pipeline/Lit");
            var m = new Material(baseLit) { enableInstancing = true };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0);
            if (twoSided) { m.SetFloat("_Cull", (float)CullMode.Off); m.doubleSidedGI = true; }
            return m;
        }

        public static Material Unlit(Color c)
        {
            baseUnlit ??= LoadOr("Unlit", "Universal Render Pipeline/Unlit");
            var m = new Material(baseUnlit);
            m.SetColor("_BaseColor", c);
            return m;
        }

        public static Material Water(Color c)
        {
            baseWater ??= Resources.Load<Material>("FieldShift/Materials/Water");
            Material m;
            if (baseWater != null) m = new Material(baseWater);
            else
            {
                m = Lit(c, 0.92f);
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            m.SetColor("_BaseColor", c);
            return m;
        }

        public static Material Terrain()
        {
            baseTerrain ??= LoadOr("TerrainLit", "Universal Render Pipeline/Terrain/Lit");
            return baseTerrain;
        }
    }

    /// <summary>GPU-instanced batch of one mesh (with one material per submesh).</summary>
    public class InstanceSet
    {
        public Mesh mesh;
        public Material[] materials;
        public readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
        public ShadowCastingMode shadows = ShadowCastingMode.On;
        public MaterialPropertyBlock props;
        Matrix4x4[] buffer = new Matrix4x4[0];
        bool dirty = true;

        public InstanceSet(Mesh mesh, params Material[] materials) { this.mesh = mesh; this.materials = materials; }

        public void Clear() { matrices.Clear(); dirty = true; }
        public void Add(Matrix4x4 m) { matrices.Add(m); dirty = true; }
        public void Add(Vector3 pos, float yaw, Vector3 scale) => Add(Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw, 0), scale));

        public void Draw()
        {
            if (mesh == null || matrices.Count == 0) return;
            if (dirty) { buffer = matrices.ToArray(); dirty = false; }
            for (int s = 0; s < mesh.subMeshCount && s < materials.Length; s++)
            {
                var rp = new RenderParams(materials[s]) { shadowCastingMode = shadows, receiveShadows = true, matProps = props };
                for (int start = 0; start < buffer.Length; start += 1023)
                    Graphics.RenderMeshInstanced(rp, mesh, s, buffer, Mathf.Min(1023, buffer.Length - start), start);
            }
        }
    }

    /// <summary>Procedural fallback textures so the game runs before photo textures are added.</summary>
    public static class TexGen
    {
        public static Texture2D Load(string name) => Resources.Load<Texture2D>("FieldShift/Textures/" + name);

        /// <summary>
        /// Terrain shaders read smoothness from the colour texture's alpha. Photos have none (alpha = 1), which
        /// makes the ground look wet and shiny, so copy the texture with a low, matte alpha.
        /// </summary>
        public static Texture2D Noise(string name, Color a, Color b, float scale, int seed, float speckle = 0.15f)
        {
            const int N = 256;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat };
            var px = new Color[N * N];
            float ox = seed * 13.1f, oy = seed * 7.7f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    // seamless: blend four offset samples
                    float u = x / (float)N, v = y / (float)N;
                    float n = 0, amp = 1, tot = 0, f = scale;
                    for (int o = 0; o < 4; o++)
                    {
                        float s00 = Mathf.PerlinNoise(ox + u * f, oy + v * f), s10 = Mathf.PerlinNoise(ox + (u - 1) * f, oy + v * f);
                        float s01 = Mathf.PerlinNoise(ox + u * f, oy + (v - 1) * f), s11 = Mathf.PerlinNoise(ox + (u - 1) * f, oy + (v - 1) * f);
                        float s = Mathf.Lerp(Mathf.Lerp(s00, s10, u), Mathf.Lerp(s01, s11, u), v);
                        n += s * amp; tot += amp; amp *= 0.5f; f *= 2;
                    }
                    n /= tot;
                    float sp = ((x * 73856093 ^ y * 19349663 ^ seed) & 255) / 255f;
                    var c = Color.Lerp(a, b, n) * (1 - speckle * 0.5f + speckle * sp);
                    c.a = 0.06f;                    // matte: terrain smoothness comes from alpha
                    px[y * N + x] = c;
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }
    }
}
