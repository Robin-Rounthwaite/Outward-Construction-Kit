using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Lists the game's items for the Unity kit's Items tab: the same list as the game's debug item spawner, with names in
    /// the player's language. It's written from the running game because names are only available there, and stays on
    /// the player's own PC.
    /// </summary>
    internal static class ItemCatalog
    {
        private const string FILE_NAME = "OCK_Items.txt";

        internal static string Line(int id, string type, string prefabName, string displayName)
        {
            return $"{id} | {Clean(type)} | {Clean(prefabName)} | {Clean(displayName)}";
        }

        private static string Clean(string text)
        {
            return (text ?? "").Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ').Trim();
        }

        public static IEnumerator WriteWhenLoaded()
        {
            while (ResourcesPrefabManager.Instance == null || !ResourcesPrefabManager.Instance.Loaded || LocalizationManager.Instance == null || !LocalizationManager.Instance.Loaded)
                yield return new WaitForSecondsRealtime(1f);
            try
            {
                Write();
            }
            catch (System.Exception e)
            {
                KitPlugin.Log.LogError("Couldn't write the list of items for the Unity kit: " + e);
            }
        }

        private static void Write()
        {
            var prefabs = (Dictionary<string, Item>)AccessTools.Field(typeof(ResourcesPrefabManager), "ITEM_PREFABS").GetValue(null);
            List<string> lines = prefabs.Values.Where(Spawnable).OrderBy(i => i.ItemID).Select(i => Line(i.ItemID, i.GetType().Name, i.name, i.Name)).ToList();
            lines.Insert(0, "# The game's items, written by the Construction Kit. Don't edit: it's replaced each time the game starts.");
            File.WriteAllLines(Path.Combine(Paths.BepInExRootPath, FILE_NAME), lines.ToArray());
            KitPlugin.Log.LogMessage($"Listed {lines.Count - 1} items for the Unity kit.");
        }

        // As the debug spawner chooses, for a human character. Quests are items too, but not ones to leave on the ground.
        private static bool Spawnable(Item item)
        {
            if (item == null || item is Skill || item is Quest || !item.IsPickable)
                return false;
            var equipment = item as Equipment;
            return equipment == null || equipment.RequiredPType != PlayerSystem.PlayerTypes.Trog;
        }
    }
}
