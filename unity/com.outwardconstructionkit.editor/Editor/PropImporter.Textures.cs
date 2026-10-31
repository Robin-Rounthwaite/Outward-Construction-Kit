using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Copying textures from the rip, and the two quality modes they can be imported in.
internal static partial class PropImporter
{
    private const int FAST_TEXTURE_SIZE = 1024;
    private const int FULL_TEXTURE_SIZE = 2048;

    private static void CopyTextureIfNeeded(string guid, string folder, bool isNormalMap)
    {
        string existing = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(existing) || AssetDatabase.LoadAssetAtPath<Texture>(existing) == null)
            CopyTexture(guid, folder, isNormalMap);
    }

    /// <summary>
    /// Fast mode's textures are for display only. Anything shipping in a mod needs full quality, so
    /// this raises them at export, which is where that cost now falls.
    /// </summary>
    public static void EnsureFullQuality(Material material)
    {
        foreach (string dependency in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(material), true))
            if (AssetDatabase.GetMainAssetTypeAtPath(dependency) == typeof(Texture2D))
                ApplyTextureMode(dependency, false);
    }

    /// <summary>Applies the chosen mode to every imported stand-in texture.</summary>
    public static void ApplyTextureModeToLibrary()
    {
        string[] textures = AssetDatabase.FindAssets("t:Texture2D", new[] { LIBRARY });
        try
        {
            for (int i = 0; i < textures.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(textures[i]);
                EditorUtility.DisplayProgressBar("Construction Kit", "Updating " + Path.GetFileName(path), (float)i / textures.Length);
                ApplyTextureMode(path, OckSettings.FastImport);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void ApplyTextureMode(string path, bool fast)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return;
        TextureImporterCompression compression = fast ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
        int size = fast ? FAST_TEXTURE_SIZE : FULL_TEXTURE_SIZE;
        if (importer.textureCompression == compression && importer.maxTextureSize == size)
            return;
        importer.textureCompression = compression;
        importer.maxTextureSize = size;
        importer.SaveAndReimport();
    }

    private static Texture CopyTexture(string guid, string folder, bool isNormalMap)
    {
        string file = RipFile(guid);
        if (file == null)
            return null;

        string existing = AssetDatabase.GUIDToAssetPath(guid);
        string path = !string.IsNullOrEmpty(existing) && File.Exists(existing) ? existing : CopyTextureWithMode(file, folder + "/Textures");
        if (isNormalMap)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture>(path);
    }

    // Compressing textures is most of an import's cost, so fast mode settles this in the .meta before the first import.
    private static string CopyTextureWithMode(string file, string folder)
    {
        string path = folder + "/" + Path.GetFileName(file);
        if (!OckSettings.FastImport || File.Exists(path))
            return CopyWithMeta(file, folder);

        string meta = File.ReadAllText(file + ".meta");
        meta = Regex.Replace(meta, @"textureCompression: \d+", "textureCompression: 0");
        meta = Regex.Replace(meta, @"maxTextureSize: \d+", "maxTextureSize: " + FAST_TEXTURE_SIZE);
        File.WriteAllText(path + ".meta", meta);
        File.Copy(file, path);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        return path;
    }

    /// <summary>Brings in a vanilla texture that no prop has asked for yet, such as a ground texture.</summary>
    public static Texture2D ImportTextureByName(string sceneFolder, string name, bool isNormalMap)
    {
        foreach (string root in OckSettings.RipRoots())
        {
            string meta = Path.Combine(root, "Assets", "Texture2D", name + ".png.meta");
            if (!File.Exists(meta))
                continue;
            string folder = LIBRARY + "/" + sceneFolder;
            EnsureFolder(folder + "/Textures");
            string guid = File.ReadLines(meta).Skip(1).First().Replace("guid:", "").Trim();
            return CopyTexture(guid, folder, isNormalMap) as Texture2D;
        }
        throw new FileNotFoundException("Texture " + name + " isn't in any rip.");
    }
}
