using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// A small line on screen for someone testing their own region: where they are, the door hotkeys, and how many
    /// errors the kit has logged. It only appears for a game started from Unity's Test or Launch buttons, which leave a
    /// file saying so, or with Dev.AuthorHelp on, so players of a finished mod never see it.
    /// </summary>
    internal class AuthorHelp : MonoBehaviour
    {
        private const string SESSION_FILE = "OCK_AuthorSession.txt";
        private const int FONT_SIZE = 14;
        private const float HEIGHT = 26f;

        private GUIStyle m_style;
        private ErrorCounter m_errors;

        public static void BeginIfWanted(KitPlugin plugin)
        {
            string file = Path.Combine(Paths.BepInExRootPath, SESSION_FILE);
            bool session = File.Exists(file);
            if (session)
                File.Delete(file);
            if (session || plugin.AuthorHelpOn.Value)
                plugin.gameObject.AddComponent<AuthorHelp>();
        }

        private void Awake()
        {
            m_errors = new ErrorCounter();
            BepInEx.Logging.Logger.Listeners.Add(m_errors);
        }

        private void OnDestroy()
        {
            BepInEx.Logging.Logger.Listeners.Remove(m_errors);
        }

        /// <summary>The line's words, apart from the game state they're filled in from.</summary>
        internal static string Line(string regionName, string where, string chosen, string placeKey, string chooseKey, int errors)
        {
            string line = $"{regionName ?? "Not in a region"} at {where}  |  {chooseKey} chooses a region ({chosen ?? "none installed"}), {placeKey} places its door";
            return errors > 0 ? line + $"  |  {errors} kit errors: see BepInEx\\LogOutput.log" : line;
        }

        private void OnGUI()
        {
            if (Global.HideUI)
                return;
            Character player = CharacterManager.Instance != null ? CharacterManager.Instance.GetFirstLocalCharacter() : null;
            if (player == null)
                return;
            if (m_style == null)
                m_style = new GUIStyle(GUI.skin.box) { fontSize = FONT_SIZE, alignment = TextAnchor.MiddleLeft };
            Region here = Region.ForScene(SceneManager.GetActiveScene().name);
            KitPlugin plugin = KitPlugin.Instance;
            GUI.Box(new Rect(0f, 0f, Screen.width, HEIGHT), Line(here != null ? here.DisplayName : null, KitPlugin.FormatVector(player.transform.position), plugin.SelectedRegionName, plugin.PlaceDoorKey.Value.ToString(), plugin.NextRegionKey.Value.ToString(), m_errors.Count), m_style);
        }

        private class ErrorCounter : ILogListener
        {
            public int Count { get; private set; }

            public void LogEvent(object sender, LogEventArgs eventArgs)
            {
                if (eventArgs.Level == LogLevel.Error && eventArgs.Source.SourceName == KitPlugin.NAME)
                    Count++;
            }

            public void Dispose()
            {
            }
        }
    }
}
