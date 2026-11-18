using UnityEngine;

namespace FPSBase.Core
{
    /// <summary>Player wallet and gold stash. One per scene.</summary>
    public class Economy : MonoBehaviour
    {
        public static Economy Instance { get; private set; }

        public float startingMoney = 2500f;
        public float Money { get; private set; }
        public float GoldGrams { get; private set; }

        void Awake()
        {
            Instance = this;
            Money = startingMoney;
        }

        /// <summary>Test mode: buying never costs anything (toggle in the Esc menu).</summary>
        public bool Unlimited => GameSettings.UnlimitedMoney;

        public bool CanAfford(float amount) => Unlimited || amount <= Money + 0.001f;

        public bool TrySpend(float amount)
        {
            if (Unlimited) return true;
            if (amount > Money + 0.001f) { GameUI.Toast($"Not enough money (need ${amount:N0})"); return false; }
            Money -= amount;
            return true;
        }

        public void Earn(float amount) => Money += amount;
        public void AddGold(float grams) => GoldGrams += grams;

        public float TakeAllGold()
        {
            float g = GoldGrams;
            GoldGrams = 0f;
            return g;
        }
    }
}
