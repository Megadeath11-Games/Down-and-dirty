using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// Wheel loader. Drop the bucket flat on the ground and drive into dirt or a pile to fill it, curl it back,
    /// raise the arms, and tip it forward to dump into a truck, the wash plant hopper, or onto a pile.
    /// </summary>
    public class FrontLoader : EarthmoverBase
    {
        [Header("Loader arms & bucket")]
        public Transform armPivot;
        public Transform bucketPivot;
        public Transform bucketTip;          // cutting edge centre
        public Transform bucketLoadVisual;
        [Tooltip("Which way the bucket opens, in the bucket load's parent frame (zero = work it out from the mesh).")]
        public Vector3 bucketMouth;
        public Transform[] wheelVisuals;
        public float wheelRadius = 0.55f;
        [Tooltip("Skid steer: each side's wheels turn at that side's own speed, so a spin turn runs them against each other.")]
        public bool skidSteer;
        public Vector2 armRange = new Vector2(-38f, 48f);   // deg, + = raise
        public Vector2 tiltRange = new Vector2(-45f, 60f);  // deg (self-levelling), - = curl back, + = dump
        public float armSpeed = 22f, tiltSpeed = 55f;
        public float bucketCapacity = 0.8f;  // m^3
        public float fillRate = 0.7f;        // m^3 per metre driven into dirt
        public float curlFillRate = 0.35f;   // m^3/s while curling in dirt
        public float dumpAngle = 22f;
        public float dumpRate = 0.9f;        // m^3/s

        public MaterialLoad bucketLoad;
        float arm = -31f, tilt = -4f;
        Vector3 loadBaseScale, loadBasePos;
        string underBucket = "ground";
        float fxTimer;
        float[] spins;
        Quaternion[] wheelRest;
        float lastYaw;

        protected override void Awake()
        {
            base.Awake();
            if (bucketLoadVisual != null)
            {
                loadBaseScale = bucketLoadVisual.localScale; loadBasePos = bucketLoadVisual.localPosition;
                var cc = ClumpContainer.Attach(gameObject, bucketLoadVisual.parent, loadBasePos, loadBaseScale, bucketCapacity,
                    () => bucketLoad, () => bucketTip != null ? bucketTip.position : (Vector3?)null, () => bucketTip != null ? bucketTip.position : transform.position,
                    bucketLoadVisual.GetComponentsInChildren<Renderer>(true));
                cc.takeOut = v => bucketLoad.Take(v);
                // the builder knows which way the bucket opens; otherwise find it from the bucket's shape
                if (bucketMouth.sqrMagnitude > 0.01f) cc.SetMouth(bucketMouth.normalized);
                else cc.AutoMouth(bucketLoadVisual.GetComponentsInChildren<Renderer>(true));
                // while the bucket is cutting into the ground the load is packed in: nothing falls out
                cc.packed = () =>
                {
                    var s = DigSite.At(transform.position);
                    if (bucketTip == null) return false;
                    Vector3 t = bucketTip.position;
                    return s != null && s.Contains(t) && s.SurfaceY(t) > t.y - 0.08f;
                };
            }
            BuildShell();
            SetPose(arm, tilt); // clamps the start pose into this machine's range (bucket on the ground)
            UpdateLoadVisual();
            if (wheelVisuals != null)
            {
                spins = new float[wheelVisuals.Length];
                wheelRest = new Quaternion[wheelVisuals.Length];
                for (int i = 0; i < wheelVisuals.Length; i++)
                    wheelRest[i] = wheelVisuals[i] != null ? wheelVisuals[i].localRotation : Quaternion.identity;
            }
            lastYaw = transform.eulerAngles.y;
        }

        public float Arm => arm;
        public float Tilt => tilt;

        float netArm = float.NaN, netTilt;
        public override void NetWrite(System.IO.BinaryWriter w) { base.NetWrite(w); w.Write(arm); w.Write(tilt); NetIO.Write(w, bucketLoad); }
        public override void NetRead(System.IO.BinaryReader r)
        {
            base.NetRead(r); netArm = r.ReadSingle(); netTilt = r.ReadSingle();
            var was = bucketLoad; bucketLoad = NetIO.ReadLoad(r);
            // their bucket is tipped and emptying: draw it pouring here too
            if (was.volume - bucketLoad.volume > 1e-4f && netTilt > dumpAngle) { netSpill += was.volume - bucketLoad.volume; netSpillMix = was; }
        }
        float netSpill; MaterialLoad netSpillMix;
        protected override void NetShow(float dt)
        {
            if (float.IsNaN(netArm)) return;
            arm = NetIO.Ease(arm, netArm, dt); tilt = NetIO.Ease(tilt, netTilt, dt);
            ApplyPose(); UpdateLoadVisual();
            if (netSpill > 1e-5f && bucketTip != null)
            {
                float give = Mathf.Min(netSpill, Mathf.Max(0.004f, netSpill * Mathf.Clamp01(dt * 6f)));
                netSpill -= give;
                FPSBase.Mining.DirtGrains.PourVisual(bucketTip.position + transform.forward * 0.15f, netSpillMix.Scaled(give / Mathf.Max(1e-4f, netSpillMix.volume)), transform);
            }
        }

        public void SetPose(float armDeg, float tiltDeg)
        {
            arm = Mathf.Clamp(armDeg, armRange.x, armRange.y);
            tilt = Mathf.Clamp(tiltDeg, tiltRange.x, tiltRange.y);
            ApplyPose();
        }

        void ApplyPose()
        {
            if (armPivot != null) armPivot.localRotation = Quaternion.Euler(-arm, 0f, 0f);
            // parallel linkage: the bucket keeps its angle to the ground as the arms lift
            if (bucketPivot != null) bucketPivot.localRotation = Quaternion.Euler(arm + tilt, 0f, 0f);
        }

        protected override void DriveUpdate(float dt)
        {
            ReadDriveKeys();
            float armIn = Controls.Axis(GameAction.ArmsUp, GameAction.ArmsDown);
            float tiltIn = Controls.Axis(GameAction.BucketDump, GameAction.BucketCurl);
            if (!HasFuel) { armIn = Mathf.Min(armIn, 0f); tiltIn = 0f; }
            SetPose(arm + armIn * armSpeed * dt, tilt + tiltIn * tiltSpeed * dt);
            cab.right = new Vector2(tiltIn, -armIn); // loader joystick: forward = lower, right = dump
            // shoving into a pile counts as driving into it even when the pile has stopped the machine
            Work(Mathf.Max(ForwardSpeed, throttle > 0.1f ? throttle * 0.6f : 0f), tiltIn, dt);
            workAmount = Mathf.Clamp01(Mathf.Abs(throttle) + Mathf.Abs(armIn) * 0.5f + Mathf.Abs(tiltIn) * 0.3f);
            hydraulicAmount = Mathf.Clamp01(Mathf.Abs(armIn) + Mathf.Abs(tiltIn) * 0.8f);
        }

        // nobody in the seat: a bucket left tipped keeps emptying
        protected override void IdleUpdate(float dt) { if (bucketTip != null) DumpStep(dt); UpdateLoadVisual(); }

        float lastCutAt = -10f;

        bool TipInGround()
        {
            if (bucketTip == null) return false;
            var site = DigSite.At(transform.position);
            return site != null && site.Contains(bucketTip.position) && site.IsUnderground(bucketTip.position + Vector3.up * 0.05f);
        }

        /// <summary>Dump: bucket tipped forward past the dump angle pours its load out. Returns where it pours.</summary>
        Vector3 DumpStep(float dt)
        {
            Vector3 pourAt = bucketTip.position + transform.forward * 0.15f;
            // (not while the edge is cutting into the dirt: tipped down and driven in, the dirt is packed in as it's cut)
            if (tilt > dumpAngle && !bucketLoad.IsEmpty && Time.time - lastCutAt > 0.35f && !TipInGround())
            {
                float open = Mathf.InverseLerp(dumpAngle, tiltRange.y, tilt);
                var pour = bucketLoad.Take(dumpRate * (0.3f + open) * dt);
                var lcc = GetComponent<ClumpContainer>();
                // out along the whole cutting edge (a random spot each frame: spread evenly over time), not just its middle
                Vector3 side = bucketLoadVisual != null ? bucketLoadVisual.parent.right : transform.right;
                float halfW = lcc != null ? lcc.Half.x : 0.4f;
                Vector3 at = pourAt + side * (Random.Range(-1f, 1f) * halfW * 0.85f);
                // (over a truck or hopper, only the bit of the edge that's over it: a bucket wider than the bed doesn't miss it)
                var rec = MaterialTransfer.FindReceiverBelow(pourAt, transform, out _);
                // (and a quarter metre in from its edge: poured right on the rim, dirt bounced off the side)
                Vector3 inset = at + side * (Mathf.Sign(Vector3.Dot(at - pourAt, side)) * 0.25f);
                if (rec != null && (MaterialTransfer.FindReceiverBelow(at, transform, out _) != rec || MaterialTransfer.FindReceiverBelow(inset, transform, out _) != rec)) at = pourAt;
                MaterialTransfer.Pour(at, pour, transform, lcc != null ? lcc.ClumpRadius : 0f);
            }
            return pourAt;
        }

        protected override void Update()
        {
            base.Update();
            float yaw = transform.eulerAngles.y;
            float yawRate = Time.deltaTime > 1e-5f ? Mathf.DeltaAngle(lastYaw, yaw) * Mathf.Deg2Rad / Time.deltaTime : 0f;
            lastYaw = yaw;
            if (wheelVisuals == null || spins == null || RidingTrailer) return; // chained on a trailer: wheels hold still
            for (int i = 0; i < wheelVisuals.Length; i++)
            {
                var w = wheelVisuals[i];
                if (w == null) continue;
                // a skid steer's left and right wheels each roll at their own side's ground speed
                float side = skidSteer ? transform.InverseTransformPoint(w.position).x : 0f;
                float v = ForwardSpeed - yawRate * side; // turning right (yaw up) runs the left side faster
                spins[i] += v * Time.deltaTime / Mathf.Max(0.1f, wheelRadius) * Mathf.Rad2Deg;
                w.localRotation = Quaternion.Euler(spins[i], 0f, 0f) * wheelRest[i];
            }
        }

        // ---- the bucket's steel: points all over it, so it cuts the dirt in its own shape (its whole width, not a
        // little circle at the middle of the edge that let the rest of the bucket slide through the heap)
        Vector3[] shellLocal, shellW, shellPrev, shellVel;
        float[] shellLim; bool[] shellScoop;
        bool shellHasPrev;
        Vector3? scoopedAt;
        float shellHalfW = 0.5f;
        /// <summary>Dirt the bucket took in last frame (m³, for the test).</summary>
        public float LastScooped { get; private set; }

        void BuildShell()
        {
            if (bucketPivot == null || bucketTip == null) return;
            var pts = new System.Collections.Generic.List<Vector3>();
            var toLocal = bucketPivot.worldToLocalMatrix;
            bool NotSteel(Transform t) =>
                (bucketLoadVisual != null && t.IsChildOf(bucketLoadVisual)) || t.name.Contains("Load") || t.name.Contains("dirt");
            MiniExcavator.SampleMeshes(bucketPivot, NotSteel, 0.06f, 12000, (w, _) => pts.Add(toLocal.MultiplyPoint3x4(w)));
            if (pts.Count == 0) return;
            shellLocal = pts.ToArray();
            int n = shellLocal.Length;
            shellW = new Vector3[n]; shellPrev = new Vector3[n]; shellVel = new Vector3[n];
            shellLim = new float[n]; shellScoop = new bool[n];
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in shellLocal) { lo = Mathf.Min(lo, p.x); hi = Mathf.Max(hi, p.x); }
            shellHalfW = Mathf.Max(0.2f, (hi - lo) * 0.5f * Mathf.Abs(bucketPivot.lossyScale.x));
        }

        /// <summary>One step of bucket work. `v` = forward speed, `tiltIn` = -1 curl … +1 dump. Public for the automated test.</summary>
        public void Work(float v, float tiltIn, float dt)
        {
            Vector3 tip = bucketTip.position;
            var site = DigSite.At(transform.position);
            LastScooped = 0f; scoopedAt = null;
            bool tippedDown = tilt >= dumpAngle;
            bool full = bucketLoad.volume >= bucketCapacity * 0.98f;

            // a full bucket held low acts like a small dozer blade and shoves dirt along
            if (site != null && full && v > 0.05f && tilt < 15f && site.Contains(tip))
                site.BladePush(tip, transform.forward, shellHalfW * 2f, tip.y + 0.05f);

            if (site != null && site.Contains(tip) && shellLocal != null) Carve(site, v, tiltIn, full, tippedDown, dt);
            else if (DirtPile.FindContaining(tip) is DirtPile pile && !full && (!tippedDown || v > 0.05f))
            {
                // a dirt pile off the dig field (no ground to cut): driven or curled into, it fills as before
                float want = Mathf.Max(v, 0f) * fillRate * dt + Mathf.Max(-tiltIn, 0f) * curlFillRate * dt;
                want = Mathf.Min(want, bucketCapacity - bucketLoad.volume);
                var got = want > 0f ? pile.Dig(want) : default;
                if (!got.IsEmpty) { bucketLoad.Add(got); lastCutAt = Time.time; LastScooped = got.volume; scrapeAmount = 0.6f; }
            }
            if (LastScooped > 0f)
            {
                var cc = GetComponent<ClumpContainer>();
                if (cc != null && cc.Scoop(tip, 0.9f, bucketCapacity) is Boulder b) GameUI.Toast($"Scooped up a {b.volume:0.0#} m³ boulder");
                strainAmount = Mathf.Clamp01(LastScooped / Mathf.Max(1e-4f, fillRate * dt) + 0.3f);
                scrapeAmount = 0.6f;
                if ((fxTimer -= dt) <= 0f) { PourFx.Dig(tip); fxTimer = 0.25f; }
            }

            Vector3 pourAt = DumpStep(dt);
            underBucket = Describe(MaterialTransfer.FindReceiverBelow(pourAt, transform, out _));
            UpdateLoadVisual();
        }

        /// <summary>
        /// The bucket in the dig field: every bit of its steel cuts the dirt it moves through. Driven in flat, or
        /// curled while it's in the dirt, what the cutting edge slices off goes in through the mouth: it fills
        /// across its whole width. Lowered straight down it only presses; the back or the sides shove loose dirt
        /// along. Unbroken ground is cut no faster than the machine has the push for; loose dirt (a heap) always goes.
        /// </summary>
        void Carve(DigSite site, float v, float tiltIn, bool full, bool tippedDown, float dt)
        {
            int n = shellLocal.Length;
            var m = bucketPivot.localToWorldMatrix;
            bool fresh = !shellHasPrev || dt <= 1e-4f;
            for (int i = 0; i < n; i++)
            {
                var w = m.MultiplyPoint3x4(shellLocal[i]);
                shellVel[i] = fresh ? Vector3.zero : (w - shellPrev[i]) / dt;
                shellPrev[i] = shellW[i] = w;
            }
            shellHasPrev = true;
            if (fresh) return;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            // (a stalled machine shoving at a heap still counts as driving into it: its bucket is pushing)
            bool driving = v > 0.05f;
            bool scooping = !full && (driving || (!tippedDown && tiltIn < -0.05f));
            float scrape = 0.03f * dt;
            for (int i = 0; i < n; i++)
            {
                Vector3 vel = shellVel[i];
                if (driving) vel += fwd * Mathf.Max(0f, v - Vector3.Dot(vel, fwd)); // (shoving at a heap that holds the machine)
                shellVel[i] = vel;
                float sp = vel.magnitude;
                if (sp < 0.01f || sp > 30f) { shellLim[i] = -1f; shellScoop[i] = false; continue; }
                if (!scooping && vel.y < -0.5f * sp) { shellLim[i] = -1f; shellScoop[i] = false; continue; } // pressed down: packs
                shellScoop[i] = scooping;
                shellLim[i] = scooping ? 10f : scrape;
            }
            site.GroundNameAt(bucketTip.position, out float ease);
            if (ease <= 0f) ease = 1f;
            // how much unbroken ground the machine can cut this frame (driven in hard, or curled in)
            float budget = (Mathf.Max(v, 0f) * fillRate * 1.6f + Mathf.Max(-tiltIn, 0f) * curlFillRate * 2f + 0.05f) * ease * dt;
            float cut = site.CarveBelow(shellW, shellLim, shellScoop, shellVel, n, budget, out var sc, out var pu, out var sAt, out var pAt);
            if (cut <= 0f) return;
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            if (!sc.IsEmpty)
            {
                var cc = GetComponent<ClumpContainer>();
                float space = Mathf.Max(0f, bucketCapacity - bucketLoad.volume - (cc != null ? cc.HeldRockVolume : 0f));
                var into = sc.Take(space);
                bucketLoad.Add(into);
                LastScooped = into.volume;
                if (into.volume > 0f) { lastCutAt = Time.time; scoopedAt = sAt; }
                // brim full: the rest heaps up in front of the edge, spread across its width (pushed along from there)
                if (!sc.IsEmpty)
                {
                    float spread = Mathf.Max(site.Cell * 1.5f, shellHalfW * 0.4f);
                    Vector3 at = bucketTip.position + fwd * spread;
                    var third = sc.Scaled(1f / 3f);
                    for (int k = -1; k <= 1; k++) site.Deposit(at + side * (k * shellHalfW * 0.6f), third, spread);
                }
            }
            if (!pu.IsEmpty)
            {
                float spread = Mathf.Max(site.Cell * 1.5f, shellHalfW * 0.4f);
                var third = pu.Scaled(1f / 3f);
                for (int k = -1; k <= 1; k++) site.Deposit(pAt + side * (k * shellHalfW * 0.6f), third, spread);
            }
        }

        void UpdateLoadVisual()
        {
            if (bucketLoadVisual == null) return;
            float f = Mathf.Clamp01(bucketLoad.volume / bucketCapacity);
            bucketLoadVisual.gameObject.SetActive(f > 0.01f);
            float h = Mathf.Lerp(0.2f, 1f, Mathf.Sqrt(f));
            float w = Mathf.Lerp(0.6f, 1f, Mathf.Sqrt(f));
            bucketLoadVisual.localScale = new Vector3(loadBaseScale.x * w, loadBaseScale.y * h, loadBaseScale.z * w);
            bucketLoadVisual.localPosition = loadBasePos + Vector3.up * loadBaseScale.y * (h - 1f) * 0.5f;
        }

        public override float HudBar => bucketLoad.volume / bucketCapacity;
        public override string HudBarLabel => bucketLoad.volume >= bucketCapacity * 0.98f
            ? $"BUCKET FULL  —  curl back ({N(GameAction.BucketCurl)}), raise ({N(GameAction.ArmsUp)}), drive to the truck, dump ({N(GameAction.BucketDump)})"
            : $"Bucket {bucketLoad.volume:0.00} / {bucketCapacity:0.0} m³";
        public override string HudBody => $"Fuel {fuel:0.0} / {fuelCapacity:0} L    Speed {Mathf.Abs(ForwardSpeed) * 3.6f:0.0} km/h\n" +
                                          $"Arms {arm:0}° · Bucket {(tilt < -2f ? "curled" : tilt > dumpAngle ? "DUMPING" : "flat")} ({tilt:0}°)\n" +
                                          $"Bucket: {bucketLoad.Describe()}\n" +
                                          $"Under bucket: {underBucket}";
        public override string HudControls =>
            $"{N(GameAction.DriveForward)}/{N(GameAction.DriveBack)} drive · {N(GameAction.SteerLeft)}/{N(GameAction.SteerRight)} steer\n" +
            $"{N(GameAction.ArmsUp)} raise arms · {N(GameAction.ArmsDown)} lower arms · {N(GameAction.BucketCurl)} curl back · {N(GameAction.BucketDump)} tip / dump\n" +
            "Fill: arms down, bucket flat, drive into dirt or a pile\n" +
            $"Mouse look · {N(GameAction.CabView)} cab view · {N(GameAction.Interact)} get out";

        static string N(GameAction a) => Controls.N(a);
    }
}
