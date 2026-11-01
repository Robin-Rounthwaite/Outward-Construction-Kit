using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using OutwardConstructionKit.Extractor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Vanilla props and enemies, built once from the player's own game files into small bundles on their PC, so a
    /// region can use them without loading the scenes they came from. Loaded on entering a region and let go on
    /// leaving. Anything that can't be built falls back to its donor scene, and a game update starts a new cache.
    /// </summary>
    internal static class AssetCache
    {
        private const string FOLDER = "OCK_Cache";
        private const string FAILED_SUFFIX = ".failed";

        private static string s_root;
        private static readonly Dictionary<string, string> s_levelFiles = new Dictionary<string, string>();
        private static readonly Dictionary<string, AssetBundle> s_open = new Dictionary<string, AssetBundle>();
        private static readonly Dictionary<string, GameObject> s_prefabs = new Dictionary<string, GameObject>();

        private static string FileFor(string scene, string path)
        {
            using (SHA1 sha = SHA1.Create())
            {
                string hash = System.BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(scene + "|" + path))).Replace("-", "").ToLowerInvariant();
                return Path.Combine(Path.Combine(s_root, scene), hash);
            }
        }

        /// <summary>True if this reference can come from the cache, so its scene needn't load for it.</summary>
        public static bool Has(string scene, string path)
        {
            return s_root != null && File.Exists(FileFor(scene, path));
        }

        /// <summary>Builds whatever installed regions need that isn't cached yet. Slow only the first time.</summary>
        public static void BuildMissing()
        {
            if (!KitPlugin.Instance.UseCache.Value)
                return;

            string gameData = Application.dataPath;
            string fingerprint = RegionManifest.GameFingerprint();
            s_root = Path.Combine(Path.Combine(Paths.BepInExRootPath, FOLDER), fingerprint.Length > 12 ? fingerprint.Substring(0, 12) : fingerprint);

            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                s_levelFiles[Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i))] = "level" + i;

            var wanted = Region.All.SelectMany(r => r.Manifest.props.Select(p => new { p.scene, p.path })
                .Concat(r.Manifest.enemies.Select(e => new { e.scene, e.path }))).Distinct()
                .Where(w => !File.Exists(FileFor(w.scene, w.path)) && !File.Exists(FileFor(w.scene, w.path) + FAILED_SUFFIX)).ToList();
            if (wanted.Count == 0)
                return;

            var timer = System.Diagnostics.Stopwatch.StartNew();
            int built = 0;
            int failed = 0;
            long bytes = 0;
            var builder = new BundleBuilder(gameData);
            foreach (var item in wanted)
            {
                string target = FileFor(item.scene, item.path);
                try
                {
                    string levelFile;
                    if (!s_levelFiles.TryGetValue(item.scene, out levelFile))
                        throw new FileNotFoundException("No scene named " + item.scene);
                    bytes += builder.Build(levelFile, item.path, target).Bytes;
                    built++;
                }
                catch (System.Exception e)
                {
                    failed++;
                    KitPlugin.Log.LogWarning($"Couldn't cache '{item.path}' from {item.scene}, so it'll come from its donor scene: {e.Message}");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllText(target + FAILED_SUFFIX, e.ToString());
                }
            }
            KitPlugin.Log.LogMessage($"Asset cache: built {built} in {timer.ElapsedMilliseconds} ms, {bytes / 1024} KB, and {failed} couldn't be built. It's only built once per game version.");
        }

        /// <summary>The object as it was in its level, asleep, or null if it isn't cached or won't load.</summary>
        public static GameObject Load(string scene, string path)
        {
            if (!Has(scene, path))
                return null;

            string file = FileFor(scene, path);
            GameObject prefab;
            if (s_prefabs.TryGetValue(file, out prefab))
                return prefab;

            AssetBundle bundle = AssetBundle.LoadFromFile(file);
            if (bundle == null)
            {
                KitPlugin.Log.LogError($"Unity refused the cached copy of '{path}'. Deleting the {FOLDER} folder rebuilds it.");
                return null;
            }
            prefab = bundle.LoadAsset<GameObject>(BundleBuilder.PREFAB_PATH);
            s_open[file] = bundle;
            s_prefabs[file] = prefab;
            return prefab;
        }

        /// <summary>Lets go of everything loaded, once the region using it has gone.</summary>
        public static void ReleaseAll()
        {
            foreach (AssetBundle bundle in s_open.Values)
                if (bundle != null)
                    bundle.Unload(true);
            s_open.Clear();
            s_prefabs.Clear();
        }
    }
}
