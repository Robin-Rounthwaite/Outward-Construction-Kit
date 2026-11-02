using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Vanilla chests and gathering spots that work. Unlike decoration they keep their scripts, which are items to the
    /// game, so it saves and restores them, regrows them on its own timers and syncs them in co-op. Each gets an ID worked
    /// out from the region, the thing and its place, so every player and every visit arrives at the same one.
    /// </summary>
    internal static class InteractiveDonor
    {
        private const string UID_PREFIX = "ockprop";

        private static readonly Dictionary<string, RegionManifest.Contents> s_contents = new Dictionary<string, RegionManifest.Contents>();
        private static readonly AccessTools.FieldRef<TreasureChest, bool> f_generated = AccessTools.FieldRefAccess<TreasureChest, bool>("m_hasGeneratedContent");
        private static readonly AccessTools.FieldRef<TreasureChest, bool> f_changed = AccessTools.FieldRefAccess<TreasureChest, bool>("m_contentChanged");

        public static void Patch(Harmony harmony)
        {
            harmony.PatchAll(typeof(ContentsPatch));
            harmony.PatchAll(typeof(CanGeneratePatch));
        }

        /// <summary>The same for the same thing in the same spot, and different if it's moved or swapped.</summary>
        internal static string Uid(string regionId, RegionManifest.InteractiveReference reference)
        {
            string spot = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3:0.###},{4:0.###},{5:0.###}", regionId, reference.scene, reference.path, reference.position.x, reference.position.y, reference.position.z);
            return UID_PREFIX + Region.Hash(spot).ToString("x8") + "_" + Region.Hash(spot + "|2").ToString("x8");
        }

        /// <summary>Returns how many were placed.</summary>
        public static int Place(Scene scene, Region region)
        {
            s_contents.Clear();
            if (region.Manifest.interactives.Count == 0)
                return 0;

            var waiting = new GameObject("OCK_InteractivesWaking");
            waiting.SetActive(false);
            SceneManager.MoveGameObjectToScene(waiting, scene);

            int placed = 0;
            foreach (RegionManifest.InteractiveReference reference in region.Manifest.interactives)
            {
                GameObject cached = AssetCache.Load(reference.scene, reference.path);
                if (cached == null)
                {
                    KitPlugin.Log.LogError($"'{reference.path}' from {reference.scene} isn't in the cache, so it's left out of '{region.DisplayName}'.");
                    continue;
                }
                try
                {
                    PlaceOne(cached, reference, region, waiting.transform);
                    placed++;
                }
                catch (System.Exception e)
                {
                    KitPlugin.Log.LogError($"Couldn't place '{reference.path}': {e}");
                }
            }
            Object.Destroy(waiting);
            return placed;
        }

        private static void PlaceOne(GameObject cached, RegionManifest.InteractiveReference reference, Region region, Transform waiting)
        {
            GameObject thing = Object.Instantiate(cached, waiting);
            thing.name = cached.name;
            string uid = Uid(region.Id, reference);
            Item item = thing.GetComponent<Item>();
            if (item == null)
                throw new System.InvalidOperationException("it isn't an item in this game, perhaps after a game update");
            item.SetHolderUID(new UID(uid));
            foreach (MonoBehaviour script in thing.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var holder = script as IUIDHolder;
                if (holder != null && script != item && !holder.HolderUID.IsNull)
                    holder.SetHolderUID(new UID(holder.HolderUID.Value + "_" + uid));
            }
            if (reference.contents != null)
                s_contents[uid] = reference.contents;

            thing.transform.SetParent(null, false);
            thing.transform.SetPositionAndRotation(reference.position, Quaternion.Euler(reference.rotation));
            thing.SetActive(true);
            KitPlugin.Detail($"'{thing.name}' placed at {reference.position}, as {uid}.");
        }

        /// <summary>This region's chests and gathering spots, once they've woken.</summary>
        internal static List<Item> InWorld()
        {
            return ItemManager.Instance.WorldItems.Keys.Where(u => u.StartsWith(UID_PREFIX)).Select(u => ItemManager.Instance.WorldItems[u]).Where(i => i != null).ToList();
        }

        internal static void AddChecks(Region region, System.Action<string, bool, string> check)
        {
            if (region.Manifest.interactives.Count == 0)
                return;
            List<Item> here = InWorld();
            check("every chest and gathering spot arrived", here.Count == region.Manifest.interactives.Count, $"{here.Count} of {region.Manifest.interactives.Count}");
            check("each can be used", here.All(i => i.HasInteractionTrigger), string.Join(", ", here.Select(i => i.name).ToArray()));
        }

        // As the game's own loot arrives. Going through the parent is what makes it save with the chest.
        private static void AddToChest(TreasureChest chest, int id, int amount)
        {
            while (amount > 0)
            {
                Item item = ItemManager.Instance.GenerateItemNetwork(id);
                item.ChangeParent(chest.transform);
                int here = item.HasMultipleUses ? Mathf.Min(amount, item.MaxStackAmount) : 1;
                if (item.HasMultipleUses)
                    item.RemainingAmount = here;
                amount -= here;
            }
        }

        // A chest a modder has filled holds what they chose, instead of rolling the game's loot.
        [HarmonyPatch(typeof(TreasureChest), "ProcessGenerateContent")]
        private static class ContentsPatch
        {
            private static bool Prefix(TreasureChest __instance)
            {
                RegionManifest.Contents contents;
                if (!s_contents.TryGetValue(__instance.UID, out contents))
                    return true;
                if (f_generated(__instance) || PhotonNetwork.isNonMasterClientInRoom)
                    return false;
                foreach (KeyValuePair<int, int> stack in contents.items)
                {
                    if (ResourcesPrefabManager.Instance.GetItemPrefab(stack.Key) == null)
                    {
                        KitPlugin.Log.LogError($"A chest asks for item {stack.Key}, which isn't in this game, so it's left out.");
                        continue;
                    }
                    AddToChest(__instance, stack.Key, stack.Value);
                }
                if (contents.silver > 0)
                    __instance.AddSilver(contents.silver);
                f_generated(__instance) = true;
                f_changed(__instance) = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(TreasureChest), "CanGenerateContent", MethodType.Getter)]
        private static class CanGeneratePatch
        {
            private static void Postfix(TreasureChest __instance, ref bool __result)
            {
                if (s_contents.ContainsKey(__instance.UID) && !f_generated(__instance))
                    __result = true;
            }
        }
    }
}
