using FPSBase.Core;
using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// Semi tractor (25 / 55 / 95 t heavy-haul) with a fifth wheel for an RGN lowboy. Drives like a big truck: the
    /// front axle steers, the drive axles turn with speed, the turning circle widens the faster it goes, and a heavy
    /// load on the deck slows it down.
    /// </summary>
    public class SemiTruck : EarthmoverBase
    {
        public Transform[] steerPivots;
        public Transform[] wheelSpins;
        public float wheelRadius = 0.54f;
        public float maxSteer = 34f;
        public Hitch fifthWheel;
        float steerDeg;
        float[] spins;
        Quaternion[] steerRest, spinRest;
        float baseTurn;

        protected override void Awake()
        {
            base.Awake();
            steerRest = Rest(steerPivots); spinRest = Rest(wheelSpins);
            spins = new float[wheelSpins != null ? wheelSpins.Length : 0];
            baseTurn = turnSpeed;
            // the springs stand on the ground, not on its own lowboy (the gooseneck is right over the drive axles)
            suspension.ignore = c => { var tr = c.GetComponentInParent<Trailer>(); return tr != null && tr.Tractor == this; };
        }

        static Quaternion[] Rest(Transform[] ts)
        {
            var r = new Quaternion[ts != null ? ts.Length : 0];
            for (int i = 0; i < r.Length; i++) r[i] = ts[i] != null ? ts[i].localRotation : Quaternion.identity;
            return r;
        }

        public Trailer Towing
        {
            get
            {
                if (fifthWheel != null && fifthWheel.Attached != null) return fifthWheel.Attached;
                return null;
            }
        }

        protected override void DriveUpdate(float dt)
        {
            ReadDriveKeys();
            cab.left = default;
            cab.wheel = steer;
            cab.pedalL = Mathf.Max(0f, -throttle);
            Steer(steer, dt);
            workAmount = Mathf.Clamp01(Mathf.Abs(throttle) * (0.5f + LoadFraction * 0.5f));
        }

        protected override void IdleUpdate(float dt) => Steer(0f, dt);

        void Steer(float s, float dt)
        {
            steerDeg = Mathf.Abs(s) > 0.01f ? Mathf.MoveTowards(steerDeg, s * maxSteer, 60f * dt) : Mathf.MoveTowards(steerDeg, 0f, 45f * dt);
            // the faster it goes, the wider it turns (a loaded rig even more)
            float v = Mathf.Abs(ForwardSpeed);
            turnSpeed = baseTurn * Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(v / Mathf.Max(1f, driveSpeed))) * Mathf.Lerp(1f, 0.75f, LoadFraction);
            speedFactor = Mathf.Lerp(1f, 0.5f, LoadFraction);
        }

        /// <summary>How heavy the load on the lowboy is, 0..1 of its rating (overloaded counts as 1).</summary>
        public float LoadFraction
        {
            get
            {
                var t = Towing;
                if (t == null || t.payloadTons <= 0f) return 0f;
                return Mathf.Clamp01(t.CargoTons / t.payloadTons);
            }
        }

        protected override void Update()
        {
            base.Update();
            for (int i = 0; steerPivots != null && i < steerPivots.Length; i++)
                if (steerPivots[i] != null) steerPivots[i].localRotation = steerRest[i] * Quaternion.Euler(0f, steerDeg, 0f);
            if (wheelSpins == null) return;
            for (int i = 0; i < wheelSpins.Length; i++)
            {
                if (wheelSpins[i] == null) continue;
                spins[i] += ForwardSpeed * Time.deltaTime / Mathf.Max(0.1f, wheelRadius) * Mathf.Rad2Deg;
                wheelSpins[i].localRotation = spinRest[i] * Quaternion.Euler(spins[i], 0f, 0f);
            }
        }

        public override string HudBody
        {
            get
            {
                var t = Towing;
                string tr = t == null ? "No trailer on the fifth wheel"
                    : !t.NeckOn ? "Gooseneck on the fifth wheel, deck detached (back up to it and attach it)"
                    : $"{t.displayName}: {(t.CargoCount > 0 ? $"{t.CargoTons:0.#} t chained on" : "empty deck")}" + (t.payloadTons > 0f ? $" (rated {t.payloadTons:0} t)" : "");
                return $"Fuel {fuel:0.0} / {fuelCapacity:0} L    Speed {Mathf.Abs(ForwardSpeed) * 3.6f:0} km/h\n{tr}";
            }
        }

        public override string HudControls =>
            $"{N(GameAction.DriveForward)}/{N(GameAction.DriveBack)} throttle / brake-reverse · {N(GameAction.SteerLeft)}/{N(GameAction.SteerRight)} steer\n" +
            "Get out and look at the lowboy (scroll) to detach the gooseneck and drop the deck; drive machines on, park them, then hook the gooseneck back on\n" +
            $"Mouse look · {N(GameAction.CabView)} cab view · {N(GameAction.Interact)} get out";

        static string N(GameAction a) => Controls.N(a);
    }
}
