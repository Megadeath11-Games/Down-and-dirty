using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// Procedural diesel engine sound (no audio files needed): a looping knock + rumble whose pitch and volume
    /// follow how hard the machine is working. Bigger machines rumble lower. Off when nobody is in the seat.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EngineAudio : MonoBehaviour
    {
        public float basePitch = 1f;
        float rpm;
        AudioSource src;
        static AudioClip clip;

        public static EngineAudio Attach(VehicleBase v)
        {
            var e = v.gameObject.AddComponent<EngineAudio>();
            var rb = v.GetComponent<Rigidbody>();
            float t = rb != null ? rb.mass / 1000f : 5f;
            e.basePitch = Mathf.Clamp(1.35f - Mathf.Log10(Mathf.Max(1f, t)) * 0.38f, 0.6f, 1.35f);
            if (v.vehicleCamera != null && v.vehicleCamera.GetComponentInChildren<AudioListener>(true) == null)
                v.vehicleCamera.gameObject.AddComponent<AudioListener>();
            return e;
        }

        void Awake()
        {
            src = GetComponent<AudioSource>();
            src.clip = Clip();
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0.8f;
            src.minDistance = 5f;
            src.maxDistance = 90f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.volume = 0f;
            src.time = Random.Range(0f, 0.9f);
        }

        /// <summary>Called by the vehicle every frame. work 0..1, speed m/s.</summary>
        public void Drive(bool running, float work, float speed)
        {
            float target = running ? Mathf.Clamp01(Mathf.Max(work, speed / 10f)) : 0f;
            rpm = Mathf.MoveTowards(rpm, target, Time.deltaTime * (target > rpm ? 1.6f : 0.9f));
            if (running && !src.isPlaying) src.Play();
            src.pitch = basePitch * Mathf.Lerp(0.8f, 1.75f, rpm);
            src.volume = running ? Mathf.Lerp(0.22f, 0.6f, rpm) * FPSBase.Core.GameSettings.EngineVolume : Mathf.MoveTowards(src.volume, 0f, Time.deltaTime);
            if (!running && src.volume <= 0f && src.isPlaying) src.Stop();
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
