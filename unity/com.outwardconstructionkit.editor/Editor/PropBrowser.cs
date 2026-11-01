using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Browse a ripped scene's props, and click or drag them into the open scene.</summary>
internal class PropBrowser : EditorWindow
{
    private const float ROW_HEIGHT = 22f;
    private const string DRAG_KEY = "OckProp";

    private static readonly Dictionary<string, RipScene> s_scenes = new Dictionary<string, RipScene>();

    private Dictionary<string, string> m_ripped = new Dictionary<string, string>();
    private string[] m_sceneNames = new string[0];
    private int m_sceneIndex;
    private string m_search = "";
    private string[] m_categories = { "All" };
    private int m_categoryIndex;
    private Vector2 m_scroll;
    private List<RipScene.Prop> m_visible = new List<RipScene.Prop>();
    private RipScene m_scene;
    private string m_status = "";
    private bool m_showPaths;

    [MenuItem("Construction Kit/Prop Browser")]
    public static void Open()
    {
        GetWindow<PropBrowser>("Prop Browser").Refresh();
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGui;
        Refresh();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGui;
    }

    private void Refresh()
    {
        // Waits until the editor is idle, because this can run during a reload, before new shaders have imported.
        EditorApplication.delayCall += PropImporter.RefreshLibraryMaterials;
        m_ripped = OckSettings.RippedScenes();
        m_sceneNames = m_ripped.Keys.OrderBy(n => n).ToArray();
        m_sceneIndex = Mathf.Clamp(m_sceneIndex, 0, Mathf.Max(0, m_sceneNames.Length - 1));
        m_scene = null;
        LoadScene();
    }

    private void LoadScene()
    {
        m_scene = null;
        m_visible.Clear();
        if (m_sceneNames.Length == 0)
            return;

        string name = m_sceneNames[m_sceneIndex];
        if (!s_scenes.TryGetValue(name, out m_scene))
        {
            EditorUtility.DisplayProgressBar("Construction Kit", "Reading " + name + "...", 0.5f);
            try
            {
                m_scene = RipScene.Load(name, m_ripped[name]);
                s_scenes[name] = m_scene;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        m_categories = new[] { "All" }.Concat(m_scene.Props.Select(p => p.Category).Distinct().OrderBy(c => c)).ToArray();
        m_categoryIndex = 0;
        Filter();
    }

    private void Filter()
    {
        m_visible.Clear();
        if (m_scene == null)
            return;
        string category = m_categories[Mathf.Clamp(m_categoryIndex, 0, m_categories.Length - 1)];
        m_visible.AddRange(m_scene.Props.Where(p =>
            (m_categoryIndex == 0 || p.Category == category)
            && (m_search.Length == 0 || p.BaseName.IndexOf(m_search, StringComparison.OrdinalIgnoreCase) >= 0)));
    }

    /// <summary>The paths window calls this when they change.</summary>
    public void OnPathsChanged()
    {
        s_scenes.Clear();
        Refresh();
    }

    private void OnGUI()
    {
        if (!OckSettings.PathsConfirmed)
        {
            EditorGUILayout.HelpBox("First, the kit needs to know where the game and your mods are on this PC.", MessageType.Info);
            if (GUILayout.Button("Set Up Paths"))
                PathsWindow.Open();
            return;
        }
        DrawSettings();
        if (m_scene == null)
        {
            EditorGUILayout.HelpBox("No ripped scenes found. Rip one, or set the rip folder to the folder holding your AssetRipper exports.", MessageType.Info);
            if (GUILayout.Button("Rip a Scene"))
                RipWindow.Open();
            return;
        }
        DrawFilters();
        DrawList();
        DrawRegionTools();
    }

    private void DrawSettings()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Rip folder", GUILayout.Width(70));
            EditorGUI.BeginChangeCheck();
            string root = EditorGUILayout.DelayedTextField(OckSettings.RipFolder);
            if (GUILayout.Button("...", GUILayout.Width(26)))
            {
                string picked = EditorUtility.OpenFolderPanel("Folder holding AssetRipper exports", root, "");
                if (!string.IsNullOrEmpty(picked))
                    root = picked;
                GUI.changed = true;
            }
            if (EditorGUI.EndChangeCheck())
            {
                OckSettings.RipFolder = root;
                s_scenes.Clear();
                Refresh();
            }
            if (GUILayout.Button(new GUIContent("Refresh", "Looks for newly finished rips."), GUILayout.Width(60)))
                OnPathsChanged();
            if (GUILayout.Button("Rip a Scene", GUILayout.Width(85)))
                RipWindow.Open();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Textures", GUILayout.Width(70));
            int mode = OckSettings.FastImport ? 0 : 1;
            int picked = EditorGUILayout.Popup(mode, new[] { "Fast (quicker imports, softer view)", "Detailed (slower imports, sharper view)" });
            if (picked != mode)
                ChangeTextureMode(picked == 0);
        }
    }

