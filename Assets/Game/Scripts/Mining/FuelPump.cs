using FPSBase.Core;
using FPSBase.Vehicles;
using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>Diesel pump: fills every vehicle parked within range, charged per liter.</summary>
    public class FuelPump : Interactable
    {
        public float pricePerLiter = 1.45f;
        public float range = 9f;

        public override string Prompt => $"Diesel pump  (${pricePerLiter:0.00}/L, fills vehicles within {range:0} m)";

        public override void Interact(PlayerInteractor player)
        {
            var eco = Economy.Instance;
            float total = 0f; int count = 0;
            foreach (var v in FindObjectsByType<VehicleBase>(FindObjectsSortMode.None))
            {
                if (Vector3.Distance(v.transform.position, transform.position) > range) continue;
                float need = v.fuelCapacity - v.fuel;
                if (need < 0.5f) continue;
                float affordable = Mathf.Min(need, eco.Unlimited ? need : eco.Money / pricePerLiter);
                if (affordable < 0.5f) { GameUI.Toast("Not enough money for fuel"); break; }
                eco.TrySpend(affordable * pricePerLiter);
                v.Refuel(affordable);
                total += affordable; count++;
            }
            foreach (var t in FuelTank.All)
            {
                if (t == null || Vector3.Distance(t.transform.position, transform.position) > range + 2f) continue;
                float need = t.capacity - t.liters;
                if (need < 0.5f) continue;
                float affordable = Mathf.Min(need, eco.Unlimited ? need : eco.Money / pricePerLiter);
                if (affordable < 0.5f) { GameUI.Toast("Not enough money for fuel"); break; }
                eco.TrySpend(affordable * pricePerLiter);
                t.liters += affordable;
                total += affordable; count++;
            }
            GameUI.Toast(count == 0 ? "Park a vehicle next to the pump to fill it" : $"Pumped {total:0} L into {count} vehicle(s) / tank(s) for ${total * pricePerLiter:N0}");
        }
    }
}
