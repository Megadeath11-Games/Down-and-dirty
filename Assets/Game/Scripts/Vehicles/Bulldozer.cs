using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// Crawler dozer. Everything above the blade's bottom edge gets shoved forward as you drive: lower the blade
    /// below the ground to cut, keep it at ground level to scrape and push loose dirt. The dirt piles up in front
    /// of the blade and spills off the ends as berms. Stop or back up and it stays there as a pile.
    /// Bigger dozers swing the blade on push arms and have a rear ripper that breaks hard ground into loose dirt.
    /// </summary>
    public class Bulldozer : EarthmoverBase
    {
        [Header("Blade")]
        public Transform blade;            // moves up/down; origin at the bottom of the blade
        public Transform bladeEdge;        // cutting edge (bottom front centre)
        public float bladeWidth = 2.5f;
        public Vector2 bladeRange = new Vector2(-0.35f, 0.9f); // m relative to track bottom
        public float bladeSpeed = 0.5f;    // m/s
        [Tooltip("Optional: push-arm pivot. When set, the blade height is made by swinging the arms instead of sliding the blade.")]
        public Transform liftPivot;
        [Tooltip("Heap size (m^3) the blade carries before it starts to bog the machine down.")]
        public float heapCapacity = 5f;
        [Tooltip("How far ahead of the cutting edge the pushed dirt lands (bigger blades roll it further).")]
        public float pushAhead = 0.8f;

        [Header("Ripper (optional)")]
        public Transform ripperPivot;      // swings the draft arms (+ = down)
        public Transform ripperShank;      // counter-rotates so the shanks stay upright (parallelogram)
        public Transform[] ripperTips;
        public Vector2 ripperRange = new Vector2(-25f, 30f);
        public float ripperSpeed = 20f;    // deg/s
        public float ripRadius = 0.35f;

        float bladeHeight = 0.25f, ripper;
        Vector3 bladeBase, liftRest;
        float liftPivotY;
        float liftR, liftPhi;
        float fxTimer, pushRate, heapVolume, heapGold, heapTimer, ripRate;
        MaterialLoad heapMix;

        protected override void Awake()
        {
            base.Awake();
            if (blade != null) bladeBase = blade.localPosition;
            if (liftPivot != null && bladeEdge != null)
            {
                liftPivot.localRotation = Quaternion.identity;
                // cutting edge relative to the arm pivot (in the pivot's parent space) with the arms at rest
                liftRest = liftPivot.parent.InverseTransformPoint(bladeEdge.position) - liftPivot.localPosition;
                liftR = new Vector2(liftRest.z, liftRest.y).magnitude;
                liftPhi = Mathf.Atan2(liftRest.y, liftRest.z);
                liftPivotY = liftPivot.localPosition.y; // blade height is measured from the track bottom (= ground)
            }
            ripper = ripperRange.x;
            ApplyBlade();
        }

        float netBlade = float.NaN, netRip;
        public override void NetWrite(System.IO.BinaryWriter w) { base.NetWrite(w); w.Write(bladeHeight); w.Write(ripper); }
        public override void NetRead(System.IO.BinaryReader r) { base.NetRead(r); netBlade = r.ReadSingle(); netRip = r.ReadSingle(); }
        protected override void NetShow(float dt)
        {
            if (float.IsNaN(netBlade)) return;
            bladeHeight = NetIO.Ease(bladeHeight, netBlade, dt); ripper = NetIO.Ease(ripper, netRip, dt);
            ApplyBlade();
        }

        public void SetBlade(float height) { bladeHeight = Mathf.Clamp(height, bladeRange.x, bladeRange.y); ApplyBlade(); }
        public float BladeHeight => bladeHeight;
        /// <summary>Loose dirt (m^3) sitting right in front of the blade.</summary>
        public float HeapVolume => heapVolume;
        public bool HasRipper => ripperPivot != null;
        public void SetRipper(float deg) { ripper = Mathf.Clamp(deg, ripperRange.x, ripperRange.y); ApplyBlade(); }

        void ApplyBlade()
        {
            if (liftPivot != null && liftR > 0.01f)
            {
                // swing the push arms so the cutting edge sits `bladeHeight` above the ground under the tracks
                float y = Mathf.Clamp((bladeHeight - liftPivotY) / liftR, -1f, 1f);
                float theta = Mathf.Asin(y) - liftPhi;
                liftPivot.localRotation = Quaternion.Euler(-theta * Mathf.Rad2Deg, 0f, 0f);
            }
            else if (blade != null) blade.localPosition = bladeBase + Vector3.up * bladeHeight;
            if (ripperPivot != null) ripperPivot.localRotation = Quaternion.Euler(-ripper, 0f, 0f);
            if (ripperShank != null) ripperShank.localRotation = Quaternion.Euler(ripper, 0f, 0f);
        }

        protected override void DriveUpdate(float dt)
        {
            ReadDriveKeys();
            float bladeIn = Controls.Axis(GameAction.BladeUp, GameAction.BladeDown);
            if (!HasFuel) bladeIn = Mathf.Min(bladeIn, 0f);
            SetBlade(bladeHeight + bladeIn * bladeSpeed * dt);
            float ripIn = 0f;
            if (HasRipper)
            {
                ripIn = Controls.Axis(GameAction.RipperDown, GameAction.RipperUp);
                if (!HasFuel) ripIn = Mathf.Min(ripIn, 0f);
                SetRipper(ripper + ripIn * ripperSpeed * dt);
            }
            cab.right = new Vector2(0f, -bladeIn); // blade joystick: forward = lower
            cab.aux = ripIn;
            Work(ForwardSpeed, dt);
            workAmount = Mathf.Clamp01(Mathf.Abs(throttle) + Mathf.Abs(bladeIn) * 0.3f + Mathf.Abs(ripIn) * 0.3f + Mathf.Clamp01(pushRate) * 0.5f);
        }

        /// <summary>One step of blade (and ripper) work at forward speed `v` (m/s). Public so the automated test can drive it.</summary>
        public void Work(float v, float dt)
        {
            var site = DigSite.Instance;
            if (site == null || bladeEdge == null) return;
            Vector3 edge = bladeEdge.position;
            float moved = 0f, hard = 0f;
            if (v > 0.05f && site.Contains(edge))
                moved = site.BladePush(edge, transform.forward, bladeWidth, edge.y, pushAhead, out hard);
            pushRate = Mathf.Lerp(pushRate, moved / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt * 6f));
            if (moved > 0f && (fxTimer -= dt) <= 0f) { PourFx.Dig(edge + transform.forward * 0.6f + Vector3.up * 0.3f); fxTimer = 0.2f; }
            if (moved > 0f)
            {
                // the heap rolls: clods come over the top of it and tumble down its front and sides
                int n = Mathf.Clamp(Mathf.CeilToInt(moved / 0.004f), 1, 5);
                Vector3 f = transform.forward, r = transform.right;
                for (int i = 0; i < n; i++)
                {
                    Vector3 at = edge + f * (pushAhead + Random.Range(-0.1f, 0.3f)) + r * Random.Range(-0.45f, 0.45f) * bladeWidth;
                    at.y = site.SurfaceY(at) + 0.08f;
                    DirtGrains.Spray(at, f * Random.Range(0.8f, 2f) + r * Random.Range(-0.8f, 0.8f) + Vector3.up * Random.Range(0.2f, 1f),
                        DirtGrains.PickCategory(heapMix), Random.Range(0.06f, 0.14f));
                }
            }

            if ((heapTimer -= dt) <= 0f || dt > 0.015f && moved > 0f)
            {
                heapTimer = 0.2f;
                heapVolume = site.LooseVolume(edge + transform.forward * (pushAhead + 0.1f), bladeWidth * 0.55f, out heapGold);
                heapMix = site.LooseLoad(edge + transform.forward * (pushAhead + 0.1f), bladeWidth * 0.55f);
            }
            // a big heap slows the machine down, and so does cutting unbroken ground
            speedFactor = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(heapVolume / (heapCapacity * 1.4f)));
            float hardRate = hard / Mathf.Max(dt, 1e-4f);
            speedFactor *= Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(hardRate / (bladeWidth * 0.5f)));

            // ripper: shanks below the ground break the native ground into loose dirt as you drive
            float broken = 0f;
            if (ripperTips != null && v > 0.05f)
                foreach (var t in ripperTips)
                    if (t != null && site.IsUnderground(t.position)) broken += site.Rip(t.position, ripRadius);
            ripRate = Mathf.Lerp(ripRate, broken / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt * 6f));
            if (ripRate > 0.01f) speedFactor *= 0.75f;
        }

        /// <summary>Depth of the deepest ripper tip below the surface (m), 0 when out of the ground.</summary>
        public float RipperDepth
        {
            get
            {
                var site = DigSite.Instance;
                float d = 0f;
                if (site != null && ripperTips != null)
                    foreach (var t in ripperTips)
                        if (t != null && site.Contains(t.position)) d = Mathf.Max(d, site.SurfaceY(t.position) - t.position.y);
                return d;
            }
        }

        public override float HudBar => Mathf.Clamp01(heapVolume / heapCapacity);
        public override string HudBarLabel => $"Dirt in front of the blade: {heapVolume:0.0} / {heapCapacity:0.#} m³" + (pushRate > 0.05f ? "  —  pushing" : "");
        public override string HudBody => $"Fuel {fuel:0.0} / {fuelCapacity:0} L    Speed {Mathf.Abs(ForwardSpeed) * 3.6f:0.0} km/h\n" +
                                          $"Blade height {bladeHeight * 100f:+0;-0;0} cm  ({(bladeHeight < -0.02f ? "cutting" : bladeHeight < 0.08f ? "scraping" : "carried")})\n" +
                                          $"Heap: {(heapVolume > 0.05f ? heapMix.Describe() : "none")} · {heapGold:0.0} g gold" +
                                          (HasRipper ? "\nRipper " + (RipperDepth > 0.02f ? $"in the ground {RipperDepth * 100f:0} cm" + (ripRate > 0.01f ? " — ripping" : "") : "up") : "");
        public override string HudControls =>
            $"{N(GameAction.DriveForward)}/{N(GameAction.DriveBack)} forward / reverse · {N(GameAction.SteerLeft)}/{N(GameAction.SteerRight)} turn (tracks spin in place)\n" +
            $"{N(GameAction.BladeUp)} blade up · {N(GameAction.BladeDown)} blade down (below 0 cm = cutting)\n" +
            (HasRipper ? $"{N(GameAction.RipperDown)} ripper down · {N(GameAction.RipperUp)} ripper up (rip hard ground, then push it)\n" : "") +
            "Drive forward to push · stop or back up to leave a pile\n" +
            $"Mouse look · {N(GameAction.CabView)} cab view · {N(GameAction.Interact)} get out";

        static string N(GameAction a) => Controls.N(a);
    }
}
