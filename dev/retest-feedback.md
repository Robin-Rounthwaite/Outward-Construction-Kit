# Retest feedback

From the retest of steps 1 to 4 on 2026-11-01. Still for a person to test: the defeat scenario, and steps 5 onward from the list at the top of `test-feedback.md`.

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
