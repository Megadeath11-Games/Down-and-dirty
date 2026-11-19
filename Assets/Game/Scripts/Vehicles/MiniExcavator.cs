using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// Tracked excavator: drive on tracks, swing the house, move boom / stick / bucket.
    /// Two control schemes (switch in Esc menu): Realistic ISO joysticks on keys, or Simplified mouse controls.
    /// Digs where the bucket tip is underground while curling or pulling the stick in; dumps when the bucket opens.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class MiniExcavator : VehicleBase
    {
        [Header("Rig")]
        public Transform upper;        // swings
        public Transform boomPivot;
        public Transform stickPivot;
        public Transform bucketPivot;
        public Transform bucketTip;
        public Transform bucketLoadVisual;
        [Tooltip("Which way the bucket opens, in the bucket load's parent frame (zero = work it out from the mesh).")]
        public Vector3 bucketMouth;

        [Header("Joint limits (deg)")]
        public Vector2 boomRange = new Vector2(-35f, 60f);     // + = raise
        public Vector2 stickRange = new Vector2(-155f, -35f);  // relative to boom, + = out
        public Vector2 bucketRange = new Vector2(-150f, 25f);  // relative to stick, - = curl
        public float boomSpeed = 28f, stickSpeed = 40f, bucketSpeed = 70f, swingSpeed = 40f;

        [Header("Tracks")]
        public float driveSpeed = 1.6f;   // m/s (mini excavators are slow)
        public float turnSpeed = 35f;     // deg/s

        [Header("Bucket")]
        public float bucketCapacity = 0.08f; // m^3
        public float digRate = 0.12f;        // m^3/s at full input
        public float digRadius = 0.35f;
        public float dumpRate = 0.25f;       // m^3/s
        public float dumpAngle = -25f;       // bucket angle above which material pours out

        [Tooltip("Uniform size of this model (1 = 1.5 t mini). Scales ground checks.")]
        public float sizeScale = 1f;
        public float centerOfMassHeight = 0.15f;
        [Tooltip("Digging already-loose dirt (ripped, pushed or dumped) is this much faster.")]
        public float looseDigBonus = 1.6f;

        [Header("Front backfill blade (optional)")]
        public Transform blade;        // pivot on the undercarriage, + = raise
        public Transform bladeEdge;
        public float bladeWidth = 2f;
        public Vector2 bladeRange = new Vector2(-12f, 22f);
        public float bladeSpeed = 25f;
        float bladeAngle = 8f;

        [Header("Ground & dig resistance")]
        public TrackSuspension suspension = new TrackSuspension();
        [Tooltip("How far (m) the bucket tip can be forced below the dirt surface before the ground pushes back.")]
        public float allowedPenetration = -1f; // < 0 = from the bucket size
        [Tooltip("Overload (m past the allowed depth) at which the ground pushes back with the machine's full weight.")]
        public float resistanceTravel = 0.25f;
        /// <summary>0..1: how hard the ground is pushing back on the bucket, as a fraction of the machine's weight.</summary>
        public float Overload { get; private set; }
        public float MaxTilt { get; private set; }
        Collider[] tools;
        readonly System.Collections.Generic.HashSet<int> toolPiles = new();

        public MaterialLoad bucketLoad;
        string underBucket = "ground";
        Vector3 loadBaseScale, loadBasePos;

        float boom = 15f, stick = -95f, bucket = -60f, swing;
        Rigidbody rb;
        float trackInput, turnInput;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.centerOfMass = new Vector3(0f, centerOfMassHeight, 0f);
            rb.angularDamping = Mathf.Max(rb.angularDamping, 3f);
            suspension.Setup(rb, GetComponent<BoxCollider>(), false);
            tools = ToolColliders.Find(transform);
            if (bucketLoadVisual != null)
            {
                loadBaseScale = bucketLoadVisual.localScale; loadBasePos = bucketLoadVisual.localPosition;
                // the dirt in the bucket is live clumps: new ones come in at the teeth, the ones at the teeth leave first
                var cc = ClumpContainer.Attach(gameObject, bucketLoadVisual.parent, loadBasePos, loadBaseScale, bucketCapacity,
                    () => bucketLoad, () => bucketTip != null ? bucketTip.position : (Vector3?)null, () => bucketTip != null ? bucketTip.position : transform.position,
                    bucketLoadVisual.GetComponentsInChildren<Renderer>(true));
                cc.takeOut = v => bucketLoad.Take(v);
                // the marker shows where the load lands once the bucket opens, not where the teeth are now
                DumpMarker.Attach(gameObject, bucketTip, Mathf.Max(0.35f, digRadius * 1.3f)).sourceAt = () => DumpPoint(AimAngle);
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
            ApplyPose();
        }

        void Start() => ToolColliders.IgnoreGround(tools);

        protected override void OnEnterVehicle() => rb.isKinematic = false;

        protected override void DriveUpdate(float dt)
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool simplified = GameSettings.ExcavatorControls == ExcavatorControlMode.Simplified;
            if (vehicleCamera != null) vehicleCamera.AllowMouse = !simplified;

            float boomIn, stickIn, bucketIn, swingIn;
            if (!simplified)
            {
                // ISO pattern. Left stick: W/S = stick out/in, A/D = swing. Right stick: I/K = boom down/up, J/L = bucket curl/dump.
                stickIn = Controls.Axis(GameAction.StickOut, GameAction.StickIn);
                swingIn = Controls.Axis(GameAction.SwingRight, GameAction.SwingLeft);
                boomIn = Controls.Axis(GameAction.BoomUp, GameAction.BoomDown);
                bucketIn = Controls.Axis(GameAction.ExBucketDump, GameAction.ExBucketCurl);
                trackInput = Controls.Axis(GameAction.TracksForward, GameAction.TracksBack);
                turnInput = Controls.Axis(GameAction.TracksRight, GameAction.TracksLeft);
                // controller: while a trigger drives the tracks, the left stick steers them (instead of swinging)
                var gp = Gamepad.current;
                if (gp != null && !(GameUI.Instance != null && GameUI.Instance.BlocksGameplay) && Mathf.Max(gp.leftTrigger.ReadValue(), gp.rightTrigger.ReadValue()) > 0.1f)
                {
                    float sx = gp.leftStick.ReadValue().x;
                    turnInput = Mathf.Clamp(turnInput + sx, -1f, 1f);
                    swingIn = Mathf.Clamp(swingIn - sx, -1f, 1f);
                }
            }
            else
            {
                Vector2 md = mouse.delta.ReadValue() * GameSettings.MouseSensitivity * 0.05f;
                swingIn = Mathf.Clamp(md.x, -1.5f, 1.5f);
                boomIn = Mathf.Clamp(md.y, -1.5f, 1.5f);
                float scroll = mouse.scroll.ReadValue().y;
                stickIn = Mathf.Clamp(scroll * 0.05f, -3f, 3f) * 3f;
                bucketIn = (mouse.rightButton.isPressed ? 1f : 0f) - (mouse.leftButton.isPressed ? 1f : 0f);
                if (mouse.leftButton.isPressed) stickIn -= 0.4f; // digging pulls the stick in a little, like a real dig cycle
                trackInput = Controls.Axis(GameAction.DriveForward, GameAction.DriveBack);
                turnInput = Controls.Axis(GameAction.SteerRight, GameAction.SteerLeft);
            }

            float bladeIn = blade != null ? Controls.Axis(GameAction.ExBladeUp, GameAction.ExBladeDown) : 0f;
            Operate(boomIn, stickIn, bucketIn, swingIn, bladeIn, dt);
        }

        ClumpContainer rockBoxC; bool rockBoxLooked;
        ClumpContainer rockBox { get { if (!rockBoxLooked) { rockBoxC = GetComponent<ClumpContainer>(); rockBoxLooked = true; } return rockBoxC; } }
        Vector3 lastTip; bool hasLastTip;
        /// <summary>Dirt the bucket spread last frame (m^3).</summary>
        public float LastSpread { get; private set; }

        /// <summary>
        /// Spreading: the bucket moving through loose dirt without digging (swung sideways, pushed out, or dragged
        /// back with the bucket opened out) shoves the dirt along like a small blade: level a pile, fill a trench.
        /// </summary>
        void Spread(bool digging, float dt)
        {
            LastSpread = 0f;
            var site = DigSite.At(transform.position);
            if (site == null || bucketTip == null) return;
            Vector3 tip = bucketTip.position;
            Vector3 v = hasLastTip && dt > 1e-4f ? (tip - lastTip) / dt : Vector3.zero;
            lastTip = tip; hasLastTip = true;
            v.y = 0f;
            if (digging || v.magnitude < 0.12f || v.magnitude > 25f || !site.Contains(tip)) return;
            if (site.SurfaceY(tip) < tip.y + 0.02f) return; // not in the dirt
            float width = Mathf.Max(0.4f, digRadius * 2.4f);
            // the bucket shaves unbroken ground too (a little per frame, never below the teeth), so dragging it
            // across undug ground levels and reshapes it
            LastSpread = site.Spread(tip, v.normalized, width, tip.y, Mathf.Clamp(0.25f * sizeScale, 0.15f, 0.8f), 1.2f * dt);
            if (LastSpread > 0.002f && Random.value < 0.15f) PourFx.Dig(tip);
        }

        /// <summary>The operator's inputs this frame (-1..1 each), for the cab controls and the network.</summary>
        public struct Inputs { public float boom, stick, bucket, swing, tracks, turn, blade; }
        public Inputs LastInputs { get; private set; }
        Inputs? testInputs;
        /// <summary>Drive the controls without anyone in the seat (tests, remote players). Null releases them.</summary>
        public void TestControl(Inputs? i) => testInputs = i;

        /// <summary>One frame of work from the controls: moves the arm, digs, pushes the blade, dumps.</summary>
        void Operate(float boomIn, float stickIn, float bucketIn, float swingIn, float bladeIn, float dt)
        {
            bool fuelOk = HasFuel;
            if (!fuelOk) { boomIn = stickIn = bucketIn = swingIn = trackInput = turnInput = 0f; bladeIn = Mathf.Min(bladeIn, 0f); }
            LastInputs = new Inputs { boom = boomIn, stick = stickIn, bucket = bucketIn, swing = swingIn, tracks = trackInput, turn = turnInput, blade = bladeIn };
            // ISO pilot controls in the cab: left stick = swing / stick, right stick = bucket / boom (forward = down),
            // a travel lever per track, the blade lever on the right
            cab.left = new Vector2(Mathf.Clamp(swingIn, -1f, 1f), Mathf.Clamp(stickIn, -1f, 1f));
            cab.right = new Vector2(Mathf.Clamp(bucketIn, -1f, 1f), Mathf.Clamp(-boomIn, -1f, 1f));
            cab.leverL = Mathf.Clamp(trackInput + turnInput, -1f, 1f);
            cab.leverR = Mathf.Clamp(trackInput - turnInput, -1f, 1f);
            cab.aux = bladeIn;
            bladeAngle = Mathf.Clamp(bladeAngle + bladeIn * bladeSpeed * dt, bladeRange.x, bladeRange.y);
            BladeWork();

            swing += swingIn * swingSpeed * dt;
            boom = Mathf.Clamp(boom + boomIn * boomSpeed * dt, boomRange.x, boomRange.y);
            stick = Mathf.Clamp(stick + stickIn * stickSpeed * dt, stickRange.x, stickRange.y);
            bucket = Mathf.Clamp(bucket + bucketIn * bucketSpeed * dt, bucketRange.x, bucketRange.y);
            ApplyPose();

            workAmount = Mathf.Clamp01(Mathf.Abs(boomIn) + Mathf.Abs(stickIn) * 0.5f + Mathf.Abs(bucketIn) * 0.5f + Mathf.Abs(trackInput) + Mathf.Abs(swingIn) * 0.3f);

            // Dig: bucket tip in the ground while curling or pulling in (an opened-out bucket drags instead)
            float digInput = Mathf.Max(-bucketIn, -stickIn, 0f);
            float rockRoom = rockBox != null ? rockBox.HeldRockVolume : 0f;
            bool digging = digInput > 0f && bucketLoad.volume + rockRoom < bucketCapacity && bucket < dumpAngle;
            if (digging)
            {
                var site = DigSite.At(transform.position);
                if (site != null && site.IsUnderground(bucketTip.position))
                {
                    float rate = digRate;
                    if (site.LooseVolume(bucketTip.position, digRadius, out _) > bucketCapacity * 0.25f) rate *= looseDigBonus;
                    float want = Mathf.Min(rate * digInput * dt, bucketCapacity - bucketLoad.volume - rockRoom);
                    var got = DigSite.DigAnywhere(bucketTip.position, digRadius, want);
                    bucketLoad.Add(got);
                    if (!got.IsEmpty) PourFx.Dig(bucketTip.position);
                }
                // boulders small enough to fit roll into the bucket (they ride in it as real rocks)
                var cc = rockBox;
                if (cc != null && cc.Scoop(bucketTip.position, digRadius * 1.4f, bucketCapacity) is Boulder b)
                    GameUI.Toast($"Scooped up a {b.volume:0.0#} m³ boulder");
            }
            Spread(digging, dt);

            DumpStep(dt);
            UpdateBucketVisual();

            // What would the bucket dump into right now?
            var rec = MaterialTransfer.FindReceiverBelow(DumpPoint(AimAngle), transform, out _);
            underBucket = rec switch
            {
                DumpPickup t => $"{t.displayName}: {t.load.volume:0.00} / {t.capacity:0.0} m³",
                WashPlant w => $"Wash plant hopper: {w.hopper.volume:0.00} / {w.hopperSize.Value:0.#} m³",
                DirtPile p => $"Dirt pile ({p.load.volume:0.0} m³)",
                _ => "ground (makes a pile)",
            };
        }

        // nobody in the seat: a bucket left open keeps emptying
        protected override void IdleUpdate(float dt)
        {
            if (testInputs is Inputs t)
            {
                trackInput = t.tracks; turnInput = t.turn;
                Operate(t.boom, t.stick, t.bucket, t.swing, t.blade, dt);
                return;
            }
            LastInputs = default;
            DumpStep(dt); UpdateBucketVisual();
        }

        /// <summary>The bucket angle the load comes out at: now if it's open, else a typical dump (part way open).</summary>
        float AimAngle => bucket > dumpAngle ? bucket : Mathf.Lerp(dumpAngle, bucketRange.y, 0.5f);

        /// <summary>
        /// Where the load leaves the bucket opened to `angle`: across its mouth, between the hinge and the teeth.
        /// (Curled in, the teeth point up and back: the dirt doesn't come out there.)
        /// </summary>
        public Vector3 DumpPoint(float angle)
        {
            if (bucketTip == null) return transform.position;
            if (bucketPivot == null || bucketPivot.parent == null) return bucketTip.position;
            Vector3 tipLocal = Vector3.Scale(bucketPivot.localScale, bucketPivot.InverseTransformPoint(bucketTip.position));
            Vector3 tip = bucketPivot.parent.TransformPoint(bucketPivot.localPosition + Quaternion.Euler(-angle, 0f, 0f) * tipLocal);
            return Vector3.Lerp(bucketPivot.position, tip, 0.6f);
        }

        /// <summary>Dump: bucket opened past the dump angle pours its load out (driven or not).</summary>
        void DumpStep(float dt)
        {
            if (bucket > dumpAngle && !bucketLoad.IsEmpty && bucketTip != null)
            {
                float open = Mathf.InverseLerp(dumpAngle, bucketRange.y, bucket);
                var pour = bucketLoad.Take(dumpRate * (0.3f + open) * dt);
                MaterialTransfer.Pour(DumpPoint(bucket), pour, transform);
            }
        }

        void UpdateBucketVisual()
        {
            if (bucketLoadVisual == null) return;
            float f = Mathf.Clamp01(bucketLoad.volume / bucketCapacity);
            bucketLoadVisual.gameObject.SetActive(f > 0.01f);
            // mound grows from a thin layer to a heaped bucket
            float h = Mathf.Lerp(0.2f, 1f, Mathf.Sqrt(f));
            float w = Mathf.Lerp(0.6f, 1f, Mathf.Sqrt(f));
            bucketLoadVisual.localScale = new Vector3(loadBaseScale.x * w, loadBaseScale.y * h, loadBaseScale.z * w);
            bucketLoadVisual.localPosition = loadBasePos + Vector3.up * loadBaseScale.y * (h - 1f) * 0.5f;
        }

        void FixedUpdate()
        {
            if (!Occupied && !testInputs.HasValue) { trackInput = turnInput = 0f; }
            ToolColliders.IgnorePiles(tools, toolPiles);
            if (rb.isKinematic) { Overload = 0f; return; }
            suspension.Step();
            DigResistance();
            float tilt = Vector3.Angle(transform.up, Vector3.up);
            MaxTilt = Mathf.Max(MaxTilt * 0.999f, tilt);
            bool grounded = suspension.Contacts > 0 ? suspension.Touching > 0
                : Physics.Raycast(transform.position + Vector3.up * 0.3f * sizeScale, Vector3.down, 0.6f * sizeScale + 0.1f, ~0, QueryTriggerInteraction.Ignore);
            if (!grounded) { suspension.ResetTrack(); return; }
            // tracks grip hard: set the velocity along the ground, keep what the springs and gravity do across it
            Vector3 n = suspension.Touching > 0 ? suspension.GroundNormal : Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, n).normalized;
            Vector3 v = rb.linearVelocity;
            float grip = suspension.Grip;
            rb.linearVelocity = suspension.Contacts > 0 ? suspension.TrackVelocity(v, fwd * trackInput * driveSpeed, n, grip) : Vector3.Lerp(v, fwd * trackInput * driveSpeed + n * Vector3.Dot(v, n), grip);
            if (Mathf.Abs(turnInput) > 0.01f)
                rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, turnInput * turnSpeed * Time.fixedDeltaTime, 0f));
            rb.angularVelocity = new Vector3(rb.angularVelocity.x, 0f, rb.angularVelocity.z);
        }

        /// <summary>
        /// The ground pushes back on a bucket that is forced in deeper than it can cut (bucket full, not curling,
        /// bedrock, or just shoving too fast). The push acts at the bucket tip, so a hard shove lifts the tracks
        /// on that side of the machine — the bigger the machine, the more it can take.
        /// </summary>
        void DigResistance()
        {
            Overload = 0f;
            var site = DigSite.At(transform.position);
            if (site == null || bucketTip == null) return;
            Vector3 tip = bucketTip.position;
            if (!site.Contains(tip)) return;
            float allowed = allowedPenetration >= 0f ? allowedPenetration : digRadius * 0.45f;
            float excess = site.SurfaceY(tip) - tip.y - allowed;
            if (excess <= 0f) return;
            float m = rb.mass, w = m * -Physics.gravity.y * suspension.gravityScale;
            float k = w / Mathf.Max(0.05f, resistanceTravel * sizeScale);
            float vUp = rb.GetPointVelocity(tip).y;
            float f = Mathf.Clamp(k * excess - Mathf.Sqrt(k * m) * vUp, 0f, w * 0.9f);
            rb.AddForceAtPosition(Vector3.up * f, tip, ForceMode.Force);
            Overload = f / w;
        }

        void ApplyPose()
        {
            if (upper != null) upper.localRotation = Quaternion.Euler(0f, swing, 0f);
            if (boomPivot != null) boomPivot.localRotation = Quaternion.Euler(-boom, 0f, 0f);
            if (stickPivot != null) stickPivot.localRotation = Quaternion.Euler(-stick, 0f, 0f);
            if (bucketPivot != null) bucketPivot.localRotation = Quaternion.Euler(-bucket, 0f, 0f);
            if (blade != null) blade.localRotation = Quaternion.Euler(-bladeAngle, 0f, 0f);
        }

        public bool HasBlade => blade != null;
        public void SetBlade(float deg) { bladeAngle = Mathf.Clamp(deg, bladeRange.x, bladeRange.y); ApplyPose(); }
        float[] netPose;
        public override void NetWrite(System.IO.BinaryWriter w)
        {
            base.NetWrite(w);
            w.Write(boom); w.Write(stick); w.Write(bucket); w.Write(swing); w.Write(bladeAngle);
            NetIO.Write(w, bucketLoad);
        }
        public override void NetRead(System.IO.BinaryReader r)
        {
            base.NetRead(r);
            netPose ??= new float[5];
            for (int i = 0; i < 5; i++) netPose[i] = r.ReadSingle();
            var was = bucketLoad; bucketLoad = NetIO.ReadLoad(r);
            // their bucket is open and emptying: draw it pouring here too
            if (was.volume - bucketLoad.volume > 1e-4f && netPose[2] > dumpAngle) { netSpill += was.volume - bucketLoad.volume; netSpillMix = was; }
        }
        float netSpill; MaterialLoad netSpillMix;
        protected override void NetShow(float dt)
        {
            if (netPose == null) return;
            boom = NetIO.Ease(boom, netPose[0], dt); stick = NetIO.Ease(stick, netPose[1], dt); bucket = NetIO.Ease(bucket, netPose[2], dt);
            swing = Mathf.LerpAngle(swing, netPose[3], 1f - Mathf.Exp(-14f * dt)); bladeAngle = NetIO.Ease(bladeAngle, netPose[4], dt);
            ApplyPose(); UpdateBucketVisual();
            if (netSpill > 1e-5f)
            {
                float give = Mathf.Min(netSpill, Mathf.Max(0.004f, netSpill * Mathf.Clamp01(dt * 6f)));
                netSpill -= give;
                FPSBase.Mining.DirtGrains.PourVisual(DumpPoint(bucket), netSpillMix.Scaled(give / Mathf.Max(1e-4f, netSpillMix.volume)), transform);
            }
        }

        public void SetPose(float boomDeg, float stickDeg, float bucketDeg, float swingDeg)
        {
            boom = Mathf.Clamp(boomDeg, boomRange.x, boomRange.y);
            stick = Mathf.Clamp(stickDeg, stickRange.x, stickRange.y);
            bucket = Mathf.Clamp(bucketDeg, bucketRange.x, bucketRange.y);
            swing = swingDeg;
            ApplyPose();
        }

        /// <summary>
        /// The backfill blade works like a small dozer blade: down and driving forward it cuts and pushes dirt
        /// ahead; reversing with it on the ground drags loose dirt back (backfilling a trench toward the machine).
        /// </summary>
        public float BladeWork()
        {
            var site = DigSite.At(transform.position);
            if (blade == null || bladeEdge == null || site == null || rb == null) return 0f;
            float v = Vector3.Dot(rb.linearVelocity, transform.forward);
            Vector3 e = bladeEdge.position;
            if (!site.Contains(e)) return LastBladeMoved = 0f;
            if (v > 0.05f) return LastBladeMoved = site.BladePush(e, transform.forward, bladeWidth, e.y);
            if (v < -0.05f) return LastBladeMoved = site.BladePush(e - transform.forward * 0.15f * sizeScale, -transform.forward, bladeWidth, e.y + 0.02f, 0.45f * sizeScale, out _);
            return LastBladeMoved = 0f;
        }
        /// <summary>Dirt the blade moved last frame (m^3).</summary>
        public float LastBladeMoved { get; private set; }
        public float BladeAngle => bladeAngle;

        string TipGround
        {
            get
            {
                var site = DigSite.At(transform.position);
                if (site == null || bucketTip == null) return "—";
                string g = site.GroundNameAt(bucketTip.position, out float ease);
                return ease > 0f && ease < 0.8f ? g + " (sticky, slow)" : ease > 1.1f ? g + " (digs fast)" : g;
            }
        }

        static float Axis(KeyControl pos, KeyControl neg) => (pos.isPressed ? 1f : 0f) - (neg.isPressed ? 1f : 0f);

        public override string HudBody =>
            $"Fuel {fuel:0.0} / {fuelCapacity:0} L\n" +
            $"Bucket: {bucketLoad.Describe()} · {bucketLoad.Grade:0.0} g/m³\n" +
            $"Bucket tip in: {TipGround} · Under bucket: {underBucket}\n" +
            $"Controls: {(GameSettings.ExcavatorControls == ExcavatorControlMode.Realistic ? "Realistic" : "Simplified")} (change in Esc menu)" +
            (blade != null ? $"\nFront blade {(bladeAngle < -1f ? "down (pushes when you drive forward)" : bladeAngle > 6f ? "up" : "at ground")} - {N(GameAction.ExBladeUp)}/{N(GameAction.ExBladeDown)} blade up/down" : "");

        public override float HudBar => bucketLoad.volume / bucketCapacity;
        public override string HudBarLabel => Overload > 0.12f
            ? $"DIGGING TOO HARD  —  the ground is lifting the machine ({Overload * 100f:0}% of its weight). Ease off the boom or curl slower."
            : bucketLoad.volume >= bucketCapacity * 0.98f
            ? "BUCKET FULL  —  swing over the truck and open the bucket"
            : $"Bucket {bucketLoad.volume * 1000f:0} / {bucketCapacity * 1000f:0} L";

        public override string HudControls => GameSettings.ExcavatorControls == ExcavatorControlMode.Realistic
            ? $"Tracks: {N(GameAction.TracksForward)}/{N(GameAction.TracksBack)}/{N(GameAction.TracksLeft)}/{N(GameAction.TracksRight)}\n" +
              $"{N(GameAction.StickOut)}/{N(GameAction.StickIn)} stick out/in · {N(GameAction.SwingLeft)}/{N(GameAction.SwingRight)} swing\n" +
              $"{N(GameAction.BoomDown)}/{N(GameAction.BoomUp)} boom down/up · {N(GameAction.ExBucketCurl)}/{N(GameAction.ExBucketDump)} bucket curl/dump\n" +
              $"Dig: tip in the ground + curl or pull stick in · Dump: open bucket\n{N(GameAction.CabView)} cab view · {N(GameAction.Interact)} get out"
            : $"{N(GameAction.DriveForward)}{N(GameAction.SteerLeft)}{N(GameAction.DriveBack)}{N(GameAction.SteerRight)}: drive tracks\nMouse X swing · Mouse Y boom · Scroll stick\nHold LMB curl/dig · Hold RMB dump\n{N(GameAction.CabView)} cab view · {N(GameAction.Interact)} get out";

        static string N(GameAction a) => Controls.N(a);
    }
}
