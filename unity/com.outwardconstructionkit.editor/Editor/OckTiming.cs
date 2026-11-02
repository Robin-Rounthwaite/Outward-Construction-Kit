using System;
using System.Diagnostics;
using UnityEditor;
using UnityEditorInternal;

/// <summary>
/// Says in the log when something is slow, so a stall can be traced to its cause. Steps under half a second say
/// nothing. It also notices when Unity as a whole stops updating, which catches stalls no kit step is measuring.
/// </summary>
[InitializeOnLoad]
internal static class OckTiming
{
    private const double QUIET_SECONDS = 0.5;
    private const double BUSY_SECONDS = 5.0;

    private static double s_lastUpdate;
    private static bool s_wasActive;
    private static string s_lastStep = "none yet";

    static OckTiming()
    {
        EditorApplication.update += WatchForBusyEditor;
    }

    /// <summary>Times everything up to the end of a using block.</summary>
    public static IDisposable Measure(string step)
    {
        return new Timer(step);
    }

    // An editor that's in the background can stop updating without being busy, so those gaps don't count.
    private static void WatchForBusyEditor()
    {
        double now = EditorApplication.timeSinceStartup;
        bool active = InternalEditorUtility.isApplicationActive;
        if (s_wasActive && active && now - s_lastUpdate >= BUSY_SECONDS)
        {
            string window = EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.titleContent.text : "none";
            string selected = Selection.activeObject != null ? Selection.activeObject.name : "nothing";
            UnityEngine.Debug.Log($"[OCK timing] Unity was busy for {now - s_lastUpdate:0.0} s. Last kit step: {s_lastStep}. Focused window: {window}. Selected: {selected}.");
        }
        s_lastUpdate = now;
        s_wasActive = active;
    }

    private sealed class Timer : IDisposable
    {
        private readonly string m_step;
        private readonly Stopwatch m_watch = Stopwatch.StartNew();

        public Timer(string step)
        {
            m_step = step;
            s_lastStep = step;
        }

        public void Dispose()
        {
            if (m_watch.Elapsed.TotalSeconds >= QUIET_SECONDS)
                UnityEngine.Debug.Log($"[OCK timing] {m_step}: {m_watch.Elapsed.TotalSeconds:0.0} s.");
        }
    }
}
