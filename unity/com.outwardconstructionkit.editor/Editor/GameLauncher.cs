using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Starts the game with the mods in the chosen profile, the way r2modman does: through Steam, with the Definitive
/// Edition's ID, pointed at the profile's BepInEx. Testing a region also leaves the kit a note to go straight there.
/// </summary>
internal static class GameLauncher
{
    private const int APP_ID = 1758860;
    private const string REQUEST_FILE = "OCK_AutoTest.txt"; // the plugin's AutoTest reads this
    private const string AUTHOR_SESSION_FILE = "OCK_AuthorSession.txt"; // and the plugin's AuthorHelp this
    private const string CHARACTER_PREF = "OCK.TestCharacterId";
    private const string OLD_CHARACTER_NAME_PREF = "OCK.TestCharacter"; // a name, from before characters were chosen from a list
    private const double SECONDS_TO_APPEAR = 60d;
    private const double SECONDS_BETWEEN_LOOKS = 3d;

    private static double s_startedAt;
    private static double s_nextLook;

    /// <summary>The save ID of who a test plays as: the last one chosen, or else whoever's top of the list.</summary>
    public static string TestCharacterId
    {
        get
        {
            List<TestCharacters.Character> all = TestCharacters.All;
            string id = EditorPrefs.GetString(CHARACTER_PREF, "");
            if (all.Any(c => c.Id == id))
                return id;
            string oldName = EditorPrefs.GetString(OLD_CHARACTER_NAME_PREF, "");
            TestCharacters.Character chosen = all.FirstOrDefault(c => c.Name == oldName) ?? all.FirstOrDefault();
            return chosen != null ? chosen.Id : "";
        }
        set { EditorPrefs.SetString(CHARACTER_PREF, value); }
    }

