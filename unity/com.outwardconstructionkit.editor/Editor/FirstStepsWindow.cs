using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>From a new project to walking around the example region, one step at a time, showing what's done.</summary>
internal class FirstStepsWindow : EditorWindow
{
    private const string SHOWN_PREF = "OCK.FirstStepsShown";
    private const string EXAMPLE_SCENE = "Assets/Regions/ExampleRegion.unity";
    private const string TUTORIAL = "Guides/tutorial.md";

    private bool m_ripped;
    private string m_message = "";

    [MenuItem("Construction Kit/First Steps")]
    public static void Open()
    {
        EditorPrefs.SetBool(SHOWN_PREF, true);
        GetWindow<FirstStepsWindow>(false, "First Steps").minSize = new Vector2(460f, 420f);
    }

    /// <summary>Once per PC, after the paths are first confirmed.</summary>
    public static void OpenIfNew()
    {
        if (!EditorPrefs.GetBool(SHOWN_PREF, false))
            Open();
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
        m_ripped = OckSettings.RippedScenes().ContainsKey(ExampleRegion.GROUND_RIP);
    }

    private void OnGUI()
    {
        bool paths = OckSettings.PathsConfirmed && !string.IsNullOrEmpty(OckSettings.RipFolder);
        bool example = File.Exists(EXAMPLE_SCENE);

        if (Step(1, "Tell the kit where things are", paths, true, "The game, your r2modman profile, and a folder with room for what you rip."))
        {
            if (GUILayout.Button(new GUIContent(paths ? "Change Paths" : "Set Up Paths", "Opens the window that says where the game, your mods and your rips are.")))
                PathsWindow.Open();
        }

        if (Step(2, "Rip what you need from the game", m_ripped, paths, "To start small, try Monsoon. Alternatively, you can rip all scenes. Time and disk space aren't yet measured, and you can always rip more later. Ripping runs in its own window."))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(m_ripped))
                {
                    if (GUILayout.Button(new GUIContent("Rip Monsoon", "Copies Monsoon's models out of your own game files, in its own window, so you can keep working.")))
                        RipWindow.RipByName(ExampleRegion.GROUND_RIP);
                }
                if (GUILayout.Button(new GUIContent("Rip All Scenes", "Rips every scene not yet ripped, one after another. It says how much that takes before starting.")))
                {
                    RipWindow.RipAll();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(new GUIContent("Check Again", "Looks for finished rips.")))
                    Refresh();
            }
        }

        if (Step(3, "Make the example region", example, m_ripped, "A small finished region, to look around and take apart."))
        {
            if (GUILayout.Button(new GUIContent(example ? "Open Example Region" : "Create Example Region", "A small finished region, to look around and take apart. It's kept apart from your own regions.")))
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    if (example)
                        EditorSceneManager.OpenScene(EXAMPLE_SCENE);
                    else
                        ExampleRegion.Create();
                }
                GUIUtility.ExitGUI();
            }
        }

        if (Step(4, "Try it in the game", false, example, "Exports the example, starts the game with your mods, and takes your character straight there. It plays and saves as normal."))
        {
            GameLauncher.DrawCharacterChoice(170f);
            if (GUILayout.Button(new GUIContent("Visit the Example Region", "Exports the example, starts the game with your mods, and takes the chosen character straight there.")))
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    if (EditorSceneManager.GetActiveScene().path != EXAMPLE_SCENE)
                        EditorSceneManager.OpenScene(EXAMPLE_SCENE);
                    m_message = GameLauncher.TestRegion();
                }
                GUIUtility.ExitGUI();
            }
        }

        if (Step(5, "Make your own", false, example, "The tutorial builds a region from nothing, in the Prop Browser."))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("New Region Scene", "Makes a new scene with a sun and a spawn point, then asks what ground to start with. It closes the open scene, after asking.")))
                {
                    RegionExporter.NewRegionScene();
                    GUIUtility.ExitGUI();
                }
                if (File.Exists(TUTORIAL) && GUILayout.Button(new GUIContent("Open the Tutorial", "Opens the guide that builds a region from nothing.")))
                    EditorUtility.OpenWithDefaultApp(Path.GetFullPath(TUTORIAL));
                if (GUILayout.Button(new GUIContent("Open the Prop Browser", "Opens the window where you add what goes in a region, and export and test it.")))
                    PropBrowser.Open();
            }
        }

        if (m_message.Length > 0)
            KitStyles.HelpBox(m_message, MessageType.None);
    }

    // Says whether to draw the step's controls, which wait until the steps before it are done.
    private static bool Step(int number, string title, bool done, bool ready, string help)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"{number}. {title}{(done ? "  (done)" : "")}", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(help, KitStyles.WrappedNote);
        return ready;
    }
}
