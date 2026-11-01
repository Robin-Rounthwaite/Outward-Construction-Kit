using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Loads every installed region. Each gets a real InteractionSwitchArea door in the world, so travel goes
    /// through the game's own level loader, with a way back at the far end.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class KitPlugin : BaseUnityPlugin
    {
        public const string GUID = "catalysten.outwardconstructionkit";
        public const string NAME = "Outward Construction Kit";
        public const string VERSION = "0.3.0";

        internal static KitPlugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;

        internal ConfigEntry<KeyboardShortcut> PlaceDoorKey;
        internal ConfigEntry<KeyboardShortcut> NextRegionKey;
        internal ConfigEntry<bool> ProbeScenes;
        internal ConfigEntry<bool> MemoryReport;
        internal ConfigEntry<bool> SelfCheck;
        internal ConfigEntry<bool> UseCache;

        internal ConfigEntry<bool> VerboseLog;

        /// <summary>Routine detail, such as each prop placed, for working on the kit or a region.</summary>
        internal static void Detail(string message)
        {
            if (Instance != null && (Instance.VerboseLog.Value || Instance.SelfCheck.Value))
                Log.LogMessage(message);
        }

        private readonly Dictionary<Region, GameObject> m_doors = new Dictionary<Region, GameObject>();
        private int m_selected;
        private bool m_buildPending;

        internal void Awake()
        {
            Instance = this;
            Log = Logger;

            PlaceDoorKey = Config.Bind("Doors", "PlaceDoorKey", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftControl),
                "Press this in game to put the selected region's door in front of you (and save the spot).");
            NextRegionKey = Config.Bind("Doors", "NextRegionKey", new KeyboardShortcut(KeyCode.F8, KeyCode.LeftControl),
                "Press this in game to choose which region the place-door key applies to.");
            ProbeScenes = Config.Bind("Dev", "ProbeScenes", false,
                "Write what each loaded scene contains to a probes folder beside the plugin.");
            MemoryReport = Config.Bind("Dev", "MemoryReport", false,
                "Behind the first loading screen, log what's holding on to the assets kept from donor scenes. Adds many seconds.");
            SelfCheck = Config.Bind("Dev", "SelfCheck", false,
                "After entering a region, log [CHECK] lines saying whether its props, enemies, ground, water and doors came out right.");

            VerboseLog = Config.Bind("Dev", "VerboseLog", false,
                "Log routine detail too, such as every prop and door placed. SelfCheck turns this on as well.");
            UseCache = Config.Bind("Dev", "UseCache", true,
                "Load props and enemies from a cache built from your game files, instead of loading the scenes they came from.");

            Region.LoadAll(Config);
            try
            {
                AssetCache.BuildMissing();
            }
            catch (System.Exception e)
            {
                Logger.LogError("Couldn't build the asset cache, so regions will use donor scenes: " + e);
            }

            var harmony = new Harmony(GUID);
            harmony.PatchAll(typeof(AreaManagerAwakePatch));
            SceneExtractor.Patch(harmony);
            try
            {
                MissingRegionGuard.Remember();
                MissingRegionGuard.Patch(harmony);
            }
            catch (System.Exception e)
            {
                Logger.LogError("Couldn't set up the guard for saves left in removed regions: " + e);
            }
            try
            {
                DeathReturn.Patch(harmony);
            }
            catch (System.Exception e)
            {
                Logger.LogError("Couldn't set up returning from a defeat in a region: " + e);
            }
            try
            {
                CoopGuard.Patch(harmony);
            }
            catch (System.Exception e)
            {
                Logger.LogError("Couldn't set up checking that co-op players have the same regions: " + e);
            }
            if (AreaManager.Instance != null)
                RegisterAll(AreaManager.Instance);

            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(BuildWhenLoaded());
            if (AutoTest.Begin(this))
                Logger.LogMessage("[AUTOTEST] Started, from a request file.");

            Logger.LogMessage($"{NAME} {VERSION} loaded. {NextRegionKey.Value} chooses a region, and {PlaceDoorKey.Value} places its door.");
        }

        internal static void RegisterAll(AreaManager manager)
        {
            foreach (Region region in Region.All)
                region.Register(manager);
        }

        internal void Update()
        {
            if (PlaceDoorKey.Value.IsDown())
                PlaceDoorInFrontOfPlayer();
            if (NextRegionKey.Value.IsDown())
                SelectNextRegion();
        }

        // Runs before the loader spawns the player, which is the last moment the spawn list can change.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (SceneExtractor.IsExtracting)
                return;
            // Vanilla travel shows what an ordinary level load costs, to compare with what donor scenes keep.
            KitPlugin.Detail($"Scene {scene.name} loaded, process memory {SceneExtractor.PrivateMemory() / (1024 * 1024)} MB.");
            try
            {
                DeathReturn.OnSceneLoaded(scene.name);
                Region here = Region.ForScene(scene.name);
                if (here != null)
                    here.PrepareScene(scene);
                else
                    AssetCache.ReleaseAll();
                foreach (Region region in Region.All)
                    if (region.EntranceScene == scene.name && region != here)
                        AddReturnSpawn(region, scene);
            }
            catch (System.Exception e)
            {
                Logger.LogError("Scene setup failed in " + scene.name + ": " + e);
            }
            m_buildPending = true;
        }

        private IEnumerator BuildWhenLoaded()
        {
            var wait = new WaitForSeconds(0.5f);
            while (true)
            {
                yield return wait;

                if (!m_buildPending || NetworkLevelLoader.Instance == null || !NetworkLevelLoader.Instance.IsOverallLoadingDone)
                    continue;

                m_buildPending = false;
                try
                {
                    if (ProbeScenes.Value)
                        SceneProbe.Run(Path.GetDirectoryName(Info.Location));
                    BuildDoors();
                    MissingRegionGuard.OnLevelReady();
                    PlayerNotice.ShowPending();
                }
                catch (System.Exception e)
                {
                    Logger.LogError("Failed to build doors: " + e);
                }
            }
        }

        private void BuildDoors()
        {
            if (AreaManager.Instance == null)
                return;

            string active = SceneManager.GetActiveScene().name;
            foreach (Region region in Region.All)
            {
                if (region.SceneName == active)
                    BuildReturnDoor(region);
                else if (region.EntranceScene == active)
                    BuildDoor(region);
            }
        }

        private void BuildDoor(Region region)
        {
            Vector3 pos;
            if (!TryParseVector(region.EntrancePosition, out pos))
                return;

            // Without its borrowed environment the region can't finish loading, so a door would trap the player.
            if (!region.CanLoad || !EnvironmentDonor.IsReady(region.Manifest.environmentDonor))
            {
                Logger.LogError($"Region '{region.Id}' couldn't borrow its environment from '{region.Manifest.environmentDonor}', so its door is left out.");
                return;
            }

            GameObject old;
            if (m_doors.TryGetValue(region, out old) && old != null)
                Destroy(old);
            m_doors[region] = KitDoor.Build("OCK_Door_" + region.Id, pos, Quaternion.Euler(0f, region.EntranceRotationY, 0f), region.AreaId, 0, DoorModelOf(region));
            KitPlugin.Detail($"Door to '{region.DisplayName}' (area {region.AreaId}) placed at {pos}.");
        }

        private static RegionManifest.PropReference DoorModelOf(Region region)
        {
            return region.Manifest.doorModel ?? RegionManifest.DefaultDoorModel;
        }

        // Where the modder put it, or else in front of the arrival spawn, facing it.
        private void BuildReturnDoor(Region region)
        {
            Area home = AreaManager.Instance.GetAreaFromSceneName(region.EntranceScene);
            SpawnPointManager spawns = SpawnPointManager.Instance;
            if (home == null || region.ReturnSpawnIndex.Value < 0 || spawns == null || spawns.SpawnPoints == null || spawns.SpawnPoints.Length == 0)
            {
                Logger.LogWarning($"Can't build the return door in '{region.Id}': its door hasn't been placed, or this scene has no spawn points.");
                return;
            }

            if (region.Manifest.returnDoors.Count > 0)
            {
                foreach (RegionManifest.ReturnDoor door in region.Manifest.returnDoors)
                {
                    KitDoor.Build("OCK_ReturnDoor", door.position, Quaternion.Euler(0f, door.rotationY, 0f), home.ID, region.ReturnSpawnIndex.Value, DoorModelOf(region));
                    KitPlugin.Detail($"Return door to '{home.SceneName}' placed at {door.position}, as the region asks.");
                }
                return;
            }

            Transform arrival = spawns.SpawnPoints[0];
            Vector3 forward = arrival.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;

            Vector3 pos = arrival.position + forward * 2.5f;
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out hit, 6f, Global.FullEnvironmentMask, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y;

            KitDoor.Build("OCK_ReturnDoor", pos, Quaternion.LookRotation(-forward), home.ID, region.ReturnSpawnIndex.Value, DoorModelOf(region));
            KitPlugin.Detail($"Return door to '{home.SceneName}' placed at {pos}.");
        }

        // Adds a spawn beside the door. The list is always built in the same order, so the index stays valid.
        private void AddReturnSpawn(Region region, Scene scene)
        {
            Vector3 pos;
            if (!TryParseVector(region.EntrancePosition, out pos))
                return;
            SpawnPointManager spawns = SpawnPointManager.Instance;
            if (spawns == null)
            {
                Logger.LogWarning("No SpawnPointManager in " + scene.name + " yet; the return door can't be wired up.");
                return;
            }

            Quaternion rot = Quaternion.Euler(0f, region.EntranceRotationY, 0f);
            var marker = new GameObject("OCK_ReturnSpawn_" + region.Id);
            SceneManager.MoveGameObjectToScene(marker, scene);
            marker.transform.position = pos + rot * Vector3.forward * 1.5f;
            marker.transform.rotation = rot;

            Transform[] current = spawns.SpawnPoints ?? new Transform[0];
            var extended = new Transform[current.Length + 1];
            current.CopyTo(extended, 0);
            extended[current.Length] = marker.transform;
            spawns.SpawnPoints = extended;

            if (region.ReturnSpawnIndex.Value != current.Length)
            {
                region.ReturnSpawnIndex.Value = current.Length;
                Config.Save();
            }
        }

        private Region SelectedRegion
        {
            get { return Region.All.Count > 0 ? Region.All[m_selected % Region.All.Count] : null; }
        }

        private void SelectNextRegion()
        {
            if (Region.All.Count == 0)
                return;
            m_selected = (m_selected + 1) % Region.All.Count;
            Notify("Next door leads to: " + SelectedRegion.DisplayName);
        }

        private static void Notify(string message)
        {
            Character ch = CharacterManager.Instance != null ? CharacterManager.Instance.GetFirstLocalCharacter() : null;
            if (ch != null && ch.CharacterUI != null)
                ch.CharacterUI.ShowInfoNotification(message);
        }

        private void PlaceDoorInFrontOfPlayer()
        {
            Region region = SelectedRegion;
            if (region == null)
            {
                Logger.LogWarning("No regions are installed.");
                return;
            }
            Character ch = CharacterManager.Instance != null ? CharacterManager.Instance.GetFirstLocalCharacter() : null;
            if (ch == null)
            {
                Logger.LogWarning("No local character yet; load into the game first.");
                return;
            }

            // Only this player would have it, so nobody else could follow them through.
            if (PhotonNetwork.otherPlayers.Length > 0)
            {
                Notify("Doors can only be placed when playing alone.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.name == region.SceneName)
            {
                Notify(region.DisplayName + "'s door can't go inside " + region.DisplayName + ".");
                return;
            }

            Vector3 eye = ch.transform.position + Vector3.up * 1.2f;
            Vector3 fwd = ch.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();

            Vector3 pos;
            float rotY;
            RaycastHit hit;
            if (Physics.Raycast(eye, fwd, out hit, 4f, Global.FullEnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 n = hit.normal;
                n.y = 0f;
                if (n.sqrMagnitude < 0.01f) n = -fwd;
                n.Normalize();
                pos = new Vector3(hit.point.x, ch.transform.position.y, hit.point.z) + n * 0.15f;
                rotY = Quaternion.LookRotation(n).eulerAngles.y; // door faces out of the wall
            }
            else
            {
                pos = ch.transform.position + fwd * 2f;
                rotY = Quaternion.LookRotation(-fwd).eulerAngles.y; // door faces the player
            }

            region.DoorScene.Value = scene.name;
            region.DoorPosition.Value = FormatVector(pos);
            region.DoorRotationY.Value = rotY;
            Config.Save();

            // The marker from an earlier placement stays in the list but is never used again.
            AddReturnSpawn(region, scene);
            BuildDoor(region);
            Notify("Door to " + region.DisplayName + " placed.");
        }

        internal static bool TryParseVector(string s, out Vector3 v)
        {
            v = Vector3.zero;
            if (string.IsNullOrEmpty(s)) return false;
            string[] p = s.Split(',');
            if (p.Length != 3) return false;
            float x, y, z;
            var ci = CultureInfo.InvariantCulture;
            if (!float.TryParse(p[0].Trim(), NumberStyles.Float, ci, out x)) return false;
            if (!float.TryParse(p[1].Trim(), NumberStyles.Float, ci, out y)) return false;
            if (!float.TryParse(p[2].Trim(), NumberStyles.Float, ci, out z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }

        internal static string FormatVector(Vector3 v)
        {
            var ci = CultureInfo.InvariantCulture;
            return v.x.ToString("0.###", ci) + "," + v.y.ToString("0.###", ci) + "," + v.z.ToString("0.###", ci);
        }
    }

    [HarmonyPatch(typeof(AreaManager), "Awake")]
    internal static class AreaManagerAwakePatch
    {
        internal static void Postfix(AreaManager __instance)
        {
            KitPlugin.RegisterAll(__instance);
        }
    }
}
