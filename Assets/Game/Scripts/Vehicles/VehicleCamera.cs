using FPSBase.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSBase.Vehicles
{
    /// <summary>Third-person orbit camera that follows a vehicle; V toggles a seat (cab) view.</summary>
    public class VehicleCamera : MonoBehaviour
    {
        public Transform target;        // what to orbit around (e.g. excavator upper house)
        public Transform seat;          // cab view position
        public float distance = 7f;
        public float height = 2.2f;
        public float pitch = 14f;
        public bool followTargetYaw = true;
        public bool AllowMouse { get; set; } = true;

        float yawOffset, pitchOffset;
        bool cabView;
        /// <summary>True while looking out from the seat.</summary>
        public bool CabView => cabView;

        /// <summary>Automated tests: look out from the seat, `down` degrees below the default.</summary>
        public void TestCabView(bool on, float down = 0f, float yaw = 0f) { cabView = on; pitchOffset = down; yawOffset = yaw; }

        void LateUpdate()
        {
            if (target == null) return;
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            bool blocked = GameUI.Instance != null && GameUI.Instance.BlocksGameplay;

            if (!blocked && Controls.Pressed(GameAction.CabView) && seat != null) cabView = !cabView;

            if (!blocked && AllowMouse && mouse != null)
            {
                Vector2 d = mouse.delta.ReadValue() * GameSettings.MouseSensitivity * 0.022f;
                if (GameSettings.InvertY) d.y = -d.y;
                yawOffset += d.x;
                pitchOffset = Mathf.Clamp(pitchOffset - d.y, cabView ? -60f : -10f, cabView ? 60f : 60f);
            }
            if (!AllowMouse) { yawOffset = Mathf.MoveTowards(yawOffset, 0f, 90f * Time.deltaTime); pitchOffset = Mathf.MoveTowards(pitchOffset, 0f, 60f * Time.deltaTime); }

            float baseYaw = followTargetYaw ? target.eulerAngles.y : 0f;
            if (cabView)
            {
                transform.position = seat.position;
                transform.rotation = Quaternion.Euler(10f + pitchOffset, baseYaw + yawOffset, 0f);
                return;
            }

            var rot = Quaternion.Euler(pitch + pitchOffset, baseYaw + yawOffset, 0f);
            Vector3 pivot = target.position + Vector3.up * height;
            Vector3 want = pivot - rot * Vector3.forward * distance;
            // keep the camera out of the ground
            if (Physics.Linecast(pivot, want, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform.root))
                want = hit.point + hit.normal * 0.3f;
            transform.position = want;
            transform.rotation = rot;
        }
    }
}
