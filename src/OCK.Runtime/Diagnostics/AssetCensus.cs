using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Compares loaded assets before and after extraction to show what it really keeps alive, since
    /// process memory also counts whatever Unity has freed but not returned to Windows. Sizes are
    /// estimates: release builds don't report them.
    /// </summary>
    internal class AssetCensus
    {
        private const int TOP_COUNT = 10;

        private const int MAX_OBJECTS_WALKED = 3000000;
        private const int TOP_HOLDERS = 15;

        private readonly HashSet<int> m_seen = new HashSet<int>();
        private readonly Dictionary<string, int> m_countsByType = new Dictionary<string, int>();
        private readonly long[] m_memoryBefore = ReadMemory();

        public static AssetCensus Take()
        {
            var census = new AssetCensus();
            foreach (Object asset in All())
                census.m_seen.Add(asset.GetInstanceID());
            foreach (Character character in Resources.FindObjectsOfTypeAll<Character>())
                census.m_seen.Add(character.GetInstanceID());
            foreach (IGrouping<string, Object> group in Resources.FindObjectsOfTypeAll<Object>().GroupBy(o => o.GetType().Name))
                census.m_countsByType[group.Key] = group.Count();
            return census;
        }

        private static long[] ReadMemory()
        {
            return new[]
            {
                UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),
                UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong(),
                UnityEngine.Profiling.Profiler.GetTotalUnusedReservedMemoryLong(),
                UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(),
                UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong(),
            };
        }

        public void LogReport()
        {
            LogDifference();
            LogHolders();
        }

        /// <summary>
        /// Splits what extraction kept between Unity and script memory, then looks for what holds the kept textures:
        /// live renderers, grouped by their root object, and script fields, walked from every static field and script.
        /// Script fields count even on destroyed objects, so a list that outlives a donor scene can pin its art.
        /// </summary>
        private void LogHolders()
        {
            long[] now = ReadMemory();
            string[] labels = { "Unity allocated", "Unity reserved", "Unity reserved but free", "script used", "script heap" };
            for (int i = 0; i < labels.Length; i++)
                KitPlugin.Log.LogMessage($"Memory, {labels[i]}: {now[i] / (1024 * 1024)} MB, {(now[i] - m_memoryBefore[i]) / (1024 * 1024):+0;-0;0} MB since before the donors.");

            IEnumerable<string> growth = Resources.FindObjectsOfTypeAll<Object>().GroupBy(o => o.GetType().Name)
                .Select(g => new { g.Key, Added = g.Count() - (m_countsByType.ContainsKey(g.Key) ? m_countsByType[g.Key] : 0) })
                .Where(g => g.Added > 0).OrderByDescending(g => g.Added).Take(TOP_HOLDERS).Select(g => g.Key + " +" + g.Added);
            KitPlugin.Log.LogMessage("Objects added, by type: " + string.Join(", ", growth.ToArray()));

            Dictionary<int, Texture> kept = Resources.FindObjectsOfTypeAll<Texture>().Where(t => !m_seen.Contains(t.GetInstanceID())).ToDictionary(t => t.GetInstanceID());
            var claimed = new HashSet<int>();

            var byRoot = new Dictionary<string, HashSet<int>>();
            foreach (Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                string owner = (renderer.gameObject.scene.IsValid() ? "" : "asset ") + renderer.transform.root.name;
                foreach (Material material in renderer.sharedMaterials)
                    Claim(material, kept, owner, byRoot, claimed);
            }
            LogTallies("Held by renderers under", byRoot, kept);

            var byField = new Dictionary<string, HashSet<int>>();
            var destroyed = new Dictionary<string, int>();
            int walked = WalkScripts(kept, byField, destroyed, claimed);
            LogTallies("Held by script field", byField, kept);
            KitPlugin.Log.LogMessage($"Walked {walked} script objects. Destroyed objects still referenced: {string.Join(", ", destroyed.OrderByDescending(d => d.Value).Take(TOP_HOLDERS).Select(d => d.Key + " " + d.Value).ToArray())}");

            List<Texture> unclaimed = kept.Values.Where(t => !claimed.Contains(t.GetInstanceID())).OrderByDescending(Estimate).ToList();
            KitPlugin.Log.LogMessage($"Kept textures nothing was found holding: {unclaimed.Count}, about {unclaimed.Sum(Estimate) / (1024 * 1024)} MB. Largest: {string.Join(", ", unclaimed.Take(TOP_COUNT).Select(t => t.name).ToArray())}");
        }

        private static void Claim(Material material, Dictionary<int, Texture> kept, string owner, Dictionary<string, HashSet<int>> tally, HashSet<int> claimed)
        {
            if (material == null)
                return;
            foreach (string property in material.GetTexturePropertyNames())
                Claim(material.GetTexture(property), kept, owner, tally, claimed);
        }

        private static void Claim(Texture texture, Dictionary<int, Texture> kept, string owner, Dictionary<string, HashSet<int>> tally, HashSet<int> claimed)
        {
            if (texture == null || !kept.ContainsKey(texture.GetInstanceID()))
                return;
            HashSet<int> ids;
            if (!tally.TryGetValue(owner, out ids))
                tally[owner] = ids = new HashSet<int>();
            ids.Add(texture.GetInstanceID());
            claimed.Add(texture.GetInstanceID());
        }

        private static void LogTallies(string title, Dictionary<string, HashSet<int>> tally, Dictionary<int, Texture> kept)
        {
            foreach (KeyValuePair<string, HashSet<int>> entry in tally.OrderByDescending(e => e.Value.Sum(id => Estimate(kept[id]))).Take(TOP_HOLDERS))
                KitPlugin.Log.LogMessage($"{title} {entry.Key}: {entry.Value.Count} textures, about {entry.Value.Sum(id => Estimate(kept[id])) / (1024 * 1024)} MB, including {string.Join(", ", entry.Value.Take(3).Select(id => kept[id].name).ToArray())}.");
        }

        // Each root keeps what it reaches first, so a tally names one holder rather than everything that could reach it.
        private static int WalkScripts(Dictionary<int, Texture> kept, Dictionary<string, HashSet<int>> tally, Dictionary<string, int> destroyed, HashSet<int> claimed)
        {
            var visited = new HashSet<object>(ReferenceComparer.Instance);
            var stack = new Stack<object>();

            foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies().Where(IsScriptAssembly))
                foreach (System.Type type in HarmonyLib.AccessTools.GetTypesFromAssembly(assembly).Where(t => !t.ContainsGenericParameters))
                    foreach (System.Reflection.FieldInfo field in type.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        if (field.IsLiteral || !IsWalkable(field.FieldType))
                            continue;
                        object value = null;
                        try { value = field.GetValue(null); } catch (System.Exception) { continue; }
                        if (value != null)
                            Walk(value, type.Name + "." + field.Name, visited, stack, kept, tally, destroyed, claimed);
                    }

            foreach (MonoBehaviour script in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                Walk(script, "script on " + script.transform.root.name + ": " + script.GetType().Name, visited, stack, kept, tally, destroyed, claimed);
            return visited.Count;
        }

        private static void Walk(object start, string owner, HashSet<object> visited, Stack<object> stack, Dictionary<int, Texture> kept, Dictionary<string, HashSet<int>> tally, Dictionary<string, int> destroyed, HashSet<int> claimed)
        {
            stack.Push(start);
            while (stack.Count > 0 && visited.Count < MAX_OBJECTS_WALKED)
            {
                object current = stack.Pop();
                System.Type type = current.GetType();
                if (!IsWalkable(type) || (!type.IsValueType && !visited.Add(current)))
                    continue;

                var unityObject = current as Object;
                if (unityObject != null)
                {
                    var texture = current as Texture;
                    var material = current as Material;
                    var renderer = current as Renderer;
                    if (texture != null)
                        Claim(texture, kept, owner, tally, claimed);
                    else if (material != null)
                        Claim(material, kept, owner, tally, claimed);
                    else if (renderer != null)
                        foreach (Material shared in renderer.sharedMaterials)
                            Claim(shared, kept, owner, tally, claimed);
                }
                else if (current is Object)
                {
                    // Unity says it's gone, but the script object remains, and its fields still count.
                    int count;
                    destroyed.TryGetValue(type.Name, out count);
                    destroyed[type.Name] = count + 1;
                }

                if (current is Object && !(current is MonoBehaviour) && !(current is ScriptableObject))
                    continue; // no script fields worth following

                var array = current as System.Array;
                if (array != null)
                {
                    if (IsWalkable(type.GetElementType()))
                        foreach (object element in array)
                            if (element != null)
                                stack.Push(element);
                    continue;
                }

                for (System.Type t = type; t != null && t != typeof(object) && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject); t = t.BaseType)
                    foreach (System.Reflection.FieldInfo field in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        if (!IsWalkable(field.FieldType))
                            continue;
                        object value = field.GetValue(current);
                        if (value != null)
                            stack.Push(value);
                    }
            }
        }

        private static bool IsScriptAssembly(System.Reflection.Assembly assembly)
        {
            string name = assembly.GetName().Name;
            return !name.StartsWith("System") && !name.StartsWith("mscorlib") && !name.StartsWith("Mono") && !name.StartsWith("UnityEngine") && !name.StartsWith("netstandard");
        }

        private static bool IsWalkable(System.Type type)
        {
            if (type.IsPrimitive || type.IsEnum || type.IsPointer || type == typeof(string) || type == typeof(decimal) || type == typeof(System.IntPtr) || type == typeof(System.UIntPtr))
                return false;
            if (typeof(System.Type).IsAssignableFrom(type) || typeof(System.Reflection.MemberInfo).IsAssignableFrom(type) || typeof(System.Reflection.Assembly).IsAssignableFrom(type) || typeof(System.Threading.Thread).IsAssignableFrom(type))
                return false;
            // Plain Unity value types hold no references.
            return !type.IsValueType || type.Namespace == null || !type.Namespace.StartsWith("UnityEngine");
        }

        private class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            bool IEqualityComparer<object>.Equals(object a, object b)
            {
                return ReferenceEquals(a, b);
            }

            int IEqualityComparer<object>.GetHashCode(object o)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
            }
        }

        private static IEnumerable<Object> All()
        {
            return Resources.FindObjectsOfTypeAll<Texture>().Cast<Object>()
                .Concat(Resources.FindObjectsOfTypeAll<Mesh>())
                .Concat(Resources.FindObjectsOfTypeAll<AudioClip>());
        }

        private void LogDifference()
        {
            List<Object> added = All().Where(a => !m_seen.Contains(a.GetInstanceID())).ToList();

            foreach (IGrouping<string, Object> group in added.GroupBy(Kind))
                KitPlugin.Log.LogMessage($"Kept {group.Key}: {group.Count()} assets, about {group.Sum(Estimate) / (1024 * 1024)} MB.");

            string largest = string.Join(", ", added.OrderByDescending(Estimate).Take(TOP_COUNT)
                .Select(a => $"{a.name} ({Estimate(a) / (1024 * 1024)} MB)").ToArray());
            KitPlugin.Log.LogMessage("Largest kept: " + largest);

            List<Character> characters = Resources.FindObjectsOfTypeAll<Character>().Where(c => !m_seen.Contains(c.GetInstanceID()) && c.gameObject.scene.IsValid()).ToList();
            KitPlugin.Log.LogMessage($"Characters left behind by donors: {characters.Count}. {string.Join(", ", characters.Take(TOP_COUNT).Select(c => c.name).ToArray())}");
        }

        private static string Kind(Object asset)
        {
            return asset is Mesh ? "meshes" : asset is AudioClip ? "audio" : "textures";
        }

        private static long Estimate(Object asset)
        {
            var mesh = asset as Mesh;
            if (mesh != null)
                return mesh.vertexCount * 32L;

            var clip = asset as AudioClip;
            if (clip != null)
                return clip.samples * (long)clip.channels * 2L; // the uncompressed size, so an upper limit

            var cubemap = asset as Cubemap;
            if (cubemap != null)
                return cubemap.width * (long)cubemap.height * 6L * 4L / 3L * BytesPerPixelTimesTwo(cubemap.format) / 2L;

            var texture2D = asset as Texture2D;
            if (texture2D != null)
                return texture2D.width * (long)texture2D.height * 4L / 3L * BytesPerPixelTimesTwo(texture2D.format) / 2L;

            var texture = (Texture)asset;
            return texture.width * (long)texture.height * 4L;
        }

        private static long BytesPerPixelTimesTwo(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.DXT1:
                case TextureFormat.BC4:
                    return 1L;
                case TextureFormat.DXT5:
                case TextureFormat.BC5:
                case TextureFormat.BC6H:
                case TextureFormat.BC7:
                    return 2L;
                case TextureFormat.RGBAHalf:
                    return 16L;
                case TextureFormat.RGBAFloat:
                    return 32L;
                default:
                    return 8L;
            }
        }
    }
}
