using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Some per-scene objects can't be built from code, so a region borrows them: the environment and
    /// TOD sky rig, the main camera, and the audio manager. They come from the asset cache, or else
    /// SceneExtractor supplies a donor scene and they're cloned under an inactive parent, where nothing
    /// runs until they're used. Regions naming the same donor share one set of templates.
    /// </summary>
    internal static class EnvironmentDonor
    {
        public const string ENVIRONMENT = "Environment";
        private const string MAIN_CAMERA = "Main Camera"; // the game finds it by this exact name

        // Everything else under Environment is that scene's own terrain, props and water.
        public static readonly string[] KeepChildren = { "WindZone", "WorldItems", "Sky Dome Custom", "WeatherEvents" };

        private static readonly Dictionary<string, List<GameObject>> s_templates = new Dictionary<string, List<GameObject>>();
        private static GameObject s_holder;

        private static string NameOf(string part)
        {
            return part == RegionManifest.ENVIRONMENT_KEY ? ENVIRONMENT : part;
        }

        private static bool AllCached(string donor)
        {
            return RegionManifest.EnvironmentParts.All(part => AssetCache.Has(donor, part));
        }

        public static bool IsReady(string donor)
        {
            if (donor == null)
                return false;
            List<GameObject> templates;
            return AllCached(donor) || (s_templates.TryGetValue(donor, out templates) && templates.Count == RegionManifest.EnvironmentParts.Length);
        }

        private static GameObject Holder()
        {
            if (s_holder == null)
            {
                s_holder = new GameObject("OCK_EnvironmentDonor");
                s_holder.SetActive(false);
                Object.DontDestroyOnLoad(s_holder);
            }
            return s_holder;
        }

        public static void Harvest(Scene scene)
        {
            if (AllCached(scene.name))
                return;

            var templates = new List<GameObject>();
            s_templates[scene.name] = templates;
            foreach (string part in RegionManifest.EnvironmentParts)
            {
                string name = NameOf(part);
                GameObject original = System.Array.Find(scene.GetRootGameObjects(), root => root.name == name);
                if (original == null)
                {
                    KitPlugin.Log.LogWarning($"{scene.name} has no '{name}' to borrow.");
                    continue;
                }
                templates.Add(MakeTemplate(original, name));
            }
        }

        private static GameObject MakeTemplate(GameObject original, string name)
        {
            GameObject template = Object.Instantiate(original, Holder().transform);
            template.name = name;
            template.SetActive(true); // stays asleep under the holder
            if (name == ENVIRONMENT)
                StripEnvironment(template);
            return template;
        }

        private static void StripEnvironment(GameObject environment)
        {
            // Seasons are components somewhere under Environment, and weather reads them every frame.
            var conditions = environment.GetComponent<EnvironmentConditions>();
            if (conditions.Seasons != null)
                foreach (Season season in conditions.Seasons)
                    if (season != null && season.gameObject != environment)
                        season.transform.SetParent(environment.transform, false);

            for (int i = environment.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = environment.transform.GetChild(i);
                if (System.Array.IndexOf(KeepChildren, child.name) < 0 && child.GetComponent<Season>() == null)
                    Object.DestroyImmediate(child.gameObject);
            }

            // Tied to the terrain that was just removed.
            var terrainData = environment.GetComponent<LargeTerrainData>();
            if (terrainData != null)
                Object.DestroyImmediate(terrainData);
        }

        public static void Place(Scene scene, string donor)
        {
            foreach (string part in RegionManifest.EnvironmentParts)
            {
                string name = NameOf(part);
                GameObject cached = AssetCache.Load(donor, part);
                List<GameObject> templates;
                GameObject template = cached != null ? MakeTemplate(cached, name)
                    : s_templates.TryGetValue(donor, out templates) ? templates.Find(t => t.name == name) : null;
                if (template == null)
                    continue;

                GameObject clone = Object.Instantiate(template);
                clone.name = name;
                SceneManager.MoveGameObjectToScene(clone, scene);
                if (cached != null)
                    Object.Destroy(template);

                // Normally found by name as the level loads, which may be before or after this runs.
                if (name == MAIN_CAMERA)
                    Global.MainCamera = clone.GetComponent<Camera>();
            }
        }
    }
}
