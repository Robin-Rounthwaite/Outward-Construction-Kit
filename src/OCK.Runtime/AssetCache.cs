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
    /// Vanilla objects, built once from the player's own game files into small bundles on their PC, so a
    /// region can use them without loading the scenes they came from. Loaded on entering a region and let go on
    /// leaving. Anything that can't be built falls back to its donor scene, and a game update starts a new cache.
    /// </summary>
    internal static class AssetCache
    {
        private const string FOLDER = "OCK_Cache";
        private const string FAILED_SUFFIX = ".failed";
        private const string SHADER_FOLDER = "#shaders";

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
            s_root = Path.Combine(Path.Combine(Paths.BepInExRootPath, FOLDER), CacheName());
            RemoveOutdated();

            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                s_levelFiles[Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i))] = "level" + i;

            var wanted = Region.All.SelectMany(r => r.Manifest.props.Select(p => new { p.scene, p.path })
                .Concat(new[] { r.Manifest.doorModel ?? RegionManifest.DefaultDoorModel }.Select(d => new { d.scene, d.path }))
                .Concat(r.Manifest.enemies.Select(e => new { e.scene, e.path }))
                .Concat(RegionManifest.EnvironmentParts.Where(_ => !string.IsNullOrEmpty(r.Manifest.environmentDonor)).Select(part => new { scene = r.Manifest.environmentDonor, path = part }))
                .Concat(r.Manifest.water.Select(w => new { w.scene, path = RegionManifest.WATER_KEY }))
                .Concat(r.Manifest.water.Select(w => new { w.scene, path = RegionManifest.WATER_SOURCE_KEY }))
                .Concat(r.Manifest.groundLayers.Select(g => new { g.scene, path = RegionManifest.TextureKey(g.diffuse) }))
                .Concat(r.Manifest.groundLayers.Where(g => g.normal.Length > 0).Select(g => new { g.scene, path = RegionManifest.TextureKey(g.normal) }))).Distinct()
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
                    string textureName;
                    if (item.path == RegionManifest.ENVIRONMENT_KEY)
                        bytes += builder.BuildPruned(levelFile, EnvironmentDonor.ENVIRONMENT, EnvironmentDonor.KeepChildren, "Season", target).Bytes;
                    else if (item.path == RegionManifest.WATER_KEY)
                        bytes += builder.BuildScriptHolder(levelFile, "WaterRender", true, target).Bytes;
                    else if (item.path == RegionManifest.WATER_SOURCE_KEY)
                        bytes += builder.BuildScriptHolder(levelFile, "WaterSource", false, target).Bytes;
                    else if (RegionManifest.IsTextureKey(item.path, out textureName))
                        bytes += builder.BuildTexturePointer(levelFile, textureName, target).Bytes;
                    else
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

        // Named for the game build and the extractor, since a new version of either can build different bundles.
        private static string CacheName()
        {
            string game = RegionManifest.GameFingerprint();
            using (SHA1 sha = SHA1.Create())
            {
                string extractor = System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(typeof(BundleBuilder).Assembly.Location))).Replace("-", "");
                return (game.Length > 12 ? game.Substring(0, 12) : game) + "-" + extractor.Substring(0, 12);
            }
        }

        // Nothing can use these any more, and everything in them can be built again.
        private static void RemoveOutdated()
        {
            string parent = Path.GetDirectoryName(s_root);
            if (!Directory.Exists(parent))
                return;
            foreach (string folder in Directory.GetDirectories(parent).Where(f => f != s_root))
            {
                try
                {
                    Directory.Delete(folder, true);
                }
                catch (IOException e)
                {
                    KitPlugin.Log.LogWarning($"Couldn't remove the outdated cache {folder}: {e.Message}");
                }
            }
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

        /// <summary>A vanilla texture from the player's game files, or null if it isn't cached or won't load.</summary>
        public static Texture2D LoadTexture(string scene, string textureName)
        {
            string key = RegionManifest.TextureKey(textureName);
            return Has(scene, key) ? LoadPointer<Texture2D>(FileFor(scene, key), textureName) : null;
        }

        /// <summary>A game shader from the player's game files, found and cached the first time it's asked for. Null if it isn't there.</summary>
        public static Shader LoadShader(string shaderName)
        {
            if (s_root == null)
                return null;

            string file = FileFor(SHADER_FOLDER, shaderName);
            if (File.Exists(file + FAILED_SUFFIX))
                return null;
            if (!File.Exists(file))
            {
                try
                {
                    new BundleBuilder(Application.dataPath).BuildShaderPointer(shaderName, file);
                }
                catch (System.Exception e)
                {
                    KitPlugin.Log.LogWarning($"Couldn't find shader '{shaderName}': {e.Message}");
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllText(file + FAILED_SUFFIX, e.ToString());
                    return null;
                }
            }
            return LoadPointer<Shader>(file, shaderName);
        }

        private static T LoadPointer<T>(string file, string name) where T : Object
        {
            AssetBundle bundle;
            if (!s_open.TryGetValue(file, out bundle))
            {
                bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null)
                {
                    KitPlugin.Log.LogError($"Unity refused the cached pointer to '{name}'. Deleting the {FOLDER} folder rebuilds it.");
                    return null;
                }
                s_open[file] = bundle;
            }
            return bundle.LoadAsset<T>(BundleBuilder.POINTER_PATH);
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