    /// <summary>Lets a window choose the character, with a warning of what testing does to them.</summary>
    public static void DrawCharacterChoice(float labelWidth)
    {
        var label = new GUIContent("Test as (Character Name)", CHARACTER_HELP);
        List<TestCharacters.Character> all = TestCharacters.All;
        if (all.Count == 0)
        {
            EditorGUILayout.LabelField(label, new GUIContent("No saved characters found. Make one in the game first."));
            return;
        }
        string current = TestCharacterId;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            int picked = EditorGUILayout.Popup(Mathf.Max(0, all.FindIndex(c => c.Id == current)), all.Select(c => new GUIContent(c.Label, CHARACTER_HELP)).ToArray());
            if (all[picked].Id != current)
                TestCharacterId = all[picked].Id;
        }
    }

    private const string CHARACTER_HELP = "Who the test plays as. The game saves as normal, so this character's save changes, and a character kept just for testing is safest.";

    [MenuItem("Construction Kit/Launch Game")]
    public static void LaunchFromMenu()
    {
        EditorUtility.DisplayDialog("Construction Kit", Launch(), "OK");
    }

    [MenuItem("Construction Kit/Test Region In-Game")]
    public static void TestFromMenu()
    {
        EditorUtility.DisplayDialog("Construction Kit", TestRegion(), "OK");
    }

    /// <summary>Starts the game and says how that went.</summary>
    public static string Launch()
    {
        string refused = ReasonNotToStart();
        if (refused != null)
            return refused;
        File.Delete(RequestPath()); // a leftover would take the game somewhere nobody asked for
        File.WriteAllText(AuthorSessionPath(), "");
        return Start("Starting the game with your mods.");
    }

    /// <summary>Exports the open region, then starts the game, which goes straight to it, at a spot if given. Says how that went.</summary>
    public static string TestRegion(Vector3? startAt = null)
    {
        string refused = ReasonNotToStart();
        if (refused != null)
            return refused;

        if (!PathDetector.HasKit(OckSettings.PluginsFolder))
            return $"The game wasn't started, because the kit isn't installed in '{PathDetector.ProfileName(OckSettings.PluginsFolder)}', so nothing could take you to your region. Install it there with r2modman, or pick the profile you test with in Construction Kit > Paths.";

        string character = TestCharacterId;
        if (character.Length == 0)
            return "No saved characters were found, so the game wasn't started. Start the game, make a character, and try again.";

        string id = RegionInfo.For(SceneManager.GetActiveScene().path).Id;
        string exported;
        try
        {
            exported = RegionExporter.ExportToProfile();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogException(e);
            return "Export failed, so the game wasn't started: " + e.Message;
        }
        File.WriteAllLines(RequestPath(), TestRequest(id, character, startAt));
        File.WriteAllText(AuthorSessionPath(), "");
        TestCharacters.MarkUsed(character);
        return Start(exported + "\n\nStarting the game, which goes straight to your region.");
    }

    public static string[] TestRequest(string regionId, string characterId, Vector3? startAt = null)
    {
        var request = new List<string> { "characterid " + characterId, "visit " + regionId };
        if (startAt.HasValue)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            request.Add("startat " + startAt.Value.x.ToString("0.###", ci) + "," + startAt.Value.y.ToString("0.###", ci) + "," + startAt.Value.z.ToString("0.###", ci));
        }
        return request.ToArray();
    }

    public static string SteamArguments(string bepInExFolder)
    {
        return $"-applaunch {APP_ID} --doorstop-enable true --doorstop-target \"{Path.Combine(bepInExFolder, "core", "BepInEx.Preloader.dll")}\"";
    }

    private static string BepInExFolder()
    {
        return Path.GetDirectoryName(OckSettings.PluginsFolder.TrimEnd('\\', '/'));
    }

    private static string RequestPath()
    {
        return Path.Combine(BepInExFolder(), REQUEST_FILE);
    }

    // Started from here means it's the region's author at the keyboard, which turns on the plugin's help line for that run.
    private static string AuthorSessionPath()
    {
        return Path.Combine(BepInExFolder(), AUTHOR_SESSION_FILE);
    }

    private static string ReasonNotToStart()
    {
        if (!OckSettings.PathsConfirmed)
        {
            PathsWindow.Open();
            return "Please confirm where the game and your mods are first.";
        }
        if (IsGameRunning())
            return "The game wasn't started, because it's still running or still closing. Close it, wait a moment, and try again.";
        if (!File.Exists(Path.Combine(BepInExFolder(), "core", "BepInEx.Preloader.dll")))
            return "BepInEx isn't in the profile you chose in Construction Kit > Paths. Install the kit into that profile with r2modman, and click Start modded there once.";
        return null;
    }

    private static string Start(string message)
    {
        string steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", null) as string;
        if (string.IsNullOrEmpty(steam) || !File.Exists(steam))
            return "Steam wasn't found, so the game wasn't started. Use Start modded in r2modman instead.";
        Process.Start(new ProcessStartInfo(steam, SteamArguments(BepInExFolder())) { UseShellExecute = true });
        s_startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.update -= WatchForGame;
        EditorApplication.update += WatchForGame;
        return message + " If Steam asks whether to allow the launch options, allow them.";
    }

    // Steam gives no answer when it's asked to start a game, so the only way to know is to look for it.
    private static void WatchForGame()
    {
        double waited = EditorApplication.timeSinceStartup - s_startedAt;
        if (waited < s_nextLook)
            return;
        s_nextLook = waited + SECONDS_BETWEEN_LOOKS;
        if (IsGameRunning())
        {
            StopWatching();
            return;
        }
        if (waited < SECONDS_TO_APPEAR)
            return;
        StopWatching();
        EditorUtility.DisplayDialog("Construction Kit", $"The game hasn't appeared after {SECONDS_TO_APPEAR:0} seconds. Check whether Steam is waiting for an answer behind another window, then try again, or use Start modded in r2modman.", "OK");
    }

    private static void StopWatching()
    {
        EditorApplication.update -= WatchForGame;
        s_nextLook = 0d;
    }

    private static bool IsGameRunning()
    {
        return Process.GetProcesses().Any(p => p.ProcessName.StartsWith("Outward", StringComparison.OrdinalIgnoreCase));
    }
}
