using FPSBase.Core;
using FPSBase.Player;
using UnityEngine;

namespace FPSBase.UI
{
    /// <summary>On-foot HUD: crosshair, speed (u/s like CS), movement state, controls. Hidden while in a vehicle.</summary>
    public class MovementHud : MonoBehaviour
    {
        public SourceMovement movement;
        public float respawnBelowY = -30f;
        public Vector3 respawnPoint;
        GUIStyle big, small;

        void Start()
        {
            if (movement == null) movement = FindFirstObjectByType<SourceMovement>();
            if (movement != null) respawnPoint = movement.transform.position;
        }

        void Update()
        {
            if (movement == null || !movement.isActiveAndEnabled) return;
            if (movement.transform.position.y < respawnBelowY) movement.Teleport(respawnPoint);
        }

        void OnGUI()
        {
            GUI.skin = FPSBase.UI.ShopTheme.Skin;
            if (movement == null || !movement.isActiveAndEnabled) return;
            if (FPSBase.Core.GameUI.Instance != null && FPSBase.Core.GameUI.Instance.BlocksGameplay) return;
            big ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            small ??= new GUIStyle(GUI.skin.label) { fontSize = 13 };

            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.DrawTexture(new Rect(cx - 2, cy - 2, 4, 4), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            int ups = Mathf.RoundToInt(movement.HorizontalSpeed / 0.0254f);
            GUI.Label(new Rect(cx - 150, Screen.height - 60, 300, 30), $"{ups}  ({movement.HorizontalSpeed:0.0} m/s)", big);

            GUI.Label(new Rect(12, 30, 560, 54),
                $"{N(GameAction.MoveForward)}{N(GameAction.MoveLeft)}{N(GameAction.MoveBack)}{N(GameAction.MoveRight)} move · {N(GameAction.Sprint)} sprint · {N(GameAction.Jump)} jump (bhop: jump as you land, auto-bhop in Esc menu)\n" +
                $"{N(GameAction.Crouch)} crouch (sprint + crouch = slide) · Hold {N(GameAction.Jump)} + {N(GameAction.MoveForward)} at a ledge to climb\n" +
                $"{N(GameAction.Interact)} use / shops / get in vehicles · Esc menu, settings & controls", small);
            GUI.color = Color.white;
        }

        static string N(GameAction a) => Controls.N(a);
    }
}
