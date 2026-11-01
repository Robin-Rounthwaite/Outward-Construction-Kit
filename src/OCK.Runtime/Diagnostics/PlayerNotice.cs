using System.Collections.Generic;
using System.Linq;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>Tells the player on screen when a region has a problem, since few people read a log. Each says so once.</summary>
    internal static class PlayerNotice
    {
        private static readonly List<string> s_pending = new List<string>();
        private static readonly HashSet<string> s_said = new HashSet<string>();

        public static void Add(string message)
        {
            if (!s_said.Add(message))
                return;
            KitPlugin.Log.LogWarning("Notice: " + message);
            s_pending.Add(message);
        }

        /// <summary>Shows what's waiting, once a player is in the world to see it.</summary>
        public static void ShowPending()
        {
            if (s_pending.Count == 0 || CharacterManager.Instance == null)
                return;
            Character player = CharacterManager.Instance.GetFirstLocalCharacter();
            if (player == null || player.CharacterUI == null)
                return;

            player.CharacterUI.ShowInfoNotification("Construction Kit: " + string.Join(" ", s_pending.ToArray()) + " See BepInEx/LogOutput.log.");
            s_pending.Clear();
        }
    }
}
