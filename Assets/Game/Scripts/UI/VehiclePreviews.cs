using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FPSBase.UI
{
    /// <summary>
    /// Shop pictures of the machines: each one is set up on its own high above the map (its scripts and physics
    /// taken off: just the model), photographed by a camera that only sees it, and the picture kept for the shop.
    /// Pictures are saved to disk per build, so this only happens the first time a build opens the shop.
    /// </summary>
    public class VehiclePreviews : MonoBehaviour
    {
        public const int Width = 512, Height = 320;
        public static readonly Color Background = new(0.16f, 0.115f, 0.075f, 1f);

        static VehiclePreviews inst;
        static readonly Dictionary<GameObject, Texture2D> done = new();
        static readonly HashSet<GameObject> queued = new();
        static readonly Queue<(GameObject prefab, string name)> todo = new();
        const int Layer = 30; // nothing else in the game uses it: the preview camera sees only this

        /// <summary>The machine's picture, or null while it's being made (it's queued the first time you ask).</summary>
        public static Texture2D Get(GameObject prefab, string name)
        {
            if (prefab == null) return null;
            if (done.TryGetValue(prefab, out var t)) return t;
            if (queued.Contains(prefab)) return null;
            queued.Add(prefab);
            if (TryLoad(name, out t)) { done[prefab] = t; return t; }
            if (Application.isBatchMode && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            if (inst == null)
            {
                var go = new GameObject("Vehicle Previews");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<VehiclePreviews>();
            }
            todo.Enqueue((prefab, name));
            return null;
        }

        /// <summary>Pictures still to make.</summary>
        public static int Pending => todo.Count;

        static string Folder => Path.Combine(Application.persistentDataPath, "Previews", Application.isEditor ? "editor" : Application.buildGUID);
        static string FileFor(string name)
        {
            var safe = new System.Text.StringBuilder();
            foreach (char c in name) safe.Append(char.IsLetterOrDigit(c) ? c : '_');
            return Path.Combine(Folder, safe + ".png");
        }

        static bool TryLoad(string name, out Texture2D t)
        {
            t = null;
            if (Application.isEditor) return false; // the editor rebuilds the machines: always fresh pictures
            try
            {
                string f = FileFor(name);
                if (!File.Exists(f)) return false;
                t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = name };
                if (!t.LoadImage(File.ReadAllBytes(f))) { Destroy(t); t = null; return false; }
                return true;
            }
            catch { return false; }
        }

        Camera cam;
        RenderTexture rt;
        bool running;

        void Update()
        {
            if (!running && todo.Count > 0) StartCoroutine(Work());
        }

        IEnumerator Work()
        {
            running = true;
            Setup();
            int index = 0;
            while (todo.Count > 0)
            {
                var (prefab, name) = todo.Dequeue();
                Texture2D tex = null;
                GameObject holder = null;
                try { holder = Stage(prefab, index++); }
                catch (System.Exception e) { Debug.LogWarning($"[Previews] {name}: {e.Message}"); }
                if (holder != null)
                {
                    Frame(holder);
                    cam.enabled = true;
                    // a few frames so the lighting and the tracks settle
                    for (int i = 0; i < 4; i++) yield return null;
                    yield return new WaitForEndOfFrame();
                    tex = Grab(name);
                    cam.enabled = false;
                    Destroy(holder);
                }
                if (tex != null)
                {
                    done[prefab] = tex;
                    try { Directory.CreateDirectory(Folder); File.WriteAllBytes(FileFor(name), tex.EncodeToPNG()); }
                    catch (System.Exception e) { Debug.LogWarning($"[Previews] couldn't save {name}: {e.Message}"); }
                }
                yield return null;
            }
            running = false;
        }

        void Setup()
        {
            if (cam != null) return;
            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Vehicle preview", antiAliasing = 1 };
            var go = new GameObject("Preview Camera");
            go.transform.SetParent(transform, false);
            cam = go.AddComponent<Camera>();
            cam.cullingMask = 1 << Layer;
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 400f;
            cam.targetTexture = rt;
            cam.enabled = false;
            var hd = go.AddComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = Background.linear;
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            hd.volumeLayerMask = ~0;

            // its own look: steady exposure (the dark backdrop would fool the auto exposure), no clouds or fog
            var vgo = new GameObject("Preview Volume") { layer = Layer };
            vgo.transform.SetParent(transform, false);
            var vol = vgo.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 100f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var ex = profile.Add<Exposure>(true);
            ex.mode.value = ExposureMode.Fixed; ex.fixedExposure.value = 13.2f;
            profile.Add<VolumetricClouds>(true).enable.value = false;
            profile.Add<Fog>(true).enabled.value = false;
            vol.sharedProfile = profile;
        }

        /// <summary>A copy of the machine with only what you see: no scripts (except the track belts), no physics.</summary>
        GameObject Stage(GameObject prefab, int index)
        {
            var holder = new GameObject("Preview of " + prefab.name);
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            holder.transform.position = new Vector3(index % 8 * 60f - 210f, 2400f, -2000f - index / 8 * 60f);
            var m = Instantiate(prefab, holder.transform);
            m.transform.localPosition = Vector3.zero; m.transform.localRotation = Quaternion.identity;
            foreach (var mb in m.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb is FPSBase.Vehicles.TrackAnimator ta) { ta.drawDistance = 1e6f; continue; }
                DestroyImmediate(mb);
            }
            foreach (var j in m.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
            foreach (var w in m.GetComponentsInChildren<WheelCollider>(true)) DestroyImmediate(w);
            foreach (var c in m.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var r in m.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(r);
            foreach (var c in m.GetComponentsInChildren<Camera>(true)) DestroyImmediate(c);
            foreach (var l in m.GetComponentsInChildren<Light>(true)) DestroyImmediate(l);
            foreach (var a in m.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);
            foreach (var a in m.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(a);
            foreach (var l in m.GetComponentsInChildren<LineRenderer>(true)) l.enabled = false;
            foreach (var t in m.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            holder.SetActive(true);
            return holder;
        }

        /// <summary>A three-quarter view from the front right, a little above, the whole machine in frame.</summary>
        void Frame(GameObject holder)
        {
            bool any = false; var b = new Bounds();
            foreach (var r in holder.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is LineRenderer || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) b = new Bounds(holder.transform.position, Vector3.one * 4f);
            Vector3 dir = new Vector3(0.78f, 0.42f, 1f).normalized; // front (+z) right, above
            float aspect = (float)Width / Height;
            float halfV = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfH = Mathf.Atan(Mathf.Tan(halfV) * aspect);
            float radius = b.extents.magnitude;
            float dist = radius / Mathf.Sin(Mathf.Min(halfV, halfH)) * 0.92f;
            cam.transform.position = b.center + dir * dist;
            cam.transform.rotation = Quaternion.LookRotation(b.center - cam.transform.position);
        }

        Texture2D Grab(string name)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = name };
            t.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            return t;
        }
    }
}
