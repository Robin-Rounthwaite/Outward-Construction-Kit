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
            if (GUILayout.Button(paths ? "Change Paths" : "Set Up Paths"))
                PathsWindow.Open();
        }

        if (Step(2, "Rip what you need from the game", m_ripped, paths, "Monsoon is enough to start with: about 15 GB and 18 minutes. Everything is probably about 100 to 170 GB and about a day, and you can always rip more later. Ripping runs in its own window."))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(m_ripped))
                {
                    if (GUILayout.Button("Rip Monsoon"))
                        RipWindow.RipByName(ExampleRegion.GROUND_RIP);
                }
                if (GUILayout.Button("Rip All Scenes"))
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
            if (GUILayout.Button(example ? "Open Example Region" : "Create Example Region"))
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
            GameLauncher.TestCharacter = EditorGUILayout.TextField(new GUIContent("Character", "Who to load. Leave it empty for the first one the game lists."), GameLauncher.TestCharacter);
            if (GUILayout.Button("Visit the Example Region"))
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
                if (File.Exists(TUTORIAL) && GUILayout.Button("Open the Tutorial"))
                    EditorUtility.OpenWithDefaultApp(Path.GetFullPath(TUTORIAL));
                if (GUILayout.Button("Open the Prop Browser"))
                    PropBrowser.Open();
            }
        }

        if (m_message.Length > 0)
            EditorGUILayout.HelpBox(m_message, MessageType.None);
    }

    // Says whether to draw the step's controls, which wait until the steps before it are done.
    private static bool Step(int number, string title, bool done, bool ready, string help)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"{number}. {title}{(done ? "  (done)" : "")}", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(help, EditorStyles.wordWrappedMiniLabel);
        return ready;
    }
}
