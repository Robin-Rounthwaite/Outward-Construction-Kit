# Test feedback

Findings from the hands-on tests, to apply together. Nothing here has been built yet.

## Test 1: New region, Prop Browser, ground and water

### Answers

- **"New Region" or "New Region Scene"?** Both are right, and they do the same thing. The Construction Kit menu says **New Region Scene**, and the Prop Browser's button says **New Region**.
- **Is `3100000_ElitePlateArmor_v` in the right place?** Yes. It's a display model placed by hand in Monsoon (`Environment/Assets/Props`), with none of the game's item scripts, so it can't be picked up there. Props is the right tab. Monsoon has four of these, and the number in each name looks like the real item's ID.
- **Why does the Items tab have only 3 things?** The tab only finds items sitting in the scenes you've ripped: one in Monsoon and two in Chersonese. The full list isn't in any scene. The F1 debug menu reads it from the game's own `items` bundle (301 MB, with models in the 1.9 GB `item_visuals`). So your idea should work, and the kit can read that list directly.
- **What does "The scene's own" water type mean?** The water keeps whatever type the location it was copied from has, so its type depends on a choice made for its looks. That isn't useful, so it goes.

### Action items

**Items**
1. Fill the Items tab from the game's full item list, with each item's ID and name, instead of from scenes. Reading the bundle uses the same tool as the asset cache, but I haven't tried it on this bundle. I also haven't counted the items, or checked how to get English names outside the game.
2. Make placed items pickable, before the beta, as decided by the user. The game can create a real item from its ID, including the pot kit (ID 5010100).

**Ground**

3. Make **Add** on a ground texture visibly respond. Today the only response is a small message at the bottom of the window.
4. Make that message larger, or use a pop-up over the window, and say where the brush is: select the Terrain, then in the Inspector choose **Paint Texture** from the tool dropdown.
5. After **Add**, select the Terrain, switch it to Paint Texture, and select the newly added texture, so the next stroke paints with it. Those two switches use Unity internals with no public way in, so I still need to confirm they can be done. If not, the message should walk through the steps instead.
6. The "Shader" dropdown is the menu just under the paint button, in Unity's right-hand sidebar. Check it against the Paint Texture panel, so the guide names the right control.

**Water**

7. Remove "The scene's own", and make **Clean** the default. Older water markers with no type become Clean.
8. In the guides, make "lower a hollow first" its own clear step before adding water. The tutorial mentions it in passing today.

**Wording**

9. Rename the Prop Browser's button to **New Region Scene**, so it matches the menu and the guides.

### Additional ideas

- **Water placement:** add water at the middle of the Scene view, as props are, rather than at a fixed spot in the world. A new marker can currently start out of sight.
- **Water hint:** after adding water, the message could say to move it into a hollow, just below ground level.
- **Water that can't be drunk:** some locations have no water source to copy, so their water is labelled "can't be drunk" and choosing Clean wouldn't change that. Since Clean is the default, it would be better to borrow a water source from another location. I haven't checked whether that works.
- **Ground list:** mark textures already added to the Terrain, so a second click doesn't look broken.
- **Ground buttons:** add a **Paint** button beside textures already added, which selects that texture and brush in one click.
- **Messages:** move the browser's status message to the top of the window, or next to the thing clicked, across all tabs. The same problem can appear anywhere it reports something.
- **First-time guidance:** add a short "what to do next" line to each tab, such as Add Ground > Sculpt > Add textures > Paint.

## Test 2: Test Region and Launch Game

Tested with the Example Region, using Test Region.

### Answers and analysis

- **HUD, menus, door prompt and water (likely one bug, not confirmed):** nothing in the kit hides the HUD on purpose, and it wasn't meant to be off. Test Region loads the character through the kit's automated test path and presses "any key" for the player, which may skip a step that turns the HUD on. Interaction prompts may depend on the same step, which could also explain the door and water having no prompt. The in-game checks passed for that run: the water is Salt and drinkable, and a return door exists. But they only check that these things exist, not that a player can use them.
- **Quick way to narrow it down:** start with **Launch Game** instead, and walk through the Example Region's door from Chirpy Cierzo's side. If the HUD and prompts work that way, Test Region's path is the cause. If not, it's the doors and water themselves. No person has used a kit return door since real doors were added, because the automated tests travel by calling the game directly.
- **Pot kit:** it's for show today, because pickable items are after-beta item 4. So this is known behaviour, but as you say, not what we want. It's a real item (ID 5010100). Whether pickable items should move ahead of the beta is your call.
- **Marsh-looking water:** this one is Monsoon's water with its type set to Salt. Look and type are separate settings, so it looks like marsh water by design, and the Example Region is a poor showcase of it.
- **Door missing in Unity:** the return door in Unity is a plain blue marker made of two boxes, so it isn't really missing a texture. The real model is only built in game.
- **"xN" beside names:** it's how many copies of that thing are in the location you ripped. It doesn't affect anything you add, so it can go.
- **Death return:** it works. Customising death scenarios is already on the after-beta list. The kit currently skips the game's own defeat handling, so I still need to find how the game shows its splash screens and whether one can be reused.

