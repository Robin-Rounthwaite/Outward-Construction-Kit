using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using UnityEditor;
using UnityEngine.SceneManagement;

/// <summary>
/// Starts the game with the mods in the chosen profile, the way r2modman does: through Steam, with the Definitive
/// Edition's ID, pointed at the profile's BepInEx. Test Region also leaves the kit a note to go straight to a region.
/// </summary>
internal static class GameLauncher
{
    private const int APP_ID = 1758860;
    private const string REQUEST_FILE = "OCK_AutoTest.txt"; // the plugin's AutoTest reads this
    private const string CHARACTER_PREF = "OCK.TestCharacter";

    /// <summary>Who Test Region loads. Empty means the first character the game lists.</summary>
    public static string TestCharacter
    {
        get { return EditorPrefs.GetString(CHARACTER_PREF, ""); }
        set { EditorPrefs.SetString(CHARACTER_PREF, value); }
    }

    [MenuItem("Construction Kit/Launch Game")]
    public static void LaunchFromMenu()
    {
        EditorUtility.DisplayDialog("Construction Kit", Launch(), "OK");
    }

    [MenuItem("Construction Kit/Test Region")]
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
        return Start("Starting the game with your mods.");
    }

    /// <summary>Exports the open region, then starts the game, which goes straight to it. Says how that went.</summary>
    public static string TestRegion()
    {
        string refused = ReasonNotToStart();
        if (refused != null)
            return refused;

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
        File.WriteAllLines(RequestPath(), TestRequest(id, TestCharacter));
        return Start(exported + "\n\nStarting the game, which goes straight to your region.");
    }

    public static string[] TestRequest(string regionId, string character)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(character))
            lines.Add("character " + character);
        lines.Add("visit " + regionId);
        return lines.ToArray();
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

    private static string ReasonNotToStart()
    {
        if (!OckSettings.PathsConfirmed)
        {
            PathsWindow.Open();
            return "Please confirm where the game and your mods are first.";
        }
        if (Process.GetProcesses().Any(p => p.ProcessName.StartsWith("Outward", StringComparison.OrdinalIgnoreCase)))
            return "The game is already running. Close it first, because it holds on to what it's loaded.";
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
        return message + " If Steam asks whether to allow the launch options, allow them.";
    }
}
