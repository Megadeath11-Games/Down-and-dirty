using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>
    /// Where the dirt is: a bucket's teeth scraping through the ground, and loads pouring out of buckets and
    /// truck beds. A few looping sounds that go to wherever it's happening (louder the more is moving) and fade
    /// when it stops. Works for friends' machines too (their pours are drawn here, and so heard).
    /// </summary>
    public class WorkSounds : MonoBehaviour
    {
        const int PerKind = 4;
        class Emitter { public AudioSource s; public float level, target; public Vector3 at; }
        Emitter[] dig, pour;
        static WorkSounds inst;
        static bool failed;

        static WorkSounds Get()
        {
            if (inst != null || failed) return inst;
            var d = Resources.Load<AudioClip>("Audio/SFX/dig_scrape");
            var p = Resources.Load<AudioClip>("Audio/SFX/pour");
            if (d == null || p == null) { failed = true; return null; }
            var go = new GameObject("Work Sounds");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<WorkSounds>();
            inst.dig = inst.Make(d, "Dig");
            inst.pour = inst.Make(p, "Pour");
            return inst;
        }

        Emitter[] Make(AudioClip c, string name)
        {
            var e = new Emitter[PerKind];
            for (int i = 0; i < PerKind; i++)
            {
                var go = new GameObject(name + " " + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.clip = c; s.loop = true; s.playOnAwake = false; s.spatialBlend = 1f; s.dopplerLevel = 0f;
                s.minDistance = 4f; s.maxDistance = 70f; s.rolloffMode = AudioRolloffMode.Linear; s.volume = 0f;
                s.time = Random.Range(0f, c.length * 0.9f);
                e[i] = new Emitter { s = s };
            }
            return e;
        }

        /// <summary>The bucket's teeth bit into the ground here (called as it digs).</summary>
        public static void Dig(Vector3 at, float amount = 1f) => Get()?.Bump(inst.dig, at, amount);

        /// <summary>`volume` m³ of dirt is pouring from here (called as it pours).</summary>
        public static void Pour(Vector3 at, float volume) => Get()?.Bump(inst.pour, at, Mathf.Clamp01(0.35f + volume * 6f));

        void Bump(Emitter[] set, Vector3 at, float amount)
        {
            if (Application.isBatchMode) return;
            // the one already sounding nearby, else the quietest
            Emitter best = null; float bestD = 9f;
            foreach (var e in set) { float d = (e.at - at).sqrMagnitude; if (e.level > 0.01f && d < bestD) { best = e; bestD = d; } }
            if (best == null) { best = set[0]; foreach (var e in set) if (e.level < best.level) best = e; best.at = at; }
            best.at = Vector3.Lerp(best.at, at, 0.5f);
            best.target = Mathf.Max(best.target, amount);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Step(dig, dt, 0.5f, 0.3f);
            Step(pour, dt, 0.55f, 0.45f);
        }

        void Step(Emitter[] set, float dt, float loud, float hold)
        {
            float fx = FPSBase.Core.GameSettings.EffectsVolume;
            foreach (var e in set)
            {
                e.level = Mathf.MoveTowards(e.level, e.target, dt * (e.target > e.level ? 8f : 2.5f));
                e.target = Mathf.MoveTowards(e.target, 0f, dt / hold);
                e.s.transform.position = e.at;
                e.s.volume = e.level * loud * fx;
                e.s.pitch = Mathf.Lerp(0.9f, 1.08f, e.level);
                if (e.level > 0.01f && !e.s.isPlaying) e.s.Play();
                else if (e.level <= 0.01f && e.s.isPlaying) e.s.Stop();
            }
        }
    }
}
