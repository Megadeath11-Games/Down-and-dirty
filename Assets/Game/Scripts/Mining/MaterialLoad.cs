using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>
    /// A quantity of dirt: volume in cubic meters, the grams of gold mixed in it, and what it's made of.
    /// `sand`, `gravel`, `clay` and `rock` are the m^3 of each inside `volume`; the rest is plain dirt (topsoil/silt).
    /// Rock means boulders: they ride along in buckets and truck beds, but come back out as boulders when dumped.
    /// </summary>
    [System.Serializable]
    public struct MaterialLoad
    {
        public float volume;     // m^3
        public float goldGrams;  // g contained (before processing losses)
        public float sand, gravel, clay, rock; // m^3 of each, part of volume

        public MaterialLoad(float volume, float goldGrams) { this.volume = volume; this.goldGrams = goldGrams; sand = gravel = clay = rock = 0f; }

        /// <summary>`volume` m^3 with the given fractions (0..1) of sand, gravel and clay.</summary>
        public static MaterialLoad Mix(float volume, float goldGrams, float sandF, float gravelF, float clayF)
            => new MaterialLoad(volume, goldGrams) { sand = volume * sandF, gravel = volume * gravelF, clay = volume * clayF };

        public static MaterialLoad Rocks(float volume) => new MaterialLoad(volume, 0f) { rock = volume };

        public float Grade => volume > 1e-5f ? goldGrams / volume : 0f;
        public bool IsEmpty => volume <= 1e-5f;
        public float Dirt => Mathf.Max(0f, volume - sand - gravel - clay - rock);
        public float SandF => volume > 1e-5f ? sand / volume : 0f;
        public float GravelF => volume > 1e-5f ? gravel / volume : 0f;
        public float ClayF => volume > 1e-5f ? clay / volume : 0f;
        public float RockF => volume > 1e-5f ? rock / volume : 0f;

        public void Add(MaterialLoad o)
        {
            volume += o.volume; goldGrams += o.goldGrams;
            sand += o.sand; gravel += o.gravel; clay += o.clay; rock += o.rock;
        }

        /// <summary>A copy scaled by `f` (same make-up).</summary>
        public MaterialLoad Scaled(float f) => new MaterialLoad(volume * f, goldGrams * f)
            { sand = sand * f, gravel = gravel * f, clay = clay * f, rock = rock * f };

        /// <summary>Removes up to `amount` m^3 (keeping the same grade and make-up) and returns it.</summary>
        public MaterialLoad Take(float amount)
        {
            amount = Mathf.Clamp(amount, 0f, volume);
            if (amount <= 0f) return default;
            var taken = Scaled(amount / volume);
            volume -= taken.volume; goldGrams -= taken.goldGrams;
            sand -= taken.sand; gravel -= taken.gravel; clay -= taken.clay; rock -= taken.rock;
            if (volume < 1e-5f) this = default;
            return taken;
        }

        /// <summary>Splits the boulders out of this load and returns them (as a rock-only load).</summary>
        public MaterialLoad TakeRocks()
        {
            if (rock <= 1e-5f) return default;
            var r = Rocks(Mathf.Min(rock, volume));
            volume -= r.volume; rock = 0f;
            if (volume < 1e-5f) this = default;
            return r;
        }

        /// <summary>What it is, e.g. "gravel 55% · sand 25% · clay 10%".</summary>
        public string Describe()
        {
            if (IsEmpty) return "empty";
            var sb = new System.Text.StringBuilder();
            void P(string n, float f) { if (f >= 0.05f) { if (sb.Length > 0) sb.Append(" · "); sb.Append($"{n} {f * 100f:0}%"); } }
            P("gravel", GravelF); P("sand", SandF); P("clay", ClayF); P("dirt", Dirt / volume);
            if (rock > 0.005f) { if (sb.Length > 0) sb.Append(" · "); sb.Append($"rocks {rock:0.0#} m³"); }
            return sb.Length > 0 ? sb.ToString() : "dirt";
        }

        /// <summary>Main material name for a load: "Gravel", "Sand", "Clay", "Rock" or "Dirt".</summary>
        public string Kind
        {
            get
            {
                if (IsEmpty) return "Nothing";
                float d = Dirt;
                float best = Mathf.Max(Mathf.Max(sand, gravel), Mathf.Max(Mathf.Max(clay, rock), d));
                return best == rock ? "Rock" : best == gravel ? "Gravel" : best == sand ? "Sand" : best == clay ? "Clay" : "Dirt";
            }
        }
    }

    /// <summary>Something dirt can be poured into: a truck bed, a hopper, a pile on the ground.</summary>
    public interface IMaterialReceiver
    {
        float FreeSpace { get; }
        /// <summary>True if material poured from above at this world point lands in this receiver.</summary>
        bool AcceptsAt(Vector3 worldPoint);
        void Receive(MaterialLoad load);
    }

    public static class MaterialTransfer
    {
        /// <summary>Find what is directly under a pour point (truck bed, hopper, pile, or nothing = ground).</summary>
        public static IMaterialReceiver FindReceiverBelow(Vector3 from, Transform ignore, out Vector3 groundPoint)
        {
            groundPoint = from + Vector3.down * 2f;
            // Start well above the pour point: a ray that starts inside a collider (e.g. a bucket already down
            // inside the truck bed's volume) would never hit it.
            var hits = Physics.RaycastAll(from + Vector3.up * 2.5f, Vector3.down, 15f, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                var rec = h.collider.GetComponentInParent<IMaterialReceiver>();
                if (rec != null && rec.AcceptsAt(h.point)) return rec;
                if (!h.collider.isTrigger) { groundPoint = h.point; return null; }
            }
            return null;
        }

        /// <summary>
        /// Pour `load` at a point. The dirt falls as real clods (DirtGrains) and lands in whatever catches it: a truck
        /// bed or hopper below, the side of a machine (and spills off), or the ground. Boulders drop straight down.
        /// </summary>
        public static void Pour(Vector3 from, MaterialLoad load, Transform ignore, float grainR = 0f)
        {
            if (load.IsEmpty) return;
            var rocks = load.TakeRocks();
            if (!rocks.IsEmpty) PourInstant(from, rocks, ignore);
            if (!load.IsEmpty) DirtGrains.Pour(from, load, ignore, default, grainR);
        }

        /// <summary>The instant version: straight into the receiver below, otherwise onto the ground.</summary>
        public static void PourInstant(Vector3 from, MaterialLoad load, Transform ignore)
        {
            if (load.IsEmpty) return;
            var rec = FindReceiverBelow(from, ignore, out Vector3 ground);
            PourFx.Stream(from, ground, load.volume);
            if (rec != null && rec.FreeSpace > 0f)
            {
                var fit = load.Take(rec.FreeSpace);
                rec.Receive(fit);
            }
            if (!load.IsEmpty) DigSite.DropAt(ground, load);
        }
    }
}
