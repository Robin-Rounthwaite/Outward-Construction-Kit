using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Some per-scene objects can't be built from code, so a region borrows them: the environment and
    /// TOD sky rig, the main camera, and the audio manager. SceneExtractor supplies a donor scene, and
    /// they're cloned under an inactive parent, where nothing runs until they're used. Regions naming
    /// the same donor share one set of templates.
    /// </summary>
    internal static class EnvironmentDonor
    {
        private const string ENVIRONMENT = "Environment";
        private const string MAIN_CAMERA = "Main Camera"; // the game finds it by this exact name
        private const string AUDIO_MANAGER = "AudioManager";

        // Woken in this order: audio and sky exist by the time the camera looks for them.
        private static readonly string[] s_roots = { AUDIO_MANAGER, ENVIRONMENT, MAIN_CAMERA };

        // Everything else under Environment is that scene's own terrain, props and water.
        private static readonly string[] s_keepChildren = { "WindZone", "WorldItems", "Sky Dome Custom", "WeatherEvents" };

        private static readonly Dictionary<string, List<GameObject>> s_templates = new Dictionary<string, List<GameObject>>();
        private static GameObject s_holder;

        public static bool IsReady(string donor)
        {
            List<GameObject> templates;
            return donor != null && s_templates.TryGetValue(donor, out templates) && templates.Count == s_roots.Length;
        }

        public static void Harvest(Scene scene)
        {
            if (s_holder == null)
            {
                s_holder = new GameObject("OCK_EnvironmentDonor");
                s_holder.SetActive(false);
                Object.DontDestroyOnLoad(s_holder);
            }

            var templates = new List<GameObject>();
            s_templates[scene.name] = templates;
            foreach (string name in s_roots)
            {
                GameObject original = System.Array.Find(scene.GetRootGameObjects(), root => root.name == name);
                if (original == null)
                {
                    KitPlugin.Log.LogWarning($"{scene.name} has no '{name}' to borrow.");
                    continue;
                }

                GameObject template = Object.Instantiate(original, s_holder.transform);
                template.name = name;
                template.SetActive(true); // stays asleep under the holder
                templates.Add(template);

                if (name == ENVIRONMENT)
                    StripEnvironment(template);
            }
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
                if (System.Array.IndexOf(s_keepChildren, child.name) < 0 && child.GetComponent<Season>() == null)
                    Object.DestroyImmediate(child.gameObject);
            }

            // Tied to the terrain that was just removed.
            var terrainData = environment.GetComponent<LargeTerrainData>();
            if (terrainData != null)
                Object.DestroyImmediate(terrainData);
        }

        public static void Place(Scene scene, string donor)
        {
            foreach (GameObject template in s_templates[donor])
            {
                GameObject clone = Object.Instantiate(template);
                clone.name = template.name;
                SceneManager.MoveGameObjectToScene(clone, scene);

                // Normally found by name as the level loads, which may be before or after this runs.
                if (clone.name == MAIN_CAMERA)
                    Global.MainCamera = clone.GetComponent<Camera>();
            }
        }
    }
}
