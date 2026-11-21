using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// A wheel loader's Z-bar: the bellcrank on the lift arms and the push rod down to the bucket follow the bucket
    /// as it tips (a four-bar linkage, solved every frame from the bucket's angle on the arms). The tilt ram's rod
    /// pin is on the top of the bellcrank, so the ram follows too.
    /// Everything is in the arm's own frame: z forward along the arm, y up.
    /// </summary>
    [ExecuteAlways]
    public class ZBarLinkage : MonoBehaviour
    {
        public Transform bucketPivot;    // child of the arm pivot
        public Transform bellcrank;      // pivots at `pivot` on the arm
        public Transform pushrod;        // from the bellcrank's lower pin to the bucket's ear
        public Vector2 tip;              // bucket pin on the arm (z, y)
        public Vector2 pivot;            // bellcrank pin on the arm (z, y)
        public float lowerArm = 0.25f;   // bellcrank pin to its lower (push rod) pin
        public float rodLength = 1f;
        public Vector2 ear;              // push rod pin on the bucket, in the bucket's frame (z, y)

        void LateUpdate() => Solve();

        public void Solve()
        {
            if (bucketPivot == null || bellcrank == null || pushrod == null) return;
            float ex = bucketPivot.localEulerAngles.x; if (ex > 180f) ex -= 360f;
            float b = -ex * Mathf.Deg2Rad; // (+ = the bucket's front tipped up)
            Vector2 pinD = new Vector2(tip.x + ear.x * Mathf.Cos(b) - ear.y * Mathf.Sin(b), tip.y + ear.x * Mathf.Sin(b) + ear.y * Mathf.Cos(b));
            Vector2 j = Circles(pivot, lowerArm, pinD, rodLength);
            float alpha = Mathf.Atan2(j.y - pivot.y, j.x - pivot.x);
            bellcrank.localRotation = Quaternion.Euler(-(alpha + Mathf.PI * 0.5f) * Mathf.Rad2Deg, 0f, 0f);
            pushrod.localPosition = new Vector3(0f, j.y, j.x);
            pushrod.localRotation = Quaternion.Euler(-Mathf.Atan2(pinD.y - j.y, pinD.x - j.x) * Mathf.Rad2Deg, 0f, 0f);
        }

        /// <summary>Where two circles cross (the lower of the two points).</summary>
        static Vector2 Circles(Vector2 p0, float r0, Vector2 p1, float r1)
        {
            Vector2 d = p1 - p0;
            float raw = Mathf.Max(1e-5f, d.magnitude);
            float dist = Mathf.Clamp(raw, Mathf.Abs(r0 - r1) * 1.004f + 1e-4f, (r0 + r1) * 0.996f);
            float a = (r0 * r0 - r1 * r1 + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, r0 * r0 - a * a));
            Vector2 u = d / raw, m = p0 + a * u;
            Vector2 s1 = new Vector2(m.x - h * u.y, m.y + h * u.x), s2 = new Vector2(m.x + h * u.y, m.y - h * u.x);
            return s1.y < s2.y ? s1 : s2;
        }
    }
}
