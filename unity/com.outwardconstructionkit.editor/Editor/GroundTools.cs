using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Hills and painted ground use Unity's own Terrain, so modders sculpt and paint with its standard tools. The
/// heights and paint are the modder's and ship in the bundle, but vanilla textures stay references. Water is only
/// a marker here: it leaves the bundle on export, and the game's own water plane is sized to match in game.
/// </summary>
internal static class GroundTools
{
    private const string REGIONS_FOLDER = "Assets/Regions";
    private const string GROUND_SCENE = "Monsoon"; // its materials are known to load all the textures below
    private const float GROUND_SIZE = 200f;
    private const float GROUND_HEIGHT = 60f;
    private const float GROUND_DEPTH = 10f; // room below y = 0 for valleys and lake beds
    private const float EDGE_THICKNESS = 2f;
    private const float PLANE_SIZE = 10f; // Unity's plane primitive
    private const string WATER_PREFIX = "OCK Water ";
    private static readonly Regex s_waterName = new Regex(@"^OCK Water \(([^)]+)\)");
    private static readonly Regex s_librarySource = new Regex("^" + PropImporter.LIBRARY + @"/([^/]+)/Textures/");
    private static readonly string[] s_starterBoxes = { "Floor", "Wall_N", "Wall_S", "Wall_E", "Wall_W" };

    private struct LayerSource
    {
        public string Name, Diffuse, Normal;
        public float Tile;
    }

    // Four, because Unity draws up to four in one pass, and more need a second that hasn't been tested in game.
    private static readonly LayerSource[] s_layers =
    {
        new LayerSource { Name = "Ground_Grass", Diffuse = "tex_env_masterGroundGrassFull", Normal = "tex_env_masterGroundGrassFull_n", Tile = 6f },
        new LayerSource { Name = "Ground_Dirt", Diffuse = "tex_env_masterGroundEarth", Tile = 6f },
        new LayerSource { Name = "Ground_Sand", Diffuse = "tex_env_masterGroundRockySand", Normal = "tex_env_masterGroundRockySand_n", Tile = 6f },
        new LayerSource { Name = "Ground_Rock", Diffuse = "tex_env_masterRockFace2", Normal = "tex_env_masterRockFace2_n", Tile = 8f },
    };

    [MenuItem("Construction Kit/Add Ground")]
    public static void AddGroundFromMenu()
    {
        Terrain ground = AddGround();
        if (ground != null)
            Selection.activeGameObject = ground.gameObject;
    }

    [MenuItem("Construction Kit/Add Water")]
    public static void AddWaterFromMenu()
    {
        Selection.activeGameObject = AddWater(GROUND_SCENE);
    }

