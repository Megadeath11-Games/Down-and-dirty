using FPSBase.Mining;
using FPSBase.Net;
using FPSBase.Vehicles;
using UnityEngine;

namespace FPSBase.Core
{
    /// <summary>
    /// Writes the game's stutters to the log (Player.log): every frame that takes over 50 ms gets a line saying what
    /// went on in it (ground redrawn, physics steps, a memory clean-up, network messages), and every 30 seconds a
    /// summary of the frame rate. So a "it's laggy" can be pinned on what's really slow on that computer.
    /// </summary>
    public class LagLog : MonoBehaviour
    {
        static LagLog inst;
        const float HitchMs = 50f, SummaryEvery = 30f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst != null || Application.isBatchMode) return;
            var go = new GameObject("Lag Log");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<LagLog>();
        }

        double rb0, co0, up0, de0, rl0, ns0, nr0; long out0, in0; int gc0, gc1_0, rbn0; float fixed0;
        float sumTimer, lastLine; int frames, hitches, gcs; float worst, total; long sumOut, sumIn;

        void Snap()
        {
            rb0 = DigSite.RebuildMs; co0 = DigSite.ColliderMs; up0 = DigSite.UploadMs; de0 = DigSite.DecorMs; rl0 = DigSite.RelaxMs; rbn0 = DigSite.Rebuilds;
            ns0 = NetSession.TerrainSendMs; nr0 = NetSession.TerrainReadMs; out0 = NetSession.BytesOut; in0 = NetSession.BytesIn;
            gc0 = System.GC.CollectionCount(0); gc1_0 = System.GC.CollectionCount(1); fixed0 = Time.fixedTime;
        }

        void Start() => Snap();

        void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            frames++; total += ms; worst = Mathf.Max(worst, ms);
            int gcNow = System.GC.CollectionCount(0);
            if (gcNow != gc0) gcs++;
            sumOut += NetSession.BytesOut - out0; sumIn += NetSession.BytesIn - in0;
            if (ms > HitchMs && Time.unscaledTime > 20f)
            {
                hitches++;
                // (at most a few lines a second: a slow patch shouldn't flood the log and slow it further)
                if (Time.unscaledTime - lastLine > 0.25f)
                {
                    lastLine = Time.unscaledTime;
                    int steps = Mathf.RoundToInt((Time.fixedTime - fixed0) / Mathf.Max(1e-4f, Time.fixedDeltaTime));
                    var me = VehicleBase.LastDriven != null && VehicleBase.LastDriven.Occupied ? VehicleBase.LastDriven : null;
                    Debug.Log($"[Lag] {ms:0} ms frame: ground redrawn {DigSite.Rebuilds - rbn0} pieces {DigSite.RebuildMs - rb0:0.0} ms, collider {DigSite.ColliderMs - co0:0.0} ms, upload {DigSite.UploadMs - up0:0.0} ms, decor {DigSite.DecorMs - de0:0.0} ms, settling {DigSite.RelaxMs - rl0:0.0} ms; "
                        + $"{steps} physics steps; memory clean-up {(gcNow != gc0 ? (System.GC.CollectionCount(1) != gc1_0 ? "YES (big)" : "yes") : "no")}; "
                        + $"network: ground out {NetSession.TerrainSendMs - ns0:0.0} ms / in {NetSession.TerrainReadMs - nr0:0.0} ms, {(NetSession.BytesOut - out0) / 1024f:0.0} KB out, {(NetSession.BytesIn - in0) / 1024f:0.0} KB in, {NetSession.Backlog} waiting; "
                        + $"in {(me != null ? me.displayName : "nothing (on foot)")}, {DirtGrains.Live} clods");
                }
            }
            if ((sumTimer += Time.unscaledDeltaTime) >= SummaryEvery)
            {
                if (Time.unscaledTime > 20f)
                    Debug.Log($"[Lag] last {sumTimer:0} s: {frames / sumTimer:0} fps average, worst frame {worst:0} ms, {hitches} frames over {HitchMs:0} ms, {gcs} memory clean-ups, network {sumOut / 1024f / sumTimer:0.0} KB/s out, {sumIn / 1024f / sumTimer:0.0} KB/s in{(NetSession.InSession ? (NetSession.Instance.IsHost ? " (hosting)" : " (joined)") : "")}");
                sumTimer = 0f; frames = 0; hitches = 0; gcs = 0; worst = 0f; total = 0f; sumOut = 0; sumIn = 0;
            }
            Snap();
        }
    }
}
