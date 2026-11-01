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

        private static string s_cameFrom;
        private static bool s_returning;

        public static void Patch(Harmony harmony)
        {
            harmony.PatchAll(typeof(FailSafePatch));
            harmony.PatchAll(typeof(ReloadPatch));
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
                s_returning = !PhotonNetwork.isNonMasterClientInRoom && Region.ForScene(SceneManager.GetActiveScene().name) != null;
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
                string destination = Destination(s_cameFrom, region.EntranceScene);
                // The door's own spawn is beside it, and that only exists in the scene the door is in.
                int spawnPoint = destination == region.EntranceScene && region.ReturnSpawnIndex.Value >= 0 ? region.ReturnSpawnIndex.Value : 0;
                KitPlugin.Log.LogMessage($"Defeated in {scene}, so returning to {destination}, at spawn {spawnPoint}.");
                __instance.RequestSwitchArea(destination, spawnPoint);
                return false;
            }
        }
    }
}
