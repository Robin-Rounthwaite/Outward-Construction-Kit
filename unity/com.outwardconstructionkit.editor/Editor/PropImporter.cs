using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Brings one vanilla prop from the rip into the project as a stand-in prefab, copying only what it
/// needs. Most props are static-batched, so their meshes are cut out of the combined mesh here.
/// Materials are rebuilt on the Standard shader, because ripped shaders are only placeholders.
/// </summary>
internal static partial class PropImporter
{
    public const string LIBRARY = "Assets/OCK/Library";

    public const string STAND_IN_SHADER = "OCK/StandIn";

    public static GameObject Import(RipScene scene, RipScene.Prop prop)
    {
        string folder = LIBRARY + "/" + scene.SceneName;
        if (prop.IsEnemy)
        {
            EnsureFolder(folder + "/Materials");
            return EnemyMarker.Import(folder, scene, prop);
        }

        string prefabPath = folder + "/" + Safe(prop.BaseName) + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        PropRef existingRef = existing != null ? PropRef.ReadFrom(prefabPath) : null;
        if (existingRef != null && existingRef.Format == PropRef.CURRENT_FORMAT)
            return existing;

        EnsureFolder(folder + "/Meshes");
        EnsureFolder(folder + "/Materials");
        EnsureFolder(folder + "/Textures");
        EnsureFolder(LIBRARY + "/_Combined");

        RipScene.Node source = prop.Node;
        Matrix4x4 sourceWorld = source.WorldMatrix;
        var root = new GameObject(prop.BaseName);
        root.transform.rotation = sourceWorld.rotation;
        root.transform.localScale = sourceWorld.lossyScale;
        Matrix4x4 toRoot = sourceWorld.inverse;

        foreach (RipScene.Node node in DrawnNodes(source))
        {
            Mesh mesh = node.SubMeshCount > 0
                ? CutFromCombined(node, folder)
                : CopyMesh(node.MeshGuid, folder, node.MeshFileId);
            if (mesh == null)
            {
                Debug.LogWarning($"[OCK] Part '{node.Name}' of {prop.BaseName} has a mesh that isn't in the rip, so the stand-in is missing that part. Mesh {node.MeshGuid}, file ID {node.MeshFileId}.");
                continue;
            }

            var child = new GameObject(node.Name);
            child.transform.SetParent(root.transform, false);
            Matrix4x4 local = toRoot * node.WorldMatrix;
            child.transform.localPosition = local.GetColumn(3);
            child.transform.localRotation = local.rotation;
            child.transform.localScale = local.lossyScale;
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>().sharedMaterials = node.MaterialGuids.Select(g => GetMaterial(g, folder)).ToArray();
        }

        foreach (RipScene.Node node in source.SelfAndDescendants().Where(n => n.Active && n.Colliders.Any(c => !c.IsTrigger)))
            AddColliders(node, root.transform, toRoot, folder);

        // Overwriting keeps the prefab's GUID, so scenes already using it stay connected.
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        new PropRef
        {
            DonorScene = scene.SceneName,
            Path = source.ScenePath,
            SourceRotation = sourceWorld.rotation,
            SourceScale = sourceWorld.lossyScale,
            Check = RipScene.CheckFor(source),
        }.SaveOn(prefabPath);
        return prefab;
    }

    private static void AddColliders(RipScene.Node node, Transform root, Matrix4x4 toRoot, string folder)
    {
        var holder = new GameObject(node.Name + "_Collider");
        holder.transform.SetParent(root, false);
        Matrix4x4 local = toRoot * node.WorldMatrix;
        holder.transform.localPosition = local.GetColumn(3);
        holder.transform.localRotation = local.rotation;
        holder.transform.localScale = local.lossyScale;

        foreach (RipScene.ColliderInfo info in node.Colliders.Where(c => !c.IsTrigger))
        {
            switch (info.ClassId)
            {
                case "65":
                    BoxCollider box = holder.AddComponent<BoxCollider>();
                    box.center = info.Center;
                    box.size = info.Size;
                    break;
                case "136":
                    CapsuleCollider capsule = holder.AddComponent<CapsuleCollider>();
                    capsule.center = info.Center;
                    capsule.radius = info.Radius;
                    capsule.height = info.Height;
                    capsule.direction = info.Direction;
                    break;
                case "135":
                    SphereCollider sphere = holder.AddComponent<SphereCollider>();
                    sphere.center = info.Center;
                    sphere.radius = info.Radius;
                    break;
                case "64":
                    // A collider built from a combined mesh would cover all its neighbours too.
                    string file = string.IsNullOrEmpty(info.MeshGuid) ? null : RipFile(info.MeshGuid);
                    if (file == null || Path.GetFileName(file).StartsWith("Combined Mesh"))
                        break;
                    MeshCollider meshCollider = holder.AddComponent<MeshCollider>();
                    meshCollider.convex = info.Convex;
                    meshCollider.sharedMesh = CopyMesh(info.MeshGuid, folder);
                    break;
            }
        }
    }

    // Only the most detailed version of each visible mesh: the game's own LODs take over at runtime.
    private static List<RipScene.Node> DrawnNodes(RipScene.Node source)
    {
        List<RipScene.Node> drawn = source.SelfAndDescendants()
            .Where(n => n.HasMesh && n.HasRenderer && n.RendererEnabled && n.Active)
            .Where(n => n.Name.IndexOf("collider", System.StringComparison.OrdinalIgnoreCase) < 0)
            .ToList();
        List<RipScene.Node> best = drawn.Where(n => !n.Name.Contains("_LOD") || n.Name.Contains("_LOD0")).ToList();
        return best.Count > 0 ? best : drawn;
    }

    private static string Safe(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    internal static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
            return;
        string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
    }
}
