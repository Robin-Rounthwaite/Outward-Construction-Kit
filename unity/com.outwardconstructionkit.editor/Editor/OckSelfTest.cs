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
        bool originalMode = OckSettings.FastImport;
        try
        {
            Run(output);
        }
        catch (Exception e)
        {
            Check("the run finished", false, e.ToString());
        }
        OckSettings.FastImport = originalMode;
        s_report.AppendLine($"{s_passed} passed, {s_failed} failed.");
        File.WriteAllText(Path.Combine(output, "selftest.txt"), s_report.ToString());
        EditorApplication.Exit(s_failed == 0 ? 0 : 1);
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok)
            s_passed++;
        else
            s_failed++;
        s_report.AppendLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : ": " + detail));
    }

    private static void Run(string output)
    {
        CheckSetup();

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
        Place(rip, SHELL_HORROR, new Vector3(8f, 0f, -8f));
        // The editor's own tools record changes as they're made, but changes from code have to ask.
        PrefabUtility.RecordPrefabInstancePropertyModifications(moved.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(scaled.transform);
        Check("moving and rotating doesn't count as altering", !PropRef.IsAltered(moved));
        Check("scaling counts as altering", PropRef.IsAltered(scaled));

        EditorSceneManager.SaveScene(scene);
        string folder = Path.Combine(output, "OCK-Region-" + id);
        string summary = RegionExporter.Export(folder);
        s_report.AppendLine(summary);
        string[] manifest = File.ReadAllLines(Path.Combine(folder, id + ".refs.txt"));

        Check("the region's identity is in the manifest", manifest.Contains("region | " + id + " | Test Region Alpha"));
        Check("a moved prop is a reference", manifest.Any(l => l.StartsWith("prop ") && l.Contains(BARRELS) && l.Contains("-15,0,6")));
        Check("a rotated prop records its turn", manifest.Any(l => l.StartsWith("prop ") && l.Contains(BARRELS) && l.Contains("0,30,0")));
        Check("a scaled prop isn't a reference", !manifest.Any(l => l.StartsWith("prop ") && l.Contains(STATUE)));
        Check("the enemy is a reference", manifest.Any(l => l.StartsWith("enemy ") && l.Contains("8,0,-8")));
        Check("references each carry a check", manifest.Where(l => l.StartsWith("prop ")).All(l => l.Split('|').Length == 6 && l.Split('|')[5].Contains(":")));
        Check("the summary counts one embedded prop", summary.Contains("1 altered props embedded"), summary);
        Check("the scaled prop's art is in the bundle", RegionExporter.LastDependencies.Any(d => d.Contains("mdl_env_propStatueTripleLadies")));
        Check("referenced props' art stays out", !RegionExporter.LastDependencies.Any(d => d.Contains("BarrelGroup") || d.Contains("LightHolder")), string.Join(", ", RegionExporter.LastDependencies.Where(d => d.Contains("BarrelGroup") || d.Contains("LightHolder")).ToArray()));
        Check("the enemy marker stays out", !RegionExporter.LastDependencies.Any(d => d.Contains("ShellHorror")));

        AssetDatabase.DeleteAsset(path + ".region.json");
        AssetDatabase.DeleteAsset(path);
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

        SculptAndPaint(ground);

        GameObject water = GroundTools.AddWater(FIRST_RIP);
        water.transform.position = new Vector3(-35f, -1.5f, 0f);
        GameObject river = GroundTools.AddWater(SECOND_RIP);
        river.transform.position = new Vector3(35f, -1.5f, 0f);

        Place(first, STATUE, new Vector3(0f, 12f, 45f));
        Place(second, second.Props.First(p => !p.IsEnemy && p.Batched && p.BaseName.Contains("Rock")).BaseName, new Vector3(15f, 0f, -15f));
        Place(second, HOUND, new Vector3(0f, 0f, 20f));

        GameObject door = DoorTools.Add();
        door.transform.position = new Vector3(-6f, 0f, 6f);
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
        Check("both waters are listed with their donors", manifest.Contains("water | Monsoon | -35,-1.5,0 | 40,1,40 | 0") && manifest.Contains("water | ChersoneseNewTerrain | 35,-1.5,0 | 40,1,40 | 0"));
        Check("the return door is listed", manifest.Contains("returndoor | -6,0,6 | 135"));
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

    private static void SculptAndPaint(Terrain ground)
    {
        TerrainData data = ground.terrainData;
        int size = data.heightmapResolution;
        float[,] heights = data.GetHeights(0, 0, size, size);
        for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                Vector3 world = ground.transform.position + new Vector3(x / (size - 1f) * data.size.x, 0f, z / (size - 1f) * data.size.z);
                heights[z, x] += Bump(world, new Vector3(0f, 0f, 45f), 28f, 12f) / data.size.y; // the hill
                heights[z, x] -= Bump(world, new Vector3(-35f, 0f, 0f), 18f, 4f) / data.size.y; // the pond's bed
                heights[z, x] -= Bump(world, new Vector3(35f, 0f, 0f), 18f, 4f) / data.size.y; // the other pond's
            }
        data.SetHeights(0, 0, heights);

        float[,,] paint = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
        for (int z = 0; z < data.alphamapHeight; z++)
            for (int x = 0; x < data.alphamapWidth; x++)
            {
                Vector3 world = ground.transform.position + new Vector3(x / (data.alphamapWidth - 1f) * data.size.x, 0f, z / (data.alphamapHeight - 1f) * data.size.z);
                float pond = Mathf.Clamp01(Bump(world, new Vector3(-35f, 0f, 0f), 24f, 1f) + Bump(world, new Vector3(35f, 0f, 0f), 24f, 1f));
                float path = Mathf.Clamp01(1f - Mathf.Abs(world.x - Mathf.Sin(world.z * 0.1f) * 6f) / 4f) * (world.z > -5f ? 1f : 0f);
                float rock = Mathf.Clamp01(Bump(world, new Vector3(0f, 0f, 45f), 14f, 1f));
                paint[z, x, 0] = Mathf.Clamp01(1f - pond - path - rock);
                paint[z, x, 1] = path;
                paint[z, x, 2] = pond;
                paint[z, x, 3] = rock;
            }
        data.SetAlphamaps(0, 0, paint);
    }

    // A smooth dome, full height at the centre and nothing beyond the radius.
    private static float Bump(Vector3 world, Vector3 center, float radius, float height)
    {
        float t = Mathf.Clamp01(1f - new Vector2(world.x - center.x, world.z - center.z).magnitude / radius);
        return height * t * t * (3f - 2f * t);
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
