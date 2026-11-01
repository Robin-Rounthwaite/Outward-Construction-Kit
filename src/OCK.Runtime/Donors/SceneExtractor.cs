using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Once per session, behind the first loading screen, loads each donor scene a region references,
    /// takes what it needs and unloads the rest. It runs just before the real level load, when the game
    /// is sitting in its empty transition scene and memory use is lowest. Times and memory are logged.
    /// </summary>
    internal static class SceneExtractor
    {
        private const float LOAD_TIMEOUT_SECONDS = 180f;

        public static bool IsExtracting { get; private set; }
        private static bool s_done;
        private static List<string> s_donors;

        public static void Patch(Harmony harmony)
        {
            harmony.PatchAll(typeof(StartLoadLevelPatch));

            // Each of these reacts to any level load, and the loader's own would think its level had arrived.
            var skip = new HarmonyMethod(typeof(SceneExtractor), nameof(SkipWhileExtracting));
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(NetworkLevelLoader).Assembly))
            {
                if (type.ContainsGenericParameters)
                    continue;
                MethodInfo method = type.GetMethod("OnLevelWasLoaded", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method != null && !method.IsAbstract)
                    harmony.Patch(method, prefix: skip);
            }
        }

        private static bool SkipWhileExtracting()
        {
            return !IsExtracting;
        }

        [HarmonyPatch(typeof(NetworkLevelLoader), "StartLoadLevel")]
        private static class StartLoadLevelPatch
        {
            private static bool Prefix(NetworkLevelLoader __instance, bool ___m_transitionSceneLoaded)
            {
                if (s_done || Region.All.Count == 0)
                    return true;
                if (IsExtracting)
                    return false;
                // This call is about to load the transition scene; wait for the one after it.
                if (__instance.LowMemoryLoading && !___m_transitionSceneLoaded)
                    return true;

                IsExtracting = true;
                KitPlugin.Instance.StartCoroutine(Run(__instance));
                return false;
            }
        }

        private static IEnumerator Run(NetworkLevelLoader loader)
        {
            var total = Stopwatch.StartNew();
            long memoryBefore = PrivateMemory();
            List<StaticSnapshot> statics = StaticSnapshot.TakeAll();
            AssetCensus census = KitPlugin.Instance.MemoryReport.Value ? AssetCensus.Take() : null;

            s_donors = Region.AllDonorScenes().ToList();
            SceneManager.sceneLoaded += OnDonorLoaded;
            try
            {
                foreach (string donor in s_donors)
                {
                    var timer = Stopwatch.StartNew();
                    long memoryStart = PrivateMemory();

                    AsyncOperation load = SceneManager.LoadSceneAsync(donor, LoadSceneMode.Additive);
                    if (load == null)
                    {
                        KitPlugin.Log.LogError("Donor scene '" + donor + "' does not exist.");
                        continue;
                    }
                    float waited = 0f;
                    while (!load.isDone && waited < LOAD_TIMEOUT_SECONDS)
                    {
                        waited += Time.unscaledDeltaTime;
                        yield return null;
                    }
                    long loadedMs = timer.ElapsedMilliseconds;
                    long memoryLoaded = PrivateMemory();

                    Scene scene = SceneManager.GetSceneByName(donor);
                    if (scene.IsValid())
                    {
                        AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                        if (unload != null)
                            yield return unload;
                    }
                    yield return Resources.UnloadUnusedAssets();
                    GC.Collect();

                    KitPlugin.Log.LogMessage($"Donor {donor}: loaded in {loadedMs} ms, unloaded by {timer.ElapsedMilliseconds} ms. Memory: {Mb(memoryLoaded - memoryStart)} MB while loaded, {Mb(PrivateMemory() - memoryStart)} MB kept.");
                }
            }
            finally
            {
                // However this went, the game has to carry on or it sits on the loading screen forever.
                SceneManager.sceneLoaded -= OnDonorLoaded;
                StaticSnapshot.RestoreAll(statics);
                try
                {
                    if (census != null)
                        census.LogReport();
                }
                catch (Exception e)
                {
                    KitPlugin.Log.LogError("Asset census failed: " + e);
                }
                s_done = true;
                IsExtracting = false;
                KitPlugin.Log.LogMessage($"Extraction finished in {total.ElapsedMilliseconds} ms for {Region.All.Count} regions from {s_donors.Count} donor scenes, {Mb(PrivateMemory() - memoryBefore)} MB kept.");
                AccessTools.Method(typeof(NetworkLevelLoader), "StartLoadLevel").Invoke(loader, null);
            }
        }

        // Runs after the scene's Awakes but before its Starts, so switching it off here keeps Start from ever running.
        private static void OnDonorLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Additive || s_donors == null || !s_donors.Contains(scene.name))
                return;
            try
            {
                ShaderSwap.Collect();
                GroundDonor.Harvest(scene);
                WaterDonor.Harvest(scene);
                EnemyDonor.Harvest(scene); // while the originals' AI state can still be read as it woke

                foreach (GameObject root in scene.GetRootGameObjects())
                    root.SetActive(false);

                if (Region.All.Any(r => r.Manifest.environmentDonor == scene.name))
                    EnvironmentDonor.Harvest(scene);
                PropDonor.Harvest(scene);
            }
            catch (Exception e)
            {
                KitPlugin.Log.LogError("Extraction from " + scene.name + " failed: " + e);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCounters
        {
            public uint Size;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern bool K32GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCounters counters, uint size);

        // Process.PrivateMemorySize64 reads as 0 under the game's Mono runtime.
        internal static long PrivateMemory()
        {
            var counters = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf(typeof(ProcessMemoryCounters)) };
            return K32GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size) ? (long)counters.PrivateUsage.ToUInt64() : 0L;
        }

        private static string Mb(long bytes)
        {
            return (bytes / (1024 * 1024)).ToString("+0;-0;0");
        }

        /// <summary>
        /// A donor's Awakes overwrite singletons such as EnvironmentConditions.Instance and Global.AudioManager.
        /// Every static object reference in the game's code is recorded beforehand and put back afterwards.
        /// </summary>
        private struct StaticSnapshot
        {
            private FieldInfo m_field;
            private object m_value;

            public static List<StaticSnapshot> TakeAll()
            {
                var snapshots = new List<StaticSnapshot>();
                foreach (Assembly assembly in new[] { typeof(NetworkLevelLoader).Assembly, typeof(TOD_Sky).Assembly }.Distinct())
                    foreach (Type type in AccessTools.GetTypesFromAssembly(assembly))
                    {
                        if (type.ContainsGenericParameters)
                            continue;
                        foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        {
                            if (field.IsLiteral || field.IsInitOnly || !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                                continue;
                            try
                            {
                                snapshots.Add(new StaticSnapshot { m_field = field, m_value = field.GetValue(null) });
                            }
                            catch (Exception) { } // a type whose static constructor can't run here
                        }
                    }
                return snapshots;
            }

            public static void RestoreAll(List<StaticSnapshot> snapshots)
            {
                var restored = new List<string>();
                foreach (StaticSnapshot snapshot in snapshots)
                {
                    if (ReferenceEquals(snapshot.m_field.GetValue(null), snapshot.m_value))
                        continue;
                    snapshot.m_field.SetValue(null, snapshot.m_value);
                    restored.Add(snapshot.m_field.DeclaringType.Name + "." + snapshot.m_field.Name);
                }
                KitPlugin.Detail($"Restored {restored.Count} statics overwritten by donors: {string.Join(", ", restored.ToArray())}");
            }
        }
    }
}
