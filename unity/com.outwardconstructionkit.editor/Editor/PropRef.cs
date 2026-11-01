using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Which vanilla prop a library prefab stands for. It's kept in the prefab's import settings, so stand-ins
/// in a scene carry no scripts and are recognised by the prefab they came from.
/// </summary>
[Serializable]
internal class PropRef
{
    /// <summary>Raised when the importer changes what a library prefab contains, so older ones get rebuilt.</summary>
    public const int CURRENT_FORMAT = 5;

    private const float SCALE_TOLERANCE = 0.001f;

    public int Format;
    public string DonorScene;
    public string Path;
    public Quaternion SourceRotation;
    public Vector3 SourceScale;

    /// <summary>What the prop should look like, so a game update can't silently swap in a different object.</summary>
    public string Check;

    /// <summary>Enemies are placed as simple markers, because they're only ever references.</summary>
    public bool IsEnemy;

    public void SaveOn(string prefabPath)
    {
        Format = CURRENT_FORMAT;
        AssetImporter importer = AssetImporter.GetAtPath(prefabPath);
        importer.userData = JsonUtility.ToJson(this);
        importer.SaveAndReimport();
    }

    public static PropRef ReadFrom(string prefabPath)
    {
        AssetImporter importer = AssetImporter.GetAtPath(prefabPath);
        return importer != null && importer.userData.StartsWith("{") ? JsonUtility.FromJson<PropRef>(importer.userData) : null;
    }

    /// <summary>Every stand-in in a scene, with the vanilla prop it stands for.</summary>
    public static List<KeyValuePair<GameObject, PropRef>> FindIn(Scene scene)
    {
        var found = new List<KeyValuePair<GameObject, PropRef>>();
        foreach (GameObject root in scene.GetRootGameObjects())
            Collect(root.transform, found);
        return found;
    }

    /// <summary>
    /// A stand-in with changes can't be a reference, because the game would load the unchanged original.
    /// Moving, rotating and scaling evenly don't count, since a reference can do those. Stretching does, because
    /// a static-batched prop's geometry can only take one transform, which can't stretch along the prop's own axes.
    /// </summary>
    public static bool IsAltered(GameObject instance)
    {
        if (!PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false))
            return false;

        GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(instance);
        if (instance.GetComponentsInChildren<Component>(true).Length != prefab.GetComponentsInChildren<Component>(true).Length)
            return true;
        foreach (PropertyModification change in PrefabUtility.GetPropertyModifications(instance))
            if (!PrefabUtility.IsDefaultOverride(change) && !(change.target == prefab.transform && change.propertyPath.StartsWith("m_LocalScale")))
                return true;
        return !IsEvenScale(instance.transform.localScale, prefab.transform.localScale);
    }

    /// <summary>How much bigger an unaltered stand-in is than the original.</summary>
    public static float ScaleOf(GameObject instance)
    {
        return instance.transform.localScale.x / PrefabUtility.GetCorrespondingObjectFromSource(instance).transform.localScale.x;
    }

    private static bool IsEvenScale(Vector3 scale, Vector3 original)
    {
        float x = scale.x / original.x;
        return x > 0f && Mathf.Abs(scale.y / original.y - x) < x * SCALE_TOLERANCE && Mathf.Abs(scale.z / original.z - x) < x * SCALE_TOLERANCE;
    }

    private static void Collect(Transform t, List<KeyValuePair<GameObject, PropRef>> found)
    {
        PropRef reference = Of(t.gameObject);
        if (reference != null)
        {
            found.Add(new KeyValuePair<GameObject, PropRef>(t.gameObject, reference));
            return;
        }
        foreach (Transform child in t)
            Collect(child, found);
    }

    /// <summary>What a stand-in stands for, or null if it isn't one.</summary>
    public static PropRef Of(GameObject instance)
    {
        if (!PrefabUtility.IsOutermostPrefabInstanceRoot(instance))
            return null;
        string path = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(instance));
        if (string.IsNullOrEmpty(path) || !path.StartsWith(PropImporter.LIBRARY))
            return null;
        return ReadFrom(path);
    }
}