    private void ChangeTextureMode(bool fast)
    {
        OckSettings.FastImport = fast;
        PropImporter.ApplyTextureModeToLibrary();
        PropImporter.RefreshLibraryMaterials();
        SceneView.RepaintAll();
        m_status = fast ? "Fast mode: smaller textures, no surface detail." : "Detailed mode: full-size textures and surface detail. Still not the game's real shaders.";
        GUIUtility.ExitGUI();
    }

    private void DrawFilters()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            m_sceneIndex = EditorGUILayout.Popup(m_sceneIndex, m_sceneNames, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck())
                LoadScene();

            EditorGUI.BeginChangeCheck();
            m_categoryIndex = EditorGUILayout.Popup(m_categoryIndex, m_categories, GUILayout.Width(120));
            m_search = EditorGUILayout.TextField(m_search, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
                Filter();
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"{m_visible.Count} props. Click Add, or drag a name into the Scene view.", EditorStyles.miniLabel);
            m_showPaths = EditorGUILayout.ToggleLeft("Paths", m_showPaths, GUILayout.Width(60));
        }
    }

    private void DrawList()
    {
        m_scroll = EditorGUILayout.BeginScrollView(m_scroll, GUILayout.ExpandHeight(true));

        // Thousands of rows are too slow to lay out, so only the visible ones are drawn.
        int first = Mathf.Max(0, Mathf.FloorToInt(m_scroll.y / ROW_HEIGHT));
        int count = Mathf.CeilToInt(position.height / ROW_HEIGHT) + 1;
        GUILayout.Space(first * ROW_HEIGHT);
        for (int i = first; i < Mathf.Min(m_visible.Count, first + count); i++)
            DrawRow(m_visible[i]);
        GUILayout.Space(Mathf.Max(0, m_visible.Count - first - count) * ROW_HEIGHT);

        EditorGUILayout.EndScrollView();
    }

    private void DrawRow(RipScene.Prop prop)
    {
        Rect row = EditorGUILayout.BeginHorizontal(GUILayout.Height(ROW_HEIGHT));
        string label = prop.BaseName + (prop.InstanceCount > 1 ? "  x" + prop.InstanceCount : "");
        if (m_showPaths)
            label = prop.Node.ScenePath;
        GUILayout.Label(new GUIContent(label, prop.Node.ScenePath));
        GUILayout.FlexibleSpace();
        if (!prop.IsEnemy && prop.Node.SelfAndDescendants().Any(n => n.IsInteractive))
            GUILayout.Label(new GUIContent("decoration", "Players can use this in its own scene, but in a region it's only for show."), EditorStyles.miniLabel, GUILayout.Width(60));
        GUILayout.Label(prop.Category, EditorStyles.miniLabel, GUILayout.Width(80));
        if (GUILayout.Button("Add", GUILayout.Width(44)))
            PlaceAtViewCentre(prop);
        EditorGUILayout.EndHorizontal();

        Event e = Event.current;
        if (e.type == EventType.MouseDrag && row.Contains(e.mousePosition))
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new UnityEngine.Object[0];
            DragAndDrop.SetGenericData(DRAG_KEY, prop);
            DragAndDrop.StartDrag(prop.BaseName);
            e.Use();
        }
    }

    private void DrawRegionTools()
    {
        if (!string.IsNullOrEmpty(m_status))
            EditorGUILayout.HelpBox(m_status, MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("Add Ground", "Replaces the starter room with a terrain. Sculpt and paint it with the Terrain component's tools in the Inspector.")))
            {
                GroundTools.AddGroundFromMenu();
                m_status = "Ground added. In the Inspector, use the Terrain's Paint tool: Raise or Lower Terrain and Smooth Height for hills, Paint Texture for grass, dirt, sand and rock.";
            }
            if (GUILayout.Button(new GUIContent("Add Water from " + m_scene.SceneName, "Adds a marker to move and scale. In game it becomes this scene's own water.")))
            {
                Selection.activeGameObject = GroundTools.AddWater(m_scene.SceneName);
                m_status = "Water added. Move and scale the blue marker; in game it's replaced by the real water at that height and size.";
            }
            if (GUILayout.Button(new GUIContent("Add Return Door", "Adds a marker for a door back to where players entered. The small box shows the side they walk up to. With none, a door appears beside the arrival point.")))
            {
                DoorTools.AddFromMenu();
                m_status = "Return door added. Stand it on the ground, with the small box facing the way players will approach.";
            }
        }

        if (!RegionExporter.HasSpawnPoint(UnityEngine.SceneManagement.SceneManager.GetActiveScene()))
        {
            EditorGUILayout.HelpBox("This scene has no spawn point, so it can't be exported yet. Players arrive at it.", MessageType.Warning);
            if (GUILayout.Button("Add Spawn Point"))
            {
                SceneView view = SceneView.lastActiveSceneView;
                Vector3 spot = view != null ? view.pivot : Vector3.zero;
                Vector3 ground;
                Selection.activeGameObject = RegionExporter.AddSpawnPoint(TryFindGround(new Ray(spot + Vector3.up * 50f, Vector3.down), out ground) ? ground : spot);
                m_status = "Spawn point added at the middle of your view. It's selected, so you can move it with the Move tool.";
            }
        }

        string scenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        if (!string.IsNullOrEmpty(scenePath))
        {
            RegionInfo info = RegionInfo.For(scenePath);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.LabelField("Region ID", GUILayout.Width(60));
                info.Id = EditorGUILayout.DelayedTextField(info.Id, GUILayout.Width(150));
                EditorGUILayout.LabelField("Name", GUILayout.Width(40));
                info.DisplayName = EditorGUILayout.DelayedTextField(info.DisplayName);
                if (EditorGUI.EndChangeCheck())
                    info.SaveFor(scenePath);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                bool custom = !string.IsNullOrEmpty(info.DoorModelPath);
                EditorGUILayout.LabelField("Door looks like: " + (custom ? System.IO.Path.GetFileName(info.DoorModelPath) : "the kit's default door"), EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("Use selected prop", "Doors will look like the prop selected in the scene, at its scale. Turn it first so its front faces along the blue axis, away from the wall it would sit in."), GUILayout.Width(120)))
                {
                    GameObject selected = Selection.activeGameObject;
                    PropRef chosen = selected != null ? PropRef.Of(selected) : null;
                    if (chosen == null || chosen.IsEnemy)
                        m_status = "Select a prop in the scene first. You can delete it afterwards, and its art stays out of the mod.";
                    else
                    {
                        info.DoorModelScene = chosen.DonorScene;
                        info.DoorModelPath = chosen.Path;
                        info.DoorModelRotation = selected.transform.eulerAngles;
                        info.DoorModelScale = PropRef.ScaleOf(selected);
                        info.SaveFor(scenePath);
                        m_status = "Doors will look like " + selected.name + " on your next export.";
                    }
                }
                using (new EditorGUI.DisabledScope(!custom))
                {
                    if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    {
                        info.DoorModelScene = info.DoorModelPath = null;
                        info.SaveFor(scenePath);
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                string door = string.IsNullOrEmpty(info.DoorScene) ? "none yet" : info.DoorScene + " at " + info.DoorPosition;
                EditorGUILayout.LabelField("Entrance: " + door, EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("Use door placed in game", "Copies where you placed this region's door with Ctrl+F10, so it ships with the mod."), GUILayout.Width(170)))
                {
                    if (info.TryCopyDoorFromGame())
                    {
                        info.SaveFor(scenePath);
                        m_status = "Entrance saved. It ships with the mod on your next export.";
                    }
                    else
                        m_status = $"No door placed for '{info.Id}' yet. Export the region, then in game press Ctrl+F8 to choose it and Ctrl+F10 to place its door.";
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Sky and audio from", GUILayout.Width(110));
            int current = Array.IndexOf(m_sceneNames, OckSettings.EnvironmentDonor);
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(Mathf.Max(0, current), m_sceneNames);
            if (EditorGUI.EndChangeCheck())
                OckSettings.EnvironmentDonor = m_sceneNames[picked];

            if (GUILayout.Button("New Region", GUILayout.Width(90)))
                RegionExporter.NewRegionScene();
            if (GUILayout.Button("Export Region", GUILayout.Width(100)))
            {
                try
                {
                    m_status = RegionExporter.ExportToProfile();
                }
                catch (Exception e)
                {
                    m_status = "Export failed: " + e.Message;
                    Debug.LogException(e);
                }
            }
            if (GUILayout.Button(new GUIContent("Package", "Makes a zip to upload to Thunderstore."), GUILayout.Width(70)))
                PackageWindow.Open();
        }
    }

    private void PlaceAtViewCentre(RipScene.Prop prop)
    {
        SceneView view = SceneView.lastActiveSceneView;
        Vector3 spot = view != null ? view.pivot : Vector3.zero;
        Vector3 ground;
        Place(prop, TryFindGround(new Ray(spot + Vector3.up * 50f, Vector3.down), out ground) ? ground : spot);
    }

    private void OnSceneGui(SceneView view)
    {
        Event e = Event.current;
        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
            return;
        var prop = DragAndDrop.GetGenericData(DRAG_KEY) as RipScene.Prop;
        if (prop == null)
            return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            Vector3 ground;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!TryFindGround(ray, out ground))
                ground = view.pivot;
            Place(prop, ground);
        }
        e.Use();
    }

    private static bool TryFindGround(Ray ray, out Vector3 point)
    {
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 5000f))
        {
            point = hit.point;
            return true;
        }
        float distance;
        if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }
        point = Vector3.zero;
        return false;
    }

    private void Place(RipScene.Prop prop, Vector3 point)
    {
        try
        {
            EditorUtility.DisplayProgressBar("Construction Kit", "Importing " + prop.BaseName + "...", 0.5f);
            GameObject prefab = PropImporter.Import(m_scene, prop);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = point;
            Undo.RegisterCreatedObjectUndo(instance, "Add " + prop.BaseName);
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(instance.scene);
            m_status = "Added " + prop.BaseName + ".";
        }
        catch (Exception e)
        {
            m_status = "Couldn't import " + prop.BaseName + ": " + e.Message;
            Debug.LogException(e);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }
}
