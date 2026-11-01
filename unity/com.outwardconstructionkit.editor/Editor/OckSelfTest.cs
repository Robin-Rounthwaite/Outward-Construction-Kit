using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Launch the editor with -ockSelfTest and OCK_TEST_OUT set to a folder. Runs the whole authoring path on real
/// props and checks the results, writing PASS or FAIL lines to selftest.txt and exiting with 1 if any fail. Set
/// OCK_TEST_RENDER as well to also render stand-ins in both texture modes, which is slow. Waits for the editor to
/// go idle, because scenes can't be made during startup. It runs in a window because batch mode needs a licence
/// activated for it, and refuses to start otherwise.
/// </summary>
[InitializeOnLoad]
internal static class OckSelfTest
{
    private const string FIRST_RIP = "Monsoon";
    private const string SECOND_RIP = "ChersoneseNewTerrain";
    private const string BARRELS = "mdl_env_propBarrelGroupA";
    private const string STATUE = "mdl_env_propStatueTripleLadies_c";
    private const string ROCK = "mdl_env_natureRockMountainSculptD_c";
    private const string LIGHT = "mdl_env_buildingLightHolderTallA";
    private const string BUSH = "mdl_env_bushAzaleaA";
    private const string VINE = "mdl_env_naturePlantVineHangA";
    private const string SHELL_HORROR = "Enemy ShellHorror";
    private const string HOUND = "Enemy Hound";
    private const int MAX_GROUND_BUNDLE_KB = 10 * 1024;

    private static StringBuilder s_report;
    private static int s_passed;
    private static int s_failed;
    private static string s_output;
    private static bool s_originalMode;
    private static string s_originalRips;
    private static string s_originalGameData;
    private static bool s_originalConfirmed;
    private const double TOUR_SECONDS_PER_WINDOW = 4.0;
    private const int TOUR_TITLE_HEIGHT = 40;
    private static double s_tourFrom;
    private static int s_tourStage = -1;
    private static EditorWindow s_tourWindow;

