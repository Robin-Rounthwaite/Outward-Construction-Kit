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
    private bool m_showPreview = true;

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
            KitStyles.HelpBox("Save the scene first.", MessageType.Info);
            return;
        }

        RegionInfo info = RegionInfo.For(scenePath);
        info.TryCopyDoorFromGame(); // a door placed in game counts, with nothing more to click
        if (string.IsNullOrEmpty(info.PackageName))
            info.PackageName = Regex.Replace(info.DisplayName ?? info.Id, "[^a-zA-Z0-9]+", "_").Trim('_');
        if (string.IsNullOrEmpty(info.PackageTeam))
            info.PackageTeam = OckSettings.LastTeam;

        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        EditorGUILayout.LabelField(info.DisplayName + " (" + info.Id + ")", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        info.PackageTeam = EditorGUILayout.DelayedTextField(new GUIContent("Team name", "Your team on Thunderstore. It names the zip, and reminds you which team to choose on the upload page, because the zip itself doesn't carry a team."), info.PackageTeam);
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
        {
            info.SaveFor(scenePath);
            OckSettings.LastTeam = info.PackageTeam;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            string folder = EditorGUILayout.DelayedTextField(new GUIContent("Save zip to", "Where the zip goes. It's remembered for next time. A relative path is inside this Unity project."), OckSettings.UploadFolder);
            if (GUILayout.Button(new GUIContent("...", "Choose a folder."), GUILayout.Width(26f)))
            {
                string picked = EditorUtility.OpenFolderPanel("Save zip to", folder, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    folder = picked.Replace('/', Path.DirectorySeparatorChar);
                    GUI.changed = true;
                }
                GUIUtility.keyboardControl = 0;
            }
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(folder))
                OckSettings.UploadFolder = folder;
        }

        EditorGUILayout.Space();
        List<string> problems = RegionPackager.Problems(info);
        foreach (string problem in problems)
            KitStyles.HelpBox(problem, MessageType.Warning);

        string[] made = Directory.Exists(OckSettings.UploadFolder) ? Directory.GetFiles(OckSettings.UploadFolder, info.PackageTeam + "-" + info.PackageName + "-*.zip") : new string[0];
        string suggested = RegionPackager.SuggestedVersion(info.PackageVersion, made);
        if (suggested != null)
        {
            KitStyles.HelpBox($"You've already made this version or a later one. Thunderstore rejects a version it already has, so {suggested} would be next.", MessageType.Info);
            if (GUILayout.Button(new GUIContent("Use " + suggested, "Sets the version to this.")))
            {
                info.PackageVersion = suggested;
                info.SaveFor(scenePath);
                GUIUtility.keyboardControl = 0;
            }
        }
        m_showPreview = EditorGUILayout.Foldout(m_showPreview, "Preview", true);
        if (m_showPreview)
            EditorGUILayout.LabelField(RegionPackager.Preview(info), KitStyles.WrappedNote);

        using (new EditorGUI.DisabledScope(problems.Count > 0))
        {
            if (GUILayout.Button(new GUIContent("Make Upload Zip", "Exports the open region and makes a zip to upload to Thunderstore. It doesn't upload anything."), GUILayout.Height(28f)))
            {
                try
                {
                    string summary;
                    m_lastZip = RegionPackager.Package(info, OckSettings.UploadFolder, out summary);
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
            KitStyles.HelpBox(m_status, MessageType.None);
        if (m_lastZip != null && GUILayout.Button(new GUIContent("Show Zip in Folder", "Opens the folder holding the zip you just made.")))
            EditorUtility.RevealInFinder(m_lastZip);
        KitStyles.HelpBox($"This window only makes the zip. To publish, upload it yourself on thunderstore.io under the Outward community, choosing the team {info.PackageTeam}. The kit, version {RegionPackager.KitVersion}, needs to be on Thunderstore already, and your mod installs it for players.", MessageType.Info);
        EditorGUILayout.EndScrollView();
    }
}
