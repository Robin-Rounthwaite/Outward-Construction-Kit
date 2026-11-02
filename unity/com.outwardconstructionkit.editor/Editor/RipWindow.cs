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
    public const string RIP_ALL_SCRIPT = "Packages/com.outwardconstructionkit.editor/Tools~/rip-all-scenes.ps1";
    private const int LOW_DISK_GB = 20;

    // The empty scenes the game passes through, going by their names.
    private static readonly string[] s_nothingToRip = { "MainMenu_Empty", "LowMemory_TransitionScene" };

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
        m_scenes = OckSettings.BuildSceneNames().OrderBy(s => Array.IndexOf(s_nothingToRip, s) >= 0).ToList();
        m_ripped = new HashSet<string>(OckSettings.RippedScenes().Keys);
    }

    private void OnGUI()
    {
        if (!OckSettings.PathsConfirmed || string.IsNullOrEmpty(OckSettings.RipFolder))
        {
            KitStyles.HelpBox("First, choose a rip folder in the paths window.", MessageType.Info);
            if (GUILayout.Button(new GUIContent("Set Up Paths", "Opens the window that says where the game, your mods and your rips are.")))
                PathsWindow.Open();
            return;
        }

        KitStyles.HelpBox("Ripping copies a scene's models out of your own game files, for the Prop Browser to show. Time and disk space aren't yet measured, so choose a drive with plenty of room. Later rips share what's the same with earlier ones. It runs in its own window, so you can keep working, and appears in the Prop Browser when it's done.", MessageType.Info);
        if (GUILayout.Button(new GUIContent("Rip All Scenes", "Rips every scene not yet ripped, one after another. It asks before starting.")))
            RipAll();
        m_search = EditorGUILayout.TextField("Search", m_search);
        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        foreach (string scene in m_scenes)
        {
            string name = LocationNames.For(scene);
            if (m_search.Length > 0 && scene.IndexOf(m_search, StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf(m_search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(name, "The game calls this " + scene + "."));
                if (Array.IndexOf(s_nothingToRip, scene) >= 0)
                    EditorGUILayout.LabelField(new GUIContent("little to offer", "The game only passes through this scene, so it has almost nothing of its own."), KitStyles.Note, GUILayout.Width(95f));
                if (m_ripped.Contains(scene))
                    EditorGUILayout.LabelField("ripped", KitStyles.Note, GUILayout.Width(70f));
                else if (GUILayout.Button(new GUIContent("Rip", "Copies this scene's models out of your own game files, in its own window, so you can keep working."), GUILayout.Width(60f)))
                    RipByName(scene);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    /// <summary>Rips one scene by name, asking first.</summary>
    public static void RipByName(string scene)
    {
        int level = OckSettings.BuildSceneNames().IndexOf(scene);
        if (level >= 0)
            Rip(scene, level);
    }

    private static void Rip(string scene, int level)
    {
        if (string.IsNullOrEmpty(OckSettings.RipFolder))
        {
            PathsWindow.Open();
            return;
        }
        string message = $"Rip {LocationNames.For(scene)} into {OckSettings.RipFolder}? Time and disk space aren't yet measured. Later rips share what's the same with earlier ones.";
        if (WillDownloadRipper())
            message += "\n\nThis first time, it also downloads AssetRipper 2.0.0 from GitHub, about 42 MB, and keeps it in that folder.";
        long free = FreeGigabytes(OckSettings.RipFolder);
        if (free >= 0 && free < LOW_DISK_GB)
            message += $"\n\nThat drive has only {free} GB free, which may not be enough.";
        if (!EditorUtility.DisplayDialog("Rip a Scene", message, "Rip", "Cancel"))
            return;

        string arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.GetFullPath(SCRIPT)}\" -GameData \"{OckSettings.GameData}\" -Level {level} -Scene \"{scene}\" -RipFolder \"{OckSettings.RipFolder}\"" + RipperArgument();
        Process.Start(new ProcessStartInfo("powershell.exe", arguments) { UseShellExecute = true });
    }

    private static bool WillDownloadRipper()
    {
        return string.IsNullOrEmpty(OckSettings.AssetRipperFolder) && !Directory.Exists(Path.Combine(OckSettings.RipFolder, "_AssetRipper"));
    }

    private static string RipperArgument()
    {
        return string.IsNullOrEmpty(OckSettings.AssetRipperFolder) ? "" : $" -ToolFolder \"{OckSettings.AssetRipperFolder}\"";
    }

    /// <summary>Asks first, because it's a very large job, then rips every scene not yet ripped, in a window of its own.</summary>
    public static void RipAll()
    {
        if (!OckSettings.PathsConfirmed || string.IsNullOrEmpty(OckSettings.RipFolder))
        {
            PathsWindow.Open();
            return;
        }
        List<string> names = OckSettings.BuildSceneNames();
        List<string> remaining = ScenesToRip(names, OckSettings.RippedScenes().Keys);
        if (remaining.Count == 0)
        {
            EditorUtility.DisplayDialog("Rip All Scenes", "Every scene is already ripped.", "OK");
            return;
        }

        string message = $"Rip the {remaining.Count} scenes not yet ripped, into {OckSettings.RipFolder}?\n\n"
            + "This is a very large job, and its time and disk space aren't yet measured. Most modders only need a few scenes.\n\n"
            + "Regions and towns go first, then dungeons. It runs in its own window, stops before the drive fills up, and carries on from where it stopped if you run it again.";
        long free = FreeGigabytes(OckSettings.RipFolder);
        if (free >= 0)
            message += $"\n\nThat drive has {free} GB free.";
        if (WillDownloadRipper())
            message += "\n\nThis first time, it also downloads AssetRipper 2.0.0 from GitHub, about 42 MB, and keeps it in that folder.";
        if (!EditorUtility.DisplayDialog("Rip All Scenes", message, "Rip All", "Cancel"))
            return;

        string scenes = string.Join(",", remaining.Select(s => names.IndexOf(s) + "=" + s).ToArray());
        string arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.GetFullPath(RIP_ALL_SCRIPT)}\" -GameData \"{OckSettings.GameData}\" -Scenes \"{scenes}\" -RipFolder \"{OckSettings.RipFolder}\"" + RipperArgument();
        Process.Start(new ProcessStartInfo("powershell.exe", arguments) { UseShellExecute = true });
    }

    /// <summary>What Rip All does and in what order: the places most worth having first, in case the drive fills up.</summary>
    public static List<string> ScenesToRip(IEnumerable<string> allScenes, IEnumerable<string> ripped)
    {
        return allScenes.Except(ripped).Except(s_nothingToRip).OrderBy(s => s.IndexOf("Dungeon", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
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
