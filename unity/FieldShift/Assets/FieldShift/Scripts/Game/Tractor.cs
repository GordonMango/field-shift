using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FieldShift
{
    /// <summary>
    /// A drivable farm tractor. Drive it over a field that is waiting for work and the seeder or harvester on
    /// the back covers the field strip by strip; when 90% is covered the job is done.
    /// </summary>
    public class Tractor : MonoBehaviour
    {
        public const int Cells = 10;                // 10 x 10 coverage cells per 1 ha field
        public const float ImplementWidth = 6f;

        public World world;
        public bool driving;
        public float speed;
        public Transform seeder, harvester, cabCam, chaseCam;
        readonly List<Transform> wheels = new List<Transform>();
        float steer;

        float wheelRadius = 0.9f;
        bool modelWheels;

        public static Tractor Build(World world, Vector3 pos, float yaw)
        {
            if (Models.Has("Tractor")) return BuildFromModels(world, pos, yaw);
            var root = new GameObject("Tractor");
            var t = root.AddComponent<Tractor>();
            t.world = world;
            var body = Mats.Lit(new Color(0.62f, 0.1f, 0.08f), 0.45f);
            var dark = Mats.Lit(new Color(0.08f, 0.08f, 0.09f), 0.2f);
            var glass = Mats.Lit(new Color(0.15f, 0.2f, 0.24f), 0.85f);
            var steel = Mats.Lit(new Color(0.55f, 0.56f, 0.58f), 0.5f);
            var yellow = Mats.Lit(new Color(0.85f, 0.65f, 0.1f), 0.4f);

            Part(root.transform, "Hood", PrimitiveType.Cube, new Vector3(0, 1.35f, 1.1f), new Vector3(1.1f, 1.0f, 2.4f), body);
            Part(root.transform, "Grille", PrimitiveType.Cube, new Vector3(0, 1.3f, 2.32f), new Vector3(1.0f, 0.8f, 0.05f), dark);
            Part(root.transform, "Chassis", PrimitiveType.Cube, new Vector3(0, 0.95f, 0.2f), new Vector3(1.2f, 0.5f, 3.6f), dark);
            Part(root.transform, "Cab", PrimitiveType.Cube, new Vector3(0, 2.25f, -0.9f), new Vector3(1.7f, 1.6f, 1.7f), glass);
            Part(root.transform, "Roof", PrimitiveType.Cube, new Vector3(0, 3.1f, -0.9f), new Vector3(1.9f, 0.12f, 1.9f), body);
            Part(root.transform, "Fenders", PrimitiveType.Cube, new Vector3(0, 1.75f, -0.9f), new Vector3(2.5f, 0.12f, 1.6f), body);
            Part(root.transform, "Exhaust", PrimitiveType.Cylinder, new Vector3(0.4f, 2.6f, 1.9f), new Vector3(0.1f, 0.7f, 0.1f), steel);
            foreach (var sx in new[] { -1f, 1f })
            {
                t.wheels.Add(Part(root.transform, "RearWheel", PrimitiveType.Cylinder, new Vector3(sx * 1.05f, 0.85f, -0.9f), new Vector3(1.7f, 0.28f, 1.7f), dark, new Vector3(0, 0, 90)));
                t.wheels.Add(Part(root.transform, "FrontWheel", PrimitiveType.Cylinder, new Vector3(sx * 0.85f, 0.55f, 1.8f), new Vector3(1.1f, 0.2f, 1.1f), dark, new Vector3(0, 0, 90)));
                Part(root.transform, "Hub", PrimitiveType.Cylinder, new Vector3(sx * 1.2f, 0.85f, -0.9f), new Vector3(0.7f, 0.02f, 0.7f), yellow, new Vector3(0, 0, 90));
            }

            // implements hitched behind
            t.seeder = new GameObject("Seeder").transform; t.seeder.SetParent(root.transform, false);
            Part(t.seeder, "Frame", PrimitiveType.Cube, new Vector3(0, 0.7f, -3.6f), new Vector3(ImplementWidth, 0.25f, 0.4f), yellow);
            Part(t.seeder, "Hopper", PrimitiveType.Cube, new Vector3(0, 1.2f, -3.6f), new Vector3(ImplementWidth * 0.8f, 0.7f, 0.7f), body);
            for (int i = 0; i < 8; i++) Part(t.seeder, "Disc", PrimitiveType.Cylinder, new Vector3(-ImplementWidth / 2 + 0.4f + i * 0.74f, 0.35f, -3.8f), new Vector3(0.5f, 0.02f, 0.5f), steel, new Vector3(0, 0, 90));
            Part(t.seeder, "Hitch", PrimitiveType.Cube, new Vector3(0, 0.8f, -2.6f), new Vector3(0.15f, 0.15f, 1.6f), dark);

            t.harvester = new GameObject("Harvester").transform; t.harvester.SetParent(root.transform, false);
            Part(t.harvester, "Header", PrimitiveType.Cube, new Vector3(0, 0.6f, -4.0f), new Vector3(ImplementWidth, 0.9f, 1.2f), steel);
            Part(t.harvester, "Body", PrimitiveType.Cube, new Vector3(1.6f, 1.4f, -4.4f), new Vector3(1.4f, 1.6f, 1.8f), yellow);
            Part(t.harvester, "Spout", PrimitiveType.Cylinder, new Vector3(2.4f, 2.8f, -4.0f), new Vector3(0.3f, 1.2f, 0.3f), yellow, new Vector3(0, 0, -35));
            Part(t.harvester, "Hitch", PrimitiveType.Cube, new Vector3(0, 0.8f, -2.7f), new Vector3(0.15f, 0.15f, 1.8f), dark);
            t.harvester.gameObject.SetActive(false);

            t.cabCam = new GameObject("CabCam").transform; t.cabCam.SetParent(root.transform, false);
            t.cabCam.localPosition = new Vector3(-0.2f, 2.55f, -0.7f);
            t.chaseCam = new GameObject("ChaseCam").transform; t.chaseCam.SetParent(root.transform, false);
            t.chaseCam.localPosition = new Vector3(0, 5.5f, -11f);
            t.chaseCam.localRotation = Quaternion.Euler(16, 0, 0);

            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 1.5f, 0); col.size = new Vector3(2.6f, 3f, 5f);
            t.Place(pos, yaw);
            return t;
        }

        /// <summary>The Blender tractor with a hitched seeder and harvester.</summary>
        static Tractor BuildFromModels(World world, Vector3 pos, float yaw)
        {
            var root = new GameObject("Tractor");
            var t = root.AddComponent<Tractor>();
            t.world = world;
            var body = Models.Spawn("Tractor", "Grille", null, false);
            body.transform.SetParent(root.transform, false);
            foreach (var n in new[] { "WheelRL", "WheelRR", "WheelFL", "WheelFR" })
            {
                var w = Models.Find(body.transform, n);
                if (w != null) t.wheels.Add(w);
            }
            var bb = Models.Bounds(body);
            var hitch = new Vector3(0, 0.6f, root.transform.InverseTransformPoint(bb.center).z - bb.extents.z + 0.2f);
            t.seeder = Hitch(root.transform, "Seeder", hitch);
            t.harvester = Hitch(root.transform, "Harvester", hitch);
            if (t.harvester != null) t.harvester.gameObject.SetActive(false);
            t.cabCam = new GameObject("CabCam").transform; t.cabCam.SetParent(root.transform, false);
            t.cabCam.localPosition = new Vector3(0f, 2.65f, -0.35f);
            t.chaseCam = new GameObject("ChaseCam").transform; t.chaseCam.SetParent(root.transform, false);
            t.chaseCam.localPosition = new Vector3(0, 5.2f, -13f);
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 1.6f, 0); col.size = new Vector3(2.8f, 3.2f, 6.4f);
            t.implementOffset = Mathf.Abs(hitch.z) + 2.6f;
            t.wheelRadius = 0.95f;
            t.modelWheels = true;
            t.Place(pos, yaw);
            return t;
        }

        /// <summary>Attach an implement so the tip of its tongue sits on the tractor's drawbar.</summary>
        static Transform Hitch(Transform tractor, string model, Vector3 hitch)
        {
            var go = Models.Spawn(model, "Tongue", null, false);
            if (go == null) return null;
            go.transform.SetParent(tractor, false);
            var tongue = Models.Find(go.transform, "Tongue");
            var tb = Models.Bounds(tongue != null ? tongue.gameObject : go);
            var tip = tractor.InverseTransformPoint(new Vector3(tb.center.x, tb.min.y, tb.max.z));
            go.transform.localPosition += new Vector3(hitch.x - tip.x, 0, hitch.z - tip.z);
            return go.transform;
        }

        public float implementOffset = 3.8f;

        static Transform Part(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        public void Place(Vector3 pos, float yaw)
        {
            transform.position = new Vector3(pos.x, world.H(pos.x, pos.z), pos.z);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            speed = 0;
        }

        /// <summary>Which implement to show: the seeder for planting jobs, the harvester for harvest jobs.</summary>
        public void ShowImplement(string job)
        {
            bool harvest = job == "harvest";
            if (seeder != null) seeder.gameObject.SetActive(!harvest);
            if (harvester != null) harvester.gameObject.SetActive(harvest);
        }

        public Vector3 ImplementCentre => transform.position - transform.forward * implementOffset;

        void Update()
        {
            if (!driving) { speed = Mathf.MoveTowards(speed, 0, 6 * Time.deltaTime); return; }
            float throttle = 0, turn = 0;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttle -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) turn += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) turn -= 1;
            }
