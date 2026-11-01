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
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        CreateStarterScene("NewRegion");
    }

    /// <summary>A walled room with a sun and a spawn point, which is the least a region can load with.</summary>
    public static Scene CreateStarterScene(string name)
    {
        Directory.CreateDirectory(REGIONS_FOLDER);
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var light = new GameObject("Sun").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
        Box("Wall_N", new Vector3(0f, 3f, 20f), new Vector3(40f, 6f, 1f));
        Box("Wall_S", new Vector3(0f, 3f, -20f), new Vector3(40f, 6f, 1f));
        Box("Wall_E", new Vector3(20f, 3f, 0f), new Vector3(1f, 6f, 40f));
        Box("Wall_W", new Vector3(-20f, 3f, 0f), new Vector3(1f, 6f, 40f));

        var spawns = new GameObject(SPAWN_ROOT);
        var spawn = new GameObject("Spawn0");
        spawn.transform.SetParent(spawns.transform, false);
        spawn.transform.position = new Vector3(0f, 0.1f, 0f);

        EditorSceneManager.SaveScene(scene, AssetDatabase.GenerateUniqueAssetPath(REGIONS_FOLDER + "/" + name + ".unity"));
        return scene;
    }

    private static void Box(string name, Vector3 position, Vector3 scale)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = scale;
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
        EditorSceneManager.SaveScene(scene);
        string originalPath = scene.path;
        string sceneName = scene.name; // the scene handle stops being valid once another scene opens

        RegionInfo info = RegionInfo.For(originalPath);
        string manifest = BuildManifest(scene, info);
        int referenced = manifest.Split('\n').Count(l => l.StartsWith("prop "));
        int enemies = manifest.Split('\n').Count(l => l.StartsWith("enemy "));
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

            string summary = $"Exported '{info.DisplayName}' ({info.Id}, from {sceneName}): {new FileInfo(bundleOut).Length / 1024} KB bundle, {referenced} vanilla props referenced, {embedded} altered props embedded, {enemies} enemies, NavMesh {navMeshTriangles} triangles, entrance {(string.IsNullOrEmpty(info.DoorScene) ? "not set" : info.DoorScene)}, to {outputFolder}";
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
        GameObject spawns = scene.GetRootGameObjects().FirstOrDefault(r => r.name == SPAWN_ROOT);
        return spawns != null && spawns.transform.childCount > 0;
    }

    /// <summary>Adds where players arrive, for a scene that didn't start from New Region.</summary>
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
            throw new InvalidOperationException("This scene has no spawn point, so players would have nowhere to arrive. Click Add Spawn Point in the Prop Browser, or start from New Region.");
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
        foreach (KeyValuePair<GameObject, PropRef> enemy in PropRef.FindIn(scene).Where(p => p.Value.IsEnemy).OrderBy(p => p.Key.name))
        {
            Vector3 p = enemy.Key.transform.position;
            text.Append("enemy | ").Append(enemy.Value.DonorScene).Append(" | ").Append(enemy.Value.Path).Append(" | ")
                .Append(p.x.ToString("0.###", ci)).Append(',').Append(p.y.ToString("0.###", ci)).Append(',').Append(p.z.ToString("0.###", ci)).Append(" | ")
                .Append(enemy.Key.transform.eulerAngles.y.ToString("0.###", ci)).Append('\n');
        }
        foreach (KeyValuePair<GameObject, PropRef> prop in PropRef.FindIn(scene).Where(p => !p.Value.IsEnemy && !PropRef.IsAltered(p.Key)).OrderBy(p => p.Key.name))
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
        GroundTools.AppendManifest(scene, text);
        DoorTools.AppendManifest(scene, text);
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
