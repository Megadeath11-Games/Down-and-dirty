using FPSBase.City;
using FPSBase.Core;
using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// Concrete mixer truck: a dump truck's chassis with a turning drum instead of a box. Fill it under the batch
    /// plant's chute, drive it to the job with the drum slowly turning, swing the chute where you want it and hold
    /// the pour key: the drum turns the other way and the concrete runs out down the chute. Concrete stays workable
    /// for a while after it's batched; leave it in the drum too long and it goes off (wash it out to empty it).
    /// </summary>
    public class MixerTruck : DumpPickup
    {
        [Header("Mixer drum")]
        [Tooltip("The drum: turns about its own z axis.")]
        public Transform drum;
        [Tooltip("The charging hopper over the drum's mouth: park it under the batch plant's chute.")]
        public Transform hopper;
        [Tooltip("The chute: swings about its own y (left / right), tips about the pitch node's x (up / down).")]
        public Transform chuteYaw, chutePitch, chuteEnd;
        public float drumCapacity = 8f;
        [Tooltip("m³ a second down the chute with the drum turning out.")]
        public float pourRateM3 = 0.3f;
        [Tooltip("How long concrete stays workable after it's batched (seconds).")]
        public float workableSeconds = 1200f;
        public float chuteYawRange = 100f, chutePitchMin = 6f, chutePitchMax = 42f, chuteSpeed = 45f;
        public Material concreteMaterial;
        [Tooltip("Weight of a cubic metre of wet concrete (kg).")]
        public float concreteKg = 2400f;

        public float Concrete { get; private set; }
        /// <summary>Seconds since the concrete in the drum was batched (a blend when topped up).</summary>
        public float Age { get; private set; }
        public bool GoneOff => Concrete > 0.001f && Age > workableSeconds;
        public float Room => Mathf.Max(0f, drumCapacity - Concrete);
        public float WorkableLeft => Mathf.Max(0f, workableSeconds - Age);
        public bool PouringNow { get; private set; }
        /// <summary>Concrete poured down the chute so far (m³).</summary>
        public float PouredConcrete { get; private set; }
        public float ChuteYawDeg => yawDeg;
        public float ChutePitchDeg => pitchDeg;
        public float DrumRpm => rpm;
        /// <summary>Set by the batch plant while it's charging the drum (the drum turns fast to take it in).</summary>
        [System.NonSerialized] public float loadingUntil;

        float yawDeg, pitchDeg = 22f, rpm, drumAngle, emptyMass = -1f;
        Rigidbody body;
        Transform stream;
        bool testPour; float testYaw, testPitch;

        void Start()
        {
            body = GetComponent<Rigidbody>();
            if (body != null && emptyMass < 0f) emptyMass = body.mass;
            capacity = drumCapacity; // (the HUD and the yard read it: the drum's what it carries)
            if (concreteMaterial != null && chuteEnd != null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(go.GetComponent<Collider>());
                go.name = "Concrete stream";
                go.GetComponent<MeshRenderer>().sharedMaterial = concreteMaterial;
                go.SetActive(false);
                stream = go.transform;
            }
            ApplyChute();
            SetMass();
        }

        void OnDestroy() { if (stream != null) Destroy(stream.gameObject); }

        /// <summary>Concrete in from the plant. Fresh concrete on top of older blends its age.</summary>
        public float AddConcrete(float m3)
        {
            if (GoneOff) return 0f;
            float give = Mathf.Min(m3, Room);
            if (give <= 0f) return 0f;
            Age = Concrete > 0.001f ? Age * Concrete / (Concrete + give) : 0f;
            Concrete += give;
            SetMass();
            return give;
        }

        /// <summary>Empty the drum (wash it out). Returns what was in it.</summary>
        public float WashOut()
        {
            float was = Concrete;
            Concrete = 0f; Age = 0f;
            SetMass();
            return was;
        }

        /// <summary>Tests and saves: put this much concrete in, this many seconds old.</summary>
        public void SetConcrete(float m3, float age)
        {
            Concrete = Mathf.Clamp(m3, 0f, drumCapacity);
            Age = Mathf.Max(0f, age);
            SetMass();
        }

        void SetMass()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (body == null) return;
            if (emptyMass < 0f) emptyMass = body.mass;
            body.mass = emptyMass + Concrete * concreteKg;
        }

        // ------------------------------------------------------------------ driving

        protected override void DriveUpdate(float dt)
        {
            if (vehicleCamera != null) vehicleCamera.AllowMouse = true;
            float yawIn = Controls.Axis(GameAction.ChuteRight, GameAction.ChuteLeft);
            float pitchIn = Controls.Axis(GameAction.ChuteDown, GameAction.ChuteUp);
            yawDeg = Mathf.Clamp(yawDeg + yawIn * chuteSpeed * dt, -chuteYawRange, chuteYawRange);
            pitchDeg = Mathf.Clamp(pitchDeg + pitchIn * chuteSpeed * 0.6f * dt, chutePitchMin, chutePitchMax);
            bool pour = Controls.Held(GameAction.ConcretePour);
            if (Controls.Pressed(GameAction.ConcretePour))
            {
                if (Concrete <= 0.001f) GameUI.Toast("The drum's empty: fill it under the chute at the concrete plant");
                else if (GoneOff) GameUI.Toast("The concrete's gone off in the drum: it won't come out. Wash the drum out (get out, look at the truck)");
            }
            if (Controls.Pressed(GameAction.ConcreteLoad))
            {
                var plant = ConcretePlant.Under(this);
                if (plant != null) plant.OpenFor(this);
                else GameUI.Toast(ConcretePlant.Instance != null
                    ? "Not under the concrete plant's chute: back the hopper (on top at the back) in under it"
                    : "No concrete plant here");
            }
            if (Controls.Pressed(GameAction.DiffLock))
            {
                rearLocked = !rearLocked;
                GameUI.Toast(rearLocked ? "Rear diff LOCKED: both rear wheels pull together" : "Rear diff open");
            }
            MixStep(dt, pour);

            float throttle = Controls.Axis(GameAction.DriveForward, GameAction.DriveBack);
            cab.wheel = Controls.Axis(GameAction.SteerRight, GameAction.SteerLeft);
            cab.pedalR = Mathf.Max(0f, throttle);
            cab.pedalL = Mathf.Max(0f, -throttle);
            cab.right = new Vector2(yawIn, -pitchIn);
            workAmount = Mathf.Abs(throttle) + Mathf.Abs(rpm) / 30f;
            hydraulicAmount = Mathf.Max(Mathf.Abs(yawIn), Mathf.Abs(pitchIn)) * 0.6f + (PouringNow ? 0.4f : 0f);
            ApplyDrive(HasFuel ? throttle : 0f, Controls.Axis(GameAction.SteerRight, GameAction.SteerLeft), Controls.Held(GameAction.Handbrake));
        }

        protected override void IdleUpdate(float dt)
        {
            base.IdleUpdate(dt);
            yawDeg = Mathf.MoveTowards(yawDeg, Mathf.Clamp(testYaw, -chuteYawRange, chuteYawRange), chuteSpeed * dt * (testAim ? 1f : 0f));
            pitchDeg = Mathf.MoveTowards(pitchDeg, Mathf.Clamp(testPitch, chutePitchMin, chutePitchMax), chuteSpeed * dt * (testAim ? 1f : 0f));
            MixStep(dt, testPour);
        }
        bool testAim;

        /// <summary>Tests: hold the pour key with nobody in the cab.</summary>
        public void TestPour(bool on) => testPour = on;
        /// <summary>Tests: swing the chute with nobody in the cab.</summary>
        public void TestChute(float yaw, float pitch) { testYaw = yaw; testPitch = pitch; testAim = true; }

        /// <summary>
        /// The drum: turning fast while the plant charges it, slowly to keep the load mixed on the road, backwards
        /// to pour it out. The chute follows its angles; poured concrete runs down it to the ground (or a form).
        /// </summary>
        void MixStep(float dt, bool pour)
        {
            if (Concrete > 0.001f) Age += dt;
            bool canPour = pour && Concrete > 0.001f && !GoneOff && HasFuel;
            float want = !HasFuel ? 0f
                : canPour ? -12f
                : Time.time < loadingUntil ? 14f
                : Concrete > 0.001f && !GoneOff ? 2.5f : 0f;
            rpm = Mathf.MoveTowards(rpm, want, 20f * dt);
            drumAngle = Mathf.Repeat(drumAngle + rpm * 6f * dt, 360f);
            if (drum != null) drum.localRotation = Quaternion.Euler(0f, 0f, drumAngle);
            ApplyChute();

            // concrete only comes out once the drum is turning out at speed
            PouringNow = canPour && rpm < -6f;
            float poured = 0f;
            if (PouringNow && chuteEnd != null)
            {
                poured = Mathf.Min(Concrete, pourRateM3 * dt * Mathf.InverseLerp(-6f, -12f, rpm) * Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(chutePitchMin, chutePitchMax, pitchDeg)));
                if (poured > 0f)
                {
                    Concrete -= poured;
                    PouredConcrete += poured;
                    SetMass();
                    ConcreteFlow.Pour(chuteEnd.position, poured, concreteMaterial, transform);
                }
            }
            DrawStream(PouringNow);
        }

        void ApplyChute()
        {
            if (chuteYaw != null) chuteYaw.localRotation = Quaternion.Euler(0f, 180f - yawDeg, 0f); // (+ swings it out to the right)
            if (chutePitch != null) chutePitch.localRotation = Quaternion.Euler(pitchDeg, 0f, 0f);
        }

        void DrawStream(bool on)
        {
            if (stream == null) return;
            if (!on || chuteEnd == null) { if (stream.gameObject.activeSelf) stream.gameObject.SetActive(false); return; }
            Vector3 a = chuteEnd.position;
            float drop = ConcreteFlow.GroundBelow(a, transform, out Vector3 hit) ? Mathf.Max(0.05f, a.y - hit.y) : 1.5f;
            stream.gameObject.SetActive(true);
            stream.SetPositionAndRotation(a + Vector3.down * drop * 0.5f, Quaternion.identity);
            float w = 0.16f + Mathf.PerlinNoise(Time.time * 6f, 0.3f) * 0.05f;
            stream.localScale = new Vector3(w, drop * 0.5f, w);
        }

        // ------------------------------------------------------------------ menu, multiplayer, HUD

        protected override void ExtraOptions(System.Collections.Generic.List<InteractOption> list)
        {
            if (Concrete > 0.001f)
                list.Add(new InteractOption
                {
                    label = GoneOff ? $"Wash out the drum   ({Concrete:0.0} m³ gone off: it's lost)" : $"Wash out the drum   (throws away {Concrete:0.0} m³ of concrete)",
                    enabled = !Occupied && !NetRemoteDriven, note = "someone's in the truck",
                    run = p => { float v = WashOut(); GameUI.Toast($"Drum washed out: {v:0.0} m³ of concrete gone"); },
                });
        }

        float netConcrete = float.NaN, netYaw, netPitch, netRpm; bool netPour;
        public override void NetWrite(System.IO.BinaryWriter w)
        {
            base.NetWrite(w);
            w.Write(Concrete); w.Write(Age); w.Write(yawDeg); w.Write(pitchDeg); w.Write(rpm); w.Write(PouringNow);
        }
        public override void NetRead(System.IO.BinaryReader r)
        {
            base.NetRead(r);
            float c = r.ReadSingle(); Age = r.ReadSingle();
            netYaw = r.ReadSingle(); netPitch = r.ReadSingle(); netRpm = r.ReadSingle(); netPour = r.ReadBoolean();
            // their drum poured: lay the same concrete down here (puddles are each game's own)
            if (!float.IsNaN(netConcrete) && netPour && netConcrete - c > 1e-4f && chuteEnd != null)
                ConcreteFlow.Pour(chuteEnd.position, netConcrete - c, concreteMaterial, transform);
            netConcrete = c; Concrete = c;
            SetMass();
        }
        protected override void NetShow(float dt)
        {
            base.NetShow(dt);
            if (float.IsNaN(netConcrete)) return;
            yawDeg = NetIO.Ease(yawDeg, netYaw, dt); pitchDeg = NetIO.Ease(pitchDeg, netPitch, dt);
            rpm = netRpm;
            drumAngle = Mathf.Repeat(drumAngle + rpm * 6f * dt, 360f);
            if (drum != null) drum.localRotation = Quaternion.Euler(0f, 0f, drumAngle);
            ApplyChute();
            PouringNow = netPour;
            DrawStream(netPour);
        }

        public override float HudBar => Concrete / Mathf.Max(0.1f, drumCapacity);
        public override string HudBarLabel => $"Drum {Concrete:0.0} / {drumCapacity:0} m³ of concrete";

        public override string HudBody
        {
            get
            {
                float kmh = body != null ? body.linearVelocity.magnitude * 3.6f : 0f;
                string state = Concrete <= 0.001f ? "empty: fill it at the concrete plant"
                    : GoneOff ? "<color=#e06050>GONE OFF: wash the drum out</color>"
                    : $"workable for {Mathf.FloorToInt(WorkableLeft / 60f)}:{Mathf.FloorToInt(WorkableLeft % 60f):00} more";
                string drumDo = PouringNow ? "turning out (pouring)" : rpm > 8f ? "charging" : rpm > 0.5f ? "mixing" : "stopped";
                var plant = ConcretePlant.Under(this);
                return $"Fuel {fuel:0.0} / {fuelCapacity:0} L    Speed {kmh:0} km/h\n" +
                       $"Drum {Concrete:0.00} / {drumCapacity:0} m³   ({state})\n" +
                       $"Drum {drumDo}, {Mathf.Abs(rpm):0} rpm   Chute {(yawDeg < -2f ? $"{-yawDeg:0}° left" : yawDeg > 2f ? $"{yawDeg:0}° right" : "straight back")}, {pitchDeg:0}° down\n" +
                       $"Poured {PouredConcrete:0.0} m³   Rear diff {(rearLocked ? "LOCKED" : "open")}" +
                       (plant != null ? $"\n<color=#8fd16a>Under the concrete plant's chute: {Controls.N(GameAction.ConcreteLoad)} to order a load</color>" : "");
            }
        }

        public override string HudControls =>
            $"{N(GameAction.DriveForward)}/{N(GameAction.DriveBack)} throttle / brake-reverse · {N(GameAction.SteerLeft)}/{N(GameAction.SteerRight)} steer · {N(GameAction.Handbrake)} handbrake\n" +
            $"Hold {N(GameAction.ConcretePour)} pour · {N(GameAction.ChuteLeft)}/{N(GameAction.ChuteRight)} swing chute · {N(GameAction.ChuteUp)}/{N(GameAction.ChuteDown)} chute up / down · {N(GameAction.ConcreteLoad)} load at the plant\n" +
            $"{N(GameAction.DiffLock)} rear diff lock · Mouse look · {N(GameAction.CabView)} cab view · {N(GameAction.Interact)} get out";

        static string N(GameAction a) => Controls.N(a);
    }
}
