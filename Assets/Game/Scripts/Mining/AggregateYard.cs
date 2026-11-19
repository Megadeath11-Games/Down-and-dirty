using FPSBase.Core;
using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>
    /// Sand &amp; gravel yard: dump any load into the bay and get paid for what it's made of. Turns the overburden,
    /// clay, tailings and boulders you have to move anyway into a bit of cash. Any gold in it is lost.
    /// </summary>
    public class AggregateYard : Interactable, IMaterialReceiver
    {
        public BoxCollider bayVolume;
        [Header("Prices ($ per m³)")]
        public float dirtPrice = 6f, clayPrice = 15f, sandPrice = 26f, gravelPrice = 34f, rockPrice = 45f;

        public float totalSold, totalEarned;
        MaterialLoad pending;
        float pendingMoney, pendingTimer;

        public override string Prompt => $"Sand & gravel yard  —  dump loads in the bay  (gravel ${gravelPrice:0}, sand ${sandPrice:0}, rock ${rockPrice:0}, clay ${clayPrice:0}, dirt ${dirtPrice:0} per m³)";

        public float FreeSpace => float.MaxValue;

        void Start()
        {
            // the bay sits on the dirt field: scuff the grass off under it so no tufts poke through the pad
            var site = DigSite.At(transform.position);
            if (site == null || bayVolume == null) return;
            Vector3 c = bayVolume.transform.TransformPoint(bayVolume.center);
            Vector3 h = bayVolume.size * 0.5f + new Vector3(1f, 0f, 1f);
            for (float x = -h.x; x <= h.x; x += 0.5f)
                for (float z = -h.z; z <= h.z; z += 0.5f)
                    site.Trample(c + bayVolume.transform.rotation * new Vector3(x, 0f, z), 0.4f);
        }

        public bool AcceptsAt(Vector3 worldPoint)
        {
            if (bayVolume == null) return false;
            Vector3 l = bayVolume.transform.InverseTransformPoint(worldPoint) - bayVolume.center;
            Vector3 h = bayVolume.size * 0.5f;
            return Mathf.Abs(l.x) <= h.x && Mathf.Abs(l.z) <= h.z;
        }

        public float PriceOf(MaterialLoad l) =>
            l.Dirt * dirtPrice + l.clay * clayPrice + l.sand * sandPrice + l.gravel * gravelPrice + l.rock * rockPrice;

        public void Receive(MaterialLoad l)
        {
            if (l.IsEmpty) return;
            float money = PriceOf(l);
            Economy.Instance?.Earn(money);
            totalSold += l.volume; totalEarned += money;
            pending.Add(l); pendingMoney += money;
            pendingTimer = 1.5f;
        }

        void Update()
        {
            // loads pour in over a few seconds: one message per load
            if (pendingTimer <= 0f || (pendingTimer -= Time.deltaTime) > 0f) return;
            string lost = pending.goldGrams > 0.2f ? $"  ({pending.goldGrams:0.0} g of gold went with it!)" : "";
            GameUI.Toast($"Sold {pending.volume:0.0} m³ ({pending.Describe()}) for ${pendingMoney:N0}{lost}");
            pending = default; pendingMoney = 0f;
        }

        public override void Interact(PlayerInteractor player) => GameUI.Instance.OpenPanel("Sand & Gravel Yard", Draw);

        void Draw()
        {
            GUILayout.Label("Back a truck up to the bay and tip it, or dump a bucket in. You're paid for what the load is made of:");
            GUILayout.Space(4);
            GUILayout.Label($"   Gravel  ${gravelPrice:0} / m³      Sand  ${sandPrice:0} / m³      Rock (boulders)  ${rockPrice:0} / m³");
            GUILayout.Label($"   Clay  ${clayPrice:0} / m³      Dirt / topsoil  ${dirtPrice:0} / m³");
            GUILayout.Space(6);
            GUILayout.Label("Good for overburden, tailings from the wash plant, and rocks the grizzly throws out. Gold in a load is NOT paid for — wash pay dirt first!");
            GUILayout.Space(8);
            GUILayout.Label($"Sold here so far: {totalSold:0.0} m³ for ${totalEarned:N0}");
        }
    }
}
