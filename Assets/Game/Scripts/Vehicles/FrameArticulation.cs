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
        void Start() => Split();

        /// <summary>The hitch pin in the machine's own space.</summary>
        public Vector3 HitchLocal { get; private set; }
        /// <summary>The front wheels' colliders ride on the front frame (they go round with it; their steering angle is the frame's).</summary>
        public bool CarriesWheels { get; private set; }
        bool split;

        /// <summary>
        /// Hand the front frame its share of the physics: the front wheels, and the body collider ahead of the hitch
        /// (cut off where it spans the hitch). Then the hit box and the tyres' tracks swing with the front frame as it
        /// steers, instead of the body staying straight while the model turns. (Once; the machine calls it after it
        /// has set up its own colliders.)
        /// </summary>
        public void Split()
        {
            if (split || front == null) return;
            split = true;
            var root = transform;
            var keep = front.localRotation; front.localRotation = Quaternion.identity;
            HitchLocal = root.InverseTransformPoint(front.position);
            float hz = HitchLocal.z;
            foreach (var wc in GetComponentsInChildren<WheelCollider>(true))
            {
                if (wc.transform.IsChildOf(front)) { CarriesWheels = true; continue; }
                if (root.InverseTransformPoint(wc.transform.position).z <= hz + 0.2f) continue;
                wc.transform.SetParent(front, true);
                CarriesWheels = true;
            }
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.isTrigger || c is WheelCollider || c.transform.IsChildOf(front)) continue;
                if (c.transform != root)
                {
                    // a box of its own ahead of the hitch (the hauler's tractor): it goes with the front frame
                    var go = c.gameObject;
                    if (go.transform.childCount > 0 || go.GetComponents<Component>().Length > 2 || go.transform.parent != root) continue;
                    if (root.InverseTransformPoint(c.bounds.center).z > hz + 0.2f) go.transform.SetParent(front, true);
                    continue;
                }
                // the body box over both frames (the loader's): cut at the hitch, the front half on the front frame
                if (c is not BoxCollider b) continue;
                float z0 = b.center.z - b.size.z * 0.5f, z1 = b.center.z + b.size.z * 0.5f;
                if (z1 < hz + 0.3f || z0 > hz - 0.1f) continue;
                var piece = new GameObject("Front Hull");
                piece.layer = gameObject.layer;
                piece.transform.SetParent(front, false);
                piece.transform.position = root.TransformPoint(new Vector3(b.center.x, b.center.y, (hz + z1) * 0.5f));
                piece.transform.rotation = root.rotation;
                var nb = piece.AddComponent<BoxCollider>();
                nb.sharedMaterial = b.sharedMaterial;
                Vector3 sc = root.lossyScale, fs = piece.transform.lossyScale;
                nb.size = new Vector3(b.size.x * sc.x / fs.x, b.size.y * sc.y / fs.y, (z1 - hz) * sc.z / fs.z);
                b.center = new Vector3(b.center.x, b.center.y, (z0 + hz) * 0.5f);
                b.size = new Vector3(b.size.x, b.size.y, hz - z0);
            }
            front.localRotation = keep;
        }

        /// <summary>A point in the machine's own space, carried round with the front frame if it's ahead of the hitch.</summary>
        public Vector3 MapPoint(Vector3 local)
        {
            if (!split || local.z <= HitchLocal.z || Mathf.Abs(Angle) < 0.01f) return local;
            return HitchLocal + Quaternion.Euler(0f, Angle, 0f) * (local - HitchLocal);
        }

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
