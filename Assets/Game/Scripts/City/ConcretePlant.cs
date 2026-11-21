using System.Collections.Generic;
using FPSBase.Core;
using FPSBase.Vehicles;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>
    /// The concrete batch plant beside the block yard: back a mixer truck in under the loading chute, order how
    /// many cubic metres you want (here at the control house, or with the load key from the cab) and pay; the plant
    /// charges the drum while it turns. Concrete stays workable for a while after it's batched.
    /// </summary>
    public class ConcretePlant : Interactable
    {
        public static ConcretePlant Instance { get; private set; }
        public static readonly List<ConcretePlant> All = new List<ConcretePlant>();

        [Tooltip("The loading chute's mouth: the truck's hopper goes under it.")]
        public Transform chute;
        public float pricePerM3 = 145f;
        [Tooltip("m³ a second into the drum.")]
        public float loadRate = 0.7f;
        [Tooltip("How far the hopper can be off the chute (m, flat).")]
        public float reach = 1.4f;
        public Material concreteMaterial;

        float owed;          // ordered, not in the drum yet (m³)
        MixerTruck target;
        Transform stream;
        bool warned;
        MixerTruck panelFor;

        public bool Loading => owed > 0.001f;
        public float Owed => owed;

        void Awake() { Instance = this; }
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            if (concreteMaterial == null || chute == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(go.GetComponent<Collider>());
            go.name = "Concrete stream";
            go.GetComponent<MeshRenderer>().sharedMaterial = concreteMaterial;
            go.SetActive(false);
            stream = go.transform;
        }

        public override string Prompt => $"Concrete plant: back a mixer truck's hopper in under the chute   (${pricePerM3:0} a m³)";
        public override void Interact(PlayerInteractor player) { panelFor = null; GameUI.Instance.OpenPanel("Concrete Plant", Draw); }

        /// <summary>The order panel, for the truck under the chute (from its cab).</summary>
        public void OpenFor(MixerTruck m) { panelFor = m; GameUI.Instance.OpenPanel("Concrete Plant", Draw); }

        /// <summary>Is this truck's hopper under this plant's chute?</summary>
        public bool IsUnder(MixerTruck m)
        {
            if (m == null || m.hopper == null || chute == null) return false;
            Vector3 d = chute.position - m.hopper.position;
            return new Vector2(d.x, d.z).magnitude <= reach && d.y > -0.3f && d.y < 3f;
        }

        /// <summary>The plant this truck is parked under, if any.</summary>
        public static ConcretePlant Under(MixerTruck m)
        {
            foreach (var p in All) if (p != null && p.IsUnder(m)) return p;
            return null;
        }

        public MixerTruck TruckUnder()
        {
            foreach (var v in VehicleBase.All) if (v is MixerTruck m && IsUnder(m)) return m;
            return null;
        }

        /// <summary>Order `m3` for the truck under the chute and pay for it. False if it can't.</summary>
        public bool Order(MixerTruck m, float m3, out string why)
        {
            why = null;
            if (m == null || !IsUnder(m)) { why = "No mixer truck under the chute"; return false; }
            if (m.GoneOff) { why = "The concrete in that drum has gone off: wash it out first"; return false; }
            m3 = Mathf.Min(m3, m.Room - owed);
            if (m3 < 0.05f) { why = "The drum's full"; return false; }
            float cost = m3 * pricePerM3;
            if (Economy.Instance == null || !Economy.Instance.TrySpend(cost)) { why = $"Not enough money (${cost:N0})"; return false; }
            if (target != m) owed = 0f;
            target = m; owed += m3; warned = false;
            GameUI.Toast($"Batching {m3:0.#} m³ of concrete (${cost:N0}): keep the truck under the chute");
            return true;
        }

        void Draw()
        {
            var m = panelFor != null && IsUnder(panelFor) ? panelFor : TruckUnder();
            GUILayout.Label("Ready-mix concrete for your footings and slabs. Back a mixer truck in so the hopper on top at the back is under the chute, then order. You pay up front; the drum is charged while you wait.");
            GUILayout.Space(4);
            GUILayout.Label($"${pricePerM3:0} a cubic metre. It stays workable for {(m != null ? m.workableSeconds : 1200f) / 60f:0} minutes after it's batched: pour it before it goes off.");
            GUILayout.Space(6);
            if (m == null)
            {
                GUILayout.Label("<color=#e8a060>No mixer truck under the chute.</color> (Buy one at the equipment dealer: Trucks, Concrete Mixer Truck.)");
                return;
            }
            float room = Mathf.Max(0f, m.Room - owed);
            GUILayout.Label($"Under the chute: {m.displayName}, {m.Concrete:0.0} / {m.drumCapacity:0} m³" +
                (m.GoneOff ? "  <color=#e06050>(gone off: wash it out)</color>" : m.Concrete > 0.01f ? $"  (workable for {m.WorkableLeft / 60f:0} more min)" : ""));
            if (Loading) GUILayout.Label($"Charging the drum... {owed:0.0} m³ to go");
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            foreach (float v in new[] { 1f, 2f, 4f, 6f })
            {
                GUI.enabled = room >= v - 0.001f && !m.GoneOff;
                if (GUILayout.Button($"{v:0} m³  ${v * pricePerM3:N0}", GUILayout.Height(34)) && !Order(m, v, out string why)) GameUI.Toast(why);
            }
            GUI.enabled = room > 0.1f && !m.GoneOff;
            float fill = Mathf.Floor(room * 10f) / 10f;
            if (GUILayout.Button(room > 0.1f ? $"Fill it  ({fill:0.0} m³, ${fill * pricePerM3:N0})" : "Full", GUILayout.Height(34)) && !Order(m, fill, out string why2)) GameUI.Toast(why2);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        void Update()
        {
            bool flowing = false;
            if (owed > 0.001f)
            {
                if (target == null) { Refund(); }
                else if (!IsUnder(target))
                {
                    if (!warned) { warned = true; GameUI.Toast($"The truck moved out from under the chute: loading stopped ({owed:0.0} m³ still to come; back it in again)"); }
                }
                else
                {
                    warned = false;
                    float give = target.AddConcrete(Mathf.Min(owed, loadRate * Time.deltaTime));
                    owed -= give;
                    target.loadingUntil = Time.time + 0.6f;
                    flowing = give > 0f;
                    if (give <= 0f) Refund(); // full, or gone off
                    else if (owed <= 0.001f) { owed = 0f; GameUI.Toast($"Drum loaded: {target.Concrete:0.0} m³. Workable for {target.workableSeconds / 60f:0} minutes"); }
                }
            }
            DrawStream(flowing);
        }

        void Refund()
        {
            if (owed > 0.001f && Economy.Instance != null)
            {
                Economy.Instance.Earn(owed * pricePerM3);
                GameUI.Toast($"Refunded ${owed * pricePerM3:N0} for {owed:0.0} m³ that didn't go in");
            }
            owed = 0f; target = null;
        }

        void DrawStream(bool on)
        {
            if (stream == null) return;
            if (!on) { if (stream.gameObject.activeSelf) stream.gameObject.SetActive(false); return; }
            Vector3 a = chute.position, b = target != null && target.hopper != null ? target.hopper.position : a + Vector3.down * 1.5f;
            float drop = Mathf.Max(0.1f, a.y - b.y);
            stream.gameObject.SetActive(true);
            stream.SetPositionAndRotation(new Vector3(a.x, a.y - drop * 0.5f, a.z), Quaternion.identity);
            float w = 0.28f + Mathf.PerlinNoise(Time.time * 5f, 0.7f) * 0.06f;
            stream.localScale = new Vector3(w, drop * 0.5f, w);
        }
    }
}