#else
            throttle = Input.GetAxisRaw("Vertical"); turn = Input.GetAxisRaw("Horizontal");
#endif
            float max = throttle >= 0 ? 9f : 3f;
            speed = Mathf.MoveTowards(speed, throttle * max, (throttle == 0 ? 5f : 3.5f) * Time.deltaTime);
            steer = Mathf.MoveTowards(steer, turn * 35f, 90 * Time.deltaTime);
            // bicycle model: turn rate from speed, wheelbase and steering angle
            float yawRate = speed / 2.7f * Mathf.Tan(steer * Mathf.Deg2Rad) * Mathf.Rad2Deg;
            transform.Rotate(0, yawRate * Time.deltaTime, 0, Space.World);
            var p = transform.position + Flat(transform.forward) * speed * Time.deltaTime;
            // stay on the terrain, tilt with the ground
            float hF = world.H(p.x + transform.forward.x * 1.5f, p.z + transform.forward.z * 1.5f), hB = world.H(p.x - transform.forward.x * 1.5f, p.z - transform.forward.z * 1.5f);
            float hR = world.H(p.x + transform.right.x * 1f, p.z + transform.right.z * 1f), hL = world.H(p.x - transform.right.x * 1f, p.z - transform.right.z * 1f);
            p.y = (hF + hB + hR + hL) / 4;
            float pitch = Mathf.Atan2(hB - hF, 3f) * Mathf.Rad2Deg, roll = Mathf.Atan2(hL - hR, 2f) * Mathf.Rad2Deg;
            transform.SetPositionAndRotation(p, Quaternion.Euler(pitch, transform.eulerAngles.y, roll));
            float spin = speed / Mathf.Max(0.3f, wheelRadius) * Mathf.Rad2Deg * Time.deltaTime;
            foreach (var w in wheels) w.Rotate(modelWheels ? spin : 0, modelWheels ? 0 : speed * Time.deltaTime * 90f, 0, Space.Self);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.normalized; }
    }
}
