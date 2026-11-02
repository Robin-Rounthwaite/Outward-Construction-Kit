using System;
using UnityEditor;
using UnityEngine;

/// <summary>The part of the browser below the tabs, about the region being made rather than what's going in it.</summary>
internal partial class PropBrowser
{
    private void DrawRegionTools()
    {
        UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!RegionExporter.HasSpawnPoint(activeScene))
            KitStyles.HelpBox("This scene has no spawn point, so it can't be exported yet. Players arrive at it.", MessageType.Warning);

        string scenePath = activeScene.path;
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
            bool custom = !string.IsNullOrEmpty(info.DoorModelPath);
            string entrance = string.IsNullOrEmpty(info.DoorScene) ? "not placed yet" : LocationNames.For(info.DoorScene) + " at " + info.DoorPosition;
            int returnDoors = System.Linq.Enumerable.Count(DoorTools.Doors(activeScene));
            EditorGUILayout.LabelField("Doors look like " + (custom ? System.IO.Path.GetFileName(info.DoorModelPath) : "the kit's default door") + ". Return doors: " + (returnDoors > 0 ? returnDoors.ToString() : "automatic, beside the arrival point") + ". Entrance: " + entrance + ".", KitStyles.WrappedNote);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (DoorTools.ModelMissing && GUILayout.Button(new GUIContent("Show Door Model", "Your return door marker is a plain box until this door's model is brought in from its rip, which takes a few seconds.")))
                {
                    DoorTools.ImportMissingModel();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(new GUIContent("Use Selected Prop as Door Model", "Sets how all this region's doors look, entrance and return, using the prop selected in the scene, at its scale. It doesn't add a door. Turn the prop first so its front faces along the blue axis, away from the wall it would sit in. The prop itself stays where it is.")))
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
                        Say("Door model chosen", "Doors will look like " + selected.name + " from your next export, and return door markers show it now. This doesn't add a door. Next: click Add Return Door to choose where the door back is. The entrance is placed in the game, with Ctrl+F8 to choose this region and Ctrl+F10 to place it, then Use Door Placed in Game here.");
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
                if (GUILayout.Button(new GUIContent("Add Return Door", "Adds a marker for a door back to where players entered. The small box shows the side they walk up to. With none, a door appears beside the arrival point.")))
                {
                    DoorTools.AddFromMenu();
                    Say("Return door added", "Return door added. Stand it on the ground, with the small box facing the way players will approach.");
                }
                GameObject spawn = RegionExporter.FirstSpawnPoint(activeScene);
                if (spawn != null)
                {
                    if (GUILayout.Button(new GUIContent("Select Spawn Point", "Selects where players arrive, so you can move it with the Move tool.")))
                        Selection.activeGameObject = spawn;
                }
                else if (GUILayout.Button(new GUIContent("Add Spawn Point", "Adds where players arrive, at the middle of your view.")))
                {
                    SceneView view = SceneView.lastActiveSceneView;
                    Vector3 spot = view != null ? view.pivot : Vector3.zero;
                    Vector3 ground;
                    Selection.activeGameObject = RegionExporter.AddSpawnPoint(TryFindGround(new Ray(spot + Vector3.up * 50f, Vector3.down), out ground) ? ground : spot);
                    Say("Spawn point added", "Spawn point added at the middle of your view. It's selected, so you can move it with the Move tool.");
                }
                if (GUILayout.Button(new GUIContent("Use Door Placed in Game", "Copies where you placed this region's entrance with Ctrl+F10, so it ships with the mod.")))
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
        EditorGUILayout.LabelField("Testing plays and saves as this character, so its save changes. A character kept just for testing is safest.", KitStyles.WrappedNote);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Test Region In-Game", "Exports this region, starts the game with your mods, and takes the chosen character straight there. It plays and saves as normal. The main menu shows for a moment, and there's no need to click anything."), GUILayout.Width(135)))
            {
                m_status = GameLauncher.TestRegion();
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button(new GUIContent("Launch Game", "Starts the game with your mods, the same as Start modded in r2modman."), GUILayout.Width(95)))
                m_status = GameLauncher.Launch();
        }
    }
}
