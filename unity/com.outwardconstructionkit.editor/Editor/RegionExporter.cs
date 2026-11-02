using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Turns the open scene into a mod: a bundle holding the scene, plus a text list of vanilla props.
/// Each stand-in is decided on its own. Unchanged ones become references and leave the bundle, so no
/// art ships for them. Altered ones can't be references, so they stay in the scene and are embedded.
/// </summary>
internal static class RegionExporter
{
    public const string PLUGIN_CONFIG = "catalysten.outwardconstructionkit.cfg";
    private const string SPAWN_ROOT = "SpawnPointManager"; // the plugin finds it by this exact name
    private const float SPAWN_HEIGHT = 0.1f;
    private const float GROUND_CHECK_START = 3f;
    private const float GROUND_CHECK_DISTANCE = 200f;
    private const float MAX_GROUND_GAP = 1f;
    private const string REGIONS_FOLDER = "Assets/Regions";
    private const string WORK_FOLDER = "Assets/OCK/_Export";

    /// <summary>What went into the last bundle, and the area enemies can walk, for the self-test to examine.</summary>
    public static string[] LastDependencies = new string[0];
    public static Bounds LastNavMeshBounds;

    /// <summary>Each region gets its own mod folder, so it can be installed and removed on its own.</summary>
    public static string ExportToProfile()
    {
        if (!OckSettings.PathsConfirmed)
        {
            PathsWindow.Open();
            throw new InvalidOperationException("Please confirm where the game and your plugins folder are, then export again.");
        }
        string id = RegionInfo.For(SceneManager.GetActiveScene().path).Id;
        return Export(Path.Combine(OckSettings.PluginsFolder, "OCK-Region-" + id));
    }

