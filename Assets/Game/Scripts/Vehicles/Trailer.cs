using System.Collections;
using System.Collections.Generic;
using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// A towed trailer: free-rolling wheels on its own rigidbody, coupled to a truck's hitch with a ball-and-socket
    /// style joint. Look at it and scroll for hitch / unhitch, the fuel pump, and on a removable-gooseneck lowboy
    /// (RGN) to detach the neck (it stays on the tractor's fifth wheel and the deck drops to the ground so machines
    /// can drive on over the front) and to hook it back on. Unoccupied machines parked on the deck are chained down
    /// and ride along.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Trailer : Interactable
    {
        public string displayName = "Trailer";
        public HitchType coupler = HitchType.Pintle;
        [Tooltip("The lunette ring / kingpin: meets the truck's hitch point.")]
        public Transform couplerPoint;
        public WheelCollider[] wheels;
        public Transform[] wheelVisuals;
        public float parkBrake = 9000f;
        [Tooltip("Rated payload (t): over this the deck is overloaded.")]
        public float payloadTons = 0f;
        [Tooltip("Tongue jack: wound down to hold the front up when it's unhitched.")]
        public Transform jack;
        public float jackDrop = 0.25f;
        Vector3 jackRest;

        [Header("Equipment trailer: tilt deck (bumper-pull)")]
        [Tooltip("The deck, on its pivot: tilts rear-down so machines drive on over the back.")]
        public Transform tiltDeck;
        public float tiltMax = 14f;
        public float tiltSpeed = 5f;
        [Header("Equipment trailer: folding ramps (gooseneck)")]
        [Tooltip("Ramp hinges at the back of the deck. Stowed standing up; lowered they reach the ground.")]
        public Transform[] ramps;
        [Tooltip("How far the ramps swing from stowed (upright) to lowered (deg).")]
        public float rampSwing = 112f;
        public float rampSpeed = 55f;
        [Tooltip("Display names (start of) of the machines it's built to carry. Empty = anything that fits.")]
        public string[] carriesOnly;

        float tilt, tiltTarget, rampAmt, rampTarget;
        Quaternion tiltRest; Quaternion[] rampRest;
        readonly HashSet<VehicleBase> warnedTooBig = new();
        /// <summary>Deck tilt now (deg, 0 = level for hauling).</summary>
        public float DeckTilt => tilt;
        /// <summary>Ramps: 0 = folded up, 1 = down on the ground.</summary>
        public float RampsDown => rampAmt;
        public bool Moving => Tractor != null && Tractor.GetComponent<Rigidbody>() is Rigidbody trb && !trb.isKinematic && trb.linearVelocity.magnitude > 0.6f
                               || (rb != null && !rb.isKinematic && rb.linearVelocity.magnitude > 0.6f);

        /// <summary>Tilt the deck down (rear on the ground) or back up level. False if it can't now.</summary>
        public bool SetDeckTilt(bool down)
        {
            if (tiltDeck == null) return false;
            tiltTarget = down ? tiltMax : 0f;
            return true;
        }
        /// <summary>Lower or fold up the loading ramps.</summary>
        public bool SetRamps(bool down)
        {
            if (ramps == null || ramps.Length == 0) return false;
            rampTarget = down ? 1f : 0f;
            return true;
        }
        /// <summary>Tests: jump straight to a deck / ramp position.</summary>
        public void SnapDeck() { tilt = tiltTarget; rampAmt = rampTarget; ApplyDeck(); }

        /// <summary>Chained machines ride on this: the tilting deck (so they tilt with it), else the trailer.</summary>
        Transform CargoParent => tiltDeck != null ? tiltDeck : transform;

        public bool CanCarry(VehicleBase v)
        {
            if (carriesOnly == null || carriesOnly.Length == 0) return true;
            foreach (var n in carriesOnly) if (!string.IsNullOrEmpty(n) && v.displayName.StartsWith(n)) return true;
            return false;
        }

        void DeckStep(float dt)
        {
            if (tiltDeck == null && (ramps == null || ramps.Length == 0)) return;
            float t0 = tilt, r0 = rampAmt;
            tilt = Mathf.MoveTowards(tilt, tiltTarget, tiltSpeed * dt);
            rampAmt = Mathf.MoveTowards(rampAmt, rampTarget, rampSpeed / Mathf.Max(1f, rampSwing) * dt);
            if (tilt != t0 || rampAmt != r0) { ApplyDeck(); rb.WakeUp(); }
        }

        void ApplyDeck()
        {
            if (tiltDeck != null) tiltDeck.localRotation = tiltRest * Quaternion.Euler(-tilt, 0f, 0f); // nose up, rear down
            if (ramps != null && rampRest != null)
                for (int i = 0; i < ramps.Length; i++)
                    if (ramps[i] != null) ramps[i].localRotation = rampRest[i] * Quaternion.Euler(-rampSwing * rampAmt, 0f, 0f);
        }

        [Header("Removable gooseneck (RGN lowboy)")]
        public Transform gooseneck;
        [Tooltip("Front of the deck, where the neck locks on.")]
        public Transform deckSocket;
        [Tooltip("On the gooseneck: mates with the deck socket.")]
        public Transform neckSocket;
        [Tooltip("Centre of the load deck (top surface); size in its local x (width), z (length).")]
        public Transform deckArea;
        public Vector2 deckSize = new Vector2(2.6f, 6.4f);

        // ------------------------------------------------------------------ multiplayer
        // Whoever drives the truck a trailer is hitched to runs it and sends where it is and what it's doing; on
        // everyone else's game it's a copy that follows (kinematic, no joint). A parked trailer belongs to whoever
        // used it last (hitched it, unhitched it, or used its menu): `NetSeq` goes up each time someone takes it.

        /// <summary>Network id: scene trailers 1.., bought ones like bought machines; a semi's own lowboy uses minus the semi's id.</summary>
        public int NetId { get; set; }
        public int NetKey => NetId != 0 ? NetId : BuiltOn != null && BuiltOn.NetId != 0 ? -BuiltOn.NetId : 0;
        /// <summary>Another player runs it: a copy that follows their updates.</summary>
        public bool NetRemote { get; private set; }
        /// <summary>Parked: the player whose game last ran it (0 = this game).</summary>
        [System.NonSerialized] public ulong NetHeldBy;
        /// <summary>This game ran it last (sends its updates while parked).</summary>
        [System.NonSerialized] public bool NetMine;
        [System.NonSerialized] public int NetSeq;
        Vector3 netPos; Quaternion netRot; bool netHas;
        float[] netSpin; Vector3 netLast; bool netLastHas;

        /// <summary>This player uses it (menu, hitching): it's ours now, everyone else follows us.</summary>
        public void NetTake()
        {
            if (NetMine && !NetRemote && NetHeldBy == 0) return;
            NetSeq++; NetMine = true; NetHeldBy = 0;
            NetSetRemote(false);
        }

        public void NetSetRemote(bool remote)
        {
            if (remote == NetRemote) return;
            NetRemote = remote;
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (remote)
            {
                if (joint != null) { Destroy(joint); joint = null; }
                rb.isKinematic = true;
                netHas = false; netLastHas = false;
            }
            else
            {
                if (!busy) rb.isKinematic = false;
                // runs here again: put the hitch joint back
                if (Hitched != null && joint == null && NeckOn && !busy)
                {
                    var h = Hitched; Hitched = null;
                    if (h.Attached == this) h.Attached = null;
                    Couple(h, false);
                }
                rb.WakeUp();
            }
        }

        public void NetTarget(Vector3 p, Quaternion q)
        {
            // how fast it's going (from the last two updates), to keep it moving between updates
            float now = Time.unscaledTime, dt = now - netTime;
            if (netHas && dt > 0.02f && dt < 0.6f) { var v = (p - netPos) / dt; netVel = v.sqrMagnitude < 3600f ? Vector3.Lerp(netVel, v, 0.6f) : Vector3.zero; }
            else if (!netHas || dt >= 0.6f) netVel = Vector3.zero;
            netPos = p; netRot = q; netHas = true; netTime = now;
        }
        Vector3 netVel; float netTime;
        Vector3 NetAim => netPos + netVel * Mathf.Min(Time.unscaledTime - netTime, 0.25f);

        /// <summary>What it's hitched to and what it's doing (position and rotation go separately).</summary>
        public void NetWriteState(System.IO.BinaryWriter w)
        {
            var hh = Hitched != null ? Hitched : NeckCarrier;
            int vid = 0, hi = -1;
            if (hh != null && hh.Vehicle != null)
            {
                vid = hh.Vehicle.NetId;
                hi = System.Array.IndexOf(hh.Vehicle.GetComponentsInChildren<Hitch>(true), hh);
            }
            w.Write(vid); w.Write((sbyte)hi); w.Write(NeckOn);
            w.Write(tiltTarget); w.Write(rampTarget); w.Write(Tank != null ? Tank.liters : 0f);
        }

        public void NetReadState(System.IO.BinaryReader r, System.Func<int, VehicleBase> find)
        {
            int vid = r.ReadInt32(); int hi = r.ReadSByte(); bool neck = r.ReadBoolean();
            float tt = r.ReadSingle(), rt = r.ReadSingle(), lit = r.ReadSingle();
            Hitch h = null;
            var v = vid != 0 ? find(vid) : null;
            if (v != null && hi >= 0) { var hs = v.GetComponentsInChildren<Hitch>(true); if (hi < hs.Length) h = hs[hi]; }
            tiltTarget = tiltDeck != null ? Mathf.Clamp(tt, 0f, tiltMax) : 0f;
            rampTarget = ramps != null && ramps.Length > 0 ? Mathf.Clamp01(rt) : 0f;
            if (Tank != null) Tank.liters = Mathf.Clamp(lit, 0f, Tank.capacity);
            if (busy) return;
            if (gooseneck != null && neck != NeckOn)
            {
                if (!neck)
                {
                    if (Hitched == null && h != null && h.Attached == null) { Hitched = h; h.Attached = this; }
                    DetachNeck();
                }
                else AttachNeck(true); // (once the copies line up; the next update tries again)
                return;
            }
            if (!NeckOn) return;
            if (h != Hitched)
            {
                if (Hitched != null) Uncouple();
                if (h != null && h.Attached == null) Couple(h, false);
            }
        }

        static readonly List<Trailer> all = new();
        public static IReadOnlyList<Trailer> All => all;
        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        Rigidbody rb;
        ConfigurableJoint joint;
        float emptyMass;
        Vector3 neckRestPos; Quaternion neckRestRot;
        Transform formerParent;
        bool busy;

        /// <summary>The hitch it's coupled to (null = standing on its own).</summary>
        public Hitch Hitched { get; private set; }
        /// <summary>RGN with the neck detached: the fifth wheel the gooseneck sits on.</summary>
        public Hitch NeckCarrier { get; private set; }
        public bool HasGooseneck => gooseneck != null;
        public bool NeckOn => gooseneck == null || gooseneck.parent == transform;
        public FuelTank Tank => tank != null ? tank : (tank = GetComponentInChildren<FuelTank>());
        FuelTank tank;
        public Rigidbody Body => rb != null ? rb : (rb = GetComponent<Rigidbody>());
        /// <summary>The truck it came with (a semi's own lowboy), even after it's unhitched.</summary>
        public VehicleBase BuiltOn { get; private set; }
        /// <summary>Set right after spawning it: couples to this hitch on its first frame (loading a save).</summary>
        public Hitch PendingCouple { get; set; }
        public static Trailer OwnLowboy(VehicleBase v)
        {
            if (v == null) return null;
            foreach (var t in all) if (t != null && t.BuiltOn == v) return t;
            // not started yet: still riding on the truck it was built on
            return v.GetComponentInChildren<Trailer>(true);
        }

        class Cargo { public VehicleBase v; public Rigidbody rb; public bool kin; public RigidbodyInterpolation interp; public Transform parent; public float mass; public List<WheelCollider> wheelsOff; }
        readonly List<Cargo> cargo = new();
        float scanTimer;
        public int CargoCount => cargo.Count;
        public bool Carries(VehicleBase v) { foreach (var c in cargo) if (c.v == v) return true; return false; }
        public float CargoTons { get { float m = 0f; foreach (var c in cargo) m += c.mass; return m / 1000f; } }

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            emptyMass = rb.mass;
            if (wheels != null && wheels.Length > 0 && wheels[0] != null) wheels[0].ConfigureVehicleSubsteps(5f, 12, 12); // (as the pickup: no lock-ups crossing 5 m/s)
            if (jack != null) jackRest = jack.localPosition;
            // built riding on its truck: never collide with it (the gooseneck sits right in the tractor's body box)
            var pv = transform.parent != null ? transform.parent.GetComponentInParent<VehicleBase>() : null;
            if (pv != null) IgnoreWith(pv.transform);
            if (gooseneck != null) { neckRestPos = gooseneck.localPosition; neckRestRot = gooseneck.localRotation; }
            if (tiltDeck != null) tiltRest = tiltDeck.localRotation;
            if (ramps != null) { rampRest = new Quaternion[ramps.Length]; for (int i = 0; i < ramps.Length; i++) if (ramps[i] != null) rampRest[i] = ramps[i].localRotation; }
        }

        void Start()
        {
            // built riding on its truck (one prefab): stand on its own and couple to the hitch it was built on
            formerParent = transform.parent;
            if (formerParent != null) BuiltOn = formerParent.GetComponentInParent<VehicleBase>();
            if (formerParent != null)
            {
                transform.SetParent(null, true);
                rb.position = transform.position; rb.rotation = transform.rotation;
                rb.isKinematic = false;
                Hitch best = null; float bd = 0.6f;
                foreach (var h in formerParent.GetComponentsInChildren<Hitch>())
                {
                    if (h.type != coupler || h.Attached != null || couplerPoint == null) continue;
                    float d = Vector3.Distance(h.transform.position, couplerPoint.position);
                    if (d < bd) { bd = d; best = h; }
                }
                if (best != null) Couple(best, false);
            }
            rb.isKinematic = false;
            if (Hitched == null && PendingCouple != null && PendingCouple.Attached == null) Couple(PendingCouple, true);
            PendingCouple = null;
            if (Hitched == null) SetBrake(true);
            if (NetRemote) rb.isKinematic = true;
            UpdateMass();
        }

        void UpdateMass()
        {
            float m = emptyMass + CargoTons * 1000f + (Tank != null ? Tank.liters * 0.84f : 0f);
            if (Mathf.Abs(rb.mass - m) > 5f) rb.mass = m;
        }

        // ------------------------------------------------------------------ coupling

        /// <summary>A free hitch of the right kind within `reach` of the coupler.</summary>
        public Hitch FindHitch(float reach = 1.6f)
        {
            if (couplerPoint == null) return null;
            Hitch best = null; float bd = reach;
            foreach (var h in Hitch.All)
            {
                if (h == null || h.type != coupler || h.Attached != null) continue;
                if (h.Vehicle != null && Carries(h.Vehicle)) continue;
                Vector3 d = h.transform.position - couplerPoint.position;
                float dist = new Vector2(d.x, d.z).magnitude + Mathf.Max(0f, Mathf.Abs(d.y) - 0.5f);
                if (dist < bd) { bd = dist; best = h; }
            }
            return best;
        }

        /// <summary>Couple to `h`; `snap` slides the trailer the last bit so the ring drops onto the hook.</summary>
        public void Couple(Hitch h, bool snap)
        {
            if (h == null || joint != null) return;
            if (NetRemote)
            {
                h.Attached = this; Hitched = h;
                SetBrake(false);
                return;
            }
            if (h.Vehicle != null && !h.Vehicle.NetRemote) NetTake(); // our truck: the trailer runs on our game now
            if (snap && couplerPoint != null)
            {
                Vector3 delta = h.transform.position - couplerPoint.position;
                transform.position += delta;
                rb.position = transform.position;
                Physics.SyncTransforms();
            }
            var hb = h.Body;
            joint = gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = hb;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = transform.InverseTransformPoint(couplerPoint != null ? couplerPoint.position : h.transform.position);
            joint.connectedAnchor = hb != null ? hb.transform.InverseTransformPoint(h.transform.position) : h.transform.position;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
            // pitch over crests, swing round corners, a little roll (a fifth wheel rocks fore-aft, a pintle twists)
            joint.lowAngularXLimit = new SoftJointLimit { limit = -32f };
            joint.highAngularXLimit = new SoftJointLimit { limit = 32f };
            joint.angularYLimit = new SoftJointLimit { limit = 85f };
            joint.angularZLimit = new SoftJointLimit { limit = h.type == HitchType.FifthWheel ? 8f : h.type == HitchType.Gooseneck ? 12f : 25f };
            joint.enableCollision = false;
            h.Attached = this;
            Hitched = h;
            // only the bits that meet at the hitch pass through each other (tongue, ring, jack vs the truck's back end):
            // the tank, deck and cabinet still hit the truck, so a jackknife folds into it instead of through it
            var other = h.Vehicle != null ? h.Vehicle.transform : hb != null ? hb.transform : null;
            if (other != null && BuiltOn != null && other == BuiltOn.transform) IgnoreWith(other);
            else IgnoreNear(other, 0.35f);
            SetBrake(false);
            rb.isKinematic = false;
            rb.WakeUp();
        }

        public void Uncouple()
        {
            if (joint != null) Destroy(joint);
            joint = null;
            // the truck it was hitched to collides with it again once the two are clear of each other
            // (a semi's own lowboy never does: its gooseneck lives inside the tractor's body box)
            if (Hitched != null)
            {
                var other = Hitched.Vehicle != null ? Hitched.Vehicle.transform : Hitched.Body != null ? Hitched.Body.transform : null;
                if (other != null && (BuiltOn == null || other != BuiltOn.transform)) rejoin.Add(other);
            }
            if (Hitched != null && Hitched.Attached == this) Hitched.Attached = null;
            Hitched = null;
            SetBrake(true);
        }

        /// <summary>The truck pulling it (or carrying its gooseneck).</summary>
        public VehicleBase Tractor => Hitched != null ? Hitched.Vehicle : NeckCarrier != null ? NeckCarrier.Vehicle : null;

        readonly HashSet<Transform> ignored = new();
        readonly HashSet<Transform> rejoin = new();
        void IgnoreWith(Transform other)
        {
            if (other == null) return;
            ignored.Add(other);
            rejoin.Remove(other);
            IgnorePair(GetComponentsInChildren<Collider>(true), other.GetComponentsInChildren<Collider>(true), true);
        }

        static void IgnorePair(Collider[] a, Collider[] b, bool ignore)
        {
            // never the player (on foot they must bump into it, whatever they were driving when it was hitched)
            foreach (var x in a) foreach (var y in b)
                if (x != null && y != null && x != y && !(x is CharacterController) && !(y is CharacterController)) Physics.IgnoreCollision(x, y, ignore);
        }

        /// <summary>Ignore collisions only between our colliders and `other`'s that are within `gap` of each other now (around the hitch).</summary>
        void IgnoreNear(Transform other, float gap)
        {
            if (other == null) return;
            ignored.Add(other);
            rejoin.Remove(other);
            var mine = GetComponentsInChildren<Collider>(true);
            var theirs = other.GetComponentsInChildren<Collider>(true);
            Physics.SyncTransforms();
            foreach (var x in mine)
            {
                if (x == null || x.isTrigger || x is WheelCollider || x is CharacterController) continue;
                var bx = x.bounds; bx.Expand(gap * 2f);
                foreach (var y in theirs)
                    if (y != null && !y.isTrigger && !(y is WheelCollider) && !(y is CharacterController) && bx.Intersects(y.bounds))
                        Physics.IgnoreCollision(x, y, true);
            }
        }

        float rejoinTimer;
        /// <summary>Unhitched trucks that are now clear of it: collide with them again.</summary>
        void RejoinStep()
        {
            if (rejoin.Count == 0 || Hitched != null || (rejoinTimer -= Time.fixedDeltaTime) > 0f) return;
            rejoinTimer = 0.25f;
            var mine = GetComponentsInChildren<Collider>(true);
            List<Transform> done = null;
            foreach (var other in rejoin)
            {
                if (other == null) { (done ??= new()).Add(other); continue; }
                var theirs = other.GetComponentsInChildren<Collider>(true);
                bool touching = false;
                foreach (var x in mine)
                {
                    if (x == null || x.isTrigger || x is WheelCollider) continue;
                    var bx = x.bounds; bx.Expand(0.1f);
                    foreach (var y in theirs)
                        if (y != null && !y.isTrigger && !(y is WheelCollider) && bx.Intersects(y.bounds)) { touching = true; break; }
                    if (touching) break;
                }
                if (touching) continue;
                IgnorePair(mine, theirs, false);
                ignored.Remove(other);
                (done ??= new()).Add(other);
            }
            if (done != null) foreach (var d in done) rejoin.Remove(d);
        }

        /// <summary>Tests: is it still ignoring collisions with `other`?</summary>
        public bool IgnoresCollisionWith(Transform other) => ignored.Contains(other);

        void SetBrake(bool on)
        {
            if (jack != null) jack.localPosition = jackRest + (on ? Vector3.down * jackDrop : Vector3.zero);
            if (wheels == null) return;
            // (a wheel with exactly zero torque acts braked at low speed: a hair of torque lets it roll free)
            foreach (var w in wheels) if (w != null) { w.brakeTorque = on ? parkBrake : 0f; w.motorTorque = on ? 0f : 1e-4f; }
        }

        // ------------------------------------------------------------------ RGN gooseneck

        /// <summary>Detach the removable gooseneck: it stays on the tractor, the deck front drops to the ground.</summary>
        public bool DetachNeck()
        {
            if (gooseneck == null || !NeckOn || Hitched == null || busy) return false;
            var h = Hitched;
            var tr = h.Vehicle != null ? h.Vehicle.transform : h.Body.transform;
            var neckCols = gooseneck.GetComponentsInChildren<Collider>(true);
            Uncouple();
            h.Attached = this; // the neck still sits on that fifth wheel
            NeckCarrier = h;
            gooseneck.SetParent(tr, true);
            IgnorePair(neckCols, GetComponentsInChildren<Collider>(true), true);
            rb.ResetCenterOfMass();
            var trb = h.Body; if (trb != null) trb.ResetCenterOfMass();
            rb.WakeUp();
            return true;
        }

        /// <summary>Is the tractor's gooseneck backed up to the deck closely enough to lock on?</summary>
        public bool NeckLinedUp(out float dist, out float yaw)
        {
            dist = yaw = 999f;
            if (gooseneck == null || NeckOn || deckSocket == null || neckSocket == null) return false;
            dist = Vector3.Distance(deckSocket.position, neckSocket.position);
            Quaternion want = gooseneck.rotation * Quaternion.Inverse(neckRestRot);
            yaw = Mathf.Abs(Mathf.DeltaAngle(want.eulerAngles.y, transform.eulerAngles.y));
            return dist < 2.2f && yaw < 20f;
        }

        /// <summary>Lock the neck back on: the deck lifts and swings level onto it, then it tows again.</summary>
        public bool AttachNeck(bool instant = false)
        {
            if (!NeckLinedUp(out _, out _) || busy) return false;
            StartCoroutine(LiftOntoNeck(instant ? 0f : 1.4f));
            return true;
        }

        IEnumerator LiftOntoNeck(float seconds)
        {
            busy = true;
            rb.isKinematic = true;
            Vector3 p0 = transform.position; Quaternion r0 = transform.rotation;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Quaternion rot = gooseneck.rotation * Quaternion.Inverse(neckRestRot);
                Vector3 pos = gooseneck.position - rot * neckRestPos;
                float u = Mathf.SmoothStep(0f, 1f, t / seconds);
                transform.SetPositionAndRotation(Vector3.Lerp(p0, pos, u), Quaternion.Slerp(r0, rot, u));
                yield return null;
            }
            {
                Quaternion rot = gooseneck.rotation * Quaternion.Inverse(neckRestRot);
                transform.SetPositionAndRotation(gooseneck.position - rot * neckRestPos, rot);
            }
            var h = NeckCarrier;
            gooseneck.SetParent(transform, false);
            gooseneck.localPosition = neckRestPos; gooseneck.localRotation = neckRestRot;
            NeckCarrier = null;
            if (h != null) { h.Attached = null; var hb = h.Body; if (hb != null) hb.ResetCenterOfMass(); }
            rb.position = transform.position; rb.rotation = transform.rotation;
            Physics.SyncTransforms();
            rb.ResetCenterOfMass();
            rb.isKinematic = NetRemote;
            if (h != null) Couple(h, false);
            busy = false;
        }

        // ------------------------------------------------------------------ cargo: machines parked on the deck ride along

        [Tooltip("Trailer brakes (electric, from the truck's brake controller): how hard they match the truck's slowing (1 = the trailer stops itself).")]
        public float brakeGain = 1.1f;
        float towLastV, towDecel;
        bool towHasV;
        /// <summary>How hard the trailer brakes are on right now (0..1, for the test and the HUD).</summary>
        public float BrakeLevel { get; private set; }

        /// <summary>
        /// A trailer this heavy has its own brakes: the truck's brake controller feels the rig slowing and brakes the
        /// trailer wheels to match, so the trailer stops itself instead of shoving the truck (and jackknifing it) every
        /// time you slow down. Stopped, they hold it with the truck.
        /// </summary>
        void BrakeStep()
        {
            var hb = Hitched != null ? Hitched.Body : null;
            if (hb == null || wheels == null || wheels.Length == 0 || rb.isKinematic) { towHasV = false; BrakeLevel = 0f; return; }
            float dt = Time.fixedDeltaTime;
            Vector3 tf = hb.transform.forward;
            float vT = Vector3.Dot(hb.linearVelocity, tf);
            float a = towHasV ? (vT - towLastV) / dt : 0f;
            towLastV = vT; towHasV = true;
            towDecel = Mathf.Lerp(towDecel, a, 1f - Mathf.Exp(-dt / 0.08f));
            float s = Vector3.Dot(rb.linearVelocity, transform.forward);
            float want = 0f;
            float slowing = -towDecel * Mathf.Sign(Mathf.Abs(vT) > 0.05f ? vT : s);
            var tractor = Hitched.Vehicle;
            bool pulling = tractor != null && tractor.Throttling;
            // the controller is wired to the truck's brake pedal: off the pedal the trailer rolls free (coasting,
            // bumps and corners slow the truck too, but mustn't drag the trailer brakes on)
            int pedal = tractor != null ? tractor.BrakePedal : -1;
            if (Mathf.Abs(vT) < 0.25f && !pulling && Mathf.Abs(towDecel) < 0.6f) want = 1f; // truck stopped: hold with it
            else if (pedal == 0) want = 0f;
            else if (pedal == 1) want = Mathf.Clamp01(Mathf.Max(0f, slowing - 0.4f) / 6f + 0.3f);
            else if (slowing > 0.4f) want = Mathf.Clamp01((slowing - 0.4f) / 6f + 0.1f);
            BrakeLevel = pulling && want < 0.5f ? Mathf.MoveTowards(BrakeLevel, want, dt * 12f) : Mathf.MoveTowards(BrakeLevel, want, dt * 6f);
            float r = wheels[0] != null ? wheels[0].radius : 0.4f;
            // torque for the trailer's own mass to slow like the truck (a touch more, so the hitch pulls rather than pushes)
            float perWheel = BrakeLevel >= 0.999f && Mathf.Abs(vT) < 0.25f ? parkBrake
                : want <= 0f ? 0f : rb.mass * Mathf.Max(0f, slowing) * brakeGain * r / wheels.Length;
            perWheel = Mathf.Min(perWheel, parkBrake * 2f) * (BrakeLevel > 0.01f ? 1f : 0f);
            foreach (var w in wheels)
                if (w != null) { w.brakeTorque = perWheel; w.motorTorque = perWheel > 0f ? 0f : 1e-4f; }
        }

        bool tippedWheelsOff;
        /// <summary>Lying on its side or roof: the wheels are in the air, so they mustn't act on anything.</summary>
        public bool Tipped => transform.up.y < 0.5f;
        void TipStep()
        {
            bool off = Tipped && Hitched == null;
            if (off == tippedWheelsOff || wheels == null) return;
            tippedWheelsOff = off;
            foreach (var w in wheels) if (w != null) w.enabled = !off;
        }

        /// <summary>Set a tipped trailer back on its wheels where it lies.</summary>
        public void SetUpright()
        {
            Vector3 p = transform.position;
            float y = FPSBase.Mining.VehicleDealer.GroundAt(p).y;
            // (flat on its side transform.forward still points along it: keep that heading)
            Vector3 fw = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            float yaw = fw.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fw).eulerAngles.y : transform.eulerAngles.y;
            if (Hitched != null && couplerPoint != null)
            {
                // on the hitch: upright with its ring on the hook, pointing the way it lies
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                transform.position += Hitched.transform.position - couplerPoint.position;
            }
            else transform.SetPositionAndRotation(new Vector3(p.x, Mathf.Max(p.y, y) + 0.6f, p.z), Quaternion.Euler(0f, yaw, 0f));
            rb.position = transform.position; rb.rotation = transform.rotation;
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            Physics.SyncTransforms();
            TipStep();
        }

        void FixedUpdate()
        {
            UpdateMass();
            RejoinStep();
            TipStep();
            if (Hitched != null) BrakeStep();
            DeckStep(Time.fixedDeltaTime);
            if (deckArea == null || busy) return;
            if ((scanTimer -= Time.fixedDeltaTime) > 0f) return;
            scanTimer = 0.5f;
            for (int i = cargo.Count - 1; i >= 0; i--)
                if (cargo[i].v == null || cargo[i].v.Occupied || cargo[i].v.transform.parent != CargoParent) Release(cargo[i]);
            ScanDeck();
        }

        static readonly Collider[] hits = new Collider[64];
        void ScanDeck()
        {
            // every game chains the machines it runs itself, whoever runs the trailer (a friend sitting in the truck)
            if (tilt > 0.5f) return; // machines get chained once the deck is level
            Vector3 half = new Vector3(deckSize.x * 0.5f, 1.4f, deckSize.y * 0.5f);
            int n = Physics.OverlapBoxNonAlloc(deckArea.position + deckArea.up * 1.45f, half, hits, deckArea.rotation, ~0, QueryTriggerInteraction.Ignore);
            var tractor = Tractor;
            for (int i = 0; i < n; i++)
            {
                var v = hits[i] != null ? hits[i].GetComponentInParent<VehicleBase>() : null;
                if (v == null || v == tractor || v.Occupied || v.NetRemote || Carries(v) || v.transform.parent != null) continue;
                var vrb = v.GetComponent<Rigidbody>();
                if (vrb == null || vrb.isKinematic) continue;
                // sitting on the deck (its middle over it) and stopped on it
                Vector3 l = deckArea.InverseTransformPoint(v.transform.position);
                if (Mathf.Abs(l.x) > half.x || Mathf.Abs(l.z) > half.z || l.y < -0.6f) continue;
                Vector3 rel = vrb.linearVelocity - rb.GetPointVelocity(v.transform.position);
                if (rel.sqrMagnitude > 0.16f) continue;
                if (!CanCarry(v))
                {
                    if (warnedTooBig.Add(v)) GameUI.Toast($"The {displayName} isn't built for the {v.displayName}: it's rated for the skid steers and the 5 t excavator");
                    continue;
                }
                Chain(v, vrb);
            }
        }

        void Chain(VehicleBase v, Rigidbody vrb)
        {
            var c = new Cargo { v = v, rb = vrb, kin = vrb.isKinematic, interp = vrb.interpolation, parent = v.transform.parent, mass = vrb.mass };
            cargo.Add(c);
            v.RidingTrailer = true;
            vrb.linearVelocity = Vector3.zero; vrb.angularVelocity = Vector3.zero;
            vrb.isKinematic = true;
            vrb.interpolation = RigidbodyInterpolation.None;
            // its wheels off while it rides: a chained machine's (braked) wheels would feel the deck moving under
            // them and drag the whole rig to a stop
            c.wheelsOff = new List<WheelCollider>();
            foreach (var w in v.GetComponentsInChildren<WheelCollider>()) if (w.enabled) { w.enabled = false; c.wheelsOff.Add(w); }
            v.transform.SetParent(CargoParent, true);
            var mine = GetComponentsInChildren<Collider>(true);
            var theirs = v.GetComponentsInChildren<Collider>(true);
            IgnorePair(theirs, mine, true);
            var tr = Tractor; if (tr != null) IgnorePair(theirs, tr.GetComponentsInChildren<Collider>(true), true);
            UpdateMass();
            rb.WakeUp();
            GameUI.Toast($"{v.displayName} chained down on the {displayName.ToLower()}");
        }

        void Release(Cargo c)
        {
            cargo.Remove(c);
            UpdateMass();
            if (c.v == null) return;
            c.v.RidingTrailer = false;
            if (c.v.transform.parent == transform || c.v.transform.parent == CargoParent) c.v.transform.SetParent(c.parent, true);
            if (c.wheelsOff != null) foreach (var w in c.wheelsOff) if (w != null) w.enabled = true;
            if (c.rb != null)
            {
                c.rb.position = c.v.transform.position; c.rb.rotation = c.v.transform.rotation;
                c.rb.interpolation = c.interp;
                c.rb.isKinematic = c.kin;
                if (!c.rb.isKinematic) c.rb.linearVelocity = rb.GetPointVelocity(c.v.transform.position);
            }
            var theirs = c.v.GetComponentsInChildren<Collider>(true);
            IgnorePair(theirs, GetComponentsInChildren<Collider>(true), false);
            var tr = Tractor; if (tr != null) IgnorePair(theirs, tr.GetComponentsInChildren<Collider>(true), false);
        }

        /// <summary>Unchain `v` wherever it rides (someone gets in it, it's towed or repositioned).</summary>
        public static void ReleaseCargo(VehicleBase v)
        {
            foreach (var t in all)
                for (int i = t.cargo.Count - 1; i >= 0; i--)
                    if (t.cargo[i].v == v) t.Release(t.cargo[i]);
        }

        /// <summary>The truck is teleported (tow, reposition): its coupled trailers come along.</summary>
        public static List<(Trailer t, Vector3 p, Quaternion r)> Hooked(VehicleBase v)
        {
            var list = new List<(Trailer, Vector3, Quaternion)>();
            foreach (var t in all)
                if (t.Hitched != null && t.Hitched.Vehicle == v)
                    list.Add((t, v.transform.InverseTransformPoint(t.transform.position), Quaternion.Inverse(v.transform.rotation) * t.transform.rotation));
            return list;
        }

        public static void Follow(VehicleBase v, List<(Trailer t, Vector3 p, Quaternion r)> hooked)
        {
            foreach (var (t, p, r) in hooked)
            {
                t.transform.SetPositionAndRotation(v.transform.TransformPoint(p), v.transform.rotation * r);
                if (t.Hitched != null && t.couplerPoint != null)
                {
                    // upright and straight behind the truck, its coupler on the hitch (whatever state it was in)
                    t.transform.rotation = Quaternion.Euler(0f, v.transform.eulerAngles.y, 0f);
                    t.transform.position += t.Hitched.transform.position - t.couplerPoint.position;
                }
                t.rb.position = t.transform.position; t.rb.rotation = t.transform.rotation;
                if (!t.rb.isKinematic) { t.rb.linearVelocity = Vector3.zero; t.rb.angularVelocity = Vector3.zero; }
            }
        }

        // ------------------------------------------------------------------ wheels

        float[] spin;
        void Update()
        {
            if (NetRemote && netHas && !busy)
            {
                float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
                Vector3 aim = NetAim;
                if ((transform.position - aim).sqrMagnitude > 100f) transform.SetPositionAndRotation(aim, netRot);
                else transform.SetPositionAndRotation(Vector3.Lerp(transform.position, aim, k), Quaternion.Slerp(transform.rotation, netRot, k));
                // towed: keep the coupler on the hitch of our copy of their truck (both copies are smoothed on their own)
                if (Hitched != null && NeckOn && couplerPoint != null && (Hitched.transform.position - couplerPoint.position).sqrMagnitude < 9f)
                    transform.position += Hitched.transform.position - couplerPoint.position;
            }
            if (wheels == null || wheelVisuals == null) return;
            if (NetRemote)
            {
                // the wheel physics is off on a copy: roll them by how far it moves
                if (netSpin == null || netSpin.Length != wheels.Length) netSpin = new float[wheels.Length];
                Vector3 pos = transform.position;
                float moved = netLastHas ? Vector3.Dot(pos - netLast, transform.forward) : 0f;
                if (Mathf.Abs(moved) > Mathf.Max(3f, Time.deltaTime * 40f)) moved = 0f;
                netLast = pos; netLastHas = true;
                for (int i = 0; i < wheels.Length && i < wheelVisuals.Length; i++)
                {
                    if (wheels[i] == null || wheelVisuals[i] == null) continue;
                    netSpin[i] = Mathf.Repeat(netSpin[i] + moved / Mathf.Max(0.1f, wheels[i].radius) * Mathf.Rad2Deg, 360f);
                    wheels[i].GetWorldPose(out Vector3 p, out _);
                    wheelVisuals[i].SetPositionAndRotation(p, transform.rotation * Quaternion.Euler(netSpin[i], 0f, 0f));
                }
                return;
            }
            for (int i = 0; i < wheels.Length && i < wheelVisuals.Length; i++)
            {
                if (wheels[i] == null || wheelVisuals[i] == null) continue;
                wheels[i].GetWorldPose(out Vector3 p, out Quaternion q);
                wheelVisuals[i].SetPositionAndRotation(p, q);
            }
        }
        /// <summary>Tests: how far wheel `i` has rolled on a copy (deg).</summary>
        public float NetWheelSpin(int i) => netSpin != null && i < netSpin.Length ? netSpin[i] : 0f;

        // ------------------------------------------------------------------ look-at menu

        public override string Prompt => displayName;
        public override void Interact(PlayerInteractor player) { }

        public override void GetOptions(List<InteractOption> list)
        {
            string load = cargo.Count > 0 ? $"   carrying {CargoTons:0.#} t" + (payloadTons > 0f ? $" of {payloadTons:0} t" : "") : payloadTons > 0f ? $"   rated {payloadTons:0} t" : "";
            list.Add(new InteractOption { label = displayName + load, enabled = false, note = Hitched != null ? $"hitched to {Tractor?.displayName}" : "standing on its own" });
            AddOptions(list);
        }

        /// <summary>The trailer's own choices (shown on the trailer, and on the truck pulling it).</summary>
        public void AddOptions(List<InteractOption> list)
        {
            int first = list.Count;
            AddOptionsInner(list);
            for (int i = first; i < list.Count; i++)
            {
                var run = list[i].run;
                if (run == null) continue;
                list[i].run = p => { var tr = Tractor; if (tr == null || !tr.NetRemote) NetTake(); run(p); };
            }
        }

        void AddOptionsInner(List<InteractOption> list)
        {
            bool moving = Tractor != null && Tractor.GetComponent<Rigidbody>() is Rigidbody trb && !trb.isKinematic && trb.linearVelocity.magnitude > 0.6f;
            if (transform.up.y < 0.85f) // leaning hard or on its side, hitched or not
                list.Add(new InteractOption { label = "Set it back upright", enabled = true, run = p => { SetUpright(); GameUI.Toast($"{displayName} set back on its wheels"); } });
            if (tiltDeck != null)
            {
                bool down = tiltTarget > 0.5f;
                list.Add(down
                    ? new InteractOption { label = "Level the deck   (hydraulic tilt up: ready to haul)", enabled = !moving, note = "stop first",
                        run = p => { SetDeckTilt(false); GameUI.Toast("Deck tilting up level: park a machine on it and it gets chained down"); } }
                    : new InteractOption { label = "Tilt the deck down   (rear on the ground: drive on over the back)", enabled = !moving,
                        note = "stop first",
                        run = p => { SetDeckTilt(true); GameUI.Toast("Deck tilting down: drive on over the back, then level it"); } });
            }
            if (ramps != null && ramps.Length > 0)
            {
                bool down = rampTarget > 0.5f;
                list.Add(down
                    ? new InteractOption { label = "Fold the ramps up", enabled = !moving, note = "stop first", run = p => { SetRamps(false); GameUI.Toast("Ramps folding up"); } }
                    : new InteractOption { label = "Lower the loading ramps   (drive on over the back)", enabled = !moving, note = "stop first",
                        run = p => { SetRamps(true); GameUI.Toast("Ramps down: line the machine up with them and drive on"); } });
            }
            if (gooseneck != null)
            {
                if (NeckOn && Hitched != null)
                    list.Add(new InteractOption
                    {
                        label = $"Detach the gooseneck   (deck drops to the ground: drive machines on over the front)",
                        enabled = !moving && !busy, note = "stop the truck first",
                        run = p => { if (DetachNeck()) GameUI.Toast("Gooseneck unpinned: the deck is down, drive on over the front"); },
                    });
                else if (!NeckOn)
                {
                    bool ok = NeckLinedUp(out float d, out float yaw);
                    list.Add(new InteractOption
                    {
                        label = ok ? "Attach the gooseneck   (lifts the deck and locks it on)" : $"Attach the gooseneck   (back the truck up to the deck: {d:0.0} m away, {yaw:0}° off)",
                        enabled = ok && !busy && !moving, note = moving ? "stop the truck first" : "back the tractor's gooseneck up to the front of the deck, lined up with it",
                        run = p => { if (AttachNeck()) GameUI.Toast("Gooseneck pinned: the deck is up, ready to haul"); },
                    });
                }
            }
            else if (Hitched != null)
                list.Add(new InteractOption
                {
                    label = $"Unhitch from the {Tractor?.displayName}",
                    enabled = !moving, note = "stop the truck first",
                    run = p => { Uncouple(); GameUI.Toast($"{displayName} unhitched (parking brake on)"); },
                });
            else
            {
                var h = FindHitch();
                list.Add(new InteractOption
                {
                    label = h != null ? $"Hitch to the {h.Vehicle?.displayName}" : coupler == HitchType.Gooseneck
                        ? "Hitch up   (fit a gooseneck ball in a pickup's bed, then back it in under the coupler)"
                        : $"Hitch up   (back a truck's {(coupler == HitchType.Pintle ? "pintle hook" : "fifth wheel")} up to the {(coupler == HitchType.Pintle ? "ring" : "kingpin")})",
                    enabled = h != null, note = "back the truck up so its hitch is right at the trailer's ring",
                    run = p => { Couple(h, true); GameUI.Toast($"{displayName} hitched to the {h.Vehicle?.displayName}"); },
                });
            }
            var tk = Tank;
            if (tk != null) list.Add(FuelPump.FillOption(tk, tk.transform.position));
        }

        /// <summary>Trailers hitched to `v` (or riding on its fifth wheel) add their choices to its menu.</summary>
        public static void AddTruckOptions(VehicleBase v, List<InteractOption> list)
        {
            foreach (var t in all)
            {
                if (t.Tractor != v) continue;
                int before = list.Count;
                t.AddOptions(list);
                for (int i = before; i < list.Count; i++) list[i].label = $"{t.displayName}: " + list[i].label;
            }
        }
    }
}
