using System.Collections.Generic;
using FPSBase.Core;
using FPSBase.Mining;
using FPSBase.UI;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>
    /// The landscape supply's overhead bins: back a truck under the chute, pick what you want and how much, pay,
    /// and it pours into the bed. Gravel for driveways, sand, and topsoil for lawns.
    /// </summary>
    public class GravelBin : Interactable
    {
        public Transform chute;
        public float gravelPrice = 48f, sandPrice = 40f, topsoilPrice = 22f;
        [Tooltip("m³ per second out of the chute.")]
        public float pourRate = 1.6f;

        MaterialLoad pouring;
        public bool Pouring => !pouring.IsEmpty;

        public override string Prompt => $"Landscape supply  —  back a truck under the chute  (gravel ${gravelPrice:0}, sand ${sandPrice:0}, topsoil ${topsoilPrice:0} per m³)";

        public override void Interact(PlayerInteractor player) => GameUI.Instance.OpenPanel("Landscape Supply", Draw);

        IMaterialReceiver Below() => chute != null ? MaterialTransfer.FindReceiverBelow(chute.position, transform, out _) : null;

        public static MaterialLoad Make(int kind, float m3)
        {
            var l = new MaterialLoad(m3, 0f);
            if (kind == 0) l.gravel = m3; else if (kind == 1) l.sand = m3;
            return l;
        }

        public float Price(int kind) => kind == 0 ? gravelPrice : kind == 1 ? sandPrice : topsoilPrice;

        /// <summary>Pay for `m3` of gravel (0), sand (1) or topsoil (2) and pour it down the chute.</summary>
        public bool Buy(int kind, float m3)
        {
            if (m3 <= 0.01f || Economy.Instance == null || !Economy.Instance.TrySpend(Price(kind) * m3)) return false;
            pouring.Add(Make(kind, m3));
            GameUI.Toast($"Loading {m3:0.#} m³ of {Names[kind].ToLower()} (${Price(kind) * m3:N0})");
            return true;
        }

        static readonly string[] Names = { "Gravel", "Sand", "Topsoil" };

        void Draw()
        {
            var rec = Below();
            float room = rec != null ? rec.FreeSpace : 0f;
            GUILayout.Label("Back a dump truck (or a trailer) under the chute, then pick what to load. You pay for what comes out.");
            GUILayout.Space(4);
            GUILayout.Label(rec != null ? $"Under the chute: a truck with room for {room:0.#} m³." : "<color=#e8a060>Nothing under the chute: it would pour on the ground.</color>");
            if (Pouring) GUILayout.Label($"Pouring... {pouring.volume:0.0} m³ to go");
            GUILayout.Space(8);
            for (int k = 0; k < 3; k++)
            {
                GUILayout.Label($"<b>{Names[k]}</b>   ${Price(k):0} / m³" + (k == 0 ? "   (driveways)" : k == 2 ? "   (lawns and garden beds)" : ""));
                GUILayout.BeginHorizontal();
                foreach (float m in new[] { 1f, 3f, 6f, 10f })
                    if (GUILayout.Button($"{m:0} m³  ${Price(k) * m:N0}", GUILayout.Height(32))) Buy(k, m);
                GUI.enabled = room > 0.2f;
                if (GUILayout.Button(room > 0.2f ? $"Fill it  ({room:0.#} m³, ${Price(k) * room:N0})" : "Fill it", GUILayout.Height(32))) Buy(k, Mathf.Floor(room * 10f) / 10f);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }
        }

        void Update()
        {
            if (pouring.IsEmpty || chute == null) return;
            var part = pouring.Take(pourRate * Time.deltaTime);
            Vector3 j = new Vector3(Random.Range(-0.35f, 0.35f), 0f, Random.Range(-0.35f, 0.35f));
            MaterialTransfer.Pour(chute.position + j, part, transform);
        }
    }
}
