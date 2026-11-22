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

        // ------------------------------------------------------------------ walls: blocks snap together

        /// <summary>How close (side to side) a block let go off the chain has to be to a spot against another block to snap into it (m).</summary>
        public const float SnapReach = 1.3f;
        /// <summary>How high over the spot it can still be hanging and snap down into it (m).</summary>
        public const float SnapDrop = 2.5f;
        /// <summary>Test switch: off = blocks are set down wherever they're let go.</summary>
        public static bool SnapOn = true;
        /// <summary>The block it last snapped against (for the test).</summary>
        [System.NonSerialized] public ConcreteBlock LastSnapTo;

        bool Upright => Vector3.Dot(transform.up, Vector3.up) > 0.985f;
        /// <summary>Near enough upright to build against (one on bumpy dirt leans a little).</summary>
        bool UprightEnough => Vector3.Dot(transform.up, Vector3.up) > 0.96f;
        /// <summary>Set down and still (on the ground, on a wall, on a deck): something to build against.</summary>
        bool SetDown => HookedBy == null && (Pinned || Registered || ChainedTo != null || Body.isKinematic || Body.IsSleeping() || Body.linearVelocity.sqrMagnitude < 0.01f);

        static float Yaw(Transform t) { Vector3 f = Vector3.ProjectOnPlane(t.forward, Vector3.up); return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; }
        public float Heading => Yaw(transform);

        /// <summary>A block's size seen from another one it's turned `turn` degrees (a multiple of 90) from: (across, up, along).</summary>
        static Vector3 SizeIn(Vector3 s, float turn) => Mathf.RoundToInt(Mathf.Abs(turn) / 90f) % 2 == 1 ? new Vector3(s.z, s.y, s.x) : s;

        /// <summary>
        /// Let go off the chain near a block that's set down: it squares up to that block and goes into the nearest
        /// spot against it (end to end, side by side, or on top: flush or half over the joint below, like brickwork).
        /// So a wall lines up off the first block put down, block by block. Returns whether it snapped.
        /// </summary>
        public bool SnapToWall()
        {
            LastSnapTo = null;
            if (HookedBy != null || !FindSnap(out var bestP, out var bestQ, out var bestB)) return false;
            var body = Body;
            transform.SetPositionAndRotation(bestP + Vector3.up * 0.004f, bestQ);
            body.position = transform.position; body.rotation = bestQ;
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            lastPos = transform.position; lastRot = bestQ; still = 0f;
            LastSnapTo = bestB;
            NetDirty = true;
            return true;
        }

        /// <summary>
        /// Where this block would go into a wall if it were let go now (also while it hangs on the chain, for the
        /// green outline): the nearest spot, measured side to side, against a block that's set down, with this one
        /// anywhere from a little below it to <see cref="SnapDrop"/> above it. Over a block it goes on top; beside
        /// one, end to end or side by side.
        /// </summary>
        public bool FindSnap(out Vector3 bestP, out Quaternion bestQ, out ConcreteBlock bestB)
        {
            bestP = default; bestQ = default; bestB = null;
            if (!SnapOn || ChainedTo != null || !NetMine) return false;
            Vector3 here = transform.position; // (the bottom middle)
            float myYaw = Yaw(transform);
            float bd = float.MaxValue;
            var cand = new List<Vector3>();
            foreach (var o in all)
            {
                if (o == null || o == this || !o.UprightEnough || !o.SetDown) continue;
                Vector3 N = o.Size;
                Vector3 off = o.transform.position - here;
                if (new Vector2(off.x, off.z).magnitude > (N.magnitude + Size.magnitude) * 0.5f + SnapReach + 0.5f) continue;
                if (off.y > 0.6f || off.y < -(SnapDrop + N.y + 0.5f)) continue;
                float turn = Mathf.Round(Mathf.DeltaAngle(Yaw(o.transform), myYaw) / 90f) * 90f;
                Quaternion q = o.transform.rotation * Quaternion.Euler(0f, turn, 0f);
                Vector3 e = SizeIn(Size, turn);
                cand.Clear();
                float fx = (N.x - e.x) * 0.5f, fz = (N.z - e.z) * 0.5f;
                var xs = Mathf.Abs(fx) > 0.02f ? new[] { 0f, fx, -fx } : new[] { 0f };
                var zs = Mathf.Abs(fz) > 0.02f ? new[] { 0f, fz, -fz } : new[] { 0f };
                foreach (int sgn in new[] { -1, 1 })
                {
                    foreach (float x in xs) cand.Add(new Vector3(x, 0f, sgn * (N.z + e.z) * 0.5f)); // end to end
                    foreach (float z in zs) cand.Add(new Vector3(sgn * (N.x + e.x) * 0.5f, 0f, z)); // side by side
                }
                var tops = new List<float>(zs) { N.z * 0.5f, -N.z * 0.5f };                         // on top
                foreach (float x in xs) foreach (float z in tops) cand.Add(new Vector3(x, N.y, z));
                foreach (var l in cand)
                {
                    Vector3 p = o.transform.TransformPoint(l);
                    // side to side how far, and it has to be over the spot (hanging above it) or about level with it
                    float flat = new Vector2(p.x - here.x, p.z - here.z).magnitude, rise = here.y - p.y;
                    if (flat > SnapReach || rise < -0.35f || rise > SnapDrop) continue;
                    float d = flat + Mathf.Abs(rise) * 0.1f;
                    if (d >= bd) continue;
                    if (l.y < 0.01f) p = OnGround(p);
                    if (!Free(p, q)) continue;
                    if (l.y > 0.01f && !Supported(p, q)) continue;
                    bd = d; bestP = p; bestQ = q; bestB = o;
                }
            }
            return bestB != null;
        }

        /// <summary>Beside a block on the ground: on the dirt there if it's higher than the block beside it stands.</summary>
        static Vector3 OnGround(Vector3 p)
        {
            var site = DigSite.At(p);
            if (site != null && site.Contains(p)) { float g = site.SurfaceY(p); if (g > p.y + 0.02f) p.y = g; }
            return p;
        }

        /// <summary>Nothing in the way there: no other block, no machine.</summary>
        bool Free(Vector3 p, Quaternion q)
        {
            int n = Physics.OverlapBoxNonAlloc(p + q * Vector3.up * (Size.y * 0.5f + 0.01f), Size * 0.5f - Vector3.one * 0.04f, near, q, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = near[i];
                if (c == null || c.transform.IsChildOf(transform)) continue;
                if (c.GetComponentInParent<ConcreteBlock>() != null || c.attachedRigidbody != null) return false;
            }
            return true;
        }

        /// <summary>On top of the wall: both its ends sit on blocks (it doesn't hang off the end of the course below).</summary>
        bool Supported(Vector3 p, Quaternion q)
        {
            Vector3 along = q * Vector3.forward, up = q * Vector3.up;
            foreach (float f in new[] { -0.38f, 0.38f })
            {
                Vector3 at = p + along * (Size.z * f) + up * 0.06f;
                bool hit = false;
                foreach (var h in Physics.RaycastAll(at, -up, 0.2f, ~0, QueryTriggerInteraction.Ignore))
                    if (!h.collider.transform.IsChildOf(transform) && h.collider.GetComponentInParent<ConcreteBlock>() != null) { hit = true; break; }
                if (!hit) return false;
            }
            return true;
        }

        /// <summary>
        /// Squared up face to face against another block (snapped into a wall, or the block a wall was started
        /// from): any face touching another's, the two lined up to within a few degrees.
        /// </summary>
        public bool InWall()
        {
            if (!Upright) return false;
            Vector3 me = transform.position;
            foreach (var o in all)
            {
                if (o == null || o == this || !o.Upright) continue;
                if ((o.transform.position - me).sqrMagnitude > 16f) continue;
                float rel = Mathf.DeltaAngle(Yaw(o.transform), Yaw(transform));
                float turn = Mathf.Round(rel / 90f) * 90f;
                if (Mathf.Abs(rel - turn) > 3f) continue;
                Vector3 e = SizeIn(Size, turn), N = o.Size;
                Vector3 l = o.transform.InverseTransformPoint(me);
                // how far apart the two boxes are along each of its axes (below 0 = they overlap that much)
                float gx = Mathf.Abs(l.x) - (N.x + e.x) * 0.5f;
                float gz = Mathf.Abs(l.z) - (N.z + e.z) * 0.5f;
                float gy = Mathf.Max(l.y - N.y, -(l.y + e.y));
                int faces = 0, over = 0;
                foreach (float gap in new[] { gx, gy, gz })
                {
                    if (gap >= -0.03f && gap <= 0.08f) faces++;
                    else if (gap < -0.05f) over++;
                }
                if (faces == 1 && over == 2) return true;
            }
            return false;
        }

        /// <summary>
        /// Blocks lying about: not in a wall, not on the chain, not on a trailer or truck (chained or just loaded).
        /// </summary>
        public static List<ConcreteBlock> Loose()
        {
            var l = new List<ConcreteBlock>();
            foreach (var b in all)
                if (b != null && b.ChainedTo == null && b.HookedBy == null && !b.CarrierBelow(out _) && !b.InWall()) l.Add(b);
            return l;
        }

        /// <summary>The fraction of the price the yard gives back for a loose block it collects.</summary>
        public const float CollectRefund = 0.5f;
        /// <summary>Multiplayer: these blocks are gone (sent to everyone before they're removed).</summary>
        public static System.Action<List<ConcreteBlock>> NetRemoved;

        /// <summary>The block yard collects every loose block (half its price back). Returns how many.</summary>
        public static int CollectLoose(out float refund)
        {
            var l = Loose();
            refund = 0f;
            foreach (var b in l) refund += b.Info.price * CollectRefund;
            if (l.Count == 0) return 0;
            NetRemoved?.Invoke(l);
            foreach (var b in l) { b.Unregister(); b.gameObject.SetActive(false); Destroy(b.gameObject); }
            Economy.Instance?.Earn(refund);
            return l.Count;
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

        // ------------------------------------------------------------------ set down, it stays put

        /// <summary>
        /// Set down on the ground it's pinned where it stands: dirt and clods landing round it, the ground rising beside
        /// it, a block knocked against it don't shift it. Only equipment moves it (a machine pushing it, a block on a
        /// chain swung into it, hooking it), or the ground going from under it.
        /// </summary>
        public bool Pinned { get; private set; }
        float pinCheck;

        void Pin()
        {
            if (Pinned || !NetMine || HookedBy != null || ChainedTo != null) return;
            var body = Body;
            body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            Pinned = true;
        }

        void Unpin()
        {
            if (!Pinned) return;
            Pinned = false;
            if (ChainedTo == null) { Body.isKinematic = !NetMine; Body.WakeUp(); }
        }

        /// <summary>Is this collider part of a machine (or something on one's chain)? Those move a set-down block.</summary>
        static bool IsEquipment(Collider c)
        {
            if (c == null) return false;
            if (c.GetComponentInParent<VehicleBase>() != null || c.GetComponentInParent<Trailer>() != null) return true;
            var b = c.GetComponentInParent<ConcreteBlock>();
            return b != null && (b.HookedBy != null || b.ChainedTo != null);
        }

        static readonly Collider[] near = new Collider[32];
        bool TouchingEquipment()
        {
            Vector3 h = Size * 0.5f + Vector3.one * 0.06f;
            int n = Physics.OverlapBoxNonAlloc(transform.position + transform.up * (Size.y * 0.5f), h, near, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (!near[i].transform.IsChildOf(transform) && IsEquipment(near[i])) return true;
            return false;
        }

        void OnCollisionEnter(Collision c) => Shoved(c);
        void OnCollisionStay(Collision c) => Shoved(c);
        void Shoved(Collision c)
        {
            if (c.collider != null) LastTouch = $"{c.collider.name} ({(c.collider.attachedRigidbody != null ? c.collider.attachedRigidbody.name : "static")}), pinned {Pinned}";
            if (!Pinned || !IsEquipment(c.collider)) return;
            Unpin(); Unregister(); still = 0f; Shoves++;
        }
        /// <summary>How many times equipment has knocked a set-down block loose (for the test).</summary>
        public static int Shoves;
        /// <summary>What last touched a block (for the test).</summary>
        public static string LastTouch = "";

        /// <summary>Pinned: the ground dug (or slumped) from under it lets it drop.</summary>
        void PinGround(float dt)
        {
            if ((pinCheck -= dt) > 0f) return;
            pinCheck = 0.25f;
            var site = restSite ?? DigSite.At(transform.position);
            if (site == null || OnABlock()) return;
            // the ground under its corners (whichever way up it lies) against its lowest corner
            Vector3 h = Size * 0.5f;
            float hi = float.MinValue, lowY = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = transform.TransformPoint(new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? 0f : Size.y, (i & 4) == 0 ? -h.z : h.z));
                lowY = Mathf.Min(lowY, p.y);
                if (site.Contains(p)) hi = Mathf.Max(hi, site.SurfaceY(p));
            }
            if (hi > float.MinValue && hi < lowY - 0.12f) { Unpin(); Unregister(); still = 0f; }
        }

        void RestCheck(float dt)
        {
            if (Pinned && (HookedBy != null || ChainedTo != null || !NetMine)) Unpin();
            if (Pinned) PinGround(dt);
            bool moving = (transform.position - lastPos).sqrMagnitude > 1e-6f || Quaternion.Angle(transform.rotation, lastRot) > 0.05f;
            lastPos = transform.position; lastRot = transform.rotation;
            // holding the ground, it lets go only when it's really moved (lifted, shoved, toppled), not for a
            // millimetre of settling: let go, the dirt round it ran in under it and kept nudging it
            if (Registered && HookedBy == null && ChainedTo == null)
            {
                // pinned once nothing of a machine is against it (a block pinned against a bucket or a track would
                // be an immovable lump the machine jolts off)
                if (!Pinned && NetMine && (pinCheck -= dt) <= 0f) { pinCheck = 0.3f; if (!TouchingEquipment()) Pin(); }
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
            // (measured from its lowest corner, so a block knocked over onto its side still holds its ground)
            Vector3 h = Size * 0.5f;
            Vector3 low = transform.position; float lowY = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = transform.TransformPoint(new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? 0f : Size.y, (i & 4) == 0 ? -h.z : h.z));
                if (c.y < lowY) { lowY = c.y; low = c; }
            }
            if (lowY - site.SurfaceY(low) > 0.25f && !OnABlock()) { still = -2f; return; }
            restCells.Clear();
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
            pinCheck = 0f; // (set down: pinned as soon as no machine is against it; only equipment moves it then)
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
            Unpin();
            if (restSite == null) return;
            restSite.RemoveSolid(this);
            restSite = null;
            restCells.Clear();
        }
    }
}
