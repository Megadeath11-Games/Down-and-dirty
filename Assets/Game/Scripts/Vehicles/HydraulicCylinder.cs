using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// A hydraulic ram between two pins. The barrel sits on pin A and points at B, the chrome rod sits on pin B and
    /// points back at A, so it looks like it slides in and out as the joints move. Both parts are fixed length.
    /// </summary>
    [DefaultExecutionOrder(100)] // after the machine poses its joints and after the bucket linkage
    public class HydraulicCylinder : MonoBehaviour
    {
        public Transform a, b;           // pins
        public Transform barrel, rod;    // meshes along their local +Z

        void LateUpdate() => Apply();

        public void Apply()
        {
            if (a == null || b == null || barrel == null || rod == null) return;
            Vector3 pa = a.position, pb = b.position, d = pb - pa;
            if (d.sqrMagnitude < 1e-8f) return;
            // the pins are all across the machine, so its sideways axis gives a stable roll
            Vector3 side = transform.parent != null ? transform.parent.right : Vector3.right;
            Vector3 up = Vector3.Cross(d, side);
            if (up.sqrMagnitude < 1e-8f) up = Vector3.up;
            barrel.SetPositionAndRotation(pa, Quaternion.LookRotation(d, up));
            rod.SetPositionAndRotation(pb, Quaternion.LookRotation(-d, up));
        }
    }
}
