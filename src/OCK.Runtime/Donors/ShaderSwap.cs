using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Embedded props arrive with placeholder shaders, because the real ones can't be extracted. A placeholder
    /// has the real shader's name and properties, so swapping in the real one by name restores the proper look.
    /// Most game shaders are only in memory while something uses them, so one that isn't comes from the asset cache,
    /// or was collected from a donor scene if any had to load.
    /// </summary>
    internal static class ShaderSwap
    {
        private const string ERROR_SHADER = "Hidden/InternalErrorShader";

        private static readonly Dictionary<string, Shader> s_shaders = new Dictionary<string, Shader>();

        public static void Collect()
        {
            foreach (Shader shader in Resources.FindObjectsOfTypeAll<Shader>())
                if (!s_shaders.ContainsKey(shader.name))
                    s_shaders[shader.name] = shader;
        }

        public static void Apply(Scene scene, Region region)
        {
            var materials = new HashSet<Material>();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null)
                            materials.Add(material);

            var swapped = new HashSet<string>();
            var missing = new HashSet<string>();
            foreach (Material material in materials)
            {
                // The room's own materials use Unity's Standard shader, which works as built.
                string name = material.shader.name;
                // A placeholder that didn't survive the bundle has lost its name, so the region says what it was.
                if (name == ERROR_SHADER && !region.Manifest.embeddedShaders.TryGetValue(material.name, out name))
                    continue;
                if (name == "Standard")
                    continue;

                Shader real;
                if (!s_shaders.TryGetValue(name, out real) && (real = Shader.Find(name)) == null && (real = AssetCache.LoadShader(name)) == null)
                {
                    missing.Add(name);
                    continue;
                }

                int queue = material.renderQueue; // assigning a shader resets this
                material.shader = real;
                material.renderQueue = queue;
                swapped.Add(name);
            }

            KitPlugin.Detail($"Shader swap: {materials.Count} materials, real shaders found for [{string.Join(", ", swapped.ToArray())}], not found for [{string.Join(", ", missing.ToArray())}]. {s_shaders.Count} shaders collected.");
        }
    }
}
