using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine.LowLevel;

namespace FPSBase.DebugTools
{
    /// <summary>
    /// For the tests and the perf line: times every step of Unity's frame (physics, each script phase, the
    /// render submit, ...) by putting a stopwatch mark after each one in the player loop. Works in a normal
    /// (non-development) build, where the profiler's markers aren't there. Off until <see cref="Install"/>.
    /// </summary>
    public static class LoopTimer
    {
        static readonly List<string> names = new();
        static double[] ms = new double[0];
        static long last;
        static bool installed;
        static readonly double toMs = 1000.0 / Stopwatch.Frequency;

        public static bool Installed => installed;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            var root = PlayerLoop.GetCurrentPlayerLoop();
            var top = root.subSystemList;
            for (int i = 0; i < top.Length; i++)
            {
                var subs = top[i].subSystemList;
                if (subs == null || subs.Length == 0) continue;
                string tn = top[i].type != null ? top[i].type.Name : "?";
                var list = new List<PlayerLoopSystem>();
                list.Add(Mark(-1, tn + ".start"));
                foreach (var s in subs)
                {
                    list.Add(s);
                    int k = names.Count;
                    names.Add(tn + "." + (s.type != null ? s.type.Name : "?"));
                    list.Add(Mark(k, null));
                }
                top[i].subSystemList = list.ToArray();
            }
            ms = new double[names.Count];
            root.subSystemList = top;
            PlayerLoop.SetPlayerLoop(root);
        }

        static PlayerLoopSystem Mark(int k, string _)
        {
            return new PlayerLoopSystem
            {
                type = typeof(LoopTimer),
                updateDelegate = k < 0
                    ? (PlayerLoopSystem.UpdateFunction)(() => last = Stopwatch.GetTimestamp())
                    : () => { long now = Stopwatch.GetTimestamp(); ms[k] += (now - last) * toMs; last = now; },
            };
        }

        /// <summary>Totals so far (copy), to subtract a later reading from.</summary>
        public static double[] Snapshot() => (double[])ms.Clone();

        /// <summary>The `n` heaviest steps since `from`, in ms a frame over `frames`.</summary>
        public static string Top(double[] from, int frames, int n = 10)
        {
            if (!installed || frames <= 0) return "(loop timer off)";
            var idx = new List<int>();
            for (int i = 0; i < ms.Length; i++) idx.Add(i);
            double D(int i) => ms[i] - (from != null && i < from.Length ? from[i] : 0);
            idx.Sort((a, b) => D(b).CompareTo(D(a)));
            var sb = new StringBuilder();
            double total = 0; for (int i = 0; i < ms.Length; i++) total += D(i);
            sb.Append($"total {total / frames:0.0} ms a frame: ");
            for (int j = 0; j < n && j < idx.Count; j++)
            {
                if (j > 0) sb.Append(", ");
                sb.Append($"{names[idx[j]]} {D(idx[j]) / frames:0.00}");
            }
            return sb.ToString();
        }
    }
}
