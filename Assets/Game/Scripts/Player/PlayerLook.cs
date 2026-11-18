using UnityEngine;

namespace FPSBase.Player
{
    /// <summary>
    /// CS-style mouse look: degrees = counts * sensitivity * 0.022 (m_yaw / m_pitch).
    /// Also handles view height for crouching, slide tilt and FOV kick.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PlayerLook : MonoBehaviour
    {
        [Header("References")]
        public Transform cameraRoot;
        public Camera viewCamera;

        [Header("Mouse (CS:GO units)")]
        public float sensitivity => FPSBase.Core.GameSettings.MouseSensitivity;
        public float yawScale = 0.022f;
        public float pitchScale = 0.022f;
        public bool rawInput = true;

        [Header("View")]
        public float baseFov = 74f;            // vertical FOV ~ 90 horizontal at 16:9
        public float maxFovKick = 10f;
        public float fovKickStartSpeed = 8f;
        public float fovKickFullSpeed = 16f;
        public float slideTilt = 4f;

        PlayerInput input;
        SourceMovement movement;
        float tpBlend;                 // 0 = first person, 1 = third person
        readonly RaycastHit[] camHits = new RaycastHit[8];
        float pitch;
        float tilt;
        float fovVel;

        void Awake()
        {
            input = GetComponent<PlayerInput>();
            movement = GetComponent<SourceMovement>();
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
        }

        void Update()
        {
            Vector2 d = input.Look;
            bool blocked = FPSBase.Core.GameUI.Instance != null && FPSBase.Core.GameUI.Instance.BlocksGameplay;
            if (!blocked && FPSBase.Core.Controls.Pressed(FPSBase.Core.GameAction.CabView))
                FPSBase.Core.GameSettings.ThirdPerson = !FPSBase.Core.GameSettings.ThirdPerson;
            float yaw = d.x * sensitivity * yawScale;
            pitch -= d.y * sensitivity * pitchScale * (FPSBase.Core.GameSettings.InvertY ? -1f : 1f);
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.Rotate(0f, yaw, 0f, Space.World);

            // Tilt into slides
            float targetTilt = 0f;
            if (movement.State == MoveState.Slide) targetTilt = slideTilt;
            tilt = Mathf.Lerp(tilt, targetTilt, 1f - Mathf.Exp(-10f * Time.deltaTime));

            cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, tilt);
            cameraRoot.localPosition = new Vector3(0f, movement.EyeHeight, 0f);

            ThirdPersonCamera();

            if (viewCamera != null)
            {
                float t = Mathf.InverseLerp(fovKickStartSpeed, fovKickFullSpeed, movement.HorizontalSpeed);
                float target = FPSBase.Core.GameSettings.Fov + maxFovKick * t;
                viewCamera.fieldOfView = Mathf.SmoothDamp(viewCamera.fieldOfView, target, ref fovVel, 0.15f);
            }
        }

        /// <summary>Third person: the camera sits behind and over the right shoulder, pulled in if a wall is in the way.</summary>
        void ThirdPersonCamera()
        {
            if (viewCamera == null || viewCamera.transform.parent != cameraRoot) return;
            tpBlend = Mathf.MoveTowards(tpBlend, FPSBase.Core.GameSettings.ThirdPerson ? 1f : 0f, Time.deltaTime * 5f);
            if (tpBlend <= 0f) { viewCamera.transform.localPosition = Vector3.zero; return; }
            float dist = FPSBase.Core.GameSettings.ThirdPersonDistance;
            Vector3 local = new Vector3(0.45f, 0.25f, -dist) * Mathf.SmoothStep(0f, 1f, tpBlend);
            Vector3 from = cameraRoot.position;
            Vector3 to = cameraRoot.TransformPoint(local);
            Vector3 dir = to - from;
            float len = dir.magnitude;
            if (len > 0.01f)
            {
                int n = Physics.SphereCastNonAlloc(from, 0.2f, dir / len, camHits, len, ~0, QueryTriggerInteraction.Ignore);
                float best = len;
                for (int i = 0; i < n; i++)
                {
                    var h = camHits[i];
                    if (h.collider.transform.IsChildOf(transform) || h.distance <= 0f) continue;
                    best = Mathf.Min(best, h.distance);
                }
                local *= best / len;
            }
            viewCamera.transform.localPosition = local;
        }
    }
}
