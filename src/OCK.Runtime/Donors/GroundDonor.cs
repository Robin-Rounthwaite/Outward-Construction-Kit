using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// A region's terrain ships with its heights and paint, but its layers arrive without vanilla textures. Those
    /// are collected from donor scenes by name and put back here. Holding them in a static field keeps them loaded.
    /// </summary>
    internal static class GroundDonor
    {
        private static readonly Dictionary<string, Texture2D> s_textures = new Dictionary<string, Texture2D>();

        public static void Harvest(Scene scene)
        {
            var wanted = new HashSet<string>(Region.All.SelectMany(r => r.Manifest.groundLayers).Where(g => g.scene == scene.name)
                .SelectMany(g => new[] { g.diffuse, g.normal }).Where(n => !string.IsNullOrEmpty(n) && !s_textures.ContainsKey(n)));
            if (wanted.Count == 0)
                return;

            foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (wanted.Contains(texture.name))
                    s_textures[texture.name] = texture;

            foreach (string missing in wanted.Where(n => !s_textures.ContainsKey(n)))
                KitPlugin.Log.LogError($"Ground texture '{missing}' not found in {scene.name}, so that layer will be blank.");
        }

        public static void Apply(Scene scene, Region region)
        {
            foreach (Terrain terrain in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Terrain>(true)))
            {
                // New layers rather than edits, so the terrain is certain to notice: a built terrain caches its layers' textures.
                TerrainLayer[] layers = terrain.terrainData.terrainLayers.Select(l => l == null ? null : WithTextures(l, region)).ToArray();
                terrain.terrainData.terrainLayers = layers;
                terrain.terrainData.SetBaseMapDirty(); // the far-away view was generated with blank layers
                terrain.Flush();
            }
        }

        private static TerrainLayer WithTextures(TerrainLayer layer, Region region)
        {
            RegionManifest.GroundLayer source = region.Manifest.groundLayers.FirstOrDefault(g => g.layer == layer.name);
            if (source == null)
                return layer; // the modder's own, which arrived complete

            TerrainLayer copy = Object.Instantiate(layer);
            copy.name = layer.name; // this is how it's found next time, since the terrain outlives the scene
            Texture2D texture = TextureFor(source.scene, source.diffuse);
            if (texture != null)
                copy.diffuseTexture = texture;
            texture = string.IsNullOrEmpty(source.normal) ? null : TextureFor(source.scene, source.normal);
            if (texture != null)
                copy.normalMapTexture = texture;
            return copy;
        }

        private static Texture2D TextureFor(string scene, string name)
        {
            Texture2D texture = AssetCache.LoadTexture(scene, name);
            if (texture == null)
                s_textures.TryGetValue(name, out texture);
            return texture;
        }
    }
}
