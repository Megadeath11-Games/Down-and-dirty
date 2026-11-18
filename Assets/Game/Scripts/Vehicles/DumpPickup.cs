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
        public void SetTilt(float deg) { tilt = Mathf.Clamp(deg, 0f, maxTilt); if (bedPivot != null) bedPivot.localRotation = Quaternion.Euler(-tilt, 0f, 0f); }

        public MaterialLoad load;
        float tilt;
        Rigidbody rb;

        public float FreeSpace => Mathf.Max(0f, capacity - load.volume);

        public bool AcceptsAt(Vector3 worldPoint)
        {
            if (tilt > 8f || bedVolume == null) return false;
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
        public override void NetWrite(System.IO.BinaryWriter w) { base.NetWrite(w); w.Write(tilt); w.Write(gateSwing); w.Write(gateSetting); NetIO.Write(w, load); }
        public override void NetRead(System.IO.BinaryReader r)
        {
            base.NetRead(r);
            netTilt = r.ReadSingle(); netGate = r.ReadSingle(); gateSetting = r.ReadInt32(); load = NetIO.ReadLoad(r);
        }
        protected override void NetShow(float dt)
        {
            if (float.IsNaN(netTilt)) return;
            SetTilt(NetIO.Ease(tilt, netTilt, dt));
            gateSwing = NetIO.Ease(gateSwing, netGate, dt);
            if (tailgate != null) tailgate.localRotation = Quaternion.Euler(-gateSwing, 0f, 0f);
            UpdateContents();
        }

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.centerOfMass = new Vector3(0f, 0.35f, 0.2f);
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
            ApplyDrive(HasFuel ? throttle : 0f, Controls.Axis(GameAction.SteerRight, GameAction.SteerLeft), Controls.Held(GameAction.Handbrake));
        }

        // nobody in the cab: the box stays where it was left and keeps emptying
        protected override void IdleUpdate(float dt) { DumpStep(dt); ApplyDrive(testThrottle, 0f, Mathf.Abs(testThrottle) < 0.01f); }
        float testThrottle;
        /// <summary>Automated tests: drive with nobody in the cab.</summary>
        public void TestDrive(float throttle) => testThrottle = throttle;

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
            if (tailgate != null) tailgate.localRotation = Quaternion.Euler(-gateSwing, 0f, 0f);
            if (!flowing || tailgatePour == null) return;
            float f = Mathf.InverseLerp(pourStartTilt, maxTilt, tilt);
            var pour = load.Take(pourRate * (0.25f + f) * Mathf.Pow(gate, 1.2f) * dt);
            if (pour.IsEmpty) return;
            Poured += pour.volume;
            // out along the gate's width (a random spot each frame: spread evenly over time)
            Vector3 across = (bedPivot != null ? bedPivot.right : transform.right) * (Random.Range(-1f, 1f) * bedInnerSize.x * 0.45f);
            MaterialTransfer.Pour(tailgatePour.position + across, pour, transform);
            UpdateContents();
        }

        void ApplyDrive(float throttle, float steer, bool handbrake)
        {
            float speed = Vector3.Dot(rb.linearVelocity, transform.forward);
            float kmh = Mathf.Abs(speed) * 3.6f;
            float steerAngle = steer * maxSteer * Mathf.Lerp(1f, 0.35f, kmh / topSpeedKmh);
            // S while rolling forward brakes. W while rolling back just drives forward (the engine fights the roll):
            // on a hill that's what pulls it up again instead of locking the wheels and sliding down
            // (rolling back faster than a walk with W held, it's lost the hill: brake rather than run away backwards)
            bool braking = (throttle < -0.1f && speed > 1.5f) || (throttle > 0.1f && speed < -3f);
            if (Mathf.Abs(throttle) > 0.01f && rb.IsSleeping()) rb.WakeUp(); // wheel torque alone doesn't wake a parked truck

            // engine torque to the axles: 2WD sends it all to the rear, 4-high splits it, 4-low multiplies it
            DriveMode mode = transferCase ? driveMode : DriveMode.TwoHigh;
            bool low = mode == DriveMode.FourLow;
            float top = low ? lowRangeTopKmh : topSpeedKmh;
            float total = (!braking && kmh < top) ? throttle * motorTorque * 4f * (low ? lowRange : 1f) : 0f;
            float front = mode == DriveMode.TwoHigh ? 0f : total * 0.5f, rear = total - front;
            AxleTorque(0, 1, front, false);      // the front diff is always open
            AxleTorque(2, 3, rear, rearLocked);

            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (w == null) continue;
                if (i < 2) w.steerAngle = steerAngle;
                w.motorTorque = axleOut[i];
                float b = 0f;
                if (braking) b = brakeTorque;
                else if (Mathf.Abs(throttle) < 0.05f) b = kmh < 2.5f ? brakeTorque : 250f; // stopped: hold it on the brakes (no creeping down hills); rolling: engine braking
                if (handbrake) b = brakeTorque * 1.5f;
                w.brakeTorque = b;

                if (wheelVisuals.Length > i && wheelVisuals[i] != null && !RidingTrailer) // on a trailer deck the tyres hold still
                {
                    w.GetWorldPose(out Vector3 p, out Quaternion q);
                    wheelVisuals[i].SetPositionAndRotation(p, q * Quaternion.Euler(0f, 0f, 90f));
                }
            }
        }

        readonly float[] axleOut = new float[4];

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
                       $"Drive {(transferCase ? DriveNames[(int)driveMode] : "rear")}   Rear diff {(rearLocked ? "LOCKED" : "open")}";
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
