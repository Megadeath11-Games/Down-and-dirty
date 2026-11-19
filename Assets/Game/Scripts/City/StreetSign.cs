using System.Collections.Generic;
using FPSBase.Core;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>
    /// A street sign at a corner (or the town's welcome board where the road comes in): its words show over it
    /// when you're close. The welcome board also lists the jobs you've taken and which way to turn for them.
    /// </summary>
    public class StreetSign : MonoBehaviour
    {
        [TextArea] public string text = "Maple St";
        public bool welcome;
        public float showWithin = 70f;
        static GUIStyle style, board;

        void OnGUI()
        {
            var ui = GameUI.Instance;
            if (ui == null || ui.BlocksGameplay) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 top = transform.position + Vector3.up * (welcome ? 3.2f : 2.9f);
            float dist = Vector3.Distance(cam.transform.position, top);
            if (dist > showWithin) return;
            Vector3 sp = cam.WorldToScreenPoint(top);
            if (sp.z <= 0f) return;
            style ??= new GUIStyle(GUI.skin.box) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white }, wordWrap = false };
            board ??= new GUIStyle(style) { fontSize = 14, alignment = TextAnchor.UpperLeft, padding = new RectOffset(12, 12, 8, 8), richText = true };
            string s = text;
            if (welcome && Jobs.Instance != null)
            {
                var lines = new List<string>();
                foreach (var j in Jobs.Instance.jobs)
                    if (j.state == JobState.Accepted && j.House != null)
                        lines.Add($"<color=#ffb050>{j.House.Address}</color> ({j.Title}): {Turn(j.House.street)}");
                if (lines.Count > 0) s += "\n\nYour jobs:\n" + string.Join("\n", lines);
            }
            var st = welcome ? board : style;
            var c = new GUIContent(s);
            var size = st.CalcSize(c);
            float a = Mathf.Clamp01((showWithin - dist) / 15f);
            var keep = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);
            var bg = GUI.backgroundColor;
            GUI.backgroundColor = welcome ? new Color(0.35f, 0.25f, 0.12f) : new Color(0.1f, 0.45f, 0.2f);
            GUI.Box(new Rect(sp.x - size.x / 2f, Screen.height - sp.y - size.y, size.x, size.y), c, st);
            GUI.backgroundColor = bg; GUI.color = keep;
        }

        static string Turn(string street) => street switch
        {
            "Maple St" => "straight on",
            "Birch Ln" => "turn right on West Ave",
            "Cedar Ave" => "turn left on West Ave",
            _ => street,
        };
    }
}
