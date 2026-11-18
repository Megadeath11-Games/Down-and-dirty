using FPSBase.Core;
using System.Collections.Generic;
using FPSBase.Mining;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// Shared driving for heavy machines that push into dirt (bulldozer, wheel loader):
    /// grippy direct-velocity drive, and they drive *through* loose dirt piles instead of bumping off them
    /// (the blade/bucket takes the dirt instead).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public abstract class EarthmoverBase : VehicleBase
    {
        [Header("Drive")]
        public float driveSpeed = 2.5f;      // m/s forward
        public float reverseSpeed = 2.5f;
        public float turnSpeed = 40f;        // deg/s
        public float acceleration = 2.5f;    // m/s^2
        [Tooltip("Tracked machines can spin in place; wheeled ones turn only while moving.")]
        public bool turnInPlace = true;
        public float groundCheck = 0.6f;
        public Vector3 centerOfMass = new Vector3(0f, 0.45f, 0f);
        [Tooltip("Sprung track/wheel contacts: the body rides over ledges and mounds instead of snagging on them.")]
        public TrackSuspension suspension = new TrackSuspension();

        protected Rigidbody rb;
        protected float throttle, steer;
        /// <summary>Set by subclasses: pushing a big heap slows the machine down (1 = full speed).</summary>
        protected float speedFactor = 1f;
        float speed;
        Collider[] myColliders, tools;
        readonly HashSet<int> ignoredPiles = new();
        float testThrottle;

        /// <summary>Automated tests: drive without a player in the seat.</summary>
        public void TestDrive(float throttleInput) => testThrottle = throttleInput;
        protected override bool DriveInput => Mathf.Abs(throttle) > 0.05f || Mathf.Abs(testThrottle) > 0.05f;
        public TrackSuspension Suspension => suspension;

        /// <summary>Forward speed in m/s (negative = reversing).</summary>
        public float ForwardSpeed => rb != null && !rb.isKinematic ? Vector3.Dot(rb.linearVelocity, transform.forward) : NetSpeed;

        protected virtual void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.centerOfMass = centerOfMass;
            rb.angularDamping = Mathf.Max(rb.angularDamping, 4f);
            suspension.Setup(rb, GetComponent<BoxCollider>(), true);
            myColliders = GetComponentsInChildren<Collider>();
            tools = ToolColliders.Find(transform);
        }

        protected virtual void Start() => ToolColliders.IgnoreGround(tools);

        protected override void OnEnterVehicle() => rb.isKinematic = false;

        protected void ReadDriveKeys()
        {
            throttle = Controls.Axis(GameAction.DriveForward, GameAction.DriveBack);
            steer = Controls.Axis(GameAction.SteerRight, GameAction.SteerLeft);
            if (!HasFuel) throttle = steer = 0f;
            if (vehicleCamera != null) vehicleCamera.AllowMouse = true;
            // drive joystick (left hand): forward / back, left / right
            cab.left = new Vector2(steer, throttle);
            cab.pedalR = Mathf.Max(0f, throttle);
        }

        protected virtual void FixedUpdate()
        {
            IgnorePileCollisions();
            if (!Occupied) { throttle = testThrottle; steer = 0f; }
            if (rb.isKinematic) return;
            suspension.Step();
            bool grounded = suspension.Contacts > 0 ? suspension.Touching > 0
                : Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, groundCheck, ~0, QueryTriggerInteraction.Ignore);
            float target = (throttle >= 0f ? throttle * driveSpeed : throttle * reverseSpeed) * speedFactor;
            speed = Mathf.MoveTowards(speed, target, acceleration * Time.fixedDeltaTime);
            if (!grounded) return;
            // tracks grip: set the velocity along the ground, keep what the springs/gravity do across it
            Vector3 n = suspension.Touching > 0 ? suspension.GroundNormal : Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, n).normalized;
            Vector3 v = rb.linearVelocity;
            float vn = Vector3.Dot(v, n);
            float grip = suspension.Grip;
            rb.linearVelocity = Vector3.Lerp(v, fwd * speed + n * vn, grip);
            float turnFactor = turnInPlace ? 1f : Mathf.Clamp(speed / 1.2f, -1f, 1f);
            if (!turnInPlace && Mathf.Abs(speed) < 0.05f) turnFactor = 0f;
            if (Mathf.Abs(steer) > 0.01f)
                rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, steer * turnSpeed * turnFactor * Time.fixedDeltaTime, 0f));
            rb.angularVelocity = new Vector3(rb.angularVelocity.x, 0f, rb.angularVelocity.z);
        }

        void IgnorePileCollisions()
        {
            foreach (var p in DirtPile.All)
            {
                if (p == null || !ignoredPiles.Add(p.GetInstanceID())) continue;
                var pc = p.GetComponent<Collider>();
                if (pc == null) continue;
                foreach (var c in myColliders) if (c != null && !c.isTrigger) Physics.IgnoreCollision(c, pc, true);
            }
        }

        /// <summary>Height of the solid ground below a point (optionally counting dirt piles), ignoring this machine.</summary>
        protected float GroundBelow(Vector3 p, bool includePiles = false)
        {
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (!includePiles && h.collider.GetComponent<DirtPile>() != null) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return best;
        }

        protected static string Describe(IMaterialReceiver rec) => rec switch
        {
            DumpPickup t => $"{t.displayName}: {t.load.volume:0.00} / {t.capacity:0.0} m³",
            WashPlant w => $"Wash plant hopper: {w.hopper.volume:0.00} / {w.hopperSize.Value:0.#} m³",
            DirtPile p => $"Dirt pile ({p.load.volume:0.0} m³)",
            _ => "ground (makes a pile)",
        };

        protected static float Axis(KeyControl pos, KeyControl neg) => (pos.isPressed ? 1f : 0f) - (neg.isPressed ? 1f : 0f);
    }
}
