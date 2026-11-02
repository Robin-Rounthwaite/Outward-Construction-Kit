using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Browse what ripped scenes offer, by tab and by where it came from, and add it to the open scene.</summary>
internal partial class PropBrowser : EditorWindow
{
    public enum Tab { Enemies, Water, Ground, Props, Items }

    private const float ROW_HEIGHT = 22f;
    private const string DRAG_KEY = "OckProp";
    private const string ALL_LOCATIONS = "All locations";
    private const string ALL_CATEGORIES = "All";
    private const int STATUS_FONT_SIZE = 13;

    // In the order of the tabs.
    private static readonly string[] s_nextSteps =
    {
        "Next: click Add or drag a name into the Scene view, then click Test Region In-Game to meet it.",
        "Next: lower a hollow in your ground, click Add, then move the blue marker into it, just below ground level.",
        "Next: click Add Ground, shape it with the Terrain's tools in the Inspector, add textures from this list, and paint.",
        "Next: click Add or drag a name into the Scene view, then move it with Unity's Move tool.",
        "Next: click Add or drag a name into the Scene view, then click Test Region In-Game to see it.",
    };
    private static GUIStyle s_statusStyle;
    private static bool s_layerPreviewsChecked;

    private static readonly string[] s_tabNames = Enum.GetNames(typeof(Tab));
    private static readonly Dictionary<string, RipScene> s_scenes = new Dictionary<string, RipScene>();
    private static readonly Dictionary<string, List<string>> s_groundTextures = new Dictionary<string, List<string>>();

    [SerializeField] private Tab m_tab = Tab.Props;
    [SerializeField] private string m_location;
    private Dictionary<string, string> m_ripped = new Dictionary<string, string>();
    private string[] m_sceneNames = new string[0];
    private string[] m_sceneLabels = new string[0];
    private string[] m_locations = new string[0];
    private string[] m_locationLabels = new string[0];
    private List<RipScene> m_loaded = new List<RipScene>();
    private string m_search = "";
    private string[] m_categories = { ALL_CATEGORIES };
    private int m_categoryIndex;
    private Vector2 m_scroll;
    private List<RipScene.Prop> m_visible = new List<RipScene.Prop>();
    private List<RipScene> m_visibleWater = new List<RipScene>();
    private List<KeyValuePair<string, string>> m_visibleTextures = new List<KeyValuePair<string, string>>();
    private string m_status = "";
    private bool m_showPaths;

    private bool ShowsProps { get { return m_tab == Tab.Enemies || m_tab == Tab.Props || m_tab == Tab.Items; } }

    [MenuItem("Construction Kit/Prop Browser")]
    public static void Open()
    {
        GetWindow<PropBrowser>("Prop Browser").Refresh();
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGui;
        // A reload shouldn't mean waiting for every rip to be read again.
        if (m_location == ALL_LOCATIONS)
            m_location = null;
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
        m_sceneNames = m_ripped.Keys.OrderBy(LocationNames.For).ToArray();
        m_sceneLabels = m_sceneNames.Select(LocationNames.For).ToArray();
        m_locations = new[] { ALL_LOCATIONS }.Concat(m_sceneNames).ToArray();
        m_locationLabels = new[] { ALL_LOCATIONS }.Concat(m_sceneLabels).ToArray();
        if (m_location != ALL_LOCATIONS && !m_sceneNames.Contains(m_location))
            m_location = m_sceneNames.FirstOrDefault();
        LoadLocation();
    }

