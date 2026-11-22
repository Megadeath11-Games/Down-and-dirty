using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSBase.Player
{
    /// <summary>
    /// Poses the mannequin procedurally (the model comes with no animations): walk / run cycle, crouch, in the air,
    /// and sitting in a machine's seat with hands on the controls. Every frame the skeleton is reset to its bind
    /// (T-) pose and bones are turned about the character's own right / forward / up axes, so it doesn't care how
    /// each bone's local axes happen to be set up in the file.
    /// Also hides the body (shadow only) whenever the camera is in its head.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class MannequinRig : MonoBehaviour
    {
        public enum Pose { Stand, Sit }

        [Header("Driven by the owner every frame")]
        public Pose pose;
        public float speed;          // m/s over the ground
        public bool grounded = true;
        public float crouch;         // 0..1
        public float verticalSpeed;
        /// <summary>When seated: the eye point (a vehicle's CabView marker). The body is placed so its head is there.</summary>
        public Transform seat;
        /// <summary>When seated: the machine's cab controls. Hands go to the joystick grips, feet to the pedals.</summary>
        public FPSBase.Vehicles.CabControls controls;
        /// <summary>True = the camera is in this body's head: draw only its shadow.</summary>
        public System.Func<bool> firstPerson;

        Transform hips, spine, spine1, spine2, neck, head;
        Transform lUp, lLo, lFoot, rUp, rLo, rFoot;
        Transform lArm, lFore, rArm, rFore, lHand, rHand, lThumb, rThumb;
        Transform lToe, rToe, lMid, rMid;
        readonly List<Transform> lFingers = new(), rFingers = new();
        readonly List<Transform> bones = new();
        readonly List<Quaternion> bind = new();
        Vector3 hipsBind;
        Renderer[] renderers;
        bool hidden;
        float phase, idleT, crouchS, airS;

        public Transform Head => head;
        public Transform RightHand => rHand;
        public Transform LeftHand => lHand;
        /// <summary>The middle of the palm (where a grip sits in the hand).</summary>
        public Vector3 Palm(bool right)
        {
            Transform h = right ? rHand : lHand, m = right ? rMid : lMid;
            if (h == null) return Vector3.zero;
            return m != null ? Vector3.Lerp(h.position, m.position, 0.6f) : h.position;
        }
        /// <summary>The ball of the foot (what goes on a pedal).</summary>
        public Vector3 BallOfFoot(bool right)
        {
            Transform f = right ? rFoot : lFoot, t = right ? rToe : lToe;
            if (f == null) return Vector3.zero;
            return t != null ? t.position : f.position;
        }
        public bool Ready => hips != null;

        void Awake()
        {
            var anim = GetComponentInChildren<Animator>();
            if (anim != null) anim.enabled = false; // we pose it ourselves
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform) continue;
                bones.Add(t); bind.Add(t.localRotation);
            }
            hips = Bone("Hips"); spine = Bone("Spine"); spine1 = Bone("Spine1"); spine2 = Bone("Spine2");
            neck = Bone("Neck"); head = Bone("Head");
            lUp = Bone("LeftUpLeg"); lLo = Bone("LeftLeg"); lFoot = Bone("LeftFoot");
            rUp = Bone("RightUpLeg"); rLo = Bone("RightLeg"); rFoot = Bone("RightFoot");
            lArm = Bone("LeftArm"); lFore = Bone("LeftForeArm");
            rArm = Bone("RightArm"); rFore = Bone("RightForeArm");
            lHand = Bone("LeftHand"); rHand = Bone("RightHand");
            foreach (var side in new[] { "Left", "Right" })
                foreach (var f in new[] { "Index", "Middle", "Ring", "Pinky" })
                    for (int j = 1; j <= 3; j++)
                    {
                        var b = Bone($"{side}Hand{f}{j}");
                        if (b != null) (side == "Left" ? lFingers : rFingers).Add(b);
                    }
            lThumb = Bone("LeftHandThumb1"); rThumb = Bone("RightHandThumb1");
            lToe = Bone("LeftToeBase"); rToe = Bone("RightToeBase");
            lMid = Bone("LeftHandMiddle1"); rMid = Bone("RightHandMiddle1");
            if (hips != null) hipsBind = hips.localPosition;
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>
        /// This body is a copy of `src` (another player's avatar): take over its rest pose (the copy was made
        /// mid-pose) and draw it fully.
        /// </summary>
        public void CloneFrom(MannequinRig src)
        {
            for (int i = 0; i < bones.Count && i < src.bind.Count; i++) { bind[i] = src.bind[i]; bones[i].localRotation = bind[i]; }
            hipsBind = src.hipsBind;
            if (hips != null) hips.localPosition = hipsBind;
            firstPerson = () => false;
            hidden = true; // makes Visibility() switch the renderers back on next frame
        }

        Transform Bone(string n)
        {
            foreach (var t in bones)
            {
                string s = t.name;
                int c = s.LastIndexOf(':');
                if ((c >= 0 ? s.Substring(c + 1) : s) == n) return t;
            }
            return null;
        }

        static void Rot(Transform t, Vector3 axis, float deg)
        {
            if (t != null) t.rotation = Quaternion.AngleAxis(deg, axis) * t.rotation;
        }

        void LateUpdate()
        {
            if (!Ready) return;
            float dt = Time.deltaTime;
            if (pose == Pose.Sit && seat != null)
            {
                // sat in the machine: tipped with it on a slope (its cab's up, not the sky's)
                Vector3 up = (seat.parent != null ? seat.parent : seat).up;
                Vector3 f = Vector3.ProjectOnPlane(seat.forward, up);
                if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(Vector3.forward, up);
                transform.rotation = Quaternion.LookRotation(f.normalized, up);
            }

            for (int i = 0; i < bones.Count; i++) bones[i].localRotation = bind[i];
            hips.localPosition = hipsBind;

            Vector3 R = transform.right, F = transform.forward, U = transform.up;
            idleT += dt;

            if (pose == Pose.Sit)
            {
                // seated: fingers curl round the grips (done in the T-pose, palms down, before the arms move:
                // each joint bends about the character's forward axis, and the arm carries the hand along)
                foreach (var b in lFingers) Rot(b, F, 38f);
                foreach (var b in rFingers) Rot(b, F, -38f);
                Rot(lThumb, U, 25f); Rot(rThumb, U, -25f);
            }

            // arms hang down from the T-pose, a little out from the body
            Rot(lArm, F, 74f); Rot(rArm, F, -74f);

            if (pose == Pose.Sit)
            {
                bool standing = controls != null && controls.standing;
                if (!standing)
                {
                    Rot(lUp, R, -88f); Rot(rUp, R, -88f);   // thighs forward
                    Rot(lLo, R, 82f); Rot(rLo, R, 82f);     // shins down
                    Rot(lUp, U, 6f); Rot(rUp, U, -6f);
                    Rot(spine, R, -6f);                     // lean back into the seat a touch
                }
                else
                {
                    Rot(lLo, R, 8f); Rot(rLo, R, 8f);       // knees soft, riding the platform
                    Rot(spine, R, 8f);                      // leaning into the hip pad
                }
                Rot(lArm, R, -38f); Rot(rArm, R, -38f); // hands forward onto the controls
                Rot(lFore, R, -48f); Rot(rFore, R, -48f);
                Rot(head, R, 6f + Mathf.Sin(idleT * 0.7f) * 2f);
                if (seat != null && head != null)
                {
                    Vector3 eye = seat.position - U * 0.08f - F * (LimbsInView ? 0.1f : 0.04f);
                    // (with the neck folded away the head bone sits at the neck: use where it would be)
                    Vector3 headAt = head.position;
                    if (neckShrunk && neck != null) headAt = neck.position + U * neckLen;
                    else if (neck != null) neckLen = Vector3.Distance(neck.position, head.position);
                    transform.position += eye - headAt;
                }
                ReachControls(R, F, U);
                Visibility();
                return;
            }

            // ---- on foot
            crouchS = Mathf.MoveTowards(crouchS, crouch, dt * 6f);
            airS = Mathf.MoveTowards(airS, grounded ? 0f : 1f, dt * (grounded ? 8f : 4f));
            float run = Mathf.Clamp01(speed / 6.5f);
            float amp = Mathf.Lerp(0f, 32f, Mathf.Clamp01(speed / 2.5f)) + run * 16f;
            float stride = Mathf.Lerp(1.3f, 2.6f, run);       // metres per full cycle (two steps)
            if (grounded && speed > 0.1f) phase += speed / stride * Mathf.PI * 2f * dt;
            else if (speed <= 0.1f) phase = Mathf.MoveTowards(phase, Mathf.Round(phase / Mathf.PI) * Mathf.PI, dt * 4f);
            amp *= 1f - airS;
            float s = Mathf.Sin(phase), c = Mathf.Cos(phase);

            // crouch: hips drop, thighs forward, knees bend
            float cr = crouchS;
            hips.position -= U * (0.34f * cr);
            Rot(spine, R, 22f * cr + run * 8f + Mathf.Sin(idleT * 1.6f) * 0.8f);

            float lThigh = -s * amp - 55f * cr, rThigh = s * amp - 55f * cr;
            float lKnee = amp * 1.25f * Mathf.Max(0f, c) + 95f * cr + 4f, rKnee = amp * 1.25f * Mathf.Max(0f, -c) + 95f * cr + 4f;
            // in the air: knees up a bit
            lThigh -= 28f * airS; rThigh -= 18f * airS; lKnee += 45f * airS; rKnee += 30f * airS;
            Rot(lUp, R, lThigh); Rot(rUp, R, rThigh);
            Rot(lLo, R, lKnee); Rot(rLo, R, rKnee);
            Rot(lFoot, R, -40f * cr); Rot(rFoot, R, -40f * cr);

            // arms swing against the legs
            float arm = amp * 0.85f;
            Rot(lArm, R, s * arm - 10f * cr); Rot(rArm, R, -s * arm - 10f * cr);
            Rot(lArm, F, -airS * 20f); Rot(rArm, F, airS * 20f);
            float elbow = 12f + amp * 0.7f + run * 30f;
            Rot(lFore, R, -elbow); Rot(rFore, R, -elbow);

            // body bobs with each step
            hips.position += U * (Mathf.Abs(c) * 0.035f * Mathf.Clamp01(amp / 30f) - 0.02f * Mathf.Clamp01(amp / 30f));
            Visibility();
        }

        /// <summary>Hands onto the joystick grips (or the wheel), feet onto the pedals: they follow the controls as they move.</summary>
        void ReachControls(Vector3 R, Vector3 F, Vector3 U)
        {
            if (controls == null) return;
            if (controls.leftGrip != null && lArm != null)
                Hand(lArm, lFore, lHand, lMid, controls.leftGrip.position, lArm.position - R * 0.3f - U * 0.35f - F * 0.15f, F, U);
            if (controls.rightGrip != null && rArm != null)
                Hand(rArm, rFore, rHand, rMid, controls.rightGrip.position, rArm.position + R * 0.3f - U * 0.35f - F * 0.15f, F, U);
            if (controls.standing) return;
            if (controls.leftFoot != null && lUp != null)
                Foot(lUp, lLo, lFoot, lToe, controls.leftFoot, lUp.position + F * 0.6f + U * 0.25f - R * 0.05f, F, U);
            if (controls.rightFoot != null && rUp != null)
                Foot(rUp, rLo, rFoot, rToe, controls.rightFoot, rUp.position + F * 0.6f + U * 0.25f + R * 0.05f, F, U);
        }

        /// <summary>
        /// The palm round the grip: the hand points out from the forearm's way and a little down, the wrist goes a
        /// palm's length back from the grip along it, then the hand is turned so the palm lands right on it.
        /// (A couple of passes: turning the hand moves the palm a little.)
        /// </summary>
        static void Hand(Transform arm, Transform fore, Transform hand, Transform mid, Vector3 grip, Vector3 hint, Vector3 F, Vector3 U)
        {
            if (fore == null || hand == null) return;
            float palm = mid != null ? Vector3.Distance(hand.position, mid.position) * 0.6f : 0.06f;
            Vector3 dir = ((grip - arm.position).normalized + F * 0.4f - U * 0.25f).normalized;
            for (int pass = 0; pass < 2; pass++)
            {
                Limb(arm, fore, hand, grip - dir * palm, hint);
                if (mid != null) hand.rotation = Quaternion.FromToRotation(mid.position - hand.position, dir) * hand.rotation;
                // (whatever is left over, the next pass aims the wrist that much further)
                Vector3 at = mid != null ? Vector3.Lerp(hand.position, mid.position, 0.6f) : hand.position;
                Vector3 miss = grip - at;
                if (miss.sqrMagnitude < 1e-6f) break;
                grip += miss;
            }
        }

        /// <summary>
        /// The ball of the foot on the pedal: the foot lies along the pedal (toes forward and down), the ankle goes
        /// a foot's length back and up from it, then the foot is turned so the toes come down onto the pad.
        /// </summary>
        static void Foot(Transform up, Transform lo, Transform foot, Transform toe, Transform pedal, Vector3 hint, Vector3 F, Vector3 U)
        {
            if (lo == null || foot == null) return;
            Vector3 pad = pedal.position + U * 0.025f; // (the sole's thickness over the pad)
            Vector3 along = Vector3.ProjectOnPlane(pedal.forward, U).sqrMagnitude > 0.1f ? pedal.forward : F;
            Vector3 dir = (along.normalized * 0.75f - pedal.up * 0.55f).normalized;
            if (Vector3.Dot(dir, F) < 0.2f) dir = (F * 0.75f - U * 0.55f).normalized;
            float len = toe != null ? Vector3.Distance(foot.position, toe.position) : 0.12f;
            for (int pass = 0; pass < 2; pass++)
            {
                Limb(up, lo, foot, pad - dir * len, hint);
                if (toe != null) foot.rotation = Quaternion.FromToRotation(toe.position - foot.position, dir) * foot.rotation;
                Vector3 at = toe != null ? toe.position : foot.position;
                Vector3 miss = pedal.position + U * 0.025f - at;
                if (miss.sqrMagnitude < 1e-6f) break;
                pad += miss;
            }
        }

        /// <summary>Two-bone IK: bends a-b-c so c reaches `target`, with the middle joint toward `hint`.</summary>
        static void Limb(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
        {
            if (a == null || b == null || c == null) return;
            Vector3 pa = a.position;
            float la = Vector3.Distance(pa, b.position), lb = Vector3.Distance(b.position, c.position);
            if (la < 1e-4f || lb < 1e-4f) return;
            Vector3 to = target - pa;
            float d = Mathf.Clamp(to.magnitude, Mathf.Abs(la - lb) + 1e-3f, (la + lb) * 0.999f);
            Vector3 dir = to.sqrMagnitude > 1e-8f ? to.normalized : a.forward;
            Vector3 pole = Vector3.ProjectOnPlane(hint - pa, dir);
            if (pole.sqrMagnitude < 1e-8f) pole = Vector3.ProjectOnPlane(Vector3.down, dir);
            pole.Normalize();
            float x = (la * la - lb * lb + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, la * la - x * x));
            Vector3 elbow = pa + dir * x + pole * h;
            a.rotation = Quaternion.FromToRotation(b.position - pa, elbow - pa) * a.rotation;
            Vector3 reach = pa + dir * d;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, reach - b.position) * b.rotation;
        }

        /// <summary>First person in a cab: your own arms and legs are drawn working the controls (only the head is hidden).</summary>
        public bool LimbsInView { get; private set; }
        Vector3 neckScale = Vector3.one; bool neckShrunk; float neckLen = 0.12f;

        void Visibility()
        {
            bool fp = firstPerson != null && firstPerson();
            // seated in first person: show the body (hands on the sticks, feet on the pedals) but fold the head and
            // neck away, so the camera in the head looks out past them. On foot the camera keeps the shadow only.
            bool limbs = fp && pose == Pose.Sit;
            LimbsInView = limbs;
            Transform hideBone = neck != null ? neck : head;
            if (hideBone != null && limbs != neckShrunk)
            {
                if (limbs) { neckScale = hideBone.localScale; hideBone.localScale = Vector3.one * 0.001f; }
                else hideBone.localScale = neckScale;
                neckShrunk = limbs;
            }
            bool shadowOnly = fp && !limbs;
            if (shadowOnly == hidden) return;
            hidden = shadowOnly;
            foreach (var r in renderers)
                if (r != null) r.shadowCastingMode = shadowOnly ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }
    }
}
