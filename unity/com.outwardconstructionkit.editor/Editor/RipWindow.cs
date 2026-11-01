using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lists the game's scenes and rips one on a click. The rip runs in a window of its own, so Unity stays usable and a
/// reload here can't interrupt it. The script holds the actual work, and can be run by hand.
/// </summary>
internal class RipWindow : EditorWindow
{
    public const string SCRIPT = "Packages/com.outwardconstructionkit.editor/Tools~/rip-scene.ps1";
    private const int LOW_DISK_GB = 20;

    private List<string> m_scenes = new List<string>();
    private HashSet<string> m_ripped = new HashSet<string>();
    private string m_search = "";
    private Vector2 m_scroll;

    [MenuItem("Construction Kit/Rip a Scene")]
    public static void Open()
    {
        GetWindow<RipWindow>(false, "Rip a Scene").minSize = new Vector2(380f, 300f);
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void OnFocus()
    {
        Refresh();
    }

    private void Refresh()
    {
        m_scenes = OckSettings.BuildSceneNames();
        m_ripped = new HashSet<string>(OckSettings.RippedScenes().Keys);
    }

    private void OnGUI()
    {
        if (!OckSettings.PathsConfirmed || string.IsNullOrEmpty(OckSettings.RipFolder))
        {
            EditorGUILayout.HelpBox("First, choose a rip folder in the paths window.", MessageType.Info);
            if (GUILayout.Button("Set Up Paths"))
                PathsWindow.Open();
            return;
        }

        EditorGUILayout.HelpBox("Ripping copies a scene's models out of your own game files, for the Prop Browser to show. A scene usually takes 10 to 30 minutes and several gigabytes, up to about 15. It runs in its own window, so you can keep working.", MessageType.Info);
        m_search = EditorGUILayout.TextField("Search", m_search);
        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        for (int i = 0; i < m_scenes.Count; i++)
        {
            string scene = m_scenes[i];
            if (m_search.Length > 0 && scene.IndexOf(m_search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(scene);
                if (m_ripped.Contains(scene))
                    EditorGUILayout.LabelField("ripped", EditorStyles.miniLabel, GUILayout.Width(60f));
                else if (GUILayout.Button("Rip", GUILayout.Width(60f)))
                    Rip(scene, i);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private static void Rip(string scene, int level)
    {
        string message = $"Rip {scene}? It usually takes 10 to 30 minutes and several gigabytes, in {OckSettings.RipFolder}.";
        if (!Directory.Exists(Path.Combine(OckSettings.RipFolder, "_AssetRipper")))
            message += "\n\nThis first time, it also downloads AssetRipper 2.0.0 from GitHub, about 42 MB, and keeps it in that folder.";
        long free = FreeGigabytes(OckSettings.RipFolder);
        if (free >= 0 && free < LOW_DISK_GB)
            message += $"\n\nThat drive has only {free} GB free, which may not be enough.";
        if (!EditorUtility.DisplayDialog("Rip a Scene", message, "Rip", "Cancel"))
            return;

        string arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.GetFullPath(SCRIPT)}\" -GameData \"{OckSettings.GameData}\" -Level {level} -Scene \"{scene}\" -RipFolder \"{OckSettings.RipFolder}\"";
        Process.Start(new ProcessStartInfo("powershell.exe", arguments) { UseShellExecute = true });
    }

    private static long FreeGigabytes(string folder)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))).AvailableFreeSpace / (1024L * 1024L * 1024L);
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
