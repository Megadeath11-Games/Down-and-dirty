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

        /// <summary>The pump whose hose reaches `p` (null = none).</summary>
        public static FuelPump Near(Vector3 p)
        {
            FuelPump best = null; float bd = float.MaxValue;
            foreach (var f in FindObjectsByType<FuelPump>(FindObjectsSortMode.None))
            {
                float d = Vector3.Distance(f.transform.position, p);
                if (d <= f.range + 2f && d < bd) { bd = d; best = f; }
            }
            return best;
        }

        /// <summary>Fill a transfer tank (tidy tank, fuel bowser) from this pump: liters bought.</summary>
        public float FillTank(FuelTank t)
        {
            var eco = Economy.Instance;
            float need = t.capacity - t.liters;
            if (need < 0.5f) return 0f;
            float affordable = eco == null ? need : Mathf.Min(need, eco.Unlimited ? need : eco.Money / pricePerLiter);
            if (affordable < 0.5f) return 0f;
            if (eco != null) eco.TrySpend(affordable * pricePerLiter);
            t.liters += affordable;
            return affordable;
        }

        /// <summary>Look-at menu entry: fill this tank at the pump (greyed out away from the pump).</summary>
        public static InteractOption FillOption(FuelTank t, Vector3 at)
        {
            var pump = Near(at);
            float need = t.capacity - t.liters;
            float price = pump != null ? pump.pricePerLiter : 1.45f;
            return new InteractOption
            {
                label = pump != null ? $"Refuel the {t.label.ToLower()} at the pump   ({t.liters:0}/{t.capacity:0} L, ${need * price:N0} to fill)"
                                     : $"{t.label}: {t.liters:0}/{t.capacity:0} L   (refuel it at the fuel pump; park by a machine to fuel it)",
                enabled = pump != null && need >= 0.5f,
                note = pump == null ? "drive it to the fuel pump at the outpost to fill it" : "it's full",
                run = p =>
                {
                    float l = pump.FillTank(t);
                    GameUI.Toast(l > 0f ? $"Filled the {t.label.ToLower()} with {l:0} L for ${l * pump.pricePerLiter:N0}  ({t.liters:0}/{t.capacity:0} L)" : "Not enough money for fuel");
                },
            };
        }
    }
}
