using System.IO;
using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>Reading and writing machine state for multiplayer.</summary>
    public static class NetIO
    {
        public static void Write(BinaryWriter w, MaterialLoad l)
        {
            w.Write(l.volume); w.Write(l.goldGrams); w.Write(l.sand); w.Write(l.gravel); w.Write(l.clay); w.Write(l.rock);
        }

        public static MaterialLoad ReadLoad(BinaryReader r) => new MaterialLoad
        {
            volume = r.ReadSingle(), goldGrams = r.ReadSingle(), sand = r.ReadSingle(),
            gravel = r.ReadSingle(), clay = r.ReadSingle(), rock = r.ReadSingle(),
        };

        public static void Write(BinaryWriter w, CabInput c)
        {
            w.Write(c.left.x); w.Write(c.left.y); w.Write(c.right.x); w.Write(c.right.y);
            w.Write(c.leverL); w.Write(c.leverR); w.Write(c.aux); w.Write(c.pedalL); w.Write(c.pedalR); w.Write(c.wheel);
        }

        public static CabInput ReadCab(BinaryReader r) => new CabInput
        {
            left = new Vector2(r.ReadSingle(), r.ReadSingle()), right = new Vector2(r.ReadSingle(), r.ReadSingle()),
            leverL = r.ReadSingle(), leverR = r.ReadSingle(), aux = r.ReadSingle(),
            pedalL = r.ReadSingle(), pedalR = r.ReadSingle(), wheel = r.ReadSingle(),
        };

        /// <summary>Ease a value toward what the network last said (state arrives ~15 times a second).</summary>
        public static float Ease(float cur, float target, float dt) => Mathf.Lerp(cur, target, 1f - Mathf.Exp(-14f * dt));
    }
}
