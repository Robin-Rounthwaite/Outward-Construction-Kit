using System;
using UnityEditor;
using UnityEngine;

/// <summary>The part of the browser below the tabs, about the region being made rather than what's going in it.</summary>
internal partial class PropBrowser
{
    private void DrawRegionTools()
    {
        if (GUILayout.Button(new GUIContent("Add Return Door", "Adds a marker for a door back to where players entered. The small box shows the side they walk up to. With none, a door appears beside the arrival point.")))
        {
            DoorTools.AddFromMenu();
            Say("Return door added", "Return door added. Stand it on the ground, with the small box facing the way players will approach.");
        }

        if (!RegionExporter.HasSpawnPoint(UnityEngine.SceneManagement.SceneManager.GetActiveScene()))
        {
            EditorGUILayout.HelpBox("This scene has no spawn point, so it can't be exported yet. Players arrive at it.", MessageType.Warning);
            if (GUILayout.Button(new GUIContent("Add Spawn Point", "Adds where players arrive, at the middle of your view.")))
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
                if (DoorTools.ModelMissing && GUILayout.Button(new GUIContent("Show Door Model", "Your return door marker is a plain box until this door's model is brought in from its rip, which takes a few seconds."), GUILayout.Width(120)))
                {
                    DoorTools.ImportMissingModel();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(new GUIContent("Use Selected Prop as Door Model", "All this region's doors, entrance and return, will look like the prop selected in the scene, at its scale, from your next export. Turn it first so its front faces along the blue axis, away from the wall it would sit in. The prop itself stays where it is."), GUILayout.Width(205)))
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
                        DoorTools.Refresh();
                        Say("Door model chosen", "Doors will look like " + selected.name + " on your next export. Return door markers show it now.");
                    }
                }
                using (new EditorGUI.DisabledScope(!custom))
                {
                    if (GUILayout.Button(new GUIContent("Reset", "Goes back to the kit's default door."), GUILayout.Width(50)))
                    {
                        info.DoorModelScene = info.DoorModelPath = null;
                        info.SaveFor(scenePath);
                        DoorTools.Refresh();
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                string door = string.IsNullOrEmpty(info.DoorScene) ? "none yet" : LocationNames.For(info.DoorScene) + " at " + info.DoorPosition;
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
            int picked = EditorGUILayout.Popup(Mathf.Max(0, current), m_sceneLabels);
            if (EditorGUI.EndChangeCheck())
                OckSettings.EnvironmentDonor = m_sceneNames[picked];

            if (GUILayout.Button(new GUIContent("New Region Scene", "Makes a new scene with a walled room, a sun and a spawn point, which is already enough to load in the game."), GUILayout.Width(125)))
                RegionExporter.NewRegionScene();
            if (GUILayout.Button(new GUIContent("Export Region", "Builds this region into your r2modman profile, so it's there the next time you start the game."), GUILayout.Width(100)))
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

        GameLauncher.DrawCharacterChoice(150);
        EditorGUILayout.LabelField("Testing plays and saves as this character, so its save changes. A character kept just for testing is safest.", EditorStyles.wordWrappedMiniLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Test Region In-Game", "Exports this region, starts the game with your mods, and takes the chosen character straight there. It plays and saves as normal."), GUILayout.Width(135)))
            {
                m_status = GameLauncher.TestRegion();
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button(new GUIContent("Launch Game", "Starts the game with your mods, the same as Start modded in r2modman."), GUILayout.Width(95)))
                m_status = GameLauncher.Launch();
        }
    }
}
