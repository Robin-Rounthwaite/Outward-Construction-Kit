using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private const float DEFAULT_GROUND_SIZE = 200f;
    private const float METRES_PER_HEIGHT_CELL = 200f / 256f;
    private const float PAINT_CELLS_PER_METRE = 512f / 200f;
    private const int MAX_PAINT_RESOLUTION = 2048;
    public const string DEFAULT_START_TEXTURE = "tex_env_masterGroundGrassFull";
    private const float GROUND_HEIGHT = 60f;
    private const float GROUND_DEPTH = 10f; // room below y = 0 for valleys and lake beds
    private const float EDGE_THICKNESS = 2f;
    private const float PLANE_SIZE = 10f; // Unity's plane primitive
    private const string WATER_PREFIX = "OCK Water ";
    private static readonly Regex s_waterName = new Regex(@"^OCK Water \(([^)]+)\)(?: \[(\w+)\])?");
    private static readonly Regex s_groundTexture = new Regex(@"^tex_env_(master(Ground|Rock|Sand|Mud)|ground)", RegexOptions.IgnoreCase);
    private static readonly Regex s_notColour = new Regex(@"_(n|g|s|sc|h|ao|mask|detail)$", RegexOptions.IgnoreCase);
    private const float ADDED_LAYER_TILE = 6f;

    /// <summary>The types of water in the game. The first is what water is unless chosen otherwise.</summary>
    public static readonly string[] WaterTypes = { "Clean", "Fresh", "Salt", "Rancid", "Magic", "Pure", "Healing" };
    private const string NEXT_WATER_TYPE_PREF = "OCK.NextWaterType";
    private const float WATER_BELOW_GROUND = 0.5f;
    private const float GROUND_SEARCH_HEIGHT = 50f;
    private const float GROUND_SEARCH_DISTANCE = 5000f;
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
        AddGroundWindow.Open(null);
    }

    [MenuItem("Construction Kit/Add Water")]
    public static void AddWaterFromMenu()
    {
        Selection.activeGameObject = AddWater(GROUND_SCENE);
    }

    /// <summary>Replaces the starter room's floor and walls with a flat, grassy terrain at y = 0.</summary>
    public static Terrain AddGround(float size = DEFAULT_GROUND_SIZE, string startScene = null, string startTexture = null)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("Construction Kit", "Save the scene before adding ground.", "OK");
            return null;
        }
        if (!OckSettings.RippedScenes().ContainsKey(GROUND_SCENE))
        {
            EditorUtility.DisplayDialog("Construction Kit", $"Ground uses textures from {GROUND_SCENE}, so rip that scene first, with Construction Kit > Rip a Scene.", "OK");
            return null;
        }

        Terrain existing = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Terrain>(true)).FirstOrDefault();
        if (existing != null)
            return existing;

        var data = new TerrainData
        {
            heightmapResolution = Mathf.ClosestPowerOfTwo(Mathf.RoundToInt(size / METRES_PER_HEIGHT_CELL)) + 1,
            alphamapResolution = Mathf.Min(MAX_PAINT_RESOLUTION, Mathf.ClosestPowerOfTwo(Mathf.RoundToInt(size * PAINT_CELLS_PER_METRE))),
            baseMapResolution = 512,
            size = new Vector3(size, GROUND_HEIGHT, size),
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
        root.transform.position = new Vector3(-size / 2f, -GROUND_DEPTH, -size / 2f);
        GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.NavigationStatic);
        var terrain = root.GetComponent<Terrain>();
        // Named outright, so it's in the bundle rather than depending on what the game's build kept.
        terrain.materialTemplate = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Terrain-Standard.mat");
        Undo.RegisterCreatedObjectUndo(root, "Add Ground");

        foreach (GameObject box in scene.GetRootGameObjects().Where(r => s_starterBoxes.Contains(r.name)))
            Undo.DestroyObjectImmediate(box);
        AddEdges(root.transform, size);
        if (startTexture != null)
            StartWith(terrain, startScene, startTexture);

        EditorSceneManager.MarkSceneDirty(scene);
        return terrain;
    }

    // First in the list and covering everything, with only as many others as draw in one pass.
    private static void StartWith(Terrain terrain, string scene, string texture)
    {
        TerrainData data = terrain.terrainData;
        TerrainLayer start = FindLayer(terrain, texture) ?? AddLayer(terrain, scene, texture);
        data.terrainLayers = new[] { start }.Concat(data.terrainLayers.Where(l => l != start)).Take(s_layers.Length).ToArray();
        var paint = new float[data.alphamapHeight, data.alphamapWidth, data.terrainLayers.Length];
        for (int z = 0; z < data.alphamapHeight; z++)
            for (int x = 0; x < data.alphamapWidth; x++)
                paint[z, x, 0] = 1f;
        data.SetAlphamaps(0, 0, paint);
    }

    // Invisible walls, or players walk off the side of the terrain and fall forever.
    private static void AddEdges(Transform ground, float size)
    {
        var edges = new GameObject("Ground_Edges").transform;
        edges.SetParent(ground, false);
        float middle = size / 2f;
        float y = GROUND_HEIGHT / 2f;
        float length = size + EDGE_THICKNESS * 2f;
        AddEdge(edges, new Vector3(middle, y, -EDGE_THICKNESS / 2f), new Vector3(length, GROUND_HEIGHT * 2f, EDGE_THICKNESS));
        AddEdge(edges, new Vector3(middle, y, size + EDGE_THICKNESS / 2f), new Vector3(length, GROUND_HEIGHT * 2f, EDGE_THICKNESS));
        AddEdge(edges, new Vector3(-EDGE_THICKNESS / 2f, y, middle), new Vector3(EDGE_THICKNESS, GROUND_HEIGHT * 2f, length));
        AddEdge(edges, new Vector3(size + EDGE_THICKNESS / 2f, y, middle), new Vector3(EDGE_THICKNESS, GROUND_HEIGHT * 2f, length));
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
        ShowWholeTexture(layer);
        AssetDatabase.CreateAsset(layer, path);
        return layer;
    }

    // Game textures keep other data in their alpha, mostly near zero, and Unity's Terrain Layers previews draw it as
    // transparency. Leaving it out affects only this working copy, because the game uses its own.
    private static void ShowWholeTexture(TerrainLayer layer)
    {
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(layer.diffuseTexture)) as TextureImporter;
        if (importer == null || importer.alphaSource == TextureImporterAlphaSource.None)
            return;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.SaveAndReimport();
    }

    /// <summary>Fixes the previews of layers made before ShowWholeTexture existed.</summary>
    public static void ShowWholeTexturesInLibrary()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TerrainLayer", new[] { PropImporter.LIBRARY + "/Ground" }))
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(guid));
            if (layer != null && layer.diffuseTexture != null)
                ShowWholeTexture(layer);
        }
    }

    /// <summary>A flat marker to move and scale, at the middle of the view. Its name carries the donor scene, which sets how the water looks, and its type.</summary>
    public static GameObject AddWater(string donorScene)
    {
        GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = WATER_PREFIX + "(" + donorScene + ") [" + WaterTypes[0] + "]";
        Object.DestroyImmediate(water.GetComponent<Collider>());
        water.transform.position = SpotForNewWater();
        water.transform.localScale = new Vector3(4f, 1f, 4f);
        water.GetComponent<Renderer>().sharedMaterial = GetWaterMaterial();
        Undo.RegisterCreatedObjectUndo(water, "Add Water");
        EditorSceneManager.MarkSceneDirty(water.scene);
        return water;
    }

    private static Vector3 SpotForNewWater()
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null)
            return new Vector3(0f, -1f, 0f);
        Vector3 spot = view.pivot;
        RaycastHit hit;
        if (Physics.Raycast(spot + Vector3.up * GROUND_SEARCH_HEIGHT, Vector3.down, out hit, GROUND_SEARCH_DISTANCE))
            spot.y = hit.point.y - WATER_BELOW_GROUND;
        return spot;
    }

    /// <summary>True if this object is a water marker. Markers carry no scripts, so what they hold is in their names.</summary>
    public static bool IsWater(GameObject marker)
    {
        return marker != null && s_waterName.IsMatch(marker.name);
    }

    /// <summary>The type chosen for water not yet added.</summary>
    public static int NextWaterType
    {
        get { return Mathf.Clamp(EditorPrefs.GetInt(NEXT_WATER_TYPE_PREF, 0), 0, WaterTypes.Length - 1); }
        set { EditorPrefs.SetInt(NEXT_WATER_TYPE_PREF, value); }
    }

    public static int WaterTypeIndex(GameObject marker)
    {
        return Mathf.Max(0, System.Array.IndexOf(WaterTypes, s_waterName.Match(marker.name).Groups[2].Value));
    }

    /// <summary>Which type of water it is: drinking from it and filling a waterskin use this.</summary>
    public static void SetWaterType(GameObject marker, int type)
    {
        Undo.RecordObject(marker, "Set Water Type");
        marker.name = WATER_PREFIX + "(" + s_waterName.Match(marker.name).Groups[1].Value + ") [" + WaterTypes[type] + "]";
        EditorSceneManager.MarkSceneDirty(marker.scene);
    }

    /// <summary>The vanilla ground textures a scene's rip offers, by name.</summary>
    public static List<string> GroundTexturesIn(string ripRoot)
    {
        string folder = Path.Combine(ripRoot, "Assets", "Texture2D");
        if (!Directory.Exists(folder))
            return new List<string>();
        return Directory.GetFiles(folder, "*.png").Select(Path.GetFileNameWithoutExtension)
            .Where(n => s_groundTexture.IsMatch(n) && !s_notColour.IsMatch(n)).OrderBy(n => n).ToList();
    }

    /// <summary>Adds a vanilla texture as a new layer to paint with, shipping as a reference like the starting four.</summary>
    public static TerrainLayer AddLayer(Terrain ground, string scene, string texture)
    {
        string folder = PropImporter.LIBRARY + "/Ground";
        PropImporter.EnsureFolder(folder);
        string name = "Ground_" + texture.Replace("tex_env_", "");
        string path = folder + "/" + name + ".terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            string normal = NormalFor(texture);
            layer = new TerrainLayer
            {
                diffuseTexture = PropImporter.ImportTextureByName(scene, texture, false),
                normalMapTexture = normal == null ? null : PropImporter.ImportTextureByName(scene, normal, true),
                tileSize = new Vector2(ADDED_LAYER_TILE, ADDED_LAYER_TILE),
            };
            ShowWholeTexture(layer);
            AssetDatabase.CreateAsset(layer, path);
        }

        TerrainData data = ground.terrainData;
        if (!data.terrainLayers.Contains(layer))
        {
            Undo.RecordObject(data, "Add Ground Texture");
            data.terrainLayers = data.terrainLayers.Concat(new[] { layer }).ToArray();
            EditorUtility.SetDirty(data);
        }
        return layer;
    }

    /// <summary>The ground's layer using this texture, if it has one yet.</summary>
    public static TerrainLayer FindLayer(Terrain ground, string texture)
    {
        return ground.terrainData.terrainLayers.FirstOrDefault(l => l != null && l.diffuseTexture != null && l.diffuseTexture.name == texture);
    }

    // Unity has no public way to choose a Terrain's tool or the layer it paints with. These are for Unity 2020.3, and
    // the self-test notices if a version doesn't have them. Without them, the Prop Browser's message gives the steps.
    private const BindingFlags ANY_MEMBER = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const double SECONDS_TO_WAIT_FOR_INSPECTOR = 2.0;
    private static readonly System.Type s_paintTool = typeof(Editor).Assembly.GetType("UnityEditor.Experimental.TerrainAPI.PaintTextureTool");
    private static readonly FieldInfo s_activeInspector = typeof(Editor).Assembly.GetType("UnityEditor.TerrainInspector")?.GetField("s_activeTerrainInspectorInstance", ANY_MEMBER);
    private static readonly MethodInfo s_selectPaintTool = typeof(Editor).Assembly.GetType("UnityEditor.TerrainInspector")?.GetMethod("SelectPaintTool", new[] { typeof(System.Type) });

    private static object PaintToolInstance()
    {
        return s_paintTool?.GetProperty("instance", ANY_MEMBER | BindingFlags.FlattenHierarchy)?.GetValue(null);
    }

    public static bool CanSwitchTerrainTools
    {
        get
        {
            return s_paintTool != null && s_activeInspector != null && s_selectPaintTool != null && PaintToolInstance() != null
                && s_paintTool.GetField("m_SelectedTerrainLayer", ANY_MEMBER) != null && s_paintTool.GetField("m_SelectedTerrainLayerIndex", ANY_MEMBER) != null;
        }
    }

    /// <summary>
    /// Selects the ground, with its Paint Texture tool on and this layer chosen, so the next stroke paints with it.
    /// Says whether that could be done. The Inspector only appears once the selection has changed, so the tool waits for it.
    /// </summary>
    public static bool ReadyToPaint(Terrain ground, TerrainLayer layer)
    {
        Selection.activeGameObject = ground.gameObject;
        if (!CanSwitchTerrainTools)
            return false;
        try
        {
            object tool = PaintToolInstance();
            s_paintTool.GetField("m_SelectedTerrainLayer", ANY_MEMBER).SetValue(tool, layer);
            s_paintTool.GetField("m_SelectedTerrainLayerIndex", ANY_MEMBER).SetValue(tool, -1); // worked out again from the layer
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[OCK] Couldn't choose the layer to paint with: " + e);
            return false;
        }

        double giveUpAt = EditorApplication.timeSinceStartup + SECONDS_TO_WAIT_FOR_INSPECTOR;
        EditorApplication.CallbackFunction waitForInspector = null;
        waitForInspector = () =>
        {
            var inspector = s_activeInspector.GetValue(null) as Editor;
            bool ready = inspector != null && inspector.target == ground;
            if (!ready && EditorApplication.timeSinceStartup < giveUpAt)
                return;
            EditorApplication.update -= waitForInspector;
            if (!ready)
                return;
            try
            {
                s_selectPaintTool.Invoke(inspector, new object[] { s_paintTool });
                inspector.Repaint();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[OCK] Couldn't switch the Terrain to Paint Texture: " + e);
            }
        };
        EditorApplication.update += waitForInspector;
        return true;
    }

    // A normal map is named like its texture, with or without the texture's own _d ending.
    private static string NormalFor(string texture)
    {
        string plain = Regex.Replace(texture, "_d$", "");
        return OckSettings.RipRoots().Select(root => Path.Combine(root, "Assets", "Texture2D"))
            .SelectMany(folder => new[] { plain + "_n" }.Where(n => File.Exists(Path.Combine(folder, n + ".png")))).FirstOrDefault();
    }

    /// <summary>Paints a layer in a soft circle, for what's made from code rather than with the Paint tool.</summary>
    public static void PaintCircle(Terrain ground, TerrainLayer layer, Vector3 center, float radius)
    {
        TerrainData data = ground.terrainData;
        int index = System.Array.IndexOf(data.terrainLayers, layer);
        float[,,] paint = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
        for (int z = 0; z < data.alphamapHeight; z++)
            for (int x = 0; x < data.alphamapWidth; x++)
            {
                Vector3 world = ground.transform.position + new Vector3(x / (data.alphamapWidth - 1f) * data.size.x, 0f, z / (data.alphamapHeight - 1f) * data.size.z);
                float amount = Mathf.Clamp01(1f - new Vector2(world.x - center.x, world.z - center.z).magnitude / radius);
                if (amount <= 0f)
                    continue;
                for (int l = 0; l < data.alphamapLayers; l++)
                    paint[z, x, l] *= 1f - amount;
                paint[z, x, index] += amount;
            }
        data.SetAlphamaps(0, 0, paint);
        EditorUtility.SetDirty(data);
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
                .Append(water.transform.eulerAngles.y.ToString("0.###", ci));
            // Older markers have no type, and count as the default.
            string type = s_waterName.Match(water.name).Groups[2].Value;
            text.Append(" | ").Append(type.Length > 0 ? type : WaterTypes[0]).Append('\n');
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
