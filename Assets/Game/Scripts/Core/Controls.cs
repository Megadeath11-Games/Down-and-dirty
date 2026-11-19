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
        // dozer blade tilt (one end down) and angle (swivel left / right)
        BladeTiltLeft, BladeTiltRight, BladeAngleLeft, BladeAngleRight,
        // pickup transfer case (2WD / 4-high / 4-low) and rear diff lock
        TransferCase, DiffLock,
        // your phone: customers in town
        Phone,
    }

    /// <summary>Controller inputs (Xbox names; a PlayStation pad is the same places: A = Cross, B = Circle, X = Square, Y = Triangle).</summary>
    public enum Pad { None, A, B, X, Y, LB, RB, LT, RT, L3, R3, Start, Select, DUp, DDown, DLeft, DRight, LUp, LDown, LLeft, LRight, RUp, RDown, RLeft, RRight }

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
            new(GameAction.Phone, General, "Phone: customers' texts and your jobs", Key.P),
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
            new(GameAction.TransferCase, "Dump truck", "Pickup 4x4: 2WD / 4-high / 4-low", Key.X),
            new(GameAction.DiffLock, "Dump truck", "Rear diff lock on / off", Key.Z),

            new(GameAction.BladeUp, "Bulldozer", "Blade up", Key.E),
            new(GameAction.BladeDown, "Bulldozer", "Blade down", Key.Q),
            new(GameAction.RipperUp, "Bulldozer", "Ripper up (big dozers)", Key.X),
            new(GameAction.RipperDown, "Bulldozer", "Ripper down (big dozers)", Key.Z),
            new(GameAction.BladeTiltLeft, "Bulldozer", "Blade tilt: left end down", Key.R),
            new(GameAction.BladeTiltRight, "Bulldozer", "Blade tilt: right end down", Key.T),
            new(GameAction.BladeAngleLeft, "Bulldozer", "Blade swivel left (dirt rolls off the left)", Key.G),
            new(GameAction.BladeAngleRight, "Bulldozer", "Blade swivel right (dirt rolls off the right)", Key.H),

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

        /// <summary>
        /// The controller layout. Like the keys, the same button does different things in different groups
        /// (on foot / driving / each machine), and analog sticks and triggers give a proportional push.
        /// </summary>
        public static readonly Dictionary<GameAction, Pad> PadMap = new()
        {
            [GameAction.Interact] = Pad.X, [GameAction.CabView] = Pad.R3, [GameAction.Map] = Pad.Select, [GameAction.Recover] = Pad.DDown, [GameAction.Phone] = Pad.DLeft,
            // on foot (right stick looks)
            [GameAction.MoveForward] = Pad.LUp, [GameAction.MoveBack] = Pad.LDown, [GameAction.MoveLeft] = Pad.LLeft, [GameAction.MoveRight] = Pad.LRight,
            [GameAction.Jump] = Pad.A, [GameAction.Crouch] = Pad.B, [GameAction.Sprint] = Pad.L3,
            [GameAction.DetectorToggle] = Pad.DUp, [GameAction.HandDig] = Pad.RT, [GameAction.DetectorDiscriminate] = Pad.DRight,
            // driving: triggers for gas / brake-reverse, left stick steers
            [GameAction.DriveForward] = Pad.RT, [GameAction.DriveBack] = Pad.LT, [GameAction.SteerLeft] = Pad.LLeft, [GameAction.SteerRight] = Pad.LRight,
            [GameAction.Handbrake] = Pad.A,
            [GameAction.BoxUp] = Pad.RB, [GameAction.BoxDown] = Pad.LB, [GameAction.Tailgate] = Pad.Y, [GameAction.TransferCase] = Pad.DUp, [GameAction.DiffLock] = Pad.DRight,
            // dozer: right stick = blade (pull back raises, sideways tilts), bumpers swivel, Y / B ripper
            [GameAction.BladeUp] = Pad.RDown, [GameAction.BladeDown] = Pad.RUp, [GameAction.BladeTiltLeft] = Pad.RLeft, [GameAction.BladeTiltRight] = Pad.RRight,
            [GameAction.BladeAngleLeft] = Pad.LB, [GameAction.BladeAngleRight] = Pad.RB, [GameAction.RipperUp] = Pad.Y, [GameAction.RipperDown] = Pad.B,
            // loader / skid steer: right stick = arms (pull back raises) and bucket (left curls, right dumps)
            [GameAction.ArmsUp] = Pad.RDown, [GameAction.ArmsDown] = Pad.RUp, [GameAction.BucketCurl] = Pad.RLeft, [GameAction.BucketDump] = Pad.RRight,
            // excavator (ISO): left stick = swing + stick, right stick = boom + bucket; triggers drive, D-pad turns
            [GameAction.SwingLeft] = Pad.LLeft, [GameAction.SwingRight] = Pad.LRight, [GameAction.StickOut] = Pad.LUp, [GameAction.StickIn] = Pad.LDown,
            [GameAction.BoomUp] = Pad.RDown, [GameAction.BoomDown] = Pad.RUp, [GameAction.ExBucketCurl] = Pad.RLeft, [GameAction.ExBucketDump] = Pad.RRight,
            [GameAction.TracksForward] = Pad.RT, [GameAction.TracksBack] = Pad.LT, [GameAction.TracksLeft] = Pad.DLeft, [GameAction.TracksRight] = Pad.DRight,
            [GameAction.ExBladeUp] = Pad.RB, [GameAction.ExBladeDown] = Pad.LB,
            // grader: right stick = blade lift and cross slope, bumpers turn the circle, D-pad side-shifts / pitches
            [GameAction.GraderBladeUp] = Pad.RDown, [GameAction.GraderBladeDown] = Pad.RUp, [GameAction.GraderTiltLeft] = Pad.RLeft, [GameAction.GraderTiltRight] = Pad.RRight,
            [GameAction.GraderCircleLeft] = Pad.LB, [GameAction.GraderCircleRight] = Pad.RB, [GameAction.GraderShiftLeft] = Pad.DLeft, [GameAction.GraderShiftRight] = Pad.DRight,
            [GameAction.GraderPitchForward] = Pad.DUp, [GameAction.GraderPitchBack] = Pad.Y, [GameAction.GraderRipperDown] = Pad.B,
        };

        public static Pad PadOf(GameAction a) => PadMap.TryGetValue(a, out var p) ? p : Pad.None;

        static ButtonControl PadCtl(Pad p)
        {
            var gp = Gamepad.current;
            if (gp == null) return null;
            return p switch
            {
                Pad.A => gp.buttonSouth, Pad.B => gp.buttonEast, Pad.X => gp.buttonWest, Pad.Y => gp.buttonNorth,
                Pad.LB => gp.leftShoulder, Pad.RB => gp.rightShoulder, Pad.LT => gp.leftTrigger, Pad.RT => gp.rightTrigger,
                Pad.L3 => gp.leftStickButton, Pad.R3 => gp.rightStickButton, Pad.Start => gp.startButton, Pad.Select => gp.selectButton,
                Pad.DUp => gp.dpad.up, Pad.DDown => gp.dpad.down, Pad.DLeft => gp.dpad.left, Pad.DRight => gp.dpad.right,
                Pad.LUp => gp.leftStick.up, Pad.LDown => gp.leftStick.down, Pad.LLeft => gp.leftStick.left, Pad.LRight => gp.leftStick.right,
                Pad.RUp => gp.rightStick.up, Pad.RDown => gp.rightStick.down, Pad.RLeft => gp.rightStick.left, Pad.RRight => gp.rightStick.right,
                _ => null,
            };
        }

        /// <summary>A machine's camera is using the right stick to look around (L3 held): the machine ignores it meanwhile.</summary>
        public static void PadLooking() => padLookFrame = Time.frameCount;
        static int padLookFrame = -10;
        static bool RightStickTaken(Pad p) => p >= Pad.RUp && Time.frameCount - padLookFrame <= 1;

        /// <summary>A menu or panel is up: the controller is driving the pointer, not the player (the View button still closes the map).</summary>
        static bool PadOnMenu(GameAction a) => a != GameAction.Map && a != GameAction.Phone && GameUI.Instance != null && GameUI.Instance.BlocksGameplay;

        /// <summary>The key for this action was pressed this frame (keyboard only, not the controller).</summary>
        public static bool KeyPressed(GameAction a) => Ctl(a)?.wasPressedThisFrame ?? false;

        static float PadValue(GameAction a)
        {
            var p = PadOf(a);
            var c = PadCtl(p);
            if (c == null || RightStickTaken(p) || PadOnMenu(a)) return 0f;
            return Mathf.Clamp01(c.ReadValue());
        }

        /// <summary>A controller is plugged in.</summary>
        public static bool HasPad => Gamepad.current != null;

        /// <summary>Esc, or on a controller Start (anywhere) / B (on a menu with the pointer showing): back out / pause.</summary>
        public static bool MenuBack(bool pointerShowing)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp == null) return false;
            return gp.startButton.wasPressedThisFrame || (pointerShowing && gp.buttonEast.wasPressedThisFrame);
        }

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

        /// <summary>
        /// Second keys for the excavator's right hand: the number pad (8/5 boom down/up, 4/6 bucket curl/open).
        /// Many keyboards can't register some three- or four-key combinations (I/J/K/L held together with W/A/S/D
        /// is a common one); the number pad is usually wired apart, so stick, swing, boom and bucket all work at once.
        /// </summary>
        static readonly Dictionary<GameAction, Key> Alt = new()
        {
            [GameAction.BoomDown] = Key.Numpad8, [GameAction.BoomUp] = Key.Numpad5,
            [GameAction.ExBucketCurl] = Key.Numpad4, [GameAction.ExBucketDump] = Key.Numpad6,
        };
        public static string AltName(GameAction a) => Alt.TryGetValue(a, out var k) ? KeyName(k) : null;
        static bool Down(GameAction a)
        {
            if (Ctl(a)?.isPressed ?? false) return true;
            var kb = Keyboard.current;
            return kb != null && Alt.TryGetValue(a, out var k) && Get(a) != k && kb[k].isPressed;
        }

        static KeyControl Ctl(GameAction a)
        {
            var kb = Keyboard.current;
            var k = Get(a);
            if (kb == null || k == Key.None) return null;
            return kb[k];
        }

        public static bool Held(GameAction a) => Down(a) || PadValue(a) > 0.5f;
        public static bool Pressed(GameAction a)
        {
            if (Ctl(a)?.wasPressedThisFrame ?? false) return true;
            var p = PadOf(a);
            var c = PadCtl(p);
            return c != null && !RightStickTaken(p) && !PadOnMenu(a) && c.wasPressedThisFrame;
        }
        /// <summary>-1..1: keys are all-or-nothing, a stick or trigger pushes in proportion.</summary>
        /// <summary>Tests: hold actions down as if a player pressed them (0..1).</summary>
        public static readonly Dictionary<GameAction, float> TestHeld = new();
        static float TestValue(GameAction a) => TestHeld.Count > 0 && TestHeld.TryGetValue(a, out float v) ? v : 0f;

        public static float Axis(GameAction positive, GameAction negative)
        {
            if (TestHeld.Count > 0) return Mathf.Clamp(TestValue(positive) - TestValue(negative), -1f, 1f);
            float k = (Down(positive) ? 1f : 0f) - (Down(negative) ? 1f : 0f);
            return Mathf.Clamp(k + PadValue(positive) - PadValue(negative), -1f, 1f);
        }

        /// <summary>Controller button name for an action ("RT", "Left stick up"...), or null if it has none.</summary>
        public static string PadName(GameAction a) => PadOf(a) switch
        {
            Pad.None => null,
            Pad.LUp => "L-stick up", Pad.LDown => "L-stick down", Pad.LLeft => "L-stick left", Pad.LRight => "L-stick right",
            Pad.RUp => "R-stick up", Pad.RDown => "R-stick down", Pad.RLeft => "R-stick left", Pad.RRight => "R-stick right",
            Pad.DUp => "D-pad up", Pad.DDown => "D-pad down", Pad.DLeft => "D-pad left", Pad.DRight => "D-pad right",
            Pad.Select => "View", Pad.Start => "Menu",
            var p => p.ToString(),
        };

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
