using FPSBase.Core;
using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>Buys your cleaned-up gold for cash.</summary>
    public class GoldBuyer : Interactable
    {
        public float pricePerGram = 95f;

        public override string Prompt => $"Gold buyer  (${pricePerGram:0}/g)";

        public override void Interact(PlayerInteractor player) => GameUI.Instance.OpenPanel("Gold Buyer", Draw);

        void Draw()
        {
            var eco = Economy.Instance;
            GUILayout.Label($"Price: ${pricePerGram:0.00} per gram   (${pricePerGram * 31.1035f:N0} per troy oz)");
            GUILayout.Label($"You have {eco.GoldGrams:0.00} g of gold  =  ${eco.GoldGrams * pricePerGram:N0}");
            GUILayout.Space(10);
            GUI.enabled = eco.GoldGrams > 0.001f;
            if (GUILayout.Button("Sell all gold", GUILayout.Height(40)))
            {
                float g = eco.TakeAllGold();
                eco.Earn(g * pricePerGram);
                GameUI.Toast($"Sold {g:0.00} g for ${g * pricePerGram:N0}");
            }
            GUI.enabled = true;
        }
    }
}
