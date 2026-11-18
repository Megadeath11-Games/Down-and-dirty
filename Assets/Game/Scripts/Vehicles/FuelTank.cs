using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// A diesel transfer tank you drive to your machines: a tidy tank in a pickup bed, or a fuel trailer.
    /// Fill it at the fuel pump; look at a machine parked near it to pump fuel into the machine.
    /// </summary>
    public class FuelTank : MonoBehaviour
    {
        public string label = "Tidy tank";
        public float capacity = 400f;   // liters
        public float liters = 400f;
        [Tooltip("How close a machine has to be parked to reach it with the hose (m).")]
        public float hoseReach = 12f;

        static readonly List<FuelTank> all = new();
        public static IReadOnlyList<FuelTank> All => all;
        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public VehicleBase Carrier => GetComponentInParent<VehicleBase>();

        /// <summary>The fullest tank whose hose reaches `v` (not one riding on `v` itself).</summary>
        public static FuelTank Near(VehicleBase v)
        {
            FuelTank best = null;
            foreach (var t in all)
            {
                if (t == null || t.Carrier == v) continue;
                if (Vector3.Distance(t.transform.position, v.transform.position) > t.hoseReach + 2f) continue;
                if (best == null || t.liters > best.liters) best = t;
            }
            return best;
        }

        /// <summary>Pump from this tank into `v`: returns the liters moved.</summary>
        public float FillMachine(VehicleBase v)
        {
            float moved = Mathf.Min(liters, v.fuelCapacity - v.fuel);
            if (moved <= 0f) return 0f;
            liters -= moved;
            v.Refuel(moved);
            return moved;
        }

        /// <summary>
        /// Builds a simple tidy tank (a steel box tank with a pump and hose reel on top) under `parent`, its
        /// bottom-centre at `localPos`, extending forward (+z) by size.z. Size in meters: width, height, length.
        /// </summary>
        public static FuelTank BuildTidyTank(Transform parent, Vector3 localPos, Vector3 size, float capacity)
        {
            var root = new GameObject("Tidy tank");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            var steel = Mat(new Color(0.62f, 0.64f, 0.62f), 0.55f);
            var red = Mat(new Color(0.7f, 0.12f, 0.08f), 0.4f);
            var black = Mat(new Color(0.06f, 0.06f, 0.06f), 0.3f);
            var r = root.transform;
            Part(r, PrimitiveType.Cube, new Vector3(0f, size.y * 0.5f, 0f), Vector3.zero, size, steel);
            Part(r, PrimitiveType.Cube, new Vector3(0f, size.y * 0.5f, 0f), Vector3.zero, new Vector3(size.x + 0.02f, 0.03f, size.z + 0.02f), black);
            Part(r, PrimitiveType.Cylinder, new Vector3(size.x * 0.32f, size.y + 0.02f, -size.z * 0.2f), Vector3.zero, new Vector3(0.12f, 0.025f, 0.12f), black);
            // pump + hose reel
            Part(r, PrimitiveType.Cube, new Vector3(-size.x * 0.28f, size.y + 0.1f, 0f), Vector3.zero, new Vector3(0.22f, 0.2f, 0.18f), red);
            Part(r, PrimitiveType.Cylinder, new Vector3(size.x * 0.08f, size.y + 0.17f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.32f, 0.12f, 0.32f), red);
            Part(r, PrimitiveType.Cylinder, new Vector3(size.x * 0.08f, size.y + 0.17f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.26f, 0.125f, 0.26f), black);
            var t = root.AddComponent<FuelTank>();
            t.capacity = capacity; t.liters = capacity;
            return t;
        }

        static void Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localEulerAngles = euler; go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        static readonly Dictionary<Color, Material> mats = new();
        static Material Mat(Color c, float smooth)
        {
            if (mats.TryGetValue(c, out var m) && m != null) return m;
            var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            m = new Material(sh) { name = "TidyTank" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.4f);
            mats[c] = m;
            return m;
        }
    }
}
