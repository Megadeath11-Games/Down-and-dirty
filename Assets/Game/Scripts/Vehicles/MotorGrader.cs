using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// Articulated motor grader (140M class). Six-wheel drive with a steerable, leaning front axle and an
    /// articulated frame; the moldboard hangs under the frame on a drawbar and circle, so it can be raised and lowered
    /// (each end on its own lift ram for cross slope), turned on the circle, slid sideways and pitched.
    /// Driving forward with the edge in the ground cuts everything above the edge line and rolls it along the angled
    /// blade to the trailing end, leaving a windrow; spreading a windrow or a dumped load levels it out into a road.
    /// The rear ripper / scarifier breaks up hard ground.
    /// </summary>
    public class MotorGrader : EarthmoverBase
    {
        [Header("Frames & wheels")]
        public Transform frontFrame, rearFrame;   // swing about the articulation hinge
        public Transform[] steerPivots, leanPivots;
        public Transform[] wheelSpins;            // spin about their local x
        public float wheelRadius = 0.68f;
        public float maxSteer = 40f, maxArtic = 25f, maxLean = 18f;

        [Header("Blade")]
        public Transform drawbar;                 // lift (x) + roll (z) about the ball joint
        public Transform circle;                  // turns about y
        public Transform shift;                   // slides along x
        public Transform pitch;                   // tips about x
        public Transform bladeEndL, bladeEndR;    // ends of the cutting edge
        public Vector2 liftRange = new Vector2(-9f, 14f);   // deg, + = raise
        public float maxRoll = 6f, maxCircle = 65f, maxShift = 0.75f;
        public Vector2 pitchRange = new Vector2(-15f, 25f); // deg, + = forward (aggressive cut)
        public float liftSpeed = 6f, rollSpeed = 4f, circleSpeed = 25f, shiftSpeed = 0.5f, pitchSpeed = 15f;

        [Header("Ripper")]
        public Transform ripperPivot;
        public Transform[] ripperTips;
        public Vector2 ripperRange = new Vector2(-18f, 22f); // deg, + = down
        public float ripperSpeed = 15f, ripRadius = 0.25f;

        float steerDeg, artic, lean, lift = 4f, roll, circleDeg = 30f, shiftM, pitchDeg = 5f, ripper;
        float[] spins;
        float cutRate, fxTimer, ripRate;
        Quaternion dRest, cRest, pRest, rRest;
        Vector3 sRest;
        Quaternion[] steerRest, leanRest, spinRest;

        protected override void Awake()
        {
            base.Awake();
            Quaternion R(Transform t) => t != null ? t.localRotation : Quaternion.identity;
            dRest = R(drawbar); cRest = R(circle); pRest = R(pitch); rRest = R(ripperPivot);
            sRest = shift != null ? shift.localPosition : Vector3.zero;
            steerRest = Rest(steerPivots); leanRest = Rest(leanPivots); spinRest = Rest(wheelSpins);
            spins = new float[wheelSpins != null ? wheelSpins.Length : 0];
            ripper = ripperRange.x;
            Apply();
        }

        static Quaternion[] Rest(Transform[] ts)
        {
            var r = new Quaternion[ts != null ? ts.Length : 0];
            for (int i = 0; i < r.Length; i++) r[i] = ts[i] != null ? ts[i].localRotation : Quaternion.identity;
            return r;
        }

        // ------------------------------------------------------------------ pose

        public void SetBlade(float liftDeg, float rollDeg, float circleAngle, float shiftMetres, float pitchAngle)
        {
            lift = Mathf.Clamp(liftDeg, liftRange.x, liftRange.y);
            roll = Mathf.Clamp(rollDeg, -maxRoll, maxRoll);
            circleDeg = Mathf.Clamp(circleAngle, -maxCircle, maxCircle);
            shiftM = Mathf.Clamp(shiftMetres, -maxShift, maxShift);
            pitchDeg = Mathf.Clamp(pitchAngle, pitchRange.x, pitchRange.y);
            Apply();
        }
        float[] netPose;
        public override void NetWrite(System.IO.BinaryWriter w)
        {
            base.NetWrite(w);
            foreach (float v in new[] { steerDeg, artic, lean, lift, roll, circleDeg, shiftM, pitchDeg, ripper }) w.Write(v);
        }
        public override void NetRead(System.IO.BinaryReader r)
        {
            base.NetRead(r);
            netPose ??= new float[9];
            for (int i = 0; i < 9; i++) netPose[i] = r.ReadSingle();
        }
        protected override void NetShow(float dt)
        {
            if (netPose == null) return;
            steerDeg = NetIO.Ease(steerDeg, netPose[0], dt); artic = NetIO.Ease(artic, netPose[1], dt); lean = NetIO.Ease(lean, netPose[2], dt);
            lift = NetIO.Ease(lift, netPose[3], dt); roll = NetIO.Ease(roll, netPose[4], dt); circleDeg = NetIO.Ease(circleDeg, netPose[5], dt);
            shiftM = NetIO.Ease(shiftM, netPose[6], dt); pitchDeg = NetIO.Ease(pitchDeg, netPose[7], dt); ripper = NetIO.Ease(ripper, netPose[8], dt);
            Apply();
        }

        public void SetRipper(float deg) { ripper = Mathf.Clamp(deg, ripperRange.x, ripperRange.y); Apply(); }
        public float Lift => lift;
        public float CircleAngle => circleDeg;

        void Apply()
        {
            if (frontFrame != null) frontFrame.localRotation = Quaternion.Euler(0f, artic * 0.5f, 0f);
            if (rearFrame != null) rearFrame.localRotation = Quaternion.Euler(0f, -artic * 0.5f, 0f);
            for (int i = 0; steerPivots != null && i < steerPivots.Length; i++)
                if (steerPivots[i] != null) steerPivots[i].localRotation = steerRest[i] * Quaternion.Euler(0f, steerDeg, 0f);
            for (int i = 0; leanPivots != null && i < leanPivots.Length; i++)
                if (leanPivots[i] != null) leanPivots[i].localRotation = leanRest[i] * Quaternion.Euler(0f, 0f, -lean);
            if (drawbar != null) drawbar.localRotation = dRest * Quaternion.Euler(lift, 0f, -roll); // the drawbar trails back from its ball joint
            if (circle != null) circle.localRotation = cRest * Quaternion.Euler(0f, circleDeg, 0f);
            if (shift != null) shift.localPosition = sRest + Vector3.right * shiftM;
            if (pitch != null) pitch.localRotation = pRest * Quaternion.Euler(pitchDeg, 0f, 0f);
            if (ripperPivot != null) ripperPivot.localRotation = rRest * Quaternion.Euler(-ripper, 0f, 0f); // arms trail back: + swings them down
        }

        // ------------------------------------------------------------------ driving

        protected override void DriveUpdate(float dt)
        {
            ReadDriveKeys();
            float steerIn = steer;
            bool fuel = HasFuel;
            float A(GameAction p, GameAction n) => fuel ? Controls.Axis(p, n) : 0f;
            float liftIn = A(GameAction.GraderBladeUp, GameAction.GraderBladeDown);
            float rollIn = A(GameAction.GraderTiltRight, GameAction.GraderTiltLeft);
            float circleIn = A(GameAction.GraderCircleRight, GameAction.GraderCircleLeft);
            float shiftIn = A(GameAction.GraderShiftRight, GameAction.GraderShiftLeft);
            float pitchIn = A(GameAction.GraderPitchForward, GameAction.GraderPitchBack);
            float articIn = A(GameAction.GraderArticRight, GameAction.GraderArticLeft);
            float leanIn = A(GameAction.GraderLeanRight, GameAction.GraderLeanLeft);
            float ripIn = A(GameAction.GraderRipperDown, GameAction.GraderRipperUp);
            Operate(steerIn, liftIn, rollIn, circleIn, shiftIn, pitchIn, articIn, leanIn, ripIn, dt);
        }

        /// <summary>One frame of controls (each -1..1). Public for the automated test.</summary>
        public void Operate(float steerIn, float liftIn, float rollIn, float circleIn, float shiftIn, float pitchIn, float articIn, float leanIn, float ripIn, float dt)
        {
            // the front wheels steer while the key is held and come back to straight when it's let go
            steerDeg = Mathf.Abs(steerIn) > 0.01f ? Mathf.MoveTowards(steerDeg, steerIn * maxSteer, 55f * dt)
                                                   : Mathf.MoveTowards(steerDeg, 0f, 35f * dt);
            artic = Mathf.Clamp(artic + articIn * 12f * dt, -maxArtic, maxArtic);
            lean = Mathf.Clamp(lean + leanIn * 15f * dt, -maxLean, maxLean);
            SetBlade(lift + liftIn * liftSpeed * dt, roll + rollIn * rollSpeed * dt, circleDeg + circleIn * circleSpeed * dt,
                     shiftM + shiftIn * shiftSpeed * dt, pitchDeg + pitchIn * pitchSpeed * dt);
            SetRipper(ripper + ripIn * ripperSpeed * dt);
            // turning comes from the steered front wheels plus the frame articulation
            steer = Mathf.Clamp(steerDeg / maxSteer * 0.75f + artic / maxArtic * 0.55f + lean / maxLean * 0.12f, -1f, 1f);
            Work(ForwardSpeed, dt);
            workAmount = Mathf.Clamp01(Mathf.Abs(throttle) + Mathf.Clamp01(cutRate) * 0.6f + (Mathf.Abs(liftIn) + Mathf.Abs(circleIn) + Mathf.Abs(shiftIn)) * 0.15f);
            hydraulicAmount = Mathf.Clamp01((Mathf.Abs(liftIn) + Mathf.Abs(circleIn) + Mathf.Abs(shiftIn)) * 0.7f);
            scrapeAmount = Mathf.Clamp01(cutRate * 2f);
            // 140M-style pods: left = steer + left lift ram, right = side shift + right lift ram, twist for the rest
            cab.left = new Vector2(steerIn, Mathf.Clamp(-(liftIn - rollIn), -1f, 1f));
            cab.right = new Vector2(shiftIn != 0f ? shiftIn : circleIn * 0.6f, Mathf.Clamp(-(liftIn + rollIn), -1f, 1f));
            cab.aux = ripIn;
        }

        // nobody driving: the steering centres, nothing else moves
        protected override void IdleUpdate(float dt)
        {
            steer = 0f;
            steerDeg = Mathf.MoveTowards(steerDeg, 0f, 35f * dt);
            Apply();
        }

        protected override void Update()
        {
            base.Update();
            Apply();
            if (wheelSpins == null) return;
            for (int i = 0; i < wheelSpins.Length; i++)
            {
                if (wheelSpins[i] == null) continue;
                spins[i] += ForwardSpeed * Time.deltaTime / Mathf.Max(0.1f, wheelRadius) * Mathf.Rad2Deg;
                wheelSpins[i].localRotation = spinRest[i] * Quaternion.Euler(spins[i], 0f, 0f);
            }
        }

        // ------------------------------------------------------------------ grading

        /// <summary>Blade + ripper work at forward speed `v`. Public for the automated test.</summary>
        public void Work(float v, float dt)
        {
            var site = DigSite.At(transform.position);
            if (site == null || bladeEndL == null || bladeEndR == null) return;
            Vector3 a = bladeEndL.position, b = bladeEndR.position;
            float moved = 0f;
            if (v > 0.05f)
            {
                // the moldboard faces its own forward; dirt cut along it rolls toward the trailing end
                Vector3 n = Vector3.ProjectOnPlane(pitch != null ? pitch.forward : transform.forward, Vector3.up);
                if (n.sqrMagnitude < 1e-4f) n = transform.forward;
                n.Normalize();
                Vector3 along = b - a; along.y = 0f;
                float len = along.magnitude;
                const int segs = 5;
                for (int i = 0; i < segs; i++)
                {
                    Vector3 c = Vector3.Lerp(a, b, (i + 0.5f) / segs);
                    if (!site.Contains(c)) continue;
                    moved += site.BladePush(c, n, len / segs + 0.05f, c.y, 0.45f, out _);
                }
            }
            cutRate = Mathf.Lerp(cutRate, moved / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt * 6f));
            if (moved > 0f && (fxTimer -= dt) <= 0f) { PourFx.Dig(Vector3.Lerp(a, b, 0.5f) + Vector3.up * 0.3f); fxTimer = 0.25f; }
            speedFactor = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(cutRate / 0.6f));

            float broken = 0f;
            if (ripperTips != null && v > 0.05f)
                foreach (var t in ripperTips)
                    if (t != null && site.IsUnderground(t.position)) broken += site.Rip(t.position, ripRadius);
            ripRate = Mathf.Lerp(ripRate, broken / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt * 6f));
            if (ripRate > 0.01f) speedFactor *= 0.8f;
        }

        /// <summary>Height of the cutting edge's lowest end above (+) or below (-) the ground under it.</summary>
        public float EdgeHeight
        {
            get
            {
                var site = DigSite.At(transform.position);
                if (site == null || bladeEndL == null || bladeEndR == null) return 0f;
                float h = float.MaxValue;
                foreach (var e in new[] { bladeEndL.position, bladeEndR.position, Vector3.Lerp(bladeEndL.position, bladeEndR.position, 0.5f) })
                    h = Mathf.Min(h, e.y - (site.Contains(e) ? site.SurfaceY(e) : GroundBelow(e)));
                return h;
            }
        }

        public float CutRate => cutRate;

        public override float HudBar => Mathf.Clamp01(cutRate / 0.5f);
        public override string HudBarLabel => cutRate > 0.02f ? $"Grading: cutting {cutRate:0.00} m³/s" : "Blade " + (EdgeHeight > 0.05f ? "carried" : "on the ground");
        public override string HudBody =>
            $"Fuel {fuel:0.0} / {fuelCapacity:0} L    Speed {Mathf.Abs(ForwardSpeed) * 3.6f:0.0} km/h\n" +
            $"Edge {EdgeHeight * 100f:+0;-0;0} cm · circle {circleDeg:0}° · cross slope {roll:+0.0;-0.0;0}° · shift {shiftM:+0.00;-0.00;0} m · pitch {pitchDeg:0}°\n" +
            $"Steer {steerDeg:0}° · articulation {artic:0}° · wheel lean {lean:0}°" +
            (ripperTips != null && ripperTips.Length > 0 ? $"\nRipper {(ripper > 2f ? "down" + (ripRate > 0.01f ? " — ripping" : "") : "up")}" : "");
        public override string HudControls =>
            $"{N(GameAction.DriveForward)}/{N(GameAction.DriveBack)} drive · {N(GameAction.SteerLeft)}/{N(GameAction.SteerRight)} steer · {N(GameAction.GraderArticLeft)}/{N(GameAction.GraderArticRight)} articulate · {N(GameAction.GraderLeanLeft)}/{N(GameAction.GraderLeanRight)} wheel lean\n" +
            $"{N(GameAction.GraderBladeUp)}/{N(GameAction.GraderBladeDown)} blade up/down · {N(GameAction.GraderTiltLeft)}/{N(GameAction.GraderTiltRight)} cross slope · {N(GameAction.GraderCircleLeft)}/{N(GameAction.GraderCircleRight)} circle · {N(GameAction.GraderShiftLeft)}/{N(GameAction.GraderShiftRight)} side shift · {N(GameAction.GraderPitchForward)}/{N(GameAction.GraderPitchBack)} pitch\n" +
            $"{N(GameAction.GraderRipperDown)}/{N(GameAction.GraderRipperUp)} ripper down/up · angle the blade and drive to cut and windrow, spread a dumped load to level a road\n" +
            $"Mouse look · {N(GameAction.CabView)} cab view · {N(GameAction.Interact)} get out";

        static string N(GameAction a) => Controls.N(a);
    }
}
