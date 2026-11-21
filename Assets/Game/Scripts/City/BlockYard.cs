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

        /// <summary>What's parked in the bay to be loaded (for the test).</summary>
        public bool CarrierInBay(out string what, out Transform deck) => FindCarrier(out deck, out _, out _, out what, out _);

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
                Layout(new Vector2(5f, 10f), k.size, n, spots, t, 0f);
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
            Physics.SyncTransforms(); // (bought again straight away: the next lot sees these)
            GameUI.Toast(onCarrier
                ? $"Loaded {n} × {k.name.ToLower()} on the {what} (${cost:N0}). Chain them down before you drive off: look at a block, F"
                : $"{n} × {k.name.ToLower()} set down on the pad (${cost:N0}): nothing was parked in the bay");
            return n;
        }

        /// <summary>The highest a load of blocks is stacked on a deck (m above it).</summary>
        public const float MaxStack = 1.6f;

        /// <summary>
        /// Rows of blocks along the deck from the front, side by side, then a second layer on top: world positions
        /// (the bottom middle of each block) in `spots`. Only where there's room: blocks already there (an earlier
        /// lot, other sizes) are built round and stacked on, never dropped into.
        /// </summary>
        static void Layout(Vector2 deck, Vector3 block, int n, List<Vector3> spots, Transform on, float topY)
        {
            const float gap = 0.06f;
            int across = Mathf.Max(0, Mathf.FloorToInt((deck.x + gap) / (block.x + gap)));
            int along = Mathf.Max(0, Mathf.FloorToInt((deck.y + gap) / (block.z + gap)));
            if (across == 0 || along == 0) return;
            Physics.SyncTransforms();
            var carrier = on.GetComponentInParent<Rigidbody>();
            float wUsed = across * block.x + (across - 1) * gap;
            int cells = across * along;
            var at = new Vector3[cells];
            var top = new float[cells]; // how high each spot is stacked already (above the deck), < 0 = something else is in the way
            for (int r = 0, i = 0; r < along; r++)
                for (int c = 0; c < across; c++, i++)
                {
                    float x = -wUsed * 0.5f + block.x * 0.5f + c * (block.x + gap);
                    float z = deck.y * 0.5f - block.z * 0.5f - r * (block.z + gap); // front first (over the axles)
                    at[i] = new Vector3(x, topY, z);
                    top[i] = StackTop(on, at[i], block, carrier);
                }
            // the bottom layer first, everywhere there's room, then on top
            for (int layer = 0; layer < 2 && spots.Count < n; layer++)
                for (int i = 0; i < cells && spots.Count < n; i++)
                {
                    float h = top[i];
                    if (h < 0f || (layer == 0) != (h < 0.01f) || h + block.y > MaxStack) continue;
                    Vector3 l = at[i] + Vector3.up * (h + 0.02f);
                    if (!Room(on, l, block)) { top[i] = -1f; continue; }
                    spots.Add(on.TransformPoint(l));
                    top[i] = h + block.y + 0.01f;
                }
        }

        /// <summary>The top of the blocks already stacked on a spot (m above the deck; 0 = empty, -1 = blocked by something that isn't a block).</summary>
        static float StackTop(Transform on, Vector3 l, Vector3 block, Rigidbody carrier)
        {
            Vector3 c = on.TransformPoint(l + Vector3.up * (MaxStack * 0.5f + 0.05f));
            Vector3 half = new Vector3(block.x * 0.5f - 0.06f, MaxStack * 0.5f, block.z * 0.5f - 0.06f);
            var hits = Physics.OverlapBox(c, half, on.rotation, ~0, QueryTriggerInteraction.Ignore);
            float h = 0f;
            foreach (var col in hits)
            {
                var b = col.GetComponentInParent<ConcreteBlock>();
                if (b != null) { h = Mathf.Max(h, on.InverseTransformPoint(b.transform.position + b.transform.up * b.Size.y).y - l.y); continue; }
                if (col.attachedRigidbody != null && col.attachedRigidbody != carrier) return -1f; // (a machine on the deck)
            }
            return h;
        }

        /// <summary>Nothing in the way of a block put down here (other blocks a little off the grid, a machine).</summary>
        static bool Room(Transform on, Vector3 l, Vector3 block)
        {
            Vector3 c = on.TransformPoint(l + Vector3.up * (block.y * 0.5f));
            // anything at all: another block, a machine, or the carrier's own headboard, gooseneck or rails (the deck's under the box)
            return Physics.OverlapBox(c, block * 0.5f - Vector3.one * 0.04f, on.rotation, ~0, QueryTriggerInteraction.Ignore).Length == 0;
        }

        void Draw()
        {
            bool onCarrier = FindCarrier(out var deck, out _, out _, out string what, out float payload);
            GUILayout.Label("Precast concrete wall blocks: they stack like bricks, with a lifting loop on top for an excavator's chain. Stack them on a slope and dirt piled behind them stays put.");
            GUILayout.Space(4);
            GUILayout.Label(onCarrier
                ? $"In the loading bay: the {what}, {(payload - LoadedKg(deck)) / 1000f:0.0} t of room left. Blocks go on loose: chain them down before you drive (look at one, F)."
                : "<color=#e8a060>Nothing in the loading bay: blocks will be set down on the pad.</color> Back a trailer or a dump truck into the painted bay to have them loaded.");
            GUILayout.Label("To unload: undo the chains, then hook each block's loop with an excavator's bucket chain (chain key, H by default) and lift it off. Let one go by a block that's set down and it squares up and goes into the wall; turn it on the chain with , and . (by default). The bay is a drive-through: semis pull straight in and out.");
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
