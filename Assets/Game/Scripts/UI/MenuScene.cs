using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FPSBase.UI
{
    /// <summary>
    /// Behind the main menu: an excavator digging a pile and loading dump trucks in turn, while a dozer keeps
    /// pushing dirt up to it. Just the models (copies of the dealer's machines with their scripts and physics
    /// taken off), moved by this script on a clear patch of ground near you, filmed by its own slowly drifting
    /// camera. Runs in real time (the world itself waits while the menu is up). Gone when the menu closes.
    /// </summary>
    public partial class MenuScene : MonoBehaviour
    {
        static MenuScene inst;
        static bool tried;
        public static bool Showing => inst != null && inst.ready;

        static bool stopped;
        /// <summary>Off, and kept off until the next fresh world (hosting or joining a game: nothing of it may linger).</summary>
        public static void Stop() { stopped = true; Want(false); }
        /// <summary>A fresh world: the menu may show its machines again.</summary>
        public static void Allow() => stopped = false;

        /// <summary>Called every frame by the menu: on while the main menu is up.</summary>
        public static void Want(bool on)
        {
            if (!on)
            {
                tried = false;
                if (inst != null) { inst.EndLive(); inst.Restore(); Destroy(inst.gameObject); inst = null; }
                return;
            }
            if (inst != null || tried || stopped) return;
            tried = true;
            if (Application.isBatchMode && SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var go = new GameObject("Menu Scene");
            inst = go.AddComponent<MenuScene>();
            bool ok = false;
            // the real machines on the real dirt if there's room on the dirt field; else the stand-in models
            try { ok = inst.BuildLive(); }
            catch (System.Exception e) { Debug.LogWarning("[MenuScene] live: " + e); inst.EndLive(); }
            if (!ok)
            {
                inst.Restore();
                try { ok = inst.Build(); }
                catch (System.Exception e) { Debug.LogWarning("[MenuScene] " + e.Message); }
            }
            if (!ok) { inst.Restore(); Destroy(go); inst = null; }
        }

        bool ready;
        Camera cam, hidden;
        Transform root;
        Vector3 camLook, camBase;
        float t0;
        Material dirt;

        // ---- the machines' moving parts
        Transform ex, exUpper, exBoom, exStick, exBucket, exLoad;
        Vector3 exLoadScale, exLoadPos;
        Transform dz, dzLift;
        float dzBladeZ;
        class Truck { public Transform t, bedPivot, bed, mound; public Vector3 bedSize, bedPos; public Transform[] wheels; public float[] radius; public float spin, fill, z, speed; public int scoops; }
        readonly List<Truck> trucks = new();
        Transform pile, pushMound, stream;
        Vector3 pileBase;
        float pileAmount = 0.7f;

        // ---- the layout (in the scene's own frame: the excavator at 0, facing +z)
        Vector3 pileAt, truckSpot;
        float truckX, loadZ;
        const float TruckStart = -75f, TruckEnd = 85f, QueueGap = 15f;

        struct Pose { public float boom, stick, bucket, swing; public Pose(float b, float s, float k, float w) { boom = b; stick = s; bucket = k; swing = w; } }
        Pose pose;
        static readonly Pose Reach = new(-8f, -50f, 15f, 0f), DigEnd = new(-14f, -112f, -105f, 0f), Lift = new(28f, -100f, -130f, 0f);
        const float DumpSwing = 90f;
        float overBoom = 34f, overStick = -72f;
        Pose Over(float bucket) => new(overBoom, overStick, bucket, DumpSwing);

        /// <summary>The box round a model's meshes, in `frame`'s coordinates (leaving out anything under `skip`).</summary>
        static Bounds Extent(Transform m, Transform frame, Transform skip = null)
        {
            bool any = false; var bb = new Bounds();
            foreach (var r in m.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (skip != null && r.transform.IsChildOf(skip)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 l = frame.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (!any) { bb = new Bounds(l, Vector3.zero); any = true; } else bb.Encapsulate(l);
                }
            }
            return bb;
        }

        bool Build()
        {
            var dealer = FindFirstObjectByType<FPSBase.Mining.VehicleDealer>();
            var main = Camera.main;
            if (dealer == null || dealer.listings == null || main == null) return false;
            GameObject Find(string exact, System.Type type)
            {
                foreach (var l in dealer.listings) if (l.prefab != null && (l.name == exact || l.prefab.name == exact)) return l.prefab;
                foreach (var l in dealer.listings) if (l.prefab != null && l.prefab.GetComponent(type) != null) return l.prefab;
                return null;
            }
            var exP = Find("Excavator 20 t", typeof(FPSBase.Vehicles.MiniExcavator));
            var dzP = Find("Medium Dozer 22 t", typeof(FPSBase.Vehicles.Bulldozer));
            var trP = Find("Haul Truck 25t", typeof(FPSBase.Vehicles.DumpPickup));
            if (exP == null || trP == null) return false;

            Vector3 around = main.transform.position;
            if (!FindSpot(around, out Vector3 spot, out float yaw)) return false;

            root = new GameObject("Menu machines").transform;
            root.SetParent(transform, false);
            root.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));

            // two dump trucks taking turns: one loading, the next waiting
            dirt = FindDirt(trP);
            for (int i = 0; i < 2; i++)
            {
                var tM = Strip(trP, out var tC);
                var dp = (FPSBase.Vehicles.DumpPickup)tC;
                var tr = new Truck { t = tM.transform, bedPivot = dp.bedPivot, bed = dp.bedContents, mound = dp.bedMound, bedSize = dp.bedInnerSize };
                if (tr.bed != null) tr.bedPos = tr.bed.localPosition;
                var wv = new List<Transform>(); var wr = new List<float>();
                if (dp.wheelVisuals != null)
                    for (int w = 0; w < dp.wheelVisuals.Length; w++)
                    {
                        if (dp.wheelVisuals[w] == null) continue;
                        var wc = dp.wheels != null && w < dp.wheels.Length ? dp.wheels[w] : null;
                        if (wc != null)
                        {
                            Vector3 c = wc.transform.TransformPoint(wc.center) - wc.transform.up * wc.suspensionDistance * (1f - wc.suspensionSpring.targetPosition);
                            dp.wheelVisuals[w].position = c;
                        }
                        wv.Add(dp.wheelVisuals[w]);
                        wr.Add(wc != null ? wc.radius : 0.55f);
                    }
                tr.wheels = wv.ToArray(); tr.radius = wr.ToArray();
                Destroy(tC);
                foreach (var wc in tM.GetComponentsInChildren<WheelCollider>(true)) DestroyImmediate(wc);
                tr.speed = 0f;
                trucks.Add(tr);
                SetFill(tr, 0f);
                Measure(tr.t);
            }

            // the excavator
            var exM = Strip(exP, out var exC);
            var e = (FPSBase.Vehicles.MiniExcavator)exC;
            ex = exM.transform;
            exUpper = e.upper; exBoom = e.boomPivot; exStick = e.stickPivot; exBucket = e.bucketPivot; exLoad = e.bucketLoadVisual;
            var tip = e.bucketTip;
            if (exLoad != null) { exLoadScale = exLoad.localScale; exLoadPos = exLoad.localPosition; }
            Destroy(exC);
            pose = Lift; ApplyPose();
            Measure(ex);
            Place(ex, Vector3.zero, 0f);

            // where the pile goes (under the bucket reaching out) and where a truck's bed goes (under it swung round)
            Vector3 TipAt(Pose p) { pose = p; ApplyPose(); return root.InverseTransformPoint(tip != null ? tip.position : exBucket.position); }
            Vector3 dig = TipAt(new Pose(-14f, -80f, -40f, 0f));
            // swung round over the truck: far enough out that the truck clears the machine's cab and boom foot,
            // high enough to clear the sides of its bed
            var t0m = trucks[0].t;
            t0m.SetPositionAndRotation(root.position, root.rotation);
            var tb = Extent(t0m, t0m);
            Vector3 bedLocal = trucks[0].bed != null ? t0m.InverseTransformPoint(trucks[0].bed.position) : Vector3.zero;
            float bedTop = (lift.TryGetValue(t0m, out float tl) ? tl : 0f) + bedLocal.y + trucks[0].bedSize.y + 0.9f;
            pose = new Pose(0f, -90f, -130f, DumpSwing); ApplyPose();
            var ub = Extent(exUpper, exUpper, exBoom);
            float cabReach = Mathf.Max(Mathf.Abs(ub.min.z), Mathf.Abs(ub.max.z), Mathf.Abs(ub.min.x), Mathf.Abs(ub.max.x)) * 0.8f;
            float needX = cabReach + 0.7f - tb.min.x + bedLocal.x;
            Vector3 over = Vector3.zero; bool found = false; float bestX = -1f;
            foreach (float bm in new[] { 38f, 32f, 26f, 20f, 14f })
            {
                for (float st = -100f; st <= -30f && !found; st += 5f)
                {
                    overBoom = bm; overStick = st;
                    Vector3 tp = TipAt(Over(-130f));
                    if (tp.x > bestX) { bestX = tp.x; over = tp; }
                    if (tp.x >= needX && tp.y >= bedTop) { over = tp; found = true; }
                }
                if (found) break;
            }
            if (!found)
            {
                // as far as it reaches
                float bx = -1f, bb = 34f, bs = -72f;
                foreach (float bm in new[] { 38f, 32f, 26f, 20f, 14f })
                    for (float st = -100f; st <= -30f; st += 5f) { overBoom = bm; overStick = st; Vector3 tp = TipAt(Over(-130f)); if (tp.y >= bedTop && tp.x > bx) { bx = tp.x; bb = bm; bs = st; over = tp; } }
                overBoom = bb; overStick = bs;
            }
            pose = Lift; ApplyPose();
            truckX = over.x - bedLocal.x;
            loadZ = over.z - bedLocal.z;
            for (int i = 0; i < trucks.Count; i++)
            {
                trucks[i].z = i == 0 ? loadZ : TruckStart;
                Place(trucks[i].t, new Vector3(truckX, 0f, trucks[i].z), 0f);
            }

            pileAt = new Vector3(0f, 0f, Mathf.Max(5f, dig.z));
            pile = Mound("Pile", root, new Vector3(pileAt.x, -0.6f, pileAt.z), new Vector3(7f, 2.6f, 6f));
            pileBase = pile.localScale;
            // the dozer's spoil heap, out behind where it works
            Mound("Spoil", root, new Vector3(pileAt.x, -0.8f, pileAt.z + 30f), new Vector3(16f, 3.2f, 9f));

            // the dozer, pushing from beyond the pile toward the excavator
            if (dzP != null)
            {
                var dzM = Strip(dzP, out var dzC);
                var d = (FPSBase.Vehicles.Bulldozer)dzC;
                dz = dzM.transform; dzLift = d.liftPivot;
                dzBladeZ = d.bladeEdge != null ? dz.InverseTransformPoint(d.bladeEdge.position).z : 3.5f;
                Destroy(dzC);
                pushMound = Mound("Push", dz, new Vector3(0f, 0.2f, dzBladeZ + 1.1f), Vector3.one * 0.1f);
                Measure(dz);
                Place(dz, new Vector3(pileAt.x, 0f, pileAt.z + 24f), 180f);
            }

            stream = Mound("Pour", root, Vector3.zero, Vector3.one);
            stream.gameObject.SetActive(false);

            MakeCamera(main, new Vector3(truckX * 0.35f, 0f, pileAt.z * 0.5f + 4f));
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            t0 = Time.unscaledTime;
            ready = true;
            StartCoroutine(Digger());
            if (dz != null) StartCoroutine(Dozer());
            return true;
        }

        /// <summary>The menu's own camera, drifting slowly round `mid` (in the scene's frame), standing in for the player's.</summary>
        void MakeCamera(Camera main, Vector3 mid)
        {
            // the camera: out to the side and in front, a little above, looking across the work
            var cgo = new GameObject("Menu Camera");
            cgo.transform.SetParent(transform, false);
            cam = cgo.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.depth = main.depth + 1;
            cam.targetTexture = null;
            var mainHd = main.GetComponent<HDAdditionalCameraData>();
            var hd = cgo.AddComponent<HDAdditionalCameraData>();
            if (mainHd != null) mainHd.CopyTo(hd);
            cam.fieldOfView = 42f;
            // the world's clock is stopped while the menu is up, so the eye's adjustment to the light would never
            // catch up: make it instant for this camera
            var vgo = new GameObject("Menu Exposure");
            vgo.transform.SetParent(transform, false);
            var vol = vgo.AddComponent<UnityEngine.Rendering.Volume>();
            vol.isGlobal = true; vol.priority = 50f;
            profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            var exq = profile.Add<Exposure>(false);
            exq.adaptationMode.Override(AdaptationMode.Fixed);
            vol.sharedProfile = profile;
            // the work in the left half of the picture (the menu is on the right)
            // from the side, low: the dozer and its pile to the left, the excavator, the trucks coming in to the right
            camBase = mid + new Vector3(-28f, 6.5f, -5f);
            Vector3 view = (mid - camBase); view.y = 0f; view.Normalize();
            camLook = mid + Vector3.Cross(Vector3.up, view) * 5.5f + Vector3.up * 2.4f;
            cgo.tag = "MainCamera"; // (stands in for the player's camera while the menu is up)
            hidden = main; main.enabled = false;
            MoveCamera(0f);
        }

        void Restore()
        {
            if (hidden != null) hidden.enabled = true;
            hidden = null;
        }

        UnityEngine.Rendering.VolumeProfile profile;
        void OnDestroy()
        {
            Restore();
            if (profile != null) Destroy(profile);
            // (gone with the scene, e.g. back to the menu after a game: it can be made again)
            if (inst == this) { inst = null; tried = false; }
        }

        // ---- a clear, fairly flat patch of ground near where the game starts
        bool FindSpot(Vector3 around, out Vector3 spot, out float yaw)
        {
            spot = default; yaw = 0f;
            float[] rings = { 70f, 110f, 150f, 200f, 260f };
            foreach (float r in rings)
                for (int a = 0; a < 16; a++)
                {
                    float ang = a * 22.5f + r * 0.37f;
                    Vector3 c = around + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * r;
                    if (!Ground(c, out float h0)) continue;
                    c.y = h0;
                    float lo = h0, hi = h0; bool bad = false;
                    for (int i = -2; i <= 2 && !bad; i++)
                        for (int j = -2; j <= 2 && !bad; j++)
                        {
                            if (!Ground(c + new Vector3(i * 13f, 0f, j * 13f), out float h)) { bad = true; break; }
                            lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                        }
                    if (bad || hi - lo > 2.2f) continue;
                    // nothing else there (buildings, machines, the dealer's lot)
                    var hits = Physics.OverlapBox(c + Vector3.up * 5f, new Vector3(30f, 4f, 30f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                    bool clear = true;
                    foreach (var col in hits) if (!IsGround(col)) { clear = false; break; }
                    if (!clear) continue;
                    spot = c; yaw = ang + 90f;
                    return true;
                }
            return false;
        }

        static bool IsGround(Collider c) =>
            c != null && c.attachedRigidbody == null &&
            (c.GetComponentInParent<FPSBase.World.LowPolyTerrain>() != null || c.GetComponentInParent<FPSBase.Mining.DigSite>() != null || c is TerrainCollider);

        static bool Ground(Vector3 at, out float h)
        {
            h = 0f;
            var hits = Physics.RaycastAll(new Vector3(at.x, at.y + 400f, at.z), Vector3.down, 1200f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity; bool any = false;
            foreach (var hit in hits) if (IsGround(hit.collider) && hit.point.y > best) { best = hit.point.y; any = true; }
            if (!any) return false;
            h = best; return true;
        }

        /// <summary>Stand a model at (x, z) in the scene's frame, facing `heading` (0 = +z), sat on the ground.</summary>
        readonly Dictionary<Transform, float> lift = new();
        /// <summary>How far the model's lowest point is below its root (so it can be stood on the ground).</summary>
        void Measure(Transform m)
        {
            bool any = false; float lo = 0f;
            foreach (var r in m.GetComponentsInChildren<Renderer>(true))
            {
                if (r is LineRenderer || r is ParticleSystemRenderer || r.transform == pushMound) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                // the mesh's corners in the model's frame (renderer bounds aren't ready while it's switched off)
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float y = m.InverseTransformPoint(r.transform.TransformPoint(c)).y;
                    if (!any || y < lo) { lo = y; any = true; }
                }
            }
            lift[m] = any ? Mathf.Clamp(-lo, -1f, 4f) : 0f;
        }

        void Place(Transform m, Vector3 local, float heading, bool smooth = false)
        {
            Vector3 w = root.TransformPoint(new Vector3(local.x, 0f, local.z));
            Quaternion yawQ = root.rotation * Quaternion.Euler(0f, heading, 0f);
            Vector3 f = yawQ * Vector3.forward * 2.5f, s = yawQ * Vector3.right * 1.5f;
            float h = Ground(w, out float g) ? g : w.y;
            float hf = Ground(w + f, out float a) ? a : h, hb = Ground(w - f, out float b) ? b : h;
            float hr = Ground(w + s, out float c) ? c : h, hl = Ground(w - s, out float d) ? d : h;
            float pitch = Mathf.Atan2(hb - hf, 5f) * Mathf.Rad2Deg, roll = Mathf.Atan2(hl - hr, 3f) * Mathf.Rad2Deg;
            var q = yawQ * Quaternion.Euler(pitch, 0f, roll);
            var p = new Vector3(w.x, (h + (hf + hb + hr + hl) * 0.25f) * 0.5f, w.z) + q * Vector3.up * (lift.TryGetValue(m, out float up) ? up : 0f);
            if (smooth) { float k = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime); m.SetPositionAndRotation(Vector3.Lerp(m.position, p, k), Quaternion.Slerp(m.rotation, q, k)); }
            else m.SetPositionAndRotation(p, q);
        }

        /// <summary>A copy of the machine with only what you see (and its track belts and rams, which follow along).</summary>
        GameObject Strip(GameObject prefab, out MonoBehaviour main)
        {
            var holder = new GameObject(prefab.name);
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            var m = Instantiate(prefab, holder.transform);
            main = m.GetComponent<FPSBase.Vehicles.VehicleBase>();
            foreach (var mb in m.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == main) continue;
                if (mb is FPSBase.Vehicles.TrackAnimator ta) { ta.drawDistance = 1e6f; continue; }
                if (mb is FPSBase.Vehicles.HydraulicCylinder || mb is FPSBase.Vehicles.ExcavatorLinkage) continue;
                DestroyImmediate(mb);
            }
            foreach (var j in m.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
            foreach (var c in m.GetComponentsInChildren<Collider>(true)) if (!(c is WheelCollider)) DestroyImmediate(c);
            foreach (var c in m.GetComponentsInChildren<Camera>(true)) DestroyImmediate(c);
            foreach (var l in m.GetComponentsInChildren<Light>(true)) DestroyImmediate(l);
            foreach (var a in m.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);
            foreach (var a in m.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(a);
            foreach (var l in m.GetComponentsInChildren<LineRenderer>(true)) l.enabled = false;
            // the main script stays (unwoken: the holder is off) until its parts have been read, then goes
            m.SetActive(false);
            m.transform.SetParent(transform, true);
            Destroy(holder);
            if (main == null) foreach (var w in m.GetComponentsInChildren<WheelCollider>(true)) DestroyImmediate(w);
            foreach (var r in m.GetComponentsInChildren<Rigidbody>(true)) { r.isKinematic = true; r.detectCollisions = false; }
            m.SetActive(false);
            StartCoroutine(Wake(m));
            return m;
        }

        IEnumerator Wake(GameObject m)
        {
            yield return null; // (after the main script has gone)
            if (m == null) yield break;
            // anything another script needed, now free to go
            foreach (var mb in m.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(mb is FPSBase.Vehicles.TrackAnimator || mb is FPSBase.Vehicles.HydraulicCylinder || mb is FPSBase.Vehicles.ExcavatorLinkage)) DestroyImmediate(mb);
            foreach (var w in m.GetComponentsInChildren<WheelCollider>(true)) DestroyImmediate(w);
            foreach (var r in m.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(r);
            yield return null;
            if (m != null) m.SetActive(true);
        }

        Material FindDirt(GameObject truckPrefab)
        {
            var dp = truckPrefab.GetComponent<FPSBase.Vehicles.DumpPickup>();
            var r = dp != null && dp.bedContents != null ? dp.bedContents.GetComponentInChildren<Renderer>(true) : null;
            if (r != null && r.sharedMaterial != null) return r.sharedMaterial;
            var m = new Material(Shader.Find("HDRP/Lit"));
            m.color = new Color(0.36f, 0.26f, 0.17f);
            return m;
        }

        Transform Mound(string name, Transform parent, Vector3 local, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local; go.transform.localScale = scale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = dirt;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }

        void ApplyPose()
        {
            if (exUpper != null) exUpper.localRotation = Quaternion.Euler(0f, pose.swing, 0f);
            if (exBoom != null) exBoom.localRotation = Quaternion.Euler(-pose.boom, 0f, 0f);
            if (exStick != null) exStick.localRotation = Quaternion.Euler(-pose.stick, 0f, 0f);
            if (exBucket != null) exBucket.localRotation = Quaternion.Euler(-pose.bucket, 0f, 0f);
        }

        void BucketLoad(float f)
        {
            if (exLoad == null) return;
            exLoad.gameObject.SetActive(f > 0.01f);
            float w = Mathf.Lerp(0.6f, 1f, f), h = Mathf.Lerp(0.3f, 1f, f);
            exLoad.localScale = new Vector3(exLoadScale.x * w, exLoadScale.y * h, exLoadScale.z * w);
            exLoad.localPosition = exLoadPos + Vector3.up * exLoadScale.y * (h - 1f) * 0.5f;
        }

        void SetFill(Truck tr, float f)
        {
            tr.fill = f;
            if (tr.bed == null) return;
            tr.bed.gameObject.SetActive(f > 0.002f);
            float layer = Mathf.Lerp(0.04f, tr.bedSize.y * 0.8f, Mathf.Sqrt(f));
            Vector3 s = tr.bed.localScale; s.y = layer; tr.bed.localScale = s;
            Vector3 p = tr.bed.localPosition; p.y = layer * 0.5f + 0.05f; tr.bed.localPosition = p;
            if (tr.mound != null)
            {
                tr.mound.gameObject.SetActive(f > 0.02f);
                float m = Mathf.Lerp(0.1f, 1f, f), spread = Mathf.Lerp(0.5f, 1f, f);
                tr.mound.localScale = new Vector3(tr.bedSize.x * 0.95f * spread, tr.bedSize.y * 0.7f * m, tr.bedSize.z * 0.95f * spread);
                tr.mound.localPosition = new Vector3(0f, layer + 0.05f, tr.bed.localPosition.z);
            }
        }

        void SetPile(float a)
        {
            pileAmount = Mathf.Clamp(a, 0.25f, 1.2f);
            float k = Mathf.Lerp(0.55f, 1.1f, pileAmount);
            pile.localScale = new Vector3(pileBase.x * Mathf.Sqrt(k), pileBase.y * k, pileBase.z * Mathf.Sqrt(k));
        }

        static float Ease(float x) => x * x * (3f - 2f * x);
        float Dt => Mathf.Min(Time.unscaledDeltaTime, 0.1f);

        IEnumerator MoveTo(Pose to, float secs, System.Action<float> during = null)
        {
            Pose from = pose;
            for (float t = 0f; t < secs; t += Dt)
            {
                float k = Ease(Mathf.Clamp01(t / secs));
                pose = new Pose(Mathf.Lerp(from.boom, to.boom, k), Mathf.Lerp(from.stick, to.stick, k), Mathf.Lerp(from.bucket, to.bucket, k), Mathf.LerpAngle(from.swing, to.swing, k));
                ApplyPose();
                during?.Invoke(k);
                yield return null;
            }
            pose = to; ApplyPose();
            during?.Invoke(1f);
        }

        Truck Loading()
        {
            foreach (var tr in trucks) if (Mathf.Abs(tr.z - loadZ) < 0.3f && tr.scoops < 4 && tr.speed < 0.2f) return tr;
            return null;
        }

        IEnumerator Digger()
        {
            BucketLoad(0f);
            while (true)
            {
                // reach out over the pile, crowd in and curl through it, lift clear
                yield return MoveTo(Reach, 2.0f);
                float startPile = pileAmount;
                yield return MoveTo(DigEnd, 1.9f, k => { BucketLoad(k); SetPile(startPile - 0.07f * k); });
                yield return MoveTo(Lift, 1.0f);
                // a truck under the bucket? (waits, loaded, until one is)
                while (Loading() == null) yield return null;
                var tr = Loading();
                yield return MoveTo(Over(-130f), 2.1f);
                // open up: dirt pours into the bed
                float before = tr.fill;
                stream.gameObject.SetActive(true);
                yield return MoveTo(Over(18f), 1.1f, k =>
                {
                    float pour = Mathf.Clamp01((k - 0.25f) / 0.6f);
                    BucketLoad(1f - pour);
                    SetFill(tr, before + 0.25f * pour);
                    Stream(tr, pour > 0f && pour < 1f);
                });
                stream.gameObject.SetActive(false);
                tr.scoops++;
                yield return Wait(0.35f);
                yield return MoveTo(new Pose(30f, -80f, -40f, 0f), 2.1f);
            }
        }

        void Stream(Truck tr, bool on)
        {
            stream.gameObject.SetActive(on);
            if (!on || exBucket == null) return;
            Vector3 top = exBucket.position - exBucket.up * 0.3f;
            Vector3 bottom = tr.bed != null ? tr.bed.position + Vector3.up * 0.4f : top - Vector3.up * 3f;
            Vector3 mid = (top + bottom) * 0.5f;
            stream.position = mid;
            stream.rotation = Quaternion.FromToRotation(Vector3.up, top - bottom);
            float len = Vector3.Distance(top, bottom);
            float wob = 0.75f + Mathf.Sin(Time.unscaledTime * 23f) * 0.1f;
            stream.localScale = new Vector3(0.9f * wob, len, 0.9f * wob);
        }

        IEnumerator Wait(float s) { for (float t = 0f; t < s; t += Dt) yield return null; }

        IEnumerator Dozer()
        {
            float z = pileAt.z + 24f, stopZ = pileAt.z + pileBase.z * 0.35f + dzBladeZ;
            float back = pileAt.z + 26f;
            while (true)
            {
                // forward, blade down, pushing a growing heap
                for (float v = 0f; z > stopZ + 0.05f;)
                {
                    float left = z - stopZ;
                    v = Mathf.Min(v + 1.2f * Dt, 2.4f, Mathf.Sqrt(2f * 1.0f * left) + 0.1f);
                    z -= v * Dt;
                    float grow = Mathf.Clamp01((pileAt.z + 24f - z) / 12f);
                    pushMound.localScale = new Vector3(3.4f, 1.2f, 1.9f) * Mathf.Lerp(0.15f, 1f, grow);
                    if (dzLift != null) dzLift.localRotation = Quaternion.Slerp(dzLift.localRotation, Quaternion.identity, Dt * 4f);
                    Place(dz, new Vector3(pileAt.x, 0f, z), 180f, true);
                    yield return null;
                }
                // the heap joins the pile
                for (float t = 0f; t < 0.6f; t += Dt)
                {
                    pushMound.localScale = Vector3.Lerp(pushMound.localScale, Vector3.one * 0.1f, Dt * 6f);
                    SetPile(pileAmount + Dt * 0.12f / 0.6f * 2f);
                    yield return null;
                }
                pushMound.localScale = Vector3.one * 0.1f;
                yield return Wait(0.6f);
                // blade up a little and back off
                for (float v = 0f; z < back - 0.05f;)
                {
                    float left = back - z;
                    v = Mathf.Min(v + 1.5f * Dt, 3.0f, Mathf.Sqrt(2f * 1.2f * left) + 0.1f);
                    z += v * Dt;
                    if (dzLift != null) dzLift.localRotation = Quaternion.Slerp(dzLift.localRotation, Quaternion.Euler(-7f, 0f, 0f), Dt * 3f);
                    Place(dz, new Vector3(pileAt.x, 0f, z), 180f, true);
                    yield return null;
                }
                yield return Wait(0.8f);
                // only push again when the excavator has made room
                while (pileAmount > 0.95f) yield return null;
            }
        }

        void Update()
        {
            if (!ready) return;
            if (live) { LiveUpdate(); MoveCamera(Time.unscaledTime - t0); return; }
            float dt = Dt;
            // the trucks: drive up, wait in line, load, drive off; round again empty
            for (int i = 0; i < trucks.Count; i++)
            {
                var tr = trucks[i];
                var other = trucks[(i + 1) % trucks.Count];
                float target;
                if (tr.scoops >= 4 || tr.z > loadZ + 0.5f) target = TruckEnd;           // loaded: off it goes
                else
                {
                    target = loadZ;
                    // someone ahead at (or coming into) the loading spot: queue behind them
                    if (other != tr && other.z > tr.z && other.z < loadZ + 1f) target = Mathf.Min(loadZ, other.z - QueueGap);
                }
                float d = target - tr.z;
                float want = d > 0.05f ? Mathf.Min(6.5f, Mathf.Sqrt(2f * 1.4f * d)) : 0f;
                tr.speed = Mathf.MoveTowards(tr.speed, want, (want > tr.speed ? 1.6f : 3.5f) * dt);
                if (d <= 0.05f && tr.speed < 0.4f) { tr.speed = 0f; if (target == loadZ) tr.z = loadZ; }
                tr.z += tr.speed * dt;
                if (tr.z >= TruckEnd - 0.5f)
                {
                    // round the back: empty again, at the end of the line
                    tr.z = TruckStart - 10f * i; tr.scoops = 0; tr.speed = 0f; SetFill(tr, 0f);
                    Place(tr.t, new Vector3(truckX, 0f, tr.z), 0f);
                }
                Place(tr.t, new Vector3(truckX, 0f, tr.z), 0f, true);
                tr.spin += tr.speed * dt;
                for (int w = 0; w < tr.wheels.Length; w++)
                    if (tr.wheels[w] != null)
                        tr.wheels[w].rotation = tr.t.rotation * Quaternion.Euler(tr.spin / Mathf.Max(0.2f, tr.radius[w]) * Mathf.Rad2Deg, 0f, 0f) * Quaternion.Euler(0f, 0f, 90f);
            }
            Place(ex, Vector3.zero, 0f, true);
            MoveCamera(Time.unscaledTime - t0);
        }

        void MoveCamera(float t)
        {
            // a slow drift side to side and in and out
            Vector3 off = camBase + new Vector3(Mathf.Sin(t * 0.05f) * 3.5f, Mathf.Sin(t * 0.031f) * 1.0f, Mathf.Sin(t * 0.043f) * 2.5f);
            Vector3 p = root.TransformPoint(off);
            if (Ground(p, out float g) && p.y < g + 2.5f) p.y = g + 2.5f;
            Vector3 look = root.TransformPoint(camLook);
            cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(look - p));
        }
    }
}
