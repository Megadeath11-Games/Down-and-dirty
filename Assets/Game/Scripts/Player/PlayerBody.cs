using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.Player
{
    /// <summary>
    /// The player's visible body (the mannequin) and footsteps. Feeds movement into the rig, shows the body in
    /// third person (only its shadow in first person), takes the body along into a machine's seat, and plays
    /// footsteps that sound like what you're walking on: grass, dirt, sand, gravel or hard floors.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public class PlayerBody : MonoBehaviour
    {
        public MannequinRig rig;

        SourceMovement mv;
        AudioSource src;
        Vector3 lastPos;
        float stepDist, airTime;
        bool wasGrounded = true;
        public int StepsPlayed { get; private set; }
        public StepSurface LastSurface { get; private set; }

        public enum StepSurface { Grass, Dirt, Sand, Gravel, Hard }
        static AudioClip[][] clips;

        void Awake()
        {
            mv = GetComponent<SourceMovement>();
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0f; src.loop = false;
            if (rig != null) rig.firstPerson = () => !GameSettings.ThirdPerson;
            lastPos = transform.position;
        }

        void OnEnable() { lastPos = transform.position; }

        void Update()
        {
            if (mv == null) return;
            Vector3 d = transform.position - lastPos; lastPos = transform.position;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            float spd = flat.magnitude / dt;
            if (spd > 30f) spd = 0f; // teleported

            if (rig != null && rig.pose == MannequinRig.Pose.Stand)
            {
                rig.speed = Mathf.Lerp(rig.speed, Mathf.Min(spd, 12f), 1f - Mathf.Exp(-12f * dt));
                rig.grounded = mv.Grounded;
                rig.crouch = mv.EyeHeight < 1.35f ? 1f : 0f;
                rig.verticalSpeed = d.y / dt;
            }

            // ---- footsteps
            if (mv.Grounded)
            {
                if (!wasGrounded && airTime > 0.25f) Step(1.3f); // landing
                airTime = 0f;
                if (spd > 0.6f && spd < 30f)
                {
                    stepDist += flat.magnitude;
                    float stride = Mathf.Lerp(0.7f, 1.25f, Mathf.InverseLerp(2f, 7f, spd));
                    if (mv.EyeHeight < 1.35f) stride *= 0.8f;
                    if (stepDist >= stride) { stepDist -= stride; Step(Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(1.5f, 7f, spd)) * (mv.EyeHeight < 1.35f ? 0.6f : 1f)); }
                }
            }
            else airTime += dt;
            wasGrounded = mv.Grounded;
        }

        void Step(float loudness)
        {
            var surf = SurfaceUnder();
            LastSurface = surf;
            StepsPlayed++;
            clips ??= BuildClips();
            var set = clips[(int)surf];
            src.pitch = Random.Range(0.9f, 1.1f);
            src.PlayOneShot(set[Random.Range(0, set.Length)], loudness * GameSettings.FootstepVolume * 0.8f);
        }

        public StepSurface SurfaceUnder()
        {
            Vector3 o = transform.position + Vector3.up * 0.3f;
            if (!Physics.Raycast(o, Vector3.down, out RaycastHit h, 1.2f, ~0, QueryTriggerInteraction.Ignore)) return StepSurface.Grass;
            var site = DigSite.Instance;
            if (site != null && h.collider.gameObject == site.gameObject) return site.StepSurfaceAt(h.point);
            if (h.collider is TerrainCollider) return StepSurface.Grass;
            return StepSurface.Hard;
        }

        // ---- the body goes with you into a machine

        /// <summary>Sit the body in a machine's seat (`eye` = the cab view point). `cabView` says when the camera is in its head.</summary>
        public void SitIn(Transform eye, System.Func<bool> cabView)
        {
            if (rig == null || eye == null) return;
            rig.transform.SetParent(eye.parent != null ? eye.parent : eye, true);
            rig.pose = MannequinRig.Pose.Sit;
            rig.seat = eye;
            var veh = eye.GetComponentInParent<FPSBase.Vehicles.VehicleBase>();
            rig.controls = veh != null ? veh.Controls3D : null;
            rig.speed = 0f; rig.crouch = 0f; rig.grounded = true;
            rig.firstPerson = cabView;
        }

        public void StandUp()
        {
            if (rig == null) return;
            rig.transform.SetParent(transform, false);
            rig.transform.localPosition = Vector3.zero;
            rig.transform.localRotation = Quaternion.identity;
            rig.pose = MannequinRig.Pose.Stand;
            rig.seat = null;
            rig.controls = null;
            rig.firstPerson = () => !GameSettings.ThirdPerson;
        }

        // ---- procedural footstep sounds (no audio files)

        static AudioClip[][] BuildClips()
        {
            var all = new AudioClip[5][];
            for (int s = 0; s < 5; s++)
            {
                all[s] = new AudioClip[4];
                for (int v = 0; v < 4; v++) all[s][v] = StepClip((StepSurface)s, v);
            }
            return all;
        }

        static AudioClip StepClip(StepSurface surf, int variant)
        {
            const int rate = 44100;
            var rng = new System.Random(variant * 131 + (int)surf * 17 + 5);
            float len = surf switch { StepSurface.Hard => 0.13f, StepSurface.Gravel => 0.2f, StepSurface.Sand => 0.22f, _ => 0.17f };
            int n = (int)(rate * len);
            var data = new float[n];
            // tone of the thump and how muffled the scuff is
            float thumpHz = surf switch { StepSurface.Hard => 140f, StepSurface.Gravel => 95f, _ => 75f } * (0.9f + variant * 0.06f);
            float lp = surf switch { StepSurface.Grass => 0.05f, StepSurface.Dirt => 0.1f, StepSurface.Sand => 0.07f, StepSurface.Gravel => 0.45f, _ => 0.6f };
            float noiseAmt = surf switch { StepSurface.Hard => 0.25f, StepSurface.Gravel => 0.9f, StepSurface.Sand => 0.7f, StepSurface.Grass => 0.6f, _ => 0.55f };
            float thumpAmt = surf switch { StepSurface.Hard => 0.9f, StepSurface.Sand => 0.3f, _ => 0.6f };
            float y = 0f, y2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * (surf == StepSurface.Hard ? 45f : 22f)) * Mathf.Clamp01(t * 400f);
                float noise = (float)rng.NextDouble() * 2f - 1f;
                // gravel: little stones clicking against each other
                if (surf == StepSurface.Gravel && rng.NextDouble() < 0.004) noise *= 6f;
                y += (noise - y) * lp;
                y2 += (y - y2) * Mathf.Min(1f, lp * 2f);
                float thump = Mathf.Sin(2f * Mathf.PI * thumpHz * t) * Mathf.Exp(-t * 40f);
                // grass and sand: a second, softer scuff as the foot rolls off
                float roll = (surf == StepSurface.Grass || surf == StepSurface.Sand) && t > len * 0.45f ? Mathf.Exp(-(t - len * 0.45f) * 30f) * 0.5f : 0f;
                data[i] = (y2 * noiseAmt * 2.2f * (env + roll) + thump * thumpAmt * env) * 0.6f;
            }
            var clip = AudioClip.Create($"Step_{surf}_{variant}", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
