using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Writes what a placed character says, and what players can say back.</summary>
internal class DialogueWindow : EditorWindow
{
    private const string END = "(ends the conversation)";

    private Vector2 m_scroll;

    [MenuItem("Construction Kit/Dialogue")]
    public static void Open()
    {
        GetWindow<DialogueWindow>(false, "Dialogue").minSize = new Vector2(460f, 320f);
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    private void OnGUI()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject selected = Selection.activeGameObject;
        PropRef reference = selected != null ? PropRef.Of(selected) : null;
        if (string.IsNullOrEmpty(scene.path) || reference == null || !reference.IsEnemy)
        {
            KitStyles.HelpBox("Select a character in the scene. Add one from the Enemies tab of the Prop Browser, where the Characters category has the game's people. An enemy that's hostile will still attack.", MessageType.Info);
            return;
        }

        RegionInfo info = RegionInfo.For(scene.path);
        RegionInfo.CharacterDialogue dialogue = info.DialogueOf(selected);
        if (dialogue == null)
        {
            if (GUILayout.Button(new GUIContent("Give This Character a Conversation", "They get a talk prompt in the game, with the name you give them.")))
            {
                EditorSceneManager.SaveScene(scene); // their lasting ID only exists once the scene is saved
                info.AddDialogue(selected);
                info.SaveFor(scene.path);
            }
            return;
        }

        bool remove = false;
        EditorGUI.BeginChangeCheck();
        dialogue.Name = EditorGUILayout.TextField(new GUIContent("Name players see", "Replaces the name they have in the game."), dialogue.Name);
        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        string[] targets = new[] { END }.Concat(dialogue.Lines.Select(l => l.Name)).ToArray();
        RegionInfo.DialogueLine removeLine = null;
        foreach (RegionInfo.DialogueLine line in dialogue.Lines)
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(line == dialogue.Lines[0] ? line.Name + " (where it starts)" : line.Name, EditorStyles.boldLabel);
                if (dialogue.Lines.Count > 1 && GUILayout.Button(new GUIContent("Remove", "Removes this line. Answers leading to it end the conversation instead."), GUILayout.Width(70f)))
                    removeLine = line;
            }
            line.Text = EditorGUILayout.TextArea(line.Text ?? "", EditorStyles.wordWrappedLabel, GUILayout.MinHeight(36f));
            RegionInfo.DialogueChoice removeChoice = null;
            foreach (RegionInfo.DialogueChoice choice in line.Choices)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Player says", GUILayout.Width(70f));
                    choice.Text = EditorGUILayout.TextField(choice.Text);
                    int current = Mathf.Max(0, System.Array.IndexOf(targets, choice.Next));
                    choice.Next = targets[EditorGUILayout.Popup(current, targets, GUILayout.Width(150f))];
                    if (GUILayout.Button(new GUIContent("X", "Removes this answer."), GUILayout.Width(22f)))
                        removeChoice = choice;
                }
            }
            if (removeChoice != null)
                line.Choices.Remove(removeChoice);
            if (GUILayout.Button(new GUIContent("Add an Answer", "With no answers, the conversation ends after this line.")))
                line.Choices.Add(new RegionInfo.DialogueChoice { Text = "", Next = END });
        }
        if (removeLine != null)
            dialogue.Lines.Remove(removeLine);
        EditorGUILayout.Space();
        if (GUILayout.Button(new GUIContent("Add a Line", "Something else they can say, which answers can lead to.")))
            dialogue.Lines.Add(new RegionInfo.DialogueLine { Name = dialogue.NextLineName(), Text = "" });
        EditorGUILayout.EndScrollView();
        if (GUILayout.Button(new GUIContent("Remove This Character's Conversation", "They go back to having nothing to say.")))
            remove = true;
        if (remove)
            info.RemoveDialogue(selected);
        if (EditorGUI.EndChangeCheck() || remove)
            info.SaveFor(scene.path);
        if (remove)
            GUIUtility.ExitGUI();
    }
}
