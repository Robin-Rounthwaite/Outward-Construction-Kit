using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Vanilla props fetched by reference: the region's manifest names a donor scene and the hierarchy
    /// path of a prop, and the whole prop is cloned from there, including its LODs and colliders.
    ///
    /// Most vanilla props are static-batched: their vertices live in a shared, unreadable mesh, already
    /// in world space. Those can't be moved through their own transform, so each prop gets a batch root
    /// positioned to carry the geometry from where it was in the donor scene to where the region wants it.
    /// </summary>
    internal static class PropDonor
    {
        private struct Template
        {
            public GameObject Object;
            public Vector3 SourcePosition;
            public Quaternion SourceRotation;
        }

        // Keyed by donor scene and path, so a prop used by several regions is only cloned once.
        private static readonly Dictionary<string, Template> s_templates = new Dictionary<string, Template>();
        private static GameObject s_holder;

        private static string KeyFor(RegionManifest.PropReference reference)
        {
            return reference.scene + "|" + reference.path;
        }

        public static void Harvest(Scene scene)
        {
            int used = 0;
            foreach (RegionManifest.PropReference reference in Region.All.SelectMany(r => r.Manifest.props).Where(p => p.scene == scene.name))
            {
                used++;
                if (s_templates.ContainsKey(KeyFor(reference)))
                    continue;

                Transform source = Find(scene, reference.path);
                if (source == null)
                {
                    KitPlugin.Log.LogError($"Prop '{reference.path}' not found in {scene.name}, so it's left out of the region. The game may have changed since this region was exported.");
                    continue;
                }

                string found = DescribeForCheck(source);
                if (!string.IsNullOrEmpty(reference.check) && reference.check != found)
                {
                    KitPlugin.Log.LogError($"'{reference.path}' in {scene.name} isn't the expected prop (expected {reference.check}, found {found}), so it's left out of the region.");
                    continue;
                }

                if (s_holder == null)
                {
                    s_holder = new GameObject("OCK_PropDonor");
                    s_holder.SetActive(false);
                    Object.DontDestroyOnLoad(s_holder);
                }

                GameObject template = Object.Instantiate(source.gameObject, s_holder.transform);
                template.name = source.name;
                template.transform.localScale = source.lossyScale;
                template.SetActive(true); // stays asleep under the holder

                // Scripts would wake up in a scene they weren't made for.
                foreach (MonoBehaviour script in template.GetComponentsInChildren<MonoBehaviour>(true).Reverse())
                    Object.DestroyImmediate(script);

                s_templates[KeyFor(reference)] = new Template
                {
                    Object = template,
                    SourcePosition = source.position,
                    SourceRotation = source.rotation,
                };
            }
            if (used > 0)
                KitPlugin.Log.LogMessage($"{used} prop placements across all regions use {scene.name}; {s_templates.Count} distinct props cloned so far.");
        }

        // The same description the exporter wrote, made from the live prop.
        private static string DescribeForCheck(Transform source)
        {
            List<MeshFilter> drawn = source.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && f.GetComponent<MeshRenderer>() != null).ToList();
            return (drawn.Count > 0 ? drawn[0].name : "") + ":" + drawn.Count;
        }

        public static int Place(Scene scene, Region region)
        {
            int placed = 0;
            foreach (RegionManifest.PropReference reference in region.Manifest.props)
            {
                Template template;
                if (!s_templates.TryGetValue(KeyFor(reference), out template))
                    continue; // already reported when the donor scene was read

                // One bad prop mustn't stop the rest of the scene setup, or the loading screen never closes.
                try
                {
                    PlaceCopy(scene, template.Object, template.SourcePosition, template.SourceRotation, reference.position, reference.rotation);
                    placed++;
                }
                catch (System.Exception e)
                {
                    KitPlugin.Log.LogError($"Couldn't place '{template.Object.name}': {e}");
                }
            }
            return placed;
        }

        internal static GameObject PlaceCopy(Scene scene, GameObject source, Vector3 sourcePosition, Quaternion sourceRotation, Vector3 position, Vector3 rotation)
        {
            GameObject prop = Object.Instantiate(source);
            prop.name = source.name;
            SceneManager.MoveGameObjectToScene(prop, scene);

            Quaternion turn = Quaternion.Euler(rotation);
            prop.transform.position = position;
            prop.transform.rotation = turn * sourceRotation;

            Transform batchRoot = null;
            int batched = 0;
            Renderer[] renderers = prop.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                renderer.lightmapIndex = -1; // the baked lighting belongs to the donor scene
                if (!renderer.isPartOfStaticBatch)
                    continue;

                if (batchRoot == null)
                {
                    batchRoot = new GameObject(prop.name + "_BatchRoot").transform;
                    SceneManager.MoveGameObjectToScene(batchRoot.gameObject, scene);
                    batchRoot.rotation = turn;
                    batchRoot.position = position - turn * sourcePosition;
                }
                // Exists on Renderer but isn't public.
                AccessTools.Property(typeof(Renderer), "staticBatchRootTransform").SetValue(renderer, batchRoot, null);

                // Unity draws batched renderers sharing a mesh and material together, under one renderer's root,
                // which puts repeated props in the wrong place. Reading .materials gives this one its own copies.
                renderer.materials = renderer.materials;
                batched++;
            }

            KitPlugin.Log.LogMessage($"Prop '{prop.name}' placed at {prop.transform.position}, turned {rotation}: {renderers.Length} renderers ({batched} batched), {prop.GetComponentsInChildren<Collider>(true).Length} colliders, LOD group {prop.GetComponent<LODGroup>() != null}.");
            return prop;
        }

        internal static Transform Find(Scene scene, string path)
        {
            string[] parts = path.Split(new[] { '/' }, 2);
            GameObject root = System.Array.Find(scene.GetRootGameObjects(), r => r.name == parts[0]);
            if (root == null)
                return null;
            return parts.Length == 1 ? root.transform : root.transform.Find(parts[1]);
        }
    }
}