### Action items

1. Find and fix why the HUD and menus are off with Test Region. New modders must not get this.
2. Make the return door show a prompt and work, then have a person confirm it. Same for drinking from and filling at water, including Salt.
3. Add a check to the automated tests that a player can actually interact with doors and water, not only that they exist.
4. Water Type: always show the dropdown on the Water tab, and update it the moment water is selected. It only appeared on hover because the window doesn't redraw when the selection changes, which affects the whole browser.
5. Rename **Test as** to **Test as (Character Name)**, and **Test Region** to **Test Region In-Game**, including the menu item and guides.
6. Replace the name box with a dropdown of all characters, most recently used first. Today, an empty box loads the first character listed, which can change a real character's save, so this also removes a risk.
7. Remove the "xN" from the Enemies tab, and from Props too, for consistency.
8. Show the real door model in Unity, on the return door marker and for "Use selected prop".
9. Show a defeat splash screen before the return, with text like "You were injured badly, and left the space to nurse yourself to health." Use the game's own screen if it can be reused.
10. Check that every prop has its textures in Unity, without screenshots: for each prop, follow its materials to their texture files in the rip, and list any that are missing or would end up blank. This is only text processing, so it should be cheap. It could run as an automated test or a menu command.
11. Choose a better water and setup for the Example Region, once water works.

12. **Launch Game failed twice before working, cause unknown.** Possible causes, none checked:
    - The game was still closing from the last run, so the kit refused to start it. That refusal only appears in the small status message.
    - Steam's launch-options prompt was hidden or missed.
    - Steam ignored the request.

    The button reports "starting" as soon as it has asked Steam, without knowing whether the game started. Action: make the reason for a refusal obvious, and check whether the game actually appears, with a clear message if it doesn't.

### Added to the after-beta list

- A **New character** option at the top of the character dropdown, with a dialog for the name, which enforces Outward's length limit and becomes the new test character. I haven't looked up the limit yet.
- Customising death scenarios (already listed).

### Additional ideas

- **Test warning:** say that testing changes the chosen character's save, and suggest keeping a character just for testing.
- **Characters with the same name:** the game allows this, I believe, and the kit picks by name today. The dropdown should choose by save ID.
- **Water looks:** rename the Water tab's "Water from Monsoon" rows to describe the look, or add a small preview, so people know what they're choosing.
- **Pickable items:** the same item list as in Test 1 could make these fairly direct, because the game can create a real item from its ID.
- **Finding the test buttons:** you had to ask where "Test as" was. It's in the bottom row of the Prop Browser, below the list. Move the test and export buttons into a clearly labelled section, and have the guide say where to look.

## Test 3: Doors

### Answers and analysis

- **"Use selected prop":** it doesn't turn the object into a door. It sets the model used for all of this region's doors (entrance and return), taken from the prop selected in the scene, on the next export.
- **Ctrl+F8 and your bush:** Ctrl+F8 only cycles which *region* the next door is for, and Ctrl+F10 places it. Neither picks a model. Your export does list the bush as the door model, so the entrance door should have looked like it. Why it never did, and why the iron and wood door only appeared sometimes, is unknown. You couldn't see the kit's messages because the HUD was off, which is Test 2's bug.
- **Purple bush leaves:** purple usually means a shader is missing in game. The bush comes from the game's foliage, whose leaves probably use a shader the kit isn't finding. It looks fine in Unity because the kit's import uses the ripped mesh and textures but rebuilds each material on its own `OCK/StandIn` shader, since ripped shaders are only placeholders. In game, the prop is the real game asset with its real shader. Not checked yet.

### Action items

1. Rename the button, for example to **Use Selected Prop as Door Model**.
2. Give every button a tooltip saying what it does. Agreed by the user. Some already have one, including this button. Use Unity's own short delay rather than a custom 3 seconds.
3. Find out why a chosen door model doesn't appear, and why the default door appears only sometimes. Make the keys' results visible, such as naming the chosen region on screen, even if that needs a fallback.
4. Fix the purple leaves. Then add a check, for all props, that every material finds a real shader in game. Include foliage in the automated game run.

## Test 4: Rip a Scene and the Prop Browser

### Answers and analysis

- **Does the AssetRipper download repeat?** No. It's installed once in `_AssetRipper` inside your rip folder and reused after that, so you saw the first-time download. It doesn't look for copies elsewhere, though the script can be given one.
- **Why was the transition scene 3.1 GB?** Very little of that is the scene. Each rip also exports the game assets the scene can reach through shared files: this one has 1.9 GB of textures, 445 MB of audio and 357 MB of animations. I haven't checked exactly which files bring them in.
- **Why such a big Rip All estimate?** It's simply the number of scenes times a typical rip size, with shared assets counted again in every rip and textures unpacked to PNG. The whole game folder is only 32 GB, so your instinct is right: a good rip of everything should cost far less. The estimate describes today's wasteful method.

