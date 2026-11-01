using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// What a region borrows from the vanilla game, read from a text file beside its bundle:
    ///   environment | scene
    ///   region | unique ID | name shown to players
    ///   door | vanilla scene | x,y,z | rotationY, for the entrance shipped with the region
    ///   enemy | scene | hierarchy path | x,y,z | rotationY
    ///   game | fingerprint of the game build it was made against
    ///   ground | scene | terrain layer name | diffuse texture | normal texture, or blank
    ///   water | scene | x,y,z | width,1,length | rotationY
    ///   returndoor | x,y,z | rotationY, replacing the automatic door beside the arrival point
    ///   prop | scene | hierarchy path | x,y,z | rotation, as x,y,z degrees or just y | optional check
    /// The check is "first drawn object's name:number of drawn objects", to catch a path finding the wrong object.
    /// Not JSON, because JsonUtility dropped the nested prop list when running from a plugin DLL.
    /// </summary>
    internal class RegionManifest
    {
        public class PropReference
        {
            public string scene;
            public string path;
            public Vector3 position;
            public Vector3 rotation;
            public string check;
        }

        public class EnemyReference
        {
            public string scene;
            public string path;
            public Vector3 position;
            public float rotationY;
        }

        public class GroundLayer
        {
            public string scene;
            public string layer;
            public string diffuse;
            public string normal;
        }

        public class WaterReference
        {
            public string scene;
            public Vector3 position;
            public Vector3 size;
            public float rotationY;
        }

        public class ReturnDoor
        {
            public Vector3 position;
            public float rotationY;
        }

        public List<ReturnDoor> returnDoors = new List<ReturnDoor>();
        public List<EnemyReference> enemies = new List<EnemyReference>();
        public List<GroundLayer> groundLayers = new List<GroundLayer>();
        public List<WaterReference> water = new List<WaterReference>();
        public string doorScene;
        public string doorPosition;
        public float doorRotationY;
        public string regionId;
        public string displayName;
        public string gameFingerprint;
        public string environmentDonor;
        public List<PropReference> props = new List<PropReference>();

        // Cache keys for things found by what they are, rather than by a hierarchy path.
        public const string WATER_KEY = "#water";
        public const string WATER_SOURCE_KEY = "#watersource";
        public const string ENVIRONMENT_KEY = "#environment";
        private const string TEXTURE_KEY_PREFIX = "#texture:";

        // Woken in this order: audio and sky exist by the time the camera looks for them.
        public static readonly string[] EnvironmentParts = { "AudioManager", ENVIRONMENT_KEY, "Main Camera" };

        public static string TextureKey(string name)
        {
            return TEXTURE_KEY_PREFIX + name;
        }

        public static bool IsTextureKey(string key, out string name)
        {
            bool isTexture = key.StartsWith(TEXTURE_KEY_PREFIX);
            name = isTexture ? key.Substring(TEXTURE_KEY_PREFIX.Length) : null;
            return isTexture;
        }

        /// <summary>The scenes that have to load for this region. A reference already cached needs no scene of its own.</summary>
        public IEnumerable<string> DonorScenes(System.Func<string, string, bool> isCached = null)
        {
            System.Func<string, string, bool> cached = isCached ?? ((scene, path) => false);
            return (EnvironmentParts.All(part => cached(environmentDonor, part)) ? new string[0] : new[] { environmentDonor }).Concat(props.Where(p => !cached(p.scene, p.path)).Select(p => p.scene)).Concat(enemies.Where(e => !cached(e.scene, e.path)).Select(e => e.scene))
                .Concat(groundLayers.Where(g => !cached(g.scene, TextureKey(g.diffuse)) || (g.normal.Length > 0 && !cached(g.scene, TextureKey(g.normal)))).Select(g => g.scene))
                .Concat(water.Where(w => !cached(w.scene, WATER_KEY)).Select(w => w.scene)).Where(s => !string.IsNullOrEmpty(s)).Distinct();
        }

        public static RegionManifest Load(string path)
        {
            if (!File.Exists(path))
            {
                KitPlugin.Log.LogError("Region manifest not found: " + path);
                return null;
            }

            RegionManifest manifest = Parse(File.ReadAllLines(path));
            KitPlugin.Log.LogMessage($"Manifest {Path.GetFileName(path)}: environment from '{manifest.environmentDonor}', {manifest.props.Count} props.");
            manifest.CheckGameBuild();
            return manifest;
        }

        internal static RegionManifest Parse(IEnumerable<string> lines)
        {
            var manifest = new RegionManifest();
            foreach (string line in lines)
            {
                string[] parts = line.Split('|').Select(p => p.Trim()).ToArray();
                Vector3 position;
                Vector3 rotation;
                float rotationY;
                if (parts[0] == "environment" && parts.Length == 2)
                    manifest.environmentDonor = parts[1];
                else if (parts[0] == "region" && parts.Length == 3)
                {
                    manifest.regionId = parts[1];
                    manifest.displayName = parts[2];
                }
                else if (parts[0] == "door" && parts.Length == 4
                    && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rotationY))
                {
                    manifest.doorScene = parts[1];
                    manifest.doorPosition = parts[2];
                    manifest.doorRotationY = rotationY;
                }
                else if (parts[0] == "enemy" && parts.Length == 5
                    && KitPlugin.TryParseVector(parts[3], out position)
                    && float.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rotationY))
                    manifest.enemies.Add(new EnemyReference { scene = parts[1], path = parts[2], position = position, rotationY = rotationY });
                else if (parts[0] == "returndoor" && parts.Length == 3
                    && KitPlugin.TryParseVector(parts[1], out position)
                    && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rotationY))
                    manifest.returnDoors.Add(new ReturnDoor { position = position, rotationY = rotationY });
                else if (parts[0] == "ground" && parts.Length == 5)
                    manifest.groundLayers.Add(new GroundLayer { scene = parts[1], layer = parts[2], diffuse = parts[3], normal = parts[4] });
                else if (parts[0] == "water" && parts.Length == 5
                    && KitPlugin.TryParseVector(parts[2], out position)
                    && KitPlugin.TryParseVector(parts[3], out rotation)
                    && float.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rotationY))
                    manifest.water.Add(new WaterReference { scene = parts[1], position = position, size = rotation, rotationY = rotationY });
                else if (parts[0] == "game" && parts.Length == 2)
                    manifest.gameFingerprint = parts[1];
                else if (parts[0] == "prop" && parts.Length >= 5
                    && KitPlugin.TryParseVector(parts[3], out position)
                    && TryParseRotation(parts[4], out rotation))
                    manifest.props.Add(new PropReference { scene = parts[1], path = parts[2], position = position, rotation = rotation, check = parts.Length > 5 ? parts[5] : null });
                else if (parts[0].Length > 0 && !parts[0].StartsWith("#"))
                    KitPlugin.Log.LogError("Can't read manifest line: " + line);
            }
            return manifest;
        }

        // Only a warning: an update needn't break anything, and each prop is checked individually anyway.
        private void CheckGameBuild()
        {
            string current = GameFingerprint();
            if (string.IsNullOrEmpty(gameFingerprint) || gameFingerprint == "unknown" || current == "unknown")
                return;
            if (current != gameFingerprint)
                KitPlugin.Log.LogWarning("This region was made for a different build of the game. If props are missing or wrong, it needs re-exporting.");
        }

        /// <summary>Identifies the running game's build. The exporter computes the same value.</summary>
        public static string GameFingerprint()
        {
            string file = Path.Combine(Application.dataPath, "globalgamemanagers");
            if (!File.Exists(file))
                return "unknown";
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", "");
        }

        private static bool TryParseRotation(string text, out Vector3 rotation)
        {
            if (KitPlugin.TryParseVector(text, out rotation))
                return true;
            float y;
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y))
                return false;
            rotation = new Vector3(0f, y, 0f);
            return true;
        }
    }
}
