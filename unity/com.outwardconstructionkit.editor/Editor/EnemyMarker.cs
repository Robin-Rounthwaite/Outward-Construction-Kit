using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Library prefabs standing for vanilla enemies, shown as their real model standing in its default pose, without
/// bones or animation. An enemy whose model isn't in the rip gets a red capsule, with a box showing its facing.
/// Either way they're only for the editor: export removes them, and the game brings the real enemy.
/// </summary>
internal static class EnemyMarker
{
    public static GameObject Import(string libraryFolder, RipScene scene, RipScene.Prop enemy)
    {
        string path = libraryFolder + "/Enemy_" + enemy.BaseName + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        PropRef existingRef = existing != null ? PropRef.ReadFrom(path) : null;
        if (existingRef != null && existingRef.Format == PropRef.CURRENT_FORMAT)
            return existing;

        var root = new GameObject("Enemy_" + enemy.BaseName);
        if (!AddModel(root, libraryFolder, enemy))
            AddCapsule(root, libraryFolder);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        new PropRef
        {
            DonorScene = scene.SceneName,
            Path = enemy.Node.ScenePath,
            SourceRotation = Quaternion.identity,
            SourceScale = Vector3.one,
            IsEnemy = true,
        }.SaveOn(path);
        return prefab;
    }

    private static bool AddModel(GameObject root, string libraryFolder, RipScene.Prop enemy)
    {
        PropImporter.EnsureFolder(libraryFolder + "/Meshes");
        PropImporter.EnsureFolder(libraryFolder + "/Textures");

        // Placed in the game facing any way, so its parts are kept relative to its own position and turn only.
        Matrix4x4 world = enemy.Node.WorldMatrix;
        Matrix4x4 toRoot = Matrix4x4.TRS(world.GetColumn(3), world.rotation, Vector3.one).inverse;
        bool any = false;
        foreach (RipScene.Node part in VisibleParts(enemy.Node))
        {
            Mesh mesh = PropImporter.CopyMesh(part.SkinnedMeshGuid, libraryFolder, part.SkinnedMeshFileId);
            if (mesh == null)
                continue;

            var child = new GameObject(part.Name);
            child.transform.SetParent(root.transform, false);
            Matrix4x4 local = toRoot * part.WorldMatrix;
            child.transform.localPosition = local.GetColumn(3);
            child.transform.localRotation = local.rotation;
            child.transform.localScale = local.lossyScale;
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>().sharedMaterials = part.SkinnedMaterialGuids.Select(g => PropImporter.GetMaterial(g, libraryFolder)).ToArray();
            any = true;
        }
        return any;
    }

    // Enemies carry spare outfits and weapons switched off, so only what's switched on counts.
    private static IEnumerable<RipScene.Node> VisibleParts(RipScene.Node node)
    {
        if (!node.Active)
            yield break;
        if (node.SkinnedMeshGuid != null && node.SkinnedEnabled)
            yield return node;
        foreach (RipScene.Node child in node.Children)
            foreach (RipScene.Node part in VisibleParts(child))
                yield return part;
    }

    private static void AddCapsule(GameObject root, string libraryFolder)
    {
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Marker";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.up;

        var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "Facing";
        Object.DestroyImmediate(nose.GetComponent<Collider>());
        nose.transform.SetParent(root.transform, false);
        nose.transform.localPosition = new Vector3(0f, 1.5f, 0.55f);
        nose.transform.localScale = new Vector3(0.25f, 0.25f, 0.5f);

        string materialPath = libraryFolder + "/Materials/_EnemyMarker.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard")) { color = new Color(0.8f, 0.15f, 0.1f) };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        body.GetComponent<Renderer>().sharedMaterial = material;
        nose.GetComponent<Renderer>().sharedMaterial = material;
    }
}
