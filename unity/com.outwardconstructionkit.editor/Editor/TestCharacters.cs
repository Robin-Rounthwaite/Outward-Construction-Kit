using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using UnityEditor;

/// <summary>
/// The characters in the game's saves, for choosing who a test plays as. A character's save is a folder of dated
/// copies, and the newest copy's Char0.defedc is gzipped XML holding the character's name. Only ever reads them.
/// </summary>
internal static class TestCharacters
{
    private const string RECENT_PREF = "OCK.TestCharacterRecent";
    private const string SAVES_FOLDER = "SaveGames";
    private const string SAVE_PREFIX = "Save_";
    private const string CHARACTER_FILE = "Char0.defedc";
    private const char SEPARATOR = '|';
    private const double SECONDS_BETWEEN_READS = 30.0;

    public class Character
    {
        public string Id;
        public string Name;
        public string Label;
        public DateTime LastSaved;
    }

    private static List<Character> s_characters;
    private static double s_readAt;

    /// <summary>Everyone with a save, most recently tested first, then most recently played.</summary>
    public static List<Character> All
    {
        get
        {
            if (s_characters == null || EditorApplication.timeSinceStartup - s_readAt > SECONDS_BETWEEN_READS)
            {
                s_characters = Order(Read(Path.GetDirectoryName(OckSettings.GameData.TrimEnd('\\', '/'))), Recent());
                s_readAt = EditorApplication.timeSinceStartup;
            }
            return s_characters;
        }
    }

    /// <summary>Puts this character at the top of the list.</summary>
    public static void MarkUsed(string id)
    {
        EditorPrefs.SetString(RECENT_PREF, string.Join(SEPARATOR.ToString(), new[] { id }.Concat(Recent().Where(r => r != id)).ToArray()));
        s_characters = null;
    }

    private static List<string> Recent()
    {
        return EditorPrefs.GetString(RECENT_PREF, "").Split(new[] { SEPARATOR }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    internal static List<Character> Order(IEnumerable<Character> characters, List<string> recent)
    {
        List<Character> ordered = characters.OrderBy(c => recent.Contains(c.Id) ? recent.IndexOf(c.Id) : int.MaxValue).ThenByDescending(c => c.LastSaved).ToList();
        foreach (Character character in ordered)
            character.Label = ordered.Count(c => c.Name == character.Name) > 1 ? $"{character.Name} (played {character.LastSaved:yyyy-MM-dd})" : character.Name;
        return ordered;
    }

    private static List<Character> Read(string gameFolder)
    {
        var found = new List<Character>();
        string saves = string.IsNullOrEmpty(gameFolder) ? null : Path.Combine(gameFolder, SAVES_FOLDER);
        if (saves == null || !Directory.Exists(saves))
            return found;
        foreach (string player in Directory.GetDirectories(saves))
            foreach (string save in Directory.GetDirectories(player, SAVE_PREFIX + "*"))
                try
                {
                    string newest = Directory.GetDirectories(save).OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault(d => File.Exists(Path.Combine(d, CHARACTER_FILE)));
                    if (newest == null)
                        continue;
                    string file = Path.Combine(newest, CHARACTER_FILE);
                    found.Add(new Character { Id = Path.GetFileName(save).Substring(SAVE_PREFIX.Length), Name = ReadName(file), LastSaved = File.GetLastWriteTime(file) });
                }
                catch (Exception e) when (e is IOException || e is InvalidDataException || e is XmlException || e is UnauthorizedAccessException)
                {
                    UnityEngine.Debug.LogWarning($"[OCK] Couldn't read the character in {save}: {e.Message}");
                }
        return found;
    }

    internal static string ReadName(string characterFile)
    {
        using (FileStream file = File.Open(characterFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var unzipped = new GZipStream(file, CompressionMode.Decompress))
        using (XmlReader reader = XmlReader.Create(unzipped))
        {
            if (!reader.ReadToFollowing("PSave") || !reader.ReadToDescendant("Name"))
                throw new InvalidDataException("It has no character name.");
            return reader.ReadElementContentAsString();
        }
    }
}
