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

    // From the four scenes ripped so far, so only a rough guide to the rest.
    private const int TYPICAL_SCENE_GB_LOW = 3;
    private const int TYPICAL_SCENE_GB_HIGH = 15;
    private const int TYPICAL_SCENE_MINUTES_LOW = 3;
    private const int TYPICAL_SCENE_MINUTES_HIGH = 18;

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
        m_scenes = OckSettings.BuildSceneNames();
        m_ripped = new HashSet<string>(OckSettings.RippedScenes().Keys);
    }

    private void OnGUI()
    {
        if (!OckSettings.PathsConfirmed || string.IsNullOrEmpty(OckSettings.RipFolder))
        {
            EditorGUILayout.HelpBox("First, choose a rip folder in the paths window.", MessageType.Info);
            if (GUILayout.Button(new GUIContent("Set Up Paths", "Opens the window that says where the game, your mods and your rips are.")))
                PathsWindow.Open();
            return;
        }

        EditorGUILayout.HelpBox("Ripping copies a scene's models out of your own game files, for the Prop Browser to show. A scene usually takes 10 to 30 minutes and several gigabytes, up to about 15. It runs in its own window, so you can keep working.", MessageType.Info);
        if (GUILayout.Button(new GUIContent("Rip All Scenes", "Rips every scene not yet ripped, one after another. It says how much that takes before starting.")))
            RipAll();
        m_search = EditorGUILayout.TextField("Search", m_search);
        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        for (int i = 0; i < m_scenes.Count; i++)
        {
            string scene = m_scenes[i];
            string name = LocationNames.For(scene);
            if (m_search.Length > 0 && scene.IndexOf(m_search, StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf(m_search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(name, "The game calls this " + scene + "."));
                if (m_ripped.Contains(scene))
                    EditorGUILayout.LabelField("ripped", EditorStyles.miniLabel, GUILayout.Width(60f));
                else if (GUILayout.Button(new GUIContent("Rip", "Copies this scene's models out of your own game files, in its own window, so you can keep working."), GUILayout.Width(60f)))
                    Rip(scene, i);
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
        string message = $"Rip {LocationNames.For(scene)}? It usually takes 10 to 30 minutes and several gigabytes, in {OckSettings.RipFolder}.";
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
            + $"Going by the four scenes measured so far, that's somewhere between {remaining.Count * TYPICAL_SCENE_GB_LOW} and {remaining.Count * TYPICAL_SCENE_GB_HIGH} GB, and {remaining.Count * TYPICAL_SCENE_MINUTES_LOW / 60} and {remaining.Count * TYPICAL_SCENE_MINUTES_HIGH / 60} hours. Most modders only need a few scenes.\n\n"
            + "Regions and towns go first, then dungeons. It runs in its own window, stops before the drive fills up, and carries on from where it stopped if you run it again.";
        long free = FreeGigabytes(OckSettings.RipFolder);
        if (free >= 0)
            message += $"\n\nThat drive has {free} GB free.";
        if (!Directory.Exists(Path.Combine(OckSettings.RipFolder, "_AssetRipper")))
            message += "\n\nThis first time, it also downloads AssetRipper 2.0.0 from GitHub, about 42 MB, and keeps it in that folder.";
        if (!EditorUtility.DisplayDialog("Rip All Scenes", message, "Rip All", "Cancel"))
            return;

        string scenes = string.Join(",", remaining.Select(s => names.IndexOf(s) + "=" + s).ToArray());
        string arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.GetFullPath(RIP_ALL_SCRIPT)}\" -GameData \"{OckSettings.GameData}\" -Scenes \"{scenes}\" -RipFolder \"{OckSettings.RipFolder}\"";
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