    // Each rip takes seconds to read, so they're only read when wanted, and reading them all can be stopped partway.
    private void LoadLocation()
    {
        m_loaded.Clear();
        string[] wanted = m_location == ALL_LOCATIONS ? m_sceneNames : m_sceneNames.Where(n => n == m_location).ToArray();
        try
        {
            for (int i = 0; i < wanted.Length; i++)
            {
                string name = wanted[i];
                RipScene scene;
                if (!s_scenes.TryGetValue(name, out scene))
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Construction Kit", "Reading " + LocationNames.For(name) + "...", (i + 0.5f) / wanted.Length))
                    {
                        m_status = $"Stopped reading rips, so this shows {m_loaded.Count} of {wanted.Length} locations.";
                        break;
                    }
                    scene = RipScene.Load(name, m_ripped[name]);
                    s_scenes[name] = scene;
                }
                m_loaded.Add(scene);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        UpdateCategories();
    }

    private void UpdateCategories()
    {
        m_categories = new[] { ALL_CATEGORIES }.Concat(m_loaded.SelectMany(s => s.Props).Where(p => InTab(p, m_tab)).Select(p => p.Category).Distinct().OrderBy(c => c)).ToArray();
        m_categoryIndex = 0;
        Filter();
    }

    private void Filter()
    {
        m_visible.Clear();
        m_visibleWater.Clear();
        m_visibleTextures.Clear();
        m_scroll = Vector2.zero;
        if (ShowsProps)
        {
            string category = m_categoryIndex > 0 && m_categoryIndex < m_categories.Length ? m_categories[m_categoryIndex] : null;
            m_visible = PropsFor(m_tab, m_loaded, category, m_search);
        }
        else if (m_tab == Tab.Water)
            m_visibleWater = m_loaded.Where(s => s.HasWater && Matches(s.SceneName, m_search)).ToList();
        else
            m_visibleTextures = TexturesFor(m_loaded, m_search);
    }

    public static bool InTab(RipScene.Prop prop, Tab tab)
    {
        switch (tab)
        {
            case Tab.Enemies: return prop.IsEnemy;
            case Tab.Items: return prop.IsItem;
            case Tab.Props: return !prop.IsEnemy && !prop.IsItem;
            default: return false;
        }
    }

    /// <summary>What a tab lists. A thing found in several locations is listed once, from the first of them.</summary>
    public static List<RipScene.Prop> PropsFor(Tab tab, IEnumerable<RipScene> scenes, string category, string search)
    {
        return scenes.SelectMany(s => s.Props)
            .Where(p => InTab(p, tab) && (category == null || p.Category == category) && Matches(p.BaseName, search))
            .GroupBy(p => p.BaseName).Select(g => g.First()).OrderBy(p => p.BaseName).ToList();
    }

    // Texture name to the location it's taken from, listed once each like the props.
    private List<KeyValuePair<string, string>> TexturesFor(IEnumerable<RipScene> scenes, string search)
    {
        var found = new Dictionary<string, string>();
        foreach (RipScene scene in scenes)
        {
            List<string> textures;
            if (!s_groundTextures.TryGetValue(scene.SceneName, out textures))
            {
                string root = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(m_ripped[scene.SceneName])));
                s_groundTextures[scene.SceneName] = textures = GroundTools.GroundTexturesIn(root);
            }
            foreach (string texture in textures.Where(t => Matches(t, search) && !found.ContainsKey(t)))
                found[texture] = scene.SceneName;
        }
        return found.OrderBy(t => t.Key).ToList();
    }

    private static bool Matches(string name, string search)
    {
        return search.Length == 0 || name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>The paths window calls this when they change.</summary>
    public void OnPathsChanged()
    {
        s_scenes.Clear();
        s_groundTextures.Clear();
        Refresh();
    }

    /// <summary>Lets the self-test go through the tabs.</summary>
    public void ShowTab(Tab tab)
    {
        m_tab = tab;
        UpdateCategories();
        Repaint();
    }

    private void OnGUI()
    {
        if (!OckSettings.PathsConfirmed)
        {
            EditorGUILayout.HelpBox("First, the kit needs to know where the game and your mods are on this PC.", MessageType.Info);
            if (GUILayout.Button(new GUIContent("Set Up Paths", "Opens the window that says where the game, your mods and your rips are.")))
                PathsWindow.Open();
            return;
        }
        DrawSettings();
        DrawStatus();
        if (m_sceneNames.Length == 0)
        {
            EditorGUILayout.HelpBox("No ripped scenes found. Rip one, or set the rip folder to the folder holding your AssetRipper exports.", MessageType.Info);
            if (GUILayout.Button(new GUIContent("Rip a Scene", "Copies a scene's models out of your own game files, for this window to show.")))
                RipWindow.Open();
            return;
        }

        EditorGUI.BeginChangeCheck();
        m_tab = (Tab)GUILayout.Toolbar((int)m_tab, s_tabNames);
        if (EditorGUI.EndChangeCheck())
            UpdateCategories();

        EditorGUILayout.LabelField(s_nextSteps[(int)m_tab], EditorStyles.wordWrappedMiniLabel);
        DrawFilters();
        switch (m_tab)
        {
            case Tab.Water:
                DrawWaterTab();
                break;
            case Tab.Ground:
                DrawGroundTab();
                break;
            default:
                if (m_tab == Tab.Items)
                    EditorGUILayout.HelpBox("Items can't be picked up yet, so for now they're only for show.", MessageType.None);
                DrawRows(m_visible.Count, i => DrawPropRow(m_visible[i]));
                break;
        }
        DrawRegionTools();
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    // The window's only feedback, so it sits above the tabs where it's seen, whichever one is open.
    private void DrawStatus()
    {
        if (string.IsNullOrEmpty(m_status))
            return;
        if (s_statusStyle == null)
            s_statusStyle = new GUIStyle(EditorStyles.helpBox) { fontSize = STATUS_FONT_SIZE, wordWrap = true };
        GUILayout.Label(m_status, s_statusStyle);
    }

    /// <summary>A brief message over the window, so a click visibly did something, and a lasting one with the details.</summary>
    private void Say(string flash, string details)
    {
        ShowNotification(new GUIContent(flash));
        m_status = details;
        Repaint();
    }

    private void DrawSettings()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Rip folder", GUILayout.Width(70));
            EditorGUI.BeginChangeCheck();
            string root = EditorGUILayout.DelayedTextField(OckSettings.RipFolder);
            if (GUILayout.Button(new GUIContent("...", "Choose the folder holding your rips."), GUILayout.Width(26)))
            {
                string picked = EditorUtility.OpenFolderPanel("Folder holding AssetRipper exports", root, "");
                if (!string.IsNullOrEmpty(picked))
                    root = picked;
                GUI.changed = true;
            }
            if (EditorGUI.EndChangeCheck())
            {
                OckSettings.RipFolder = root;
                OnPathsChanged();
            }
            if (GUILayout.Button(new GUIContent("Refresh", "Looks for newly finished rips."), GUILayout.Width(60)))
                OnPathsChanged();
            if (GUILayout.Button(new GUIContent("Rip a Scene", "Copies a scene's models out of your own game files, for this window to show."), GUILayout.Width(85)))
                RipWindow.Open();
            if (GUILayout.Button(new GUIContent("Rip All Scenes", "Rips every scene not yet ripped, one after another, which takes a very long time and hundreds of gigabytes. It says how much before starting."), GUILayout.Width(100)))
            {
                RipWindow.RipAll();
                GUIUtility.ExitGUI();
            }
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
            int location = EditorGUILayout.Popup(Mathf.Max(0, Array.IndexOf(m_locations, m_location)), m_locationLabels, GUILayout.Width(170));
            if (EditorGUI.EndChangeCheck())
            {
                m_location = m_locations[location];
                LoadLocation();
                GUIUtility.ExitGUI();
            }

            EditorGUI.BeginChangeCheck();
            if (ShowsProps)
                m_categoryIndex = EditorGUILayout.Popup(m_categoryIndex, m_categories, GUILayout.Width(120));
            m_search = EditorGUILayout.TextField(m_search, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
                Filter();
        }
        if (!ShowsProps)
            return;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"{m_visible.Count} found. Click Add, or drag a name into the Scene view.", EditorStyles.miniLabel);
            m_showPaths = EditorGUILayout.ToggleLeft("Paths", m_showPaths, GUILayout.Width(60));
        }
    }

    private void DrawWaterTab()
    {
        bool waterSelected = GroundTools.IsWater(Selection.activeGameObject);
        using (new EditorGUI.DisabledScope(!waterSelected))
        {
            int waterType = waterSelected ? GroundTools.WaterTypeIndex(Selection.activeGameObject) : 0;
            int chosenType = EditorGUILayout.Popup(new GUIContent("Water type", "What players get from drinking this water or filling a waterskin with it."), waterType, GroundTools.WaterTypes);
            if (waterSelected && chosenType != waterType)
                GroundTools.SetWaterType(Selection.activeGameObject, chosenType);
        }
        EditorGUILayout.LabelField(waterSelected ? "Choose this water's type. How it looks comes from its location, and doesn't change with its type." : "Select water in your scene to set its type. How water looks comes from the location below.", EditorStyles.wordWrappedMiniLabel);

        DrawRows(m_visibleWater.Count, i =>
        {
            RipScene scene = m_visibleWater[i];
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(ROW_HEIGHT)))
            {
                GUILayout.Label(new GUIContent("Looks like the water in " + LocationNames.For(scene.SceneName), "Only its look comes from there. Its type is set above."));
                GUILayout.FlexibleSpace();
                if (!scene.HasWaterSource)
                    GUILayout.Label(new GUIContent("can't be drunk", "This location has no water source to copy, so players can't drink from this water or fill a waterskin."), EditorStyles.miniLabel, GUILayout.Width(80));
                if (GUILayout.Button(new GUIContent("Add", "Adds a marker at the middle of your view, to move and scale. In game it becomes water that looks like this location's."), GUILayout.Width(44)))
                {
                    Selection.activeGameObject = GroundTools.AddWater(scene.SceneName);
                    Say("Water added", "Water added at the middle of your view. Move the blue marker into a hollow in your ground, just below ground level, and scale it to fit. In game it's replaced by real water at that height and size.");
                }
            }
        });
    }

    private void DrawGroundTab()
    {
        Terrain ground = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Terrain>(true)).FirstOrDefault();
        if (ground == null && GUILayout.Button(new GUIContent("Add Ground", "Replaces the starter room with a terrain. Sculpt and paint it with the Terrain component's tools in the Inspector.")))
        {
            GroundTools.AddGroundFromMenu();
            Say("Ground added", "Ground added and selected. In the Inspector on the right, click the paintbrush button, then use the dropdown just under it: Raise or Lower Terrain and Smooth Height for hills, Paint Texture for grass, dirt, sand and rock.");
            GUIUtility.ExitGUI();
        }
        EditorGUILayout.LabelField(ground == null ? "Add ground first, then its textures." : $"{m_visibleTextures.Count} textures to add to your ground's Paint Texture layers.", EditorStyles.miniLabel);
        if (!s_layerPreviewsChecked)
        {
            s_layerPreviewsChecked = true;
            EditorApplication.delayCall += GroundTools.ShowWholeTexturesInLibrary; // it reimports, which can't happen while drawing
        }

        DrawRows(m_visibleTextures.Count, i =>
        {
            KeyValuePair<string, string> texture = m_visibleTextures[i];
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(ROW_HEIGHT)))
            {
                GUILayout.Label(texture.Key);
                GUILayout.FlexibleSpace();
                if (m_location == ALL_LOCATIONS)
                    GUILayout.Label(LocationNames.For(texture.Value), EditorStyles.miniLabel, GUILayout.Width(150));
                TerrainLayer added = ground != null ? GroundTools.FindLayer(ground, texture.Key) : null;
                if (added != null)
                {
                    GUILayout.Label(new GUIContent("added", "Your ground already has this texture."), EditorStyles.miniLabel, GUILayout.Width(36));
                    if (GUILayout.Button(new GUIContent("Paint", "Selects your ground, with its Paint Texture tool on and this texture chosen."), GUILayout.Width(44)))
                        SayReadyToPaint(ground, added, "Ready to paint");
                    return;
                }
                using (new EditorGUI.DisabledScope(ground == null))
                {
                    if (GUILayout.Button(new GUIContent("Add", "Adds this game texture to your ground and gets it ready to paint with. It ships as a reference, so it adds nothing to your mod's size."), GUILayout.Width(44)))
                        SayReadyToPaint(ground, GroundTools.AddLayer(ground, texture.Value, texture.Key), "Texture added");
                }
            }
        });
    }

    private void SayReadyToPaint(Terrain ground, TerrainLayer layer, string flash)
    {
        const string steps = "If it isn't ready, select the Terrain in the Hierarchy. In the Inspector on the right, click the paintbrush button, choose Paint Texture from the dropdown just under it, and click the texture under Terrain Layers. The Shader menu above Terrain Layers belongs to the ground's material, so leave that alone.";
        bool ready = GroundTools.ReadyToPaint(ground, layer);
        Say(flash, ready
            ? $"Ready to paint with {layer.name}: drag over your ground in the Scene view. The Inspector on the right now has the Terrain's Paint Texture tool on. {steps}"
            : $"{layer.name} is on your ground, and needs choosing by hand. {steps}");
    }

    private void DrawRows(int total, Action<int> drawRow)
    {
        m_scroll = EditorGUILayout.BeginScrollView(m_scroll, GUILayout.ExpandHeight(true));

        // Thousands of rows are too slow to lay out, so only the visible ones are drawn.
        int first = Mathf.Max(0, Mathf.FloorToInt(m_scroll.y / ROW_HEIGHT));
        int count = Mathf.CeilToInt(position.height / ROW_HEIGHT) + 1;
        GUILayout.Space(first * ROW_HEIGHT);
        for (int i = first; i < Mathf.Min(total, first + count); i++)
            drawRow(i);
        GUILayout.Space(Mathf.Max(0, total - first - count) * ROW_HEIGHT);

        EditorGUILayout.EndScrollView();
    }

    private void DrawPropRow(RipScene.Prop prop)
    {
        Rect row = EditorGUILayout.BeginHorizontal(GUILayout.Height(ROW_HEIGHT));
        string label = prop.BaseName;
        if (m_showPaths)
            label = prop.Node.ScenePath;
        GUILayout.Label(new GUIContent(label, prop.Node.ScenePath));
        GUILayout.FlexibleSpace();
        if (!prop.IsEnemy && prop.Node.SelfAndDescendants().Any(n => n.IsInteractive))
            GUILayout.Label(new GUIContent("decoration", "Players can use this in its own scene, but in a region it's only for show."), EditorStyles.miniLabel, GUILayout.Width(60));
        if (m_location == ALL_LOCATIONS)
            GUILayout.Label(LocationNames.For(prop.Scene.SceneName), EditorStyles.miniLabel, GUILayout.Width(150));
        GUILayout.Label(prop.Category, EditorStyles.miniLabel, GUILayout.Width(80));
        if (GUILayout.Button(new GUIContent("Add", "Adds this at the middle of your view. The first use copies its model from the rip, which takes a moment. You can also drag its name into the Scene view."), GUILayout.Width(44)))
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
            GameObject prefab = PropImporter.Import(prop.Scene, prop);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = point;
            Undo.RegisterCreatedObjectUndo(instance, "Add " + prop.BaseName);
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(instance.scene);
            Say("Added", "Added " + prop.BaseName + ". It's selected, so you can move it with the Move tool.");
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
