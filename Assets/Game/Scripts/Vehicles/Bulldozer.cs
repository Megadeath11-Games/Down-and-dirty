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

        [Header("Blade tilt & angle")]
        [Tooltip("Tilts (one end down) and angles (swivels left / right) the blade. Empty = the blade itself.")]
        public Transform bladeSwivel;
        public float maxTilt = 9f;         // deg, + = left end down
        public float maxAngle = 25f;       // deg, + = swivelled right (right end back: dirt rolls off the right)
        public float tiltSpeed = 8f;       // deg/s
        public float angleSpeed = 16f;     // deg/s
        float bladeTilt, bladeAngle;
        Quaternion swivelRest = Quaternion.identity;
        Transform Swivel => bladeSwivel != null ? bladeSwivel : blade;

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
            if (Swivel != null) swivelRest = Swivel.localRotation;
            ApplyBlade();
        }

        float netBlade = float.NaN, netRip;
        float netTilt, netAngle;
        public override void NetWrite(System.IO.BinaryWriter w) { base.NetWrite(w); w.Write(bladeHeight); w.Write(ripper); w.Write(bladeTilt); w.Write(bladeAngle); }
        public override void NetRead(System.IO.BinaryReader r) { base.NetRead(r); netBlade = r.ReadSingle(); netRip = r.ReadSingle(); netTilt = r.ReadSingle(); netAngle = r.ReadSingle(); }
        protected override void NetShow(float dt)
        {
            if (float.IsNaN(netBlade)) return;
            bladeHeight = NetIO.Ease(bladeHeight, netBlade, dt); ripper = NetIO.Ease(ripper, netRip, dt);
            bladeTilt = NetIO.Ease(bladeTilt, netTilt, dt); bladeAngle = NetIO.Ease(bladeAngle, netAngle, dt);
            ApplyBlade();
            // a friend pushing dirt: their blade shoves the clods shown on this screen too, face for face as on theirs
            if (bladeEdge != null && FPSBase.Mining.DigSite.PushClods)
            {
                var site = FPSBase.Mining.DigSite.At(bladeEdge.position);
                Vector3 e = bladeEdge.position;
                if (site != null && site.Contains(e) && e.y < site.SurfaceY(e + transform.forward * 0.6f) + 0.15f)
                {
                    float h = Mathf.Clamp(bladeWidth * 0.32f, 0.5f, 2.2f);
                    BladeFaces(out Vector3 f, out Vector3 bladeRight, out bool square);
                    if (square) FPSBase.Mining.DirtGrains.Blade(e, transform.forward, bladeWidth, h);
                    else
                        for (int i = 0; i < 4; i++)
                            FPSBase.Mining.DirtGrains.Blade(e + bladeRight * ((i + 0.5f) / 4 - 0.5f) * bladeWidth, f, bladeWidth / 4 + 0.04f, h, bladeWidth);
                }
            }
        }

        /// <summary>Which way the blade's face pushes (`f`), its own right, and whether it's square (not tilted or angled).</summary>
        void BladeFaces(out Vector3 f, out Vector3 bladeRight, out bool square)
        {
            bladeRight = Swivel != null ? Swivel.right : transform.right;
            Vector3 flatRight = Vector3.ProjectOnPlane(bladeRight, Vector3.up).normalized;
            if (flatRight.sqrMagnitude < 0.5f) flatRight = transform.right;
            f = Vector3.Cross(flatRight, Vector3.up).normalized;
            Vector3 travel = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (travel.sqrMagnitude > 0.5f && Vector3.Dot(f, travel) > 0f) f = Vector3.Slerp(travel, f, 0.75f).normalized;
            square = Mathf.Abs(bladeTilt) < 0.5f && Mathf.Abs(bladeAngle) < 0.5f;
        }

        public void SetBlade(float height) { bladeHeight = Mathf.Clamp(height, bladeRange.x, bladeRange.y); ApplyBlade(); }
        public float BladeHeight => bladeHeight;
        /// <summary>Loose dirt (m^3) sitting right in front of the blade.</summary>
        public float HeapVolume => heapVolume;
        public bool HasRipper => ripperPivot != null;
        public void SetRipper(float deg) { ripper = Mathf.Clamp(deg, ripperRange.x, ripperRange.y); ApplyBlade(); }
        /// <summary>Blade tilt (deg, + = left end down) and angle (deg, + = swivelled right).</summary>
        public void SetBladeTilt(float deg) { bladeTilt = Mathf.Clamp(deg, -maxTilt, maxTilt); ApplyBlade(); }
        public void SetBladeAngle(float deg) { bladeAngle = Mathf.Clamp(deg, -maxAngle, maxAngle); ApplyBlade(); }
        public float BladeTilt => bladeTilt;
        public float BladeAngle => bladeAngle;

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
            // angle about the vertical, then tilt about the blade's own fore-aft axis
            if (Swivel != null) Swivel.localRotation = swivelRest * Quaternion.Euler(0f, bladeAngle, 0f) * Quaternion.Euler(0f, 0f, bladeTilt);
            if (ripperPivot != null) ripperPivot.localRotation = Quaternion.Euler(-ripper, 0f, 0f);
            if (ripperShank != null) ripperShank.localRotation = Quaternion.Euler(ripper, 0f, 0f);
        }

        /// <summary>How hard the ground pushes back on the blade, as a fraction of the machine's weight (for the test).</summary>
        public float BladePushBack { get; private set; }

        // the blade lowered into unbroken ground further than it's cutting lifts the front, like on pavement
        protected override void GroundPushBack()
        {
            BladePushBack = 0f;
            if (bladeEdge == null) return;
            Vector3 r = (Swivel != null ? Swivel.right : transform.right) * (bladeWidth * 0.5f);
            BladePushBack = GroundPush.Edge(rb, suspension.gravityScale, bladeEdge.position - r, bladeEdge.position + r, 9, 0.04f, 0.12f, out _);
        }

        protected override void FixedUpdate()
        {
            // nobody driving (parked, or rolling / test-driven without a driver): the blade still works the ground it
            // moves through, and a heap it pushed earlier and left behind doesn't keep holding the machine back
            if (!Occupied && !NetRemote)
            {
                if (Mathf.Abs(ForwardSpeed) > 0.05f) Work(ForwardSpeed, Time.fixedDeltaTime);
                else { speedFactor = 1f; heapVolume = 0f; heapTimer = 0f; }
            }
            base.FixedUpdate();
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
            float tiltIn = Controls.Axis(GameAction.BladeTiltLeft, GameAction.BladeTiltRight);
            float angleIn = Controls.Axis(GameAction.BladeAngleRight, GameAction.BladeAngleLeft);
            if (!HasFuel) { tiltIn = 0f; angleIn = 0f; }
            if (tiltIn != 0f) SetBladeTilt(bladeTilt + tiltIn * tiltSpeed * dt);
            if (angleIn != 0f) SetBladeAngle(bladeAngle + angleIn * angleSpeed * dt);
            cab.right = new Vector2(-tiltIn, -bladeIn); // blade joystick: forward = lower, sideways = tilt
            cab.aux = ripIn != 0f ? ripIn : angleIn;
            Work(ForwardSpeed, dt);
            workAmount = Mathf.Clamp01(Mathf.Abs(throttle) + (Mathf.Abs(bladeIn) + Mathf.Abs(tiltIn) + Mathf.Abs(angleIn)) * 0.3f + Mathf.Abs(ripIn) * 0.3f + Mathf.Clamp01(pushRate) * 0.5f);
            hydraulicAmount = Mathf.Clamp01(Mathf.Abs(bladeIn) + Mathf.Abs(tiltIn) + Mathf.Abs(angleIn) + Mathf.Abs(ripIn));
            scrapeAmount = Mathf.Clamp01(pushRate * 1.2f);
            strainAmount = Mathf.Clamp01(pushRate * 0.6f);
        }

        /// <summary>One step of blade (and ripper) work at forward speed `v` (m/s). Public so the automated test can drive it.</summary>
        public void Work(float v, float dt)
        {
            var site = DigSite.At(transform.position);
            if (site == null || bladeEdge == null) return;
            Vector3 edge = bladeEdge.position;
            float moved = 0f, hard = 0f;
            // the blade's face: an angled blade pushes off to one side, a tilted one cuts deeper at its low end
            Vector3 bladeRight = Swivel != null ? Swivel.right : transform.right;
            Vector3 flatRight = Vector3.ProjectOnPlane(bladeRight, Vector3.up).normalized;
            if (flatRight.sqrMagnitude < 0.5f) flatRight = transform.right;
            Vector3 f = Vector3.Cross(flatRight, Vector3.up).normalized, r = flatRight;
            // the dirt rides along the blade face and only part of it slides off the trailing end: push it mostly
            // the way the machine is going (straight off the face, all of it went sideways in a few metres)
            Vector3 travel = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (travel.sqrMagnitude > 0.5f && Vector3.Dot(f, travel) > 0f) f = Vector3.Slerp(travel, f, 0.75f).normalized;
            bool square = Mathf.Abs(bladeTilt) < 0.5f && Mathf.Abs(bladeAngle) < 0.5f;
            if (v > 0.05f && site.Contains(edge))
            {
                // the blade cuts as deep as it's set below the tracks, not deeper: when the nose dips into the cut the
                // blade would go lower still and dig itself in (a driver eases it up; this keeps the cut even)
                float deepest = Mathf.Min(bladeHeight, 0f) - 0.08f;
                edge.y = Mathf.Max(edge.y, site.SurfaceY(edge + f * 0.4f) + deepest);
                if (square) moved = site.BladePush(edge, transform.forward, bladeWidth, edge.y, pushAhead, out hard);
                else
                {
                    const int segs = 4;
                    float w = bladeWidth / segs;
                    site.BladeWidthHintSet(bladeWidth);
                    for (int i = 0; i < segs; i++)
                    {
                        Vector3 p = bladeEdge.position + bladeRight * ((i + 0.5f) / segs - 0.5f) * bladeWidth;
                        if (!site.Contains(p)) continue;
                        p.y = Mathf.Max(p.y, site.SurfaceY(p + f * 0.4f) + deepest + Mathf.Min(0f, p.y - bladeEdge.position.y)); // (a tilted blade's low end still goes deeper)
                        moved += site.BladePush(p, f, w + 0.04f, p.y, pushAhead, out float h);
                        hard += h;
                    }
                    site.BladeWidthHintSet(0f);
                }
            }
            pushRate = Mathf.Lerp(pushRate, moved / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt * 6f));
            if (moved > 0f && (fxTimer -= dt) <= 0f) { PourFx.Dig(edge + f * 0.6f + Vector3.up * 0.3f); fxTimer = 0.2f; }
            if (moved > 0f)
            {
                // the heap rolls: clods come over the top of it and tumble down its front and sides
                int n = Random.value < 0.35f ? 1 : 0;
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
                heapVolume = site.LooseVolume(edge + f * (pushAhead + 0.1f), bladeWidth * 0.55f, out heapGold);
                // plus the clods rolling in front of the blade (cut dirt the blade is shoving along)
                heapVolume += FPSBase.Mining.DirtGrains.VolumeNear(edge + f * (pushAhead * 0.5f + 0.3f), bladeWidth * 0.6f, out float clodGold);
                heapGold += clodGold;
                heapMix = site.LooseLoad(edge + f * (pushAhead + 0.1f), bladeWidth * 0.55f);
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
                var site = DigSite.At(transform.position);
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
                                          $"Blade tilt {(Mathf.Abs(bladeTilt) < 0.5f ? "level" : $"{Mathf.Abs(bladeTilt):0}° {(bladeTilt > 0f ? "left" : "right")} end down")} · angle {(Mathf.Abs(bladeAngle) < 0.5f ? "straight" : $"{Mathf.Abs(bladeAngle):0}° {(bladeAngle > 0f ? "right" : "left")}")}\n" +
                                          $"Heap: {(heapVolume > 0.05f ? heapMix.Describe() : "none")} · {heapGold:0.0} g gold" +
                                          (HasRipper ? "\nRipper " + (RipperDepth > 0.02f ? $"in the ground {RipperDepth * 100f:0} cm" + (ripRate > 0.01f ? " — ripping" : "") : "up") : "");
        public override string HudControls =>
            $"{N(GameAction.DriveForward)}/{N(GameAction.DriveBack)} forward / reverse · {N(GameAction.SteerLeft)}/{N(GameAction.SteerRight)} turn (tracks spin in place)\n" +
            $"{N(GameAction.BladeUp)} blade up · {N(GameAction.BladeDown)} blade down (below 0 cm = cutting)\n" +
            $"{N(GameAction.BladeTiltLeft)}/{N(GameAction.BladeTiltRight)} tilt blade (left / right end down) · {N(GameAction.BladeAngleLeft)}/{N(GameAction.BladeAngleRight)} swivel blade left / right\n" +
            (HasRipper ? $"{N(GameAction.RipperDown)} ripper down · {N(GameAction.RipperUp)} ripper up (rip hard ground, then push it)\n" : "") +
            "Drive forward to push · stop or back up to leave a pile\n" +
            $"Mouse look · {N(GameAction.CabView)} cab view · {N(GameAction.Interact)} get out";

        static string N(GameAction a) => Controls.N(a);
    }
}
