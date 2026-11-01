using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Trial of loading a prop from a bundle built straight from the game's files, whose materials are still
    /// pointers into those files. It loads at the main menu, before any donor scene could have brought the
    /// materials into memory, so a texture showing up here proves the pointers work on their own.
    /// </summary>
    internal static class ExtractTest
    {
        private const string BUNDLE_FOLDER = "OCK_ExtractTest";
        private const string PREFAB_PATH = "assets/ock/extracted.prefab";
        private const float PLACE_DISTANCE = 4f;

        private const string ENEMY_REGION = "test_alpha";
        private const int ENEMY_INDEX = 50; // beyond the region's own enemies, so it gets an ID of its own
        private static readonly Vector3 s_enemyPosition = new Vector3(-8f, 0f, -8f);

        private static bool s_tried;
        private static GameObject s_prefab;
        private static GameObject s_enemy;

        public static void TryLoad()
        {
            if (s_tried)
                return;
            s_tried = true;
            LoadEnemy();

            string file = Path.Combine(Path.Combine(Paths.BepInExRootPath, BUNDLE_FOLDER), "barrels");
            if (!File.Exists(file))
            {
                KitPlugin.Log.LogWarning("[EXTRACT] No test bundle at " + file);
                return;
            }

            long before = SceneExtractor.PrivateMemory();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            AssetBundle bundle = AssetBundle.LoadFromFile(file);
            if (bundle == null)
            {
                KitPlugin.Log.LogError("[EXTRACT] FAIL: Unity refused the bundle.");
                return;
            }
            KitPlugin.Log.LogMessage($"[EXTRACT] Bundle opened in {timer.ElapsedMilliseconds} ms. It lists: {string.Join(", ", bundle.GetAllAssetNames())}");

            s_prefab = bundle.LoadAsset<GameObject>(PREFAB_PATH);
            if (s_prefab == null)
            {
                KitPlugin.Log.LogError("[EXTRACT] FAIL: the prop didn't load from the bundle.");
                return;
            }
            KitPlugin.Log.LogMessage($"[EXTRACT] Prop '{s_prefab.name}' loaded by {timer.ElapsedMilliseconds} ms, at {s_prefab.transform.position}, memory +{(SceneExtractor.PrivateMemory() - before) / (1024 * 1024)} MB, {Time.frameCount} frames in, active scene {SceneManager.GetActiveScene().name}.");

            foreach (Renderer renderer in s_prefab.GetComponentsInChildren<Renderer>(true))
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                KitPlugin.Log.LogMessage($"[EXTRACT] '{renderer.name}': mesh {(filter != null && filter.sharedMesh != null ? filter.sharedMesh.name + " with " + filter.sharedMesh.vertexCount + " vertices" : "MISSING")}, batched {renderer.isPartOfStaticBatch}, {renderer.sharedMaterials.Length} materials.");
                foreach (Material material in renderer.sharedMaterials)
                    KitPlugin.Log.LogMessage(material == null
                        ? "[EXTRACT]   material MISSING: the pointer into the game's file didn't resolve."
                        : $"[EXTRACT]   material '{material.name}', shader '{material.shader.name}', supported {material.shader.isSupported}, textures: {string.Join(", ", material.GetTexturePropertyNames().Select(p => p + "=" + (material.GetTexture(p) != null ? material.GetTexture(p).name + " " + material.GetTexture(p).width : "none")).ToArray())}");
            }
            KitPlugin.Log.LogMessage("[EXTRACT] Load finished. Press the place key in game to see it.");
        }

        // The enemy's objects are a byte-for-byte copy from the level, so anything wrong with those shows up here.
        private static void LoadEnemy()
        {
            string file = Path.Combine(Path.Combine(Paths.BepInExRootPath, BUNDLE_FOLDER), "hyena");
            if (!File.Exists(file))
                return;

            long before = SceneExtractor.PrivateMemory();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            AssetBundle bundle = AssetBundle.LoadFromFile(file);
            if (bundle == null)
            {
                KitPlugin.Log.LogError("[EXTRACT] FAIL: Unity refused the enemy bundle.");
                return;
            }
            s_enemy = bundle.LoadAsset<GameObject>(PREFAB_PATH);
            if (s_enemy == null)
            {
                KitPlugin.Log.LogError("[EXTRACT] FAIL: the enemy didn't load from its bundle.");
                return;
            }
            KitPlugin.Log.LogMessage($"[EXTRACT] Enemy '{s_enemy.name}' loaded in {timer.ElapsedMilliseconds} ms, memory +{(SceneExtractor.PrivateMemory() - before) / (1024 * 1024)} MB, active {s_enemy.activeSelf}.");

            MonoBehaviour[] scripts = s_enemy.GetComponentsInChildren<MonoBehaviour>(true);
            KitPlugin.Log.LogMessage($"[EXTRACT] Scripts: {scripts.Length}, of which missing {scripts.Count(s => s == null)}. Character {s_enemy.GetComponent<Character>() != null}, AI {s_enemy.GetComponent<CharacterAI>() != null}, agent {s_enemy.GetComponent<UnityEngine.AI.NavMeshAgent>() != null}, animator controller {(s_enemy.GetComponentInChildren<Animator>(true) != null && s_enemy.GetComponentInChildren<Animator>(true).runtimeAnimatorController != null)}.");
            foreach (Renderer renderer in s_enemy.GetComponentsInChildren<Renderer>(true))
                KitPlugin.Log.LogMessage($"[EXTRACT] Enemy renderer '{renderer.name}': {string.Join(", ", renderer.sharedMaterials.Select(m => m == null ? "MISSING" : m.name + " on " + m.shader.name).ToArray())}.");
        }

        public static void PlaceEnemy(Scene scene, Region region, Transform parent)
        {
            if (s_enemy == null || region.Id != ENEMY_REGION)
                return;
            long before = SceneExtractor.PrivateMemory();
            EnemyDonor.PlaceExtracted(scene, s_enemy, region, s_enemyPosition, ENEMY_INDEX, parent);
            KitPlugin.Log.LogMessage($"[EXTRACT] Enemy placed in {region.Id}, memory +{(SceneExtractor.PrivateMemory() - before) / (1024 * 1024)} MB.");
        }

        public static void PlaceInFrontOfPlayer()
        {
            Character player = CharacterManager.Instance != null ? CharacterManager.Instance.GetFirstLocalCharacter() : null;
            if (player == null || s_prefab == null)
            {
                KitPlugin.Log.LogWarning("[EXTRACT] Nothing to place, or no player yet.");
                return;
            }

            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            Vector3 position = player.transform.position + forward.normalized * PLACE_DISTANCE;
            GameObject prop = PropDonor.PlaceCopy(SceneManager.GetActiveScene(), s_prefab, s_prefab.transform.position, s_prefab.transform.rotation, position, Vector3.zero);

            int ground = LayerMask.NameToLayer("LargeEnvironment");
            foreach (Collider collider in prop.GetComponentsInChildren<Collider>(true))
                collider.gameObject.layer = ground;
            KitPlugin.Log.LogMessage($"[EXTRACT] Placed at {position}, in scene {prop.scene.name}. Memory now {SceneExtractor.PrivateMemory() / (1024 * 1024)} MB.");
        }
    }
}
