using System.Collections.Generic;
using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>
    /// A house in town: who lives there, its address, and the bits of its lot a customer might want worked on
    /// (the driveway, a strip down the side of the lot for a drainage ditch). Look at the house or its mailbox
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
        [Tooltip("Where a drainage ditch would go down the side of the lot (ground points).")]
        public Vector3 ditchA, ditchB;
        public Transform mailbox;

        public string Owner => $"{first} {last}";
        public string Address => $"{number} {street}";
        public Vector3 Front => mailbox != null ? mailbox.position : transform.position;

        void OnEnable() { all.Add(this); all.Sort((a, b) => a.index.CompareTo(b.index)); }
        void OnDisable() => all.Remove(this);

        void Start()
        {
            // an old dirt driveway: no grass on it
            var site = DigSite.At(driveway.center3());
            if (site != null && site.Contains(driveway.center3())) site.TrampleRect(driveway);
            site?.ForgetChanges();
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
