using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// An articulated machine (wheel loader, articulated haul truck): the front frame swings about the centre hitch
    /// as it steers. The machine's physics stays one rigid body; this turns the front frame (and its wheels, arms and
    /// bucket) to the steering angle. With nobody steering it here (a friend drives it) the angle comes from how
    /// it's turning.
    /// </summary>
    public class FrameArticulation : MonoBehaviour
    {
        [Tooltip("The front frame: its origin is on the hitch pin, it turns about it.")]
        public Transform front;
        public float maxAngle = 38f;
        [Tooltip("How fast the frame swings (deg/s).")]
        public float rate = 45f;
        [Tooltip("Front axle to rear axle (m): works the angle out from the turning rate when nobody steers it here.")]
        public float wheelbase = 3f;

        /// <summary>The frame's angle now (deg, + = front swung right).</summary>
        public float Angle { get; private set; }
        float target, inputAt = -10f;
        Rigidbody rb;

        void Awake() => rb = GetComponent<Rigidbody>();

        /// <summary>The steering: -1 (full left) … 1 (full right).</summary>
        public void SetInput(float steer) { target = Mathf.Clamp(steer, -1f, 1f) * maxAngle; inputAt = Time.time; }
        /// <summary>The steering as an angle (deg).</summary>
        public void SetAngle(float deg) { target = Mathf.Clamp(deg, -maxAngle, maxAngle); inputAt = Time.time; }
        /// <summary>Straight away (tests, the editor).</summary>
        public void Snap(float deg) { Angle = target = Mathf.Clamp(deg, -maxAngle, maxAngle); Apply(); }

        void LateUpdate()
        {
            if (front == null) return;
            if (Time.time - inputAt > 0.4f && rb != null)
            {
                // nobody steering it on this machine: the turn it's making says how far it's articulated
                Vector3 v = rb.linearVelocity;
                float speed = Vector3.Dot(v, transform.forward);
                float yawRate = Vector3.Dot(rb.angularVelocity, transform.up);
                target = Mathf.Abs(speed) > 0.4f ? Mathf.Clamp(Mathf.Atan(wheelbase * yawRate / speed) * Mathf.Rad2Deg, -maxAngle, maxAngle) : target;
            }
            Angle = Mathf.MoveTowards(Angle, target, rate * Time.deltaTime);
            Apply();
        }

        void Apply() { if (front != null) front.localRotation = Quaternion.Euler(0f, Angle, 0f); }
    }
}
