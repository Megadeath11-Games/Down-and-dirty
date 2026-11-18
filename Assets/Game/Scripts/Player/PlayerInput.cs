using FPSBase.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSBase.Player
{
    /// <summary>
    /// Reads raw keyboard and mouse state each frame. Kept in one place so rebinding later is easy.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PlayerInput : MonoBehaviour
    {
        public Vector2 Move { get; private set; }        // x = strafe, y = forward
        public Vector2 Look { get; private set; }        // raw mouse counts this frame
        public bool JumpHeld { get; private set; }
        public bool JumpPressed { get; private set; }    // jump key (or a mouse wheel tick, if that option is on)
        public bool CrouchHeld { get; private set; }
        public bool SprintHeld { get; private set; }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            float x = Controls.Axis(GameAction.MoveRight, GameAction.MoveLeft);
            float y = Controls.Axis(GameAction.MoveForward, GameAction.MoveBack);
            Move = new Vector2(x, y);

            Look = Cursor.lockState == CursorLockMode.Locked ? mouse.delta.ReadValue() : Vector2.zero;

            // the CS-style "jump on mouse wheel" bind is an option now (off by default)
            bool wheel = GameSettings.WheelJump && Cursor.lockState == CursorLockMode.Locked && Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f;
            JumpHeld = Controls.Held(GameAction.Jump) || wheel;
            JumpPressed = Controls.Pressed(GameAction.Jump) || wheel;
            CrouchHeld = Controls.Held(GameAction.Crouch) || Controls.Held(GameAction.CrouchAlt);
            SprintHeld = Controls.Held(GameAction.Sprint);
        }
    }
}
