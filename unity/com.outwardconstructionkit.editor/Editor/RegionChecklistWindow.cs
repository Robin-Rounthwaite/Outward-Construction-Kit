using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>What the open region still needs before it works for players, each with a button to fix it where one can.</summary>
internal class RegionChecklistWindow : EditorWindow
{
    public class Entry
    {
        public string Text;
        public bool Done;
        public string FixLabel;
        public Action Fix;
    }

    private Vector2 m_scroll;

    [MenuItem("Construction Kit/Region Checklist")]
    public static void Open()
    {
        GetWindow<RegionChecklistWindow>(false, "Region Checklist").minSize = new Vector2(420f, 240f);
    }

    /// <summary>Each thing a region needs, in the order a person would see to them.</summary>
    public static List<Entry> Check(Scene scene, RegionInfo info, string pluginsFolder)
    {
        var entries = new List<Entry>();
        GameObject spawn = RegionExporter.FirstSpawnPoint(scene);
        entries.Add(spawn != null
            ? new Entry { Text = "Players have somewhere to arrive.", Done = true, FixLabel = "Select", Fix = () => Show(spawn) }
            : new Entry { Text = "Add a spawn point, where players arrive.", FixLabel = "Add", Fix = () => Selection.activeGameObject = RegionExporter.AddSpawnPoint(Vector3.zero) });
        entries.Add(GroundTools.HasGround(scene)
            ? new Entry { Text = "The scene has ground.", Done = true }
            : new Entry { Text = "Add ground for players to stand on.", FixLabel = "Add Ground", Fix = () => AddGroundWindow.Open(null) });
        foreach (RegionExporter.PlacementProblem problem in RegionExporter.PlacementProblems(scene))
        {
            GameObject target = problem.Target;
            entries.Add(new Entry { Text = problem.Text, FixLabel = "Drop to Ground", Fix = () => { GroundTools.DropToGround(new[] { target.transform }); Show(target); } });
        }
        entries.Add(!string.IsNullOrEmpty(info.DoorScene)
            ? new Entry { Text = $"The entrance is in {LocationNames.For(info.DoorScene)}.", Done = true }
            : new Entry { Text = "Place the entrance in game: press Ctrl+F8 until this region is named, then Ctrl+F10. The next export picks it up." });
        entries.Add(PathDetector.HasKit(pluginsFolder)
            ? new Entry { Text = $"The kit is installed in the mod list you test with, {PathDetector.ProfileName(pluginsFolder)}.", Done = true }
            : new Entry { Text = "Install the kit in the mod list you test with, or choose another.", FixLabel = "Paths", Fix = PathsWindow.Open });
        return entries;
    }

    private static void Show(GameObject target)
    {
        Selection.activeGameObject = target;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    private void OnGUI()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            KitStyles.HelpBox("Save the scene first.", MessageType.Info);
            return;
        }

        RegionInfo info = RegionInfo.For(scene.path);
        info.TryCopyDoorFromGame();
        List<Entry> entries = Check(scene, info, OckSettings.PluginsFolder);
        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        foreach (Entry entry in entries)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(entry.Done ? "Done" : "To do", KitStyles.Note, GUILayout.Width(40f));
                EditorGUILayout.LabelField(entry.Text, KitStyles.WrappedNote);
                if (entry.Fix != null && GUILayout.Button(entry.FixLabel, GUILayout.Width(110f)))
                {
                    entry.Fix();
                    GUIUtility.ExitGUI();
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }
}
