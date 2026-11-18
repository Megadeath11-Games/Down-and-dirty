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

        [Header("Removable gooseneck (RGN lowboy)")]
        public Transform gooseneck;
        [Tooltip("Front of the deck, where the neck locks on.")]
        public Transform deckSocket;
        [Tooltip("On the gooseneck: mates with the deck socket.")]
        public Transform neckSocket;
        [Tooltip("Centre of the load deck (top surface); size in its local x (width), z (length).")]
        public Transform deckArea;
        public Vector2 deckSize = new Vector2(2.6f, 6.4f);

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

        class Cargo { public VehicleBase v; public Rigidbody rb; public bool kin; public RigidbodyInterpolation interp; public Transform parent; public float mass; }
        readonly List<Cargo> cargo = new();
        float scanTimer;
        public int CargoCount => cargo.Count;
        public bool Carries(VehicleBase v) { foreach (var c in cargo) if (c.v == v) return true; return false; }
        public float CargoTons { get { float m = 0f; foreach (var c in cargo) m += c.mass; return m / 1000f; } }

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            emptyMass = rb.mass;
            if (jack != null) jackRest = jack.localPosition;
            // built riding on its truck: never collide with it (the gooseneck sits right in the tractor's body box)
            var pv = transform.parent != null ? transform.parent.GetComponentInParent<VehicleBase>() : null;
            if (pv != null) IgnoreWith(pv.transform);
            if (gooseneck != null) { neckRestPos = gooseneck.localPosition; neckRestRot = gooseneck.localRotation; }
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
            joint.lowAngularXLimit = new SoftJointLimit { limit = -22f };
            joint.highAngularXLimit = new SoftJointLimit { limit = 22f };
            joint.angularYLimit = new SoftJointLimit { limit = 85f };
            joint.angularZLimit = new SoftJointLimit { limit = h.type == HitchType.FifthWheel ? 8f : 25f };
            joint.enableCollision = false;
            h.Attached = this;
            Hitched = h;
            IgnoreWith(h.Vehicle != null ? h.Vehicle.transform : hb != null ? hb.transform : null);
            SetBrake(false);
            rb.isKinematic = false;
            rb.WakeUp();
        }

        public void Uncouple()
        {
            if (joint != null) Destroy(joint);
            joint = null;
            if (Hitched != null && Hitched.Attached == this) Hitched.Attached = null;
            Hitched = null;
            SetBrake(true);
        }

        /// <summary>The truck pulling it (or carrying its gooseneck).</summary>
        public VehicleBase Tractor => Hitched != null ? Hitched.Vehicle : NeckCarrier != null ? NeckCarrier.Vehicle : null;

        readonly HashSet<Transform> ignored = new();
        void IgnoreWith(Transform other)
        {
            if (other == null) return;
            ignored.Add(other);
            IgnorePair(GetComponentsInChildren<Collider>(true), other.GetComponentsInChildren<Collider>(true), true);
        }

        static void IgnorePair(Collider[] a, Collider[] b, bool ignore)
        {
            foreach (var x in a) foreach (var y in b) if (x != null && y != null && x != y) Physics.IgnoreCollision(x, y, ignore);
        }

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
            rb.isKinematic = false;
            if (h != null) Couple(h, false);
            busy = false;
        }

        // ------------------------------------------------------------------ cargo: machines parked on the deck ride along

        void FixedUpdate()
        {
            UpdateMass();
            if (deckArea == null || busy) return;
            if ((scanTimer -= Time.fixedDeltaTime) > 0f) return;
            scanTimer = 0.5f;
            for (int i = cargo.Count - 1; i >= 0; i--)
                if (cargo[i].v == null || cargo[i].v.Occupied || cargo[i].v.transform.parent != transform) Release(cargo[i]);
            ScanDeck();
        }

        static readonly Collider[] hits = new Collider[64];
        void ScanDeck()
        {
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
                Chain(v, vrb);
            }
        }

        void Chain(VehicleBase v, Rigidbody vrb)
        {
            var c = new Cargo { v = v, rb = vrb, kin = vrb.isKinematic, interp = vrb.interpolation, parent = v.transform.parent, mass = vrb.mass };
            cargo.Add(c);
            vrb.linearVelocity = Vector3.zero; vrb.angularVelocity = Vector3.zero;
            vrb.isKinematic = true;
            vrb.interpolation = RigidbodyInterpolation.None;
            v.transform.SetParent(transform, true);
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
            if (c.v.transform.parent == transform) c.v.transform.SetParent(c.parent, true);
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
                t.rb.position = t.transform.position; t.rb.rotation = t.transform.rotation;
                if (!t.rb.isKinematic) { t.rb.linearVelocity = Vector3.zero; t.rb.angularVelocity = Vector3.zero; }
            }
        }

        // ------------------------------------------------------------------ wheels

        float[] spin;
        void Update()
        {
            if (wheels == null || wheelVisuals == null) return;
            for (int i = 0; i < wheels.Length && i < wheelVisuals.Length; i++)
            {
                if (wheels[i] == null || wheelVisuals[i] == null) continue;
                wheels[i].GetWorldPose(out Vector3 p, out Quaternion q);
                wheelVisuals[i].SetPositionAndRotation(p, q);
            }
        }

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
            bool moving = Tractor != null && Tractor.GetComponent<Rigidbody>() is Rigidbody trb && !trb.isKinematic && trb.linearVelocity.magnitude > 0.6f;
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
                    label = h != null ? $"Hitch to the {h.Vehicle?.displayName}" : $"Hitch up   (back a truck's {(coupler == HitchType.Pintle ? "pintle hook" : "fifth wheel")} up to the {(coupler == HitchType.Pintle ? "ring" : "kingpin")})",
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
