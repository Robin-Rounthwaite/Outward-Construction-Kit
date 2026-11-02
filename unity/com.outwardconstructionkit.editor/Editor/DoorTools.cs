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
/// Return doors are markers here: export lists where they are, and the plugin builds a real door at each. A region
/// with none gets one automatically, beside the arrival point. The Scene view draws the door's real model inside
/// each marker. It's only drawn, not an object in the scene, so it can't be saved, exported or selected by accident.
/// </summary>
[InitializeOnLoad]
internal static class DoorTools
{
    private const string MARKER_NAME = "OCK Return Door";
    private const string EXPORT_FOLDER = "Assets/OCK/_Export";
    private const string MARKER_MATERIAL = "/ReturnDoorMarker.mat";
    private const string MARKER_SHADER = "Sprites/Default"; // see-through, to show the model inside
    private static readonly Color s_markerColor = new Color(0.25f, 0.45f, 0.75f, 0.3f);
    private static readonly Vector3 s_doorSize = new Vector3(1.6f, 2.8f, 0.2f); // as the plugin builds it

    // As the plugin's RegionManifest.DefaultDoorModel.
    private const string DEFAULT_MODEL_SCENE = "Monsoon";
    private const string DEFAULT_MODEL_PATH = "InnInteractions/Interactions/Room2/mdl_env_buildingDoorInteractibleF";
    private static readonly Vector3 s_defaultModelPosition = new Vector3(-0.5f, 0f, 0f);

    private static readonly Regex s_instanceSuffix = new Regex(@" \(\d+\)$");

    private struct ModelPart
    {
        public Mesh Mesh;
        public Material[] Materials;
        public Matrix4x4 FromDoor;
    }

    private static readonly List<ModelPart> s_parts = new List<ModelPart>();
    private static List<Transform> s_markers;
    private static string s_modelKey;
    private static bool s_modelMissing;

    static DoorTools()
    {
        Camera.onPreCull += DrawModels;
        EditorApplication.hierarchyChanged += () => s_markers = null;
        EditorSceneManager.activeSceneChangedInEditMode += (from, to) => Refresh();
    }

    [MenuItem("Construction Kit/Add Return Door")]
    public static void AddFromMenu()
    {
        Selection.activeGameObject = Add();
        ImportMissingModel();
    }

    /// <summary>The way a door faces is its blue axis, marked by a small box on the side players walk up to.</summary>
    public static GameObject Add()
    {
        var root = new GameObject(MARKER_NAME);

        Material material = GetMaterial();
        AddPart(root.transform, "Marker", new Vector3(0f, s_doorSize.y / 2f, 0f), s_doorSize, material);
        AddPart(root.transform, "Facing", new Vector3(0f, 1.3f, 0.35f), new Vector3(0.3f, 0.3f, 0.5f), material);

        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            Vector3 position = view.pivot;
            position.y = Mathf.Round(position.y * 10f) / 10f;
            root.transform.position = position;
        }

