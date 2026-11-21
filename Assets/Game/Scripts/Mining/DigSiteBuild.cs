using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>Square-by-square looks at the ground, for the house foundations (pads and footing trenches).</summary>
    public partial class DigSite
    {
        /// <summary>The square a world point is on (false off the field).</summary>
        public bool CellOf(Vector3 p, out int x, out int z)
        {
            x = Mathf.RoundToInt((p.x - origin.x) / cell); z = Mathf.RoundToInt((p.z - origin.z) / cell);
            return data != null && x >= 1 && z >= 1 && x < res - 1 && z < res - 1;
        }

        /// <summary>The surface (loose dirt included) of a square.</summary>
        public float CellSurface(int x, int z) => Surf(x, z);

        /// <summary>Under a street or a house (can't be dug).</summary>
        public bool CellHard(int x, int z) => hard != null && hard[z, x];

        /// <summary>The squares a side, and a square's index (z * res + x) back to x, z.</summary>
        public int CellIndex(int x, int z) => z * res + x;
        public void CellXZ(int i, out int x, out int z) { x = i % res; z = i / res; }
    }
}