    [MenuItem("Construction Kit/New Region Scene")]
    public static void NewRegionScene()
    {
        Scene current = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(current.path) && !EditorUtility.DisplayDialog("New Region Scene", $"This makes a new scene and closes '{current.name}'. It stays in your project, and you can reopen it from the Project window.", "Create", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        CreateStarterScene("NewRegion");
        AddGroundWindow.Open(null);
    }

    /// <summary>A sun and a spawn point, the setup a person wouldn't think to add. Ground comes next, from Add Ground.</summary>
    public static Scene CreateStarterScene(string name)
    {
        Directory.CreateDirectory(REGIONS_FOLDER);
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var light = new GameObject("Sun").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var spawns = new GameObject(SPAWN_ROOT);
        var spawn = new GameObject("Spawn0");
        spawn.transform.SetParent(spawns.transform, false);
        spawn.transform.position = new Vector3(0f, 0.1f, 0f);

        EditorSceneManager.SaveScene(scene, AssetDatabase.GenerateUniqueAssetPath(REGIONS_FOLDER + "/" + name + ".unity"));
        return scene;
    }

    [MenuItem("Construction Kit/Export Region")]
    public static void ExportFromMenu()
    {
        string report = ExportToProfile();
        Debug.Log("[OCK] " + report);
        EditorUtility.DisplayDialog("Construction Kit", report, "OK");
    }

    /// <summary>Exports the open scene to a folder and returns a summary. Throws if the scene can't load in game.</summary>
    public static string Export(string outputFolder)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
            throw new InvalidOperationException("Save the scene before exporting.");
        Validate(scene);
        List<string> placementWarnings = PlacementWarnings(scene);
        EditorSceneManager.SaveScene(scene);
        string originalPath = scene.path;
        string sceneName = scene.name; // the scene handle stops being valid once another scene opens

        RegionInfo info = RegionInfo.For(originalPath);
        string entranceChange = TakeEntranceFromGame(info, originalPath);
        string manifest = BuildManifest(scene, info);
        int referenced = manifest.Split('\n').Count(l => l.StartsWith("prop "));
        int enemies = manifest.Split('\n').Count(l => l.StartsWith("enemy "));
        int items = manifest.Split('\n').Count(l => l.StartsWith("item "));
        int working = manifest.Split('\n').Count(l => l.StartsWith("interactive "));
        string workPath = WORK_FOLDER + "/" + info.BuiltSceneName + ".unity";
        Directory.CreateDirectory(WORK_FOLDER);
        AssetDatabase.Refresh();
        if (!AssetDatabase.CopyAsset(originalPath, workPath))
            throw new InvalidOperationException("Couldn't copy the scene for export.");

        try
        {
            Scene work = EditorSceneManager.OpenScene(workPath, OpenSceneMode.Single);
            // Decided before baking, because the bake's static flags count as changes to a prefab instance.
            var altered = new HashSet<GameObject>(PropRef.FindIn(work).Where(p => !p.Value.IsEnemy && PropRef.IsAltered(p.Key)).Select(p => p.Key));
            GroundTools.PrepareForExport(work, WORK_FOLDER); // before the bake, so nothing walks on a water marker
            DoorTools.RemoveMarkers(work);
            ItemTools.RemoveMarkers(work);
            RemoveCameras(work);
            // Also before the bake, because enemies stand on the NavMesh and aren't part of it.
            foreach (KeyValuePair<GameObject, PropRef> enemy in PropRef.FindIn(work).Where(p => p.Value.IsEnemy))
                UnityEngine.Object.DestroyImmediate(enemy.Key);
            int navMeshTriangles = BakeNavMesh(work);
            int embedded = 0;
            var embeddedShaders = new HashSet<string>();
            int standardMaterials = 0;
            foreach (KeyValuePair<GameObject, PropRef> prop in PropRef.FindIn(work))
            {
                if (!altered.Contains(prop.Key))
                {
                    UnityEngine.Object.DestroyImmediate(prop.Key);
                    continue;
                }
                Embed(prop.Key);
                embedded++;
                foreach (Renderer renderer in prop.Key.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials.Where(m => m != null))
                    {
                        if (material.shader.name == PropImporter.STAND_IN_SHADER)
                            standardMaterials++;
                        else if (material.shader.name == "Standard")
                            continue; // Unity's own default material, which works as built
                        else
                            embeddedShaders.Add(material.shader.name);
                    }
            }
            EditorSceneManager.SaveScene(work);
            LastDependencies = AssetDatabase.GetDependencies(workPath, true);

            string buildFolder = "Build";
            Directory.CreateDirectory(buildFolder);
            // The build name is stored in the bundle, and two loaded bundles can't share one.
            var build = new AssetBundleBuild { assetBundleName = info.Id, assetNames = new[] { workPath } };
            if (BuildPipeline.BuildAssetBundles(buildFolder, new[] { build }, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64) == null)
                throw new InvalidOperationException("The bundle build failed; see the Console.");

            Directory.CreateDirectory(outputFolder);
            string bundleOut = Path.Combine(outputFolder, info.Id);
            File.Copy(Path.Combine(buildFolder, info.Id), bundleOut, true);
            File.WriteAllText(bundleOut + ".refs.txt", manifest);

            string summary = $"Exported '{info.DisplayName}' ({info.Id}, from {sceneName}): {new FileInfo(bundleOut).Length / 1024} KB bundle, {referenced} vanilla props referenced, {embedded} altered props embedded, {enemies} enemies, {items} items, {working} chests and gathering spots, NavMesh {navMeshTriangles} triangles, entrance {(string.IsNullOrEmpty(info.DoorScene) ? "not set" : info.DoorScene)}, to {outputFolder}";
            if (entranceChange != null)
                summary += "\n" + entranceChange;
            foreach (string warning in placementWarnings)
                summary += "\nWARNING: " + warning;
            if (embedded > 0)
            {
                summary += $"\nEmbedded materials expecting real game shaders: [{string.Join(", ", embeddedShaders.ToArray())}].";
                if (standardMaterials > 0)
                    summary += $"\nWARNING: {standardMaterials} embedded materials couldn't be matched to their originals, so they'll keep a simplified look.";
                summary += "\nEmbedded props contain art from Outward. Check the rights holder's and the mod site's rules before sharing this mod.";
            }
            return summary;
        }
        finally
        {
            EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);
            AssetDatabase.DeleteAsset(WORK_FOLDER);
        }
    }

    /// <summary>The short version of an export's summary: what happened and anything needing attention.</summary>
    public static string Brief(string report)
    {
        var kept = new List<string>();
        foreach (string line in report.Split('\n'))
        {
            if (line.StartsWith("Exported '"))
                kept.Add("Saved your scene and exported '" + line.Substring("Exported '".Length, line.IndexOf('\'', "Exported '".Length) - "Exported '".Length) + "'.");
            else if (line.Length > 0 && !line.StartsWith("Embedded "))
                kept.Add(line);
        }
        return string.Join("\n", kept.ToArray());
    }

    // The door placed with Ctrl+F10 is only in this PC's config, so it's copied to what ships. A change gets said out
    // loud, because a door placed by accident would otherwise replace the shipped one unnoticed.
    private static string TakeEntranceFromGame(RegionInfo info, string scenePath)
    {
        string before = info.DoorScene + "|" + info.DoorPosition;
        bool hadOne = !string.IsNullOrEmpty(info.DoorScene);
        if (!info.TryCopyDoorFromGame() || info.DoorScene + "|" + info.DoorPosition == before)
            return null;
        info.SaveFor(scenePath);
        return $"Entrance {(hadOne ? "changed" : "set")} to the door you placed in game: {LocationNames.For(info.DoorScene)} at {info.DoorPosition}.";
    }

    // Baked while stand-ins are still in the scene, because enemies have to walk around the real props too.
    // The result is stored with the scene, so it ships in the bundle, and the game's AI uses it as normal.
    private static int BakeNavMesh(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name != SPAWN_ROOT)
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name != "Marker" && renderer.name != "Facing")
                        GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) | StaticEditorFlags.NavigationStatic);
                foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true)) // not a Renderer
                    GameObjectUtility.SetStaticEditorFlags(terrain.gameObject, GameObjectUtility.GetStaticEditorFlags(terrain.gameObject) | StaticEditorFlags.NavigationStatic);
            }
        UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
        UnityEngine.AI.NavMeshTriangulation mesh = UnityEngine.AI.NavMesh.CalculateTriangulation();
        if (mesh.vertices.Length > 0)
        {
            var bounds = new Bounds(mesh.vertices[0], Vector3.zero);
            foreach (Vector3 vertex in mesh.vertices)
                bounds.Encapsulate(vertex);
            Debug.Log($"[OCK] NavMesh: {mesh.indices.Length / 3} triangles, from {bounds.min} to {bounds.max}.");
            LastNavMeshBounds = bounds;
        }
        return mesh.indices.Length / 3;
    }

    // Becomes ordinary scene content, on exact copies of the vanilla materials so the real shaders can be swapped in.
    private static void Embed(GameObject instance)
    {
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m == null ? null : PropImporter.GetFaithfulMaterial(m)).ToArray();
            foreach (Material material in renderer.sharedMaterials.Where(m => m != null))
                PropImporter.EnsureFullQuality(material);
        }
    }

    public static bool HasSpawnPoint(Scene scene)
    {
        return FirstSpawnPoint(scene) != null;
    }

    /// <summary>Where players arrive, or null if the scene has nowhere yet.</summary>
    public static GameObject FirstSpawnPoint(Scene scene)
    {
        GameObject spawns = scene.GetRootGameObjects().FirstOrDefault(r => r.name == SPAWN_ROOT);
        return spawns != null && spawns.transform.childCount > 0 ? spawns.transform.GetChild(0).gameObject : null;
    }

    /// <summary>Adds where players arrive, for a scene that didn't start from New Region Scene.</summary>
    public static GameObject AddSpawnPoint(Vector3 position)
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject spawns = scene.GetRootGameObjects().FirstOrDefault(r => r.name == SPAWN_ROOT);
        if (spawns == null)
        {
            spawns = new GameObject(SPAWN_ROOT);
            Undo.RegisterCreatedObjectUndo(spawns, "Add Spawn Point");
        }
        var spawn = new GameObject("Spawn" + spawns.transform.childCount);
        Undo.RegisterCreatedObjectUndo(spawn, "Add Spawn Point");
        spawn.transform.SetParent(spawns.transform, false);
        spawn.transform.position = position + Vector3.up * SPAWN_HEIGHT;
        EditorSceneManager.MarkSceneDirty(scene);
        return spawn;
    }

    private static void Validate(Scene scene)
    {
        if (!HasSpawnPoint(scene))
            throw new InvalidOperationException("This scene has no spawn point, so players would have nowhere to arrive. Click Add Spawn Point in the Prop Browser, or start from New Region Scene.");
        if (HasNothingToStandOn(scene))
            throw new InvalidOperationException("This scene has nothing under its spawn point, so players would fall forever. Click Add Ground on the Ground tab of the Prop Browser.");
    }

    // Stand-in props may not have colliders here, but the real ones do in game, so they could be the floor.
    private static bool HasNothingToStandOn(Scene scene)
    {
        Physics.SyncTransforms();
        return !PropRef.FindIn(scene).Any(p => !p.Value.IsEnemy)
            && !Physics.Raycast(FirstSpawnPoint(scene).transform.position + Vector3.up * GROUND_CHECK_START, Vector3.down, GROUND_CHECK_DISTANCE, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    public class PlacementProblem
    {
        public string Text;
        public GameObject Target;
    }

    /// <summary>Spawn points and doors a player couldn't use, because they float above the ground or are under it.</summary>
    public static List<PlacementProblem> PlacementProblems(Scene scene)
    {
        Physics.SyncTransforms();
        var problems = new List<PlacementProblem>();
        GameObject spawns = scene.GetRootGameObjects().FirstOrDefault(r => r.name == SPAWN_ROOT);
        if (spawns != null)
            foreach (Transform spawn in spawns.transform)
                CheckStandsOnGround("Spawn point " + spawn.name, spawn, problems);
        foreach (Transform door in DoorTools.Doors(scene))
            CheckStandsOnGround("A return door", door, problems);
        return problems;
    }

    public static List<string> PlacementWarnings(Scene scene)
    {
        return PlacementProblems(scene).Select(p => p.Text).ToList();
    }

    private static void CheckStandsOnGround(string what, Transform thing, List<PlacementProblem> problems)
    {
        Vector3 position = thing.position;
        RaycastHit hit;
        string problem = null;
        if (!Physics.Raycast(position + Vector3.up * GROUND_CHECK_START, Vector3.down, out hit, GROUND_CHECK_DISTANCE, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            problem = $"{what} at {position} has no ground under it, or is below the ground.";
        else if (position.y - hit.point.y > MAX_GROUND_GAP)
            problem = $"{what} at {position} floats {position.y - hit.point.y:0.0} m above the ground.";
        else if (hit.point.y - position.y > MAX_GROUND_GAP)
            problem = $"{what} at {position} is {hit.point.y - position.y:0.0} m under the ground.";
        if (problem != null)
            problems.Add(new PlacementProblem { Text = problem, Target = thing.gameObject });
    }

    // A scene made with Unity's own New Scene brings a camera, which would compete with the one the game borrows.
    private static int RemoveCameras(Scene scene)
    {
        Camera[] cameras = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).ToArray();
        foreach (Camera camera in cameras)
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
        return cameras.Length;
    }

    private static string BuildManifest(Scene scene, RegionInfo info)
    {
        var ci = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append("# Written by the Construction Kit exporter.\n");
        text.Append("region | ").Append(info.Id).Append(" | ").Append(info.DisplayName.Replace('|', '/')).Append('\n');
        text.Append("game | ").Append(GameFingerprint()).Append('\n');
        text.Append("environment | ").Append(OckSettings.EnvironmentDonor).Append('\n');
        if (!string.IsNullOrEmpty(info.DoorScene))
            text.Append("door | ").Append(info.DoorScene).Append(" | ").Append(info.DoorPosition).Append(" | ").Append(info.DoorRotationY.ToString("0.###", ci)).Append('\n');
        if (info.DefeatLine() != null)
            text.Append(info.DefeatLine()).Append('\n');
        if (!string.IsNullOrEmpty(info.DoorModelPath))
            text.Append("doormodel | ").Append(info.DoorModelScene).Append(" | ").Append(info.DoorModelPath).Append(" | ")
                .Append(info.DoorModelRotation.x.ToString("0.###", ci)).Append(',').Append(info.DoorModelRotation.y.ToString("0.###", ci)).Append(',').Append(info.DoorModelRotation.z.ToString("0.###", ci)).Append(" | ")
                .Append(info.DoorModelScale.ToString("0.###", ci)).Append('\n');
        foreach (KeyValuePair<GameObject, PropRef> enemy in PropRef.FindIn(scene).Where(p => p.Value.IsEnemy).OrderBy(p => p.Key.name))
        {
            Vector3 p = enemy.Key.transform.position;
            text.Append("enemy | ").Append(enemy.Value.DonorScene).Append(" | ").Append(enemy.Value.Path).Append(" | ")
                .Append(p.x.ToString("0.###", ci)).Append(',').Append(p.y.ToString("0.###", ci)).Append(',').Append(p.z.ToString("0.###", ci)).Append(" | ")
                .Append(enemy.Key.transform.eulerAngles.y.ToString("0.###", ci)).Append(info.StaysGone(enemy.Key) ? " | norespawn" : "").Append('\n');
        }
        foreach (KeyValuePair<GameObject, PropRef> prop in PropRef.FindIn(scene).Where(p => p.Value.IsWorking && !PropRef.IsAltered(p.Key)).OrderBy(p => p.Key.name))
        {
            Vector3 p = prop.Key.transform.position;
            Vector3 turn = (prop.Key.transform.rotation * Quaternion.Inverse(prop.Value.SourceRotation)).eulerAngles;
            text.Append("interactive | ").Append(prop.Value.DonorScene).Append(" | ").Append(prop.Value.Path).Append(" | ")
                .Append(p.x.ToString("0.###", ci)).Append(',').Append(p.y.ToString("0.###", ci)).Append(',').Append(p.z.ToString("0.###", ci)).Append(" | ")
                .Append(turn.x.ToString("0.###", ci)).Append(',').Append(turn.y.ToString("0.###", ci)).Append(',').Append(turn.z.ToString("0.###", ci));
            string contents = info.ContentsOf(prop.Key);
            if (!string.IsNullOrEmpty(contents))
                text.Append(" | ").Append(contents);
            text.Append('\n');
        }
        foreach (KeyValuePair<GameObject, PropRef> prop in PropRef.FindIn(scene).Where(p => !p.Value.IsEnemy && !p.Value.IsWorking && !PropRef.IsAltered(p.Key) && !ItemTools.IsItemProp(p)).OrderBy(p => p.Key.name))
        {
            Vector3 p = prop.Key.transform.position;
            Vector3 turn = (prop.Key.transform.rotation * Quaternion.Inverse(prop.Value.SourceRotation)).eulerAngles;
            text.Append("prop | ").Append(prop.Value.DonorScene).Append(" | ").Append(prop.Value.Path).Append(" | ")
                .Append(p.x.ToString("0.###", ci)).Append(',').Append(p.y.ToString("0.###", ci)).Append(',').Append(p.z.ToString("0.###", ci)).Append(" | ")
                .Append(turn.x.ToString("0.###", ci)).Append(',').Append(turn.y.ToString("0.###", ci)).Append(',').Append(turn.z.ToString("0.###", ci)).Append(" | ")
                .Append(prop.Value.Check);
            float scale = PropRef.ScaleOf(prop.Key);
            if (!Mathf.Approximately(scale, 1f))
                text.Append(" | ").Append(scale.ToString("0.###", ci));
            text.Append('\n');
        }
        var embeddedShaders = new Dictionary<string, string>();
        foreach (KeyValuePair<GameObject, PropRef> prop in PropRef.FindIn(scene).Where(p => !p.Value.IsEnemy && PropRef.IsAltered(p.Key)))
            foreach (Material material in prop.Key.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null))
            {
                string materialName, shaderName;
                if (PropImporter.TryGetVanillaShader(material, out materialName, out shaderName))
                    embeddedShaders[materialName] = shaderName;
            }
        foreach (KeyValuePair<string, string> shader in embeddedShaders.OrderBy(s => s.Key))
            text.Append("shader | ").Append(shader.Key).Append(" | ").Append(shader.Value).Append('\n');
        GroundTools.AppendManifest(scene, text);
        DoorTools.AppendManifest(scene, text);
        ItemTools.AppendManifest(scene, text, info);
        return text.ToString();
    }

    // Identifies the game build the references were made against. The plugin computes the same value.
    private static string GameFingerprint()
    {
        string file = Path.Combine(OckSettings.GameData, "globalgamemanagers");
        if (!File.Exists(file))
            return "unknown";
        using (SHA1 sha = SHA1.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", "");
    }
}
