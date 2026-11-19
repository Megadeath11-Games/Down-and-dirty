using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSBase.Core
{
    /// <summary>
    /// A controller drives the mouse pointer on menus, shops and panels (the game's menus are drawn for a mouse):
    /// left stick moves the pointer, A clicks, the right stick scrolls. B / Start go back (see Controls.MenuBack).
    /// Only while the pointer is showing; in play the sticks and buttons are the game's controls.
    /// </summary>
    public class GamepadCursor : MonoBehaviour
    {
        static GamepadCursor inst;
        Vector2 pos; bool havePos;
        float scrollAcc;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst != null || Application.isBatchMode) return;
            var go = new GameObject("Gamepad Cursor");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<GamepadCursor>();
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int data, System.UIntPtr extra);
        static void Click(bool down) => mouse_event(down ? 0x0002u : 0x0004u, 0, 0, 0, System.UIntPtr.Zero);
        static void Wheel(int notches) => mouse_event(0x0800u, 0, 0, notches * 120, System.UIntPtr.Zero);
#else
        static void Click(bool down) { }
        static void Wheel(int notches) { }
#endif

        void Update()
        {
            var gp = Gamepad.current;
            var mouse = Mouse.current;
            if (gp == null || mouse == null) { havePos = false; return; }
            bool pointer = Cursor.visible && Cursor.lockState != CursorLockMode.Locked;
            if (!pointer) { havePos = false; return; }

            if (!havePos) { pos = mouse.position.ReadValue(); havePos = true; }
            Vector2 st = gp.leftStick.ReadValue();
            if (st.sqrMagnitude > 0.0001f)
            {
                // a curve: fine at a nudge, quick at full tilt (~1.1 screen heights a second)
                float m = st.magnitude;
                Vector2 v = st / m * (m * m) * Screen.height * 1.1f;
                pos += v * Time.unscaledDeltaTime;
                pos.x = Mathf.Clamp(pos.x, 0f, Screen.width - 1f);
                pos.y = Mathf.Clamp(pos.y, 0f, Screen.height - 1f);
                mouse.WarpCursorPosition(pos);
            }
            else pos = mouse.position.ReadValue(); // the real mouse may have moved it

            if (gp.buttonSouth.wasPressedThisFrame) Click(true);
            if (gp.buttonSouth.wasReleasedThisFrame) Click(false);

            float sy = gp.rightStick.ReadValue().y;
            if (Mathf.Abs(sy) > 0.2f)
            {
                scrollAcc += sy * 8f * Time.unscaledDeltaTime;
                int n = (int)scrollAcc;
                if (n != 0) { Wheel(n); scrollAcc -= n; }
            }
            else scrollAcc = 0f;
        }
    }
}
