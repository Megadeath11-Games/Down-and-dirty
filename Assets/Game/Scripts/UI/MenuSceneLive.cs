using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FPSBase.Mining;
using FPSBase.Vehicles;

namespace FPSBase.UI
{
    /// <summary>
    /// The live menu scene: the game's own machines (a 20 t excavator, a dozer and a haul truck) working a patch of
    /// the real dirt field behind the menu, with all the real dirt: the bucket cuts and scoops, clods fall and
    /// roll, the dozer's blade pushes a berm, the truck tips its load into a heap. They drive themselves through
    /// their controls like a player would. The world's clock runs while it's up; when the menu closes the machines
    /// go, the clods go and every square of ground they touched goes back as it was (see DigSite.BeginScratch).
    /// </summary>
    public partial class MenuScene
    {
        bool live;
        /// <summary>The real machines are working behind the menu (the world's clock has to run for them).</summary>
        public static bool Live => inst != null && inst.live;

        DigSite liveSite;
        readonly List<GameObject> liveMade = new();
        MiniExcavator lEx;
        Bulldozer lDz;
        DumpPickup lTr;
        float lOverBoom = 30f, lOverStick = -70f, lDigZ = 7f;
        bool truckDumping;

        bool BuildLive()
        {
            var site = DigSite.Instance;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            var main = Camera.main;
            if (site == null || dealer == null || dealer.listings == null || main == null) return false;
            GameObject Find(string exact, System.Type type)
            {
                foreach (var l in dealer.listings) if (l.prefab != null && (l.name == exact || l.prefab.name == exact)) return l.prefab;
                foreach (var l in dealer.listings) if (l.prefab != null && l.prefab.GetComponent(type) != null) return l.prefab;
                return null;
            }
            var exP = Find("Excavator 20 t", typeof(MiniExcavator));
            var dzP = Find("Medium Dozer 22 t", typeof(Bulldozer));
            var trP = Find("Haul Truck 25t", typeof(DumpPickup));
            if (exP == null || trP == null) return false;
            if (!FindSiteSpot(site, main.transform.position, out Vector3 spot, out float yaw)) return false;

            root = new GameObject("Menu machines").transform;
            root.SetParent(transform, false);
            root.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));
            liveSite = site;
            site.BeginScratch(spot + root.forward * 10f, 50f);
            live = true; // (from here on EndLive puts everything back)

            // the excavator, and where its bucket is over the dirt in front and over the truck swung round
            lEx = Spawn(exP, Vector3.zero, 0f).GetComponent<MiniExcavator>();
            lEx.SetPose(-14f, -80f, -40f, 0f);
            lDigZ = Mathf.Max(5f, root.InverseTransformPoint(lEx.bucketTip.position).z);
            lTr = Spawn(trP, new Vector3(8f, 0f, 0f), 0f).GetComponent<DumpPickup>();
            Physics.SyncTransforms();
            var tt = lTr.transform;
            var tb = Extent(tt, tt);
            Vector3 bedC = lTr.bedVolume != null ? tt.InverseTransformPoint(lTr.bedVolume.transform.TransformPoint(lTr.bedVolume.center)) : new Vector3(0f, 2f, -1f);
            float bedTop = root.InverseTransformPoint(tt.TransformPoint(bedC)).y + (lTr.bedVolume != null ? lTr.bedVolume.size.y * 0.5f * tt.lossyScale.y : 1f) + 0.9f;
            lEx.SetPose(0f, -90f, -130f, 90f);
            var ub = Extent(lEx.upper, lEx.upper, lEx.boomPivot);
            float cabReach = Mathf.Max(Mathf.Abs(ub.min.z), Mathf.Abs(ub.max.z), Mathf.Abs(ub.min.x), Mathf.Abs(ub.max.x)) * 0.8f;
            float needX = cabReach + 0.7f - tb.min.x + bedC.x;
            Vector3 over = Vector3.zero; float bestX = -1f; bool found = false;
            foreach (float bm in new[] { 26f, 32f, 38f, 44f, 50f, 56f })
            {
                for (float st = -110f; st <= -40f && !found; st += 5f)
                {
                    lEx.SetPose(bm, st, -130f, 90f);
                    Vector3 tp = root.InverseTransformPoint(lEx.bucketTip.position);
                    if (tp.y < bedTop) continue;
                    if (tp.x > bestX) { bestX = tp.x; over = tp; lOverBoom = bm; lOverStick = st; }
                    if (tp.x >= needX) { over = tp; lOverBoom = bm; lOverStick = st; found = true; }
                }
                if (found) break;
            }
            if (bestX < 0f)
            {
                lEx.SetPose(38f, -60f, -130f, 90f);
                Debug.LogWarning($"[MenuScene] live: the bucket can't get over the truck (bed top {bedTop:0.00} m, highest tip {root.InverseTransformPoint(lEx.bucketTip.position)}, need x {needX:0.0})");
                return false;
            }
            Place(lTr.gameObject, new Vector3(over.x - bedC.x, 0f, over.z - bedC.z), 0f);
            lEx.SetPose(10f, -70f, 0f, 0f);

