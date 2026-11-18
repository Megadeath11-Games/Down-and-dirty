using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>How far each cab control is pushed this frame (-1..1). x = right, y = forward.</summary>
    public struct CabInput
    {
        public Vector2 left, right;          // joysticks
        public float leverL, leverR, aux;    // travel levers, blade / extra lever
        public float pedalL, pedalR;         // 0..1 pressed
        public float wheel;                  // steering wheel, -1 full left .. 1 full right
    }

    /// <summary>
    /// The operator's controls in a cab: joysticks, travel levers, an aux lever, foot pedals, a steering wheel.
    /// Each is a pivot built by the editor builders; the machine script reports its inputs every frame and the
    /// controls swing to match, so the driver sitting in the seat (whose hands and feet follow the grips and
    /// pedals) visibly works them.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public class CabControls : MonoBehaviour
    {
        public Transform leftStick, rightStick, leftLever, rightLever, auxLever, leftPedal, rightPedal, wheel;
        [Tooltip("Where the hands and feet go (moved by the pivots above).")]
        public Transform leftGrip, rightGrip, leftFoot, rightFoot;
        public float stickAngle = 20f, leverAngle = 24f, pedalAngle = 16f, wheelAngle = 420f;
        [Tooltip("Stand-on machines: the operator stands at the controls.")]
        public bool standing;

        Quaternion rL, rR, rLL, rRL, rAux, rPL, rPR, rW;
        CabInput cur, target;
        bool init;

        public CabInput Current => cur;

        void Init()
        {
            if (init) return;
            init = true;
            Quaternion R(Transform t) => t != null ? t.localRotation : Quaternion.identity;
            rL = R(leftStick); rR = R(rightStick); rLL = R(leftLever); rRL = R(rightLever); rAux = R(auxLever);
            rPL = R(leftPedal); rPR = R(rightPedal); rW = R(wheel);
        }

        void Awake() => Init();

        /// <summary>The machine's controls this frame. Called by the machine (or the network) every frame.</summary>
        public void Set(CabInput input) => target = input;

        void Update()
        {
            Init();
            float k = 1f - Mathf.Exp(-Time.deltaTime * 14f); // hydraulic pilot controls move quickly but not instantly
            cur.left = Vector2.Lerp(cur.left, Clamp(target.left), k);
            cur.right = Vector2.Lerp(cur.right, Clamp(target.right), k);
            cur.leverL = Mathf.Lerp(cur.leverL, Mathf.Clamp(target.leverL, -1f, 1f), k);
            cur.leverR = Mathf.Lerp(cur.leverR, Mathf.Clamp(target.leverR, -1f, 1f), k);
            cur.aux = Mathf.Lerp(cur.aux, Mathf.Clamp(target.aux, -1f, 1f), k);
            cur.pedalL = Mathf.Lerp(cur.pedalL, Mathf.Clamp01(target.pedalL), k);
            cur.pedalR = Mathf.Lerp(cur.pedalR, Mathf.Clamp01(target.pedalR), k);
            cur.wheel = Mathf.Lerp(cur.wheel, Mathf.Clamp(target.wheel, -1f, 1f), 1f - Mathf.Exp(-Time.deltaTime * 6f));

            // a pivot's +z is the machine's forward, +y up: pushing a stick forward tips its top forward
            Stick(leftStick, rL, cur.left, stickAngle);
            Stick(rightStick, rR, cur.right, stickAngle);
            Stick(leftLever, rLL, new Vector2(0f, cur.leverL), leverAngle);
            Stick(rightLever, rRL, new Vector2(0f, cur.leverR), leverAngle);
            Stick(auxLever, rAux, new Vector2(0f, cur.aux), leverAngle);
            Stick(leftPedal, rPL, new Vector2(0f, cur.pedalL), pedalAngle);
            Stick(rightPedal, rPR, new Vector2(0f, cur.pedalR), pedalAngle);
            if (wheel != null) wheel.localRotation = rW * Quaternion.Euler(0f, 0f, -cur.wheel * wheelAngle);
        }

        static Vector2 Clamp(Vector2 v) => v.sqrMagnitude > 1f ? v.normalized : v;

        static void Stick(Transform t, Quaternion rest, Vector2 v, float a)
        {
            if (t == null) return;
            t.localRotation = rest * Quaternion.Euler(v.y * a, 0f, -v.x * a);
        }
    }
}
