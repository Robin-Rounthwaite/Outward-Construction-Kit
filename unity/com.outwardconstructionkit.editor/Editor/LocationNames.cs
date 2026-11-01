using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>The names players know a scene by, since many of the game's own scene names aren't that.</summary>
internal static class LocationNames
{
    private static readonly Dictionary<string, string> s_names = new Dictionary<string, string>
    {
        { "MainMenu_Empty", "Main menu" },
        { "CierzoTutorial", "Cierzo (tutorial)" },
        { "Tutorial_Standalone", "Tutorial" },
        { "CierzoNewTerrain", "Cierzo" },
        { "CierzoDestroyed", "Cierzo (destroyed)" },
        { "ChersoneseNewTerrain", "Chersonese" },
        { "HallowedMarshNewTerrain", "Hallowed Marsh" },
        { "AntiqueField", "Antique Plateau" },
        { "Emercar", "Enmerkar Forest" },
        { "NewSirocco", "New Sirocco" },
        { "DreamWorld", "Dream world" },
        { "LowMemory_TransitionScene", "Loading transition" },
    };

    private static readonly Dictionary<string, string> s_dungeonRegions = new Dictionary<string, string>
    {
        { "Chersonese", "Chersonese" },
        { "Hallowed", "Hallowed Marsh" },
        { "Abrassar", "Abrassar" },
        { "AntiqueField", "Antique Plateau" },
        { "Emercar", "Enmerkar Forest" },
        { "Caldera", "Caldera" },
    };

    private static readonly Regex s_dungeon = new Regex(@"^([A-Za-z]+)_Dungeon(\d+)(?:_(\w+))?$");
    private static readonly Regex s_group = new Regex(@"^([A-Za-z]+)Dungeons(Small|Bosses)$");
    private static readonly Regex s_words = new Regex(@"(?<=[a-z])(?=[A-Z])");

    /// <summary>A scene's name for people, or the scene's own name if there's nothing better.</summary>
    public static string For(string scene)
    {
        string name;
        if (s_names.TryGetValue(scene, out name))
            return name;

        Match dungeon = s_dungeon.Match(scene);
        if (dungeon.Success && s_dungeonRegions.TryGetValue(dungeon.Groups[1].Value, out name))
        {
            string part = dungeon.Groups[3].Success ? ", " + s_words.Replace(dungeon.Groups[3].Value, " ") : "";
            return $"{name} dungeon {dungeon.Groups[2].Value}{part}";
        }

        Match group = s_group.Match(scene);
        if (group.Success && s_dungeonRegions.TryGetValue(group.Groups[1].Value, out name))
            return name + (group.Groups[2].Value == "Small" ? " small dungeons" : " boss arenas");

        return scene;
    }
}
