using System.Collections.Generic;
using FPSBase.City;
using UnityEditor;
using UnityEngine;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// The concrete batch plant east of the block yard: a pad with a drive-through lane under the batching tower's
    /// loading chute, two cement silos, aggregate bays with a conveyor up to the tower, a water tank and the
    /// control house where you order.
    /// </summary>
    public static partial class ProjectBootstrap
    {
        static void BuildConcretePlant(Transform root, Vector3 at, List<Rect> hard)
        {
            var plant = new GameObject("Concrete Plant").transform;
            plant.SetParent(root, false);
            plant.position = at;
            var padM = Flat("Plant_Concrete", new Color(0.58f, 0.57f, 0.54f), 0.1f);
            Vector2 ps = new Vector2(28f, 24f);
            var pad = Block(root, "Concrete Plant Pad", at + new Vector3(0f, 0.07f - 0.15f, 0f), new Vector3(ps.x, 0.3f, ps.y), padM, 3f);
            GameObjectUtility.SetStaticEditorFlags(pad, StaticEditorFlags.BatchingStatic);
            hard.Add(new Rect(at.x - ps.x / 2f, at.z - ps.y / 2f, ps.x, ps.y));

            var steel = Flat("Plant_Steel", new Color(0.5f, 0.52f, 0.54f), 0.35f, 0.5f);
            var dark = Flat("Plant_Dark", new Color(0.2f, 0.21f, 0.22f), 0.25f, 0.3f);
            var blue = Flat("Plant_Blue", new Color(0.16f, 0.36f, 0.62f), 0.35f);
            var white = Flat("Plant_White", new Color(0.86f, 0.86f, 0.84f), 0.25f);
            var yellow = Flat("Bay_Paint_Yellow", new Color(0.95f, 0.78f, 0.15f), 0.3f);
            var block = Flat("Concrete_Block", new Color(0.66f, 0.65f, 0.62f), 0.12f);
            var belt = Flat("Conveyor_Belt", new Color(0.12f, 0.12f, 0.12f), 0.2f);
            var sand = Flat("Field_Sand", new Color(0.83f, 0.71f, 0.47f));
            var gravel = Flat("Field_Gravel", new Color(0.55f, 0.52f, 0.47f), 0.12f);

            const float Y = 0.07f, laneX = -5f;
            var g = new Geo();
            // --- the batching tower over the lane: four legs, the weigh hoppers and mixer house, the loading chute
            foreach (float lx in new[] { laneX - 2.3f, laneX + 2.3f })
                foreach (float lz in new[] { -2.3f, 2.3f })
                {
                    g.Box(steel, new Vector3(lx, Y + 3.6f, lz), new Vector3(0.32f, 7.2f, 0.32f));
                    ColBox(plant, "Tower Leg", new Vector3(lx, Y + 3.6f, lz), new Vector3(0.32f, 7.2f, 0.32f));
                    g.Box(yellow, new Vector3(lx, Y + 0.6f, lz), new Vector3(0.36f, 1.2f, 0.36f)); // bump guards
                }
            foreach (float lz in new[] { -2.3f, 2.3f }) // cross bracing
            {
                Rail(g, steel, new Vector3(laneX - 2.3f, Y + 5.4f, lz), new Vector3(laneX + 2.3f, Y + 7.1f, lz), 0.06f);
                Rail(g, steel, new Vector3(laneX + 2.3f, Y + 5.4f, lz), new Vector3(laneX - 2.3f, Y + 7.1f, lz), 0.06f);
            }
            g.Box(blue, new Vector3(laneX, Y + 6.4f, 0f), new Vector3(5f, 1.8f, 5f));
            ColBox(plant, "Tower Box", new Vector3(laneX, Y + 6.4f, 0f), new Vector3(5f, 1.8f, 5f));
            g.Box(white, new Vector3(laneX, Y + 8.4f, 0f), new Vector3(4.6f, 2.2f, 4.6f)); // mixer house
            g.Box(dark, new Vector3(laneX, Y + 9.6f, 0f), new Vector3(5f, 0.2f, 5f));
            for (int i = 0; i < 4; i++) g.Box(dark, new Vector3(laneX - 2.31f, Y + 7.6f + i * 0.5f, 0f), new Vector3(0.02f, 0.06f, 4.4f)); // cladding ribs
            g.Box(white, new Vector3(laneX + 2.51f, Y + 6.4f, 0f), new Vector3(0.02f, 1.1f, 4f)); // name board
            g.Box(blue, new Vector3(laneX + 2.53f, Y + 6.4f, 0f), new Vector3(0.02f, 0.5f, 3.4f));
            g.Cyl(steel, new Vector3(laneX, Y + 5.05f, 0f), Quaternion.identity, 0.34f, 1.4f, 0.9f, 18); // batcher cone
            g.Cyl(dark, new Vector3(laneX, Y + 4.72f, 0f), Quaternion.identity, 0.3f, 0.3f, 0.3f, 14);  // loading spout
            g.Cyl(belt, new Vector3(laneX, Y + 4.5f, 0f), Quaternion.identity, 0.33f, 0.33f, 0.16f, 14); // rubber boot
            var chute = Marker(plant, "Loading Chute", new Vector3(laneX, Y + 4.4f, 0f));
            // a stair up the side to the mixer house
            Vector3 s0 = new Vector3(laneX + 3.2f, Y, -4.2f), s1 = new Vector3(laneX + 3.2f, Y + 7.3f, 1.6f);
            foreach (float o in new[] { -0.45f, 0.45f }) Rail(g, steel, s0 + Vector3.right * o, s1 + Vector3.right * o, 0.05f);
            for (int i = 1; i < 22; i++) g.Box(dark, Vector3.Lerp(s0, s1, i / 22f), new Vector3(0.9f, 0.04f, 0.25f));
            g.Box(dark, new Vector3(laneX + 2.85f, Y + 7.3f, 0f), new Vector3(1.1f, 0.06f, 4.8f)); // landing
            Rail(g, yellow, new Vector3(laneX + 3.4f, Y + 8.3f, -2.4f), new Vector3(laneX + 3.4f, Y + 8.3f, 2.4f), 0.04f);

            // --- the lane: painted lines, a stop bar where the hopper goes, a sign
            foreach (int sd in new[] { -1, 1 })
                g.Box(yellow, new Vector3(laneX + sd * 1.9f, Y + 0.01f, 0f), new Vector3(0.14f, 0.02f, ps.y - 2f));
            g.Box(white, new Vector3(laneX, Y + 0.01f, 0f), new Vector3(3.4f, 0.02f, 0.25f));

            // --- two cement silos with screw conveyors up into the tower
            foreach (float sz in new[] { -3.6f, 3.6f })
            {
                Vector3 c = new Vector3(3.2f, 0f, sz);
                g.Cyl(white, c + new Vector3(0f, Y + 8f, 0f), Quaternion.identity, 1.55f, 1.55f, 9f, 22);
                g.Cyl(white, c + new Vector3(0f, Y + 12.7f, 0f), Quaternion.identity, 1.55f, 0.5f, 0.6f, 22);
                g.Cyl(steel, c + new Vector3(0f, Y + 2.7f, 0f), Quaternion.identity, 0.3f, 1.55f, 1.6f, 22);
                g.Cyl(dark, c + new Vector3(0f, Y + 7f, 0f), Quaternion.identity, 1.57f, 1.57f, 0.12f, 22);
                foreach (float lx in new[] { -1.1f, 1.1f })
                    foreach (float lz in new[] { -1.1f, 1.1f })
                        g.Box(steel, c + new Vector3(lx, Y + 1.9f, lz), new Vector3(0.22f, 3.8f, 0.22f));
                ColBox(plant, "Silo", c + new Vector3(0f, Y + 6f, 0f), new Vector3(3.1f, 12f, 3.1f));
                Rail(g, steel, c + new Vector3(-0.3f, Y + 2.2f, 0f), new Vector3(laneX + 2.4f, Y + 6.9f, sz * 0.4f), 0.16f);
                Rail(g, yellow, c + new Vector3(0f, Y + 13.1f, 0f), c + new Vector3(0f, Y + 13.1f, sz > 0f ? -1.4f : 1.4f), 0.04f); // top rail
            }
            // --- aggregate bays (block walls) with sand and gravel, a feed hopper and the conveyor up to the tower
            for (int i = 0; i < 4; i++)
                g.Box(block, new Vector3(10f + i * 0f, Y + 0.9f, -6f + i * 4f), new Vector3(5f, 1.8f, 0.6f));
            g.Box(block, new Vector3(12.8f, Y + 0.9f, 0f), new Vector3(0.6f, 1.8f, 12.6f));
            g.Box(sand, new Vector3(10.3f, Y + 0.55f, -4f), new Vector3(4.2f, 1.1f, 3.3f));
            g.Box(gravel, new Vector3(10.3f, Y + 0.6f, 0f), new Vector3(4.2f, 1.2f, 3.3f));
            g.Box(gravel, new Vector3(10.3f, Y + 0.5f, 4f), new Vector3(4.2f, 1.0f, 3.3f));
            ColBox(plant, "Aggregate Bays", new Vector3(10.2f, Y + 0.9f, 0f), new Vector3(5.6f, 1.8f, 12.6f));
            Vector3 c0 = new Vector3(7f, Y + 1.6f, 8.2f), c1 = new Vector3(laneX + 1.2f, Y + 7.4f, 2.2f);
            g.Box(steel, new Vector3(7f, Y + 1.2f, 8.2f), new Vector3(2.4f, 1.4f, 2.4f)); // feed hopper
            g.Cyl(steel, new Vector3(7f, Y + 2.2f, 8.2f), Quaternion.identity, 1.2f, 1.6f, 0.6f, 4);
            ColBox(plant, "Feed Hopper", new Vector3(7f, Y + 1.2f, 8.2f), new Vector3(2.4f, 2.4f, 2.4f));
            Rail(g, belt, c0, c1, 0.32f);
            Rail(g, steel, c0 + Vector3.down * 0.4f, c1 + Vector3.down * 0.4f, 0.12f);
            for (int i = 1; i < 6; i++)
            {
                Vector3 q = Vector3.Lerp(c0, c1, i / 6f);
                if (q.y > Y + 2.4f) g.Box(steel, new Vector3(q.x, (q.y + Y) / 2f, q.z), new Vector3(0.14f, q.y - Y, 0.14f));
            }
            // --- a water tank on a stand
            Vector3 wt = new Vector3(-11.5f, 0f, -8f);
            g.Cyl(blue, wt + new Vector3(0f, Y + 4.4f, 0f), Quaternion.identity, 1.3f, 1.3f, 3.2f, 18);
            foreach (float lx in new[] { -0.9f, 0.9f })
                foreach (float lz in new[] { -0.9f, 0.9f })
                    g.Box(steel, wt + new Vector3(lx, Y + 1.4f, lz), new Vector3(0.18f, 2.8f, 0.18f));
            ColBox(plant, "Water Tank", wt + new Vector3(0f, Y + 3f, 0f), new Vector3(2.6f, 6f, 2.6f));
            g.Attach(plant, "ConcretePlant");

            // --- the control house where you order (and its window onto the lane)
            var office = new GameObject("Concrete Plant Control House").transform;
            office.SetParent(plant, false);
            office.localPosition = new Vector3(5.5f, Y, -8.4f); // (clear of the block yard's drive-through lane along the pad's south edge)
            var wall = Flat("Office_Siding", new Color(0.78f, 0.74f, 0.62f), 0.15f);
            var roof = Flat("Office_Roof", new Color(0.32f, 0.36f, 0.4f), 0.3f);
            var glass = Flat("Office_Window", new Color(0.35f, 0.55f, 0.7f), 0.85f);
            Part(office, "Control House Walls", new Vector3(0f, 1.3f, 0f), new Vector3(4.2f, 2.6f, 2.8f), wall, true);
            Part(office, "Control House Roof", new Vector3(0f, 2.7f, 0f), new Vector3(4.6f, 0.2f, 3.2f), roof, true);
            Part(office, "Control House Window", new Vector3(-2.11f, 1.6f, 0f), new Vector3(0.04f, 0.9f, 2f), glass);
            Part(office, "Control House Window Front", new Vector3(0f, 1.6f, 1.41f), new Vector3(1.6f, 0.8f, 0.04f), glass);
            Part(office, "Order Counter", new Vector3(0f, 1.0f, 1.65f), new Vector3(2.0f, 0.12f, 0.5f), roof, true);
            var cp = office.gameObject.AddComponent<ConcretePlant>();
            cp.chute = chute;
            cp.concreteMaterial = WetConcrete();
            MapMark(office, "Concrete plant", new Color(0.62f, 0.66f, 0.72f), new Vector3(0f, 0f, 4f));
        }
    }
}
