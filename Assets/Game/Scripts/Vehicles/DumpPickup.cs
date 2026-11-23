using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// 3/4-ton pickup (the GMT400 starter truck) with a hydraulic dump box. Load it with the excavator, back up the wash plant ramp,
    /// raise the box to tip the load into the hopper.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class DumpPickup : VehicleBase, IMaterialReceiver
    {
        [Header("Wheels (FL, FR, RL, RR)")]
        public WheelCollider[] wheels = new WheelCollider[4];
        public Transform[] wheelVisuals = new Transform[4];

        [Header("Drive")]
        public float motorTorque = 900f;     // per wheel, 4x4
        public float brakeTorque = 3500f;
        public float maxSteer = 32f;
        [Tooltip("An articulated hauler: the front frame swings at the hitch as it steers (the front wheels go with it).")]
        public FrameArticulation articulation;
        public float topSpeedKmh = 95f;
        [Tooltip("Has a transfer case: 2WD (rear) / 4-high / 4-low. Without one it's rear drive only.")]
        public bool transferCase;
        [Tooltip("4-low gear reduction: this much more torque at the wheels, top speed lowRangeTopKmh.")]
        public float lowRange = 2.6f;
        public float lowRangeTopKmh = 38f;

        public enum DriveMode { TwoHigh, FourHigh, FourLow }
        static readonly string[] DriveNames = { "2WD", "4-HIGH", "4-LOW" };
        public DriveMode driveMode = DriveMode.FourHigh;
        [Tooltip("Rear diff locked: both rear wheels turn together (one in the air or in mud doesn't steal the drive). Open: the wheel with the least grip gets it.")]
        public bool rearLocked;
        public void SetDrive(DriveMode m, bool locked) { driveMode = transferCase ? m : DriveMode.TwoHigh; rearLocked = locked; }

        [Header("Dump box")]
        public Transform bedPivot;           // hinge at the back of the box
        public Transform bedContents;        // dirt visual inside the box
        public Transform tailgatePour;       // where material leaves when tipping
        public Vector3 bedInnerSize = new Vector3(1.7f, 0.6f, 2.1f); // local to bedPivot's child "BedVolume"
        public BoxCollider bedVolume;
        public float capacity = 1.5f;        // m^3 (about 2 cubic yards)
        public float maxTilt = 50f;
        public float tiltSpeed = 12f;
        public float pourStartTilt = 28f;
        public float pourRate = 0.35f;       // m^3/s at full tilt
        [Tooltip("Seconds a full box takes to rush out with the tailgate fully open and the box all the way up.")]
        public float fullDumpSeconds = 4f;
        [Tooltip("Tailgate, hinged along its top edge: swings out as the load pushes on it.")]
        public Transform tailgate;
        public float tailgateMaxSwing = 75f;

        /// <summary>How far the tailgate chains let it open: full dump, half, quarter, a crack to spread a thin even layer, latched shut.</summary>
        public static readonly float[] GateSettings = { 1f, 0.5f, 0.25f, 0.1f, 0f };
        static readonly string[] GateNames = { "FULL OPEN", "HALF", "QUARTER", "SPREAD (thin layer)", "LATCHED" };
        public int gateSetting;
        float gateSwing;
        /// <summary>Volume poured out of the tailgate so far (m3).</summary>
        public float Poured { get; private set; }
        public float Tilt => tilt;
        public override float[] SavePose() => new[] { tilt };
        public override void LoadPose(float[] p) { if (p != null && p.Length >= 1) SetTilt(p[0]); }
        public void SetTilt(float deg) { tilt = Mathf.Clamp(deg, 0f, maxTilt); if (bedPivot != null) bedPivot.localRotation = Quaternion.Euler(-tilt, 0f, 0f); }

        public MaterialLoad load;
        [Tooltip("Low gears: how many times the engine's pull at a crawl (fading to 1x by about a third of top speed). Big trucks climb ramps on this.")]
        public float lowGearBoost = 1f;
        float tilt;
        Rigidbody rb;

        public float FreeSpace => Mathf.Max(0f, capacity - load.volume);

        public bool AcceptsAt(Vector3 worldPoint)
        {
            if (tilt > 8f || bedVolume == null || GooseneckFitted) return false;
            Vector3 l = bedVolume.transform.InverseTransformPoint(worldPoint) - bedVolume.center;
            Vector3 h = bedVolume.size * 0.5f;
            return Mathf.Abs(l.x) <= h.x && Mathf.Abs(l.z) <= h.z;
        }

        public void Receive(MaterialLoad l)
        {
            // another player runs this truck: the dirt goes into it on their game
            if (NetRemote && NetForwardLoad != null) { NetForwardLoad(this, l); return; }
            load.Add(l); UpdateContents();
        }

        float netTilt = float.NaN, netGate;
        public override void NetWrite(System.IO.BinaryWriter w) { base.NetWrite(w); w.Write(tilt); w.Write(gateSwing); w.Write(gateSetting); NetIO.Write(w, load); w.Write(GooseneckFitted); }
        public override void NetRead(System.IO.BinaryReader r)
        {
            base.NetRead(r);
            netTilt = r.ReadSingle(); netGate = r.ReadSingle(); gateSetting = r.ReadInt32();
            var was = load; load = NetIO.ReadLoad(r);
            // their box tipped and the load went down: draw it pouring out of the tailgate here too
            if (was.volume - load.volume > 1e-4f && netTilt > pourStartTilt * 0.8f) { netSpill += was.volume - load.volume; netSpillMix = was; }
            bool goose = r.ReadBoolean();
            if (gooseneckBall != null && goose != gooseneckFitted) { gooseneckFitted = goose; gooseneckBall.SetActive(goose); }
        }
        protected override void NetShow(float dt)
        {
            if (float.IsNaN(netTilt)) return;
            SetTilt(NetIO.Ease(tilt, netTilt, dt));
            gateSwing = NetIO.Ease(gateSwing, netGate, dt);
            if (tailgate != null) tailgate.localRotation = Quaternion.Euler(gateSwing, 0f, 0f); // swings out the back
            UpdateContents();
            NetWheels();
            if (netSpill > 1e-5f && tailgatePour != null)
            {
                float give = Mathf.Min(netSpill, Mathf.Max(0.004f, netSpill * Mathf.Clamp01(dt * 6f)));
                netSpill -= give;
                Vector3 across = (bedPivot != null ? bedPivot.right : transform.right) * (Random.Range(-1f, 1f) * bedInnerSize.x * 0.45f);
                FPSBase.Mining.DirtGrains.PourVisual(tailgatePour.position + across, netSpillMix.Scaled(give / Mathf.Max(1e-4f, netSpillMix.volume)), transform);
            }
        }
        float netSpill; MaterialLoad netSpillMix;

        // another player drives it: its wheel physics is off here, so roll the wheels by how far it moves on screen
        float[] netSpin; Vector3 netLastPos; bool netHasPos;
        void NetWheels()
        {
            if (wheels == null || wheelVisuals == null || RidingTrailer) { netHasPos = false; return; }
            if (netSpin == null || netSpin.Length != wheels.Length) netSpin = new float[wheels.Length];
            Vector3 pos = transform.position;
            float moved = netHasPos ? Vector3.Dot(pos - netLastPos, transform.forward) : 0f;
            if (Mathf.Abs(moved) > Mathf.Max(3f, Time.deltaTime * 40f)) moved = 0f; // a teleport (tow / reposition), not driving
            netLastPos = pos; netHasPos = true;
            for (int i = 0; i < wheels.Length && i < wheelVisuals.Length; i++)
            {
                var w = wheels[i];
                if (w == null || wheelVisuals[i] == null) continue;
                netSpin[i] = Mathf.Repeat(netSpin[i] + moved / Mathf.Max(0.1f, w.radius) * Mathf.Rad2Deg, 360f);
                w.GetWorldPose(out Vector3 p, out _);
                Quaternion q = transform.rotation * Quaternion.Euler(netSpin[i], 0f, 0f);
                if (articulation != null && i < 2) Articulate(ref p, ref q, transform.rotation);
                wheelVisuals[i].SetPositionAndRotation(p, q * Quaternion.Euler(0f, 0f, 90f));
            }
        }
        /// <summary>Tests: how far wheel `i` has rolled on screen while another player drives (deg).</summary>
        public float NetWheelSpin(int i) => netSpin != null && i < netSpin.Length ? netSpin[i] : 0f;

        protected void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.centerOfMass = new Vector3(0f, 0.35f, 0.2f);
            // the same fine wheel substeps at every speed: switching counts at 5 m/s made heavy-laden wheels lock up
            if (wheels != null && wheels.Length > 0 && wheels[0] != null) wheels[0].ConfigureVehicleSubsteps(5f, 12, 12);
            if (gooseneckBall != null) gooseneckBall.SetActive(gooseneckFitted);
            if (bedContents != null)
            {
                fullInner = bedInnerSize; fullCap = capacity; fullContentsZ = bedContents.localPosition.z;
                if (bedVolume != null) { fullVolC = bedVolume.center; fullVolS = bedVolume.size; }
                if (tidyTank != null) ApplyBed(tidyTankFitted);
            }
            if (bedContents != null)
            {
                // the load rides in the bed as live clumps that shake about and slide out of the tailgate when tipped
                float h = bedInnerSize.y; // the real side walls: a load can heap above them and slosh over
                var hide = new System.Collections.Generic.List<Renderer>(bedContents.GetComponentsInChildren<Renderer>(true));
                if (bedMound != null) hide.AddRange(bedMound.GetComponentsInChildren<Renderer>(true));
                var cc = ClumpContainer.Attach(gameObject, bedContents.parent,
                    new Vector3(bedContents.localPosition.x, 0.05f + h * 0.5f, bedContents.localPosition.z), new Vector3(bedInnerSize.x, h, bedInnerSize.z), capacity,
                    () => load, () => null, () => tailgatePour != null ? tailgatePour.position : transform.position - transform.forward * 3f, hide.ToArray());
                cc.takeOut = v => load.Take(v);
                cc.holdCos = Mathf.Cos(pourStartTilt * Mathf.Deg2Rad); // rocks slide out once the box tips past the pour angle
                clumps = cc;
            }
            UpdateContents();
            AddTyreBodies();
        }

        // ------------------------------------------------------------------ tyres you can stand on a ledge with

        /// <summary>
        /// Tests: each tyre is a solid body too (off: the old way, held up only by the wheel's ray down from the middle
        /// of the tyre, so one with half its tread still on a ledge dropped straight off it).
        /// </summary>
        public static bool TyreBodies = true;
        static PhysicsMaterial slick;
        static readonly System.Collections.Generic.Dictionary<int, Mesh> tyreMeshes = new();
        /// <summary>The solid tyre shapes (for the test).</summary>
        public MeshCollider[] TyreSupports { get; private set; }

        /// <summary>
        /// A solid tyre round each wheel, a little smaller than it (so on the ground the wheel does the work and the tyre
        /// never drags), at the wheel's resting height. Off the edge of a deck with part of its tread still on it, the
        /// wheel's ray finds nothing but the tyre sits on the edge and holds the truck up. Slick, so it never brakes it.
        /// </summary>
        void AddTyreBodies()
        {
            if (!TyreBodies || wheels == null || TyreSupports != null) return;
            if (slick == null) slick = new PhysicsMaterial("Tyre Support") { dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
            var list = new System.Collections.Generic.List<MeshCollider>();
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (w == null) continue;
                float width = TyreWidth(i, w);
                var go = new GameObject("Tyre Support");
                go.layer = w.gameObject.layer;
                go.transform.SetParent(w.transform, false);
                float rest = w.suspensionDistance * (1f - w.suspensionSpring.targetPosition);
                go.transform.localPosition = w.center - Vector3.up * rest;
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = TyreMesh(w.radius * 0.88f, width);
                mc.convex = true;
                mc.sharedMaterial = slick;
                list.Add(mc);
            }
            TyreSupports = list.ToArray();
        }

        /// <summary>How wide the tyre is across (m): from its model, along the axle.</summary>
        float TyreWidth(int i, WheelCollider w)
        {
            var vis = wheelVisuals != null && i < wheelVisuals.Length ? wheelVisuals[i] : null;
            var mf = vis != null ? vis.GetComponentInChildren<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) return Mathf.Max(0.2f, w.radius * 0.6f);
            // (the model's thinnest way across: a tyre is narrower than it is tall, whichever way the model is turned yet)
            Vector3 s = Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale);
            float width = Mathf.Min(Mathf.Abs(s.x), Mathf.Min(Mathf.Abs(s.y), Mathf.Abs(s.z)));
            return Mathf.Clamp(width, 0.15f, w.radius * 1.6f);
        }

        /// <summary>A plain 16-sided cylinder along x: the solid tyre.</summary>
        static Mesh TyreMesh(float r, float width)
        {
            int key = Mathf.RoundToInt(r * 100f) * 1000 + Mathf.RoundToInt(width * 100f);
            if (tyreMeshes.TryGetValue(key, out var m) && m != null) return m;
            const int n = 16;
            var v = new Vector3[n * 2];
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2f / n;
                float y = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                v[k] = new Vector3(-width * 0.5f, y, z);
                v[k + n] = new Vector3(width * 0.5f, y, z);
            }
            var t = new System.Collections.Generic.List<int>();
            for (int k = 0; k < n; k++)
            {
                int a = k, b = (k + 1) % n;
                t.AddRange(new[] { a, b, b + n, a, b + n, a + n });
                if (k > 0 && k < n - 1) { t.AddRange(new[] { 0, b, a }); t.AddRange(new[] { n, a + n, b + n }); }
            }
            m = new Mesh { name = "Tyre Support", vertices = v, triangles = t.ToArray() };
            m.RecalculateBounds();
            tyreMeshes[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ tidy tank (diesel transfer tank at the front of the bed)

        [Header("Tidy tank (front of the bed, can be taken out)")]
        public FuelTank tidyTank;
        [Tooltip("Bed length the tank takes up (m).")]
        public float tidyTankLength = 0.68f;
        public bool tidyTankFitted = true;
        Vector3 fullInner, fullVolC, fullVolS; float fullCap, fullContentsZ;
        ClumpContainer clumps;

        public bool TidyTankFitted => tidyTank != null && tidyTankFitted;

        [Header("Gooseneck ball (in the bed floor, just ahead of the rear axle)")]
        public GameObject gooseneckBall;
        public bool gooseneckFitted;
        public bool GooseneckFitted => gooseneckBall != null && gooseneckFitted;
        Hitch GooseHitch => gooseneckBall != null ? gooseneckBall.GetComponentInChildren<Hitch>(true) : null;

        /// <summary>Fit or take out the gooseneck ball. Fitting needs an empty box, down; taking it out needs nothing hitched to it.</summary>
        public bool SetGooseneck(bool fit)
        {
            if (gooseneckBall == null) return false;
            if (fit && (!load.IsEmpty || tilt > 1f)) return false;
            if (!fit && GooseHitch != null && GooseHitch.Attached != null) return false;
            gooseneckFitted = fit;
            gooseneckBall.SetActive(fit);
            return true;
        }

        /// <summary>Fit or take out the tidy tank (only with the bed empty and down). Returns false if it can't.</summary>
        public bool SetTidyTank(bool fit)
        {
            if (tidyTank == null || !load.IsEmpty || tilt > 1f) return false;
            ApplyBed(fit);
            UpdateContents();
            return true;
        }

        void ApplyBed(bool fit)
        {
            tidyTankFitted = fit;
            if (tidyTank != null) tidyTank.gameObject.SetActive(fit);
            float cut = fit ? tidyTankLength + 0.05f : 0f;
            bedInnerSize = new Vector3(fullInner.x, fullInner.y, Mathf.Max(0.5f, fullInner.z - cut));
            capacity = fullCap * bedInnerSize.z / fullInner.z;
            var p = bedContents.localPosition; p.z = fullContentsZ - cut * 0.5f; bedContents.localPosition = p;
            if (bedVolume != null) { bedVolume.center = fullVolC - new Vector3(0f, 0f, cut * 0.5f); bedVolume.size = fullVolS - new Vector3(0f, 0f, cut); }
            if (clumps != null)
                clumps.Reshape(new Vector3(bedContents.localPosition.x, 0.05f + bedInnerSize.y * 0.5f, bedContents.localPosition.z), bedInnerSize, capacity);
        }

        protected override void ExtraOptions(System.Collections.Generic.List<InteractOption> list)
        {
            if (gooseneckBall != null)
            {
                bool empty = load.IsEmpty && tilt < 1f && !Occupied && !NetRemoteDriven;
                if (!GooseneckFitted)
                    list.Add(new InteractOption { label = "Fit the gooseneck ball   (for gooseneck trailers: locks the dump box down)", enabled = empty,
                        note = !load.IsEmpty ? "empty the dump box first" : tilt >= 1f ? "lower the dump box first" : "someone's in the truck",
                        run = p => { if (SetGooseneck(true)) GameUI.Toast("Gooseneck ball fitted in the bed: back up under a gooseneck trailer's coupler"); } });
                else
                {
                    bool hooked = GooseHitch != null && GooseHitch.Attached != null;
                    list.Add(new InteractOption { label = "Take the gooseneck ball out   (dump box free again)", enabled = !hooked && !Occupied,
                        note = hooked ? "unhitch the gooseneck trailer first" : "someone's in the truck",
                        run = p => { if (SetGooseneck(false)) GameUI.Toast("Gooseneck ball out: the dump box works again"); } });
                }
            }
            if (tidyTank == null) return;
            bool can = load.IsEmpty && tilt < 1f && !Occupied && !NetRemoteDriven;
            string note = !load.IsEmpty ? "empty the dump box first" : tilt >= 1f ? "lower the dump box first" : "someone's in the truck";
            list.Add(tidyTankFitted
                ? new InteractOption { label = $"Take the tidy tank out   (whole bed for dirt: {fullCap:0.0} m³)", enabled = can, note = note,
                    run = p => { if (SetTidyTank(false)) GameUI.Toast($"Tidy tank unstrapped and set aside: the dump box holds {capacity:0.0} m³"); } }
                : new InteractOption { label = $"Fit the tidy tank   ({tidyTank.capacity:0} L of diesel for your machines, box {fullCap * Mathf.Max(0.5f, fullInner.z - tidyTankLength - 0.05f) / fullInner.z:0.0} m³)", enabled = can, note = note,
                    run = p => { if (SetTidyTank(true)) GameUI.Toast($"Tidy tank strapped in ({tidyTank.liters:0}/{tidyTank.capacity:0} L): park by a machine and look at it to refuel it"); } });
        }

        protected override void DriveUpdate(float dt)
        {
            if (vehicleCamera != null) vehicleCamera.AllowMouse = true;

            float tiltIn = Controls.Axis(GameAction.BoxUp, GameAction.BoxDown);
            if (!HasFuel) tiltIn = Mathf.Min(tiltIn, 0f); // box can always come down (gravity)
            if (GooseneckFitted)
            {
                if (Controls.Pressed(GameAction.BoxUp)) GameUI.Toast("Dump box locked: the gooseneck ball is fitted (take it out from the truck's menu)");
                tiltIn = Mathf.Min(tiltIn, 0f);
            }
            SetTilt(tilt + tiltIn * tiltSpeed * dt);
            if (Controls.Pressed(GameAction.Tailgate))
            {
                gateSetting = (gateSetting + 1) % GateSettings.Length;
                GameUI.Toast($"Tailgate: {GateNames[gateSetting]}");
            }
            DumpStep(dt);
            if (transferCase && Controls.Pressed(GameAction.TransferCase))
            {
                driveMode = (DriveMode)(((int)driveMode + 1) % 3);
                GameUI.Toast(driveMode == DriveMode.TwoHigh ? "2WD: rear wheels only (fastest, least grip)"
                    : driveMode == DriveMode.FourHigh ? "4-HIGH: all four wheels"
                    : $"4-LOW: all four wheels, {lowRange:0.0}x the pull, top speed {lowRangeTopKmh:0} km/h (hills, mud, heavy trailers)");
            }
            if (Controls.Pressed(GameAction.DiffLock))
            {
                rearLocked = !rearLocked;
                GameUI.Toast(rearLocked ? "Rear diff LOCKED: both rear wheels pull together" : "Rear diff open");
            }

            float throttle = Controls.Axis(GameAction.DriveForward, GameAction.DriveBack);
            cab.wheel = Controls.Axis(GameAction.SteerRight, GameAction.SteerLeft);
            cab.pedalR = Mathf.Max(0f, throttle);
            cab.pedalL = Mathf.Max(0f, -throttle);
            cab.right = new Vector2(0f, -tiltIn); // dump box lever
            workAmount = Mathf.Abs(throttle) + Mathf.Abs(tiltIn) * 0.5f;
            hydraulicAmount = Mathf.Abs(tiltIn) * (tilt > 0.01f && tilt < maxTilt - 0.01f ? 1f : 0.3f);
            ApplyDrive(HasFuel ? throttle : 0f, Controls.Axis(GameAction.SteerRight, GameAction.SteerLeft), Controls.Held(GameAction.Handbrake));
        }

        // nobody in the cab: the box stays where it was left and keeps emptying
        protected override void IdleUpdate(float dt) { DumpStep(dt); ApplyDrive(testThrottle, testSteer, Mathf.Abs(testThrottle) < 0.01f && !testCoast); }
        float testThrottle, testSteer;
        /// <summary>Tests: steer with nobody in the cab (-1 left … 1 right).</summary>
        public void TestSteer(float steer) => testSteer = steer;
        /// <summary>Automated tests: drive with nobody in the cab.</summary>
        public void TestDrive(float throttle) { testThrottle = throttle; testCoast = false; }
        /// <summary>Tests: off the gas with nobody in it, without the parked handbrake (like a driver coasting).</summary>
        public void TestCoast() { testThrottle = 0f; testCoast = true; }
        bool testCoast;

        /// <summary>
        /// Tipping: once the box is steep enough the load slides back against the tailgate, which swings open as far
        /// as its setting lets it. The flow is set by the box angle and how far the gate is open, and it leaves along
        /// the whole width of the gate, so a cracked gate while driving lays down an even strip (gravel on a road).
        /// </summary>
        void DumpStep(float dt)
        {
            float gate = GateSettings[Mathf.Clamp(gateSetting, 0, GateSettings.Length - 1)];
            bool flowing = tilt > pourStartTilt && !load.IsEmpty && gate > 0f;
            // the gate hangs from its top hinge: the load pushes it open when tipped, it swings shut as the box comes down
            float want = tilt > pourStartTilt * 0.6f ? gate * tailgateMaxSwing * Mathf.Clamp01((tilt - pourStartTilt * 0.6f) / 12f) : 0f;
            gateSwing = Mathf.MoveTowards(gateSwing, want, 90f * dt);
            if (tailgate != null) tailgate.localRotation = Quaternion.Euler(gateSwing, 0f, 0f); // swings out the back
            if (!flowing || tailgatePour == null) return;
            float f = Mathf.InverseLerp(pourStartTilt, maxTilt, tilt);
            // a fully unlatched gate lets the whole load rush out in a few seconds, like a real dump truck
            float rate = Mathf.Max(pourRate, capacity / Mathf.Max(0.5f, fullDumpSeconds) / 1.25f);
            var pour = load.Take(rate * (0.25f + f) * Mathf.Pow(gate, 1.2f) * dt);
            if (pour.IsEmpty) return;
            Poured += pour.volume;
            // out along the gate's width (a random spot each frame: spread evenly over time)
            Vector3 across = (bedPivot != null ? bedPivot.right : transform.right) * (Random.Range(-1f, 1f) * bedInnerSize.x * 0.45f);
            MaterialTransfer.Pour(tailgatePour.position + across, pour, transform, clumps != null ? clumps.ClumpRadius : 0f);
            UpdateContents();
        }

        float lastThrottle; bool lastBraking, lastHandbrake;
        protected override bool DriveInput => Mathf.Abs(lastThrottle) > 0.05f;
        public override int BrakePedal => lastBraking || lastHandbrake ? 1 : 0;

        /// <summary>
        /// The firmer front end (on for every truck; off only for the tests' comparison): the steering winds on
        /// over a moment instead of snapping to full lock, keeps more of its lock at speed, and the front tyres bite
        /// harder the faster it goes. Pushed too hard it still slides, it just turns in first.
        /// </summary>
        public static bool FirmSteering = true;
        float steerNow;
        float[] sideGrip;

        protected void ApplyDrive(float throttle, float steer, bool handbrake)
        {
            lastThrottle = throttle;
            float speed = Vector3.Dot(rb.linearVelocity, transform.forward);
            float kmh = Mathf.Abs(speed) * 3.6f;
            float fast = Mathf.Clamp01(kmh / Mathf.Max(1f, topSpeedKmh));
            if (FirmSteering)
            {
                // (quick at a crawl, steadier at speed: a full swing of the wheel takes a moment on the road)
                float rate = Mathf.Lerp(4f, 1.8f, fast);
                steerNow = Mathf.MoveTowards(steerNow, steer, rate * Time.deltaTime * (Mathf.Abs(steer) < Mathf.Abs(steerNow) ? 1.6f : 1f));
                steer = steerNow;
            }
            else steerNow = steer;
            float steerAngle = steer * maxSteer * Mathf.Lerp(1f, FirmSteering ? 0.42f : 0.35f, fast);
            // the front tyres' side grip: more of it as the speed comes up (it's what turns a truck in at speed)
            if (sideGrip == null || sideGrip.Length != wheels.Length)
            {
                sideGrip = new float[wheels.Length];
                for (int i = 0; i < wheels.Length; i++) sideGrip[i] = wheels[i] != null ? wheels[i].sidewaysFriction.stiffness : 1f;
            }
            // (the rears a little too, or the bitier front swings the tail round at speed)
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null) continue;
                var sf = wheels[i].sidewaysFriction;
                float g = Mathf.Clamp01(kmh / 60f);
                float want = sideGrip[i] * (FirmSteering ? (i < 2 ? Mathf.Lerp(1.1f, 1.35f, g) : Mathf.Lerp(1f, 1.3f, g)) : 1f);
                if (Mathf.Abs(sf.stiffness - want) > 0.01f) { sf.stiffness = want; wheels[i].sidewaysFriction = sf; }
            }
            // S while rolling forward brakes. W while rolling back just drives forward (the engine fights the roll):
            // on a hill that's what pulls it up again instead of locking the wheels and sliding down
            // (rolling back faster than a walk with W held, it's lost the hill: brake rather than run away backwards)
            bool braking = (throttle < -0.1f && speed > 1.5f) || (throttle > 0.1f && speed < -3f);
            lastBraking = braking; lastHandbrake = handbrake;
            if (Mathf.Abs(throttle) > 0.01f && rb.IsSleeping()) rb.WakeUp(); // wheel torque alone doesn't wake a parked truck

            // engine torque to the axles: 2WD sends it all to the rear, 4-high splits it, 4-low multiplies it
            DriveMode mode = transferCase ? driveMode : DriveMode.TwoHigh;
            bool low = mode == DriveMode.FourLow;
            float top = low ? lowRangeTopKmh : topSpeedKmh;
            float total = (!braking && kmh < top) ? throttle * motorTorque * 4f * (low ? lowRange : 1f) : 0f;
            total *= Mathf.Lerp(lowGearBoost, 1f, kmh / Mathf.Max(1f, top * 0.35f)); // low gears at a crawl
            float front = mode == DriveMode.TwoHigh ? 0f : total * 0.5f, rear = total - front;
            AxleTorque(0, 1, front, false);      // the front diff is always open
            // the rear drive is shared over every rear axle (the 25 t hauler's tandem gets half each)
            if (axleOut.Length != wheels.Length) axleOut = new float[wheels.Length];
            int rearAxles = Mathf.Max(1, wheels.Length / 2 - 1);
            for (int a = 1; a <= rearAxles; a++) AxleTorque(a * 2, a * 2 + 1, rear / rearAxles, rearLocked);

            if (articulation != null) articulation.SetAngle(steerAngle);
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (w == null) continue;
                // (an articulated hauler's front wheels ride on the front frame: they point the way it's swung right now)
                if (i < 2) w.steerAngle = articulation != null && articulation.CarriesWheels ? articulation.Angle : steerAngle;
                // (exactly zero motor torque makes a WheelCollider grab like a brake and lock up: a hair of torque lets it roll)
                w.motorTorque = Mathf.Abs(axleOut[i]) < 1e-3f ? 1e-4f : axleOut[i];
                float b = 0f;
                if (braking) b = brakeTorque;
                else if (Mathf.Abs(throttle) < 0.05f) b = kmh < 1.2f ? brakeTorque : 70f; // stopped: hold it on the brakes (no creeping down hills); rolling: a light engine drag, it coasts
                // anti-lock: a locked wheel has no side grip and lets the tail swing round; ease off as it starts to skid
                if (braking && !handbrake && w.GetGroundHit(out WheelHit wh) && Mathf.Abs(wh.forwardSlip) > 0.4f) b *= 0.35f;
                if (handbrake) b = brakeTorque * 1.5f;
                w.brakeTorque = b;

                if (wheelVisuals.Length > i && wheelVisuals[i] != null && !RidingTrailer) // on a trailer deck the tyres hold still
                {
                    w.GetWorldPose(out Vector3 p, out Quaternion q);
                    // an articulated hauler's front wheels turn with the front frame, not on steering knuckles
                    if (articulation != null && i < 2) Articulate(ref p, ref q, transform.rotation * Quaternion.Euler(0f, w.steerAngle, 0f));
                    wheelVisuals[i].SetPositionAndRotation(p, q * Quaternion.Euler(0f, 0f, 90f));
                }
            }
        }

        float[] axleOut = new float[4];

        /// <summary>A front wheel on the articulated front frame: swung about the hitch by the frame's angle, facing the frame's way.</summary>
        void Articulate(ref Vector3 p, ref Quaternion q, Quaternion steered)
        {
            if (articulation.front == null) return;
            float a = articulation.Angle;
            Vector3 h = articulation.front.position;
            // (wheels carried on the front frame are already where it's swung them)
            if (!articulation.CarriesWheels) p = h + Quaternion.AngleAxis(a, transform.up) * (p - h);
            Quaternion spin = Quaternion.Inverse(steered) * q; // (just the roll)
            q = transform.rotation * Quaternion.Euler(0f, a, 0f) * spin;
        }

        /// <summary>
        /// Split an axle's torque between its two wheels. Open diff: equal torque, but no more than the wheel with
        /// the least grip can take, so a wheel in the air or spinning in mud robs the other. Locked: the axle turns
        /// as one, so a wheel with grip gets the drive (all of it if the other is in the air).
        /// </summary>
        void AxleTorque(int a, int b, float t, bool locked)
        {
            axleOut[a] = axleOut[b] = 0f;
            if (a >= wheels.Length || b >= wheels.Length || wheels[a] == null || wheels[b] == null) { if (a < wheels.Length && wheels[a] != null) axleOut[a] = t; return; }
            bool ga = wheels[a].GetGroundHit(out WheelHit ha), gb = wheels[b].GetGroundHit(out WheelHit hb);
            float half = t * 0.5f;
            if (locked)
            {
                if (ga && !gb) axleOut[a] = t;
                else if (gb && !ga) axleOut[b] = t;
                else axleOut[a] = axleOut[b] = half;
                return;
            }
            float k = 1f;
            if (ga != gb) k = 0.12f; // one wheel hanging: it just spins
            else if (ga) k = Mathf.Clamp(1f - Mathf.Max(0f, Mathf.Abs(ha.forwardSlip - hb.forwardSlip) - 0.2f) * 1.5f, 0.3f, 1f);
            axleOut[a] = axleOut[b] = half * k;
        }

        public Transform bedMound;           // heaped dirt on top of the flat layer

        void UpdateContents()
        {
            if (bedContents == null) return;
            float f = Mathf.Clamp01(load.volume / capacity);
            bedContents.gameObject.SetActive(f > 0.002f);
            // sqrt so the first few bucketfuls are clearly visible
            float layer = Mathf.Lerp(0.04f, bedInnerSize.y * 0.8f, Mathf.Sqrt(f));
            Vector3 s = bedContents.localScale; s.y = layer; bedContents.localScale = s;
            Vector3 p = bedContents.localPosition; p.y = layer * 0.5f + 0.05f; bedContents.localPosition = p;
            if (bedMound != null)
            {
                bedMound.gameObject.SetActive(f > 0.02f);
                float m = Mathf.Lerp(0.1f, 1f, f);
                float spread = Mathf.Lerp(0.5f, 1f, f);
                bedMound.localScale = new Vector3(bedInnerSize.x * 0.95f * spread, bedInnerSize.y * 0.7f * m, bedInnerSize.z * 0.95f * spread);
                bedMound.localPosition = new Vector3(0f, layer + 0.05f, bedContents.localPosition.z);
            }
        }

        protected override System.Collections.Generic.IEnumerable<Vector3> ContactPoints()
        {
            foreach (var w in wheels)
                if (w != null) yield return w.transform.position - transform.up * (w.radius + w.suspensionDistance * 0.5f);
        }

        public override float HudBar => load.volume / capacity;
        public override string HudBarLabel => $"Dump box {load.volume:0.00} / {capacity:0.0} m³";

        public override string HudBody
        {
            get
            {
                float kmh = rb != null ? rb.linearVelocity.magnitude * 3.6f : 0f;
                return $"Fuel {fuel:0.0} / {fuelCapacity:0} L    Speed {kmh:0} km/h\n" +
                       $"Dump box {load.volume:0.00} / {capacity:0.0} m³   ({load.goldGrams:0.0} g gold, {load.Grade:0.0} g/m³)\n" +
                       $"Load: {load.Describe()}\n" +
                       $"Box tilt {tilt:0}°   Tailgate {GateNames[Mathf.Clamp(gateSetting, 0, GateNames.Length - 1)]}\n" +
                       $"Drive {(transferCase ? DriveNames[(int)driveMode] : "rear")}   Rear diff {(rearLocked ? "LOCKED" : "open")}" +
                       (GooseneckFitted ? "   Gooseneck ball fitted (box locked)" : "");
            }
        }

        public override string HudControls =>
            $"{N(GameAction.DriveForward)}/{N(GameAction.DriveBack)} throttle / brake-reverse · {N(GameAction.SteerLeft)}/{N(GameAction.SteerRight)} steer · {N(GameAction.Handbrake)} handbrake\n" +
            $"{N(GameAction.BoxUp)} raise dump box · {N(GameAction.BoxDown)} lower dump box (pours above {pourStartTilt:0}°) · {N(GameAction.Tailgate)} tailgate opening (crack it and drive to spread gravel)\n" +
            (transferCase ? $"{N(GameAction.TransferCase)} 2WD / 4-high / 4-low · " : "") + $"{N(GameAction.DiffLock)} rear diff lock\n" +
            $"Mouse look · {N(GameAction.CabView)} cab view · {N(GameAction.Interact)} get out";

        static string N(GameAction a) => Controls.N(a);
    }
}
