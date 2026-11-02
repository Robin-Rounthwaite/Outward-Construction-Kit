using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Vanilla enemies fetched by reference. As with props, each is cloned from a donor scene under an inactive
    /// parent. Unlike props they keep their scripts, so each placed copy needs its own identity before it wakes,
    /// and they're pulled out of squads, which belong to the donor scene. Enemies are the one thing the
    /// game saves individually, so their IDs are built from the region, enemy and position, which don't change.
    /// </summary>
    internal static class EnemyDonor
    {
        /// <summary>
        /// Photon reads a view ID as (player number x 1000) + slot, so these use player numbers no real player
        /// can have, and the blocks above those the Increased Enemy Count mod uses.
        /// </summary>
        private const int VIEW_ID_BASE = 2110000 * 1000;
        private const int VIEW_IDS_PER_ENEMY = 50;

        private static readonly FieldInfo f_aiStates = AccessTools.Field(typeof(CharacterAI), "m_aiStates");
        private static readonly FieldInfo f_aiStatesRoot = AccessTools.Field(typeof(CharacterAI), "m_aiStatesRoot");

        private static readonly Dictionary<string, GameObject> s_templates = new Dictionary<string, GameObject>();
        private static GameObject s_holder;
        private static Vector2 s_enableDistances = new Vector2(-1f, -1f);
        private static readonly Vector2 s_usualEnableDistances = new Vector2(150f, 50f);

        /// <summary>
        /// Worked out from the enemy's place in the region, because every player's game has to arrive at the same
        /// number for the same enemy, whatever else they've done this session, or the host's messages reach the wrong one.
        /// </summary>
        internal static int ViewId(int enemyIndex, int slot)
        {
            return VIEW_ID_BASE + enemyIndex * VIEW_IDS_PER_ENEMY + slot;
        }

        internal const string GONE_PREFIX = "enemy";

        /// <summary>Stays the same while the enemy does, as RegionItems' keys do.</summary>
        internal static string GoneKey(string regionId, RegionManifest.EnemyReference enemy)
        {
            string spot = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1}|{2:0.###},{3:0.###},{4:0.###}", regionId, enemy.path, enemy.position.x, enemy.position.y, enemy.position.z);
            return GONE_PREFIX + Region.Hash(spot).ToString("x8");
        }

        private static string UidFor(string templateName, Region region, int index)
        {
            return region.Id + "_" + templateName + "_ock_" + region.Id + "_" + index;
        }

        private static IEnumerable<KeyValuePair<RegionManifest.EnemyReference, Character>> ThoseNotRespawning(Region region)
        {
            int index = 0;
            foreach (RegionManifest.EnemyReference reference in region.Manifest.enemies)
            {
                index++;
                if (!reference.noRespawn)
                    continue;
                string name = reference.path.Substring(reference.path.LastIndexOf('/') + 1);
                Character character = CharacterManager.Instance.GetCharacter(UidFor(name, region, index));
                if (character != null)
                    yield return new KeyValuePair<RegionManifest.EnemyReference, Character>(reference, character);
            }
        }

        /// <summary>The enemies lying dead that a modder asked not to come back.</summary>
        internal static IEnumerable<string> DefeatedForGood(Region region)
        {
            return ThoseNotRespawning(region).Where(e => e.Value.IsDead).Select(e => GoneKey(region.Id, e.Key)).ToList();
        }

        /// <summary>After a reset, the game would bring them back, so they're taken out again.</summary>
        internal static void RemoveDefeatedForGood(Region region, HashSet<string> gone)
        {
            foreach (KeyValuePair<RegionManifest.EnemyReference, Character> enemy in ThoseNotRespawning(region).ToList())
                if (gone.Contains(GoneKey(region.Id, enemy.Key)) && !enemy.Value.IsDead)
                {
                    KitLog($"'{enemy.Value.name}' stays defeated, as this region asks.", false);
                    enemy.Value.gameObject.SetActive(false);
                }
        }

        private static string KeyFor(RegionManifest.EnemyReference reference)
        {
            return reference.scene + "|" + reference.path;
        }

        public static void Harvest(Scene scene)
        {
            foreach (RegionManifest.EnemyReference reference in Region.All.SelectMany(r => r.Manifest.enemies).Where(e => e.scene == scene.name))
            {
                if (s_templates.ContainsKey(KeyFor(reference)))
                    continue;

                Transform source = PropDonor.Find(scene, reference.path);
                Character original = source != null ? source.GetComponent<Character>() : null;
                if (original == null)
                {
                    KitLog($"Enemy '{reference.path}' not found in {scene.name}, so it's left out of the region. The game may have changed since this region was exported.", true);
                    continue;
                }

                if (s_holder == null)
                {
                    s_holder = new GameObject("OCK_EnemyDonor");
                    s_holder.SetActive(false);
                    Object.DontDestroyOnLoad(s_holder);
                }

                GameObject template = Object.Instantiate(source.gameObject, s_holder.transform);
                template.name = source.name;
                template.SetActive(true); // stays asleep under the holder
                RestoreAIStates(original, template);
                RemoveDonorTies(template);
                s_templates[KeyFor(reference)] = template;
                ReadEnableDistances(scene);
                KitLog($"Enemy '{source.name}' cloned from {scene.name}, from {PathOf(source)}.", false);
            }
        }

        // Enemies only start moving within these distances of a player. Vanilla scenes set them on their squad
        // manager, and a new one leaves them off. Keeps the largest, so no enemy wakes later than it would at home.
        private static void ReadEnableDistances(Scene scene)
        {
            AISquadManager manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AISquadManager>(true)).FirstOrDefault();
            if (manager != null)
                s_enableDistances = Vector2.Max(s_enableDistances, manager.AIEnableDistances);
        }

        private static string PathOf(Transform t)
        {
            return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        }

        public static void Place(Scene scene, Region region, Transform parent)
        {
            var waiting = new GameObject("OCK_EnemiesWaking");
            waiting.SetActive(false);
            SceneManager.MoveGameObjectToScene(waiting, scene);

            // Each enemy reads these as it wakes, so they have to be set first. A donor scene gives its own, and
            // enemies from the cache use the distances every vanilla scene read so far has set.
            parent.GetComponent<AISquadManager>().AIEnableDistances = s_enableDistances.x > 0f || s_enableDistances.y > 0f ? s_enableDistances : s_usualEnableDistances;
            if (region.Manifest.enemies.Count > 0)
                KitLog($"Enemies wake within {parent.GetComponent<AISquadManager>().AIEnableDistances} of a player.", false);

            int index = 0;
            foreach (RegionManifest.EnemyReference reference in region.Manifest.enemies)
            {
                index++;
                GameObject cached = AssetCache.Load(reference.scene, reference.path);
                GameObject template = cached != null ? TemplateFromCache(cached) : null;
                if (template == null && !s_templates.TryGetValue(KeyFor(reference), out template))
                    continue;

                // One bad enemy mustn't stop the rest of the scene setup, or the loading screen never closes.
                try
                {
                    PlaceOne(template, reference, region, index, waiting.transform, parent);
                }
                catch (System.Exception e)
                {
                    KitLog($"Couldn't place enemy '{template.name}': {e}", true);
                }

                if (cached != null)
                    Object.Destroy(template);
            }
            Object.Destroy(waiting);
        }

        // Unlike a clone from a donor scene, a cached enemy has never woken, so its AI needs no repair.
        private static GameObject TemplateFromCache(GameObject cached)
        {
            if (s_holder == null)
            {
                s_holder = new GameObject("OCK_EnemyDonor");
                s_holder.SetActive(false);
                Object.DontDestroyOnLoad(s_holder);
            }

            GameObject template = Object.Instantiate(cached, s_holder.transform);
            template.name = cached.name;
            template.SetActive(true); // stays asleep under the holder
            RemoveDonorTies(template);
            return template;
        }

        private static void PlaceOne(GameObject template, RegionManifest.EnemyReference reference, Region region, int index, Transform waiting, Transform parent)
        {
            GameObject enemy = Object.Instantiate(template, waiting);
            enemy.name = template.name + "_" + index;

            Character character = enemy.GetComponent<Character>();
            string suffix = "_ock_" + region.Id + "_" + index;
            character.SetUID(new UID(region.Id + "_" + template.name + suffix));

            int slot = 0;
            foreach (PhotonView view in enemy.GetComponentsInChildren<PhotonView>(true))
            {
                if (view.viewID == 0)
                    continue;
                if (slot >= VIEW_IDS_PER_ENEMY)
                    throw new System.InvalidOperationException($"it has more than {VIEW_IDS_PER_ENEMY} network views");
                view.viewID = ViewId(index, slot++);
                view.ownerId = 0; // an area object, so whoever hosts controls it, like the originals
            }
            foreach (MonoBehaviour script in enemy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var holder = script as IUIDHolder;
                if (holder != null && !(script is Character) && !holder.HolderUID.IsNull)
                    holder.SetHolderUID(new UID(holder.HolderUID.Value + suffix));
            }

            enemy.transform.SetParent(parent, false);
            enemy.transform.position = reference.position;
            enemy.transform.rotation = Quaternion.Euler(0f, reference.rotationY, 0f);

            NavMeshHit hit;
            bool onNavMesh = NavMesh.SamplePosition(reference.position, out hit, 3f, NavMesh.AllAreas);
            if (onNavMesh)
                enemy.transform.position = hit.position;
            KitLog($"Enemy '{enemy.name}' placed at {enemy.transform.position}, on the NavMesh: {onNavMesh}.", false);
        }

        // An enemy's AI switches off all but its current behaviour state as it wakes. These switch the
        // originals' back on in the clone, so it registers the full set: otherwise it never starts fighting.
        private static void RestoreAIStates(Character original, GameObject clone)
        {
            var ai = original.GetComponent<CharacterAI>();
            if (ai == null)
                return;

            var root = f_aiStatesRoot.GetValue(ai) as AIRoot;
            if (root != null)
                SetCounterpartActive(original.transform, root.transform, clone.transform);

            var states = f_aiStates.GetValue(ai) as AIState[];
            if (states == null)
                return;
            foreach (AIState state in states)
                if (state != null)
                    SetCounterpartActive(original.transform, state.transform, clone.transform);
        }

        private static void SetCounterpartActive(Transform originalRoot, Transform target, Transform cloneRoot)
        {
            var path = new List<int>();
            for (Transform t = target; t != originalRoot; t = t.parent)
            {
                if (t == null)
                    return;
                path.Add(t.GetSiblingIndex());
            }

            Transform match = cloneRoot;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] >= match.childCount)
                    return;
                match = match.GetChild(path[i]);
            }
            match.gameObject.SetActive(true);
        }

        // Things that tie the clone to the donor scene: its squad, quests, and components its Awake adds again.
        private static void RemoveDonorTies(GameObject clone)
        {
            var ai = clone.GetComponent<CharacterAI>();
            if (ai != null && ai.InstantiateCharAIDisable)
                foreach (CharAIDisable disable in clone.GetComponents<CharAIDisable>())
                    Object.DestroyImmediate(disable);

            foreach (QuestEventOnDeath quest in clone.GetComponentsInChildren<QuestEventOnDeath>(true))
                Object.DestroyImmediate(quest);
            foreach (AISquadMember member in clone.GetComponentsInChildren<AISquadMember>(true))
                Object.DestroyImmediate(member);
        }

        private static void KitLog(string message, bool isError)
        {
            if (isError)
                KitPlugin.Log.LogError(message);
            else
                KitPlugin.Detail(message);
        }
    }
}
