using UnityEngine;

namespace FPSBase.Core
{
    /// <summary>Marks a machine or trailer bought at the dealer (its listing), so a save can bring it back.</summary>
    public class BoughtFrom : MonoBehaviour
    {
        public int listing = -1;

        public static void Mark(GameObject go, int listing)
        {
            var b = go.GetComponent<BoughtFrom>();
            if (b == null) b = go.AddComponent<BoughtFrom>();
            b.listing = listing;
        }
    }
}