        Undo.RegisterCreatedObjectUndo(root, "Add Return Door");
        EditorSceneManager.MarkSceneDirty(root.scene);
        return root;
    }

    private static void AddPart(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static Material GetMaterial()
    {
        string folder = PropImporter.LIBRARY + "/Markers";
        PropImporter.EnsureFolder(folder);
        string path = folder + MARKER_MATERIAL;
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find(MARKER_SHADER)) { color = s_markerColor };
            AssetDatabase.CreateAsset(material, path);
        }
        return material;
    }

    // Markers made before models were drawn are solid, which would hide the model.
    private static void MakeMarkersSeeThrough()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(PropImporter.LIBRARY + "/Markers" + MARKER_MATERIAL);
        if (material == null || material.shader.name == MARKER_SHADER)
            return;
        material.shader = Shader.Find(MARKER_SHADER);
        material.color = s_markerColor;
        EditorUtility.SetDirty(material);
    }

    private static IEnumerable<GameObject> Markers(Scene scene)
    {
        return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => t.name.StartsWith(MARKER_NAME)).Select(t => t.gameObject).ToList();
    }

    public static IEnumerable<Transform> Doors(Scene scene)
    {
        return Markers(scene).Select(m => m.transform);
    }

    public static void AppendManifest(Scene scene, StringBuilder text)
    {
        var ci = CultureInfo.InvariantCulture;
        foreach (GameObject door in Markers(scene))
        {
            Vector3 p = door.transform.position;
            text.Append("returndoor | ").Append(p.x.ToString("0.###", ci)).Append(',').Append(p.y.ToString("0.###", ci)).Append(',').Append(p.z.ToString("0.###", ci)).Append(" | ")
                .Append(door.transform.eulerAngles.y.ToString("0.###", ci)).Append('\n');
        }
    }

    public static void RemoveMarkers(Scene work)
    {
        foreach (GameObject door in Markers(work))
            Object.DestroyImmediate(door);
    }

    /// <summary>Picks up a change to which model the region's doors use.</summary>
    public static void Refresh()
    {
        s_markers = null;
        s_modelKey = null;
    }

    /// <summary>True if this region has a door whose model hasn't been brought into the project yet.</summary>
    public static bool ModelMissing
    {
        get
        {
            UpdateModel();
            return s_modelMissing;
        }
    }

    /// <summary>Copies the door's model from its rip, as the first use of a prop does. It takes a few seconds, so it's only on request.</summary>
    public static void ImportMissingModel()
    {
        RegionInfo info = InfoForActiveScene();
        string scene = info != null && !string.IsNullOrEmpty(info.DoorModelPath) ? info.DoorModelScene : DEFAULT_MODEL_SCENE;
        string path = info != null && !string.IsNullOrEmpty(info.DoorModelPath) ? info.DoorModelPath : DEFAULT_MODEL_PATH;
        if (FindPrefab(scene, path) != null)
        {
            Refresh();
            return;
        }

        string file;
        if (!OckSettings.RippedScenes().TryGetValue(scene, out file))
            return; // the marker stays a plain box until that scene is ripped
        try
        {
            EditorUtility.DisplayProgressBar("Construction Kit", "Bringing in the door's model from " + LocationNames.For(scene) + "...", 0.5f);
            RipScene rip = RipScene.Load(scene, file);
            string name = BaseName(path);
            RipScene.Prop prop = rip.Props.FirstOrDefault(p => p.BaseName == name);
            if (prop != null)
                PropImporter.Import(rip, prop);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        Refresh();
    }

    private static RegionInfo InfoForActiveScene()
    {
        string path = SceneManager.GetActiveScene().path;
        return string.IsNullOrEmpty(path) ? null : RegionInfo.For(path);
    }

    private static string BaseName(string scenePath)
    {
        return s_instanceSuffix.Replace(Path.GetFileName(scenePath), "");
    }

    private static GameObject FindPrefab(string scene, string path)
    {
        string folder = PropImporter.LIBRARY + "/" + scene;
        if (!AssetDatabase.IsValidFolder(folder))
            return null;
        string name = BaseName(path);
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            PropRef reference = PropRef.ReadFrom(prefabPath);
            if (reference != null && !reference.IsEnemy && BaseName(reference.Path) == name)
                return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }
        return null;
    }

    private static void UpdateModel()
    {
        Scene active = SceneManager.GetActiveScene();
        // A scene partway through loading has no objects to ask about, and an export's working copy doesn't need showing.
        if (!active.isLoaded || active.path.StartsWith(EXPORT_FOLDER))
        {
            s_markers = new List<Transform>();
            s_modelMissing = false;
            return;
        }
        if (s_markers == null)
            s_markers = Doors(active).ToList();
        if (s_markers.Count == 0)
        {
            s_modelMissing = false;
            return;
        }

        RegionInfo info = InfoForActiveScene();
        bool custom = info != null && !string.IsNullOrEmpty(info.DoorModelPath);
        string scene = custom ? info.DoorModelScene : DEFAULT_MODEL_SCENE;
        string path = custom ? info.DoorModelPath : DEFAULT_MODEL_PATH;
        Quaternion turn = custom ? Quaternion.Euler(info.DoorModelRotation) : Quaternion.identity;
        Vector3 offset = custom ? Vector3.zero : s_defaultModelPosition;
        float scale = custom ? info.DoorModelScale : 1f;
        string key = string.Join("|", new[] { scene, path, turn.ToString(), scale.ToString(CultureInfo.InvariantCulture) });
        if (key == s_modelKey)
            return;
        s_modelKey = key;

        MakeMarkersSeeThrough();
        s_parts.Clear();
        GameObject prefab = FindPrefab(scene, path);
        s_modelMissing = prefab == null;
        if (prefab == null)
            return;

        // As the plugin places it: its root takes the door's rotation and the model's own, whatever way it stood in its scene.
        Matrix4x4 fromDoor = Matrix4x4.TRS(offset, turn, Vector3.one * scale) * Matrix4x4.Scale(prefab.transform.localScale);
        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
        foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                s_parts.Add(new ModelPart { Mesh = filter.sharedMesh, Materials = renderer.sharedMaterials, FromDoor = fromDoor * (toRoot * renderer.transform.localToWorldMatrix) });
        }
    }

    private static void DrawModels(Camera camera)
    {
        if (camera.cameraType != CameraType.SceneView || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        UpdateModel();
        foreach (Transform marker in s_markers)
        {
            if (marker == null || !marker.gameObject.activeInHierarchy)
                continue;
            foreach (ModelPart part in s_parts)
            {
                if (part.Mesh == null)
                    continue;
                for (int i = 0; i < part.Mesh.subMeshCount && i < part.Materials.Length; i++)
                    if (part.Materials[i] != null)
                        Graphics.DrawMesh(part.Mesh, marker.localToWorldMatrix * part.FromDoor, part.Materials[i], marker.gameObject.layer, camera, i);
            }
        }
    }
}