    static OckSelfTest()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ockSelfTest") >= 0)
            EditorApplication.update += RunWhenIdle;
    }

    private static void RunWhenIdle()
    {
        if (EditorApplication.timeSinceStartup < 20 || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        EditorApplication.update -= RunWhenIdle;

        string output = Environment.GetEnvironmentVariable("OCK_TEST_OUT");
        s_report = new StringBuilder();
        s_originalMode = OckSettings.FastImport;
        s_originalRips = OckSettings.RipFolder;
        s_originalGameData = OckSettings.GameData;
        s_originalConfirmed = OckSettings.PathsConfirmed;
        try
        {
            OckSettings.RipFolder = Environment.GetEnvironmentVariable("OCK_TEST_RIP_FOLDER");
            OckSettings.GameData = Environment.GetEnvironmentVariable("OCK_TEST_GAME_DATA");
            Run(output);
        }
        catch (Exception e)
        {
            Check("the run finished", false, e.ToString());
        }
        s_output = output;
        if (Environment.GetEnvironmentVariable("OCK_TEST_WINDOW_SHOTS") != null)
        {
            s_tourFrom = EditorApplication.timeSinceStartup;
            EditorApplication.update += ShowWindows;
            return;
        }
        Finish();
    }

    private static void Finish()
    {
        OckSettings.FastImport = s_originalMode;
        OckSettings.RipFolder = s_originalRips;
        OckSettings.GameData = s_originalGameData;
        OckSettings.PathsConfirmed = s_originalConfirmed;
        s_report.AppendLine($"{s_passed} passed, {s_failed} failed.");
        File.WriteAllText(Path.Combine(s_output, "selftest.txt"), s_report.ToString());
        EditorApplication.Exit(s_failed == 0 ? 0 : 1);
    }

    // Only windows drawn on screen can be looked at, so this takes a picture of each, a moment apart, for a person to check.
    private static void ShowWindows()
    {
        double elapsed = EditorApplication.timeSinceStartup - s_tourFrom;
        int stage = (int)(elapsed / TOUR_SECONDS_PER_WINDOW);
        if (stage == s_tourStage)
            return;
        if (s_tourWindow != null)
        {
            Color[] pixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(s_tourWindow.position.position, (int)s_tourWindow.position.width, (int)s_tourWindow.position.height + TOUR_TITLE_HEIGHT);
            var picture = new Texture2D((int)s_tourWindow.position.width, (int)s_tourWindow.position.height + TOUR_TITLE_HEIGHT);
            picture.SetPixels(pixels);
            File.WriteAllBytes(Path.Combine(s_output, $"window{s_tourStage}.png"), picture.EncodeToPNG());
            s_tourWindow.Close();
            s_tourWindow = null;
        }
        s_tourStage = stage;

        var tabs = (PropBrowser.Tab[])Enum.GetValues(typeof(PropBrowser.Tab));
        if (stage < tabs.Length)
        {
            PropBrowser.Open();
            PropBrowser browser = EditorWindow.GetWindow<PropBrowser>();
            browser.ShowTab(tabs[stage]);
            s_tourWindow = browser;
        }
        else if (stage == tabs.Length)
        {
            RipWindow.Open();
            s_tourWindow = EditorWindow.GetWindow<RipWindow>();
        }
        else if (stage == tabs.Length + 1)
        {
            FirstStepsWindow.Open();
            s_tourWindow = EditorWindow.GetWindow<FirstStepsWindow>();
        }
        else
        {
            EditorApplication.update -= ShowWindows;
            Finish();
            return;
        }
        s_tourWindow.position = new Rect(60f, 60f, 780f, 700f);
        s_tourWindow.Focus();
        s_tourWindow.Repaint();
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok)
            s_passed++;
        else
            s_failed++;
        s_report.AppendLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : ": " + detail));
    }

    private static void CheckPathDetection()
    {
        List<string> libraries = PathDetector.ParseLibraries("\"0\"\n{\n\t\"path\"\t\t\"C:\\\\Games\\\\Steam\"\n}\n\"1\"\n{\n\t\"path\"\t\t\"T:\\\\SteamLibrary\"\n\t\"apps\" { }\n}");
        Check("Steam's library list can be read", libraries.SequenceEqual(new[] { @"C:\Games\Steam", @"T:\SteamLibrary" }), string.Join(", ", libraries.ToArray()));
        Check("the game is detected in its Steam library", PathDetector.GameDataFolders().Any(f => Path.GetFullPath(f) == Path.GetFullPath(OckSettings.GameData)), string.Join(", ", PathDetector.GameDataFolders().ToArray()));
        Check("a game folder is recognised", PathDetector.IsGameData(OckSettings.GameData) && !PathDetector.IsGameData(Path.GetTempPath()));
        PathsWindow.Open();
        PathsWindow window = EditorWindow.GetWindow<PathsWindow>();
        window.Repaint();
        Check("the paths window opens", window != null);
        window.Close();
        Check("the rip script ships with the package", File.Exists(Path.GetFullPath(RipWindow.SCRIPT)), Path.GetFullPath(RipWindow.SCRIPT));
    }

    private static void Run(string output)
    {
        CheckSetup();
        CheckPathDetection();

        Dictionary<string, string> ripped = OckSettings.RippedScenes();
        RipScene first = RipScene.Load(FIRST_RIP, ripped[FIRST_RIP]);
        RipScene second = RipScene.Load(SECOND_RIP, ripped[SECOND_RIP]);
        CheckRipIndex(first, second);
        CheckImporter(first);

        if (Environment.GetEnvironmentVariable("OCK_TEST_RENDER") != null)
            RenderModes(first, output);

        CheckMissingSpawnIsRefused(output);
        CheckReferencesAndAlterations(first, output);
        CheckGroundWaterAndDoors(first, second, output);
        CheckExampleRegion(output);
    }

    private static void CheckSetup()
    {
        Check("the editor matches the game's Unity version", Application.unityVersion == "2020.3.26f1", Application.unityVersion);
        Check("the project renders in Linear like the game", PlayerSettings.colorSpace == ColorSpace.Linear, PlayerSettings.colorSpace.ToString());
        Dictionary<string, string> ripped = OckSettings.RippedScenes();
        Check("level numbers map to the real scene names", ripped.ContainsKey(FIRST_RIP) && ripped.ContainsKey(SECOND_RIP), string.Join(", ", ripped.Keys.ToArray()));
        Check("a game fingerprint can be read", File.Exists(Path.Combine(OckSettings.GameData, "globalgamemanagers")));
    }

    private static void CheckRipIndex(RipScene first, RipScene second)
    {
        s_report.AppendLine($"Indexed {first.SceneName}: {first.Props.Count} entries. {second.SceneName}: {second.Props.Count} entries, enemies: {string.Join(", ", second.Props.Where(p => p.IsEnemy).Select(p => p.BaseName).ToArray())}");
        Check("the first rip lists its enemy", first.Props.Any(p => p.IsEnemy && p.BaseName == SHELL_HORROR));
        Check("the second rip lists the hyena", second.Props.Any(p => p.IsEnemy && p.BaseName == HOUND));
        Check("a battle enemy is in the Enemies category", first.Props.First(p => p.BaseName == SHELL_HORROR).Category == "Enemies", first.Props.First(p => p.BaseName == SHELL_HORROR).Node.ScenePath);
        Check("a wild enemy is in the Enemies category", second.Props.First(p => p.BaseName == HOUND).Category == "Enemies", second.Props.First(p => p.BaseName == HOUND).Node.ScenePath);
        Check("characters are only ever enemies or others", first.Props.Concat(second.Props).Where(p => p.IsEnemy).All(p => p.Category == "Enemies" || p.Category == "Characters"));
        Check("bandit AI that isn't a character isn't an enemy", !first.Props.Any(p => p.IsEnemy && p.BaseName.StartsWith("AI")));
        Check("water planes aren't offered as props", !first.Props.Concat(second.Props).Any(p => p.BaseName.Contains("Waterplane")));
        Check("each entry names its first instance", first.Props.Where(p => !p.IsEnemy).All(p => p.Node != null && p.Node.ScenePath.Contains("/")));
        Check("props players can use in their own scene are noticed", first.Props.Concat(second.Props).Any(p => !p.IsEnemy && p.Node.SelfAndDescendants().Any(n => n.IsInteractive)));
        Check("plain props aren't marked as interactive", !first.Props.First(p => p.BaseName == BARRELS).Node.SelfAndDescendants().Any(n => n.IsInteractive));
        CheckBrowserTabs(first, second);
    }

    private static void CheckBrowserTabs(RipScene first, RipScene second)
    {
        var both = new[] { first, second };
        s_report.AppendLine($"Items: {string.Join(", ", both.SelectMany(s => s.Props).Where(p => p.IsItem).Select(p => p.BaseName).Distinct().ToArray())}");
        List<RipScene.Prop> enemies = PropBrowser.PropsFor(PropBrowser.Tab.Enemies, both, null, "");
        List<RipScene.Prop> props = PropBrowser.PropsFor(PropBrowser.Tab.Props, both, null, "");
        List<RipScene.Prop> items = PropBrowser.PropsFor(PropBrowser.Tab.Items, both, null, "");
        Check("everything in a rip is in exactly one tab", both.SelectMany(s => s.Props).All(p => (p.IsEnemy ? 1 : 0) + (p.IsItem ? 1 : 0) + (!p.IsEnemy && !p.IsItem ? 1 : 0) == 1));
        Check("the Enemies tab has enemies from both locations", enemies.Any(p => p.BaseName == SHELL_HORROR) && enemies.Any(p => p.BaseName == HOUND));
        Check("the Props tab has props and no enemies", props.Any(p => p.BaseName == BARRELS) && !props.Any(p => p.IsEnemy));
        Check("the Items tab has what the rips hold", items.Count > 0 && items.All(p => p.Node.SelfAndDescendants().Any(n => n.HasMesh && n.HasRenderer)));
        Check("a thing in two locations is listed once", props.Select(p => p.BaseName).Distinct().Count() == props.Count);
        Check("filtering by location only shows that location", PropBrowser.PropsFor(PropBrowser.Tab.Enemies, new[] { second }, null, "").All(p => p.Scene == second));
        Check("searching and categories narrow a tab", PropBrowser.PropsFor(PropBrowser.Tab.Enemies, both, "Enemies", "hound").All(p => p.BaseName.IndexOf("hound", StringComparison.OrdinalIgnoreCase) >= 0 && p.Category == "Enemies"));
        Check("the game's invisible markers aren't offered", !props.Any(p => p.BaseName == "collision" || p.BaseName.StartsWith("Cube") || p.BaseName.EndsWith("Zone")), string.Join(", ", props.Where(p => p.BaseName == "collision" || p.BaseName.StartsWith("Cube") || p.BaseName.EndsWith("Zone")).Select(p => p.Node.ScenePath).ToArray()));
        Check("an item can be imported", PropImporter.Import(items[0].Scene, items[0]) != null, items[0].BaseName);
        Check("rips know their water", first.HasWater && first.HasWaterSource && second.HasWater && second.HasWaterSource);
        Check("items and enemies say which rip they came from", both.SelectMany(s => s.Props).All(p => p.Scene != null));

        List<string> order = RipWindow.ScenesToRip(new[] { "MainMenu_Empty", "Abrassar_Dungeon1", "Monsoon", "Levant", "Berg", "LowMemory_TransitionScene" }, new[] { "Monsoon" });
        Check("Rip All does regions before dungeons, and only what's missing", order.SequenceEqual(new[] { "Levant", "Berg", "Abrassar_Dungeon1" }), string.Join(", ", order.ToArray()));
        Check("the Rip All script ships with the package", File.Exists(Path.GetFullPath(RipWindow.RIP_ALL_SCRIPT)), Path.GetFullPath(RipWindow.RIP_ALL_SCRIPT));

        Check("scenes get the names players know", LocationNames.For("HallowedMarshNewTerrain") == "Hallowed Marsh" && LocationNames.For("AntiqueField_Dungeon3") == "Antique Plateau dungeon 3" && LocationNames.For("Chersonese_Dungeon4_BlueChamber") == "Chersonese dungeon 4, Blue Chamber" && LocationNames.For("EmercarDungeonsBosses") == "Enmerkar Forest boss arenas" && LocationNames.For("SomethingNew") == "SomethingNew");
        Check("every scene has a name that isn't the game's raw one, or is already readable", OckSettings.BuildSceneNames().All(s => LocationNames.For(s) != s ? true : !s.Contains("_") && !s.Contains("NewTerrain")), string.Join(", ", OckSettings.BuildSceneNames().Where(s => LocationNames.For(s) == s).ToArray()));
        Check("testing a region asks for the region and the character's save ID", GameLauncher.TestRequest("r", "abc").SequenceEqual(new[] { "characterid abc", "visit r" }));
        Check("test characters list the most recently used first, and tell apart shared names", TestCharacterOrderIsRight());
        Check("a character's name can be read from a save", ReadsACharacterName());
        Check("Unity's terrain tools can still be switched from code", GroundTools.CanSwitchTerrainTools);
        Check("water is Clean unless chosen otherwise", GroundTools.WaterTypes[0] == "Clean");
        Check("the game is started pointing at the profile's BepInEx", GameLauncher.SteamArguments(@"C:\p\BepInEx").Contains(@"--doorstop-target ""C:\p\BepInEx\core\BepInEx.Preloader.dll"""));

        // Every tab has to draw, and the errors of a window that doesn't are only ever in the log.
        var errors = new List<string>();
        Application.LogCallback listen = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception)
                errors.Add(message);
        };
        Application.logMessageReceived += listen;
        OckSettings.PathsConfirmed = true;
        PropBrowser.Open();
        PropBrowser browser = EditorWindow.GetWindow<PropBrowser>();
        foreach (PropBrowser.Tab tab in Enum.GetValues(typeof(PropBrowser.Tab)))
        {
            browser.ShowTab(tab);
            browser.SendEvent(new Event { type = EventType.Layout });
            browser.SendEvent(new Event { type = EventType.Repaint });
        }
        browser.Close();
        FirstStepsWindow.Open();
        FirstStepsWindow steps = EditorWindow.GetWindow<FirstStepsWindow>();
        steps.SendEvent(new Event { type = EventType.Layout });
        steps.SendEvent(new Event { type = EventType.Repaint });
        steps.Close();
        Application.logMessageReceived -= listen;
        Check("every tab of the browser draws without errors", errors.Count == 0, string.Join(" | ", errors.ToArray()));
    }

    private static void CheckImporter(RipScene rip)
    {
        GameObject prefab = PropImporter.Import(rip, rip.Props.First(p => p.BaseName == BARRELS));
        Check("importing a prop makes a prefab", prefab != null);
        PropRef reference = PropRef.ReadFrom(AssetDatabase.GetAssetPath(prefab));
        Check("the prefab remembers its source", reference != null && reference.DonorScene == FIRST_RIP && !string.IsNullOrEmpty(reference.Path));
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        Check("a static-batched prop gets its own meshes", renderers.Length > 0 && prefab.GetComponentsInChildren<MeshFilter>(true).All(f => f.sharedMesh != null && f.sharedMesh.vertexCount > 0));
        Check("stand-ins use the kit's shader", renderers.SelectMany(r => r.sharedMaterials).All(m => m != null && m.shader.name == PropImporter.STAND_IN_SHADER));
    }

    private static void CheckMissingSpawnIsRefused(string output)
    {
        Scene scene = RegionExporter.CreateStarterScene("OCK_SelfTest_nospawn");
        string path = scene.path;
        UnityEngine.Object.DestroyImmediate(scene.GetRootGameObjects().First(r => r.name == "SpawnPointManager"));
        bool refused = false;
        try
        {
            RegionExporter.Export(Path.Combine(output, "nospawn"));
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }
        Check("export refuses a scene with no spawn point", refused);
        AssetDatabase.DeleteAsset(path + ".region.json");
        AssetDatabase.DeleteAsset(path);
    }

    private static void CheckReferencesAndAlterations(RipScene rip, string output)
    {
        const string id = "test_alpha";
        Scene scene = RegionExporter.CreateStarterScene("OCK_SelfTest_" + id);
        string path = scene.path;
        new RegionInfo { Id = id, DisplayName = "Test Region Alpha" }.SaveFor(path);

        GameObject moved = Place(rip, BARRELS, new Vector3(-15f, 0f, 6f));
        moved.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
        Place(rip, LIGHT, new Vector3(1f, 0f, 6f));
        GameObject scaled = Place(rip, STATUE, new Vector3(-12f, 0f, -12f));
        scaled.transform.localScale *= 0.75f;
        GameObject stretched = Place(rip, STATUE, new Vector3(-20f, 0f, -12f));
        stretched.transform.localScale = Vector3.Scale(stretched.transform.localScale, new Vector3(1f, 2f, 1f));
        Place(rip, SHELL_HORROR, new Vector3(8f, 0f, -8f));
        // The editor's own tools record changes as they're made, but changes from code have to ask.
        PrefabUtility.RecordPrefabInstancePropertyModifications(moved.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(scaled.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(stretched.transform);
        Check("moving and rotating doesn't count as altering", !PropRef.IsAltered(moved));
        Check("scaling evenly doesn't count as altering", !PropRef.IsAltered(scaled));
        Check("stretching counts as altering", PropRef.IsAltered(stretched));

        EditorSceneManager.SaveScene(scene);
        string folder = Path.Combine(output, "OCK-Region-" + id);
        string summary = RegionExporter.Export(folder);
        s_report.AppendLine(summary);
        string[] manifest = File.ReadAllLines(Path.Combine(folder, id + ".refs.txt"));

        Check("the region's identity is in the manifest", manifest.Contains("region | " + id + " | Test Region Alpha"));
        Check("a moved prop is a reference", manifest.Any(l => l.StartsWith("prop ") && l.Contains(BARRELS) && l.Contains("-15,0,6")));
        Check("a rotated prop records its turn", manifest.Any(l => l.StartsWith("prop ") && l.Contains(BARRELS) && l.Contains("0,30,0")));
        Check("an evenly scaled prop is a reference that records its scale", manifest.Any(l => l.StartsWith("prop ") && l.Contains(STATUE) && l.Contains("-12,0,-12") && l.EndsWith("| 0.75")));
        Check("a stretched prop isn't a reference", !manifest.Any(l => l.StartsWith("prop ") && l.Contains("-20,0,-12")));
        Check("the enemy is a reference", manifest.Any(l => l.StartsWith("enemy ") && l.Contains("8,0,-8")));
        Check("references each carry a check", manifest.Where(l => l.StartsWith("prop ")).All(l => l.Split('|').Length >= 6 && l.Split('|')[5].Contains(":")));
        Check("the summary counts one embedded prop", summary.Contains("1 altered props embedded"), summary);
        Check("the stretched prop's art is in the bundle",RegionExporter.LastDependencies.Any(d => d.Contains("mdl_env_propStatueTripleLadies")));
        Check("referenced props' art stays out", !RegionExporter.LastDependencies.Any(d => d.Contains("BarrelGroup") || d.Contains("LightHolder")), string.Join(", ", RegionExporter.LastDependencies.Where(d => d.Contains("BarrelGroup") || d.Contains("LightHolder")).ToArray()));
        Check("the enemy marker stays out", !RegionExporter.LastDependencies.Any(d => d.Contains("ShellHorror") || d.Contains("mdl_cha_")));

        CheckPackaging(RegionInfo.For(path), output);

        AssetDatabase.DeleteAsset(path + ".region.json");
        AssetDatabase.DeleteAsset(path);
    }

    private static void CheckPackaging(RegionInfo info, string output)
    {
        Check("packaging asks for a mod name and description", RegionPackager.Problems(new RegionInfo { PackageName = "has spaces", PackageVersion = "1", DoorScene = "Monsoon" }).Count == 3);
        Check("packaging asks for an entrance", RegionPackager.Problems(new RegionInfo { PackageName = "Good", PackageVersion = "1.0.0", Description = "Fine." }).Count == 1);
        Check("packaging refuses a long description", RegionPackager.Problems(new RegionInfo { PackageName = "Good", PackageVersion = "1.0.0", Description = new string('x', 251), DoorScene = "Monsoon" }).Count == 1);

        info.PackageName = "Test_Region_Alpha";
        info.PackageVersion = "1.2.3";
        info.Description = "A \"quoted\" test.";
        info.DoorScene = "CierzoNewTerrain";
        info.DoorModelScene = FIRST_RIP;
        info.DoorModelPath = "Some/Prop";
        info.DoorModelRotation = new Vector3(0f, 90f, 0f);
        info.DoorModelScale = 1.5f;
        info.SaveFor(SceneManager.GetActiveScene().path);
        Check("a complete region is ready to package", RegionPackager.Problems(info).Count == 0, string.Join(" ", RegionPackager.Problems(info).ToArray()));

        string summary;
        string zip = RegionPackager.Package(info, Path.Combine(output, "upload"), out summary);
        Check("the zip is named as Thunderstore expects", Path.GetFileName(zip) == "Test_Region_Alpha-1.2.3.zip", zip);
        using (var archive = new System.IO.Compression.ZipArchive(File.OpenRead(zip)))
        {
            using (var reader = new StreamReader(archive.GetEntry(info.Id + ".refs.txt").Open()))
                Check("a chosen door model is listed", reader.ReadToEnd().Contains("doormodel | " + FIRST_RIP + " | Some/Prop | 0,90,0 | 1.5"));
            string[] names = archive.Entries.Select(e => e.FullName).ToArray();
            Check("the zip holds the region and Thunderstore's files, at its top level", new[] { "manifest.json", "README.md", "icon.png", info.Id, info.Id + ".refs.txt" }.All(names.Contains) && names.Length == 5, string.Join(", ", names));
            using (var reader = new StreamReader(archive.GetEntry("manifest.json").Open()))
            {
                string manifest = reader.ReadToEnd();
                Check("its manifest depends on the kit", manifest.Contains("\"Cat_and_Community-Outward_Construction_Kit-" + RegionPackager.KitVersion + "\""), manifest);
                Check("its manifest escapes quotes", manifest.Contains("A \\\"quoted\\\" test."), manifest);
            }
        }
    }

    // A hill, a dirt path, ponds and a return door, the way a modder would make them but without the clicks.
    private static void CheckGroundWaterAndDoors(RipScene first, RipScene second, string output)
    {
        const string id = "test_gamma";
        Scene scene = RegionExporter.CreateStarterScene("OCK_SelfTest_" + id);
        string path = scene.path;
        new RegionInfo { Id = id, DisplayName = "Test Region Gamma" }.SaveFor(path);

        Terrain ground = GroundTools.AddGround();
        TerrainData data = ground.terrainData;
        string dataPath = AssetDatabase.GetAssetPath(data);
        Check("adding ground removes the starter room", !scene.GetRootGameObjects().Any(r => r.name == "Floor" || r.name.StartsWith("Wall_")));
        Check("ground starts level with the spawn point", Mathf.Abs(ground.SampleHeight(Vector3.zero) + ground.transform.position.y) < 0.01f, ground.SampleHeight(Vector3.zero).ToString());
        Check("adding ground twice keeps one terrain", GroundTools.AddGround() == ground);

        ExampleRegion.SculptAndPaint(ground, new Vector3(0f, 0f, 45f), new[] { new Vector3(-35f, 0f, 0f), new Vector3(35f, 0f, 0f) });

        GameObject water = GroundTools.AddWater(FIRST_RIP);
        water.transform.position = new Vector3(-35f, -1.5f, 0f);
        GameObject river = GroundTools.AddWater(SECOND_RIP);
        river.transform.position = new Vector3(35f, -1.5f, 0f);

        Place(first, STATUE, new Vector3(0f, 12f, 45f));
        Place(second, second.Props.First(p => !p.IsEnemy && p.Batched && p.BaseName.Contains("Rock")).BaseName, new Vector3(15f, 0f, -15f));
        GameObject hound = Place(second, HOUND, new Vector3(0f, 0f, 20f));
        Renderer[] houndParts = hound.GetComponentsInChildren<Renderer>(true);
        Check("an enemy shows its real model", houndParts.Length > 0 && houndParts.All(r => r.name != "Marker"));
        if (houndParts.Length > 0)
        {
            Bounds size = houndParts[0].bounds;
            foreach (Renderer part in houndParts)
                size.Encapsulate(part.bounds);
            Check("an enemy's model is about the right size", size.size.y > 0.3f && size.size.y < 4f && size.size.z < 6f, size.size.ToString());
            Check("an enemy's model stands by its position", Vector3.Distance(size.center, hound.transform.position) < 3f, size.center + " from " + hound.transform.position);
        }

        GameObject door = DoorTools.Add();
        door.transform.position = new Vector3(-1.8f, 0f, 1.8f); // about as far as the automatic door stands
        door.transform.rotation = Quaternion.Euler(0f, 135f, 0f); // facing the spawn at the middle

        Render(Path.Combine(output, "ground.png"), new Vector3(0f, 22f, -48f), new Vector3(-8f, 2f, 15f));

        EditorSceneManager.SaveScene(scene);
        string folder = Path.Combine(output, "OCK-Region-" + id);
        string summary = RegionExporter.Export(folder);
        s_report.AppendLine(summary);
        string bundle = Path.Combine(folder, id);
        string[] manifest = File.ReadAllLines(bundle + ".refs.txt");

        Check("all four ground layers are references", manifest.Count(l => l.StartsWith("ground ")) == 4);
        Check("ground layers name their textures", manifest.Any(l => l == "ground | Monsoon | Ground_Grass | tex_env_masterGroundGrassFull | tex_env_masterGroundGrassFull_n"));
        Check("both waters are listed with their donors", manifest.Contains("water | Monsoon | -35,-1.5,0 | 40,1,40 | 0 | Clean") && manifest.Contains("water | ChersoneseNewTerrain | 35,-1.5,0 | 40,1,40 | 0 | Clean"));
        Check("the return door is listed", manifest.Contains("returndoor | -1.8,0,1.8 | 135"));
        Check("the second rip's enemy has its real path", manifest.Any(l => l.StartsWith("enemy | ChersoneseNewTerrain | AISquadManagerStructure/")));

        string[] art = RegionExporter.LastDependencies.Where(d => d.StartsWith(PropImporter.LIBRARY)).ToArray();
        Check("no vanilla art reaches a reference-only bundle", art.Length == 0, string.Join(", ", art));
        Check("no texture files reach it either", !RegionExporter.LastDependencies.Any(d => d.EndsWith(".png") || d.EndsWith(".tga")), string.Join(", ", RegionExporter.LastDependencies.Where(d => d.EndsWith(".png") || d.EndsWith(".tga")).ToArray()));
        Check("the bundle stays small", new FileInfo(bundle).Length / 1024 < MAX_GROUND_BUNDLE_KB, new FileInfo(bundle).Length / 1024 + " KB");

        Bounds walkable = RegionExporter.LastNavMeshBounds;
        Check("enemies can walk the whole terrain", walkable.size.x > 190f && walkable.size.z > 190f, walkable.ToString());
        Check("enemies can walk up the hill", walkable.max.y > 10f, walkable.ToString());

        Scene reopened = SceneManager.GetActiveScene();
        Check("export leaves the modder's scene open", reopened.path == path, reopened.path);
        Terrain original = reopened.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Terrain>(true)).FirstOrDefault();
        Check("export leaves the modder's textures alone", original != null && original.terrainData.terrainLayers.All(l => l.diffuseTexture != null));
        Check("export leaves the water markers", reopened.GetRootGameObjects().Count(r => r.name.StartsWith("OCK Water ")) == 2);
        Check("export leaves the door marker", reopened.GetRootGameObjects().Any(r => r.name == "OCK Return Door"));
        Check("export cleans up after itself", !AssetDatabase.IsValidFolder("Assets/OCK/_Export"));

        AssetDatabase.DeleteAsset(path + ".region.json");
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.DeleteAsset(dataPath);
    }

    private static void CheckPlacementWarnings()
    {
        Scene room = RegionExporter.CreateStarterScene("OCK_SelfTest_placement");
        string roomPath = room.path;
        Check("a starter room has nothing to warn about", RegionExporter.PlacementWarnings(room).Count == 0, string.Join(" ", RegionExporter.PlacementWarnings(room).ToArray()));
        GameObject door = DoorTools.Add();
        door.transform.position = new Vector3(5f, 4f, 5f);
        Check("a floating door gets a warning", RegionExporter.PlacementWarnings(room).Count == 1, string.Join(" ", RegionExporter.PlacementWarnings(room).ToArray()));
        door.transform.position = new Vector3(5f, 0f, 5f);
        Check("a door on the ground doesn't", RegionExporter.PlacementWarnings(room).Count == 0, string.Join(" ", RegionExporter.PlacementWarnings(room).ToArray()));
        door.transform.position = new Vector3(500f, 0f, 500f);
        Check("a door off the map gets a warning", RegionExporter.PlacementWarnings(room).Count == 1);
        EditorSceneManager.SaveScene(room);
        AssetDatabase.DeleteAsset(roomPath);
    }

    private static void CheckExampleRegion(string output)
    {
        CheckPlacementWarnings();
        Scene scene = ExampleRegion.Create();
        string path = scene.path;
        string dataPath = AssetDatabase.GetAssetPath(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Terrain>(true)).First().terrainData);
        string folder = Path.Combine(output, "OCK-Region-" + ExampleRegion.ID);
        s_report.AppendLine(RegionExporter.Export(folder));
        string[] manifest = File.ReadAllLines(Path.Combine(folder, ExampleRegion.ID + ".refs.txt"));
        Check("the example lists its props as references", manifest.Count(l => l.StartsWith("prop ")) == 3, string.Join(" / ", manifest));
        Check("the example's scaled barrels stay a reference", manifest.Any(l => l.StartsWith("prop ") && l.EndsWith("| 1.5")));
        Check("the example has an enemy, water, ground and a door", manifest.Any(l => l.StartsWith("enemy ")) && manifest.Any(l => l.StartsWith("water ")) && manifest.Any(l => l.StartsWith("ground ")) && manifest.Any(l => l.StartsWith("returndoor ")));
        Check("a region with no door model leaves it to the kit", !manifest.Any(l => l.StartsWith("doormodel ")));
        Check("water lists the type it was given", manifest.Any(l => l.StartsWith("water ") && l.EndsWith("| Salt")), string.Join(" / ", manifest));
        Check("an added ground texture is a reference", manifest.Any(l => l.Contains("| Ground_masterGroundPebble | tex_env_masterGroundPebble |")), string.Join(" / ", manifest));
        Check("a texture from another scene names that scene", manifest.Any(l => l.StartsWith("ground | " + SECOND_RIP + " | Ground_masterMuddyForestFloor_d | tex_env_masterMuddyForestFloor_d |")), string.Join(" / ", manifest));
        Check("a scene's rip offers its ground textures", GroundTools.GroundTexturesIn(OckSettings.RipRoots().First()).Contains("tex_env_masterGroundEarth"));
        Check("normal maps aren't offered as ground textures", !GroundTools.GroundTexturesIn(OckSettings.RipRoots().First()).Any(n => n.EndsWith("_n")));
        Check("the example ships no vanilla art",!RegionExporter.LastDependencies.Any(d => d.StartsWith(PropImporter.LIBRARY)), string.Join(", ", RegionExporter.LastDependencies.Where(d => d.StartsWith(PropImporter.LIBRARY)).ToArray()));

        AssetDatabase.DeleteAsset(path + ".region.json");
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.DeleteAsset(dataPath);
    }

    private static void RenderModes(RipScene rip, string output)
    {
        Scene scene = RegionExporter.CreateStarterScene("OCK_SelfTest_render");
        string path = scene.path;
        float x = -12f;
        foreach (string name in new[] { BARRELS, STATUE, ROCK, LIGHT, BUSH, VINE })
        {
            Place(rip, name, new Vector3(x, 0f, 5f));
            x += 5f;
        }

        foreach (bool fast in new[] { true, false })
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            OckSettings.FastImport = fast;
            PropImporter.ApplyTextureModeToLibrary();
            PropImporter.RefreshLibraryMaterials();
            s_report.AppendLine($"Switched to {(fast ? "Fast" : "Detailed")} in {timer.ElapsedMilliseconds} ms.");
            Render(Path.Combine(output, fast ? "mode_fast.png" : "mode_detailed.png"));
        }
        OckSettings.FastImport = true;
        PropImporter.ApplyTextureModeToLibrary();
        PropImporter.RefreshLibraryMaterials();

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.DeleteAsset(path);
    }

    private static GameObject Place(RipScene rip, string name, Vector3 position)
    {
        GameObject prefab = PropImporter.Import(rip, rip.Props.First(p => p.BaseName == name));
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = position;
        return instance;
    }

    private static bool TestCharacterOrderIsRight()
    {
        var old = new TestCharacters.Character { Id = "old", Name = "Sam", LastSaved = new System.DateTime(2020, 1, 1) };
        var fresh = new TestCharacters.Character { Id = "fresh", Name = "Sam", LastSaved = new System.DateTime(2024, 1, 1) };
        var tested = new TestCharacters.Character { Id = "tested", Name = "Jim", LastSaved = new System.DateTime(2021, 1, 1) };
        List<TestCharacters.Character> ordered = TestCharacters.Order(new[] { old, fresh, tested }, new List<string> { "tested" });
        return ordered.Select(c => c.Id).SequenceEqual(new[] { "tested", "fresh", "old" }) && tested.Label == "Jim" && old.Label != fresh.Label;
    }

    private static bool ReadsACharacterName()
    {
        string file = Path.GetTempFileName();
        try
        {
            using (FileStream stream = File.Create(file))
            using (var zipped = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Compress))
            using (var writer = new StreamWriter(zipped))
                writer.Write("<?xml version=\"1.0\"?><Character><Name>wrong</Name><PSave><Level>1</Level><Name>Jim &amp; Co</Name></PSave></Character>");
            return TestCharacters.ReadName(file) == "Jim & Co";
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void Render(string path)
    {
        Render(path, new Vector3(0f, 5f, -9f), new Vector3(0f, 1.5f, 5f));
    }

    private static void Render(string path, Vector3 from, Vector3 to)
    {
        var cameraObject = new GameObject("OCK_TestCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = from;
        camera.transform.LookAt(to);
        camera.backgroundColor = new Color(0.45f, 0.6f, 0.8f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.farClipPlane = 200f;

        var target = new RenderTexture(1280, 720, 24);
        camera.targetTexture = target;
        camera.Render();

        RenderTexture.active = target;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        File.WriteAllBytes(path, image.EncodeToPNG());

        RenderTexture.active = null;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(cameraObject);
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(image);
    }
}
