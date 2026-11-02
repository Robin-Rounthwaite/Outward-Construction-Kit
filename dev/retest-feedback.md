# Retest feedback

From the retest of steps 1 to 4 on 2026-11-01. The action items below are built, apart from testing Paint Holes in game (9), which needs a person, and the 18-second wait (8), which hasn't returned to be measured. Still for a person to test: these changes, the defeat scenario, and steps 5 onward from the list at the top of `test-feedback.md`.

## Findings

- **Prop Browser opening:** it no longer has the long stall. The logs show its longest wait this session was about 18 seconds, right after the material check, and reading all locations took 9.5 seconds. That reading should be quicker from now on, because it's now cached. I can't say which change fixed the stall, and the 18 seconds is still unexplained.

## Questions answered

- **How do I get back to the ground's paintbrush after adding water?** Click **Ground** in the Hierarchy panel, usually on the left. The Inspector then shows the Terrain component again, with its paintbrush tools. Choose **Raise or Lower Terrain** in the dropdown to change altitude. You don't need to add ground again.
- **What does "Paint Holes" mean?** It's a Unity terrain tool that cuts see-through, walk-through holes in the ground, such as cave entrances. I haven't tested whether holes survive export and work in game.
- **How do I add the actual door after Use Selected Prop as Door Model?** That button only sets what the doors look like. Doors are added separately:
  - **Return door:** click **Add Return Door** in the Prop Browser, and place the marker. Without one, a door appears beside the arrival point by itself.
  - **Entrance door:** this is placed in the game. Export, start the game, press **Ctrl+F8** until your region is named, then **Ctrl+F10** where you want the door. Back in Unity, click **Use door placed in game**, so it ships with your mod.
  - Export again after changing the door model, because it's saved when you export.

## Action items

### Ground
1. **Add Ground opens a small window** for choosing the starting ground texture and the size. Use the 200 by 200 m size as the default, and check what sizes the kit's NavMesh and export handle well.
2. **Start the Prop Browser on the Ground tab,** since that's what people need first. It should still remember the tab they last used.

### Water
3. **Let people choose the water type before adding water.** The Water tab always has an enabled dropdown. With nothing selected, it sets the type for the next water added. With water selected, it shows and changes that water's type.

### Text size
4. **Make small messages larger:** "This scene has no spawn point...", "Door looks like: the kit's default door", and everything else using that small font, across the kit's windows. Check that the longer rows still fit, and that they wrap if not.

### Doors
5. **Make the door steps clearer in the Prop Browser.** Group the door controls in a section with three short steps: how the doors look, the return door, and the entrance. Say what to do next after **Use Selected Prop as Door Model**.

12. **Move Add Return Door** next to **Use Selected Prop as Door Model**, and place **Add Spawn Point** next to Add Return Door. Add Spawn Point currently only appears when a scene has no spawn point, so decide whether it stays visible in its new place.
13. **Fix the return door marker and door model not lining up.** They appear side by side, and should overlap. The marker is the see-through blue slab, and the model is drawn on top of it as a preview, so the preview's position or the model's own offset looks wrong. The in-game door also needs checking against this.
14. **Make the door model feel like part of the marker.** It can't be selected or moved because it's only a drawn preview, which is why only the slab responds. Clicking the model should select the marker, and it should move with it. If that can't be done with a preview, use a real object that's kept out of exports.
15. **Check that the model shown matches the chosen one.** You saw the wood and iron door, which is the default. Confirm whether a model had been chosen for this scene, since the choice is saved per scene, and show it clearly in Unity.

### Test Region In-Game
17. **Stop the main menu inviting clicks during a test start.** The kit waits for the menu to be ready, then clicks through it by itself. Try first: turn off the menu's input while this happens, and show a "Starting your test..." message over it. The menu has to stay active for the kit to use it, so check that it still works this way. If that can't be done easily, add to the button's hover text: "The main menu shows for a moment. There's no need to click anything."

### Team name
16. **Change the mod's team name to `Blue_Chamber_Collective`.** This replaces `Cat_and_Community` in the dependency that region mods declare on the kit, the Thunderstore links, the plugin's folder name and the matching test. Then rename the folder in the test profile, and rebuild the zips. The name's availability needs checking when you create the team on Thunderstore.

### Guides
6. **Record in the README and tutorial** how to return to the ground tools after adding water, by clicking **Ground** in the Hierarchy. Also add the door steps above.
7. **Explain Paint Holes** in `docs/making-a-region.md`, once it's been tested in game. If holes don't survive export, say so there.

### Investigate
8. **Find what's behind the 18-second wait** after the material check, using the timing lines. Do this if it comes back or if it's easy to see.
9. **Test Paint Holes** through an export and a game run.

### Additional ideas
10. **Add a "Select Ground" button** to the Ground tab, so returning to the tools doesn't rely on finding it in the Hierarchy.
11. **Show the current door setup** in one line: the model, whether a return door exists, and whether the entrance is set. This could become part of the readiness checklist.

## Testing your level, 2026-11-01

Items 18 to 31 are built, except where noted below.

