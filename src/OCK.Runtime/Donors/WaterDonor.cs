using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Water is the donor scene's own main water plane, with its material and its WaterRender script, which draws
    /// the reflections. Its waves are sized by its transform's scale and mesh UVs, so shrinking it would shrink them
    /// too. It keeps its huge vanilla scale instead, on a small mesh of our own laid out the way the original is.
    /// A flat collider on the water layer gives splashing footsteps, since characters look for water with a ray.
    /// Drinking and filling come from the scene's own water source, stretched to cover the plane.
    /// </summary>
    internal static class WaterDonor
    {
        // The vanilla plane's UVs, read from its mesh: u = 0.5 - x * this, and v = 0.5 - z * this.
        private const float UV_PER_MESH_UNIT = 0.041455f;
        private const int GRID_CELLS = 8;
        private const float SPLASH_COLLIDER_HEIGHT = 0.02f;
        private const float DRINK_ZONE_HEIGHT = 4f;
        private const string WATER_LAYER = "Water";

        private static readonly Dictionary<string, GameObject> s_templates = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, GameObject> s_sources = new Dictionary<string, GameObject>();
        private static GameObject s_holder;

        // Called while the scene is still active, since bounds aren't reliable on inactive renderers.
        public static void Harvest(Scene scene)
        {
            if (s_templates.ContainsKey(scene.name) || !Region.All.Any(r => r.Manifest.water.Any(w => w.scene == scene.name)))
                return;

            WaterRender source = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WaterRender>(true))
                .Where(w => w.GetComponent<MeshFilter>() != null && w.GetComponent<Renderer>() != null)
                .OrderByDescending(w => w.GetComponent<Renderer>().bounds.size.sqrMagnitude).FirstOrDefault();
            if (source == null)
            {
                KitPlugin.Log.LogError($"No water plane found in {scene.name}, so water using it is left out of the region.");
                return;
            }

            GameObject template = Object.Instantiate(source.gameObject, Holder().transform);
            template.name = source.name;
            template.transform.localScale = source.transform.lossyScale;
            foreach (MonoBehaviour script in template.GetComponentsInChildren<MonoBehaviour>(true).Reverse())
                if (!(script is WaterRender))
                    Object.DestroyImmediate(script);
            // These belong to the donor scene, and Start reads them without checking.
            template.GetComponent<WaterRender>().BoundsColliders = new BoxCollider[0];

            s_templates[scene.name] = template;
            Renderer renderer = template.GetComponent<Renderer>();
            KitPlugin.Detail($"Water '{source.name}' cloned from {scene.name}: material {renderer.sharedMaterial.name}, shader {renderer.sharedMaterial.shader.name}, layer {LayerMask.LayerToName(source.gameObject.layer)}, batched {renderer.isPartOfStaticBatch}.");

            WaterSource drinkable = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WaterSource>(true)).FirstOrDefault();
            if (drinkable == null)
            {
                KitPlugin.Log.LogWarning($"{scene.name} has no water source, so its water can't be drunk or collected.");
                return;
            }
            GameObject sourceTemplate = Object.Instantiate(drinkable.gameObject, s_holder.transform);
            sourceTemplate.name = drinkable.name;
            sourceTemplate.SetActive(true); // stays asleep under the holder
            s_sources[scene.name] = sourceTemplate;
            KitPlugin.Detail($"Water source '{drinkable.name}' cloned from {scene.name}: {drinkable.WaterType} water, drinkable directly {drinkable.CanDrinkDirectly}, {sourceTemplate.GetComponentsInChildren<BoxCollider>(true).Length} zones.");
        }

        public static void Place(Scene scene, Region region)
        {
            var temporary = new List<GameObject>();
            int index = 0;
            foreach (RegionManifest.WaterReference reference in region.Manifest.water)
            {
                index++;
                GameObject template = TemplateFromCache(reference.scene, temporary);
                if (template == null && !s_templates.TryGetValue(reference.scene, out template))
                    continue;

                // One bad plane mustn't stop the rest of the scene setup, or the loading screen never closes.
                try
                {
                    GameObject water = Object.Instantiate(template);
                    water.name = template.name;
                    SceneManager.MoveGameObjectToScene(water, scene);
                    Vector3 scale = template.transform.localScale;
                    water.GetComponent<MeshFilter>().sharedMesh = BuildMesh(reference.size.x / scale.x, reference.size.z / scale.z);
                    water.transform.position = reference.position;
                    water.transform.rotation = Quaternion.Euler(0f, reference.rotationY, 0f);
                    water.transform.localScale = scale;
                    water.GetComponent<Renderer>().lightmapIndex = -1;
                    AddSplashCollider(water, reference.size.x / scale.x, reference.size.z / scale.z);
                    AddWaterSource(scene, region, reference, index);
                    KitPlugin.Detail($"Water placed at {reference.position}, {reference.size.x} by {reference.size.z} m, at scale {scale}.");
                }
                catch (System.Exception e)
                {
                    KitPlugin.Log.LogError($"Couldn't place water from {reference.scene}: {e}");
                }
            }
            foreach (GameObject template in temporary)
                Object.Destroy(template);
        }

        private static GameObject Holder()
        {
            if (s_holder == null)
            {
                s_holder = new GameObject("OCK_WaterDonor");
                s_holder.SetActive(false);
                Object.DontDestroyOnLoad(s_holder);
            }
            return s_holder;
        }

        // A cached plane is as it was in its level, so it gets the same treatment as one cloned from a donor scene.
        private static GameObject TemplateFromCache(string scene, List<GameObject> temporary)
        {
            GameObject cached = AssetCache.Load(scene, RegionManifest.WATER_KEY);
            if (cached == null)
                return null;

            GameObject template = Object.Instantiate(cached, Holder().transform);
            template.name = cached.name;
            template.transform.localScale = cached.transform.lossyScale;
            template.SetActive(true); // stays asleep under the holder
            foreach (MonoBehaviour script in template.GetComponentsInChildren<MonoBehaviour>(true).Reverse())
                if (!(script is WaterRender))
                    Object.DestroyImmediate(script);
            template.GetComponent<WaterRender>().BoundsColliders = new BoxCollider[0]; // Start reads these without checking
            temporary.Add(template);
            return template;
        }

        private static void AddSplashCollider(GameObject water, float width, float length)
        {
            if ((Global.WaterMask & (1 << water.layer)) == 0)
                water.layer = LayerMask.NameToLayer(WATER_LAYER);

            BoxCollider box = water.GetComponent<BoxCollider>();
            if (box == null)
                box = water.AddComponent<BoxCollider>();
            box.size = new Vector3(width, SPLASH_COLLIDER_HEIGHT, length);
            box.isTrigger = true;
        }

        // Built asleep, because a trigger registers its ID as it wakes. The IDs only need to be stable and unique.
        private static void AddWaterSource(Scene scene, Region region, RegionManifest.WaterReference reference, int index)
        {
            GameObject template = AssetCache.Load(reference.scene, RegionManifest.WATER_SOURCE_KEY);
            if (template == null && !s_sources.TryGetValue(reference.scene, out template))
                return;

            GameObject source = Object.Instantiate(template, Holder().transform);
            source.SetActive(true); // stays asleep under the holder
            source.name = template.name + "_" + index;
            if (!string.IsNullOrEmpty(reference.waterType))
            {
                try
                {
                    var type = (WaterType)System.Enum.Parse(typeof(WaterType), reference.waterType, true);
                    foreach (WaterSource water in source.GetComponentsInChildren<WaterSource>(true))
                        water.WaterType = type;
                }
                catch (System.ArgumentException)
                {
                    KitPlugin.Log.LogError($"'{reference.waterType}' isn't a type of water, so the water at {reference.position} keeps its scene's own.");
                }
            }

            string suffix = "_ock_" + region.Id + "_water" + index;
            foreach (MonoBehaviour script in source.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var holder = script as IUIDHolder;
                if (holder != null)
                    holder.SetHolderUID(new UID((holder.HolderUID.IsNull ? region.Id + "_" + script.GetType().Name : holder.HolderUID.Value) + suffix));
            }

            source.transform.position = reference.position;
            source.transform.rotation = Quaternion.Euler(0f, reference.rotationY, 0f);
            foreach (Transform child in source.GetComponentsInChildren<Transform>(true))
                if (child != source.transform)
                    child.localPosition = Vector3.zero;
            foreach (BoxCollider zone in source.GetComponentsInChildren<BoxCollider>(true))
            {
                zone.center = Vector3.zero;
                zone.size = new Vector3(reference.size.x, DRINK_ZONE_HEIGHT, reference.size.z);
            }

            source.transform.SetParent(null, true); // wakes it
            SceneManager.MoveGameObjectToScene(source, scene);
        }

        private static Mesh BuildMesh(float width, float length)
        {
            int side = GRID_CELLS + 1;
            var vertices = new Vector3[side * side];
            var uvs = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];
            for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    vertices[i] = new Vector3((x / (float)GRID_CELLS - 0.5f) * width, 0f, (z / (float)GRID_CELLS - 0.5f) * length);
                    uvs[i] = new Vector2(0.5f - vertices[i].x * UV_PER_MESH_UNIT, 0.5f - vertices[i].z * UV_PER_MESH_UNIT);
                    normals[i] = Vector3.up;
                    tangents[i] = new Vector4(-1f, 0f, 0f, -1f); // as on the original
                }

            var triangles = new int[GRID_CELLS * GRID_CELLS * 6];
            int t = 0;
            for (int z = 0; z < GRID_CELLS; z++)
                for (int x = 0; x < GRID_CELLS; x++)
                {
                    int corner = z * side + x;
                    triangles[t++] = corner;
                    triangles[t++] = corner + side;
                    triangles[t++] = corner + 1;
                    triangles[t++] = corner + 1;
                    triangles[t++] = corner + side;
                    triangles[t++] = corner + side + 1;
                }

            var mesh = new Mesh { name = "OCK_WaterPlane", vertices = vertices, uv = uvs, normals = normals, tangents = tangents, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
