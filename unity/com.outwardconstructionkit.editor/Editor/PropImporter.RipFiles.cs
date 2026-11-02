using System;
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
    private const string INDEX_FOLDER = "Library/OCK/RipCache";
    private static readonly string[] s_indexedFolders = { "Mesh", "Material", "Texture2D", "Shader" };

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

    /// <summary>Forgets where things are in the rips, so the next lookup goes through them again.</summary>
    public static void ForgetRipFiles()
    {
        s_ripFiles = null;
    }

    private static void BuildRipFileIndex(string roots)
    {
        using (OckTiming.Measure("Finding files in the rips"))
        {
            s_ripFilesRoot = roots;
            s_ripFiles = new Dictionary<string, string>();
            foreach (string root in OckSettings.RipRoots())
                foreach (KeyValuePair<string, string> found in IndexOf(root))
                    s_ripFiles[found.Key] = found.Value;
        }
    }

    // Reading every .meta takes seconds a rip, so what's found is kept, for as long as the rip's folders stay the same.
    private static Dictionary<string, string> IndexOf(string root)
    {
        string[] folders = s_indexedFolders.Select(f => Path.Combine(root, "Assets", f)).Where(Directory.Exists).ToArray();
        string stamp = root + "|" + string.Join("|", folders.Select(f => Directory.GetLastWriteTimeUtc(f).Ticks.ToString(CultureInfo.InvariantCulture)).ToArray());
        string rip = Path.GetFileName(Path.GetDirectoryName(root.TrimEnd('\\', '/')));
        string kept = Path.Combine(INDEX_FOLDER, (string.IsNullOrEmpty(rip) ? "rip" : rip) + ".files.txt");

        var index = new Dictionary<string, string>();
        if (File.Exists(kept))
        {
            string[] lines = File.ReadAllLines(kept);
            if (lines.Length > 0 && lines[0] == stamp)
            {
                foreach (string line in lines.Skip(1))
                {
                    int tab = line.IndexOf('\t');
                    index[line.Substring(0, tab)] = line.Substring(tab + 1);
                }
                return index;
            }
        }

        foreach (string folder in folders)
            foreach (string meta in Directory.GetFiles(folder, "*.meta"))
            {
                string guidLine = File.ReadLines(meta).Skip(1).First();
                index[guidLine.Replace("guid:", "").Trim()] = meta.Substring(0, meta.Length - 5);
            }

        try
        {
            Directory.CreateDirectory(INDEX_FOLDER);
            File.WriteAllLines(kept, new[] { stamp }.Concat(index.Select(i => i.Key + "\t" + i.Value)).ToArray());
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            // Not being able to keep it only means reading it again next time.
        }
        return index;
    }
}