    /// <summary>Replaces the starter room's floor and walls with a flat, grassy terrain at y = 0.</summary>
    public static Terrain AddGround()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("Construction Kit", "Save the scene before adding ground.", "OK");
            return null;
        }

        Terrain existing = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Terrain>(true)).FirstOrDefault();
        if (existing != null)
            return existing;

        var data = new TerrainData
        {
            heightmapResolution = 257,
            alphamapResolution = 512,
            baseMapResolution = 512,
            size = new Vector3(GROUND_SIZE, GROUND_HEIGHT, GROUND_SIZE),
        };
        data.SetDetailResolution(0, 8); // no painted grass tufts, so no memory for them
        data.terrainLayers = s_layers.Select(GetLayer).ToArray();

        int heights = data.heightmapResolution;
        var flat = new float[heights, heights];
        for (int z = 0; z < heights; z++)
            for (int x = 0; x < heights; x++)
                flat[z, x] = GROUND_DEPTH / GROUND_HEIGHT;
        data.SetHeights(0, 0, flat);

        var paint = new float[data.alphamapHeight, data.alphamapWidth, s_layers.Length];
        for (int z = 0; z < data.alphamapHeight; z++)
            for (int x = 0; x < data.alphamapWidth; x++)
                paint[z, x, 0] = 1f;
        data.SetAlphamaps(0, 0, paint);

        Directory.CreateDirectory(REGIONS_FOLDER);
        AssetDatabase.CreateAsset(data, AssetDatabase.GenerateUniqueAssetPath(REGIONS_FOLDER + "/" + scene.name + "_Ground.asset"));

        GameObject root = Terrain.CreateTerrainGameObject(data);
        root.name = "Ground";
        root.transform.position = new Vector3(-GROUND_SIZE / 2f, -GROUND_DEPTH, -GROUND_SIZE / 2f);
        GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.NavigationStatic);
        var terrain = root.GetComponent<Terrain>();
        // Named outright, so it's in the bundle rather than depending on what the game's build kept.
        terrain.materialTemplate = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Terrain-Standard.mat");
        Undo.RegisterCreatedObjectUndo(root, "Add Ground");

        foreach (GameObject box in scene.GetRootGameObjects().Where(r => s_starterBoxes.Contains(r.name)))
            Undo.DestroyObjectImmediate(box);
        AddEdges(root.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        return terrain;
    }

    // Invisible walls, or players walk off the side of the terrain and fall forever.
    private static void AddEdges(Transform ground)
    {
        var edges = new GameObject("Ground_Edges").transform;
        edges.SetParent(ground, false);
        float middle = GROUND_SIZE / 2f;
        float y = GROUND_HEIGHT / 2f;
        float length = GROUND_SIZE + EDGE_THICKNESS * 2f;
        AddEdge(edges, new Vector3(middle, y, -EDGE_THICKNESS / 2f), new Vector3(length, GROUND_HEIGHT * 2f, EDGE_THICKNESS));
        AddEdge(edges, new Vector3(middle, y, GROUND_SIZE + EDGE_THICKNESS / 2f), new Vector3(length, GROUND_HEIGHT * 2f, EDGE_THICKNESS));
        AddEdge(edges, new Vector3(-EDGE_THICKNESS / 2f, y, middle), new Vector3(EDGE_THICKNESS, GROUND_HEIGHT * 2f, length));
        AddEdge(edges, new Vector3(GROUND_SIZE + EDGE_THICKNESS / 2f, y, middle), new Vector3(EDGE_THICKNESS, GROUND_HEIGHT * 2f, length));
    }

    private static void AddEdge(Transform parent, Vector3 center, Vector3 size)
    {
        var box = new GameObject("Edge").AddComponent<BoxCollider>();
        box.transform.SetParent(parent, false);
        box.center = center;
        box.size = size;
    }

    private static TerrainLayer GetLayer(LayerSource source)
    {
        string folder = PropImporter.LIBRARY + "/Ground";
        PropImporter.EnsureFolder(folder);
        string path = folder + "/" + source.Name + ".terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer != null)
            return layer;

        layer = new TerrainLayer
        {
            diffuseTexture = PropImporter.ImportTextureByName(GROUND_SCENE, source.Diffuse, false),
            normalMapTexture = source.Normal == null ? null : PropImporter.ImportTextureByName(GROUND_SCENE, source.Normal, true),
            tileSize = new Vector2(source.Tile, source.Tile),
        };
        AssetDatabase.CreateAsset(layer, path);
        return layer;
    }

    /// <summary>A flat marker to move and scale. Its name carries the donor scene, which sets how the water looks.</summary>
    public static GameObject AddWater(string donorScene)
    {
        GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = WATER_PREFIX + "(" + donorScene + ")";
        Object.DestroyImmediate(water.GetComponent<Collider>());
        water.transform.position = new Vector3(0f, -1f, 0f);
        water.transform.localScale = new Vector3(4f, 1f, 4f);
        water.GetComponent<Renderer>().sharedMaterial = GetWaterMaterial();
        Undo.RegisterCreatedObjectUndo(water, "Add Water");
        EditorSceneManager.MarkSceneDirty(water.scene);
        return water;
    }

    private static Material GetWaterMaterial()
    {
        string folder = PropImporter.LIBRARY + "/Ground";
        PropImporter.EnsureFolder(folder);
        string path = folder + "/WaterMarker.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.2f, 0.45f, 0.6f, 0.6f) };
            AssetDatabase.CreateAsset(material, path);
        }
        return material;
    }

    private static IEnumerable<GameObject> WaterMarkers(Scene scene)
    {
        return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => s_waterName.IsMatch(t.name)).Select(t => t.gameObject).ToList();
    }

    /// <summary>The manifest lines for the scene's ground textures and water.</summary>
    public static void AppendManifest(Scene scene, StringBuilder text)
    {
        var ci = CultureInfo.InvariantCulture;
        var written = new HashSet<string>();
        foreach (Terrain terrain in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Terrain>(true)))
            foreach (TerrainLayer layer in terrain.terrainData.terrainLayers.Where(l => l != null))
            {
                string donor = DonorOf(layer.diffuseTexture);
                if (donor != null && written.Add(layer.name))
                    text.Append("ground | ").Append(donor).Append(" | ").Append(layer.name).Append(" | ").Append(layer.diffuseTexture.name).Append(" | ")
                        .Append(DonorOf(layer.normalMapTexture) != null ? layer.normalMapTexture.name : "").Append('\n');
            }

        foreach (GameObject water in WaterMarkers(scene))
        {
            Vector3 p = water.transform.position;
            Vector3 scale = water.transform.lossyScale;
            text.Append("water | ").Append(s_waterName.Match(water.name).Groups[1].Value).Append(" | ")
                .Append(p.x.ToString("0.###", ci)).Append(',').Append(p.y.ToString("0.###", ci)).Append(',').Append(p.z.ToString("0.###", ci)).Append(" | ")
                .Append((scale.x * PLANE_SIZE).ToString("0.###", ci)).Append(",1,").Append((scale.z * PLANE_SIZE).ToString("0.###", ci)).Append(" | ")
                .Append(water.transform.eulerAngles.y.ToString("0.###", ci)).Append('\n');
        }
    }

    /// <summary>
    /// Takes water markers out of the export copy and gives its terrains layers with no vanilla art, working on
    /// copies so the modder's own terrain keeps its textures. A modder's own textures stay in and ship as usual.
    /// </summary>
    public static void PrepareForExport(Scene work, string workFolder)
    {
        foreach (GameObject water in WaterMarkers(work))
            Object.DestroyImmediate(water);

        foreach (Terrain terrain in work.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Terrain>(true)))
        {
            string path = workFolder + "/" + terrain.terrainData.name + ".asset";
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(terrain.terrainData), path);
            var copy = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            copy.terrainLayers = copy.terrainLayers.Select(l => WithoutVanillaArt(l, workFolder)).ToArray();

            terrain.terrainData = copy;
            var collider = terrain.GetComponent<TerrainCollider>();
            if (collider != null)
                collider.terrainData = copy;
        }
        AssetDatabase.SaveAssets();
    }

    private static TerrainLayer WithoutVanillaArt(TerrainLayer layer, string workFolder)
    {
        if (layer == null || DonorOf(layer.diffuseTexture) == null)
            return layer;

        string path = workFolder + "/" + layer.name + ".terrainlayer";
        var existing = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (existing != null)
            return existing;

        TerrainLayer copy = Object.Instantiate(layer);
        copy.diffuseTexture = null;
        if (DonorOf(layer.normalMapTexture) != null)
            copy.normalMapTexture = null;
        AssetDatabase.CreateAsset(copy, path);
        return copy;
    }

    // Textures in the library came from a rip, under a folder named for the scene that uses them.
    private static string DonorOf(Texture texture)
    {
        if (texture == null)
            return null;
        Match match = s_librarySource.Match(AssetDatabase.GetAssetPath(texture));
        return match.Success ? match.Groups[1].Value : null;
    }
}
