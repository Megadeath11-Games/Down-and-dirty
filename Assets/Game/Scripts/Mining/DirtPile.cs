using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>A mound of dumped dirt that keeps its gold. Can be dug back up. Nearby dumps merge into one pile.</summary>
    public class DirtPile : MonoBehaviour, IMaterialReceiver
    {
        static readonly List<DirtPile> all = new();
        public static IReadOnlyList<DirtPile> All => all;

        public MaterialLoad load;
        float radius, height;

        public float FreeSpace => float.MaxValue;
        public bool AcceptsAt(Vector3 worldPoint) => true;
        public void Receive(MaterialLoad l) { load.Add(l); Reshape(); }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public static void DropAt(Vector3 ground, MaterialLoad l)
        {
            if (l.IsEmpty) return;
            foreach (var p in all)
            {
                Vector3 d = p.transform.position - ground; d.y = 0f;
                if (d.magnitude < p.radius + 0.8f) { p.Receive(l); return; }
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "DirtPile";
            Destroy(go.GetComponent<SphereCollider>());
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            if (DigSite.Instance != null && DigSite.Instance.pileMaterial != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = DigSite.Instance.pileMaterial;
            go.transform.position = ground;
            var pile = go.AddComponent<DirtPile>();
            pile.load = l;
            pile.Reshape();
        }

        public static DirtPile FindContaining(Vector3 point)
        {
            foreach (var p in all)
            {
                Vector3 d = point - p.transform.position;
                float hd = new Vector2(d.x, d.z).magnitude;
                if (hd > p.radius) continue;
                float top = p.transform.position.y + p.height * (1f - (hd / p.radius) * (hd / p.radius));
                if (point.y < top && point.y > p.transform.position.y - 0.5f) return p;
            }
            return null;
        }

        public MaterialLoad Dig(float maxVolume)
        {
            var taken = load.Take(maxVolume);
            if (load.volume < 0.01f) { Destroy(gameObject); return taken; }
            Reshape();
            return taken;
        }

        void Reshape()
        {
            // Half-ellipsoid with height = half the radius: V = (2/3) pi r^2 h = pi r^3 / 3
            radius = Mathf.Max(0.25f, Mathf.Pow(3f * load.volume / Mathf.PI, 1f / 3f));
            height = radius * 0.5f;
            transform.localScale = new Vector3(radius * 2f, height * 2f, radius * 2f);
        }
    }
}