- **Why the Paths settings changed (24):** Unity started from the Hub keeps its own settings, apart from Unity started by me. The settings my Unity sees still say `modding-experiments`, while your Hub-started Unity exported to `Catalysten Modlist`. Modders always start it the same way, so this should only affect this PC's mix of the two.
- **Team name (31):** Thunderstore's guide lists no team in `manifest.json` or the zip. It doesn't say where the team is chosen, so the upload page is still my understanding.
- **Entrance warning (28):** removed at your request, so **Make Upload Zip** no longer stops without an entrance. The export details still say "entrance not set".
- **Bush door (29):** you found your door, so there's nothing more to check.

### Findings

- **The test started the wrong mod list.** Unity's BepInEx plugins folder setting points to the `Catalysten Modlist` profile, not `modding-experiments`, so the export went there and the game started with that list. The Paths window fills that box with the first profile it finds, and `Catalysten Modlist` comes first alphabetically. So this may have been the suggestion left in place when the window reappeared, rather than a wrong choice. This fits the guess that Unity started from the Hub keeps separate settings, but doesn't prove it.
- **The test couldn't have reached your region.** `Catalysten Modlist` doesn't have the kit installed, so nothing could take the character there, and the game would stop at the main menu.
- **The black screen:** the game did reach the main menu eventually. You think your bigger mod list just took a long time, so this isn't an action item.
- **Left in your main mod list:** the export created `BepInEx\plugins\OCK-Region-catalysten_test_after_better_prop_browser` in `Catalysten Modlist`. It's still there. I only looked in the plugins folder, so I can't say whether the test changed anything else in that profile.
- **New Region Scene doesn't overwrite your scene.** It offers to save unsaved changes, then makes a new file under `Assets/Regions`, with a number added if the name is taken. Your scene stays on disk, but it's closed and replaced in the editor with no warning if it had no unsaved changes.
- **The export message includes a warning about your level:** the return door at (-11.8, -5.9, 30.0) has no ground under it, or is below the ground. I haven't checked the scene.
- **Your door after choosing the bush:** the 6:12 PM export did save the bush as the door model, and it has two return doors, at (-11.8, -5.9, 30.0) and (30.8, -0.4, 16.1). The first may be under the ground, so you wouldn't see it. The entrance isn't set, so a normal game has no door leading in. The test never reached your region, so nobody has seen these doors in game. I don't know yet which door you were looking for, or where.
- **Package Region doesn't upload anything.** **Make Upload Zip** only makes a zip, always in `Build/Upload` inside the Unity project.
- **Why Use Door Placed in Game is needed today:** Ctrl+F10 saves the door to the config file in your own profile, and players don't get that file. The button copies it into the region's details, which is what ships. Nothing makes that a manual step, apart from the export not reading the file itself.

### Action items

18. **Ask before New Region Scene replaces the open scene.** Say that the current scene stays saved and is only closed, and name it. Offer **Create** and **Cancel**.
19. **Make New Region Scene harder to click by accident.** It sits right beside **Export Region** today. Move it away from the export and test buttons.
20. **Warn when the chosen profile doesn't have the kit.** Show it in the Paths window, and stop **Test Region In-Game** with a clear message before it starts the game. Today it starts the game anyway, and the player waits at a menu.
21. **Don't suggest a profile just because it's first.** Prefer a profile with the kit installed. If none has it, or more than one does, leave the box empty so the choice is made on purpose.
22. **Name the mod list in the Prop Browser,** for example "Testing in: modding-experiments", so a wrong one shows before anything runs.
23. **Shrink the export message in the Prop Browser.** It fills most of the window at its default size. Show one short line and any real warnings, such as the return door one, and put the rest behind a Details foldout and in the Console.
24. **Find out why the Paths settings changed,** which is open question 26a on the to-do list.
25. **Replace the walled room.** Decided: New Region Scene makes a sun and a spawn point with no room, then opens the Add Ground window. It stays in the menu and First Steps, and leaves the Prop Browser, which settles item 19. Item 18's question still applies.
   - The walled room was built to prove that a scene loads in the game. It's a poor start for building, because the first job is usually deleting the walls.
   - To check first: whether the tests or the Example Region rely on the room, and what catches the player if they test before adding ground.
26. **Edit the tutorial for item 25.** Step 1 still promises a walled room that's "already enough to load in the game", and step 2 adds the ground. The Prop Browser's hover text says the same, so change that too.
27. **Prompt for ground on the Props tab.** Show a temporary pop-up over the tab when the scene has no ground, asking the user to add it, ideally with a way to open Add Ground.
28. **Use the entrance placed in game automatically.** Export and Make Upload Zip read this region's door from the profile's config themselves, and say what they used in the export message. Then remove **Use Door Placed in Game**, and update the tutorial and hover texts. To decide when building: a door placed by accident replaces the shipped one on the next export, so the message has to make a change easy to notice.
29. **Make it easier to find doors in the scene.** Add a button that selects and frames each return door, and make it easy to bring one up to the ground, because a marker under the ground can't be seen. Check that the bush model really draws at the markers.
30. **Choose where the upload zip goes.** Add a save location to the Package Region window, with a **...** button, remembered for next time. It starts at today's `Build/Upload`. The window keeps only making the zip, and the user uploads it.
31. **Add the team name to the Package Region window.** Remember it, and use it to name the zip and to say which team to upload under. As far as I know, Thunderstore takes the team from the upload page and not from the zip, so I need to confirm that when building. If it's right, this field can't set the team by itself. The kit's own team stays fixed in the dependency.
