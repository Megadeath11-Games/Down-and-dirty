using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FPSBase.Core
{
    public enum GameAction
    {
        // general
        Interact, CabView,
        // on foot
        MoveForward, MoveBack, MoveLeft, MoveRight, Jump, Crouch, CrouchAlt, Sprint,
        // driving (trucks, dozer, loader, excavator in Simplified mode)
        DriveForward, DriveBack, SteerLeft, SteerRight, Handbrake,
        // dump trucks
        BoxUp, BoxDown, Tailgate,
        // bulldozer
        BladeUp, BladeDown,
        // wheel loader
        ArmsUp, ArmsDown, BucketCurl, BucketDump,
        // excavator (Realistic)
        StickOut, StickIn, SwingLeft, SwingRight, BoomUp, BoomDown, ExBucketCurl, ExBucketDump,
        TracksForward, TracksBack, TracksLeft, TracksRight,
        // added with the big fleet
        RipperUp, RipperDown, ExBladeUp, ExBladeDown,
        // quality of life + metal detector
        Map, Help, Recover, DetectorToggle, HandDig, DetectorDiscriminate,
        // motor grader
        GraderBladeUp, GraderBladeDown, GraderTiltLeft, GraderTiltRight, GraderCircleLeft, GraderCircleRight,
        GraderShiftLeft, GraderShiftRight, GraderPitchForward, GraderPitchBack, GraderArticLeft, GraderArticRight,
        GraderLeanLeft, GraderLeanRight, GraderRipperDown, GraderRipperUp,
        // multiplayer
        Invite,
    }

    /// <summary>Rebindable keyboard controls, saved between sessions. Esc is always the menu key.</summary>
    public static class Controls
    {
        public const string General = "General";

        public readonly struct Binding
        {
            public readonly GameAction action; public readonly string category, label; public readonly Key key;
            public Binding(GameAction a, string c, string l, Key k) { action = a; category = c; label = l; key = k; }
        }

        public static readonly Binding[] Defaults =
        {
            new(GameAction.Interact, General, "Use / shops / get in & out of vehicles", Key.F),
            new(GameAction.CabView, General, "Camera: 1st / 3rd person (vehicles: cab view)", Key.V),
            new(GameAction.Map, General, "Map, fast travel & your machines", Key.M),
            new(GameAction.Help, General, "Help & tips", Key.F1),
            new(GameAction.Recover, General, "Vehicle: flip upright / get unstuck", Key.B),

            new(GameAction.MoveForward, "On foot", "Move forward", Key.W),
            new(GameAction.MoveBack, "On foot", "Move back", Key.S),
            new(GameAction.MoveLeft, "On foot", "Move left", Key.A),
            new(GameAction.MoveRight, "On foot", "Move right", Key.D),
            new(GameAction.Jump, "On foot", "Jump", Key.Space),
            new(GameAction.Crouch, "On foot", "Crouch / slide", Key.LeftCtrl),
            new(GameAction.CrouchAlt, "On foot", "Crouch (second key)", Key.C),
            new(GameAction.Sprint, "On foot", "Sprint", Key.LeftShift),

            new(GameAction.DetectorToggle, "Metal detector (on foot)", "Take out / put away metal detector", Key.G),
            new(GameAction.HandDig, "Metal detector (on foot)", "Dig with hand shovel", Key.R),
            new(GameAction.DetectorDiscriminate, "Metal detector (on foot)", "Iron discrimination on/off", Key.X),

            new(GameAction.DriveForward, "Driving", "Forward / throttle", Key.W),
            new(GameAction.DriveBack, "Driving", "Reverse / brake", Key.S),
            new(GameAction.SteerLeft, "Driving", "Steer / turn left", Key.A),
            new(GameAction.SteerRight, "Driving", "Steer / turn right", Key.D),
            new(GameAction.Handbrake, "Driving", "Handbrake (trucks)", Key.Space),

            new(GameAction.BoxUp, "Dump truck", "Raise dump box", Key.E),
            new(GameAction.BoxDown, "Dump truck", "Lower dump box", Key.Q),
            new(GameAction.Tailgate, "Dump truck", "Tailgate opening (full / half / quarter / spread / latched)", Key.R),

            new(GameAction.BladeUp, "Bulldozer", "Blade up", Key.E),
            new(GameAction.BladeDown, "Bulldozer", "Blade down", Key.Q),
            new(GameAction.RipperUp, "Bulldozer", "Ripper up (big dozers)", Key.X),
            new(GameAction.RipperDown, "Bulldozer", "Ripper down (big dozers)", Key.Z),

            new(GameAction.ArmsUp, "Loader & skid steer", "Raise arms", Key.E),
            new(GameAction.ArmsDown, "Loader & skid steer", "Lower arms", Key.Q),
            new(GameAction.BucketCurl, "Loader & skid steer", "Curl bucket back", Key.R),
            new(GameAction.BucketDump, "Loader & skid steer", "Tip / dump bucket", Key.T),

            new(GameAction.StickOut, "Excavator (Realistic)", "Stick out", Key.W),
            new(GameAction.StickIn, "Excavator (Realistic)", "Stick in", Key.S),
            new(GameAction.SwingLeft, "Excavator (Realistic)", "Swing left", Key.A),
            new(GameAction.SwingRight, "Excavator (Realistic)", "Swing right", Key.D),
            new(GameAction.BoomUp, "Excavator (Realistic)", "Boom up", Key.K),
            new(GameAction.BoomDown, "Excavator (Realistic)", "Boom down", Key.I),
            new(GameAction.ExBucketCurl, "Excavator (Realistic)", "Bucket curl (dig)", Key.J),
            new(GameAction.ExBucketDump, "Excavator (Realistic)", "Bucket open (dump)", Key.L),
            new(GameAction.TracksForward, "Excavator (Realistic)", "Tracks forward", Key.UpArrow),
            new(GameAction.TracksBack, "Excavator (Realistic)", "Tracks back", Key.DownArrow),
            new(GameAction.TracksLeft, "Excavator (Realistic)", "Tracks turn left", Key.LeftArrow),
            new(GameAction.TracksRight, "Excavator (Realistic)", "Tracks turn right", Key.RightArrow),
            new(GameAction.ExBladeUp, "Excavator blade", "Front blade up", Key.E),
            new(GameAction.ExBladeDown, "Excavator blade", "Front blade down", Key.Q),

            new(GameAction.GraderBladeUp, "Motor grader", "Blade up (both lift rams)", Key.E),
            new(GameAction.GraderBladeDown, "Motor grader", "Blade down (both lift rams)", Key.Q),
            new(GameAction.GraderTiltLeft, "Motor grader", "Cross slope: left end down", Key.R),
            new(GameAction.GraderTiltRight, "Motor grader", "Cross slope: right end down", Key.T),
            new(GameAction.GraderCircleLeft, "Motor grader", "Circle: turn blade to windrow left", Key.Z),
            new(GameAction.GraderCircleRight, "Motor grader", "Circle: turn blade to windrow right", Key.X),
            new(GameAction.GraderShiftLeft, "Motor grader", "Side-shift blade left", Key.LeftArrow),
            new(GameAction.GraderShiftRight, "Motor grader", "Side-shift blade right", Key.RightArrow),
            new(GameAction.GraderPitchForward, "Motor grader", "Blade pitch forward", Key.UpArrow),
            new(GameAction.GraderPitchBack, "Motor grader", "Blade pitch back", Key.DownArrow),
            new(GameAction.GraderArticLeft, "Motor grader", "Articulate frame left", Key.Comma),
            new(GameAction.GraderArticRight, "Motor grader", "Articulate frame right", Key.Period),
            new(GameAction.GraderLeanLeft, "Motor grader", "Lean front wheels left", Key.K),
            new(GameAction.GraderLeanRight, "Motor grader", "Lean front wheels right", Key.L),
            new(GameAction.GraderRipperDown, "Motor grader", "Ripper / scarifier down", Key.G),
            new(GameAction.GraderRipperUp, "Motor grader", "Ripper / scarifier up", Key.H),

            new(GameAction.Invite, General, "Multiplayer: host / invite a Steam friend", Key.F2),
        };

        static Dictionary<GameAction, Key> map;
        static Dictionary<GameAction, Binding> defs;

        static void Load()
        {
            if (map != null) return;
            map = new Dictionary<GameAction, Key>();
            defs = new Dictionary<GameAction, Binding>();
            foreach (var d in Defaults)
            {
                defs[d.action] = d;
                map[d.action] = (Key)PlayerPrefs.GetInt("key." + d.action, (int)d.key);
            }
        }

        public static Key Get(GameAction a) { Load(); return map.TryGetValue(a, out var k) ? k : Key.None; }

        public static void Set(GameAction a, Key k)
        {
            Load();
            map[a] = k;
            PlayerPrefs.SetInt("key." + a, (int)k);
            PlayerPrefs.Save();
        }

        public static void ResetAll()
        {
            Load();
            foreach (var d in Defaults) { map[d.action] = d.key; PlayerPrefs.DeleteKey("key." + d.action); }
            PlayerPrefs.Save();
        }

        static KeyControl Ctl(GameAction a)
        {
            var kb = Keyboard.current;
            var k = Get(a);
            if (kb == null || k == Key.None) return null;
            return kb[k];
        }

        public static bool Held(GameAction a) => Ctl(a)?.isPressed ?? false;
        public static bool Pressed(GameAction a) => Ctl(a)?.wasPressedThisFrame ?? false;
        public static float Axis(GameAction positive, GameAction negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);

        /// <summary>Short display name of the key bound to an action, e.g. "F", "Space", "L-Shift".</summary>
        public static string N(GameAction a) => KeyName(Get(a));

        public static string KeyName(Key k) => k switch
        {
            Key.None => "—",
            Key.Space => "Space",
            Key.LeftShift => "L-Shift", Key.RightShift => "R-Shift",
            Key.LeftCtrl => "L-Ctrl", Key.RightCtrl => "R-Ctrl",
            Key.LeftAlt => "L-Alt", Key.RightAlt => "R-Alt",
            Key.UpArrow => "Up", Key.DownArrow => "Down", Key.LeftArrow => "Left", Key.RightArrow => "Right",
            Key.Enter => "Enter", Key.Tab => "Tab", Key.Backspace => "Backspace", Key.F1 => "F1",
            _ => Keyboard.current != null && !string.IsNullOrEmpty(Keyboard.current[k].displayName) ? Keyboard.current[k].displayName.ToUpperInvariant() : k.ToString(),
        };

        /// <summary>Other actions that use the same key at the same time (same group, or a General action).</summary>
        public static string Conflicts(GameAction a)
        {
            Load();
            var k = Get(a);
            if (k == Key.None) return null;
            var me = defs[a];
            List<string> hits = null;
            foreach (var d in Defaults)
            {
                if (d.action == a || Get(d.action) != k) continue;
                bool together = d.category == me.category || d.category == General || me.category == General;
                if (!together) continue;
                (hits ??= new List<string>()).Add(d.label);
            }
            return hits == null ? null : "also: " + string.Join(", ", hits);
        }
    }
}
