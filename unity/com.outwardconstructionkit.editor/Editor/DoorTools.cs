using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Return doors are markers here: export lists where they are, and the plugin builds a real door at each. A region
/// with none gets one automatically, beside the arrival point.
/// </summary>
internal static class DoorTools
{
    private const string MARKER_NAME = "OCK Return Door";
    private static readonly Vector3 s_doorSize = new Vector3(1.6f, 2.8f, 0.2f); // as the plugin builds it

    [MenuItem("Construction Kit/Add Return Door")]
    public static void AddFromMenu()
    {
        Selection.activeGameObject = Add();
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
        string path = folder + "/ReturnDoorMarker.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard")) { color = new Color(0.25f, 0.45f, 0.75f) };
            AssetDatabase.CreateAsset(material, path);
        }
        return material;
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
}
