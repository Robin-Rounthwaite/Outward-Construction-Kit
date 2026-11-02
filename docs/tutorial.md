# Your first region

This builds a small region and walks into it in the game. It assumes you've finished [Getting started](getting-started.md) and ripped **Monsoon**, whose textures the ground uses. If you also rip **Chersonese**, you'll have hyenas to add.

**Tell us where this guide loses you.** If you ever feel stuck, or even briefly confused, let Cat know in the Catalysten-Workshop, so it can be made clearer.

Before starting, you can run **Construction Kit > Create Example Region** to see a finished one. Click **Test Region In-Game** to visit it. Its entrance is already placed, in Cierzo, near where you arrive from the first spawn point.

## 1. Make the scene

Run **Construction Kit > New Region Scene**, and click **Create**. If a scene is open, it stays in your project. You get a sun and a spawn point, and the **Add Ground** window opens for the next step. Leave it open for now, then open **Construction Kit > Prop Browser** and dock it somewhere handy.

In the browser, set **Region ID** to something short and unique, such as `yourname_firstregion`, and **Name** to what players will see. Don't change the ID after sharing a region, because saves depend on it.

## 2. Add ground

In the **Add Ground** window, choose what ground to start with and how big it should be, and click **Add Ground**. If you closed that window, reopen it from the **Ground** tab of the browser. You now have a flat terrain, and a region can't be exported until it has something under its spawn point. Keep the terrain selected, and in the Inspector's Terrain component click the paintbrush button:

- **Raise or Lower Terrain**, then drag in the Scene view to make a hill. Hold Shift to lower.
- **Smooth Height** softens the result.
- **Paint Texture** paints the four starting layers: grass, dirt, sand and rock.

To paint with more of the game's textures, click **Add** beside one on the **Ground** tab. That selects your ground, turns on **Paint Texture** and chooses the new texture, ready to paint. A texture already added has a **Paint** button instead.

Keep the middle fairly flat for now, because the spawn point is there.

**To get back to these tools later,** such as after adding water, click **Ground** in the Hierarchy, or **Select Ground** on the **Ground** tab. You don't need to add ground again.

## 3. Lower a hollow for water

Water needs somewhere to sit. With **Raise or Lower Terrain**, hold Shift and drag in the Scene view to lower a hollow.

## 4. Add water

On the **Water** tab, choose a **Water type**, then click **Add** beside the location whose water yours should look like. Move the blue marker into your hollow, just below the ground's level there, and scale it to fit. To change a water's type later, select it and use the same dropdown. In the game it becomes real water you can drink from and collect.

## 5. Add props

On the **Props** tab, pick a location and category, then click **Add** beside a prop, or drag its name into the Scene view. Move, rotate and scale it with Unity's usual tools. All of that stays a reference to the player's own game files. Stretching it unevenly, or changing it in other ways, makes export embed a copy of its art in your mod.

Props are decoration for now: chests and gathering spots won't work in the game.

## 6. Add an item

The **Items** tab lists the game's items, once the game has run with the kit at least once. Click **Add** beside one, and move the orange marker to where it should lie. In the game it's the real item, which players can pick up.

## 7. Add an enemy

On the **Enemies** tab, add one. It stands where you put it and wakes when a player comes near. Start with something weak, because you'll be testing with your own character.

## 8. Add a return door

A door back appears beside the arrival point by itself. To choose its place, click **Add Return Door** and stand the marker on the ground, with its small box on the side players walk up to.

To change how doors look, select a prop in the scene and click **Use Selected Prop as Door Model**. That only sets the look, of both the entrance and return doors. It doesn't add a door. Return door markers then show the model.

If you lose a door, **Find Return Door** selects it and shows it in the Scene view. **Stand on Ground** moves the selected door onto the ground, if it's buried or floating.

## 9. Check the spawn point

Select `SpawnPointManager/Spawn0` in the Hierarchy and make sure it sits just above the ground. Save the scene.

## 10. Export

Click **Export Region**. A short message at the top of the browser says it's done, with anything that needs your attention. Open **Details** under it to see what was exported and where. It should say no props were embedded, unless you meant them to be.

## 11. Visit it

Click **Test Region In-Game** in the browser, which exports your region again, starts the game with your mods, loads your character and takes you straight there. It's normal play from then on, so your character takes damage and saves as usual. Choose the character in **Test as (Character Name)**. A character kept just for testing is safest. **Testing in mod list** shows which r2modman profile it uses, and you can change that in **Construction Kit > Paths**. The kit has to be installed in that profile.

To see the way in that players will use, click **Launch Game** instead, or start the game from the profile you chose with r2modman's **Start modded**. Then, in a town or anywhere else outdoors:

1. Press **Ctrl+F8** until your region is the one chosen. The screen shows its name. This chooses a region, not what its door looks like.
2. Press **Ctrl+F10** to place its door in front of you. Check the name first, because this places a door for whichever region is chosen, wherever you're standing.
3. Walk up and use the door. You get a loading screen, then your region.

Your next export picks up the door you placed, and the export message says so, so it ships with your mod. It uses the last door placed for this region, so place it again if you moved it by accident.

If something's missing, look at the log. In r2modman, your profile's folder is under **Settings > Browse profile folder**, and the log is `BepInEx\LogOutput.log` inside it. Search it for your region's ID and for `[Error`. For a checklist of each region in the log, as `[CHECK]` lines, set `SelfCheck = true` under `[Dev]` in `BepInEx\config\catalysten.outwardconstructionkit.cfg` in the same folder.

## 12. Share it

Follow [Sharing your region](making-a-region.md#sharing-your-region).

[Making a region](making-a-region.md) goes into more detail on each of these.
