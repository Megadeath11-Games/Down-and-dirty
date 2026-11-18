using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSBase.Core
{
    /// <summary>On-foot interaction: look at something within reach and press E (scroll to pick when it has a menu).</summary>
    public class PlayerInteractor : MonoBehaviour
    {
        public Camera viewCamera;
        public float reach = 3.5f;

        public Interactable Current { get; private set; }
        /// <summary>The look-at menu for `Current` (empty = a plain prompt).</summary>
        public List<InteractOption> Options { get; } = new();
        public int Selected { get; private set; }
        /// <summary>A menu is up: the scroll wheel picks options (and doesn't jump).</summary>
        public static bool MenuActive { get; private set; }

        Interactable lastTarget;

        void OnDisable() { MenuActive = false; }

        void Update()
        {
            Current = null;
            Options.Clear();
            MenuActive = false;
            if (GameUI.Instance != null && GameUI.Instance.BlocksGameplay) return;
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();

            // in third person the camera is behind you: start the ray level with your head, not at the camera
            Vector3 fwd = viewCamera.transform.forward, from = viewCamera.transform.position;
            var head = viewCamera.transform.parent;
            if (head != null && head != transform) from += fwd * Mathf.Max(0f, Vector3.Dot(head.position - from, fwd));
            var ray = new Ray(from, fwd);
            if (Physics.Raycast(ray, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Collide))
                Current = hit.collider.GetComponentInParent<Interactable>();

            if (Current != lastTarget) { Selected = 0; lastTarget = Current; }
            if (Current == null) return;
            Current.GetOptions(Options);
            if (Options.Count > 1)
            {
                MenuActive = true;
                var mouse = Mouse.current;
                float wheel = mouse != null ? mouse.scroll.ReadValue().y : 0f;
                if (wheel < -0.01f) Selected = (Selected + 1) % Options.Count;
                else if (wheel > 0.01f) Selected = (Selected + Options.Count - 1) % Options.Count;
            }
            Selected = Options.Count > 0 ? Mathf.Clamp(Selected, 0, Options.Count - 1) : 0;

            if (Controls.Pressed(GameAction.Interact))
            {
                if (Options.Count == 0) Current.Interact(this);
                else
                {
                    var o = Options[Selected];
                    if (!o.enabled) GameUI.Toast(string.IsNullOrEmpty(o.note) ? "Can't do that right now" : o.note);
                    else o.run?.Invoke(this);
                }
            }
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
