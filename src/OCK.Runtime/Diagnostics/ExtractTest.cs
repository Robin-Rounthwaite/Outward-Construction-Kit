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

        private static bool s_tried;
        private static GameObject s_prefab;

        public static void TryLoad()
        {
            if (s_tried)
                return;
            s_tried = true;

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
