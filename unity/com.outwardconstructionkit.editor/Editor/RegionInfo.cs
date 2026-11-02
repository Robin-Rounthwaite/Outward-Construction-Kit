using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using Vector3 = UnityEngine.Vector3;

/// <summary>
/// A region's identity, kept in a small file beside its scene. Not in the scene's import settings,
/// because saving those reimports the open scene, which makes Unity think it changed on disk.
/// </summary>
[Serializable]
internal class RegionInfo
{
    private const string SUFFIX = ".region.json";

    public string Id;
    public string DisplayName;

    // Where the entrance goes, which ships with the mod. Players can still move it for themselves.
    public string DoorScene;
    public string DoorPosition;
    public float DoorRotationY;

    // What its doors look like, from a stand-in chosen in the scene. Empty uses the kit's default door.
    public string DoorModelScene;
    public string DoorModelPath;
    public Vector3 DoorModelRotation;
    public float DoorModelScale = 1f;

    // For sharing it, which Package Region uses. None of it affects the game.
    public string PackageTeam;
    public string PackageName;
    public string PackageVersion = "1.0.0";
    public string Description;
    public string WebsiteUrl;
    public string About;
    public string IconPath;

    /// <summary>The name its scene gets in the bundle. Scenes load by name, and saves are kept by it.</summary>
    public string BuiltSceneName
    {
        get { return "OCK_" + Id; }
    }

    public static RegionInfo For(string scenePath)
    {
        string file = scenePath + SUFFIX;
        if (File.Exists(file))
            return UnityEngine.JsonUtility.FromJson<RegionInfo>(File.ReadAllText(file));

        // Scenes from before this file existed.
        AssetImporter importer = AssetImporter.GetAtPath(scenePath);
        if (importer != null && importer.userData.StartsWith("{"))
            return UnityEngine.JsonUtility.FromJson<RegionInfo>(importer.userData);

        string name = Path.GetFileNameWithoutExtension(scenePath);
        return new RegionInfo { Id = CleanId(name), DisplayName = name };
    }

    public void SaveFor(string scenePath)
    {
        Id = CleanId(Id);
        string file = scenePath + SUFFIX;
        File.WriteAllText(file, UnityEngine.JsonUtility.ToJson(this, true));
        AssetDatabase.ImportAsset(file);
    }

    /// <summary>
    /// Copies the door placed in-game with the hotkeys. Vanilla scenes can't be opened in the editor,
    /// so that's how an entrance gets chosen. Returns false if none has been placed for this ID.
    /// </summary>
    public bool TryCopyDoorFromGame()
    {
        string config = Path.Combine(OckSettings.PluginsFolder, "..", "config", RegionExporter.PLUGIN_CONFIG);
        if (!File.Exists(config))
            return false;

        return TryReadDoor(File.ReadAllLines(config));
    }

    /// <summary>Takes this region's door from the lines of the plugin's config, if one was placed.</summary>
    public bool TryReadDoor(string[] configLines)
    {
        bool inSection = false;
        string scene = null, position = null, rotation = null;
        foreach (string line in configLines)
        {
            if (line.StartsWith("["))
                inSection = line.Trim() == "[Region " + Id + "]";
            else if (inSection)
            {
                Match entry = Regex.Match(line, @"^(DoorScene|DoorPosition|DoorRotationY) = (.*)$");
                if (entry.Success && entry.Groups[1].Value == "DoorScene") scene = entry.Groups[2].Value.Trim();
                if (entry.Success && entry.Groups[1].Value == "DoorPosition") position = entry.Groups[2].Value.Trim();
                if (entry.Success && entry.Groups[1].Value == "DoorRotationY") rotation = entry.Groups[2].Value.Trim();
            }
        }
        if (string.IsNullOrEmpty(scene) || string.IsNullOrEmpty(position))
            return false;

        DoorScene = scene;
        DoorPosition = position;
        float rotationY;
        float.TryParse(rotation, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rotationY);
        DoorRotationY = rotationY;
        return true;
    }

    // Used for file names, config sections and the scene name, and must never change once a region is shared.
    public static string CleanId(string text)
    {
        string id = Regex.Replace((text ?? "").ToLowerInvariant(), "[^a-z0-9_]+", "_").Trim('_');
        return id.Length > 0 ? id : "region";
    }
}
