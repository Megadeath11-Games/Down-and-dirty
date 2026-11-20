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
            BuildShell();
            tools = ToolColliders.Find(transform);
            if (GetComponent<BucketChain>() == null) gameObject.AddComponent<BucketChain>();
            if (bucketLoadVisual != null)
            {
                loadBaseScale = bucketLoadVisual.localScale; loadBasePos = bucketLoadVisual.localPosition;
                // the dirt in the bucket is live clumps: new ones come in at the teeth, the ones at the teeth leave first
                var cc = ClumpContainer.Attach(gameObject, bucketLoadVisual.parent, loadBasePos, loadBaseScale, bucketCapacity,
                    () => bucketLoad, () => bucketTip != null ? bucketTip.position : (Vector3?)null, () => bucketTip != null ? bucketTip.position : transform.position,
                    bucketLoadVisual.GetComponentsInChildren<Renderer>(true));
                cc.takeOut = v => bucketLoad.Take(v);
                cc.enterAt = () => scoopedAt;
                // the marker shows where the load lands once the bucket opens, not where the teeth are now
                DumpMarker.Attach(gameObject, bucketTip, Mathf.Max(0.35f, digRadius * 1.3f)).sourceAt = () => DumpPoint(AimAngle);
                // the builder knows which way the bucket opens; otherwise find it from the bucket's shape
                if (bucketMouth.sqrMagnitude > 0.01f) cc.SetMouth(bucketMouth.normalized);
                else cc.AutoMouth(bucketLoadVisual.GetComponentsInChildren<Renderer>(true));
                // while the bucket is cutting into the ground the load is packed in: nothing falls out
                // (the bucket cuts its own way now, so the teeth are never buried: cutting just now counts)
                cc.packed = Packed;
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

        // ---- the bucket's steel: points all over its shell, so it cuts the ground in its own shape
        Vector3[] shellLocal, shellW, shellPrev, shellVel;
        float[] shellLim; bool[] shellScoop, shellTooth;
        bool shellHasPrev;
        Vector3 shellMid;
        Vector3? scoopedAt;
        // the boom and stick: they can't go into the ground at all (they push back on the machine instead)
        Vector3[] armLocal; Transform[] armFrame;
        /// <summary>Points on the bucket's steel (for the test).</summary>
        public int ShellPoints => shellLocal != null ? shellLocal.Length : 0;
        /// <summary>How deep the bucket's steel is under the drawn dirt right now, at its deepest point (m; for the test).</summary>
        public float DeepestInDirt()
        {
            var site = DigSite.At(transform.position);
            if (site == null || shellLocal == null) return 0f;
            var m = bucketPivot.localToWorldMatrix;
            float d = 0f;
            for (int i = 0; i < shellLocal.Length; i++)
            {
                Vector3 p = m.MultiplyPoint3x4(shellLocal[i]);
                if (site.Contains(p)) d = Mathf.Max(d, site.SurfaceY(p) - p.y);
            }
            return d;
        }
        /// <summary>Whether the bucket was being dragged mouth first through the dirt last frame (for the test).</summary>
        public bool LastScooping { get; private set; }
        /// <summary>Dirt the bucket took in through its mouth last frame (m^3).</summary>
        public float LastScooped { get; private set; }
        /// <summary>How far any steel was in the unbroken ground past what it may (m), last physics step (for the test).</summary>
        public float LastPenetration { get; private set; }

        static void SampleMeshes(Transform root, System.Func<Transform, bool> skip, float step, int cap,
            System.Action<Vector3, Transform> add)
        {
            float q = step * 0.7f;
            var seen = new System.Collections.Generic.HashSet<long>();
            int count = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (skip(mf.transform)) continue;
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var m = mf.transform.localToWorldMatrix;
                void AddW(Vector3 w)
                {
                    long key = ((long)Mathf.RoundToInt(w.x / q) * 73856093L) ^ ((long)Mathf.RoundToInt(w.y / q) * 19349663L) ^ ((long)Mathf.RoundToInt(w.z / q) * 83492791L);
                    if (count < cap && seen.Add(key)) { add(w, mf.transform); count++; }
                }
                void Tri(Vector3 a, Vector3 b, Vector3 c)
                {
                    float L = Mathf.Max((b - a).magnitude, Mathf.Max((c - a).magnitude, (c - b).magnitude));
                    int k = Mathf.Clamp(Mathf.CeilToInt(L / step), 1, 80);
                    for (int u = 0; u <= k; u++)
                        for (int w = 0; u + w <= k; w++)
                            AddW(a + (b - a) * (u / (float)k) + (c - a) * (w / (float)k));
                }
                if (mesh.isReadable)
                {
                    var v = mesh.vertices; var t = mesh.triangles;
                    for (int i = 0; i + 2 < t.Length && count < cap; i += 3)
                        Tri(m.MultiplyPoint3x4(v[t[i]]), m.MultiplyPoint3x4(v[t[i + 1]]), m.MultiplyPoint3x4(v[t[i + 2]]));
                }
                else
                {
                    // can't read the mesh: its box
                    var bb = mesh.bounds;
                    Vector3 C(int i) => m.MultiplyPoint3x4(bb.center + Vector3.Scale(bb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    int[] f = { 0, 1, 3, 2, 4, 5, 7, 6, 0, 1, 5, 4, 2, 3, 7, 6, 0, 2, 6, 4, 1, 3, 7, 5 };
                    for (int i = 0; i < 24; i += 4) { Tri(C(f[i]), C(f[i + 1]), C(f[i + 2])); Tri(C(f[i]), C(f[i + 2]), C(f[i + 3])); }
                }
            }
        }

        /// <summary>
        /// Sample the bucket's steel (every plate, the teeth, the cutting edge) as points in the bucket's frame, and
        /// mark the ones at the teeth; the teeth also get a hitbox of their own (they used to poke through things).
        /// The boom and stick are sampled too, more coarsely: they push back on the machine if forced into the ground.
        /// </summary>
        void BuildShell()
        {
            if (bucketPivot == null || bucketTip == null) return;
            var pts = new System.Collections.Generic.List<Vector3>();
            var toLocal = bucketPivot.worldToLocalMatrix;
            float step = Mathf.Clamp(0.045f * Mathf.Max(1f, sizeScale), 0.04f, 0.09f);
            bool NotSteel(Transform t) =>
                (bucketLoadVisual != null && t.IsChildOf(bucketLoadVisual)) || t.name.Contains("Load") || t.name.Contains("dirt");
            SampleMeshes(bucketPivot, NotSteel, step, 20000, (w, _) => pts.Add(toLocal.MultiplyPoint3x4(w)));
            if (pts.Count == 0) return;
            shellLocal = pts.ToArray();
            int n = shellLocal.Length;
            shellW = new Vector3[n]; shellPrev = new Vector3[n]; shellVel = new Vector3[n];
            shellLim = new float[n]; shellScoop = new bool[n]; shellTooth = new bool[n];
            // the teeth and cutting edge: the points near the tip
            Vector3 tipL = toLocal.MultiplyPoint3x4(bucketTip.position);
            var all = new Bounds(shellLocal[0], Vector3.zero);
            foreach (var p in shellLocal) all.Encapsulate(p);
            shellMid = all.center;
            float teethR = Mathf.Max(0.09f, all.size.magnitude * 0.16f);
            Bounds teeth = default; bool any = false;
            for (int i = 0; i < n; i++)
            {
                if ((shellLocal[i] - tipL).magnitude > teethR) continue;
                shellTooth[i] = true;
                if (!any) { teeth = new Bounds(shellLocal[i], Vector3.zero); any = true; } else teeth.Encapsulate(shellLocal[i]);
            }
            if (any && bucketPivot.Find("Tool Teeth") == null)
            {
                var go = new GameObject("Tool Teeth");
                go.transform.SetParent(bucketPivot, false);
                go.transform.localPosition = teeth.center;
                var bc = go.AddComponent<BoxCollider>();
                Vector3 sz = teeth.size, ls = bucketPivot.lossyScale;
                float thin = 0.03f / Mathf.Max(0.01f, Mathf.Abs(ls.x));
                bc.size = new Vector3(Mathf.Max(sz.x, thin), Mathf.Max(sz.y, thin), Mathf.Max(sz.z, thin));
            }

            // the arm: boom and stick steel (not the bucket), each point kept in the frame of the part it's on
            var arm = new System.Collections.Generic.List<Vector3>();
            var frames = new System.Collections.Generic.List<Transform>();
            Transform armRoot = boomPivot != null ? boomPivot : stickPivot;
            if (armRoot != null)
                SampleMeshes(armRoot, t => t.IsChildOf(bucketPivot) || NotSteel(t), step * 2.5f, 4000,
                    (w, t) => { arm.Add(t.InverseTransformPoint(w)); frames.Add(t); });
            armLocal = arm.ToArray(); armFrame = frames.ToArray();
        }

        /// <summary>
        /// The bucket in the ground: every bit of its steel cuts the dirt it moves through, in its own shape. Moving
        /// mouth first (curling, crowding, dragging in) the dirt it cuts goes in through the mouth: it really scoops.
        /// Moving any other way (curled out into a pile, swung sideways, the back pushed along) the steel shoves the
        /// dirt ahead of it like a small blade. The teeth bite into unbroken ground whichever way they go; the back
        /// of the bucket only scrapes it; and no more is cut than the machine has the force for (the rest of the
        /// bucket stays against the ground and pushes back: see DigResistance).
        /// </summary>
        /// <summary>The bucket is being pressed straight down on its bottom: it packs the dirt and lifts the machine instead of cutting.</summary>
        public bool Pressing { get; private set; }

        /// <summary>Half the bucket's width (m): spilled and shoved dirt spreads across it instead of piling up in a spike.</summary>
        float BucketHalfWidth()
        {
            if (bucketHalfWidth > 0f) return bucketHalfWidth;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in shellLocal) { lo = Mathf.Min(lo, p.x); hi = Mathf.Max(hi, p.x); }
            bucketHalfWidth = hi > lo ? (hi - lo) * 0.5f * Mathf.Abs(bucketPivot.lossyScale.x) : 0.3f * sizeScale;
            return bucketHalfWidth;
        }
        float bucketHalfWidth, pressUntil = -10f;
        bool pressIntent;

        void Carve(bool room, float digInput, float dt)
        {
            LastSpread = 0f; LastScooped = 0f; scoopedAt = null; Pressing = false;
            if (shellLocal == null || bucketTip == null || bucketPivot == null) return;
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
            var site = DigSite.At(bucketTip.position);
            if (site == null || !site.Contains(bucketTip.position)) return;
            var cc = rockBox;
            Vector3 mouthW = cc != null && bucketLoadVisual != null ? (bucketLoadVisual.parent.rotation * cc.mouth).normalized : bucketPivot.up;
            // The bucket scoops only when it's dragged through the dirt mouth first (curling or pulling the stick in):
            // then the cutting edge slices the dirt off and it goes in through the mouth. Shoved straight down
            // the teeth only creep in a little and the rest of the steel just presses on the ground (which pushes the
            // machine up): you can't fill a bucket by stamping it in. The back or side first, it shoves loose dirt
            // along and barely scrapes the unbroken ground.
            float scrape = 0.04f * sizeScale * dt;
            Vector3 teethW = (bucketTip.position - m.MultiplyPoint3x4(shellMid)).normalized;
            Vector3 vTip = Vector3.zero; int nt = 0;
            for (int i = 0; i < n; i++) if (shellTooth[i]) { vTip += shellVel[i]; nt++; }
            if (nt > 0) vTip /= nt;
            float tsp = vTip.magnitude;
            // (the rams that drag a bucket through the dirt are the stick and the curl: the boom alone only presses)
            bool scooping = digInput > 0.05f && tsp > 0.01f && Vector3.Dot(vTip, mouthW) > 0.2f * tsp;
            bool teethIn = !scooping && tsp > 0.01f && Vector3.Dot(vTip, teethW) > 0.5f * tsp;
            float creep = 0.05f * sizeScale * dt;
            // Pressed straight down on its flat bottom (taking the weight off the tracks to turn): the steel doesn't
            // cut or shove anything, it packs the dirt under it and the ground pushes the machine up (see Penetration).
            // (the boom held down with nothing else working is that.) Any steel going mostly straight down packs the
            // dirt under it rather than shoving it out of the way; teeth first, only the teeth creep in.
            // (held a moment so a frame where the stalled arm doesn't move, or the machine rocks, doesn't drop it)
            if (!scooping && pressIntent) pressUntil = Time.time + 0.4f;
            Pressing = !scooping && Time.time < pressUntil;
            for (int i = 0; i < n; i++)
            {
                Vector3 v = shellVel[i]; float sp = v.magnitude;
                if (sp < 0.01f || sp > 30f) { shellLim[i] = -1f; shellScoop[i] = false; continue; }
                if (teethIn && shellTooth[i]) { shellLim[i] = creep; shellScoop[i] = false; continue; }
                if (Pressing || (!scooping && v.y < -0.5f * sp)) { shellLim[i] = v.y < 0f ? -2f : -1f; shellScoop[i] = false; continue; }
                // (a full bucket still cuts when dragged: what it slices off can't get in, so it heaps up in front of
                // the mouth and gets pushed or pulled along with the bucket, like a little blade)
                shellScoop[i] = scooping;
                shellLim[i] = shellScoop[i] ? 10f : (scooping || teethIn) && shellTooth[i] ? creep : scrape;
            }
            LastScooping = scooping;
            // how much unbroken ground the hydraulics can cut this frame (clay is slower, sand quicker)
            site.GroundNameAt(bucketTip.position, out float ease);
            if (ease <= 0f) ease = 1f;
            float budget = digRate * 2.5f * ease * dt;
            float cut = site.CarveBelow(shellW, shellLim, shellScoop, shellVel, n, budget, out var sc, out var pu, out var sAt, out var pAt);
            if (cut > 0f && scooping) lastCutAt = Time.time;
            if (cut <= 0f) return;
            if (!sc.IsEmpty)
            {
                float space = Mathf.Max(0f, bucketCapacity * HoldFraction - bucketLoad.volume - (cc != null ? cc.HeldRockVolume : 0f));
                var into = sc.Take(space);
                bucketLoad.Add(into);
                LastScooped = into.volume;
                scoopedAt = sAt;
                if (!sc.IsEmpty) // heaped over: the rest rolls off the front
                {
                    // (past the furthest steel that way, so it doesn't land on the bucket)
                    Vector3 dir = Vector3.ProjectOnPlane(mouthW, Vector3.up);
                    dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;
                    float reach = 0f;
                    for (int i = 0; i < n; i++) reach = Mathf.Max(reach, Vector3.Dot(shellW[i] - sAt, dir));
                    // (spread across the width of the mouth, not dropped in one spot where it stacks up in a spike)
                    float spread = Mathf.Max(site.Cell * 1.2f, BucketHalfWidth() * 0.8f);
                    Vector3 off = sAt + dir * (reach + spread * 0.6f);
                    site.Deposit(off, sc, spread);
                }
            }
            if (!pu.IsEmpty) { site.Deposit(pAt, pu, site.Cell * 1.2f); LastSpread = pu.volume; }
            strainAmount = Mathf.Max(strainAmount, Mathf.Clamp01(cut / dt / Mathf.Max(0.01f, digRate * 2f)));
            if (scooping && Random.value < 0.15f) PourFx.Dig(bucketTip.position);
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

            float boom0 = boom, stick0 = stick, bucket0 = bucket, swing0 = swing;
            swing += swingIn * swingSpeed * dt;
            boom = Mathf.Clamp(boom + boomIn * boomSpeed * dt, boomRange.x, boomRange.y);
            stick = Mathf.Clamp(stick + stickIn * stickSpeed * dt, stickRange.x, stickRange.y);
            bucket = Mathf.Clamp(bucket + bucketIn * bucketSpeed * dt, bucketRange.x, bucketRange.y);
            ApplyPose();

            workAmount = Mathf.Clamp01(Mathf.Abs(boomIn) + Mathf.Abs(stickIn) * 0.5f + Mathf.Abs(bucketIn) * 0.5f + Mathf.Abs(trackInput) + Mathf.Abs(swingIn) * 0.3f);
            hydraulicAmount = Mathf.Clamp01(Mathf.Abs(boomIn) + Mathf.Abs(stickIn) * 0.8f + Mathf.Abs(bucketIn) * 0.7f + Mathf.Abs(swingIn) * 0.6f + Mathf.Abs(bladeIn) * 0.5f);
            // a ram held against the end of its travel: the relief valve groans
            if ((boomIn > 0.1f && boom >= boomRange.y - 0.01f) || (boomIn < -0.1f && boom <= boomRange.x + 0.01f) ||
                (stickIn > 0.1f && stick >= stickRange.y - 0.01f) || (stickIn < -0.1f && stick <= stickRange.x + 0.01f) ||
                (bucketIn > 0.1f && bucket >= bucketRange.y - 0.01f) || (bucketIn < -0.1f && bucket <= bucketRange.x + 0.01f)) strainAmount = 0.6f;
            scrapeAmount = Mathf.Clamp01(LastBladeMoved * 40f);

            // Dig: bucket tip in the ground while curling or pulling in (an opened-out bucket drags instead)
            float digInput = Mathf.Max(-bucketIn, -stickIn, 0f);
            float rockRoom = rockBox != null ? rockBox.HeldRockVolume : 0f;
            bool room = bucketLoad.volume + rockRoom < bucketCapacity && bucket < dumpAngle;
            bool digging = digInput > 0f && room;
            // (dragged through the dirt even opened out, the dirt is packed in as it's cut: it only pours out of an
            // open bucket once it stops cutting)
            pressIntent = boomIn < -0.1f && Mathf.Abs(stickIn) < 0.3f && Mathf.Abs(bucketIn) < 0.3f && Mathf.Abs(swingIn) < 0.3f;
            Carve(bucketLoad.volume + rockRoom < bucketCapacity * HoldFraction, digInput, dt);
            // the hydraulics stall against ground they can't cut: the arm stops there (and the ground pushes the
            // machine up instead), so no steel goes into the earth
            // (only the rams that are pushing it in stop: the boom stalls, the bucket can still curl)
            float pen = Penetration(out _);
            float stallAt = Mathf.Max(0.04f * sizeScale, lastPen + 0.001f);
            if (pen > stallAt)
            {
                // stop the ram that helps most, then the next, until it's out of the ground again
                float[] now = { boom, swing, stick, bucket }, was = { boom0, swing0, stick0, bucket0 };
                var held = new bool[4];
                for (int round = 0; round < 4 && pen > stallAt; round++)
                {
                    int best = -1; float bestPen = pen;
                    for (int j = 0; j < 4; j++)
                    {
                        if (held[j] || now[j] == was[j]) continue;
                        SetJoints(j, was, now, held);
                        float p = Penetration(out _);
                        if (p < bestPen - 1e-4f) { bestPen = p; best = j; }
                    }
                    if (best < 0) break;
                    held[best] = true; pen = bestPen;
                }
                SetJoints(-1, was, now, held);
                pen = Penetration(out _);
                strainAmount = 0.8f; Stalls++;
                if (shellLocal != null && shellHasPrev)
                {
                    var bm = bucketPivot.localToWorldMatrix;
                    for (int i = 0; i < shellLocal.Length; i++) shellPrev[i] = shellW[i] = bm.MultiplyPoint3x4(shellLocal[i]);
                }
            }
            lastPen = pen;
            if (digging)
            {
                var site = DigSite.At(transform.position);
                // off the dirt field (a dumped pile somewhere else): the old way, from the pile under the teeth
                if ((site == null || !site.Contains(bucketTip.position)) && DirtPile.FindContaining(bucketTip.position) != null)
                {
                    float rate = digRate;
                    rate *= looseDigBonus;
                    float want = Mathf.Min(rate * digInput * dt, bucketCapacity - bucketLoad.volume - rockRoom);
                    var got = DigSite.DigAnywhere(bucketTip.position, digRadius, want);
                    bucketLoad.Add(got);
                    if (!got.IsEmpty) { PourFx.Dig(bucketTip.position); strainAmount = Mathf.Max(strainAmount, digInput); }
                }
                // boulders small enough to fit roll into the bucket (they ride in it as real rocks)
                var cc = rockBox;
                if (cc != null && cc.Scoop(bucketTip.position, digRadius * 1.4f, bucketCapacity) is Boulder b)
                    GameUI.Toast($"Scooped up a {b.volume:0.0#} m³ boulder");
            }

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

        /// <summary>How much of a full load the bucket holds at its angle: all of it curled in, under half opened out.</summary>
        float HoldFraction => bucket <= dumpAngle ? 1f : Mathf.Lerp(0.4f, 0f, Mathf.InverseLerp(dumpAngle, bucketRange.y, bucket));

        /// <summary>While the bucket is cutting, or still down in the hole it cut, the load is packed in: nothing falls out.</summary>
        bool Packed()
        {
            if (Time.time - lastCutAt < 0.35f) return true;
            var s = DigSite.At(transform.position);
            if (bucketTip == null) return false;
            Vector3 t = bucketTip.position;
            if (s == null || !s.Contains(t)) return false;
            // still down in the hole it cut: the walls round it hold the load in
            float r = 0.6f * sizeScale, top = s.SurfaceY(t);
            top = Mathf.Max(top, Mathf.Max(s.SurfaceY(t + new Vector3(r, 0f, 0f)), s.SurfaceY(t - new Vector3(r, 0f, 0f))));
            top = Mathf.Max(top, Mathf.Max(s.SurfaceY(t + new Vector3(0f, 0f, r)), s.SurfaceY(t - new Vector3(0f, 0f, r))));
            return top > t.y - 0.08f;
        }

        /// <summary>Dump: bucket opened past the dump angle pours its load out (driven or not).</summary>
        void DumpStep(float dt)
        {
            if (bucket > dumpAngle && !bucketLoad.IsEmpty && bucketTip != null && !Packed())
            {
                float open = Mathf.InverseLerp(dumpAngle, bucketRange.y, bucket);
                var pour = bucketLoad.Take(dumpRate * (0.3f + open) * dt);
                // (across the width of the bucket, a random spot each frame, not all from its middle)
                Vector3 across = shellLocal != null && bucketPivot != null ? bucketPivot.right * (Random.Range(-1f, 1f) * BucketHalfWidth() * 0.8f) : Vector3.zero;
                Vector3 from = DumpPoint(bucket), at = from + across;
                // (over a truck or hopper, only the bit of the bucket that's over it)
                var rec = MaterialTransfer.FindReceiverBelow(from, transform, out _);
                if (rec != null && MaterialTransfer.FindReceiverBelow(at, transform, out _) != rec) at = from;
                MaterialTransfer.Pour(at, pour, transform, rockBox != null ? rockBox.ClumpRadius : 0f);
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
        /// <summary>
        /// How far the steel is into unbroken ground past what it may be (m), and where: the bucket where it couldn't
        /// cut, or the boom or stick, which can't cut at all. Loose dirt doesn't count: it just gets shoved.
        /// </summary>
        float Penetration(out Vector3 at)
        {
            at = bucketTip != null ? bucketTip.position : transform.position;
            var site = DigSite.At(transform.position);
            if (site == null || bucketTip == null || !site.Contains(bucketTip.position)) return 0f;
            float allowed = allowedPenetration >= 0f ? allowedPenetration : 0.03f * sizeScale;
            float excess = 0f;
            PenetrationBy = "bucket";
            if (shellLocal == null) return Mathf.Max(0f, site.SurfaceY(at) - at.y - digRadius * 0.45f);
            var bm = bucketPivot.localToWorldMatrix;
            for (int i = 0; i < shellLocal.Length; i += 2)
            {
                Vector3 p = bm.MultiplyPoint3x4(shellLocal[i]);
                // (pressed down flat, the packed dirt on top holds it up too)
                float d = (Pressing ? site.SurfaceY(p) : site.NativeY(p)) - p.y - allowed;
                if (d > excess) { excess = d; at = p; }
            }
            for (int i = 0; armLocal != null && i < armLocal.Length; i++)
            {
                Vector3 p = armFrame[i].TransformPoint(armLocal[i]);
                float d = site.NativeY(p) - p.y - allowed;
                if (d > excess) { excess = d; at = p; PenetrationBy = armFrame[i].name; }
            }
            return excess;
        }

        float lastPen, lastCutAt = -10f;

        /// <summary>The arm at its new angles, with the rams `held` (and `also`, if set) back where they were.</summary>
        void SetJoints(int also, float[] was, float[] now, bool[] held)
        {
            float J(int j) => held[j] || j == also ? was[j] : now[j];
            boom = J(0); swing = J(1); stick = J(2); bucket = J(3);
            ApplyPose();
        }
        /// <summary>Which part is furthest into the ground (for the test).</summary>
        public string PenetrationBy { get; private set; } = "";
        public int Stalls { get; private set; }
        public int TeethPoints { get { int k = 0; if (shellTooth != null) foreach (var b in shellTooth) if (b) k++; return k; } }

        void DigResistance()
        {
            Overload = 0f;
            // the backfill blade pushed down into unbroken ground lifts the machine (like on pavement)
            if (blade != null && bladeEdge != null)
            {
                Vector3 r = blade.right * (bladeWidth * 0.5f);
                Overload = GroundPush.Edge(rb, suspension.gravityScale, bladeEdge.position - r, bladeEdge.position + r, 7, 0.04f, 0.1f * sizeScale, out _);
            }
            float excess = Penetration(out Vector3 tip);
            LastPenetration = excess;
            if (excess <= 0f) return;
            float m = rb.mass, w = m * -Physics.gravity.y * suspension.gravityScale;
            float k = w / Mathf.Max(0.03f, Mathf.Min(resistanceTravel, 0.08f) * sizeScale);
            float vUp = rb.GetPointVelocity(tip).y;
            float f = Mathf.Clamp(k * excess - Mathf.Sqrt(k * m) * vUp, 0f, w * 0.9f);
            rb.AddForceAtPosition(Vector3.up * f, tip, ForceMode.Force);
            Overload = Mathf.Max(Overload, f / w);
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
            // it cuts a little below the ground it's on, not deeper (pushed down further, it lifts the machine instead)
            if (v > 0.05f) return LastBladeMoved = site.BladePush(e, transform.forward, bladeWidth, Mathf.Max(e.y, site.SurfaceY(e + transform.forward * 0.3f * sizeScale) - 0.08f * sizeScale));
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
            $"Controls: {(GameSettings.ExcavatorControls == ExcavatorControlMode.Realistic ? "Realistic" : "Simplified")} (change in Esc menu)\n" +
            $"Moving now: {MovingNow()}" +
            (blade != null ? $"\nFront blade {(bladeAngle < -1f ? "down (pushes when you drive forward)" : bladeAngle > 6f ? "up" : "at ground")} - {N(GameAction.ExBladeUp)}/{N(GameAction.ExBladeDown)} blade up/down" : "") +
            ChainLine();

        public override float HudBar => bucketLoad.volume / bucketCapacity;
        public override string HudBarLabel => Overload > 0.12f
            ? $"DIGGING TOO HARD  —  the ground is lifting the machine ({Overload * 100f:0}% of its weight). Ease off the boom or curl slower."
            : bucketLoad.volume >= bucketCapacity * 0.98f
            ? "BUCKET FULL  —  swing over the truck and open the bucket"
            : $"Bucket {bucketLoad.volume * 1000f:0} / {bucketCapacity * 1000f:0} L";

        public override string HudControls => GameSettings.ExcavatorControls == ExcavatorControlMode.Realistic
            ? $"Tracks: {N(GameAction.TracksForward)}/{N(GameAction.TracksBack)}/{N(GameAction.TracksLeft)}/{N(GameAction.TracksRight)}\n" +
              $"{N(GameAction.StickOut)}/{N(GameAction.StickIn)} stick out/in · {N(GameAction.SwingLeft)}/{N(GameAction.SwingRight)} swing\n" +
              $"{N(GameAction.BoomDown)}/{N(GameAction.BoomUp)} boom down/up · {N(GameAction.ExBucketCurl)}/{N(GameAction.ExBucketDump)} bucket curl/dump (or numpad 8/5, 4/6)\n" +
              $"Dig: tip in the ground + curl or pull stick in · Dump: open bucket\n{N(GameAction.CabView)} cab view · {N(GameAction.Interact)} get out"
            : $"{N(GameAction.DriveForward)}{N(GameAction.SteerLeft)}{N(GameAction.DriveBack)}{N(GameAction.SteerRight)}: drive tracks\nMouse X swing · Mouse Y boom · Scroll stick\nHold LMB curl/dig · Hold RMB dump\n{N(GameAction.CabView)} cab view · {N(GameAction.Interact)} get out";

        static string N(GameAction a) => Controls.N(a);

        string ChainLine()
        {
            var c = GetComponent<BucketChain>();
            if (c == null) return "";
            if (c.Held != null) return $"\nChain: {c.Held.Info.name.ToLower()} ({c.Held.Info.kg:N0} kg) on the hook - {N(GameAction.Hook)} let it go";
            if (c.Out) return c.Nearest(0.75f) != null ? $"\nChain down: the hook's by a block's loop - {N(GameAction.Hook)} hook it" : $"\nChain down: put the hook by a block's loop ({N(GameAction.Hook)} away from one stows it)";
            foreach (var b in FPSBase.City.ConcreteBlock.All) if (b != null && (b.transform.position - transform.position).sqrMagnitude < 400f) return $"\nConcrete blocks nearby: {N(GameAction.Hook)} lets the bucket chain down to lift them";
            return "";
        }

        /// <summary>Which functions the controls are working this moment (all of them can move together).</summary>
        string MovingNow()
        {
            var i = LastInputs;
            var l = new System.Collections.Generic.List<string>();
            if (Mathf.Abs(i.boom) > 0.05f) l.Add(i.boom > 0f ? "boom up" : "boom down");
            if (Mathf.Abs(i.stick) > 0.05f) l.Add(i.stick > 0f ? "stick out" : "stick in");
            if (Mathf.Abs(i.bucket) > 0.05f) l.Add(i.bucket < 0f ? "curl" : "open");
            if (Mathf.Abs(i.swing) > 0.05f) l.Add(i.swing > 0f ? "swing right" : "swing left");
            if (Mathf.Abs(i.tracks) > 0.05f || Mathf.Abs(i.turn) > 0.05f) l.Add("tracks");
            if (Mathf.Abs(i.blade) > 0.05f) l.Add("blade");
            return l.Count == 0 ? "-" : string.Join(" + ", l);
        }
    }
}
