using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Asks what Thunderstore needs to know about the open region, and makes the zip to upload.</summary>
internal class PackageWindow : EditorWindow
{
    private string m_status = "";
    private string m_lastZip;
    private Vector2 m_scroll;

    [MenuItem("Construction Kit/Package Region")]
    public static void Open()
    {
        GetWindow<PackageWindow>(false, "Package Region").minSize = new Vector2(460f, 480f);
    }

    private void OnGUI()
    {
        string scenePath = SceneManager.GetActiveScene().path;
        if (string.IsNullOrEmpty(scenePath))
        {
            EditorGUILayout.HelpBox("Save the scene first.", MessageType.Info);
            return;
        }

        RegionInfo info = RegionInfo.For(scenePath);
        if (string.IsNullOrEmpty(info.PackageName))
            info.PackageName = Regex.Replace(info.DisplayName ?? info.Id, "[^a-zA-Z0-9]+", "_").Trim('_');

        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        EditorGUILayout.LabelField(info.DisplayName + " (" + info.Id + ")", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        info.PackageName = EditorGUILayout.DelayedTextField(new GUIContent("Mod name", "Letters, numbers and underscores. Underscores show as spaces on Thunderstore."), info.PackageName);
        info.PackageVersion = EditorGUILayout.DelayedTextField(new GUIContent("Version", "Raise this for each upload, because Thunderstore rejects a version it already has."), info.PackageVersion);
        info.WebsiteUrl = EditorGUILayout.DelayedTextField(new GUIContent("Website (optional)"), info.WebsiteUrl);
        EditorGUILayout.LabelField("Short description, shown in lists");
        info.Description = EditorGUILayout.TextArea(info.Description ?? "", EditorStyles.wordWrappedLabel, GUILayout.MinHeight(44f));
        EditorGUILayout.LabelField("About this region, for its page (optional)");
        info.About = EditorGUILayout.TextArea(info.About ?? "", EditorStyles.wordWrappedLabel, GUILayout.MinHeight(80f));
        using (new EditorGUILayout.HorizontalScope())
        {
            info.IconPath = EditorGUILayout.DelayedTextField(new GUIContent("Icon (optional)", "A 256 by 256 PNG. Empty uses the kit's door icon."), info.IconPath);
            if (GUILayout.Button(new GUIContent("...", "Choose a PNG file."), GUILayout.Width(26f)))
            {
                string picked = EditorUtility.OpenFilePanel("A 256 by 256 PNG", "", "png");
                if (!string.IsNullOrEmpty(picked))
                {
                    info.IconPath = picked;
                    GUI.changed = true;
                }
                GUIUtility.keyboardControl = 0;
            }
        }
        if (EditorGUI.EndChangeCheck())
            info.SaveFor(scenePath);

        EditorGUILayout.Space();
        List<string> problems = RegionPackager.Problems(info);
        foreach (string problem in problems)
            EditorGUILayout.HelpBox(problem, MessageType.Warning);

        using (new EditorGUI.DisabledScope(problems.Count > 0))
        {
            if (GUILayout.Button(new GUIContent("Make Upload Zip", "Exports the open region and makes a zip to upload to Thunderstore. It doesn't upload anything."), GUILayout.Height(28f)))
            {
                try
                {
                    string summary;
                    m_lastZip = RegionPackager.Package(info, RegionPackager.OUTPUT_FOLDER, out summary);
                    m_status = summary + "\n\nReady to upload: " + Path.GetFullPath(m_lastZip);
                }
                catch (Exception e)
                {
                    m_lastZip = null;
                    m_status = "Packaging failed: " + e.Message;
                    Debug.LogException(e);
                }
            }
        }
        if (m_status.Length > 0)
            EditorGUILayout.HelpBox(m_status, MessageType.None);
        if (m_lastZip != null && GUILayout.Button(new GUIContent("Show Zip in Folder", "Opens the folder holding the zip you just made.")))
            EditorUtility.RevealInFinder(m_lastZip);
        EditorGUILayout.HelpBox($"Upload it on thunderstore.io under the Outward community. The kit, version {RegionPackager.KitVersion}, needs to be on Thunderstore already, and your mod installs it for players.", MessageType.Info);
        EditorGUILayout.EndScrollView();
    }
}
