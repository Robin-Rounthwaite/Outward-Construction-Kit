using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// One installed region: a scene bundle with a manifest beside it, named "name" and "name.refs.txt".
    /// They can sit anywhere under BepInEx\plugins, so each region can be its own mod. Registering makes the
    /// scene look like a normal area to the game: the bundle lets LoadSceneAsync find it by name, and the
    /// two registrations let doors and the loader accept it.
    /// </summary>
    internal class Region
    {
        private const string MANIFEST_SUFFIX = ".refs.txt";
        private const string SPLASH_SOURCE_SCENE = "Monsoon";

        public static readonly List<Region> All = new List<Region>();

        public AssetBundle Bundle { get; private set; } // stays loaded, or the scene can't be found by name
        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public string SceneName { get; private set; }
        public int AreaId { get; private set; }
        public RegionManifest Manifest { get; private set; }

        /// <summary>The same for two players only if they have the same version of this region.</summary>
        public string Signature { get; private set; }

        public ConfigEntry<string> DoorScene;
        public ConfigEntry<string> DoorPosition;
        public ConfigEntry<float> DoorRotationY;
        public ConfigEntry<int> ReturnSpawnIndex;

        // A door the player placed takes priority over the entrance the region ships with.
        private bool PlayerPlacedDoor { get { return !string.IsNullOrEmpty(DoorScene.Value); } }
        public string EntranceScene { get { return PlayerPlacedDoor ? DoorScene.Value : Manifest.doorScene; } }
        public string EntrancePosition { get { return PlayerPlacedDoor ? DoorPosition.Value : Manifest.doorPosition; } }
        public float EntranceRotationY { get { return PlayerPlacedDoor ? DoorRotationY.Value : Manifest.doorRotationY; } }

        public static Region ForScene(string sceneName)
        {
            return All.FirstOrDefault(r => r.SceneName == sceneName);
        }

        public static IEnumerable<string> AllDonorScenes()
        {
            return All.SelectMany(r => r.Manifest.DonorScenes(AssetCache.Has)).Distinct();
        }

        /// <summary>
        /// Whether this can load at all. A region can't without the scene it borrows its sky, camera and audio from,
        /// which a game update could rename. It then gets no door, and saves left inside it go elsewhere.
        /// </summary>
        public bool CanLoad
        {
            get { return !string.IsNullOrEmpty(Manifest.environmentDonor) && Enumerable.Range(0, SceneManager.sceneCountInBuildSettings).Any(i => Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i)) == Manifest.environmentDonor); }
        }

        public static void LoadAll(ConfigFile config)
        {
            string[] manifests = Directory.GetFiles(Paths.PluginPath, "*" + MANIFEST_SUFFIX, SearchOption.AllDirectories);
            foreach (string manifestFile in manifests.OrderBy(f => Path.GetFileName(f)))
            {
                try
                {
                    Region region = Load(manifestFile.Substring(0, manifestFile.Length - MANIFEST_SUFFIX.Length), config);
                    if (region != null)
                        All.Add(region);
                }
                catch (Exception e)
                {
                    KitPlugin.Log.LogError("Couldn't load the region at " + manifestFile + ": " + e);
                }
            }
            KitPlugin.Log.LogMessage($"{All.Count} regions installed: {string.Join(", ", All.Select(r => r.Id).ToArray())}");
            if (manifests.Length > All.Count)
                PlayerNotice.Add($"{manifests.Length - All.Count} region mods couldn't load.");
            foreach (Region region in All.Where(r => !r.CanLoad))
                PlayerNotice.Add($"'{region.DisplayName}' needs a scene the game doesn't have, so it's closed until it's updated.");
        }

        private static Region Load(string bundlePath, ConfigFile config)
        {
            if (!File.Exists(bundlePath))
            {
                KitPlugin.Log.LogError("Region manifest has no bundle beside it: " + bundlePath);
                return null;
            }

            RegionManifest manifest = RegionManifest.Load(bundlePath + MANIFEST_SUFFIX);
            if (manifest == null)
                return null;

            string id = string.IsNullOrEmpty(manifest.regionId) ? Path.GetFileName(bundlePath) : manifest.regionId;
            if (All.Any(r => r.Id == id))
            {
                KitPlugin.Log.LogError($"Two regions use the ID '{id}', so {bundlePath} is left out.");
                return null;
            }

            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                KitPlugin.Log.LogError($"Couldn't load {bundlePath}. It must be built with Unity 2020.3.26, and two bundles can't share a build name.");
                return null;
            }
            string[] scenes = bundle.GetAllScenePaths();
            string sceneName = scenes.Length > 0 ? Path.GetFileNameWithoutExtension(scenes[0]) : null;
            if (sceneName == null || All.Any(r => r.SceneName == sceneName))
            {
                // Scenes load by name, and saves are kept by scene name too, so these must be unique.
                KitPlugin.Log.LogError($"{bundlePath} has no scene, or its scene name '{sceneName}' is already in use, so it's left out.");
                bundle.Unload(true);
                return null;
            }

            int areaId = AreaIdFor(id);
            if (All.Any(r => r.AreaId == areaId))
            {
                KitPlugin.Log.LogError($"Region '{id}' can't get a unique area number, so it's left out. Changing its ID fixes this.");
                bundle.Unload(true);
                return null;
            }

            string section = "Region " + id;
            return new Region
            {
                Id = id,
                Signature = id + "=" + Hash(File.ReadAllText(bundlePath + MANIFEST_SUFFIX)),
                DisplayName = string.IsNullOrEmpty(manifest.displayName) ? id : manifest.displayName,
                SceneName = sceneName,
                AreaId = areaId,
                Manifest = manifest,
                Bundle = bundle,
                DoorScene = config.Bind(section, "DoorScene", "", "Scene the door to this region lives in. Filled in when you place the door."),
                DoorPosition = config.Bind(section, "DoorPosition", "", "World position of the door (x,y,z). Filled in when you place the door."),
                DoorRotationY = config.Bind(section, "DoorRotationY", 0f, "Which way the door faces (degrees around the vertical axis)."),
                ReturnSpawnIndex = config.Bind(section, "ReturnSpawnIndex", -1, "Spawn index added beside the door for the way back. Filled in automatically."),
            };
        }

        // Stable for a given ID, and well above the game's own area numbers, which stop below 1000.
        internal static int AreaIdFor(string id)
        {
            return 100000 + (int)(Hash(id) % 900000);
        }

        internal static uint Hash(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text)
                hash = (hash ^ c) * 16777619;
            return hash;
        }

        public void Register(AreaManager manager)
        {
            if (manager.Areas.Any(a => a != null && a.ID == AreaId))
                return;

            var area = new Area
            {
                ID = AreaId,
                SceneName = SceneName,
                DefaultName = DisplayName,
                NameLocKey = "",
                DescriptionLocKey = "",
            };

            // A null path isn't safe to look up, so borrow a vanilla area's loading art and map.
            Area template = manager.Areas.FirstOrDefault(a => a != null && a.SceneName == SPLASH_SOURCE_SCENE);
            if (template != null)
            {
                foreach (string field in new[] { "m_splashScreenPath", "m_mapScreenPath" })
                {
                    FieldInfo info = AccessTools.Field(typeof(Area), field);
                    info.SetValue(area, info.GetValue(template));
                }
                area.MapResourcesName = template.MapResourcesName;
            }

            manager.Areas = manager.Areas.Concat(new[] { area }).ToArray();

            object lookup = AccessTools.Field(typeof(AreaManager), "m_areas").GetValue(manager);
            MethodInfo add = lookup.GetType().GetMethods().First(m => m.Name == "Add" && m.GetParameters().Length == 2);
            add.Invoke(lookup, new object[] { AreaId, area });

            // InteractionSwitchArea refuses any scene missing from this list, which comes from the build settings.
            AreaManager.ActiveScenes = AreaManager.ActiveScenes.Concat(new[] { SceneName }).ToArray();

            KitPlugin.Detail($"Registered region '{Id}': scene {SceneName}, area {AreaId}.");
        }

        /// <summary>
        /// The bundled scene has no game scripts in it. It names things by convention instead, and
        /// this turns them into game components before the loader asks for a spawn point.
        /// </summary>
        public void PrepareScene(Scene scene)
        {
            ShaderSwap.Apply(scene, this); // first, so it only touches what came from the bundle
            int propsPlaced = PropDonor.Place(scene, this); // before the layers, so its colliders get the ground layer below
            if (propsPlaced < Manifest.props.Count)
                PlayerNotice.Add($"'{DisplayName}' is missing {Manifest.props.Count - propsPlaced} of its props, perhaps after a game update.");
            GroundDonor.Apply(scene, this);

            int ground = LayerMask.NameToLayer("LargeEnvironment");
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (ground >= 0)
                    foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                        collider.gameObject.layer = ground;

                if (root.name != "SpawnPointManager")
                    continue;

                var spawns = root.AddComponent<SpawnPointManager>();
                var points = new Transform[root.transform.childCount];
                for (int i = 0; i < points.Length; i++)
                    points[i] = root.transform.GetChild(i);
                spawns.SpawnPoints = points;
                SpawnPointManager.Instance = spawns;
            }

            WaterDonor.Place(scene, this); // after the layers, because water isn't ground
            int interactivesPlaced = InteractiveDonor.Place(scene, this); // likewise, since players use these rather than stand on them
            if (interactivesPlaced < Manifest.interactives.Count)
                PlayerNotice.Add($"'{DisplayName}' is missing {Manifest.interactives.Count - interactivesPlaced} of its chests and gathering spots, perhaps after a game update.");

            // Enemies go under the AI managers, which find the characters they run by looking at their children.
            Transform aiRoot = AddAIManagers(scene);
            EnvironmentDonor.Place(scene, Manifest.environmentDonor);
            EnemyDonor.Place(scene, this, aiRoot);

            foreach (string missing in SceneProbe.MissingRequired())
                KitPlugin.Log.LogWarning("Region scene has no " + missing + ".");

            if (KitPlugin.Instance.SelfCheck.Value)
                KitPlugin.Instance.StartCoroutine(RegionChecks.Run(this, scene, propsPlaced));
        }

        // AISquadManager.Awake looks these children up without checking, so they must exist before it wakes.
        private static Transform AddAIManagers(Scene scene)
        {
            var root = new GameObject("AISquadManagerStructure");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            foreach (string child in new[] { "SquadReserve", "SquadSpawnPoints", "DeadAiContainer" })
                new GameObject(child).transform.SetParent(root.transform, false);

            root.AddComponent<AISquadManager>();
            // Normally filled in by the scene file, and Start loops over it without checking.
            root.AddComponent<AISceneManager>().PresetSetups = new List<AIPresetSetup>();
            root.SetActive(true);
            return root.transform;
        }
    }
}
