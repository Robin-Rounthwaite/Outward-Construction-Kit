using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A small finished region to look around, take apart and export, using what the tutorial teaches.</summary>
internal static class ExampleRegion
{
    public const string ID = "example_region";
    public const string GROUND_RIP = "Monsoon";
    private const string ENEMY_RIP = "ChersoneseNewTerrain";
    private const string BARRELS = "mdl_env_propBarrelGroupA";
    private const string STATUE = "mdl_env_propStatueTripleLadies_c";
    private const string HOUND = "Enemy Hound";
    private const string ENTRANCE_SCENE = "CierzoNewTerrain";
    private const string ENTRANCE_POSITION = "-176,-1495.1,764"; // in the open, near where the game starts you off in town
    private static readonly Vector3 s_hill = new Vector3(0f, 0f, 45f);
    private static readonly Vector3 s_pond = new Vector3(-30f, 0f, 5f);

    [MenuItem("Construction Kit/Create Example Region")]
    public static void CreateFromMenu()
    {
        if (!OckSettings.RippedScenes().ContainsKey(GROUND_RIP))
        {
            EditorUtility.DisplayDialog("Construction Kit", $"The example uses models from {GROUND_RIP}, so rip that scene first, with Construction Kit > Rip a Scene.", "OK");
            return;
        }
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            Create();
    }

    /// <summary>Makes and saves the scene, adding a hyena if that rip is there.</summary>
    public static Scene Create()
    {
        Scene scene = RegionExporter.CreateStarterScene("ExampleRegion");
        new RegionInfo { Id = ID, DisplayName = "Example Region", DoorScene = ENTRANCE_SCENE, DoorPosition = ENTRANCE_POSITION }.SaveFor(scene.path);

        Terrain ground = GroundTools.AddGround();
        SculptAndPaint(ground, s_hill, new[] { s_pond });

        Dictionary<string, string> ripped = OckSettings.RippedScenes();
        RipScene props = RipScene.Load(GROUND_RIP, ripped[GROUND_RIP]);
        Place(props, STATUE, new Vector3(0f, 12f, 45f), 0f, 1f);
        Place(props, BARRELS, new Vector3(6f, 0f, 8f), 30f, 1f);
        Place(props, BARRELS, new Vector3(-6f, 0f, 12f), 200f, 1.5f); // moved, turned and scaled, and still only a reference

        if (ripped.ContainsKey(ENEMY_RIP))
            Place(RipScene.Load(ENEMY_RIP, ripped[ENEMY_RIP]), HOUND, new Vector3(10f, 0f, 30f), 180f, 1f);

        GameObject water = GroundTools.AddWater(GROUND_RIP);
        water.transform.position = new Vector3(s_pond.x, -1.5f, s_pond.z);

        GameObject door = DoorTools.Add();
        door.transform.position = new Vector3(-1.8f, 0f, -1.8f);
        door.transform.rotation = Quaternion.Euler(0f, 45f, 0f); // facing the spawn point in the middle

        EditorSceneManager.SaveScene(scene);
        return scene;
    }

    private static void Place(RipScene rip, string name, Vector3 position, float turnY, float scale)
    {
        RipScene.Prop prop = rip.Props.FirstOrDefault(p => p.BaseName == name);
        if (prop == null)
        {
            Debug.LogWarning($"[OCK] The example wanted '{name}', which isn't in the {rip.SceneName} rip, so it's left out.");
            return;
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(PropImporter.Import(rip, prop));
        instance.transform.position = position;
        instance.transform.rotation = Quaternion.Euler(0f, turnY, 0f) * instance.transform.rotation;
        instance.transform.localScale *= scale;
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
    }

    /// <summary>Raises a rocky hill and sinks sandy pond beds, with a dirt path up to the hill from the middle.</summary>
    public static void SculptAndPaint(Terrain ground, Vector3 hill, Vector3[] ponds)
    {
        TerrainData data = ground.terrainData;
        int size = data.heightmapResolution;
        float[,] heights = data.GetHeights(0, 0, size, size);
        for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                Vector3 world = ground.transform.position + new Vector3(x / (size - 1f) * data.size.x, 0f, z / (size - 1f) * data.size.z);
                heights[z, x] += Bump(world, hill, 28f, 12f) / data.size.y;
                foreach (Vector3 pond in ponds)
                    heights[z, x] -= Bump(world, pond, 18f, 4f) / data.size.y;
            }
        data.SetHeights(0, 0, heights);

        float[,,] paint = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
        for (int z = 0; z < data.alphamapHeight; z++)
            for (int x = 0; x < data.alphamapWidth; x++)
            {
                Vector3 world = ground.transform.position + new Vector3(x / (data.alphamapWidth - 1f) * data.size.x, 0f, z / (data.alphamapHeight - 1f) * data.size.z);
                float sand = Mathf.Clamp01(ponds.Sum(p => Bump(world, p, 24f, 1f)));
                float path = Mathf.Clamp01(1f - Mathf.Abs(world.x - Mathf.Sin(world.z * 0.1f) * 6f) / 4f) * (world.z > -5f ? 1f : 0f);
                float rock = Mathf.Clamp01(Bump(world, hill, 14f, 1f));
                paint[z, x, 0] = Mathf.Clamp01(1f - sand - path - rock);
                paint[z, x, 1] = path;
                paint[z, x, 2] = sand;
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
}
