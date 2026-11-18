using FPSBase.Core;
using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>
    /// Gold wash plant: dump pay dirt in the hopper, it feeds through the sluice, gold collects in the sluice box,
    /// waste goes out to the tailings pile. Press E for the panel: clean up the gold, refuel the generator, buy upgrades.
    /// </summary>
    public class WashPlant : Interactable, IMaterialReceiver
    {
        [System.Serializable]
        public class UpgradeTrack
        {
            public string name;
            public string unit;
            public float[] values;
            public float[] costs;  // cost to reach that level (index 0 = starting level, free)
            public int level;
            public float Value => values[level];
            public bool Maxed => level >= values.Length - 1;
            public float NextCost => Maxed ? 0f : costs[level + 1];
        }

        [Header("Parts")]
        public BoxCollider hopperVolume;      // trigger over the hopper opening
        public Transform hopperContents;      // dirt visual in the hopper
        public Transform tailingsPoint;
        public Transform sluiceGoldVisual;
        [Tooltip("Where rocks rejected by the grizzly bars over the hopper roll off.")]
        public Transform rejectPoint;

        [Header("Upgrades")]
        public UpgradeTrack throughput = new UpgradeTrack
        {
            name = "Feed rate", unit = "m³/min",
            values = new[] { 0.4f, 0.8f, 1.5f, 3f }, costs = new[] { 0f, 4000f, 12000f, 35000f }
        };
        public UpgradeTrack recovery = new UpgradeTrack
        {
            name = "Gold recovery", unit = "%",
            values = new[] { 55f, 70f, 82f, 92f }, costs = new[] { 0f, 3000f, 10000f, 30000f }
        };
        public UpgradeTrack scrubber = new UpgradeTrack
        {
            name = "Clay scrubber", unit = "% clay loss",
            values = new[] { 60f, 40f, 20f, 6f }, costs = new[] { 0f, 5000f, 15000f, 40000f }
        };
        public UpgradeTrack hopperSize = new UpgradeTrack
        {
            name = "Hopper size", unit = "m³",
            values = new[] { 1.5f, 3f, 6f, 12f }, costs = new[] { 0f, 2500f, 8000f, 20000f }
        };

        [Header("Generator")]
        public float fuelCapacity = 100f;
        public float fuel = 40f;
        public float burnPerM3 = 1.2f;        // liters of diesel per m^3 processed
        public float fuelPrice = 1.65f;       // $/L delivered to the plant

        public MaterialLoad hopper;
        public float sluiceGold;               // grams waiting for clean-up
        public float totalProcessed;
        public bool running = true;
        public float rocksRejected;
        float lastRejectToast = -10f;

        /// <summary>Gold recovery right now (0..1) for a feed with this much clay in it.</summary>
        public float RecoveryFor(float clayFraction) => recovery.Value / 100f * (1f - scrubber.Value / 100f * Mathf.Clamp01(clayFraction * 1.4f));

        public override string Prompt => $"Wash plant  (hopper {hopper.volume:0.0}/{hopperSize.Value:0.#} m³, sluice {sluiceGold:0.00} g)";

        public float FreeSpace => Mathf.Max(0f, hopperSize.Value - hopper.volume);

        public bool AcceptsAt(Vector3 worldPoint)
        {
            if (hopperVolume == null) return false;
            Vector3 l = hopperVolume.transform.InverseTransformPoint(worldPoint) - hopperVolume.center;
            Vector3 h = hopperVolume.size * 0.5f;
            return Mathf.Abs(l.x) <= h.x && Mathf.Abs(l.z) <= h.z;
        }

        public void Receive(MaterialLoad l)
        {
            // the grizzly (steel bars over the hopper) keeps the boulders out
            var rocks = l.TakeRocks();
            if (!rocks.IsEmpty)
            {
                rocksRejected += rocks.volume;
                Vector3 at = rejectPoint != null ? rejectPoint.position : transform.position + transform.right * 3f;
                Boulder.SpawnFromLoad(at, rocks);
                if (Time.time - lastRejectToast > 4f) { GameUI.Toast($"The grizzly rejected {rocks.volume:0.0#} m³ of rocks"); lastRejectToast = Time.time; }
            }
            hopper.Add(l);
            UpdateVisuals();
        }

        void Update()
        {
            if (!running || hopper.IsEmpty) return;
            if (fuel <= 0f) return;
            // sticky clay feeds slower and rolls gold away in clay balls
            float clay = hopper.ClayF;
            float m3 = throughput.Value / 60f * Time.deltaTime * (1f - 0.45f * clay);
            var batch = hopper.Take(m3);
            fuel = Mathf.Max(0f, fuel - batch.volume * burnPerM3);
            float rec = RecoveryFor(batch.ClayF);
            sluiceGold += batch.goldGrams * rec;
            totalProcessed += batch.volume;
            var tail = batch; tail.goldGrams = batch.goldGrams * (1f - rec); // washed sand & gravel out the back
            if (tailingsPoint != null) DigSite.DropAt(tailingsPoint.position, tail);
            if (fuel <= 0f) GameUI.Toast("Wash plant generator is out of fuel!");
            UpdateVisuals();
        }

        void UpdateVisuals()
        {
            if (hopperContents != null)
            {
                float f = Mathf.Clamp01(hopper.volume / hopperSize.Value);
                hopperContents.gameObject.SetActive(f > 0.005f);
                var s = hopperContents.localScale; s.y = Mathf.Lerp(0.02f, 1f, f); hopperContents.localScale = s;
            }
            if (sluiceGoldVisual != null) sluiceGoldVisual.gameObject.SetActive(sluiceGold > 0.05f);
        }

        public override void Interact(PlayerInteractor player)
        {
            GameUI.Instance.OpenPanel("Wash Plant", DrawPanel);
        }

        void DrawPanel()
        {
            var eco = Economy.Instance;
            GUILayout.Label($"Hopper: {hopper.volume:0.00} / {hopperSize.Value:0.#} m³   (avg grade {hopper.Grade:0.0} g/m³)");
            if (!hopper.IsEmpty) GUILayout.Label($"Feed: {hopper.Describe()}   →  recovering {RecoveryFor(hopper.ClayF) * 100f:0}% of the gold");
            GUILayout.Label("Tip: clay balls carry gold out with the tailings — upgrade the scrubber, or blend clay with gravel. The grizzly rejects rocks.");
            GUILayout.Label($"Gold in sluice box: {sluiceGold:0.00} g");
            GUILayout.Label($"Feed rate {throughput.Value:0.0} m³/min · Recovery {recovery.Value:0}% · Processed {totalProcessed:0.0} m³ total");
            GUILayout.Label($"Generator fuel: {fuel:0} / {fuelCapacity:0} L");
            running = GUILayout.Toggle(running, " Plant running");

            GUILayout.Space(8);
            GUI.enabled = sluiceGold > 0.001f;
            if (GUILayout.Button($"Clean up sluice  (collect {sluiceGold:0.00} g gold)", GUILayout.Height(34)))
            {
                eco.AddGold(sluiceGold);
                GameUI.Toast($"Cleaned up {sluiceGold:0.00} g of gold");
                sluiceGold = 0f;
                UpdateVisuals();
            }
            float need = fuelCapacity - fuel;
            GUI.enabled = need > 0.5f;
            if (GUILayout.Button($"Refill generator  ({need:0} L for ${need * fuelPrice:N0})", GUILayout.Height(30)))
                if (eco.TrySpend(need * fuelPrice)) fuel = fuelCapacity;
            GUI.enabled = true;

            GUILayout.Space(10);
            GUILayout.Label("Upgrades");
            DrawUpgrade(throughput, eco);
            DrawUpgrade(recovery, eco);
            DrawUpgrade(scrubber, eco);
            DrawUpgrade(hopperSize, eco);
        }

        void DrawUpgrade(UpgradeTrack t, Economy eco)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{t.name}: {t.Value:0.##} {t.unit}  (level {t.level + 1}/{t.values.Length})", GUILayout.Width(300));
            if (t.Maxed) GUILayout.Label("MAX");
            else if (GUILayout.Button($"→ {t.values[t.level + 1]:0.##} {t.unit}  ${t.NextCost:N0}"))
            {
                if (eco.TrySpend(t.NextCost)) { t.level++; GameUI.Toast($"{t.name} upgraded to {t.Value:0.##} {t.unit}"); }
            }
            GUILayout.EndHorizontal();
        }
    }
}
