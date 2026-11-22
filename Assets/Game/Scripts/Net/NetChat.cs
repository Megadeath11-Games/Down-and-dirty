using System.Collections.Generic;
using System.IO;
using Steamworks;
using UnityEngine;

namespace FPSBase.Net
{
    /// <summary>
    /// Text chat in a multiplayer game: a line typed (T, or Enter) goes to everyone in the session, and shows on
    /// every screen with who said it.
    /// </summary>
    public partial class NetSession
    {
        const byte MsgChat = 21;
        public const int ChatMaxLength = 200;

        public struct ChatLine { public string who, text; public float at; public bool mine; }
        /// <summary>The chat so far (oldest first, the last 50 lines).</summary>
        public static readonly List<ChatLine> ChatLog = new();

        static void AddChat(string who, string text, bool mine)
        {
            ChatLog.Add(new ChatLine { who = who, text = text, at = Time.unscaledTime, mine = mine });
            while (ChatLog.Count > 50) ChatLog.RemoveAt(0);
        }

        /// <summary>Say something to everyone in the session.</summary>
        public static void Say(string text)
        {
            text = Clean(text);
            if (text.Length == 0 || Instance == null || !InSession) return;
            AddChat(Instance.MyName, text, true);
            var w = Begin(MsgChat);
            w.Write(text);
            Instance.Send(w, true, null);
        }

        /// <summary>One line, no markup tricks, not too long.</summary>
        static string Clean(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            t = t.Replace('\n', ' ').Replace('\r', ' ').Replace("<", "‹").Replace(">", "›").Trim();
            return t.Length > ChatMaxLength ? t.Substring(0, ChatMaxLength) : t;
        }

        void ReadChat(CSteamID from, BinaryReader r)
        {
            string t = Clean(r.ReadString());
            if (t.Length > 0) AddChat(NameOf(from), t, false);
        }
    }
}
