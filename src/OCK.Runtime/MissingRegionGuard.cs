using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// A save made inside a region names that region's scene. If the region has since been uninstalled or can no longer
    /// load, loading the save would wait forever, so the character goes to where they entered it instead.
    /// That's remembered from launch to launch, since a removed region can't say. It needs the kit itself installed.
    /// </summary>
    internal static class MissingRegionGuard
    {
        private const string SCENE_PREFIX = "OCK_";
        private const string FILE = "OCK_RegionEntrances.txt";
        private const string FALLBACK_SCENE = "CierzoNewTerrain";

        private const float ARRIVAL_HEIGHT = 0.5f;

        private static readonly Dictionary<string, string> s_entrances = new Dictionary<string, string>();
        private static bool s_arrivingElsewhere;

        private static string FilePath
        {
            get { return Path.Combine(Paths.BepInExRootPath, FILE); }
        }

        public static void Patch(Harmony harmony)
        {
            harmony.PatchAll(typeof(ChooseInstancePatch));
            harmony.PatchAll(typeof(CharacterClickedPatch));
            harmony.PatchAll(typeof(LoadPositionPatch));
        }

        /// <summary>Notes where each installed region is entered from, adding to what earlier launches knew.</summary>
        public static void Remember()
        {
            if (File.Exists(FilePath))
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length == 2)
                        s_entrances[parts[0]] = parts[1];
                }
            foreach (Region region in Region.All.Where(r => !string.IsNullOrEmpty(r.EntranceScene)))
                s_entrances[region.SceneName] = region.EntranceScene;
            File.WriteAllLines(FilePath, s_entrances.Select(e => e.Key + "|" + e.Value).ToArray());
        }

        internal static string Replacement(string scene, IEnumerable<string> installed, IDictionary<string, string> entrances)
        {
            if (string.IsNullOrEmpty(scene) || !scene.StartsWith(SCENE_PREFIX) || installed.Contains(scene))
                return null;
            string entrance;
            return entrances.TryGetValue(scene, out entrance) && !installed.Contains(entrance) && entrance.StartsWith(SCENE_PREFIX) ? FALLBACK_SCENE
                : entrance ?? FALLBACK_SCENE;
        }

        private static void Check(SaveInstance instance)
        {
            if (instance == null || instance.CharSave == null || instance.CharSave.PSave == null)
                return;
            PlayerSaveData save = instance.CharSave.PSave;
            string replacement = Replacement(save.AreaName, Region.All.Where(r => r.CanLoad).Select(r => r.SceneName), s_entrances);
            if (replacement == null)
                return;
            KitPlugin.Log.LogWarning($"'{save.Name}' was saved in {save.AreaName}, which isn't installed any more, so they'll arrive in {replacement}.");
            save.AreaName = replacement;
            s_arrivingElsewhere = true;
        }

        // The save's position was in the region. Used here it's somewhere off the map, and the game's failsafe keeps
        // restarting the scene, so this character starts from the scene's first spawn point instead.
        [HarmonyPatch(typeof(Character), "CheckLoadPosition")]
        private static class LoadPositionPatch
        {
            private static bool Prefix(Character __instance)
            {
                if (!s_arrivingElsewhere || !__instance.IsLocalPlayer)
                    return true;
                SpawnPointManager spawns = SpawnPointManager.Instance;
                if (spawns == null || spawns.SpawnPoints == null || spawns.SpawnPoints.Length == 0)
                    return true;
                s_arrivingElsewhere = false;
                __instance.transform.position = spawns.SpawnPoints[0].position + Vector3.up * ARRIVAL_HEIGHT;
                __instance.transform.forward = spawns.SpawnPoints[0].forward;
                KitPlugin.Log.LogMessage($"Started '{__instance.Name}' at {__instance.transform.position}, this scene's first spawn point.");
                return false;
            }
        }

        [HarmonyPatch(typeof(CharacterSaveInstanceHolder), nameof(CharacterSaveInstanceHolder.ChooseCharacterSaveInstance))]
        private static class ChooseInstancePatch
        {
            private static void Postfix(CharacterSaveInstanceHolder __instance)
            {
                Check(__instance.CurrentSaveInstance);
            }
        }

        [HarmonyPatch(typeof(CharacterSelectionPanel), "OnCharacterClicked", new[] { typeof(CharacterSaveInstanceHolder) })]
        private static class CharacterClickedPatch
        {
            private static void Prefix(CharacterSaveInstanceHolder _character)
            {
                if (_character != null)
                    Check(_character.MostRecentInstance);
            }
        }
    }
}
