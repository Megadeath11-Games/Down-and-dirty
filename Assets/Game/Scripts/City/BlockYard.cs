using System.Collections.Generic;
using FPSBase.Core;
using FPSBase.Vehicles;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>
    /// The landscape supply's block yard: concrete retaining-wall blocks in several sizes. Park a trailer or a dump
    /// truck in the loading bay, buy blocks at the counter, and the yard loads them onto it for you (loose: chain
    /// them down before you drive off). With nothing in the bay they're set down on the pad beside it.
    /// </summary>
    public class BlockYard : Interactable
    {
        public static BlockYard Instance { get; private set; }

        [Tooltip("One prefab per ConcreteBlock.Kinds entry.")]
        public GameObject[] prefabs;
        [Tooltip("The loading bay: its centre, facing along the bay; size in its local x (width), z (length).")]
        public Transform bay;
        public Vector2 baySize = new Vector2(6f, 14f);
        [Tooltip("Where blocks go when nothing's parked in the bay.")]
        public Transform groundDrop;

        void Awake() { Instance = this; }

        public override string Prompt => "Block yard: concrete wall blocks. Park a trailer or truck in the bay and they load it for you";
        public override void Interact(PlayerInteractor player) => GameUI.Instance.OpenPanel("Block Yard", Draw);

        /// <summary>A copy of a block (bought, from a save or from a friend).</summary>
        public static ConcreteBlock Spawn(int kind, Vector3 p, Quaternion q, int netId = 0)
        {
            var y = Instance;
            if (y == null || y.prefabs == null || kind < 0 || kind >= y.prefabs.Length || y.prefabs[kind] == null) return null;
            var go = Instantiate(y.prefabs[kind], p, q);
            var b = go.GetComponent<ConcreteBlock>();
            if (netId != 0) b.NetId = netId;
            return b;
        }

        // ------------------------------------------------------------------ what's in the bay

        /// <summary>The load surface parked in the bay: its transform (blocks are laid out in its local x/z) and size.</summary>
        bool FindCarrier(out Transform deck, out Vector2 size, out float topY, out string what, out float payloadKg)
        {
            deck = null; size = default; topY = 0f; what = null; payloadKg = 0f;
            if (bay == null) return false;
            foreach (var t in Trailer.All)
            {
                if (t == null || t.deckArea == null || !InBay(t.deckArea.position)) continue;
                deck = t.deckArea; size = t.deckSize; topY = 0f; what = t.displayName;
                payloadKg = t.payloadTons > 0f ? t.payloadTons * 1000f : 8000f;
                return true;
            }
            foreach (var v in VehicleBase.All)
            {
                if (v is not DumpPickup pk || pk.bedContents == null || !InBay(pk.bedContents.position)) continue;
                deck = pk.bedContents.parent; size = new Vector2(pk.bedInnerSize.x, pk.bedInnerSize.z); topY = 0.06f; what = pk.displayName;
                payloadKg = Mathf.Max(800f, pk.capacity * 1500f);
                return true;
            }
            return false;
        }

        bool InBay(Vector3 p)
        {
            Vector3 l = bay.InverseTransformPoint(p);
            return Mathf.Abs(l.x) <= baySize.x * 0.5f && Mathf.Abs(l.z) <= baySize.y * 0.5f && Mathf.Abs(l.y) < 4f;
        }

        static float LoadedKg(Transform deck)
        {
            float kg = 0f;
            foreach (var b in ConcreteBlock.All)
            {
                if (b == null) continue;
                if (b.ChainedTo != null ? b.ChainedTo.root == deck.root : OnDeck(b, deck)) kg += b.Info.kg;
            }
            return kg;
        }

        static bool OnDeck(ConcreteBlock b, Transform deck)
        {
            Vector3 l = deck.InverseTransformPoint(b.transform.position);
            return l.y > -0.3f && l.y < 3f && Mathf.Abs(l.x) < 4f && Mathf.Abs(l.z) < 9f;
        }

        // ------------------------------------------------------------------ buying

        /// <summary>Pay for `n` blocks of `kind` and have them loaded. Returns how many were loaded.</summary>
        public int Buy(int kind, int n)
        {
            var k = ConcreteBlock.Kinds[kind];
            var eco = Economy.Instance;
            if (eco == null) return 0;
            bool onCarrier = FindCarrier(out var deck, out var size, out float topY, out string what, out float payload);
            var spots = new List<Vector3>();
            Quaternion rot;
            if (onCarrier)
            {
                float room = payload - LoadedKg(deck);
                int byWeight = Mathf.FloorToInt(room / k.kg);
                if (byWeight <= 0) { GameUI.Toast($"The {what} can't take any more weight ({room / 1000f:0.0} t of room)"); return 0; }
                n = Mathf.Min(n, byWeight);
                Layout(size, k.size, n, spots, deck, topY);
                rot = deck.rotation;
                if (spots.Count == 0) { GameUI.Toast($"No room on the {what} for a {k.name.ToLower()}"); return 0; }
            }
            else
            {
                var t = groundDrop != null ? groundDrop : transform;
                Layout(new Vector2(8f, 12f), k.size, n, spots, t, 0f);
                rot = t.rotation;
            }
            n = spots.Count;
            float cost = k.price * n;
            if (!eco.TrySpend(cost)) { GameUI.Toast($"Not enough money: {n} × ${k.price:0} = ${cost:N0}"); return 0; }
            foreach (var p in spots)
            {
                var b = Spawn(kind, p, rot);
                if (b == null) continue;
                b.Claim();
            }
            GameUI.Toast(onCarrier
                ? $"Loaded {n} × {k.name.ToLower()} on the {what} (${cost:N0}). Chain them down before you drive off: look at a block, F"
                : $"{n} × {k.name.ToLower()} set down on the pad (${cost:N0}): nothing was parked in the bay");
            return n;
        }

        /// <summary>
        /// Rows of blocks along the deck from the front, side by side, then a second layer on top: world positions
        /// (the bottom middle of each block) in `spots`.
        /// </summary>
        static void Layout(Vector2 deck, Vector3 block, int n, List<Vector3> spots, Transform on, float topY)
        {
            const float gap = 0.06f;
            int across = Mathf.Max(0, Mathf.FloorToInt((deck.x + gap) / (block.x + gap)));
            int along = Mathf.Max(0, Mathf.FloorToInt((deck.y + gap) / (block.z + gap)));
            if (across == 0 || along == 0) return;
            float wUsed = across * block.x + (across - 1) * gap;
            for (int layer = 0; layer < 2 && spots.Count < n; layer++)
                for (int r = 0; r < along && spots.Count < n; r++)
                    for (int c = 0; c < across && spots.Count < n; c++)
                    {
                        float x = -wUsed * 0.5f + block.x * 0.5f + c * (block.x + gap);
                        float z = deck.y * 0.5f - block.z * 0.5f - r * (block.z + gap); // front first (over the axles)
                        float y = topY + layer * (block.y + 0.01f) + 0.02f;
                        spots.Add(on.TransformPoint(new Vector3(x, y, z)));
                    }
        }

        void Draw()
        {
            bool onCarrier = FindCarrier(out var deck, out _, out _, out string what, out float payload);
            GUILayout.Label("Precast concrete wall blocks: they stack like bricks, with a lifting loop on top for an excavator's chain. Stack them on a slope and dirt piled behind them stays put.");
            GUILayout.Space(4);
            GUILayout.Label(onCarrier
                ? $"In the loading bay: the {what}, {(payload - LoadedKg(deck)) / 1000f:0.0} t of room left. Blocks go on loose: chain them down before you drive (look at one, F)."
                : "<color=#e8a060>Nothing in the loading bay: blocks will be set down on the pad.</color> Back a trailer or a dump truck into the painted bay to have them loaded.");
            GUILayout.Label("To unload: undo the chains, then hook each block's loop with an excavator's bucket chain (chain key, H by default) and lift it off.");
            GUILayout.Space(8);
            for (int k = 0; k < ConcreteBlock.Kinds.Length; k++)
            {
                var kd = ConcreteBlock.Kinds[k];
                GUILayout.Label($"<b>{kd.name}</b>   ${kd.price:0} each   {kd.kg:N0} kg   ({kd.note})");
                GUILayout.BeginHorizontal();
                foreach (int m in new[] { 1, 2, 4, 8, 12 })
                    if (GUILayout.Button($"{m}  ${kd.price * m:N0}", GUILayout.Height(30))) Buy(k, m);
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
        }
    }
}
