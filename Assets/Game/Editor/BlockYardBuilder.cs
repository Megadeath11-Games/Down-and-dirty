using System.Collections.Generic;
using FPSBase.City;
using UnityEditor;
using UnityEngine;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// The concrete wall blocks (one prefab per ConcreteBlock.Kinds entry) and the block yard beside the landscape
    /// supply: a pad, a painted loading bay, stacks of blocks for show and the yard office where you buy them.
    /// </summary>
    public static partial class ProjectBootstrap
    {
        static GameObject[] BuildBlockPrefabs()
        {
            var concrete = Flat("Concrete_Block", new Color(0.66f, 0.65f, 0.62f), 0.12f);
            var worn = Flat("Concrete_Block_Worn", new Color(0.57f, 0.56f, 0.53f), 0.1f);
            var pocket = Flat("Concrete_Block_Pocket", new Color(0.36f, 0.35f, 0.33f), 0.05f);
            var rebar = ColorMat("Rebar_Loop", new Color(0.38f, 0.24f, 0.16f), 0.55f, 0.3f);
            var grip = new PhysicsMaterial("Concrete grip") { dynamicFriction = 0.75f, staticFriction = 0.9f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Maximum };
            EnsureFolder(Gen + "/Physics");
            string gp = $"{Gen}/Physics/ConcreteGrip.physicMaterial";
            AssetDatabase.DeleteAsset(gp); AssetDatabase.CreateAsset(grip, gp);

            var kinds = ConcreteBlock.Kinds;
            var prefabs = new GameObject[kinds.Length];
            for (int k = 0; k < kinds.Length; k++)
            {
                var kd = kinds[k];
                Vector3 s = kd.size;
                var go = new GameObject("Concrete " + kd.name.Replace("×", "x"));
                var g = new Geo();
                // the body, with a slight bevel round the top edge (a chamfered form)
                float bev = Mathf.Min(0.03f, s.y * 0.08f);
                g.Box(concrete, new Vector3(0f, (s.y - bev) / 2f, 0f), new Vector3(s.x, s.y - bev, s.z));
                g.Box(concrete, new Vector3(0f, s.y - bev / 2f, 0f), new Vector3(s.x - bev * 2f, bev, s.z - bev * 2f));
                // form seams down the sides and the darker, rougher bottom
                g.Box(worn, new Vector3(0f, 0.025f, 0f), new Vector3(s.x + 0.004f, 0.05f, s.z + 0.004f));
                for (int side = -1; side <= 1; side += 2)
                    g.Box(worn, new Vector3(side * (s.x / 2f + 0.002f), s.y * 0.5f, 0f), new Vector3(0.006f, s.y * 0.86f, 0.018f));
                bool cap = k == 4;
                // interlocking knobs on top (they sit in the recesses under the block above); none on a cap block
                if (!cap)
                {
                    int knobs = s.z > 1.0f ? 2 : 1;
                    for (int i = 0; i < knobs; i++)
                    {
                        float z = knobs == 1 ? 0f : (i == 0 ? -1f : 1f) * s.z * 0.27f;
                        g.Cyl(worn, new Vector3(0f, s.y - 0.005f, z), Quaternion.identity, s.x * 0.24f, s.x * 0.2f, 0.035f, 12);
                        // the matching recess underneath, drawn as a dark ring
                        g.Cyl(pocket, new Vector3(0f, -0.001f, z), Quaternion.identity, s.x * 0.25f, s.x * 0.25f, 0.002f, 12);
                    }
                }
                // the cast-in lifting loop: a rebar hoop standing in a pocket in the middle of the top
                const float pz = 0f;
                float ph = Mathf.Min(0.09f, s.y * 0.3f);
                g.Box(pocket, new Vector3(0f, s.y - 0.004f, pz), new Vector3(0.2f, 0.01f, 0.2f));
                float lh = 0.07f, lw = 0.1f, rr = 0.012f;
                g.Box(rebar, new Vector3(-lw / 2f, s.y - ph / 2f + lh / 2f, pz), new Vector3(rr * 2f, ph + lh, rr * 2f));
                g.Box(rebar, new Vector3(lw / 2f, s.y - ph / 2f + lh / 2f, pz), new Vector3(rr * 2f, ph + lh, rr * 2f));
                g.Box(rebar, new Vector3(0f, s.y + lh, pz), new Vector3(lw + rr * 2f, rr * 2f, rr * 2f));
                g.Attach(go.transform, "ConcreteBlock_" + k);
                var eye = Marker(go.transform, "Lifting Eye", new Vector3(0f, s.y + lh, pz));

                var bc = go.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, s.y / 2f, 0f); bc.size = s; bc.sharedMaterial = grip;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = kd.kg; rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.linearDamping = 0.05f; rb.angularDamping = 0.05f;
                var cb = go.AddComponent<ConcreteBlock>();
                cb.kind = k; cb.eye = eye;
                prefabs[k] = SavePrefab(go);
            }
            return prefabs;
        }

        /// <summary>The block yard: east of the landscape supply's pad (`supply` is that pad's centre).</summary>
        static void BuildBlockYard(Transform root, Vector3 supply, List<Rect> hard)
        {
            var prefabs = BuildBlockPrefabs();
            var yard = new GameObject("Block Yard").transform;
            yard.SetParent(root, false);
            yard.position = supply + new Vector3(32f, 0f, -1f);
            var padM = Flat("Yard_Concrete", new Color(0.6f, 0.59f, 0.56f), 0.1f);
            // the pad runs further south than the stock, for the drive-through lane along its south side
            const float padS = -16f, padN = 11f;
            var pad = Block(root, "Block Yard Pad", yard.position + new Vector3(0f, 0.07f - 0.15f, (padS + padN) * 0.5f), new Vector3(26f, 0.3f, padN - padS), padM, 3f);
            GameObjectUtility.SetStaticEditorFlags(pad, StaticEditorFlags.BatchingStatic);
            hard.Add(new Rect(yard.position.x - 13f, yard.position.z + padS, 26f, padN - padS));

            // the loading bay: a drive-through lane the whole length of the yard (along x), so a semi comes off the
            // end of West Ave along the south side of the supply pad, gets loaded, and drives on out the far side,
            // along the south edge of the concrete plant (west of the supply pad the ground climbs into the hills)
            var paint = Flat("Bay_Paint_Yellow", new Color(0.95f, 0.78f, 0.15f), 0.3f);
            const float laneZ = -12f, laneW = 6.5f;
            var bay = Marker(yard, "Loading Bay", new Vector3(0f, 0.07f, laneZ));
            bay.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Vector2 bs = new Vector2(laneW - 1f, 26f);
            Part(bay, "Bay Line L", new Vector3(-bs.x / 2f, 0.01f, 0f), new Vector3(0.12f, 0.02f, bs.y - 1f), paint);
            Part(bay, "Bay Line R", new Vector3(bs.x / 2f, 0.01f, 0f), new Vector3(0.12f, 0.02f, bs.y - 1f), paint);
            // arrows down the middle: in at the west end, out at the east
            foreach (float az in new[] { -8f, 0f, 8f })
            {
                Part(bay, "Bay Arrow", new Vector3(0f, 0.01f, az), new Vector3(0.18f, 0.02f, 2.2f), paint);
                foreach (int sd in new[] { -1, 1 })
                    Part(bay, "Bay Arrow", new Vector3(sd * 0.38f, 0.01f, az + 0.75f), new Vector3(0.16f, 0.02f, 1.1f), paint).transform.localRotation = Quaternion.Euler(0f, -sd * 40f, 0f);
            }
            // the lane carries on as a concrete road both ways: west to the end of West Ave, east past the concrete plant
            float laneWorldZ = yard.position.z + laneZ;
            float westX = supply.x + 7f, yardW = yard.position.x - 13f, yardE = yard.position.x + 13f, eastX = supply.x + 92f;
            var apronM = Flat("Yard_Concrete", new Color(0.6f, 0.59f, 0.56f), 0.1f);
            // (kept off the supply and plant pads: their edges are at z -11 and -12 from their middles)
            float northEdge = Mathf.Min(laneWorldZ + laneW * 0.5f, supply.z - 11.05f);
            float plantSouth = supply.z - 1f - 12.05f;
            void Apron(string n, float x0, float x1, float zTop)
            {
                float z0 = laneWorldZ - laneW * 0.5f;
                var a = Block(root, n, new Vector3((x0 + x1) * 0.5f, 0.07f - 0.15f, (z0 + zTop) * 0.5f), new Vector3(x1 - x0, 0.3f, zTop - z0), apronM, 3f);
                GameObjectUtility.SetStaticEditorFlags(a, StaticEditorFlags.BatchingStatic);
                hard.Add(new Rect(x0, z0 - 1f, x1 - x0, zTop - z0 + 2f));
            }
            Apron("Block Yard Lane West", westX, yardW, northEdge);
            float plantE = supply.x + 62f + 14f; // (the plant pad is 28 m across)
            Apron("Block Yard Lane East", yardE, plantE, Mathf.Min(laneWorldZ + laneW * 0.5f, plantSouth));
            Apron("Block Yard Lane East End", plantE, eastX, laneWorldZ + laneW * 0.5f);

            // stock on the yard: the blocks in rows, a few courses high (scenery: the bought ones come from prefabs)
            var stock = new GameObject("Block Stock (scenery)").transform; stock.SetParent(yard, false);
            var concrete = Flat("Concrete_Block", new Color(0.66f, 0.65f, 0.62f), 0.12f);
            var kinds = ConcreteBlock.Kinds;
            for (int k = 0; k < kinds.Length; k++)
            {
                Vector3 s = kinds[k].size;
                float x = 7.5f, z = -7.5f + k * 2.9f; // (north of the lane)
                int courses = k == 5 ? 2 : 3;
                for (int c = 0; c < courses; c++)
                    for (int j = 0; j < 2; j++)
                        Part(stock, $"Stock {k}", new Vector3(x + (j - 0.5f) * (s.z + 0.08f) + (c % 2) * 0.15f, 0.07f + s.y * (c + 0.5f), z), new Vector3(s.z, s.y, s.x), concrete, true);
            }
            // a short demonstration wall of lock blocks, stepped like a garden retaining wall
            for (int c = 0; c < 3; c++)
                for (int j = 0; j < 5 - c; j++)
                    Part(stock, "Demo Wall", new Vector3(-9f + j * 1.24f + c * 0.62f, 0.07f + 0.61f * (c + 0.5f), 9.2f - c * 0.15f), new Vector3(1.22f, 0.61f, 0.61f), concrete, true);

            var drop = Marker(yard, "Ground Drop", new Vector3(-2f, 0.07f, 6f));
            drop.localRotation = Quaternion.Euler(0f, 90f, 0f);

            // the yard office (where you buy)
            var office = new GameObject("Block Yard Office").transform;
            office.SetParent(yard, false);
            office.localPosition = new Vector3(10.5f, 0.07f, 9.0f);
            var wall = Flat("Office_Siding", new Color(0.78f, 0.74f, 0.62f), 0.15f);
            var roof = Flat("Office_Roof", new Color(0.32f, 0.36f, 0.4f), 0.3f);
            var glass = Flat("Office_Window", new Color(0.35f, 0.55f, 0.7f), 0.85f);
            Part(office, "Office Walls", new Vector3(0f, 1.3f, 0f), new Vector3(4f, 2.6f, 2.6f), wall, true);
            Part(office, "Office Roof", new Vector3(0f, 2.7f, 0f), new Vector3(4.4f, 0.2f, 3f), roof, true);
            Part(office, "Office Window", new Vector3(0f, 1.5f, -1.31f), new Vector3(1.8f, 0.9f, 0.04f), glass);
            Part(office, "Counter", new Vector3(0f, 1.0f, -1.55f), new Vector3(2.2f, 0.12f, 0.5f), roof, true);
            var by = office.gameObject.AddComponent<BlockYard>();
            by.prefabs = prefabs;
            by.bay = bay; by.baySize = new Vector2(laneW, bs.y); // (the whole lane counts, not just between the lines)
            by.groundDrop = drop;
            MapMark(office, "Block yard", new Color(0.75f, 0.75f, 0.72f), new Vector3(0f, 0f, -4f));
        }
    }
}
