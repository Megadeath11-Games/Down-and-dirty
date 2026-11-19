using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// A machine's sound: the diesel (an idle loop and a working loop, crossfaded and pitched with the revs),
    /// the hydraulic pump whining while the rams move, a strained groan when they push hard (digging), the blade
    /// scraping the ground, and the tracks clanking. Bigger machines sound lower. Clips are in Resources/Audio/SFX
    /// (made for the game); if they're missing, a built-in diesel loop is used. Quiet when nobody is in the seat.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EngineAudio : MonoBehaviour
    {
        public float basePitch = 1f;
        float rpm, hyd, strain, scrape;
        bool tracked;
        AudioSource src, high, whine, groan, scraper, clank;
        static AudioClip clip, idleC, highC, whineC, strainC, scrapeC, clankC;
        static bool loaded;

        public static EngineAudio Attach(VehicleBase v)
        {
            var e = v.gameObject.AddComponent<EngineAudio>();
            var rb = v.GetComponent<Rigidbody>();
            float t = rb != null ? rb.mass / 1000f : 5f;
            e.basePitch = Mathf.Clamp(1.35f - Mathf.Log10(Mathf.Max(1f, t)) * 0.38f, 0.6f, 1.35f);
            e.tracked = v.GetComponentInChildren<TrackAnimator>(true) != null;
            if (v.vehicleCamera != null && v.vehicleCamera.GetComponentInChildren<AudioListener>(true) == null)
                v.vehicleCamera.gameObject.AddComponent<AudioListener>();
            return e;
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            idleC = Resources.Load<AudioClip>("Audio/SFX/diesel_idle");
            highC = Resources.Load<AudioClip>("Audio/SFX/diesel_high");
            whineC = Resources.Load<AudioClip>("Audio/SFX/hydraulic_whine");
            strainC = Resources.Load<AudioClip>("Audio/SFX/hydraulic_strain");
            scrapeC = Resources.Load<AudioClip>("Audio/SFX/dig_scrape");
            clankC = Resources.Load<AudioClip>("Audio/SFX/track_clank");
        }

        void Awake()
        {
            Load();
            src = GetComponent<AudioSource>();
            Setup(src, idleC != null ? idleC : Clip());
            if (highC != null) high = Layer("Engine (working)", highC);
            if (whineC != null) whine = Layer("Hydraulics", whineC);
            if (strainC != null) groan = Layer("Hydraulics (strain)", strainC);
            if (scrapeC != null) scraper = Layer("Blade scrape", scrapeC);
            if (clankC != null) clank = Layer("Tracks", clankC);
        }

        AudioSource Layer(string name, AudioClip c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            Setup(s, c);
            return s;
        }

        static void Setup(AudioSource s, AudioClip c)
        {
            s.clip = c;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0.8f;
            s.minDistance = 5f;
            s.maxDistance = 90f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.dopplerLevel = 0f;
            s.volume = 0f;
            if (c != null) s.time = Random.Range(0f, c.length * 0.9f);
        }

        /// <summary>Called by the vehicle every frame. work 0..1, speed m/s.</summary>
        public void Drive(bool running, float work, float speed) => Drive(running, work, speed, 0f, 0f, 0f);

        /// <summary>
        /// hydraulic: how much the rams are moving (0..1). push: how hard they're working against the ground
        /// (digging, 0..1). blade: a blade or bucket edge scraping along the ground (0..1).
        /// </summary>
        public void Drive(bool running, float work, float speed, float hydraulic, float push, float blade)
        {
            float dt = Time.deltaTime;
            float target = running ? Mathf.Clamp01(Mathf.Max(work, speed / 10f)) : 0f;
            rpm = Mathf.MoveTowards(rpm, target, dt * (target > rpm ? 1.6f : 0.9f));
            hyd = Mathf.MoveTowards(hyd, running ? Mathf.Clamp01(hydraulic) : 0f, dt * (hydraulic > hyd ? 6f : 3f));
            strain = Mathf.MoveTowards(strain, running ? Mathf.Clamp01(push) : 0f, dt * (push > strain ? 5f : 2f));
            scrape = Mathf.MoveTowards(scrape, running ? Mathf.Clamp01(blade) : 0f, dt * (blade > scrape ? 6f : 3f));
            float vol = FPSBase.Core.GameSettings.EngineVolume;
            float level = Mathf.Lerp(0.26f, 0.62f, rpm);
            if (high != null)
            {
                // idle loop at low revs, the working loop as it revs up
                float x = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.8f, rpm));
                Set(src, running, level * (1f - x * 0.85f) * vol, basePitch * Mathf.Lerp(0.92f, 1.45f, rpm));
                Set(high, running, level * x * vol, basePitch * Mathf.Lerp(0.72f, 1.12f, rpm));
            }
            else Set(src, running, level * vol, basePitch * Mathf.Lerp(0.8f, 1.75f, rpm));
            float hp = Mathf.Lerp(0.65f, 1f, basePitch) * Mathf.Lerp(0.85f, 1.2f, rpm);
            Set(whine, running, hyd * 0.22f * vol, hp * Mathf.Lerp(0.95f, 1.08f, hyd));
            Set(groan, running, strain * Mathf.Max(hyd, 0.4f) * 0.2f * vol, hp * Mathf.Lerp(0.9f, 1.05f, strain));
            Set(scraper, running, scrape * 0.4f * vol, Mathf.Lerp(0.8f, 1.1f, Mathf.Clamp01(speed / 3f)));
            float tr = tracked ? Mathf.Clamp01(speed / 2.2f) : 0f;
            Set(clank, running, tr * 0.32f * vol, Mathf.Lerp(0.55f, 1.6f, Mathf.Clamp01(speed / 4.5f)) * Mathf.Lerp(0.85f, 1.1f, basePitch - 0.6f));
        }

        static void Set(AudioSource s, bool running, float volume, float pitch)
        {
            if (s == null) return;
            if (!running) volume = Mathf.MoveTowards(s.volume, 0f, Time.deltaTime);
            if (volume > 0.002f && !s.isPlaying) s.Play();
            s.volume = volume;
            s.pitch = pitch;
            if (volume <= 0.002f && s.isPlaying) s.Stop();
        }

        static AudioClip Clip()
        {
            if (clip != null) return clip;
            const int rate = 22050, n = rate; // 1 s, every frequency a whole number of Hz so it loops cleanly
            var d = new float[n];
            var rng = new System.Random(5);
            float lp = 0f, peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float s = 0f;
                for (int h = 1; h <= 7; h++) s += Mathf.Sin(2f * Mathf.PI * 32f * h * t + h * 1.3f) / h;
                float knock = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * 64f * t)), 12f); // firing pulses
                lp += ((float)rng.NextDouble() * 2f - 1f - lp) * 0.08f;                  // low rumble noise
                d[i] = s * 0.45f + knock * 0.6f + lp * 0.9f;
                peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            }
            for (int i = 0; i < n; i++) d[i] = d[i] / peak * 0.8f;
            // fade the noise seam
            for (int i = 0; i < 200; i++) { float f = i / 200f; d[n - 1 - i] = Mathf.Lerp(d[i], d[n - 1 - i], f); }
            clip = AudioClip.Create("DieselEngine", n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
