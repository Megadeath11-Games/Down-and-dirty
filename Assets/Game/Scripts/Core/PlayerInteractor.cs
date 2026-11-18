using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSBase.Core
{
    /// <summary>On-foot interaction: look at something within reach and press E.</summary>
    public class PlayerInteractor : MonoBehaviour
    {
        public Camera viewCamera;
        public float reach = 3.5f;

        public Interactable Current { get; private set; }

        void Update()
        {
            Current = null;
            if (GameUI.Instance != null && GameUI.Instance.BlocksGameplay) return;
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();

            // in third person the camera is behind you: start the ray level with your head, not at the camera
            Vector3 fwd = viewCamera.transform.forward, from = viewCamera.transform.position;
            var head = viewCamera.transform.parent;
            if (head != null && head != transform) from += fwd * Mathf.Max(0f, Vector3.Dot(head.position - from, fwd));
            var ray = new Ray(from, fwd);
            if (Physics.Raycast(ray, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Collide))
                Current = hit.collider.GetComponentInParent<Interactable>();

            if (Current != null && Controls.Pressed(GameAction.Interact))
                Current.Interact(this);
        }

        /// <summary>Hide the on-foot player (used when getting into a vehicle).</summary>
        public void SetActive(bool active) => gameObject.SetActive(active);

        public void PlaceAt(Vector3 position, float yaw)
        {
            var mover = GetComponent<FPSBase.Player.SourceMovement>();
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (mover != null) mover.Teleport(position); else transform.position = position;
        }
    }
}
