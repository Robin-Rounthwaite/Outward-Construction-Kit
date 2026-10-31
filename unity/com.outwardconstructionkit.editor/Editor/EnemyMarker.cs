using UnityEditor;
using UnityEngine;

/// <summary>Library prefabs standing for vanilla enemies. A red capsule shows where one will spawn and its facing.</summary>
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
}
