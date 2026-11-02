using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// The checks only the running game can make: after a region has settled, compares what's really in the scene
    /// with what its manifest asked for, and logs a [CHECK] line for each. Nothing here changes the game.
    /// </summary>
    internal static class RegionChecks
    {
        private const float SETTLE_SECONDS = 8f;
        private const float GROUND_SEARCH_DISTANCE = 5f;
        private const int MAX_LISTED = 10;
        private const float PARKED_BELOW_HEIGHT = -1000f; // the game moves players far below the world as they leave

        public static IEnumerator Run(Region region, Scene scene, int propsPlaced)
        {
            while (NetworkLevelLoader.Instance == null || !NetworkLevelLoader.Instance.IsOverallLoadingDone)
                yield return null;
            yield return new WaitForSeconds(SETTLE_SECONDS);
            Character player = CharacterManager.Instance != null ? CharacterManager.Instance.GetFirstLocalCharacter() : null;
            if (!scene.isLoaded || (player != null && player.transform.position.y < PARKED_BELOW_HEIGHT))
            {
                KitPlugin.Log.LogMessage($"[CHECK] {region.Id}: skipped, because the player left within {SETTLE_SECONDS} seconds.");
                yield break;
            }

            int passed = 0;
            int failed = 0;
            System.Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) passed++; else failed++;
                string line = $"[CHECK] {(ok ? "PASS" : "FAIL")} {region.Id}: {name}. {detail}";
                if (ok)
                    KitPlugin.Log.LogMessage(line);
                else
                    KitPlugin.Log.LogError(line);
            };

            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                CheckBasics(region, propsPlaced, check);
                CheckPlayerFooting(check);
                CheckEnemies(region, roots, check);
                RegionItems.AddChecks(region, check);
                InteractiveDonor.AddChecks(region, check);
                CheckGround(region, roots, check);
                CheckWater(region, roots, check);
                CheckReturnDoors(region, check);
                CheckShaders(roots, check);
            }
            catch (System.Exception e)
            {
                check("the checks themselves", false, e.ToString());
            }
            KitPlugin.Log.LogMessage($"[CHECK] {region.Id}: {passed} passed, {failed} failed.");
        }

        private static void CheckBasics(Region region, int propsPlaced, System.Action<string, bool, string> check)
        {
            SpawnPointManager spawns = SpawnPointManager.Instance;
            int spawnCount = spawns != null && spawns.SpawnPoints != null ? spawns.SpawnPoints.Length : 0;
            check("has spawn points", spawnCount > 0, spawnCount + " found");
            check("has an environment", EnvironmentConditions.Instance != null, "from " + region.Manifest.environmentDonor);
            check("has a main camera", Global.MainCamera != null, "");
            check("placed every referenced prop", propsPlaced == region.Manifest.props.Count, $"{propsPlaced} of {region.Manifest.props.Count}");
        }

        private static void CheckPlayerFooting(System.Action<string, bool, string> check)
        {
            Character player = CharacterManager.Instance != null ? CharacterManager.Instance.GetFirstLocalCharacter() : null;
            if (player == null)
                return;
            bool grounded = Physics.Raycast(player.transform.position + Vector3.up, Vector3.down, GROUND_SEARCH_DISTANCE, Global.FullEnvironmentMask, QueryTriggerInteraction.Ignore);
            check("player is standing on something", grounded, "at " + player.transform.position);
        }

        private static void CheckEnemies(Region region, GameObject[] roots, System.Action<string, bool, string> check)
        {
            if (region.Manifest.enemies.Count == 0)
                return;

            List<Character> enemies = roots.SelectMany(r => r.GetComponentsInChildren<Character>(true)).Where(c => c.GetComponent<CharacterAI>() != null).ToList();
            check("placed every enemy", enemies.Count == region.Manifest.enemies.Count, $"{enemies.Count} of {region.Manifest.enemies.Count}");

            AISquadManager squads = AISquadManager.Instance;
            check("enemies have a wake distance", squads != null && (squads.AIEnableDistances.x > 0f || squads.AIEnableDistances.y > 0f), squads != null ? squads.AIEnableDistances.ToString() : "no squad manager");

            int dead = enemies.Count(e => e.IsDead);
            if (dead > 0)
                KitPlugin.Log.LogMessage($"[CHECK] {region.Id}: {dead} enemies are already dead, so their movement can't be checked.");

            foreach (Character enemy in enemies.Where(e => !e.IsDead))
            {
                var ai = enemy.GetComponent<CharacterAI>();
                NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
                check($"'{enemy.name}' can move", !ai.CloseToPlayer || (agent != null && agent.enabled && agent.isOnNavMesh),
                    $"close to player {ai.CloseToPlayer}, agent {(agent != null && agent.enabled ? "on" : "off")}, on NavMesh {agent != null && agent.isOnNavMesh}, at {enemy.transform.position}");
            }
        }

        private static void CheckGround(Region region, GameObject[] roots, System.Action<string, bool, string> check)
        {
            if (region.Manifest.groundLayers.Count == 0)
                return;

            List<Terrain> terrains = roots.SelectMany(r => r.GetComponentsInChildren<Terrain>(true)).ToList();
            check("has its terrain", terrains.Count > 0, terrains.Count + " found");
            int groundLayer = LayerMask.NameToLayer("LargeEnvironment");
            foreach (Terrain terrain in terrains)
            {
                string[] blank = terrain.terrainData.terrainLayers.Where(l => l == null || l.diffuseTexture == null).Select(l => l == null ? "(missing)" : l.name).ToArray();
                check($"terrain '{terrain.name}' has all its textures", blank.Length == 0, blank.Length == 0 ? "" : "blank: " + string.Join(", ", blank));
                var collider = terrain.GetComponent<TerrainCollider>();
                check($"terrain '{terrain.name}' can be walked on", collider != null && collider.enabled && collider.gameObject.layer == groundLayer, collider != null ? "layer " + LayerMask.LayerToName(collider.gameObject.layer) : "no collider");
            }
        }

        private static void CheckWater(Region region, GameObject[] roots, System.Action<string, bool, string> check)
        {
            if (region.Manifest.water.Count == 0)
                return;

            List<WaterRender> planes = roots.SelectMany(r => r.GetComponentsInChildren<WaterRender>(true)).ToList();
            check("placed all its water", planes.Count == region.Manifest.water.Count, $"{planes.Count} of {region.Manifest.water.Count}");
            foreach (WaterRender plane in planes)
            {
                bool splashes = plane.GetComponents<Collider>().Any(c => c.enabled) && (Global.WaterMask & (1 << plane.gameObject.layer)) != 0;
                check($"water at {plane.transform.position} splashes underfoot", splashes, "layer " + LayerMask.LayerToName(plane.gameObject.layer));
            }

            int sources = roots.SelectMany(r => r.GetComponentsInChildren<WaterSource>(true)).Count();
            check("every water can be drunk from", sources == region.Manifest.water.Count, $"{sources} of {region.Manifest.water.Count}");
            foreach (RegionManifest.WaterReference asked in region.Manifest.water.Where(w => !string.IsNullOrEmpty(w.waterType)))
            {
                string[] types = roots.SelectMany(r => r.GetComponentsInChildren<IWaterSource>(true)).Select(w => w.WaterType.ToString()).Distinct().ToArray();
                check($"water asking to be {asked.waterType} is", types.Any(t => t.Equals(asked.waterType, System.StringComparison.OrdinalIgnoreCase)), string.Join(", ", types));
            }
        }

        // A missing or unsupported shader draws purple, and nothing else reports it.
        private static void CheckShaders(GameObject[] roots, System.Action<string, bool, string> check)
        {
            var broken = new HashSet<string>();
            foreach (Renderer renderer in roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
                foreach (Material material in renderer.sharedMaterials.Where(m => m != null))
                    if (material.shader == null || !material.shader.isSupported || material.shader.name == "Hidden/InternalErrorShader")
                        broken.Add($"{renderer.name} ({material.name}: {(material.shader != null ? material.shader.name : "no shader")})");
            check("every material has a working shader", broken.Count == 0, string.Join(", ", broken.Take(MAX_LISTED).ToArray()));
        }

        private static void CheckReturnDoors(Region region, System.Action<string, bool, string> check)
        {
            if (region.ReturnSpawnIndex.Value < 0)
            {
                check("has a way back", false, "its entrance door hasn't been placed");
                return;
            }
            int expected = Mathf.Max(1, region.Manifest.returnDoors.Count);
            int doors = SceneManager.GetActiveScene().GetRootGameObjects().Count(r => r.name == "OCK_ReturnDoor");
            check("has a way back", doors == expected, $"{doors} of {expected} return doors");
        }
    }
}
