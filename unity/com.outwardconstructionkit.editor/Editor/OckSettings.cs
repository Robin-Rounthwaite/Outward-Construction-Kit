using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

/// <summary>Per-machine paths, kept in EditorPrefs so they never end up in a shared project.</summary>
internal static class OckSettings
{
    /// <summary>Set once the person has checked the paths in the paths window. Nothing guesses without them seeing it.</summary>
    public static bool PathsConfirmed
    {
        get { return EditorPrefs.GetBool("OCK.PathsConfirmed", false); }
        set { EditorPrefs.SetBool("OCK.PathsConfirmed", value); }
    }

    public static string RipFolder
    {
        get { return EditorPrefs.GetString("OCK.RipFolder", ""); }
        set { EditorPrefs.SetString("OCK.RipFolder", value); }
    }

    public static string PluginsFolder
    {
        get { return EditorPrefs.GetString("OCK.PluginsFolder", ""); }
        set { EditorPrefs.SetString("OCK.PluginsFolder", value); }
    }

    /// <summary>Where Package Region saves zips. A relative path is inside the Unity project.</summary>
    public static string UploadFolder
    {
        get { return EditorPrefs.GetString("OCK.UploadFolder", RegionPackager.OUTPUT_FOLDER); }
        set { EditorPrefs.SetString("OCK.UploadFolder", value); }
    }

    /// <summary>The Thunderstore team last used, to start each new region's package with.</summary>
    public static string LastTeam
    {
        get { return EditorPrefs.GetString("OCK.LastTeam", ""); }
        set { EditorPrefs.SetString("OCK.LastTeam", value); }
    }

    /// <summary>An AssetRipper the person already has, if they'd rather use that. Empty means the kit's own.</summary>
    public static string AssetRipperFolder
    {
        get { return EditorPrefs.GetString("OCK.AssetRipperFolder", ""); }
        set { EditorPrefs.SetString("OCK.AssetRipperFolder", value); }
    }

    /// <summary>Each AssetRipper export under the rip folder, or the folder itself if it's one export.</summary>
    public static IEnumerable<string> RipRoots()
    {
        string folder = RipFolder;
        if (string.IsNullOrEmpty(folder))
            return new string[0];
        if (Directory.Exists(Path.Combine(folder, "Assets", "Scenes")))
            return new[] { folder };
        if (!Directory.Exists(folder))
            return new string[0];
        return Directory.GetDirectories(folder).Select(d => Path.Combine(d, "ExportedProject"))
            .Where(d => Directory.Exists(Path.Combine(d, "Assets", "Scenes"))).ToList();
    }

    public static string GameData
    {
        get { return EditorPrefs.GetString("OCK.GameData", ""); }
        set { EditorPrefs.SetString("OCK.GameData", value); }
    }

    public static string EnvironmentDonor
    {
        get { return EditorPrefs.GetString("OCK.EnvironmentDonor", "Monsoon"); }
        set { EditorPrefs.SetString("OCK.EnvironmentDonor", value); }
    }

    /// <summary>Fast imports stand-ins with smaller, uncompressed textures. Off means full quality.</summary>
    public static bool FastImport
    {
        get { return EditorPrefs.GetBool("OCK.FastImport", true); }
        set { EditorPrefs.SetBool("OCK.FastImport", value); }
    }

    /// <summary>Scene names in build order. Rips call scenes levelN, but references need the real name.</summary>
    public static List<string> BuildSceneNames()
    {
        string path = Path.Combine(GameData, "globalgamemanagers");
        if (!File.Exists(path))
            return new List<string>();
        string text = Encoding.ASCII.GetString(File.ReadAllBytes(path));
        return Regex.Matches(text, @"Assets/[A-Za-z0-9_/ \-]{0,80}?([A-Za-z0-9_\-]+)\.unity").Cast<Match>()
            .Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    /// <summary>The ripped scenes available, as real scene name to file path.</summary>
    public static Dictionary<string, string> RippedScenes()
    {
        var result = new Dictionary<string, string>();
        List<string> names = BuildSceneNames();
        foreach (string file in RipRoots().SelectMany(r => Directory.GetFiles(Path.Combine(r, "Assets", "Scenes"), "*.unity")))
        {
            Match match = Regex.Match(Path.GetFileNameWithoutExtension(file), @"^level(\d+)$");
            int index = match.Success ? int.Parse(match.Groups[1].Value) : -1;
            string name = index >= 0 && index < names.Count ? names[index] : Path.GetFileNameWithoutExtension(file);
            result[name] = file;
        }
        return result;
    }
}
