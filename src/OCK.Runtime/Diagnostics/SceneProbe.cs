using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Writes what a loaded scene contains to probes\&lt;scene&gt;.txt next to the DLL. The loader leans on
    /// these managers, and this shows which survive between scenes and which a new region must supply.
    /// </summary>
    internal static class SceneProbe
    {
        private const int MAX_DEPTH = 2;

        private static readonly Type[] s_managers =
        {
            typeof(SpawnPointManager), typeof(SceneInteractionManager), typeof(DefeatScenariosManager),
            typeof(EnvironmentConditions), typeof(NetworkEnvironment), typeof(WeatherManager),
            typeof(QuestEventManager), typeof(AISceneManager), typeof(AISquadManager), typeof(SceneWind),
            typeof(TerrainManager), typeof(EnvironmentInteractionsManager),
        };

        // Every vanilla scene carries these itself, and game code uses them without null checks.
        private static readonly Type[] s_requiredInScene =
        {
            typeof(SpawnPointManager), typeof(EnvironmentConditions), typeof(AISquadManager), typeof(AISceneManager),
        };

        public static IEnumerable<string> MissingRequired()
        {
            return s_requiredInScene.Where(t => GetInstance(t) == null).Select(t => t.Name);
        }

        private static Component GetInstance(Type type)
        {
            FieldInfo field = type.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return field != null ? field.GetValue(null) as Component : null;
        }

        public static IEnumerable<string> MissingManagers()
        {
            return s_managers.Where(t => GetInstance(t) == null).Select(t => t.Name);
        }

        public static void Run(string pluginDir)
        {
            Scene scene = SceneManager.GetActiveScene();
            var text = new StringBuilder();
            text.AppendLine("Scene: " + scene.name + " (build index " + scene.buildIndex + ")");

            text.AppendLine().AppendLine("== Managers ==");
            foreach (Type type in s_managers)
            {
                Component instance = GetInstance(type);
                text.AppendLine(type.Name + ": " + (instance == null ? "MISSING" : "in scene '" + instance.gameObject.scene.name + "' at " + PathOf(instance.transform)));
            }

            if (SpawnPointManager.Instance != null && SpawnPointManager.Instance.SpawnPoints != null)
                text.AppendLine().AppendLine("Spawn points: " + SpawnPointManager.Instance.SpawnPoints.Length);

            text.AppendLine().AppendLine("== Areas ==");
            if (AreaManager.Instance != null)
                foreach (Area area in AreaManager.Instance.Areas.Where(a => a != null))
                    text.AppendLine(area.ID + " " + area.SceneName + " '" + area.DefaultName + "'");

            text.AppendLine().AppendLine("== Scene objects ==");
            foreach (GameObject root in scene.GetRootGameObjects())
                Dump(root.transform, 0, text);

            // There is no API for this list, but any object moved there can see its neighbours.
            text.AppendLine().AppendLine("== DontDestroyOnLoad objects ==");
            var probe = new GameObject("OCK_Probe");
            UnityEngine.Object.DontDestroyOnLoad(probe);
            foreach (GameObject root in probe.scene.GetRootGameObjects())
                if (root != probe)
                    Dump(root.transform, MAX_DEPTH, text);
            UnityEngine.Object.Destroy(probe);

            string dir = Path.Combine(pluginDir, "probes");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, scene.name + ".txt"), text.ToString());
            KitPlugin.Log.LogMessage("Scene probe written for " + scene.name + ". Missing managers: " + string.Join(", ", MissingManagers().ToArray()));
        }

        private static void Dump(Transform t, int depth, StringBuilder text)
        {
            string components = string.Join(", ", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToArray());
            text.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)");
            if (components.Length > 0)
                text.Append("  [").Append(components).Append(']');
            text.AppendLine();

            if (depth >= MAX_DEPTH)
                return;
            foreach (Transform child in t)
                Dump(child, depth + 1, text);
        }

        private static string PathOf(Transform t)
        {
            return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        }
    }
}
