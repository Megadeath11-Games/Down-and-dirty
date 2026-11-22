using System.Collections.Generic;
using FPSBase.City;
using FPSBase.Core;
using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// A lifting chain shackled to an excavator's bucket linkage. Chain key: let the chain down (it hangs from the
    /// pin under the bucket), hook it on a concrete block's loop when the hook is right by it, and let the block go
    /// again. A hooked block hangs on the chain and swings under the bucket, so you can set it down where you want
    /// it, one at a time. Added to every excavator at start.
    /// </summary>
    public class BucketChain : MonoBehaviour
    {
        MiniExcavator ex;
        Rigidbody rb;
        public bool Out { get; private set; }
        public ConcreteBlock Held { get; private set; }
        /// <summary>On a friend's game: their machine and their chain (we only draw it).</summary>
        public bool RemoteDriven => ex != null && ex.NetRemote;
        ConfigurableJoint joint;
        float length;
        static readonly List<BucketChain> all = new();
        public static IReadOnlyList<BucketChain> All => all;

        public float Length => length;

        float minLength = -1f;
        /// <summary>
        /// The shortest the chain goes: long enough that a block on it hangs clear below the whole bucket (the
        /// furthest corner of the bucket's steel from the shackle, plus a hand's width), so it can't be pulled up into it.
        /// </summary>
        public float MinLength
        {
            get
            {
                if (minLength > 0f) return minLength;
                float far = 0.3f;
                if (ex != null && ex.bucketPivot != null)
                {
                    Vector3 sh = Shackle;
                    foreach (var c in ex.bucketPivot.GetComponentsInChildren<BoxCollider>(true))
                    {
                        if (!c.name.StartsWith("Tool")) continue;
                        Vector3 h = c.size * 0.5f;
                        for (int i = 0; i < 8; i++)
                        {
                            Vector3 corner = c.transform.TransformPoint(c.center + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
                            far = Mathf.Max(far, Vector3.Distance(sh, corner));
                        }
                    }
                }
                minLength = far + 0.15f;
                return minLength;
            }
        }
        /// <summary>The longest it lets out (m).</summary>
        public float MaxLength => Mathf.Max(MinLength + 1f, 2.6f + 1.2f * (ex != null ? ex.sizeScale : 1f));
        const float LengthSpeed = 0.5f; // m/s, letting it out or taking it in

        /// <summary>Let the chain out (+) or take it in (-) by `dm` metres, between its shortest and longest.</summary>
        public void ChangeLength(float dm)
        {
            float was = length;
            length = Mathf.Clamp(length + dm, MinLength, MaxLength);
            if (Mathf.Approximately(was, length)) return;
            if (joint != null) { joint.linearLimit = new SoftJointLimit { limit = length }; if (Held != null) Held.Body.WakeUp(); }
        }
        /// <summary>Where the chain is shackled: under the bucket's pin, on the linkage.</summary>
        public Vector3 Shackle => ex != null && ex.bucketPivot != null ? ex.bucketPivot.position - ex.bucketPivot.up * 0.12f * ex.sizeScale : transform.position;
        /// <summary>The hook end, hanging straight down when nothing's on it.</summary>
        public Vector3 HookEnd => Held != null && Held.eye != null ? Held.eye.position : rope != null && payout > 0.01f ? rope[RopeN - 1] : Shackle;
        /// <summary>How much chain is let out right now (m): it runs out and back in, it doesn't just appear.</summary>
        public float PaidOut => payout;
        /// <summary>How close a block's loop has to be to the hook to hook it (m).</summary>
        public const float Reach = 0.75f;

        // ------------------------------------------------------------------ turning the block on the chain

        /// <summary>How fast the turn keys turn a block on the chain (deg/s).</summary>
        public const float TurnSpeed = 50f;
        float turnTo;
        /// <summary>The heading the block on the chain is being turned to (deg, about straight up).</summary>
        public float TurnTarget => turnTo;
        /// <summary>Turn the block on the chain by `deg` (+ = clockwise seen from above). Public for the test.</summary>
        public void TurnBy(float deg) { if (Held != null) { turnTo = Mathf.Repeat(turnTo + deg + 180f, 360f) - 180f; Held.Body.WakeUp(); } }

        /// <summary>
        /// The block hangs from one loop, so nothing but the air stops it spinning: steady it on the heading it's
        /// set to (a hand on a tag line), turning it smoothly when the keys turn it.
        /// </summary>
        void HoldHeading()
        {
            var b = Held.Body;
            float err = Mathf.DeltaAngle(Held.Heading, turnTo) * Mathf.Deg2Rad;
            float a = Mathf.Clamp(6f * err - 4f * b.angularVelocity.y, -6f, 6f);
            b.AddTorque(Vector3.up * a, ForceMode.Acceleration);
        }

        void Awake()
        {
            ex = GetComponent<MiniExcavator>();
            rb = GetComponent<Rigidbody>();
            length = 0.9f + 0.36f * (ex != null ? ex.sizeScale : 1f);
        }
        void Start()
        {
            // (after the excavator has built its bucket's hitboxes: the chain is never shorter than the bucket is big)
            length = Mathf.Clamp(length, MinLength, MaxLength);
            // with the chain down the dump marker turns into the hook's reach: green when a loop is close enough
            var mk = GetComponent<DumpMarker>();
            if (mk != null) mk.hookRing = () => Out && Held == null && payout > 0.05f ? (HookEnd, Reach, Nearest(Reach) != null) : null;
        }
        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDisable() { all.Remove(this); if (Held != null) Release(true); }

        void Update()
        {
            RopeStep(Time.deltaTime);
            ShowSnap();
            if (ex == null || !ex.Occupied || ex.NetRemote) return;
            if (Controls.Pressed(GameAction.Hook)) Toggle();
            if (Out || Held != null)
            {
                float ax = Controls.Axis(GameAction.ChainLonger, GameAction.ChainShorter);
                if (Mathf.Abs(ax) > 0.05f) ChangeLength(ax * LengthSpeed * Time.deltaTime);
            }
            if (Held != null)
            {
                float tu = Controls.Axis(GameAction.ChainTurnRight, GameAction.ChainTurnLeft);
                if (Mathf.Abs(tu) > 0.05f) TurnBy(tu * TurnSpeed * Time.deltaTime);
            }
        }

        // ------------------------------------------------------------------ where the block on the chain will go

        GameObject ghost;
        float ghostAt;
        static Material ghostMat;
        /// <summary>Where the block on the chain would snap into the wall if let go now (for the test).</summary>
        public bool SnapShown => ghost != null && ghost.activeSelf;

        /// <summary>A see-through green block where the one on the chain will go into the wall when it's let go.</summary>
        void ShowSnap()
        {
            if (Held == null || !Held.NetMine) { if (ghost != null) ghost.SetActive(false); return; }
            if (Time.time - ghostAt < 0.1f) return;
            ghostAt = Time.time;
            if (!Held.FindSnap(out var p, out var q, out _)) { if (ghost != null) ghost.SetActive(false); return; }
            if (ghost == null)
            {
                ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ghost.name = "Block snap outline";
                Destroy(ghost.GetComponent<Collider>());
                if (ghostMat == null)
                {
                    ghostMat = new Material(Shader.Find("HDRP/Lit"));
                    ghostMat.SetColor("_BaseColor", new Color(0.3f, 1f, 0.35f, 0.35f));
                    ghostMat.SetFloat("_SurfaceType", 1f); ghostMat.SetFloat("_BlendMode", 0f); ghostMat.SetFloat("_ZWrite", 0f);
                    ghostMat.SetFloat("_DoubleSidedEnable", 1f);
                    try { UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(ghostMat); } catch { }
                }
                var r = ghost.GetComponent<MeshRenderer>();
                r.sharedMaterial = ghostMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            ghost.SetActive(true);
            ghost.transform.SetPositionAndRotation(p + q * Vector3.up * (Held.Size.y * 0.5f + 0.01f), q);
            ghost.transform.localScale = Held.Size + Vector3.one * 0.02f;
        }

        void OnDestroy() { if (ghost != null) Destroy(ghost); }

        // ------------------------------------------------------------------ the free chain: links that swing and hang

        const int RopeN = 14;
        Vector3[] rope, ropePrev;
        float payout;
        const float PayoutSpeed = 1.1f; // m/s, running out or reeling in

        /// <summary>
        /// The chain hanging free: a string of links pinned at the shackle, each pulled down by gravity and held to
        /// its neighbours, so it swings when the arm moves or slews, trails behind, and lies on the ground when let
        /// down onto it. Let out it runs down a link at a time; stowed it reels back up.
        /// </summary>
        void RopeStep(float dt)
        {
            float want = Held != null ? length : Out ? length : 0f;
            payout = Mathf.MoveTowards(payout, want, PayoutSpeed * dt);
            if (Held != null) payout = length;
            Vector3 top = Shackle;
            if (rope == null)
            {
                rope = new Vector3[RopeN]; ropePrev = new Vector3[RopeN];
                for (int i = 0; i < RopeN; i++) rope[i] = ropePrev[i] = top;
            }
            if (payout <= 0.01f || Held != null)
            {
                // stowed (or straight on a block): it starts from the shackle next time
                for (int i = 0; i < RopeN; i++)
                {
                    Vector3 p = Held != null ? Vector3.Lerp(top, HookEnd, i / (RopeN - 1f)) : top;
                    rope[i] = ropePrev[i] = p;
                }
                return;
            }
            if (dt <= 0f) return;
            float seg = payout / (RopeN - 1);
            float h = Mathf.Min(dt, 1f / 30f);
            Vector3 g = Physics.gravity * h * h;
            var site = FPSBase.Mining.DigSite.At(top);
            for (int i = 1; i < RopeN; i++)
            {
                Vector3 p = rope[i];
                Vector3 vel = (p - ropePrev[i]) * 0.985f; // (air and the links rubbing: it settles after a while)
                ropePrev[i] = p;
                rope[i] = p + vel + g;
            }
            rope[0] = top;
            for (int it = 0; it < 12; it++)
            {
                rope[0] = top;
                for (int i = 0; i < RopeN - 1; i++)
                {
                    Vector3 d = rope[i + 1] - rope[i];
                    float L = d.magnitude;
                    if (L < 1e-6f) { if (i > 0 || it == 0) rope[i + 1] = rope[i] + Vector3.down * seg * 0.5f; continue; }
                    float diff = (L - seg) / L;
                    if (i == 0) rope[i + 1] -= d * diff;
                    else { rope[i] += d * diff * 0.5f; rope[i + 1] -= d * diff * 0.5f; }
                }
                // the ground (the dig field, or anything solid under it): the links lie on it
                for (int i = 1; i < RopeN; i++)
                {
                    float gy = float.MinValue;
                    if (site != null && site.Contains(rope[i])) gy = site.SurfaceY(rope[i]) + 0.02f;
                    if (rope[i].y < gy)
                    {
                        rope[i].y = gy;
                        Vector3 v = rope[i] - ropePrev[i]; v.x *= 0.6f; v.z *= 0.6f; // (dragged on the dirt)
                        ropePrev[i] = rope[i] - new Vector3(v.x, 0f, v.z);
                    }
                }
            }
        }

        /// <summary>The chain key: let go of a block / hook the one at the hook / let the chain out or take it in.</summary>
        public void Toggle()
        {
            if (Held != null) { Release(); return; }
            if (Out)
            {
                var b = Nearest(Reach);
                if (b != null) { Hook(b); return; }
                Out = false; GameUI.Toast("Chain stowed"); return;
            }
            Out = true;
            var near = Nearest(Reach);
            GameUI.Toast(near != null ? "Chain down: the hook's right by a block's loop, press again to hook it" : "Chain down: put the hook by a block's lifting loop and press again to hook it");
        }

        /// <summary>The free block whose loop is nearest the hook, within `reach`.</summary>
        public ConcreteBlock Nearest(float reach)
        {
            ConcreteBlock best = null; float bd = reach;
            Vector3 hook = HookEnd;
            Vector3 mid = rope != null && payout > 0.01f ? rope[RopeN / 2] : Vector3.Lerp(Shackle, hook, 0.5f);
            foreach (var b in ConcreteBlock.All)
            {
                if (b == null || b.eye == null || b.HookedBy != null) continue;
                // (the hook swings: anywhere along the lower half of the chain will do)
                Vector3 e = b.eye.position;
                float d = Mathf.Min(Vector3.Distance(e, hook), DistToSegment(e, mid, hook));
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }

        static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t);
        }

        public void Hook(ConcreteBlock b, bool fromNet = false)
        {
            if (b == null || Held != null) return;
            if (b.ChainedTo != null)
            {
                if (fromNet) b.Unchain(true);
                else { GameUI.Toast("It's chained down: undo its chains first (look at it, F)"); return; }
            }
            if (b.HookedBy != null) b.HookedBy.Release(fromNet);
            Out = true;
            Held = b; b.HookedBy = this;
            turnTo = b.Heading;
            if (!fromNet) b.Claim();
            if (b.NetMine && !b.Body.isKinematic) MakeJoint(b);
            b.NetDirty = true;
            if (!fromNet)
            {
                float ratio = b.Info.kg / Mathf.Max(1f, rb != null ? rb.mass : 1f);
                string keys = $"{Controls.KeyName(Controls.Get(GameAction.ChainTurnLeft))} / {Controls.KeyName(Controls.Get(GameAction.ChainTurnRight))} turns it";
                GameUI.Toast(ratio > 0.35f ? $"Hooked the {b.Info.name.ToLower()} ({b.Info.kg:N0} kg): heavy for this machine, keep it close in. {keys}" : $"Hooked the {b.Info.name.ToLower()} ({b.Info.kg:N0} kg). {keys}");
            }
        }

        void MakeJoint(ConcreteBlock b)
        {
            var body = b.Body;
            joint = b.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = rb;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = b.transform.InverseTransformPoint(b.eye.position);
            joint.connectedAnchor = rb.transform.InverseTransformPoint(Shackle);
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Limited;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.linearLimit = new SoftJointLimit { limit = length };
            joint.linearLimitSpring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
            joint.enableCollision = true; // (it bangs into the bucket, the arm and the tracks like anything else)
            joint.massScale = 1f; joint.connectedMassScale = 1f;
            body.linearDamping = 0.35f; body.angularDamping = 2.5f; // (a chain lift swings, but settles)
            body.WakeUp();
        }

        public void Release(bool fromNet = false)
        {
            var b = Held;
            if (b == null) return;
            if (joint != null) Destroy(joint);
            joint = null;
            var body = b.Body;
            if (body != null) { body.linearDamping = 0.05f; body.angularDamping = 0.05f; body.WakeUp(); }
            b.HookedBy = null; Held = null;
            b.NetDirty = true;
            if (fromNet) return;
            // let go by another block: it squares up and goes into the wall
            if (b.SnapToWall()) GameUI.Toast($"Set the {b.Info.name.ToLower()} into the wall");
            else GameUI.Toast($"Unhooked the {b.Info.name.ToLower()}");
        }

        // the bucket, stick and boom pass round a block on the chain instead of knocking it about
        void IgnoreTools(ConcreteBlock b, bool ignore)
        {
            var mine = GetComponentsInChildren<Collider>(true);
            var its = b.GetComponentsInChildren<Collider>(true);
            foreach (var a in mine) if (a != null && a.name.StartsWith("Tool")) foreach (var c in its) if (c != null) Physics.IgnoreCollision(a, c, ignore);
        }

        void FixedUpdate()
        {
            if (Held == null) return;
            if (Held == null || !Held.isActiveAndEnabled) { Held = null; joint = null; return; }
            // the shackle moves with the arm: keep the chain's top end on it
            if (joint == null && Held.NetMine && !Held.Body.isKinematic) MakeJoint(Held);
            if (joint != null) joint.connectedAnchor = rb.transform.InverseTransformPoint(Shackle);
            if (joint != null && Held.NetMine) HoldHeading();
        }

        // ------------------------------------------------------------------ drawing: steel links from shackle to hook

        static Mesh link;
        static Material steel;
        static readonly Matrix4x4[] mats = new Matrix4x4[256];

        void LateUpdate()
        {
            if (!Out && Held == null && payout <= 0.01f) return;
            if (link == null) link = BuildLink();
            if (steel == null) steel = Paint(this, "Chain steel", new Color(0.23f, 0.23f, 0.25f), 0.7f, 0.45f);
            if (steel == null) return;
            Vector3 a = Shackle, b = HookEnd;
            if (!Finite(a) || !Finite(b)) { rope = null; return; } // (never hand the renderer a broken chain; it restarts at the shackle)
            bool free = Held == null && rope != null;
            float d = Vector3.Distance(a, b);
            // slack chain sags in the middle
            float sag = Mathf.Sqrt(Mathf.Max(0f, length * length - d * d)) * 0.5f;
            float s = Mathf.Max(0.6f, ex != null ? ex.sizeScale * 0.55f : 1f);
            float pitch = 0.09f * s;
            int n = Mathf.Clamp(Mathf.CeilToInt((free ? payout : length) / pitch), 2, mats.Length - 2);
            Vector3 prev = a;
            int m = 0;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                Vector3 p = free ? RopeAt(t) : Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t));
                Vector3 dir = p - prev;
                if (dir.sqrMagnitude > 1e-8f)
                {
                    var rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, (i & 1) == 0 ? 0f : 90f);
                    mats[m++] = Matrix4x4.TRS((p + prev) * 0.5f, rot, new Vector3(s, s, s * dir.magnitude / pitch));
                }
                prev = p;
            }
            var rp = new RenderParams(steel) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true, worldBounds = new Bounds((a + b) * 0.5f, Vector3.one * (length * 2f + 2f)) };
            // (never an empty draw: just let down, the chain is a few millimetres long and has no link to draw yet,
            // and asking for zero instances crashed the D3D12 player in ConstantBuffersD3D12::ResizeConstantBuffers)
            if (m > 0) Graphics.RenderMeshInstanced(rp, link, 0, mats, m);
            // the hook: a big safety-yellow hook you can see from the cab, its eye on the last link
            if (hookMesh == null) hookMesh = BuildHook();
            if (hookPaint == null) hookPaint = Paint(this, "Hook paint", new Color(1f, 0.78f, 0.05f), 0f, 0.35f);
            if (hookPaint == null) return;
            Vector3 before = free ? RopeAt(Mathf.Clamp01(1f - pitch * 2f / Mathf.Max(0.05f, payout))) : Vector3.Lerp(a, b, 0.9f);
            Vector3 hup = (before - b).sqrMagnitude > 1e-6f ? (before - b).normalized : Vector3.up;
            Vector3 hfw = Vector3.ProjectOnPlane(transform.right, hup);
            if (hfw.sqrMagnitude < 1e-4f) hfw = Vector3.ProjectOnPlane(Vector3.forward, hup);
            var hrp = new RenderParams(hookPaint) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true, worldBounds = new Bounds(b, Vector3.one * 2f) };
            hookMat[0] = Matrix4x4.TRS(b, Quaternion.LookRotation(hfw, hup), Vector3.one * s * 1.25f);
            Graphics.RenderMeshInstanced(hrp, hookMesh, 0, hookMat, 1);
        }

        /// <summary>
        /// A paint for the chain, copied from a material the machine already wears (its track shoes, drawn the same
        /// instanced way, or else any lit part of it) and recoloured. A material made from scratch out of
        /// Shader.Find("HDRP/Lit") has none of HDRP's set-up, and drawing it crashed the D3D12 player (in
        /// ConstantBuffersD3D12::ResizeConstantBuffers) the moment the chain was let down; the editor drew it fine.
        /// </summary>
        static Material Paint(BucketChain c, string name, Color col, float metal, float smooth)
        {
            Material src = null;
            var ta = c.GetComponentInChildren<TrackAnimator>(true);
            if (ta != null && ta.shoeMaterial != null) src = ta.shoeMaterial;
            if (src == null)
                foreach (var r in c.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var m = r.sharedMaterial;
                    if (m != null && m.shader != null && m.shader.name == "HDRP/Lit") { src = m; break; }
                }
            if (src == null) return null; // (nothing safe to copy: no chain drawn rather than a crash)
            var p = new Material(src) { name = name, enableInstancing = true };
            foreach (var t in new[] { "_BaseColorMap", "_MaskMap", "_NormalMap", "_DetailMap", "_EmissiveColorMap" })
                if (p.HasProperty(t)) p.SetTexture(t, null);
            if (p.HasProperty("_BaseColor")) p.SetColor("_BaseColor", col);
            p.color = col;
            if (p.HasProperty("_Metallic")) p.SetFloat("_Metallic", metal);
            if (p.HasProperty("_Smoothness")) p.SetFloat("_Smoothness", smooth);
            return p;
        }

        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        /// <summary>A point along the free chain, 0 = the shackle … 1 = the hook.</summary>
        Vector3 RopeAt(float t)
        {
            float f = Mathf.Clamp01(t) * (RopeN - 1);
            int i = Mathf.Min((int)f, RopeN - 2);
            return Vector3.Lerp(rope[i], rope[i + 1], f - i);
        }

        static Mesh hookMesh;
        static Material hookPaint;
        static readonly Matrix4x4[] hookMat = new Matrix4x4[1];

        /// <summary>
        /// A lifting hook (about 26 cm tall at scale 1) hanging from its eye at the origin: a ring eye, a shank, a thick
        /// curved throat opening to +z with its point turned up, and the spring latch across the mouth.
        /// </summary>
        static Mesh BuildHook()
        {
            var v = new List<Vector3>(); var t = new List<int>();
            void Bar(Vector3 p0, Vector3 p1, float r)
            {
                Vector3 d = p1 - p0; float L = d.magnitude; if (L < 1e-5f) return;
                var q = Quaternion.LookRotation(d / L, Mathf.Abs(d.y) / L > 0.9f ? Vector3.forward : Vector3.up);
                Vector3 c = (p0 + p1) * 0.5f; int o = v.Count;
                for (int i = 0; i < 8; i++)
                    v.Add(c + q * new Vector3((i & 1) == 0 ? -r : r, (i & 2) == 0 ? -r : r, (i & 4) == 0 ? -L * 0.5f - r * 0.5f : L * 0.5f + r * 0.5f));
                int[] f = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
                foreach (int k in f) t.Add(o + k);
            }
            void Arc(Vector3 c, float rad, float a0, float a1, int seg, float r)
            {
                Vector3 P(float a) => c + new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a)) * rad;
                for (int i = 0; i < seg; i++) Bar(P(Mathf.Lerp(a0, a1, i / (float)seg)), P(Mathf.Lerp(a0, a1, (i + 1f) / seg)), r);
            }
            Arc(new Vector3(0f, -0.035f, 0f), 0.035f, 0f, Mathf.PI * 2f, 10, 0.009f);         // the eye
            Bar(new Vector3(0f, -0.07f, 0f), new Vector3(0f, -0.2f, 0f), 0.017f);              // shank
            Arc(new Vector3(0f, -0.2f, 0.045f), 0.045f, Mathf.PI, Mathf.PI * 2.05f, 9, 0.019f); // throat, round to the front
            Bar(new Vector3(0f, -0.195f, 0.09f), new Vector3(0f, -0.15f, 0.095f), 0.014f);     // point turned up
            Bar(new Vector3(0f, -0.11f, 0f), new Vector3(0f, -0.15f, 0.09f), 0.005f);          // latch
            var fv = new List<Vector3>(); var ft = new List<int>();
            for (int i = 0; i < t.Count; i++) { fv.Add(v[t[i]]); ft.Add(i); }
            var m = new Mesh { name = "Lifting hook" };
            m.SetVertices(fv); m.SetTriangles(ft, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>One oval chain link along z, about 9 cm long (at scale 1), made of four bars.</summary>
        static Mesh BuildLink()
        {
            var v = new List<Vector3>(); var t = new List<int>();
            void Box(Vector3 c, Vector3 sz)
            {
                Vector3 h = sz * 0.5f; int o = v.Count;
                for (int i = 0; i < 8; i++) v.Add(c + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
                int[] f = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
                foreach (int k in f) t.Add(o + k);
            }
            const float r = 0.011f, w = 0.025f, l = 0.05f;
            Box(new Vector3(-w, 0f, 0f), new Vector3(r * 2f, r * 2f, l));
            Box(new Vector3(w, 0f, 0f), new Vector3(r * 2f, r * 2f, l));
            Box(new Vector3(0f, 0f, l * 0.5f), new Vector3(w * 2f + r * 2f, r * 2f, r * 2f));
            Box(new Vector3(0f, 0f, -l * 0.5f), new Vector3(w * 2f + r * 2f, r * 2f, r * 2f));
            // flat-shaded: split the corners
            var fv = new List<Vector3>(); var ft = new List<int>();
            for (int i = 0; i < t.Count; i++) { fv.Add(v[t[i]]); ft.Add(i); }
            var m = new Mesh { name = "Chain link" };
            m.SetVertices(fv); m.SetTriangles(ft, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
