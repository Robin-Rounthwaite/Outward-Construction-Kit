using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Finding files in the rips by GUID.
internal static partial class PropImporter
{
    private static Dictionary<string, string> s_ripFiles;
    private static string s_ripFilesRoot;

    // The .meta goes too, so the asset keeps the GUID it has in the rip.
    private static string CopyWithMeta(string file, string folder)
    {
        string path = folder + "/" + Path.GetFileName(file);
        if (!File.Exists(path))
        {
            File.Copy(file + ".meta", path + ".meta");
            File.Copy(file, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
        return path;
    }

    private static string RipFile(string guid)
    {
        string roots = string.Join("|", OckSettings.RipRoots().ToArray());
        if (s_ripFiles == null || s_ripFilesRoot != roots)
            BuildRipFileIndex(roots);
        string file;
        return s_ripFiles.TryGetValue(guid, out file) ? file : null;
    }

    private static void BuildRipFileIndex(string roots)
    {
        s_ripFilesRoot = roots;
        s_ripFiles = new Dictionary<string, string>();
        foreach (string root in OckSettings.RipRoots())
            foreach (string folder in new[] { "Mesh", "Material", "Texture2D", "Shader" })
            {
                string dir = Path.Combine(root, "Assets", folder);
                if (!Directory.Exists(dir))
                    continue;
                foreach (string meta in Directory.GetFiles(dir, "*.meta"))
                {
                    string guidLine = File.ReadLines(meta).Skip(1).First();
                    s_ripFiles[guidLine.Replace("guid:", "").Trim()] = meta.Substring(0, meta.Length - 5);
                }
            }
    }
}
