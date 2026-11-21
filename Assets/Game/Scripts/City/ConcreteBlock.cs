using System.Collections.Generic;
using FPSBase.Core;
using FPSBase.Mining;
using FPSBase.Vehicles;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>
    /// A precast concrete landscape block (the interlocking "lock blocks" a retaining wall is stacked from), bought at
    /// the landscape supply. It has a steel lifting loop cast into the top: hook an excavator's bucket chain on it and
    /// lift it. On a trailer or truck it slides about until you chain it down. Resting on the dig ground it's a wall:
    /// dirt piled against it stays behind it instead of slumping through.
    /// </summary>
    public class ConcreteBlock : Interactable
    {
        public struct Kind
        {
            public string name, note; public Vector3 size; public float kg, price;
        }

        /// <summary>What the landscape supply sells (sizes are x across, y up, z along the block).</summary>
        public static readonly Kind[] Kinds =
        {
            new Kind { name = "Half block 2×2×2 ft", note = "ends and steps", size = new Vector3(0.61f, 0.61f, 0.61f), kg = 545f, price = 55f },
            new Kind { name = "Lock block 2×2×4 ft", note = "the standard wall block", size = new Vector3(0.61f, 0.61f, 1.22f), kg = 1090f, price = 95f },
            new Kind { name = "Long block 2×2×6 ft", note = "long runs, fewer joints", size = new Vector3(0.61f, 0.61f, 1.83f), kg = 1635f, price = 135f },
            new Kind { name = "Big block 2.5×2.5×5 ft", note = "the bottom course of a tall wall", size = new Vector3(0.76f, 0.76f, 1.52f), kg = 2120f, price = 175f },
            new Kind { name = "Cap block 1×2×4 ft", note = "a flat top course", size = new Vector3(0.61f, 0.3f, 1.22f), kg = 545f, price = 70f },
            new Kind { name = "Barrier block 3×3×6 ft", note = "the heavy one: bin walls and stockpile bays", size = new Vector3(0.91f, 0.91f, 1.83f), kg = 3650f, price = 260f },
        };

        public int kind;
        [Tooltip("The lifting loop's top (where a chain hooks on).")]
        public Transform eye;

        static readonly List<ConcreteBlock> all = new();
        public static IReadOnlyList<ConcreteBlock> All => all;

        public Kind Info => Kinds[Mathf.Clamp(kind, 0, Kinds.Length - 1)];
        public Vector3 Size => Info.size;
        public Rigidbody Body => rb != null ? rb : (rb = GetComponent<Rigidbody>());
        Rigidbody rb;
        Collider[] cols;

        // ---- multiplayer: whoever last moved it runs it; on everyone else's game it's a copy that follows
        public int NetId;
        [System.NonSerialized] public bool NetMine = true;
        [System.NonSerialized] public int NetSeq;
        Vector3 netPos; Quaternion netRot = Quaternion.identity; bool netHas;
        /// <summary>Set when something about it changed that the others must hear about straight away.</summary>
        [System.NonSerialized] public bool NetDirty = true;
        public static System.Action<ConcreteBlock> NetClaimed;

        /// <summary>The chain it hangs from (an excavator's bucket), if any.</summary>
        [System.NonSerialized] public BucketChain HookedBy;
        /// <summary>Chained down to a trailer deck or a truck bed: rides on it.</summary>
        public Transform ChainedTo { get; private set; }
        Collider[] chainedAgainst;
        /// <summary>The trailer whose deck it was unchained on by hand: it isn't chained again by itself until lifted off.</summary>
        [System.NonSerialized] public Transform NoAutoChain;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            cols = GetComponentsInChildren<Collider>();
            if (rb != null) rb.mass = Info.kg;
            if (NetId == 0) NetId = Random.Range(1, int.MaxValue);
        }
        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDisable() { all.Remove(this); Unregister(); }

        public override string Prompt => ChainedTo != null ? $"{Info.name}  (chained down)" : HookedBy != null ? $"{Info.name}  (on the chain)" : $"{Info.name}  ({Info.kg:N0} kg)";

        public override void Interact(PlayerInteractor player)
        {
            if (ChainedTo != null) Unchain();
            else if (CarrierBelow(out var c)) ChainTo(c);
            else GameUI.Toast($"{Info.name}, {Info.kg:N0} kg: lift it by its loop with an excavator's bucket chain");
        }

        public override void GetOptions(List<InteractOption> list)
        {
            if (ChainedTo != null)
            {
                list.Add(new InteractOption { label = "Undo the chains", run = _ => Unchain() });
                var deck = ChainedTo;
                list.Add(new InteractOption { label = "Undo the chains on every block here", run = _ => { foreach (var b in OnCarrier(deck)) b.Unchain(); } });
            }
            else
            {
                bool can = CarrierBelow(out var c);
                list.Add(new InteractOption { label = "Chain it down", enabled = can, note = can ? null : "only on a trailer deck or a truck bed", run = _ => { if (CarrierBelow(out var c2)) ChainTo(c2); } });
                if (can)
                    list.Add(new InteractOption { label = "Chain down every block on this load", run = _ => { foreach (var b in new List<ConcreteBlock>(all)) if (b.ChainedTo == null && b.HookedBy == null && b.CarrierBelow(out var c3) && c3.root == c.root) b.ChainTo(c3); } });
            }
        }

        static List<ConcreteBlock> OnCarrier(Transform deck)
        {
            var l = new List<ConcreteBlock>();
            foreach (var b in all) if (b.ChainedTo != null && b.ChainedTo.root == deck.root) l.Add(b);
            return l;
        }

        // ------------------------------------------------------------------ chaining down

        /// <summary>Is it sitting on a trailer deck or a truck bed (or on blocks that are)? `deck` = what it would be chained to.</summary>
        public bool CarrierBelow(out Transform deck)
        {
            deck = null;
            Vector3 p = transform.position + Vector3.up * 0.05f;
            var hits = Physics.RaycastAll(p, Vector3.down, Size.y + 0.6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance >= best) continue;
                var other = h.collider.GetComponentInParent<ConcreteBlock>();
                if (other != null) { if (other.ChainedTo != null) { best = h.distance; deck = other.ChainedTo; } continue; }
                var tr = h.collider.GetComponentInParent<Trailer>();
                var v = h.collider.GetComponentInParent<VehicleBase>();
                if (tr == null && v == null) { best = h.distance; deck = null; continue; } // the ground
                best = h.distance;
                deck = tr != null ? (tr.tiltDeck != null ? tr.tiltDeck : tr.transform) : DeckOf(v);
            }
            return deck != null;
        }

        /// <summary>What a block chained onto this machine rides on: a dump truck's box (it tips with it), else the machine.</summary>
        public static Transform DeckOf(VehicleBase v) => v is DumpPickup pk && pk.bedContents != null ? pk.bedContents.parent : v.transform;

        /// <summary>Chain it to `deck`: it rides on it, still and solid, until it's unchained.</summary>
        public void ChainTo(Transform deck, bool fromNet = false)
        {
            if (deck == null || ChainedTo == deck) return;
            if (HookedBy != null) HookedBy.Release();
            if (!fromNet) Claim();
            Unregister();
            ChainedTo = deck;
            transform.SetParent(deck, true);
            var body = Body;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            // (it's part of the load now: it mustn't shove its own trailer about)
            chainedAgainst = deck.root.GetComponentsInChildren<Collider>(true);
            foreach (var a in cols) foreach (var b in chainedAgainst) if (a != null && b != null && !(b is CharacterController)) Physics.IgnoreCollision(a, b, true);
            NetDirty = true;
            if (!fromNet) GameUI.Toast($"Chained down the {Info.name.ToLower()}");
        }

        public void Unchain(bool fromNet = false)
        {
            if (ChainedTo == null) return;
            if (!fromNet) Claim();
            var deckBody = ChainedTo.GetComponentInParent<Rigidbody>();
            if (!fromNet) NoAutoChain = ChainedTo.root;
            transform.SetParent(null, true);
            if (chainedAgainst != null)
                foreach (var a in cols) foreach (var b in chainedAgainst) if (a != null && b != null && !(b is CharacterController)) Physics.IgnoreCollision(a, b, false);
            chainedAgainst = null;
            ChainedTo = null;
            var body = Body;
            body.isKinematic = !NetMine;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            if (!body.isKinematic && deckBody != null) body.linearVelocity = deckBody.GetPointVelocity(transform.position);
            NetDirty = true;
            if (!fromNet) GameUI.Toast($"Unchained the {Info.name.ToLower()}");
        }

        // ------------------------------------------------------------------ multiplayer

        /// <summary>This game runs it now (we hooked, chained or bought it).</summary>
        public void Claim()
        {
            if (!NetMine) { NetMine = true; NetSeq++; if (ChainedTo == null) Body.isKinematic = false; }
            NetDirty = true;
            NetClaimed?.Invoke(this);
        }

        /// <summary>A friend runs it: it follows what they send.</summary>
        public void NetSetRemote(int seq)
        {
            NetSeq = seq; NetMine = false;
            if (HookedBy != null && !HookedBy.RemoteDriven) HookedBy.Release(true);
            Body.isKinematic = true;
        }

        public void NetTarget(Vector3 p, Quaternion q) { netPos = p; netRot = q; netHas = true; }

        void Update()
        {
            if (!NetMine && netHas && ChainedTo == null)
            {
                float k = 1f - Mathf.Exp(-Time.deltaTime * 14f);
                if ((transform.position - netPos).sqrMagnitude > 25f) transform.SetPositionAndRotation(netPos, netRot);
                else transform.SetPositionAndRotation(Vector3.Lerp(transform.position, netPos, k), Quaternion.Slerp(transform.rotation, netRot, k));
            }
            RestCheck(Time.deltaTime);
        }

        // ------------------------------------------------------------------ holding back the dirt

        DigSite restSite;
        readonly List<int> restCells = new();
        float still;
        Vector3 lastPos; Quaternion lastRot = Quaternion.identity;
        public bool Registered => restSite != null;
        Vector3 pressedAt = new Vector3(1e9f, 0f, 1e9f), regPos;
        Quaternion regRot = Quaternion.identity;
        /// <summary>How far the loose dirt under it went down when it was last set down (m, for the test).</summary>
        public float LastSink { get; private set; }

        void RestCheck(float dt)
        {
            bool moving = (transform.position - lastPos).sqrMagnitude > 1e-6f || Quaternion.Angle(transform.rotation, lastRot) > 0.05f;
            lastPos = transform.position; lastRot = transform.rotation;
            // holding the ground, it lets go only when it's really moved (lifted, shoved, toppled), not for a
            // millimetre of settling: let go, the dirt round it ran in under it and kept nudging it
            if (Registered && HookedBy == null && ChainedTo == null)
            {
                if ((transform.position - regPos).sqrMagnitude > 0.0009f || Quaternion.Angle(transform.rotation, regRot) > 2f) { Unregister(); still = 0f; }
                return;
            }
            if (moving || HookedBy != null || ChainedTo != null) { still = 0f; if (Registered) Unregister(); return; }
            still += dt;
            if (!Registered && still > 0.6f) Register();
        }

        /// <summary>The ground squares under it: dirt can't flow into them, and they hold up to its top.</summary>
        void Register()
        {
            var site = DigSite.At(transform.position);
            if (site == null || !site.Contains(transform.position)) return;
            // only standing on the dirt (or on blocks that do): resting on a machine or a heap of something else it holds nothing
            if (transform.position.y - site.SurfaceY(transform.position) > 0.25f && !OnABlock()) { still = -2f; return; }
            restCells.Clear();
            Vector3 h = Size * 0.5f;
            float top = float.MinValue;
            var b = new Bounds(transform.position, Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = transform.TransformPoint(new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z) + Vector3.up * h.y);
                b.Encapsulate(c); top = Mathf.Max(top, c.y);
            }
            site.CellsUnder(b, p =>
            {
                Vector3 l = transform.InverseTransformPoint(p);
                return Mathf.Abs(l.x) <= h.x && Mathf.Abs(l.z) <= h.z;
            }, restCells);
            if (restCells.Count == 0) return;
            restSite = site;
            site.AddSolid(this, restCells, top);
            regPos = transform.position; regRot = transform.rotation;
            // set down on loose dirt: its weight presses it in (once per spot it's put down on: it settling a few
            // centimetres into what it pressed doesn't press again)
            Vector3 flat = new Vector3(transform.position.x, 0f, transform.position.z);
            if (HookedBy == null && (flat - pressedAt).sqrMagnitude > 0.09f)
            {
                pressedAt = flat;
                LastSink = site.PressUnder(restCells, Info.kg);
                if (LastSink > 0.005f)
                {
                    // it goes down with the dirt it pressed, straight away (dropped onto it, it moved, let go of the
                    // ground, and the dirt round it ran back in under it)
                    transform.position += Vector3.down * LastSink;
                    if (Body != null) { Body.position = transform.position; Body.linearVelocity = Vector3.zero; }
                    lastPos = transform.position;
                    site.AddSolid(this, restCells, top - LastSink);
                    regPos = transform.position;
                }
            }
        }

        bool OnABlock()
        {
            foreach (var h in Physics.RaycastAll(transform.position + Vector3.up * 0.1f, Vector3.down, 0.4f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                var o = h.collider.GetComponentInParent<ConcreteBlock>();
                if (o != null && o.Registered) return true;
            }
            return false;
        }

        void Unregister()
        {
            if (restSite == null) return;
            restSite.RemoveSolid(this);
            restSite = null;
            restCells.Clear();
        }
    }
}
