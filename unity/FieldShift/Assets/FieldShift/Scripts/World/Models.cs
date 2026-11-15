using System.Collections.Generic;
using UnityEngine;

namespace FieldShift
{
    /// <summary>
    /// Loads the Blender-built FBX models from Resources/FieldShift/Models and gives every part a URP material by
    /// its role name (Paint, Glass, Tire, Siding, Roof...). Models are re-oriented so their front faces +Z.
    /// </summary>
    public static class Models
    {
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public static bool Has(string name) => Resources.Load<GameObject>("FieldShift/Models/" + name) != null;

        static Material Textured(string tex, Color tint, float smooth, float metal = 0, float tile = 1)
        {
            var m = Mats.Lit(tint, smooth);
            m.SetFloat("_Metallic", metal);
            var c = TexGen.Load(tex + "_Color");
            if (c != null) { m.SetTexture("_BaseMap", c); m.SetTextureScale("_BaseMap", Vector2.one * tile); }
            var n = TexGen.Load(tex + "_Normal");
            if (n != null) { m.SetTexture("_BumpMap", n); m.SetTextureScale("_BumpMap", Vector2.one * tile); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
            return m;
        }

        static Material Emissive(Color c)
        {
            var m = Mats.Lit(c, 0.8f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 1.5f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        public static Material Role(string role)
        {
            if (cache.TryGetValue(role, out var m)) return m;
            switch (role)
            {
                case "Paint": m = Mats.Lit(new Color(0.58f, 0.06f, 0.04f), 0.72f); m.SetFloat("_Metallic", 0.15f); break;
                case "Dark": m = Mats.Lit(new Color(0.045f, 0.045f, 0.05f), 0.35f); break;
                case "Glass": m = Mats.Water(new Color(0.08f, 0.11f, 0.13f, 0.6f)); m.SetFloat("_Smoothness", 0.95f); break;
                case "Tire": m = Mats.Lit(new Color(0.035f, 0.035f, 0.035f), 0.12f); break;
                case "Rim": m = Mats.Lit(new Color(0.38f, 0.38f, 0.37f), 0.5f); m.SetFloat("_Metallic", 0.7f); break;
                case "Chrome": m = Mats.Lit(new Color(0.85f, 0.85f, 0.87f), 0.85f); m.SetFloat("_Metallic", 1f); break;
                case "LightWhite": m = Emissive(new Color(1f, 0.97f, 0.9f)); break;
                case "LightAmber": m = Emissive(new Color(1f, 0.5f, 0.05f)); break;
                case "Implement": m = Mats.Lit(new Color(0.14f, 0.22f, 0.33f), 0.55f); m.SetFloat("_Metallic", 0.1f); break;
                case "Siding": m = Textured("Siding", new Color(0.86f, 0.2f, 0.14f), 0.2f); break;
                case "HouseSiding": m = Textured("Siding", new Color(0.93f, 0.96f, 1.05f), 0.2f); break;
                case "OfficeSiding": m = Textured("Siding", new Color(0.62f, 0.45f, 0.3f), 0.2f); break;
                case "Trim": m = Mats.Lit(new Color(0.92f, 0.91f, 0.87f), 0.3f); break;
                case "Roof": m = Textured("Steel", new Color(0.72f, 0.74f, 0.78f), 0.5f, 0.45f); break;
                case "Shingles": m = Textured("Shingles", new Color(0.62f, 0.6f, 0.6f), 0.15f); break;
                case "Steel": m = Textured("Steel", new Color(0.92f, 0.93f, 0.96f), 0.6f, 0.6f); break;
                case "Brick": m = Mats.Lit(new Color(0.45f, 0.19f, 0.14f), 0.15f); break;
                default: m = Mats.Lit(new Color(0.5f, 0.5f, 0.5f), 0.3f); break;
            }
            cache[role] = m;
            return m;
        }

        static string RoleOf(Material m)
        {
            if (m == null) return "Default";
            var n = m.name.Replace(" (Instance)", "");
            int dot = n.IndexOf('.');                     // Blender duplicates: Paint.001
            return dot > 0 ? n.Substring(0, dot) : n;
        }

        /// <summary>
        /// Instantiate a model. Returns a wrapper whose +Z is the model's front (the side the frontPart is on).
        /// roleOverrides swaps a role for another (e.g. HouseSiding to OfficeSiding).
        /// </summary>
        public static GameObject Spawn(string name, string frontPart = null, Dictionary<string, string> roleOverrides = null, bool addCollider = true)
        {
            var prefab = Resources.Load<GameObject>("FieldShift/Models/" + name);
            if (prefab == null) return null;
            var wrapper = new GameObject(name);
            var model = Object.Instantiate(prefab, wrapper.transform, false);
            model.name = name + " model";
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var role = RoleOf(mats[i]);
                    if (roleOverrides != null && roleOverrides.TryGetValue(role, out var alt)) role = alt;
                    mats[i] = Role(role);
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = role0(mats) == "Glass" ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            if (frontPart != null)
            {
                var front = Find(model.transform, frontPart);
                if (front != null)
                {
                    var local = wrapper.transform.InverseTransformPoint(Bounds(front.gameObject).center);
                    var centre = wrapper.transform.InverseTransformPoint(Bounds(model).center);
                    if (local.z < centre.z) model.transform.localRotation = Quaternion.Euler(0, 180, 0) * model.transform.localRotation;
                }
            }
            if (addCollider)
            {
                var b = Bounds(model);
                var col = wrapper.AddComponent<BoxCollider>();
                col.center = wrapper.transform.InverseTransformPoint(b.center);
                col.size = b.size;
            }
            return wrapper;
        }

        static string role0(Material[] mats) => mats.Length > 0 && mats[0] != null ? cacheKey(mats[0]) : "";
        static string cacheKey(Material m) { foreach (var kv in cache) if (kv.Value == m) return kv.Key; return ""; }

        public static Transform Find(Transform root, string name)
        {
            if (root.name == name || root.name.StartsWith(name + " ") || root.name.StartsWith(name + ".")) return root;
            foreach (Transform c in root) { var f = Find(c, name); if (f != null) return f; }
            return null;
        }

        public static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
