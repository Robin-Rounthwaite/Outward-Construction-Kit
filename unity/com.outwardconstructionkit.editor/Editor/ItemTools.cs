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
/// Items are markers here, each named with its game item ID: export lists where they are, and the plugin creates a real
/// item at each that players can pick up. The list of items comes from the game, which writes it for the kit when it
/// starts with the plugin installed, because their names only exist there.
/// </summary>
internal static class ItemTools
{
    public class Entry
    {
        public int Id;
        public string Type;
        public string PrefabName;
        public string Name;
    }

    private const string CATALOG_FILE = "OCK_Items.txt";
    private const float MARKER_SIZE = 0.4f;

    private static readonly Regex s_markerName = new Regex(@"^OCK Item (\d+)\b");
    private static readonly Regex s_itemInRip = new Regex(@"^(\d{6,8})_");

    private static List<Entry> s_catalog = new List<Entry>();
    private static string s_catalogPath;
    private static System.DateTime s_catalogTime;

    public static string CatalogPath
    {
        get { return Path.Combine(Path.GetDirectoryName(OckSettings.PluginsFolder.TrimEnd('\\', '/')), CATALOG_FILE); }
    }

    /// <summary>The game's items, read again only if the file has changed. Empty until the game has run with the plugin.</summary>
    public static List<Entry> Catalog()
    {
        string path = string.IsNullOrEmpty(OckSettings.PluginsFolder) ? null : CatalogPath;
        if (path == null || !File.Exists(path))
            return new List<Entry>();
        System.DateTime time = File.GetLastWriteTimeUtc(path);
        if (path != s_catalogPath || time != s_catalogTime)
        {
            s_catalog = ParseCatalog(File.ReadAllLines(path));
            s_catalogPath = path;
            s_catalogTime = time;
        }
        return s_catalog;
    }

    public static List<Entry> ParseCatalog(IEnumerable<string> lines)
    {
        var entries = new List<Entry>();
        foreach (string line in lines)
        {
            string[] parts = line.Split('|').Select(p => p.Trim()).ToArray();
            int id;
            if (parts.Length == 4 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                entries.Add(new Entry { Id = id, Type = parts[1], PrefabName = parts[2], Name = parts[3].Length > 0 ? parts[3] : parts[2] });
        }
        return entries;
    }

    /// <summary>The item ID of a prop taken from a rip that's really an item, such as a cooking pot lying in a scene.</summary>
    public static bool TryGetItemId(string scenePath, out int id)
    {
        id = 0;
        string name = scenePath.Substring(scenePath.LastIndexOf('/') + 1);
        Match match = s_itemInRip.Match(name);
        // The game's display models end in _v, and are only models: there's nothing to pick up.
        return match.Success && !name.EndsWith("_v") && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }

    public static GameObject AddMarker(Entry item, Vector3 position)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "OCK Item " + item.Id + " " + item.Name;
        Object.DestroyImmediate(marker.GetComponent<Collider>());
        marker.transform.position = position + Vector3.up * MARKER_SIZE / 2f;
        marker.transform.localScale = Vector3.one * MARKER_SIZE;
        marker.GetComponent<Renderer>().sharedMaterial = GetMaterial();
        Undo.RegisterCreatedObjectUndo(marker, "Add " + item.Name);
        EditorSceneManager.MarkSceneDirty(marker.scene);
        return marker;
    }

    private static Material GetMaterial()
    {
        string folder = PropImporter.LIBRARY + "/Markers";
        PropImporter.EnsureFolder(folder);
        string path = folder + "/ItemMarker.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard")) { color = new Color(0.95f, 0.6f, 0.15f) };
            AssetDatabase.CreateAsset(material, path);
        }
        return material;
    }

    private static List<GameObject> Markers(Scene scene)
    {
        return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => s_markerName.IsMatch(t.name)).Select(t => t.gameObject).ToList();
    }

    public static string ManifestLine(int id, Vector3 position, Vector3 rotation, bool once = false)
    {
        var ci = CultureInfo.InvariantCulture;
        return "item | " + id + " | " + position.x.ToString("0.###", ci) + "," + position.y.ToString("0.###", ci) + "," + position.z.ToString("0.###", ci)
            + " | " + rotation.x.ToString("0.###", ci) + "," + rotation.y.ToString("0.###", ci) + "," + rotation.z.ToString("0.###", ci) + (once ? " | once" : "");
    }

    /// <summary>Whether this is one of the markers that becomes a real item.</summary>
    public static bool IsMarker(GameObject thing)
    {
        return thing != null && s_markerName.IsMatch(thing.name);
    }

    public static void AppendManifest(Scene scene, StringBuilder text, RegionInfo info)
    {
        foreach (GameObject marker in Markers(scene))
            text.Append(ManifestLine(int.Parse(s_markerName.Match(marker.name).Groups[1].Value, CultureInfo.InvariantCulture), marker.transform.position - Vector3.up * MARKER_SIZE / 2f, marker.transform.eulerAngles, info.StaysGone(marker))).Append('\n');
        foreach (KeyValuePair<GameObject, PropRef> prop in PropRef.FindIn(scene).Where(p => IsItemProp(p)).OrderBy(p => p.Key.name))
        {
            int id;
            TryGetItemId(prop.Value.Path, out id);
            Vector3 turn = (prop.Key.transform.rotation * Quaternion.Inverse(prop.Value.SourceRotation)).eulerAngles;
            text.Append(ManifestLine(id, prop.Key.transform.position, turn, info.StaysGone(prop.Key))).Append('\n');
        }
    }

    /// <summary>A prop from a rip that becomes a real item. One stretched into another shape stays a decoration.</summary>
    public static bool IsItemProp(KeyValuePair<GameObject, PropRef> prop)
    {
        int id;
        return !prop.Value.IsEnemy && !prop.Value.IsWorking && !PropRef.IsAltered(prop.Key) && TryGetItemId(prop.Value.Path, out id);
    }

    public static void RemoveMarkers(Scene work)
    {
        foreach (GameObject marker in Markers(work))
            Object.DestroyImmediate(marker);
    }
}
