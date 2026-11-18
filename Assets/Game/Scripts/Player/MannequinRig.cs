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
        Transform lArm, lFore, rArm, rFore, lHand, rHand;
        readonly List<Transform> bones = new();
        readonly List<Quaternion> bind = new();
        Vector3 hipsBind;
        Renderer[] renderers;
        bool hidden;
        float phase, idleT, crouchS, airS;

        public Transform Head => head;
        public Transform RightHand => rHand;
        public Transform LeftHand => lHand;
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
                Vector3 f = Vector3.ProjectOnPlane(seat.forward, Vector3.up);
                if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
            }

            for (int i = 0; i < bones.Count; i++) bones[i].localRotation = bind[i];
            hips.localPosition = hipsBind;

            Vector3 R = transform.right, F = transform.forward, U = transform.up;
            idleT += dt;

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
                    Vector3 eye = seat.position - Vector3.up * 0.08f - F * 0.04f;
                    transform.position += eye - head.position;
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
                Limb(lArm, lFore, lHand, Wrist(controls.leftGrip.position, lArm.position), lArm.position - R * 0.3f - U * 0.35f - F * 0.15f);
            if (controls.rightGrip != null && rArm != null)
                Limb(rArm, rFore, rHand, Wrist(controls.rightGrip.position, rArm.position), rArm.position + R * 0.3f - U * 0.35f - F * 0.15f);
            if (controls.standing) return;
            if (controls.leftFoot != null && lUp != null)
                Limb(lUp, lLo, lFoot, controls.leftFoot.position + U * 0.07f, lUp.position + F * 0.6f + U * 0.25f - R * 0.05f);
            if (controls.rightFoot != null && rUp != null)
                Limb(rUp, rLo, rFoot, controls.rightFoot.position + U * 0.07f, rUp.position + F * 0.6f + U * 0.25f + R * 0.05f);
        }

        // the wrist sits a hand's length short of the grip
        static Vector3 Wrist(Vector3 grip, Vector3 shoulder) => grip - (grip - shoulder).normalized * 0.07f;

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

        void Visibility()
        {
            bool fp = firstPerson != null && firstPerson();
            if (fp == hidden) return;
            hidden = fp;
            foreach (var r in renderers)
                if (r != null) r.shadowCastingMode = fp ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }
    }
}
