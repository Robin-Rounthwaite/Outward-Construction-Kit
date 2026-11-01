using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

/// <summary>
/// Suggests where things are on this PC, only ever by reading. Everything it finds is a suggestion, which the
/// person confirms in the paths window, because a PC can have several installs and several mod profiles.
/// </summary>
internal static class PathDetector
{
    private static readonly string[] s_gameDataPaths =
    {
        Path.Combine("Outward", "Outward_Defed", "Outward Definitive Edition_Data"),
        Path.Combine("Outward Definitive Edition", "Outward Definitive Edition_Data"),
    };

    // r2modman and the Thunderstore app keep profiles in the same layout.
    private static readonly string[] s_modManagerGameFolders =
    {
        Path.Combine("r2modmanPlus-local", "OutwardDe"),
        Path.Combine("Thunderstore Mod Manager", "DataFolder", "OutwardDe"),
    };

    /// <summary>Each Definitive Edition data folder in a Steam library on this PC.</summary>
    public static List<string> GameDataFolders()
    {
        return SteamLibraries().SelectMany(library => s_gameDataPaths.Select(p => Path.Combine(library, "steamapps", "common", p)))
            .Where(IsGameData).Distinct().ToList();
    }

    /// <summary>Each place a region could be installed: every mod manager profile, then any BepInEx in the game itself.</summary>
    public static List<string> PluginFolders(IEnumerable<string> gameDataFolders)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var found = new List<string>();
        foreach (string manager in s_modManagerGameFolders)
            found.AddRange(ProfilePlugins(Path.Combine(appData, manager, "profiles")));
        foreach (string gameData in gameDataFolders)
        {
            string plugins = Path.Combine(Path.GetDirectoryName(gameData), "BepInEx", "plugins");
            if (Directory.Exists(plugins))
                found.Add(plugins);
        }
        return found;
    }

    public static List<string> ProfilePlugins(string profilesFolder)
    {
        if (!Directory.Exists(profilesFolder))
            return new List<string>();
        return Directory.GetDirectories(profilesFolder).Select(p => Path.Combine(p, "BepInEx", "plugins")).Where(Directory.Exists).ToList();
    }

    public static bool IsGameData(string folder)
    {
        return !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "globalgamemanagers"));
    }

    public static bool IsPluginsFolder(string folder)
    {
        return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) && Path.GetFileName(folder.TrimEnd('/', '\\')) == "plugins";
    }

    private static List<string> SteamLibraries()
    {
        string steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrEmpty(steam) || !Directory.Exists(steam))
            return new List<string>();

        var libraries = new List<string> { steam };
        string list = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(list))
            libraries.AddRange(ParseLibraries(File.ReadAllText(list)));
        return libraries;
    }

    /// <summary>The library paths in Steam's libraryfolders.vdf, whose backslashes are doubled.</summary>
    public static List<string> ParseLibraries(string vdf)
    {
        return Regex.Matches(vdf, "\"path\"\\s*\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value.Replace(@"\\", @"\")).ToList();
    }
}