            // the dozer, out in front, pushing dirt up toward the excavator
            if (dzP != null) lDz = Spawn(dzP, new Vector3(0f, 0f, lDigZ + 22f), 180f).GetComponent<Bulldozer>();

            foreach (var v in root.GetComponentsInChildren<VehicleBase>()) { v.fuel = v.fuelCapacity; }
            MakeCamera(main, new Vector3(over.x * 0.35f, 0f, lDigZ * 0.5f + 4f));
            t0 = Time.unscaledTime;
            ready = true;
            // the automated screenshot run: where the frame time goes while the machines work
            if (System.Environment.CommandLine.Contains("-menushot") && root.GetComponent<FPSBase.DebugTools.PerfProbe>() == null)
                root.gameObject.AddComponent<FPSBase.DebugTools.PerfProbe>().label = "menu excavator";
            StartCoroutine(LiveStart());
            Debug.Log($"[MenuScene] live: real machines on the dirt field at {spot} (excavator over the truck at boom {lOverBoom:0}, stick {lOverStick:0})");
            return true;
        }

        GameObject Spawn(GameObject prefab, Vector3 local, float heading)
        {
            var go = Instantiate(prefab, root.TransformPoint(local), root.rotation * Quaternion.Euler(0f, heading, 0f));
            go.transform.SetParent(root, true);
            liveMade.Add(go);
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true; // (held until everything is placed)
            Place(go, local, heading);
            return go;
        }

        void Place(GameObject go, Vector3 local, float heading)
        {
            Vector3 w = root.TransformPoint(new Vector3(local.x, 0f, local.z));
            w.y = liveSite.SurfaceY(w) + 0.25f;
            go.transform.SetPositionAndRotation(w, root.rotation * Quaternion.Euler(0f, heading, 0f));
            Physics.SyncTransforms();
        }

        IEnumerator LiveStart()
        {
            yield return new WaitForFixedUpdate();
            foreach (var go in liveMade)
            {
                if (go == null) continue;
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) { rb.isKinematic = false; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
            for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate(); // (settle on the ground)
            // now they're down: the truck's bed right under where the bucket pours, swung round and opened up
            if (lTr != null && lTr.bedVolume != null && lEx != null)
            {
                float b0 = lEx.BoomAngle, s0 = lEx.StickAngle, k0 = lEx.BucketAngle, w0 = lEx.SwingAngle;
                lEx.SetPose(lOverBoom, lOverStick, 18f, 90f);
                Physics.SyncTransforms();
                Vector3 pour = lEx.DumpPoint(18f);
                lEx.SetPose(b0, s0, k0, w0);
                Vector3 bed = lTr.bedVolume.transform.TransformPoint(lTr.bedVolume.center);
                Vector3 move = pour - bed; move.y = 0f;
                var trb = lTr.GetComponent<Rigidbody>();
                if (trb != null) { trb.position += move; trb.linearVelocity = Vector3.zero; }
                else lTr.transform.position += move;
                Debug.Log($"[MenuScene] live: truck moved {move.magnitude:0.00} m to put its bed under the bucket");
                for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
            }
            StartCoroutine(LiveDigger());
            if (lDz != null) StartCoroutine(LiveDozer());
        }

        /// <summary>The machines away, the clods away, the ground back as it was.</summary>
        void EndLive()
        {
            if (!live) return;
            live = false; ready = false;
            StopAllCoroutines();
            foreach (var go in liveMade) if (go != null) DestroyImmediate(go);
            liveMade.Clear();
            DirtGrains.ClearAll();
            if (root != null && root.GetComponent<FPSBase.DebugTools.PerfProbe>() is var probe && probe != null) Destroy(probe);
            if (liveSite != null)
            {
                Vector3 c = root != null ? root.position : Vector3.zero;
                int before = liveSite.CountWorkedNear(c, 40f);
                int n = liveSite.EndScratch();
                Debug.Log($"[MenuScene] live: put back {n} squares of ground ({before} worked round the machines before, {liveSite.CountWorkedNear(c, 40f)} after)");
            }
            liveSite = null;
        }

        float nextLog;
        void LiveUpdate()
        {
            foreach (var v in root.GetComponentsInChildren<VehicleBase>()) if (v.fuel < v.fuelCapacity * 0.5f) v.fuel = v.fuelCapacity;
            if (Time.unscaledTime >= nextLog && lEx != null)
            {
                nextLog = Time.unscaledTime + 8f;
                Debug.Log($"[MenuScene] live {Time.unscaledTime - t0:0}s: bucket {lEx.bucketLoad.volume:0.00} m³ (boom {lEx.BoomAngle:0}, stick {lEx.StickAngle:0}, bucket {lEx.BucketAngle:0}, swing {lEx.SwingAngle:0}), " +
                          $"truck {(lTr != null ? lTr.load.volume : 0f):0.00} of {(lTr != null ? lTr.capacity : 0f):0.0} m³, clods {DirtGrains.Live}, {1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime):0} fps");
            }
        }

        // ------------------------------------------------------------------ a clear patch of the dirt field

        bool FindSiteSpot(DigSite site, Vector3 near, out Vector3 spot, out float yaw)
        {
            spot = default; yaw = 0f;
            Vector3 o = site.transform.position, sz = site.Size;
            if (sz.x < 100f) return false;
            Rect claim = site.claimRect;
            claim.xMin -= 15f; claim.yMin -= 15f; claim.xMax += 15f; claim.yMax += 15f;
            float best = float.MaxValue;
            for (float x = o.x + 30f; x < o.x + sz.x - 30f; x += 8f)
                for (float z = o.z + 30f; z < o.z + sz.z - 40f; z += 8f)
                {
                    if (claim.Overlaps(new Rect(x - 20f, z - 15f, 40f, 50f))) continue;
                    var c = new Vector3(x, 0f, z);
                    float d = Vector3.Distance(new Vector3(near.x, 0f, near.z), c);
                    float score = Mathf.Abs(d - 90f);
                    if (score >= best) continue;
                    // open, fairly flat dirt over the whole work area (the excavator at c, the dozer out to +z, the truck to +x)
                    float lo = float.MaxValue, hi = float.MinValue; bool ok = true;
                    for (int i = -3; i <= 3 && ok; i++)
                        for (int j = -2; j <= 6 && ok; j++)
                        {
                            Vector3 p = c + new Vector3(i * 5f, 0f, j * 5f);
                            if (!site.Contains(p)) { ok = false; break; }
                            float h = site.SurfaceY(p);
                            lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                        }
                    if (!ok || hi - lo > 1.5f) continue;
                    c.y = site.SurfaceY(c);
                    var hits = Physics.OverlapBox(c + new Vector3(0f, 5.5f, 10f), new Vector3(20f, 5f, 26f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                    bool clear = true;
                    foreach (var col in hits) if (!IsGround(col)) { clear = false; break; }
                    if (!clear) continue;
                    best = score; spot = c;
                }
            return best < float.MaxValue;
        }

        // ------------------------------------------------------------------ driving the controls

        static float P(float err, float band) => Mathf.Clamp(err / band, -1f, 1f);

        /// <summary>Work the levers toward an arm pose (deg) for up to `secs`.</summary>
        IEnumerator ExTo(float boom, float stick, float bucket, float swing, float secs)
        {
            for (float t = 0f; t < secs && lEx != null; t += Time.deltaTime)
            {
                float eb = boom - lEx.BoomAngle, es = stick - lEx.StickAngle, ek = bucket - lEx.BucketAngle, ew = Mathf.DeltaAngle(lEx.SwingAngle, swing);
                if (Mathf.Abs(eb) < 1.5f && Mathf.Abs(es) < 2f && Mathf.Abs(ek) < 3f && Mathf.Abs(ew) < 1.5f) break;
                lEx.TestControl(new MiniExcavator.Inputs { boom = P(eb, 6f), stick = P(es, 8f), bucket = P(ek, 10f), swing = P(ew, 10f) });
                yield return null;
            }
            if (lEx != null) lEx.TestControl(new MiniExcavator.Inputs());
        }

        IEnumerator ExHold(MiniExcavator.Inputs i, float secs, System.Func<bool> until = null)
        {
            for (float t = 0f; t < secs && lEx != null && (until == null || !until()); t += Time.deltaTime)
            {
                lEx.TestControl(i);
                yield return null;
            }
            if (lEx != null) lEx.TestControl(new MiniExcavator.Inputs());
        }

        IEnumerator LiveDigger()
        {
            var site = liveSite;
            float cap = lEx.bucketCapacity;
            while (lEx != null)
            {
                // out over the dirt in front, then down until the teeth are in
                yield return ExTo(10f, -70f, 0f, 0f, 3f);
                yield return ExHold(new MiniExcavator.Inputs { boom = -1f }, 4f, () => site.IsUnderground(lEx.bucketTip.position + Vector3.down * 0.25f * lEx.sizeScale));
                // crowd: the stick in and the bucket curling through the dirt
                yield return ExHold(new MiniExcavator.Inputs { stick = -1f, bucket = -0.6f, boom = -0.15f }, 5f, () => lEx.bucketLoad.volume >= cap * 0.95f);
                // curl right up and lift out
                yield return ExHold(new MiniExcavator.Inputs { bucket = -1f, boom = 1f }, 2.2f);
                // the truck tipping its last load: wait with the bucket full
                while (truckDumping) { lEx.TestControl(new MiniExcavator.Inputs()); yield return null; }
                // up high enough to clear the bed, then round over it
                yield return ExTo(lOverBoom, lOverStick, -130f, lEx.SwingAngle, 2.5f);
                yield return ExTo(lOverBoom, lOverStick, -130f, 90f, 4f);
                // open up: the load pours into the bed
                yield return ExTo(lOverBoom, lOverStick, 18f, 90f, 2f);
                yield return ExHold(new MiniExcavator.Inputs(), 2.5f, () => lEx.bucketLoad.volume < cap * 0.03f);
                if (lTr != null && lTr.load.volume >= lTr.capacity * 0.5f) StartCoroutine(TruckDump());
                // and back round to the dirt
                yield return ExTo(lOverBoom, lOverStick, -40f, 90f, 1.5f);
                yield return ExTo(lOverBoom, -70f, -40f, 0f, 4f);
            }
        }

        IEnumerator TruckDump()
        {
            truckDumping = true;
            float tilt = 0f;
            for (; lTr != null && tilt < lTr.maxTilt; tilt += 12f * Time.deltaTime) { lTr.SetTilt(tilt); yield return null; }
            for (float t = 0f; lTr != null && t < 8f && lTr.load.volume > lTr.capacity * 0.04f; t += Time.deltaTime) yield return null;
            for (float t = 0f; lTr != null && t < 1.5f; t += Time.deltaTime) yield return null;
            for (; lTr != null && tilt > 0f; tilt -= 18f * Time.deltaTime) { lTr.SetTilt(Mathf.Max(0f, tilt)); yield return null; }
            truckDumping = false;
        }

        IEnumerator LiveDozer()
        {
            var site = liveSite;
            float bladeOff = lDz.bladeEdge != null ? Vector3.Dot(lDz.bladeEdge.position - lDz.transform.position, lDz.transform.forward) : 3.5f;
            float back = lDigZ + 22f, stop = lDigZ + 3f + bladeOff;
            float grade = root.position.y - 0.35f; // (cuts down to here and no deeper: it ends up pushing what's loose)
            float Z() => root.InverseTransformPoint(lDz.transform.position).z;
            while (lDz != null)
            {
                // blade down to the grade and push toward the excavator
                for (float t = 0f; t < 14f && lDz != null && Z() > stop; t += Time.deltaTime)
                {
                    float h = lDz.BladeHeight + Mathf.Clamp(grade - lDz.bladeEdge.position.y, -0.6f * Time.deltaTime, 0.6f * Time.deltaTime);
                    lDz.SetBlade(Mathf.Min(h, 0.05f));
                    lDz.TestDrive(Z() - stop < 3f ? 0.35f : 0.6f);
                    yield return null;
                }
                lDz.TestDrive(0f);
                for (float t = 0f; t < 0.8f; t += Time.deltaTime) yield return null;
                // blade up and back off for the next pass
                for (float t = 0f; t < 1f && lDz != null; t += Time.deltaTime) { lDz.SetBlade(lDz.BladeHeight + 0.5f * Time.deltaTime); yield return null; }
                for (float t = 0f; t < 14f && lDz != null && Z() < back; t += Time.deltaTime) { lDz.TestDrive(-0.7f); yield return null; }
                if (lDz != null) lDz.TestDrive(0f);
                for (float t = 0f; t < 1.5f; t += Time.deltaTime) yield return null;
            }
        }
    }
}
