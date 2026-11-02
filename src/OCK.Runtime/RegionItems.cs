using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Real items a region places, which players can pick up. The host creates them once the loader has read the area's
    /// save, and the game then saves, restores and syncs them like any item left in the world. So a picked-up item stays
    /// gone until the area resets, and comes back when it does, as the game's own area saves work.
    ///
    /// Which items have been placed is kept in the area's save too, in a field the game no longer reads, so it follows
    /// the save: an item added to a region later still appears for a character who has been there before.
    /// </summary>
    internal static class RegionItems
    {
        private const string UID_PREFIX = "ockitem";
        private const string RECORD_PREFIX = "ockitems:";
        private const char SEPARATOR = ',';
        private const char GONE_SEPARATOR = '|';

        private static HashSet<string> s_placed = new HashSet<string>();
        private static HashSet<string> s_gone = new HashSet<string>();
        private static bool s_environmentRead;
        private static int s_unknown;

        public static void Patch(Harmony harmony)
        {
            harmony.PatchAll(typeof(ReadSavePatch));
            harmony.PatchAll(typeof(ApplySavePatch));
            harmony.PatchAll(typeof(StartReadingPatch));
            harmony.PatchAll(typeof(WriteSavePatch));
            harmony.PatchAll(typeof(PlacePatch));
            harmony.PatchAll(typeof(RemoveOldPatch));
        }

        /// <summary>The same for the same item in the same spot, and different if the item is moved or swapped.</summary>
        internal static string Key(string regionId, RegionManifest.ItemReference item)
        {
            string spot = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1}|{2:0.###},{3:0.###},{4:0.###}", regionId, item.id, item.position.x, item.position.y, item.position.z);
            return Region.Hash(spot).ToString("x8");
        }

        internal static HashSet<string> ReadRecord(string saved)
        {
            if (string.IsNullOrEmpty(saved) || !saved.StartsWith(RECORD_PREFIX))
                return new HashSet<string>();
            string placed = saved.Substring(RECORD_PREFIX.Length).Split(GONE_SEPARATOR)[0];
            return new HashSet<string>(placed.Split(new[] { SEPARATOR }, System.StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>What a modder asked not to come back, which a reset leaves gone: taken items and defeated enemies.</summary>
        internal static HashSet<string> ReadGone(string saved)
        {
            string[] parts = string.IsNullOrEmpty(saved) || !saved.StartsWith(RECORD_PREFIX) ? new string[0] : saved.Split(GONE_SEPARATOR);
            return new HashSet<string>(parts.Length > 1 ? parts[1].Split(new[] { SEPARATOR }, System.StringSplitOptions.RemoveEmptyEntries) : new string[0]);
        }

        internal static string WriteRecord(IEnumerable<string> placed, IEnumerable<string> gone = null)
        {
            string record = RECORD_PREFIX + string.Join(SEPARATOR.ToString(), placed.OrderBy(k => k, System.StringComparer.Ordinal).ToArray());
            string[] goneKeys = gone != null ? gone.OrderBy(k => k, System.StringComparer.Ordinal).ToArray() : new string[0];
            return goneKeys.Length > 0 ? record + GONE_SEPARATOR + string.Join(SEPARATOR.ToString(), goneKeys) : record;
        }

        // Stays in the record once gone, or an item taken before an earlier reset would come back at the next.
        private static IEnumerable<string> GoneNow(Region region)
        {
            var lying = new HashSet<string>(InWorld().Select(i => KeyOfUid(i.UID)));
            IEnumerable<string> items = region.Manifest.items.Where(i => i.once).Select(i => Key(region.Id, i)).Where(k => s_gone.Contains(k) || (s_placed.Contains(k) && !lying.Contains(k)));
            return items.Concat(EnemyDonor.DefeatedForGood(region)).Concat(s_gone.Where(k => k.StartsWith(EnemyDonor.GONE_PREFIX))).Distinct();
        }

        /// <summary>The key inside a UID this kit gave an item, or null for any other item.</summary>
        internal static string KeyOfUid(string uid)
        {
            if (string.IsNullOrEmpty(uid) || !uid.StartsWith(UID_PREFIX))
                return null;
            int end = uid.IndexOf('-', UID_PREFIX.Length);
            return end > UID_PREFIX.Length ? uid.Substring(UID_PREFIX.Length, end - UID_PREFIX.Length) : null;
        }

        private static void PlaceAll(Region region)
        {
            Dictionary<string, RegionManifest.ItemReference> wanted = region.Manifest.items.ToDictionary(i => Key(region.Id, i));
            s_placed.IntersectWith(wanted.Keys); // an item removed and later put back should appear again
            s_gone.RemoveWhere(k => !k.StartsWith(EnemyDonor.GONE_PREFIX) && !wanted.ContainsKey(k));
            s_unknown = 0;
            foreach (KeyValuePair<string, RegionManifest.ItemReference> entry in wanted.Where(w => !s_placed.Contains(w.Key)))
            {
                RegionManifest.ItemReference reference = entry.Value;
                if (ResourcesPrefabManager.Instance.GetItemPrefab(reference.id) == null)
                {
                    s_unknown++;
                    KitPlugin.Log.LogError($"'{region.DisplayName}' asks for item {reference.id}, which isn't in this game, so it's left out.");
                    continue;
                }
                Item item = ItemManager.Instance.GenerateItemNetwork(reference.id);
                // A fresh UID each time, because the last one may still be in a player's bag after a reset.
                item.SetHolderUID(new UID(UID_PREFIX + entry.Key + "-" + UID.Generate()));
                item.transform.SetPositionAndRotation(reference.position, Quaternion.Euler(reference.rotation));
                item.SetStuck(true); // stays where the modder put it, as the game's own placed items do
                s_placed.Add(entry.Key);
                KitPlugin.Detail($"Item {reference.id} ({item.Name}) placed at {reference.position}.");
            }
            if (s_unknown > 0)
                PlayerNotice.Add($"'{region.DisplayName}' is missing {s_unknown} of its items, perhaps after a game update.");
        }

        // A saved item the region no longer has, from a version before the modder moved or removed it.
        private static void RemoveOld(Region region)
        {
            var wanted = new HashSet<string>(region.Manifest.items.Select(i => Key(region.Id, i)));
            foreach (string uid in ItemManager.Instance.WorldItems.Keys.ToArray())
            {
                string key = KeyOfUid(uid);
                Item item = ItemManager.Instance.WorldItems[uid];
                if (key != null && !wanted.Contains(key) && item.IsInWorld && item.OwnerCharacter == null)
                    ItemManager.Instance.DestroyItem(uid);
            }
        }

        /// <summary>This region's items lying in the world right now.</summary>
        internal static List<Item> InWorld()
        {
            return ItemManager.Instance.WorldItems.Keys.Where(u => KeyOfUid(u) != null).Select(u => ItemManager.Instance.WorldItems[u]).Where(i => i != null && i.IsInWorld && !i.IsBeingTaken).ToList();
        }

        internal static void AddChecks(Region region, System.Action<string, bool, string> check)
        {
            if (region.Manifest.items.Count == 0)
                return;
            List<Item> lying = InWorld();
            check("every item exists in this game", s_unknown == 0, $"{s_unknown} unknown");
            check("no more items are lying here than it places", lying.Count <= region.Manifest.items.Count, $"{lying.Count} lying here, of {region.Manifest.items.Count}");
            check("items lying here can be picked up", lying.All(i => i.IsPickable && i.IsWorldDetectable && i.HasInteractionTrigger), string.Join(", ", lying.Select(i => i.Name).ToArray()));
        }

        private static Region RegionHere()
        {
            return Region.ForScene(SceneManager.GetActiveScene().name);
        }

        [HarmonyPatch(typeof(EnvironmentSave), "ProcessDeserialize")]
        private static class ReadSavePatch
        {
            private static void Postfix(EnvironmentSave __instance, object _elementSave)
            {
                __instance.DEPRECATED_MapSaveData = ((EnvironmentSave)_elementSave).DEPRECATED_MapSaveData;
            }
        }

        [HarmonyPatch(typeof(SaveInstance), nameof(SaveInstance.ApplyEnvironment))]
        private static class StartReadingPatch
        {
            private static void Prefix()
            {
                s_placed.Clear();
                s_gone.Clear();
                s_environmentRead = true;
            }
        }

        // True means the area has reset, so nothing in its save was loaded, and everything is placed again.
        [HarmonyPatch(typeof(EnvironmentSave), nameof(EnvironmentSave.ApplyData))]
        private static class ApplySavePatch
        {
            private static void Postfix(EnvironmentSave __instance, bool __result)
            {
                s_gone = ReadGone(__instance.DEPRECATED_MapSaveData);
                // After a reset, what's gone for good counts as placed, so it isn't placed again.
                s_placed = __result ? new HashSet<string>(s_gone) : ReadRecord(__instance.DEPRECATED_MapSaveData);
            }
        }

        [HarmonyPatch(typeof(EnvironmentSave), nameof(EnvironmentSave.PrepareSave))]
        private static class WriteSavePatch
        {
            private static void Postfix(EnvironmentSave __instance)
            {
                Region region = RegionHere();
                __instance.DEPRECATED_MapSaveData = region != null ? WriteRecord(s_placed.Where(k => !k.StartsWith(EnemyDonor.GONE_PREFIX)), GoneNow(region)) : null;
            }
        }

        [HarmonyPatch(typeof(NetworkLevelLoader), nameof(NetworkLevelLoader.MidLoadLevel))]
        private static class PlacePatch
        {
            private static void Postfix()
            {
                bool read = s_environmentRead;
                s_environmentRead = false;
                Region region = RegionHere();
                if (!read || region == null || PhotonNetwork.isNonMasterClientInRoom)
                    return; // a guest gets the host's items through the game's own sync
                try
                {
                    EnemyDonor.RemoveDefeatedForGood(region, s_gone);
                    PlaceAll(region);
                }
                catch (System.Exception e)
                {
                    KitPlugin.Log.LogError($"Couldn't place the items in '{region.DisplayName}': {e}");
                }
            }
        }

        [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.DoneLoadingLevel))]
        private static class RemoveOldPatch
        {
            private static void Prefix()
            {
                Region region = RegionHere();
                if (region == null || PhotonNetwork.isNonMasterClientInRoom)
                    return;
                try
                {
                    RemoveOld(region);
                }
                catch (System.Exception e)
                {
                    KitPlugin.Log.LogError($"Couldn't tidy up old items in '{region.DisplayName}': {e}");
                }
            }
        }
    }
}
