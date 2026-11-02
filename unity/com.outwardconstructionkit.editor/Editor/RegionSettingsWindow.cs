using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Choices about how the whole region behaves, as opposed to what's in it. For now, what a defeat does.</summary>
internal class RegionSettingsWindow : EditorWindow
{
    private const string CAME_FROM = "Where they came from";
    private const string IN_THIS_REGION = "A spawn point in this region";

    [MenuItem("Construction Kit/Region Settings")]
    public static void Open()
    {
        GetWindow<RegionSettingsWindow>(false, "Region Settings").minSize = new Vector2(420f, 300f);
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
        EditorGUILayout.LabelField("When a party is defeated here", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("With nothing changed, they return where they came from, with no penalty.", KitStyles.WrappedNote);

        EditorGUI.BeginChangeCheck();
        List<string> scenes = OckSettings.BuildSceneNames();
        var options = new List<string> { CAME_FROM, IN_THIS_REGION };
        options.AddRange(scenes.Select(LocationNames.For));
        int current = info.DefeatReturn == RegionInfo.DEFEAT_RETURN_HERE ? 1 : string.IsNullOrEmpty(info.DefeatReturn) ? 0 : scenes.IndexOf(info.DefeatReturn) + 2;
        int picked = EditorGUILayout.Popup(new GUIContent("Return to", "Where the party wakes up. A location other than this region is at its first spawn point."), Mathf.Max(0, current), options.ToArray());
        info.DefeatReturn = picked == 0 ? "" : picked == 1 ? RegionInfo.DEFEAT_RETURN_HERE : scenes[picked - 2];
        if (picked == 1)
            info.DefeatSpawn = EditorGUILayout.IntField(new GUIContent("Spawn point number", "Counting from 0, in the order they're listed under SpawnPointManager."), info.DefeatSpawn);
        info.DefeatSilverPercent = EditorGUILayout.IntSlider(new GUIContent("Silver lost (%)", "A share of what each player carries."), info.DefeatSilverPercent, 0, 100);
        info.DefeatItemPercent = EditorGUILayout.IntSlider(new GUIContent("Carried items lost (%)", "A random share of what's in each player's pouch and bag, rounded down. What they're wearing is safe."), info.DefeatItemPercent, 0, 100);
        info.DefeatVitalsPercent = EditorGUILayout.IntSlider(new GUIContent("Health and stamina left (%)", "How much they wake up with. 0 leaves it as the game has it."), info.DefeatVitalsPercent, 0, 100);
        EditorGUILayout.LabelField(new GUIContent("Message", "Shown on the defeat screen. Empty uses the kit's own."));
        info.DefeatMessage = EditorGUILayout.TextArea(info.DefeatMessage ?? "", EditorStyles.wordWrappedLabel, GUILayout.MinHeight(44f));
        if (EditorGUI.EndChangeCheck())
            info.SaveFor(scene.path);

        EditorGUILayout.Space();
        KitStyles.HelpBox("These apply from your next export.", MessageType.Info);
    }
}
