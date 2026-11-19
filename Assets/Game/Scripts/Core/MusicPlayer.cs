using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.Core
{
    /// <summary>
    /// Chill background music: the game's own lofi tracks (Resources/Audio/Music), shuffled, one fading into
    /// the next. Plays in the menus, and while you play unless that's switched off (Audio settings). Quieter
    /// while you're driving a machine, so the engine still comes through.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        static MusicPlayer inst;
        AudioClip[] clips;
        readonly List<int> order = new();
        int next;
        AudioSource a, b; // playing, fading out
        float fade = 1f, duck = 1f;
        const float Crossfade = 3f;

        public static string NowPlaying => inst != null && inst.a != null && inst.a.clip != null && inst.a.isPlaying ? Pretty(inst.a.clip.name) : null;
        public static void Next() { if (inst != null) inst.PlayNext(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst != null || Application.isBatchMode) return;
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-netbot") >= 0 || System.Array.IndexOf(args, "-nomusic") >= 0) return; // (the automated tests run quiet)
            var go = new GameObject("Music");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<MusicPlayer>();
        }

        static string Pretty(string n)
        {
            n = n.StartsWith("lofi_") ? n.Substring(5) : n;
            var parts = n.Split('_');
            for (int i = 0; i < parts.Length; i++) if (parts[i].Length > 0) parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            return string.Join(" ", parts);
        }

        void Awake()
        {
            clips = Resources.LoadAll<AudioClip>("Audio/Music");
            a = Source(); b = Source();
            for (int i = 0; i < clips.Length; i++) order.Add(i);
            Shuffle();
            if (clips.Length > 0) PlayNext();
            Debug.Log($"[Music] {clips.Length} tracks; playing {NowPlaying}");
        }

        AudioSource Source()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.spatialBlend = 0f; s.playOnAwake = false; s.loop = false;
            s.ignoreListenerPause = true; s.bypassReverbZones = true; s.priority = 0;
            return s;
        }

        void Shuffle()
        {
            for (int i = order.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }
            next = 0;
        }

        void PlayNext()
        {
            if (clips.Length == 0) return;
            if (next >= order.Count) Shuffle();
            var clip = clips[order[next++]];
            if (a.isPlaying && clip == a.clip && clips.Length > 1) { PlayNext(); return; }
            (a, b) = (b, a);
            a.clip = clip; a.time = 0f; a.volume = 0f; a.Play();
            fade = 0f;
        }

        void Update()
        {
            if (clips == null || clips.Length == 0) return;
            float dt = Time.unscaledDeltaTime;
            var ui = GameUI.Instance;
            bool inMenu = ui == null || ui.InMainMenu;
            bool want = inMenu || GameSettings.MusicInGame;
            // quieter in a machine (the engine is the star there)
            float duckTo = !inMenu && ui != null && ui.ActiveVehicle != null ? 0.55f : 1f;
            duck = Mathf.MoveTowards(duck, duckTo, dt * 0.5f);
            fade = Mathf.MoveTowards(fade, 1f, dt / Crossfade);
            float vol = GameSettings.MusicVolume * duck * (want ? 1f : 0f);
            onOff = Mathf.MoveTowards(onOff, want ? 1f : 0f, dt / 1.5f);
            a.volume = vol * fade * onOff * 0.9f;
            if (b.isPlaying) { b.volume = vol * (1f - fade) * onOff * 0.9f; if (fade >= 1f) b.Stop(); }
            // near the end of a song: the next one fades in over it
            if (a.clip != null && (!a.isPlaying || a.time > a.clip.length - Crossfade - 0.5f)) PlayNext();
        }
        float onOff = 1f;
    }
}
