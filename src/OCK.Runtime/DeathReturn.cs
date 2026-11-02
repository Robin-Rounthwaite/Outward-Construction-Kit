using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// With no defeat scenario, the game brings a defeated party back by reloading the scene they died in, which in a
    /// region is beside whatever killed them. This sends them back to where they loaded in from, instead. The host alone
    /// decides that, and the game's own request takes everyone there together, because players can have come from
    /// different places: a guest who joined inside the region only knows its entrance.
    /// </summary>
    internal static class DeathReturn
    {
        private const string FALLBACK_SCENE = "CierzoNewTerrain";
        private const string TRANSITION_SCENE = "LowMemory_TransitionScene";
        private const string MENU_SCENE_PREFIX = "MainMenu";
        private const string DEFEAT_TEXT = "You were injured badly, and left the space to nurse yourself to health.";

        private static string s_cameFrom;
        private static bool s_returning;
        private static float s_vitalsAfterArrival = -1f;

        public static void Patch(Harmony harmony)
        {
            harmony.PatchAll(typeof(FailSafePatch));
            harmony.PatchAll(typeof(ReloadPatch));
        }

        /// <summary>Where a defeat leads, given what the region's modder asked for. Nothing asked for means where they came from.</summary>
        internal static string Destination(RegionManifest.DefeatSetup setup, string cameFrom, string entranceScene, string regionScene)
        {
            if (setup != null && setup.returnTo == RegionManifest.DefeatSetup.RETURN_HERE)
                return regionScene;
            return setup != null && !string.IsNullOrEmpty(setup.returnTo) ? setup.returnTo : Destination(cameFrom, entranceScene);
        }

        /// <summary>How many of this many things a percentage takes, rounding down, so a small loss can't take everything.</summary>
        internal static int HowManyToLose(int count, int percent)
        {
            return count * UnityEngine.Mathf.Clamp(percent, 0, 100) / 100;
        }

        // Each player's game sees to its own players, because a guest's belongings aren't the host's to change.
        private static void ApplyPenalties(RegionManifest.DefeatSetup setup)
        {
            if (setup == null)
                return;
            s_vitalsAfterArrival = setup.vitalsPercent > 0 ? setup.vitalsPercent / 100f : -1f;
            foreach (Character player in CharacterManager.Instance.Characters.Values.Where(c => c != null && c.IsLocalPlayer))
            {
                try
                {
                    if (setup.silverPercent > 0)
                        player.Inventory.RemoveMoneyPercent(setup.silverPercent);
                    if (setup.itemPercent <= 0)
                        continue;
                    List<Item> carried = player.Inventory.Pouch.GetContainedItems().ToList();
                    if (player.Inventory.EquippedBag != null)
                        carried.AddRange(player.Inventory.EquippedBag.Container.GetContainedItems());
                    int lose = HowManyToLose(carried.Count, setup.itemPercent);
                    foreach (Item item in carried.OrderBy(_ => UnityEngine.Random.value).Take(lose))
                        ItemManager.Instance.DestroyItem(item.UID);
                    KitPlugin.Log.LogMessage($"The defeat cost {player.Name} {lose} of {carried.Count} carried items.");
                }
                catch (System.Exception e)
                {
                    KitPlugin.Log.LogError("Couldn't apply a defeat's penalties: " + e);
                }
            }
        }

        /// <summary>Once the return has finished loading, leaves players as hurt as the region asked for.</summary>
        internal static void OnLevelReady()
        {
            if (s_vitalsAfterArrival < 0f)
                return;
            float share = s_vitalsAfterArrival;
            s_vitalsAfterArrival = -1f;
            foreach (Character player in CharacterManager.Instance.Characters.Values.Where(c => c != null && c.IsLocalPlayer))
            {
                player.Stats.SetHealth(player.Stats.ActiveMaxHealth * share);
                player.Stats.UseStamina((Tag[])null, player.Stats.CurrentStamina * (1f - share));
            }
        }

        /// <summary>Notes each vanilla scene as it loads, since that's where the next region is entered from.</summary>
        public static void OnSceneLoaded(string scene)
        {
            if (Region.ForScene(scene) == null && scene != TRANSITION_SCENE && !scene.StartsWith(MENU_SCENE_PREFIX))
                s_cameFrom = scene;
        }

        // Without having come from anywhere this session, such as after loading a save made inside it, it's the entrance.
        internal static string Destination(string cameFrom, string entranceScene)
        {
            return !string.IsNullOrEmpty(cameFrom) ? cameFrom : !string.IsNullOrEmpty(entranceScene) ? entranceScene : FALLBACK_SCENE;
        }

        [HarmonyPatch(typeof(DefeatScenariosManager), nameof(DefeatScenariosManager.FailSafeDefeat))]
        private static class FailSafePatch
        {
            private static void Prefix()
            {
                Region region = Region.ForScene(SceneManager.GetActiveScene().name);
                bool inRegion = region != null;
                s_returning = !PhotonNetwork.isNonMasterClientInRoom && inRegion;
                if (PhotonNetwork.isNonMasterClientInRoom && inRegion)
                {
                    ApplyPenalties(region.Manifest.defeat);
                    ShowDefeatScreen(null, region.Manifest.defeat); // the host's choice moves everyone, whether or not a guest has read this yet
                }
            }
        }

        [HarmonyPatch(typeof(NetworkLevelLoader), nameof(NetworkLevelLoader.RequestReloadLevel))]
        private static class ReloadPatch
        {
            private static bool Prefix(NetworkLevelLoader __instance)
            {
                if (!s_returning)
                    return true;
                s_returning = false;

                string scene = SceneManager.GetActiveScene().name;
                Region region = Region.ForScene(scene);
                if (region == null)
                    return true;
                RegionManifest.DefeatSetup setup = region.Manifest.defeat;
                string destination = Destination(setup, s_cameFrom, region.EntranceScene, scene);
                // The door's own spawn is beside it, and that only exists in the scene the door is in.
                int spawnPoint = destination == scene && setup != null ? setup.spawnIndex
                    : destination == region.EntranceScene && region.ReturnSpawnIndex.Value >= 0 ? region.ReturnSpawnIndex.Value : 0;
                KitPlugin.Log.LogMessage($"Defeated in {scene}, so returning to {destination}, at spawn {spawnPoint}.");
                ApplyPenalties(setup);
                ShowDefeatScreen(() => __instance.RequestSwitchArea(destination, spawnPoint), setup);
                return false;
            }
        }

        // The game's own context screen, which waits for a key press. A failure here mustn't strand anyone in a region.
        private static void ShowDefeatScreen(UnityEngine.Events.UnityAction then, RegionManifest.DefeatSetup setup)
        {
            try
            {
                string text = setup != null && !string.IsNullOrEmpty(setup.message) ? setup.message : DEFEAT_TEXT;
                MenuManager.Instance.ShowProloguePanel(new[] { new EventContextData.ContextScreen { DefaultText = text } }, then);
            }
            catch (System.Exception e)
            {
                KitPlugin.Log.LogError("Couldn't show the defeat screen: " + e);
                then?.Invoke();
            }
        }
    }
}
