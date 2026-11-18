using FPSBase.Core;
using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>Equipment dealer at the outpost: buy bigger excavators and trucks. Bought machines appear on the sales lot.</summary>
    public class VehicleDealer : Interactable
    {
        [System.Serializable]
        public class Listing
        {
            public string name;
            [TextArea] public string description;
            public float price;
            public GameObject prefab;
            [Tooltip("Big machines are delivered to the heavy-equipment yard instead of the outpost lot.")]
            public bool heavy;
            public string category = "Machines";
        }

        public Listing[] listings;
        public Transform[] lotSpots;
        public Transform[] heavyLotSpots;
        Vector2 scroll;

        public override string Prompt => "Equipment dealer  (excavators & trucks)";

        public override void Interact(PlayerInteractor player) => GameUI.Instance.OpenPanel("Equipment Dealer", Draw);

        void Draw()
        {
            var eco = Economy.Instance;
            GUILayout.Label(eco.Unlimited ? "Your money: unlimited (test mode — everything is free)" : $"Your money: ${eco.Money:N0}");
            GUILayout.Space(6);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Min(560f, Screen.height - 260f)));
            string cat = null;
            foreach (var l in listings)
            {
                if (l.category != cat)
                {
                    cat = l.category;
                    GUILayout.Space(4);
                    GUILayout.Label($"<b>{cat}</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 18 });
                }
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"<b>{l.name}</b>   —   ${l.price:N0}", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16 });
                GUILayout.Label(l.description, new GUIStyle(GUI.skin.label) { wordWrap = true });
                GUI.enabled = eco.CanAfford(l.price) && l.prefab != null;
                if (GUILayout.Button($"Buy for ${l.price:N0}", GUILayout.Height(30)) && eco.TrySpend(l.price)) Deliver(l);
                GUI.enabled = true;
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();
        }

        void Deliver(Listing l)
        {
            bool heavy = l.heavy && heavyLotSpots != null && heavyLotSpots.Length > 0;
            var spots = heavy ? heavyLotSpots : lotSpots;
            float clear = heavy ? 5f : 2.5f;
            // size it up, then pick the first lot spot it fits on (or the nearest clear ground around the lot)
            var go = Instantiate(l.prefab, spots[0].position + Vector3.down * 500f, spots[0].rotation);
            go.name = l.name;
            var lb = FPSBase.Vehicles.VehicleSpot.LocalBounds(go.transform);
            Vector3 at = Vector3.zero; float yaw = spots[0].eulerAngles.y; bool found = false;
            foreach (var s in spots)
            {
                Vector3 g = GroundAt(s.position);
                if (FPSBase.Vehicles.VehicleSpot.IsClear(g, s.eulerAngles.y, lb, go.transform)) { at = g; yaw = s.eulerAngles.y; found = true; break; }
            }
            if (!found) at = FPSBase.Vehicles.VehicleSpot.FindFree(spots[0].position, yaw, lb, go.transform, 60f);
            go.transform.SetPositionAndRotation(at + Vector3.up * 0.5f, Quaternion.Euler(0f, yaw, 0f));
            var grb = go.GetComponent<Rigidbody>();
            if (grb != null) { grb.position = go.transform.position; grb.rotation = go.transform.rotation; }
            Physics.SyncTransforms();
            FPSBase.Net.NetSession.NotifySpawn(go, System.Array.IndexOf(listings, l));
            GameUI.Toast(heavy ? $"{l.name} delivered to the heavy-equipment yard (east of the wash plant)!" : $"{l.name} delivered to the sales lot!");
            GameUI.Instance.ClosePanel();
        }

        /// <summary>Ground height under a lot spot (the dirt field may have been dug or piled up since).</summary>
        public static Vector3 GroundAt(Vector3 p)
        {
            var site = DigSite.Instance;
            if (site != null && site.Contains(p)) { p.y = site.SurfaceY(p); return p; }
            // highest static surface (skip parked machines and anything else with a rigidbody)
            float best = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(p + Vector3.up * 60f, Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.rigidbody == null && hit.point.y > best) best = hit.point.y;
            if (best > float.NegativeInfinity) p.y = best;
            return p;
        }
    }
}
