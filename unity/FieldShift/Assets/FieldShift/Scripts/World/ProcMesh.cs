using System.Collections.Generic;
using UnityEngine;

namespace FieldShift
{
    /// <summary>
    /// Procedural meshes so the slice runs with no imported models. Every mesh is built at unit scale and
    /// scaled per instance. Swap these for photoscanned assets later without touching the game code.
    /// </summary>
    public static class ProcMesh
    {
        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<List<int>> subs = new List<List<int>>();
            public Builder(int submeshes) { for (int i = 0; i < submeshes; i++) subs.Add(new List<int>()); }

            public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, bool twoSided = false)
            {
                var nn = Vector3.Cross(b - a, c - a).normalized;
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); n.Add(nn); n.Add(nn); n.Add(nn);
                uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.up);
                subs[sub].Add(i); subs[sub].Add(i + 1); subs[sub].Add(i + 2);
                if (twoSided) Tri(sub, a, c, b);
            }

            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool twoSided = false)
            {
                Tri(sub, a, b, c, twoSided); Tri(sub, a, c, d, twoSided);
            }

            /// <summary>Smooth-shaded cone/cylinder frustum around the Y axis.</summary>
            public void Frustum(int sub, Vector3 baseCenter, float r0, float r1, float h, int sides, float jitter = 0, int seed = 0)
            {
                var rnd = new System.Random(seed);
                int start = v.Count;
                for (int i = 0; i <= sides; i++)
                {
                    float a = (i % sides) / (float)sides * Mathf.PI * 2;
                    float j = 1 + (float)(rnd.NextDouble() - 0.5) * jitter;
                    var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    var p0 = baseCenter + dir * r0 * j;
                    var p1 = baseCenter + Vector3.up * h + dir * r1 * j;
                    var slope = new Vector3(0, (r0 - r1) / Mathf.Max(0.001f, h), 0);
                    var nrm = (dir + slope).normalized;
                    v.Add(p0); n.Add(nrm); uv.Add(new Vector2(i / (float)sides, 0));
                    v.Add(p1); n.Add(nrm); uv.Add(new Vector2(i / (float)sides, 1));
                }
                for (int i = 0; i < sides; i++)
                {
                    int a = start + i * 2;
                    subs[sub].AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
                }
            }

            /// <summary>A curved, tapering leaf strip from base along dir, drooping by gravity.</summary>
            public void Leaf(int sub, Vector3 basePos, Vector3 dir, float length, float width, float droop, int segs = 4)
            {
                dir.Normalize();
                var side = Vector3.Cross(dir, Vector3.up);
                if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                side.Normalize();
                Vector3 prevL = basePos - side * width * 0.15f, prevR = basePos + side * width * 0.15f;
                for (int s = 1; s <= segs; s++)
                {
                    float t = s / (float)segs;
                    var c = basePos + dir * length * t + Vector3.down * droop * t * t;
                    float w = width * Mathf.Sin(Mathf.PI * Mathf.Min(1, t * 1.1f)) * (1 - t * 0.6f) + 0.002f;
                    var l = c - side * w * 0.5f; var r = c + side * w * 0.5f;
                    Quad(sub, prevL, l, r, prevR, true);
                    prevL = l; prevR = r;
                }
            }

            public void Box(int sub, Vector3 c, Vector3 size)
            {
                var h = size * 0.5f;
                Vector3[] p =
                {
                    c + new Vector3(-h.x, -h.y, -h.z), c + new Vector3(h.x, -h.y, -h.z), c + new Vector3(h.x, h.y, -h.z), c + new Vector3(-h.x, h.y, -h.z),
                    c + new Vector3(-h.x, -h.y, h.z), c + new Vector3(h.x, -h.y, h.z), c + new Vector3(h.x, h.y, h.z), c + new Vector3(-h.x, h.y, h.z)
                };
                // two-sided so a winding mistake can never make a wall invisible
                Quad(sub, p[0], p[3], p[2], p[1], true); Quad(sub, p[5], p[6], p[7], p[4], true);
                Quad(sub, p[4], p[7], p[3], p[0], true); Quad(sub, p[1], p[2], p[6], p[5], true);
                Quad(sub, p[3], p[7], p[6], p[2], true); Quad(sub, p[4], p[0], p[1], p[5], true);
            }

            /// <summary>Quad with explicit UVs and a shared normal (foliage uses up-tilted normals for soft lighting).</summary>
            public void QuadUV(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 normal)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                n.Add(normal); n.Add(normal); n.Add(normal); n.Add(normal);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                subs[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
                m.subMeshCount = subs.Count;
                for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i);
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>Douglas-fir style conifer, 1 m tall. Submesh 0 trunk, 1 foliage.</summary>
        public static Mesh Conifer(int seed)
        {
            var b = new Builder(2);
            b.Frustum(0, Vector3.zero, 0.022f, 0.008f, 0.35f, 6);
            var rnd = new System.Random(seed);
            int tiers = 6;
            for (int i = 0; i < tiers; i++)
            {
                float t = i / (float)tiers;
                float y = 0.12f + t * 0.78f;
                float r = Mathf.Lerp(0.2f, 0.035f, t) * (0.9f + (float)rnd.NextDouble() * 0.2f);
                b.Frustum(1, new Vector3(0, y, 0), r, 0.01f, 0.22f - t * 0.06f, 9, 0.35f, seed * 31 + i);
            }
            return b.Build("Conifer");
        }

        /// <summary>Distant conifer: one cone, same submesh layout as <see cref="Conifer"/>.</summary>
        public static Mesh ConiferLow()
        {
            var b = new Builder(2);
            b.Frustum(0, Vector3.zero, 0.02f, 0.02f, 0.15f, 3);
            b.Frustum(1, new Vector3(0, 0.12f, 0), 0.17f, 0.0f, 0.88f, 6);
            return b.Build("ConiferLow");
        }

        public static Mesh BroadleafLow()
        {
            var b = new Builder(2);
            b.Frustum(0, Vector3.zero, 0.03f, 0.03f, 0.5f, 3);
            b.Frustum(1, new Vector3(0, 0.42f, 0), 0.2f, 0.3f, 0.2f, 6);
            b.Frustum(1, new Vector3(0, 0.62f, 0), 0.3f, 0.05f, 0.33f, 6);
            return b.Build("BroadleafLow");
        }

        /// <summary>Broadleaf tree (bigleaf maple / cottonwood), 1 m tall. Submesh 0 trunk, 1 crown.</summary>
        public static Mesh Broadleaf(int seed)
        {
            var b = new Builder(2);
            b.Frustum(0, Vector3.zero, 0.035f, 0.02f, 0.5f, 6);
            var rnd = new System.Random(seed);
            for (int i = 0; i < 6; i++)
            {
                var c = new Vector3((float)rnd.NextDouble() * 0.3f - 0.15f, 0.5f + (float)rnd.NextDouble() * 0.3f, (float)rnd.NextDouble() * 0.3f - 0.15f);
                float r = 0.14f + (float)rnd.NextDouble() * 0.08f;
                b.Frustum(1, c + Vector3.down * r * 0.7f, r * 0.5f, r, r * 0.7f, 8, 0.3f, seed + i);
                b.Frustum(1, c, r, r * 0.35f, r * 0.75f, 8, 0.3f, seed + i * 7);
            }
            return b.Build("Broadleaf");
        }

        /// <summary>A corn plant, 1 m tall: stalk plus long arching leaves.</summary>
        public static Mesh Corn(int seed)
        {
            var b = new Builder(1);
            var rnd = new System.Random(seed);
            b.Frustum(0, Vector3.zero, 0.009f, 0.004f, 0.92f, 5);
            for (int i = 0; i < 12; i++)
            {
                float y = 0.06f + i * 0.068f;
                float a = i * 2.4f + (float)rnd.NextDouble() * 0.6f;
                // broad, arching leaves that hide the stalk, shorter toward the top
                var dir = new Vector3(Mathf.Cos(a), 0.75f - i * 0.03f, Mathf.Sin(a));
                b.Leaf(0, new Vector3(0, y, 0), dir, 0.62f - i * 0.03f, 0.1f, 0.22f, 6);
            }
            return b.Build("Corn");
        }

        /// <summary>Broad-leaved low crop (potato, squash, clover): a mound of drooping leaves, 1 m tall.</summary>
        public static Mesh LeafyClump(int seed, int leaves = 14, float leafWidth = 0.35f)
        {
            var b = new Builder(1);
            var rnd = new System.Random(seed);
            for (int i = 0; i < leaves; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2;
                float up = 0.6f + (float)rnd.NextDouble() * 0.9f;
                var dir = new Vector3(Mathf.Cos(a), up, Mathf.Sin(a));
                b.Leaf(0, new Vector3(Mathf.Cos(a) * 0.05f, 0, Mathf.Sin(a) * 0.05f), dir, 0.7f + (float)rnd.NextDouble() * 0.4f, leafWidth, 0.35f, 4);
            }
            return b.Build("LeafyClump");
        }

        /// <summary>Grass or grain tuft: thin upright blades, 1 m tall.</summary>
        public static Mesh Tuft(int seed, int blades = 16, float spread = 0.12f)
        {
            var b = new Builder(1);
            var rnd = new System.Random(seed);
            for (int i = 0; i < blades; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2;
                var basePos = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (float)rnd.NextDouble() * spread;
                var dir = new Vector3(Mathf.Cos(a) * 0.25f, 1, Mathf.Sin(a) * 0.25f);
                b.Leaf(0, basePos, dir, 0.8f + (float)rnd.NextDouble() * 0.3f, 0.035f, 0.12f + (float)rnd.NextDouble() * 0.15f, 3);
            }
            return b.Build("Tuft");
        }

        /// <summary>Rounded shrub, 1 m tall.</summary>
        public static Mesh Shrub(int seed)
        {
            var b = new Builder(1);
            var rnd = new System.Random(seed);
            for (int i = 0; i < 5; i++)
            {
                var c = new Vector3((float)rnd.NextDouble() * 0.5f - 0.25f, 0.15f + (float)rnd.NextDouble() * 0.3f, (float)rnd.NextDouble() * 0.5f - 0.25f);
                float r = 0.25f + (float)rnd.NextDouble() * 0.15f;
                b.Frustum(0, c + Vector3.down * r * 0.5f, r * 0.6f, r, r * 0.5f, 8, 0.35f, seed + i);
                b.Frustum(0, c, r, r * 0.3f, r * 0.6f, 8, 0.35f, seed + i * 3);
            }
            return b.Build("Shrub");
        }

        /// <summary>Gable-roofed farmhouse, real size (12 x 9 m). Submesh 0 walls, 1 roof, 2 trim.</summary>
        public static Mesh House(float w = 12, float d = 9, float wallH = 5.5f, float roofH = 3.5f)
        {
            var b = new Builder(3);
            b.Box(0, new Vector3(0, wallH / 2, 0), new Vector3(w, wallH, d));
            float o = 0.6f;
            var a = new Vector3(-w / 2 - o, wallH, -d / 2 - o); var bb = new Vector3(w / 2 + o, wallH, -d / 2 - o);
            var c = new Vector3(w / 2 + o, wallH + roofH, 0); var dd = new Vector3(-w / 2 - o, wallH + roofH, 0);
            var e = new Vector3(-w / 2 - o, wallH, d / 2 + o); var f = new Vector3(w / 2 + o, wallH, d / 2 + o);
            b.Quad(1, a, dd, c, bb, true); b.Quad(1, f, c, dd, e, true);
            b.Tri(0, new Vector3(-w / 2, wallH, -d / 2), new Vector3(-w / 2, wallH, d / 2), new Vector3(-w / 2, wallH + roofH, 0), true);
            b.Tri(0, new Vector3(w / 2, wallH, d / 2), new Vector3(w / 2, wallH, -d / 2), new Vector3(w / 2, wallH + roofH, 0), true);
            // windows and door
            for (int i = 0; i < 3; i++) b.Box(2, new Vector3(-w / 3 + i * w / 3, wallH * 0.55f, -d / 2 - 0.05f), new Vector3(1.4f, 1.4f, 0.12f));
            b.Box(2, new Vector3(w / 2 - 1.5f, 1.1f, d / 2 + 0.05f), new Vector3(1.1f, 2.2f, 0.12f));
            b.Box(2, new Vector3(w / 4, wallH + roofH * 0.6f, 1.2f), new Vector3(0.8f, roofH * 1.2f, 0.8f)); // chimney
            return b.Build("House");
        }

        public static Mesh Barn() => House(18, 12, 7, 5);

        /// <summary>Unit fence post (0.12 x 1.2 x 0.12) with a rail stub toward +X.</summary>
        public static Mesh FencePost()
        {
            var b = new Builder(1);
            b.Box(0, new Vector3(0, 0.6f, 0), new Vector3(0.12f, 1.2f, 0.12f));
            b.Box(0, new Vector3(2f, 1.0f, 0), new Vector3(4f, 0.08f, 0.04f));
            b.Box(0, new Vector3(2f, 0.55f, 0), new Vector3(4f, 0.08f, 0.04f));
            return b.Build("FencePost");
        }

        public static Mesh Sign()
        {
            var b = new Builder(2);
            b.Box(0, new Vector3(-0.9f, 1.1f, 0), new Vector3(0.12f, 2.2f, 0.12f));
            b.Box(0, new Vector3(0.9f, 1.1f, 0), new Vector3(0.12f, 2.2f, 0.12f));
            b.Box(1, new Vector3(0, 1.8f, 0), new Vector3(2.4f, 0.9f, 0.08f));
            return b.Build("Sign");
        }

        // ================================================================ alpha-card foliage
        static Vector3 SoftNormal(Vector3 faceNormal) => (faceNormal * 0.35f + Vector3.up).normalized;

        /// <summary>Crossed vertical cards (grass, grain, leafy crops), 1 m wide and 1 m tall, UV 0..1.</summary>
        public static Mesh Cards(int planes = 3, float width = 1f, float height = 1f, bool flatTop = false, int seed = 0)
        {
            var b = new Builder(1);
            var rnd = new System.Random(seed);
            for (int i = 0; i < planes; i++)
            {
                float a = i * Mathf.PI / planes + (float)rnd.NextDouble() * 0.3f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * width * 0.5f;
                var nrm = SoftNormal(Vector3.Cross(dir, Vector3.up).normalized);
                b.QuadUV(0, -dir, -dir + Vector3.up * height, dir + Vector3.up * height, dir,
                    new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), nrm);
            }
            if (flatTop)
            {
                float y = height * 0.55f, h = width * 0.5f;
                b.QuadUV(0, new Vector3(-h, y, -h), new Vector3(-h, y, h), new Vector3(h, y, h), new Vector3(h, y, -h),
                    new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), Vector3.up);
            }
            return b.Build("Cards");
        }

        /// <summary>Fir tree, 1 m tall: bark trunk (submesh 0) and drooping branch cards in tiers (submesh 1).</summary>
        public static Mesh FirTree(int seed)
        {
            var b = new Builder(2);
            var rnd = new System.Random(seed);
            b.Frustum(0, Vector3.zero, 0.022f, 0.004f, 0.97f, 7);
            int tiers = 11;
            for (int t = 0; t < tiers; t++)
            {
                float f = t / (float)(tiers - 1);
                float y = 0.12f + f * 0.8f;
                float len = Mathf.Lerp(0.27f, 0.06f, f) * (0.9f + (float)rnd.NextDouble() * 0.2f);
                int count = t < tiers - 2 ? 7 : 5;
                for (int k = 0; k < count; k++)
                {
                    float a = (k + (float)rnd.NextDouble() * 0.5f + t * 0.37f) / count * Mathf.PI * 2;
                    var outDir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    var side = Vector3.Cross(Vector3.up, outDir) * len * 0.28f;
                    var root = new Vector3(0, y, 0);
                    var tip = root + outDir * len + Vector3.down * len * (0.35f + 0.2f * (float)rnd.NextDouble());
                    var nrm = SoftNormal(Vector3.Cross(outDir, side).normalized);
                    // the branch picture has its stem on the right, tip on the left
                    b.QuadUV(1, root - side, tip - side, tip + side, root + side, new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), nrm);
                }
            }
            // leader at the top
            var top = new Vector3(0, 0.9f, 0);
            for (int k = 0; k < 2; k++)
            {
                var sd = (k == 0 ? Vector3.right : Vector3.forward) * 0.05f;
                b.QuadUV(1, top - sd, top - sd + Vector3.up * 0.12f, top + sd + Vector3.up * 0.12f, top + sd, new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), Vector3.up);
            }
            return b.Build("FirTree");
        }

        /// <summary>Broadleaf tree, 1 m tall: trunk and limbs (submesh 0), leaf-cluster cards in the crown (submesh 1).</summary>
        public static Mesh LeafTree(int seed)
        {
            var b = new Builder(2);
            var rnd = new System.Random(seed);
            b.Frustum(0, Vector3.zero, 0.04f, 0.025f, 0.5f, 7);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2 + (float)rnd.NextDouble();
                var c = new Vector3(Mathf.Cos(a) * 0.08f, 0.45f, Mathf.Sin(a) * 0.08f);
                b.Frustum(0, c, 0.018f, 0.008f, 0.3f, 5);
            }
            for (int i = 0; i < 26; i++)
            {
                // points in an egg-shaped crown
                var p = new Vector3((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1);
                if (p.sqrMagnitude > 1) { i--; continue; }
                var c = new Vector3(p.x * 0.33f, 0.66f + p.y * 0.26f, p.z * 0.33f);
                float s = 0.2f + (float)rnd.NextDouble() * 0.1f;
                float a = (float)rnd.NextDouble() * Mathf.PI;
                for (int k = 0; k < 2; k++)
                {
                    var d = new Vector3(Mathf.Cos(a + k * Mathf.PI / 2), 0, Mathf.Sin(a + k * Mathf.PI / 2)) * s;
                    var nrm = SoftNormal((c - new Vector3(0, 0.6f, 0)).normalized);
                    b.QuadUV(1, c - d - Vector3.up * s, c - d + Vector3.up * s, c + d + Vector3.up * s, c + d - Vector3.up * s,
                        new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), nrm);
                }
            }
            return b.Build("LeafTree");
        }

        /// <summary>Flat water surface made of quads for each water cell.</summary>
        public static Mesh WaterCells(List<Vector3> centres, float cell)
        {
            var b = new Builder(1);
            float h = cell * 0.5f;
            foreach (var c in centres)
                b.Quad(0, c + new Vector3(-h, 0, -h), c + new Vector3(-h, 0, h), c + new Vector3(h, 0, h), c + new Vector3(h, 0, -h));
            return b.Build("Water");
        }

        public static Mesh Plane(float size)
        {
            var b = new Builder(1);
            float h = size / 2;
            b.Quad(0, new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h));
            return b.Build("Plane");
        }
    }
}
