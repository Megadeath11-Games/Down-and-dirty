using System.Collections.Generic;
using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>
    /// A house in town: who lives there, its address, and the bits of its lot a customer might want worked on
    /// (the driveway, the yard around the house for a drainage ditch or a trench). Look at the house or its mailbox
    /// to see what's going on there; it opens their texts on your phone.
    /// </summary>
    public class House : Interactable
    {
        static readonly List<House> all = new();
        /// <summary>Every house, by its number in the town (the same on every game).</summary>
        public static IReadOnlyList<House> All => all;

        public int index;
        public string first = "Pat", last = "Smith";
        public string street = "Maple St";
        public int number = 100;
        [Tooltip("The driveway, world X/Z (from the sidewalk to the garage).")]
        public Rect driveway;
        [Tooltip("Where a drainage ditch would go down the side of the lot (ground points; older saves).")]
        public Vector3 ditchA, ditchB;
        public Transform mailbox;

        [Header("The lot (in the lot's own frame: x across, z from the sidewalk back)")]
        [Tooltip("The house walls (local x0, z0 to x1, z1).")]
        public Rect houseLocal = new Rect(-9.5f, 9f, 11f, 8f);
        [Tooltip("The driveway (local).")]
        public Rect drivewayLocal = new Rect(-9f, 0f, 4f, 9f);
        public float lotHalfWidth = 11f, lotDepth = 20.5f;
        [Tooltip("Which way the yard's open side is (+1 = local +x).")]
        public int yardSide = 1;
        [Tooltip("North side of the street (you pass it on your left driving in from the west).")]
        public bool northSide = true;

        public string Owner => $"{first} {last}";
        public string Address => $"{number} {street}";
        public Vector3 Front => mailbox != null ? mailbox.position : transform.position;
        /// <summary>The house walls on the ground (world X/Z).</summary>
        public Rect Footprint
        {
            get
            {
                Vector3 a = transform.TransformPoint(new Vector3(houseLocal.xMin, 0, houseLocal.yMin)), b = transform.TransformPoint(new Vector3(houseLocal.xMax, 0, houseLocal.yMax));
                return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
            }
        }
        public Vector3 Local(float x, float z) => transform.TransformPoint(new Vector3(x, 0f, z));

        Damageable[] parts;
        public Damageable[] Parts => parts ??= GetComponentsInChildren<Damageable>(true);
        public Damageable Part(string id) { foreach (var d in Parts) if (d.id == id) return d; return null; }

        void OnEnable() { all.Add(this); all.Sort((a, b) => a.index.CompareTo(b.index)); }
        void OnDisable() => all.Remove(this);

        void Start()
        {
            // an old dirt driveway: no grass on it
            var site = DigSite.At(driveway.center3());
            if (site != null && site.Contains(driveway.center3())) site.TrampleRect(driveway);
            site?.ForgetChanges();
        }

        /// <summary>How to get here from the road into town (it comes in over the pass onto West Ave at Maple St).</summary>
        public string Directions
        {
            get
            {
                string way = street switch
                {
                    "Maple St" => "go straight on into Maple St",
                    "Birch Ln" => "turn right on West Ave, then left onto Birch Ln",
                    "Cedar Ave" => "turn left on West Ave, then right onto Cedar Ave",
                    _ => $"find {street}",
                };
                // which house along that side of the street, from the west end
                int nth = 1;
                foreach (var h in all)
                    if (h != this && h.street == street && h.northSide == northSide && h.transform.position.x < transform.position.x) nth++;
                return $"From the road into town, {way}. Number {number} is the {Ordinal(nth)} house on your {(northSide ? "left" : "right")}.";
            }
        }

        static string Ordinal(int n) => n switch { 1 => "first", 2 => "second", 3 => "third", 4 => "fourth", 5 => "fifth", 6 => "sixth", 7 => "seventh", 8 => "eighth", _ => n + "th" };

        /// <summary>Show a part as broken (a mailbox or fence knocked over) — it's the same on every game.</summary>
        public void ShowDamage(string id)
        {
            var d = Part(id);
            if (d != null) d.ShowBroken();
        }

        public override string Prompt
        {
            get
            {
                var j = Jobs.Instance != null ? Jobs.Instance.OpenJobAt(this) : null;
                string what = j == null ? "" : j.state == JobState.Offered ? $"  —  wants: {j.Title} (${j.pay:N0}, answer on your phone)" : $"  —  {(Jobs.WhoHas(j) == "you" ? "your" : Jobs.WhoHas(j) == "the crew" ? "the crew's" : Jobs.WhoHas(j) + "'s")} job: {j.Title}";
                return $"{Address}  ({Owner}){what}";
            }
        }

        public override void Interact(PlayerInteractor player) => Jobs.Instance?.OpenPhone(this);
    }

    static class RectExt
    {
        public static Vector3 center3(this Rect r) => new Vector3(r.center.x, 0f, r.center.y);
    }
}
