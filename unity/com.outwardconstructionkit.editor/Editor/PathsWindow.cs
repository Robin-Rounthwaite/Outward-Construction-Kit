using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Asks where things are on this PC, offering what it can find. Nothing is used until it's confirmed.</summary>
[InitializeOnLoad]
internal class PathsWindow : EditorWindow
{
    private const string REQUIRED_UNITY = "2020.3.26f1";
    private const string ASKED_THIS_SESSION = "OCK.PathsAsked";

    // Opens by itself on a new project, so the first thing a modder meets is what the kit needs from them.
    static PathsWindow()
    {
        if (OckSettings.PathsConfirmed || SessionState.GetBool(ASKED_THIS_SESSION, false) || Application.isBatchMode
            || Array.IndexOf(Environment.GetCommandLineArgs(), "-ockSelfTest") >= 0)
            return;
        SessionState.SetBool(ASKED_THIS_SESSION, true);
        EditorApplication.delayCall += Open;
    }

    private string m_gameData;
    private string m_plugins;
    private string m_ripFolder;
    private string m_ripper;
    private List<string> m_foundGameData;
    private List<string> m_foundPlugins;

    [MenuItem("Construction Kit/Paths")]
    public static void Open()
    {
        GetWindow<PathsWindow>(true, "Construction Kit Paths").minSize = new Vector2(620f, 460f);
    }

    private void OnEnable()
    {
        m_foundGameData = PathDetector.GameDataFolders();
        m_foundPlugins = PathDetector.PluginFolders(m_foundGameData);
        m_gameData = FirstOf(OckSettings.GameData, m_foundGameData);
        m_plugins = FirstOf(OckSettings.PluginsFolder, m_foundPlugins);
        m_ripFolder = OckSettings.RipFolder;
        m_ripper = OckSettings.AssetRipperFolder;
    }

    private static string FirstOf(string saved, List<string> found)
    {
        return !string.IsNullOrEmpty(saved) || found.Count == 0 ? saved : found[0];
    }

    private void OnGUI()
    {
        if (Application.unityVersion != REQUIRED_UNITY)
            EditorGUILayout.HelpBox($"This is Unity {Application.unityVersion}. Regions need {REQUIRED_UNITY} exactly, to match the game, or they may not load.", MessageType.Error);
        EditorGUILayout.HelpBox("Please check these before confirming. Suggestions are only what could be found on this PC, and nothing here gets changed by looking.", MessageType.Info);

        m_gameData = PathRow("Game data folder", "The Outward Definitive Edition_Data folder, inside the game's folder.", m_gameData, m_foundGameData);
        if (!PathDetector.IsGameData(m_gameData))
            EditorGUILayout.HelpBox("This folder doesn't have the game's files in it.", MessageType.Warning);

        m_plugins = PathRow("BepInEx plugins folder", "Where exported regions go, so they load when you launch the game. With a mod manager, pick the profile you test with.", m_plugins, m_foundPlugins);
        if (!PathDetector.IsPluginsFolder(m_plugins))
            EditorGUILayout.HelpBox("This should be an existing folder called plugins, inside BepInEx.", MessageType.Warning);

        m_ripFolder = PathRow("Rip folder", "An empty folder for what you rip from the game, on a drive with plenty of space and off OneDrive. A first rip takes about 15 GB.", m_ripFolder, null);

        m_ripper = PathRow("AssetRipper folder (optional)", "Leave empty and the kit downloads the version it's tested with, once. Only fill this in to use a copy you already have; version 2.0.0 is the tested one.", m_ripper, null);
        if (!string.IsNullOrEmpty(m_ripper) && (!Directory.Exists(m_ripper) || Directory.GetFiles(m_ripper, "AssetRipper*.exe", SearchOption.AllDirectories).Length == 0))
            EditorGUILayout.HelpBox("There's no AssetRipper program in this folder.", MessageType.Warning);

        GUILayout.FlexibleSpace();
        using (new EditorGUI.DisabledScope(!PathDetector.IsGameData(m_gameData) || !PathDetector.IsPluginsFolder(m_plugins)))
        {
            if (GUILayout.Button("Confirm", GUILayout.Height(28f)))
            {
                OckSettings.GameData = m_gameData;
                OckSettings.PluginsFolder = m_plugins;
                OckSettings.RipFolder = m_ripFolder;
                OckSettings.AssetRipperFolder = m_ripper;
                OckSettings.PathsConfirmed = true;
                foreach (PropBrowser browser in Resources.FindObjectsOfTypeAll<PropBrowser>())
                    browser.OnPathsChanged();
                Close();
                FirstStepsWindow.OpenIfNew();
            }
        }
    }

    private static string PathRow(string label, string help, string value, List<string> found)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(help, EditorStyles.wordWrappedMiniLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            value = EditorGUILayout.TextField(value);
            if (GUILayout.Button("...", GUILayout.Width(26f)))
            {
                string picked = EditorUtility.OpenFolderPanel(label, value, "");
                if (!string.IsNullOrEmpty(picked))
                    value = picked.Replace('/', Path.DirectorySeparatorChar);
                GUIUtility.keyboardControl = 0;
            }
        }
        if (found != null && found.Count > 1)
        {
            int current = found.IndexOf(value);
            int picked = EditorGUILayout.Popup("Found here", current, found.ConvertAll(f => f.Replace('/', '\\').Replace("\\", " \\ ")).ToArray());
            if (picked != current)
            {
                value = found[picked];
                GUIUtility.keyboardControl = 0;
            }
        }
        return value;
    }
}
