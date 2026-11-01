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

        private static readonly FieldInfo f_aiStates = AccessTools.Field(typeof(CharacterAI), "m_aiStates");
        private static readonly FieldInfo f_aiStatesRoot = AccessTools.Field(typeof(CharacterAI), "m_aiStatesRoot");

        private static readonly Dictionary<string, GameObject> s_templates = new Dictionary<string, GameObject>();
        private static GameObject s_holder;
        private static int s_nextViewId = VIEW_ID_BASE;
        private static Vector2 s_enableDistances = new Vector2(-1f, -1f);
        private static readonly Vector2 s_usualEnableDistances = new Vector2(150f, 50f);

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

            foreach (PhotonView view in enemy.GetComponentsInChildren<PhotonView>(true))
            {
                if (view.viewID == 0)
                    continue;
                view.viewID = s_nextViewId++;
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
                KitPlugin.Log.LogMessage(message);
        }
    }
}
