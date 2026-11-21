using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace FPSBase.DebugTools
{
    /// <summary>
    /// Frame-time breakdown for the automated runs: every few seconds, logs the frame rate, the worst frame and
    /// where the time went (the game's own counters for clods and ground, and in a development build Unity's own
    /// markers: physics, each script's updates, rendering, plus draw-call counts). Off unless something adds it.
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        public string label = "perf";
        public float every = 3f;

        // Unity markers and counters looked for by name
        const string Pattern = @"^(PlayerLoop|FixedUpdate\.PhysicsFixedUpdate|BehaviourUpdate|LateBehaviourUpdate|FixedBehaviourUpdate|PostLateUpdate\.FinishFrameRendering|Physics\.Simulate|Physics\.Raycast|Physics\.OverlapSphere|GC\.Collect)$" +
                               @"|\.(Update|LateUpdate|FixedUpdate|OnGUI)\(\)|Cull|Shadow|HDRenderPipeline|RenderLoop|ScriptableRenderContext|Batch|Count$|GC Allocated In Frame";
        readonly List<(string name, ProfilerRecorder rec, bool count)> recs = new();
        double[] sums = new double[0];
        static bool listed;
        int frames; float t0, worst, last;
        double g0, gc0, gd0, gp0, r0, x0, p0, c0;

        void OnEnable()
        {
            var all = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(all);
            var seen = new HashSet<string>();
            var re = new System.Text.RegularExpressions.Regex(Pattern);
            foreach (var h in all)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (!seen.Add(d.Name) || !re.IsMatch(d.Name)) continue;
                bool isCount = d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds;
                var r = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
                r.Start();
                recs.Add((d.Name, r, isCount));
            }
            sums = new double[recs.Count];
            if (!listed) { listed = true; Debug.Log($"[Perf] recording {recs.Count} markers and counters"); }
            ResetWindow();
        }

        void OnDisable() { foreach (var r in recs) r.rec.Dispose(); recs.Clear(); }

        void ResetWindow()
        {
            frames = 0; worst = 0f; t0 = last = Time.realtimeSinceStartup;
            System.Array.Clear(sums, 0, sums.Length);
            g0 = Mining.DirtGrains.StepMs; gc0 = Mining.DirtGrains.CollideMs; gd0 = Mining.DirtGrains.DrawMs; gp0 = Mining.DirtGrains.PushersMs;
            r0 = Mining.DigSite.RebuildMs; x0 = Mining.DigSite.RelaxMs; p0 = Mining.DigSite.PushMs; c0 = Mining.DigSite.ColliderMs;
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (frames > 0) worst = Mathf.Max(worst, now - last);
            last = now; frames++;
            for (int i = 0; i < recs.Count; i++) if (recs[i].rec.Valid) sums[i] += recs[i].rec.LastValue;
            if (now - t0 < every) return;
            double f = frames;
            var sb = new System.Text.StringBuilder();
            sb.Append($"[Perf] {label}: {frames / (now - t0):0} fps, worst frame {worst * 1000f:0} ms, clods {Mining.DirtGrains.Live}, cameras [");
            foreach (var c in Camera.allCameras) sb.Append(c.name).Append(c.targetTexture != null ? "(rt) " : " ");
            sb.Append("]; ");
            sb.Append($"clods {(Mining.DirtGrains.StepMs - g0) / f:0.00} ms (bump {(Mining.DirtGrains.CollideMs - gc0) / f:0.00}, blades {(Mining.DirtGrains.PushersMs - gp0) / f:0.00}) draw {(Mining.DirtGrains.DrawMs - gd0) / f:0.00}, ");
            sb.Append($"ground redraw {(Mining.DigSite.RebuildMs - r0) / f:0.00}, slump {(Mining.DigSite.RelaxMs - x0) / f:0.00}, blade {(Mining.DigSite.PushMs - p0) / f:0.00}, collider {(Mining.DigSite.ColliderMs - c0) / f:0.00}");
            var ft = new FrameTiming[1]; FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0) sb.Append($"; main {ft[0].cpuMainThreadFrameTime:0.0} render {ft[0].cpuRenderThreadFrameTime:0.0} gpu {ft[0].gpuFrameTime:0.0}");
            // the biggest timings, then the counts
            var top = new List<(double v, string n)>();
            for (int i = 0; i < recs.Count; i++) if (!recs[i].count && sums[i] / f > 0.15e6) top.Add((sums[i] / f * 1e-6, recs[i].name));
            top.Sort((a, b) => b.v.CompareTo(a.v));
            sb.Append(" ||");
            for (int i = 0; i < Mathf.Min(28, top.Count); i++) sb.Append($" {top[i].n} {top[i].v:0.00};");
            sb.Append(" ||");
            for (int i = 0; i < recs.Count; i++)
                if (recs[i].count && sums[i] > 0)
                    sb.Append(recs[i].name.StartsWith("GC") ? $" {recs[i].name} {sums[i] / f / 1024.0:0.0} KB;" : $" {recs[i].name} {sums[i] / f:0};");
            Debug.Log(sb.ToString());
            if (!censused) Census();
            ResetWindow();
        }

        static bool censused;

        /// <summary>Once: which objects make the draw calls (renderers and their sub-meshes, by top-level object and by material).</summary>
        static void Census()
        {
            censused = true;
            var byRoot = new Dictionary<string, (int r, int sub, int shadow)>();
            var byMat = new Dictionary<string, int>();
            int total = 0, subs = 0;
            foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!mr.enabled) continue;
                var mf = mr.GetComponent<MeshFilter>();
                int n = mf != null && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 1;
                string root = mr.transform.root.name;
                if (root == "Town") { var mb = mr.GetComponentInParent<MonoBehaviour>(); root = "Town/" + mr.name + (mb != null ? $" ({mb.GetType().Name})" : ""); }
                if (root.Length > 40) root = root.Substring(0, 40);
                byRoot.TryGetValue(root, out var e);
                byRoot[root] = (e.r + 1, e.sub + n, e.shadow + (mr.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off ? n : 0));
                foreach (var m in mr.sharedMaterials) { string k = m != null ? m.shader.name + "|" + m.name : "null"; byMat.TryGetValue(k, out int c); byMat[k] = c + 1; }
                total++; subs += n;
            }
            var list = new List<KeyValuePair<string, (int r, int sub, int shadow)>>(byRoot);
            list.Sort((a, b) => b.Value.sub.CompareTo(a.Value.sub));
            var sb = new System.Text.StringBuilder($"[Perf] census: {total} mesh renderers, {subs} sub-meshes, {FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Length} skinned, {FindObjectsByType<Terrain>(FindObjectsSortMode.None).Length} terrains; by object:");
            for (int i = 0; i < Mathf.Min(25, list.Count); i++) sb.Append($" {list[i].Key} {list[i].Value.r}r/{list[i].Value.sub}s/{list[i].Value.shadow}sh;");
            var ml = new List<KeyValuePair<string, int>>(byMat);
            ml.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.Append($" || {ml.Count} materials, most used:");
            for (int i = 0; i < Mathf.Min(15, ml.Count); i++) sb.Append($" {ml[i].Key} {ml[i].Value};");
            Debug.Log(sb.ToString());
        }
    }
}
