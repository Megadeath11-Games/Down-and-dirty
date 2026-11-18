using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// The bucket's closed four-bar linkage on an excavator stick: the H-link swings on the stick, the dog-bone
    /// links its end to the bucket ear, and the bucket cylinder pushes the joint between them.
    /// Solved as the intersection of two circles in the stick's side plane (local Z = along the stick, Y = up).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class ExcavatorLinkage : MonoBehaviour
    {
        public Transform stick;      // parent of hLink and dogbone
        public Transform bucketEar;  // pin on the bucket
        public Transform hLink;      // pivots on the stick, plates along +Z
        public Transform dogbone;    // plates along +Z from the joint to the ear
        public float hLinkLength, dogboneLength;

        void LateUpdate() => Apply();

        public void Apply()
        {
            if (stick == null || bucketEar == null || hLink == null || dogbone == null) return;
            Vector3 e = stick.InverseTransformPoint(bucketEar.position);
            var h = new Vector2(hLink.localPosition.z, hLink.localPosition.y);
            var d = new Vector2(e.z, e.y);
            Vector2 j = CircleIntersect(h, hLinkLength, d, dogboneLength);
            hLink.localRotation = Quaternion.Euler(-Mathf.Atan2(j.y - h.y, j.x - h.x) * Mathf.Rad2Deg, 0f, 0f);
            dogbone.localPosition = new Vector3(hLink.localPosition.x, j.y, j.x);
            dogbone.localRotation = Quaternion.Euler(-Mathf.Atan2(d.y - j.y, d.x - j.x) * Mathf.Rad2Deg, 0f, 0f);
        }

        /// <summary>Intersection of circle (p0, r0) and (p1, r1), on the left of p0→p1. Clamped when out of reach.</summary>
        public static Vector2 CircleIntersect(Vector2 p0, float r0, Vector2 p1, float r1)
        {
            Vector2 dd = p1 - p0;
            float dist = dd.magnitude;
            float cd = Mathf.Clamp(dist, Mathf.Abs(r0 - r1) * 1.005f + 0.001f, (r0 + r1) * 0.995f);
            float a = (r0 * r0 - r1 * r1 + cd * cd) / (2f * cd);
            float hh = Mathf.Sqrt(Mathf.Max(0f, r0 * r0 - a * a));
            Vector2 u = dist > 1e-6f ? dd / dist : Vector2.right;
            Vector2 m = p0 + a * u;
            return m + hh * new Vector2(-u.y, u.x);
        }
    }
}
