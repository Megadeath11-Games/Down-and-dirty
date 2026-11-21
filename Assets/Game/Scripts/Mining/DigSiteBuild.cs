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

        // squares with concrete in them: the ground's top is the concrete's, and it can't be dug below it
        float[,] solidFloor;

        /// <summary>Concrete in a square up to `y`: the ground comes up to it (any loose dirt it's poured on is buried), and digging stops there.</summary>
        public void SetSolid(int x, int z, float y, bool cut = false)
        {
            if (data == null || x < 1 || z < 1 || x > res - 2 || z > res - 2) return;
            if (solidFloor == null)
            {
                solidFloor = new float[res, res];
                for (int j = 0; j < res; j++) for (int i = 0; i < res; i++) solidFloor[j, i] = float.NaN;
            }
            if (cut && Surf(x, z) > y) Remove(x, z, Surf(x, z) - y, y); // screeded off flat
            if (native[z, x] < y)
            {
                if (loose[z, x] > 0f) Remove(x, z, loose[z, x], native[z, x]);
                native[z, x] = y;
            }
            solidFloor[z, x] = y;
            Touch(x, z);
        }

        /// <summary>The square's diggable again (the site was given up).</summary>
        public void ClearSolid(int x, int z)
        {
            if (solidFloor == null || x < 1 || z < 1 || x > res - 2 || z > res - 2) return;
            solidFloor[z, x] = float.NaN;
        }

        public bool CellSolid(int x, int z) => solidFloor != null && !float.IsNaN(solidFloor[z, x]);
    }
}