### Action items

1. **Stop ripping shared assets repeatedly.** This is the biggest saving. Options, to compare before choosing:
   - Export shared assets once to a common folder.
   - Remove duplicates after each rip, keyed on each asset's ID. This needs AssetRipper's IDs to match across rips, which I haven't confirmed.
   - Rip only what the browser and importer need, such as skipping audio and animations.

   Then redo the estimate from real measurements.
2. **One entry per asset, listing all its locations.** Duplicates should show once, with every location they're found in, and be copied into the project once.
3. **Group the location column by region.** Dungeons count under their host region, such as a Chersonese dungeon showing as "Chersonese". This needs a scene-to-region table added to `LocationNames`.
4. **Add new rips automatically** when a rip finishes, with no Refresh needed.
5. **Make Refresh add only what's new, and remove what's gone.** Compare the list of scene files with what's already read, using each file's date to catch changed rips. Add new ones, drop any no longer in the folder, and keep everything else in memory. Keep a separate way to force a full re-read.
6. **Rip list:** mark scenes that offer little, such as `LowMemory_TransitionScene`, or move them to the end. It only offered some ground textures.
7. **Use an existing AssetRipper:** let the Paths window point to one you already have. Keep the kit on a tested version rather than the newest, because a new version could change the output the kit reads. Checking for other copies automatically isn't reliable, since there's no standard install location.
8. **Terrain Layers palette looks blank:** the textures paint correctly but the previews are nearly invisible. My guess is that the palette shows the textures' alpha channel, which in game textures often holds other data. If so, importing them with alpha ignored should fix it. Not checked yet.

### Added to the after-beta list

- Consider how to filter to only dungeon assets, or only assets from exterior regions.

## Test 5: Broken region test

- **Not run yet:** the command failed with "does not exist" because it was run from the old OneDrive folder, and the path in it only works from the repo.
- **Action:** write commands in guides and walkthroughs so they work from any folder, using full paths or a change of folder first.

## Test 6: Importing the zips

- The region zip in `dist` was assembled by hand from the exported region, so Unity wouldn't be interrupted. The **Package** button itself still needs a person to try it, and its README and description are placeholders.

## Tests 7 and 8: Guides

### Action items

1. For now, add a note to the readme and guides asking for feedback as people go through them: if they ever feel stuck or even briefly confused, tell Cat in the Catalysten-Workshop, so it can be made clearer. I still need to confirm the link to use.

"Review the tutorial" is now on your before-beta to-do list.

## Polish ideas (not yet agreed)

- Readiness checklist: what the region has and lacks, with a button to fix each.
- Test from here: start the test at the Scene view's position.
- Rip progress, time remaining and a finished message inside Unity.
- Prop thumbnails in the browser's rows.
- Favourites and recently used at the top of each tab.
- Drop to Ground for selected props.
- Offer to save the scene before Export or Test Region.
- Export summary in the window, with links that select the object at fault.
- In-game help line for region authors, showing the chosen door's region and the keys.
- Package preview, with the icon, README and a suggested next version.

## Open: the Prop Browser stall

The browser sometimes stalls for over a minute, showing "Hold on" and "Inspector.Tick". The cause isn't known yet.

- **Measured so far:** reading two rips takes about 7 s, finding files in them about 5 s, and all imports in one session about 20 s.
- **Watcher result:** a read-only watcher recorded Unity for 45 minutes (13:58 to 14:43) and caught no stall over a minute, and no "Inspector.Tick". The longest wait was about 30 seconds at 14:27, showing "Hold on" and "Application.ExecuteMenuItem", meaning a Construction Kit or other menu command was running. Unity used about one processor core and read about 250 MB, with no shader compiling. I don't know which command it was. If you remember what you clicked around then, it would narrow this down.
- **Small speed-ups to make anyway:**
  - Remember what was read from each rip, so it isn't re-read after a code reload.
  - Skip the file search when no material needs updating.
  - Group imports together.
- **Textures setting:** the dropdown showed **Detailed** when first opened in this test, though the code's default is Fast. The setting is remembered per PC, so something set it to Detailed earlier. The likeliest cause is the Unity test suite, which switches to Detailed partway through and can't switch back if it's stopped at that point. Today's cancelled run probably isn't to blame, because no project assets had changed when I checked right after it, so it may be an earlier run. Detailed could add to the stall, because it compresses textures, which is slow. Action: make sure a new user really starts on Fast, and have the test suite put the setting back even after being stopped partway. Exporting raises shipped textures to full quality automatically, and that compression could make exports slow.
