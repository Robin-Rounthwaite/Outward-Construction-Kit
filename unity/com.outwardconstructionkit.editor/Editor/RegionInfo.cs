using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public const string DEFEAT_RETURN_HERE = "here"; // as the plugin's DefeatSetup

    // What a defeat does here. All left alone means where they came from, with no penalty.
    public string DefeatReturn;
    public int DefeatSpawn;
    public int DefeatSilverPercent;
    public int DefeatItemPercent;
    public int DefeatVitalsPercent;
    public string DefeatMessage;

    /// <summary>The manifest line for this, or null if nothing's been changed.</summary>
    public string DefeatLine()
    {
        if (string.IsNullOrEmpty(DefeatReturn) && DefeatSilverPercent == 0 && DefeatItemPercent == 0 && DefeatVitalsPercent == 0 && string.IsNullOrEmpty(DefeatMessage))
            return null;
        string message = (DefeatMessage ?? "").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
        return $"defeat | {DefeatReturn} | {DefeatSpawn} | {DefeatSilverPercent} | {DefeatItemPercent} | {DefeatVitalsPercent} | {message}";
    }

    /// <summary>What a modder has chosen a chest to hold, kept here so nothing extra goes in the scene.</summary>
    [Serializable]
    public class ChestContent
    {
        public string ObjectId;
        public string Contents;
    }

    public List<ChestContent> ChestContents = new List<ChestContent>();

    private static string IdOf(UnityEngine.GameObject chest)
    {
        return GlobalObjectId.GetGlobalObjectIdSlow(chest).ToString();
    }

    /// <summary>Empty means it holds the game's own loot.</summary>
    public string ContentsOf(UnityEngine.GameObject chest)
    {
        string id = IdOf(chest);
        ChestContent found = ChestContents.Find(c => c.ObjectId == id);
        return found != null ? found.Contents : "";
    }

    public void SetContents(UnityEngine.GameObject chest, string contents)
    {
        string id = IdOf(chest);
        ChestContents.RemoveAll(c => c.ObjectId == id);
        if (!string.IsNullOrEmpty(contents))
            ChestContents.Add(new ChestContent { ObjectId = id, Contents = contents });
    }

    [Serializable]
    public class DialogueChoice
    {
        public string Text;
        public string Next;
    }

    [Serializable]
    public class DialogueLine
    {
        public string Name;
        public string Text;
        public List<DialogueChoice> Choices = new List<DialogueChoice>();
    }

    [Serializable]
    public class CharacterDialogue
    {
        public string ObjectId;
        public string Name;
        public List<DialogueLine> Lines = new List<DialogueLine>();

        public string NextLineName()
        {
            for (int number = Lines.Count + 1; ; number++)
                if (Lines.All(l => l.Name != "Line " + number))
                    return "Line " + number;
        }

        /// <summary>The manifest's lines for this conversation, named so the plugin can find them. Anything else leads to the end.</summary>
        public IEnumerable<string> ManifestLines(string dialogueName)
        {
            foreach (DialogueLine line in Lines)
            {
                yield return "say | " + dialogueName + " | " + Clean(line.Name) + " | " + Clean(line.Text);
                foreach (DialogueChoice choice in line.Choices)
                    yield return "choice | " + dialogueName + " | " + Clean(line.Name) + " | " + Clean(choice.Text) + " | " + Clean(choice.Next);
            }
        }

        // Each part of a manifest line is one line of text, between bars.
        private static string Clean(string text)
        {
            return (text ?? "").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
        }
    }

    public List<CharacterDialogue> Dialogues = new List<CharacterDialogue>();

    public CharacterDialogue DialogueOf(UnityEngine.GameObject character)
    {
        string id = IdOf(character);
        return Dialogues.Find(d => d.ObjectId == id);
    }

    public void AddDialogue(UnityEngine.GameObject character)
    {
        var dialogue = new CharacterDialogue { ObjectId = IdOf(character), Name = character.name };
        dialogue.Lines.Add(new DialogueLine { Name = "Line 1", Text = "" });
        Dialogues.Add(dialogue);
    }

    public void RemoveDialogue(UnityEngine.GameObject character)
    {
        string id = IdOf(character);
        Dialogues.RemoveAll(d => d.ObjectId == id);
    }

    // Items and enemies a modder asked not to come back at a region reset.
    public List<string> GoneForGood = new List<string>();

    public bool StaysGone(UnityEngine.GameObject thing)
    {
        return GoneForGood.Contains(IdOf(thing));
    }

    public void SetStaysGone(UnityEngine.GameObject thing, bool staysGone)
    {
        string id = IdOf(thing);
        GoneForGood.Remove(id);
        if (staysGone)
            GoneForGood.Add(id);
    }

    /// <summary>Whether this reads as, say, 5010100:2,silver:50, as the plugin expects.</summary>
    public static bool ContentsAreValid(string contents)
    {
        return string.IsNullOrEmpty(contents) || Regex.IsMatch(contents, @"^\s*(\d+|silver)\s*:\s*[1-9]\d*\s*(,\s*(\d+|silver)\s*:\s*[1-9]\d*\s*)*$");
    }

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
