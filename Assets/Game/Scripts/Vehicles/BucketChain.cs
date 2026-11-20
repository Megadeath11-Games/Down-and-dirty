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
        /// <summary>Where the chain is shackled: under the bucket's pin, on the linkage.</summary>
        public Vector3 Shackle => ex != null && ex.bucketPivot != null ? ex.bucketPivot.position - ex.bucketPivot.up * 0.12f * ex.sizeScale : transform.position;
        /// <summary>The hook end, hanging straight down when nothing's on it.</summary>
        public Vector3 HookEnd => Held != null && Held.eye != null ? Held.eye.position : Shackle + Vector3.down * length;

        void Awake()
        {
            ex = GetComponent<MiniExcavator>();
            rb = GetComponent<Rigidbody>();
            length = 0.9f + 0.36f * (ex != null ? ex.sizeScale : 1f);
        }
        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDisable() { all.Remove(this); if (Held != null) Release(true); }

        void Update()
        {
            if (ex == null || !ex.Occupied || ex.NetRemote) return;
            if (Controls.Pressed(GameAction.Hook)) Toggle();
        }

        /// <summary>The chain key: let go of a block / hook the one at the hook / let the chain out or take it in.</summary>
        public void Toggle()
        {
            if (Held != null) { Release(); return; }
            if (Out)
            {
                var b = Nearest(0.75f);
                if (b != null) { Hook(b); return; }
                Out = false; GameUI.Toast("Chain stowed"); return;
            }
            Out = true;
            var near = Nearest(0.75f);
            GameUI.Toast(near != null ? "Chain down: the hook's right by a block's loop, press again to hook it" : "Chain down: put the hook by a block's lifting loop and press again to hook it");
        }

        /// <summary>The free block whose loop is nearest the hook, within `reach`.</summary>
        public ConcreteBlock Nearest(float reach)
        {
            ConcreteBlock best = null; float bd = reach;
            Vector3 hook = Shackle + Vector3.down * length;
            foreach (var b in ConcreteBlock.All)
            {
                if (b == null || b.eye == null || b.HookedBy != null) continue;
                // (the hook swings: anywhere along the lower half of the chain will do)
                Vector3 e = b.eye.position;
                float d = Mathf.Min(Vector3.Distance(e, hook), DistToSegment(e, Shackle + Vector3.down * length * 0.5f, hook));
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
            if (!fromNet) b.Claim();
            IgnoreTools(b, true);
            if (b.NetMine && !b.Body.isKinematic) MakeJoint(b);
            b.NetDirty = true;
            if (!fromNet)
            {
                float ratio = b.Info.kg / Mathf.Max(1f, rb != null ? rb.mass : 1f);
                GameUI.Toast(ratio > 0.35f ? $"Hooked the {b.Info.name.ToLower()} ({b.Info.kg:N0} kg): heavy for this machine, keep it close in" : $"Hooked the {b.Info.name.ToLower()} ({b.Info.kg:N0} kg)");
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
            joint.enableCollision = false;
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
            IgnoreTools(b, false);
            var body = b.Body;
            if (body != null) { body.linearDamping = 0.05f; body.angularDamping = 0.05f; body.WakeUp(); }
            b.HookedBy = null; Held = null;
            b.NetDirty = true;
            if (!fromNet) GameUI.Toast($"Unhooked the {b.Info.name.ToLower()}");
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
        }

        // ------------------------------------------------------------------ drawing: steel links from shackle to hook

        static Mesh link;
        static Material steel;
        static readonly Matrix4x4[] mats = new Matrix4x4[256];

        void LateUpdate()
        {
            if (!Out && Held == null) return;
            if (link == null) link = BuildLink();
            if (steel == null)
            {
                var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
                steel = new Material(sh) { name = "Chain steel", color = new Color(0.23f, 0.23f, 0.25f), enableInstancing = true };
                if (steel.HasProperty("_Metallic")) steel.SetFloat("_Metallic", 0.7f);
                if (steel.HasProperty("_Smoothness")) steel.SetFloat("_Smoothness", 0.45f);
            }
            Vector3 a = Shackle, b = HookEnd;
            float d = Vector3.Distance(a, b);
            // slack chain sags in the middle
            float sag = Mathf.Sqrt(Mathf.Max(0f, length * length - d * d)) * 0.5f;
            float s = Mathf.Max(0.6f, ex != null ? ex.sizeScale * 0.55f : 1f);
            float pitch = 0.09f * s;
            int n = Mathf.Clamp(Mathf.CeilToInt(length / pitch), 2, mats.Length - 2);
            Vector3 prev = a;
            int m = 0;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                Vector3 p = Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t));
                Vector3 dir = p - prev;
                if (dir.sqrMagnitude > 1e-8f)
                {
                    var rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, (i & 1) == 0 ? 0f : 90f);
                    mats[m++] = Matrix4x4.TRS((p + prev) * 0.5f, rot, new Vector3(s, s, s * dir.magnitude / pitch));
                }
                prev = p;
            }
            // the hook
            mats[m++] = Matrix4x4.TRS(b + Vector3.up * 0.04f * s, Quaternion.LookRotation(transform.forward), new Vector3(1.8f * s, 1.8f * s, 1.2f * s));
            var rp = new RenderParams(steel) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true, worldBounds = new Bounds((a + b) * 0.5f, Vector3.one * (length * 2f + 2f)) };
            Graphics.RenderMeshInstanced(rp, link, 0, mats, m);
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
