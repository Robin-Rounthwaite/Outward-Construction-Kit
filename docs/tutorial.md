# Your first region

This builds a small region and walks into it in the game. It assumes you've finished [Getting started](getting-started.md) and ripped **Monsoon**, whose textures the ground uses. If you also rip **Chersonese**, you'll have hyenas to add.

Before starting, you can run **Construction Kit > Create Example Region** to see a finished one. Click **Test Region** to visit it. Its entrance is already placed, in Cierzo, near where you arrive from the first spawn point.

## 1. Make the scene

Run **Construction Kit > New Region Scene**. You get a walled room, a sun and a spawn point, which is already enough to load in the game. Open **Construction Kit > Prop Browser** and dock it somewhere handy.

In the browser, set **Region ID** to something short and unique, such as `yourname_firstregion`, and **Name** to what players will see. Don't change the ID after sharing a region, because saves depend on it.

## 2. Add ground

On the **Ground** tab, click **Add Ground**, which replaces the room with a flat 200 by 200 m terrain. Keep it selected, and in the Inspector's Terrain component choose the **Paint Terrain** tool:

- **Raise or Lower Terrain**, then drag in the Scene view to make a hill. Hold Shift to lower.
- **Smooth Height** softens the result.
- **Paint Texture** paints the four starting layers: grass, dirt, sand and rock. The **Ground** tab adds more of the game's textures.

Keep the middle fairly flat for now, because the spawn point is there.

## 3. Add water

Lower a hollow somewhere. On the **Water** tab, click **Add** beside a location, whose water yours will look like, and move the blue marker to just below the ground's level there. Scale it to fit. In the game it becomes real water you can drink from.

## 4. Add props

On the **Props** tab, pick a location and category, then click **Add** beside a prop, or drag its name into the Scene view. Move, rotate and scale it with Unity's usual tools. All of that stays a reference to the player's own game files. Stretching it unevenly, or changing it in other ways, makes export embed a copy of its art in your mod.

Props are decoration for now: chests and gathering spots won't work in the game.

## 5. Add an enemy

On the **Enemies** tab, add one. It stands where you put it and wakes when a player comes near. Start with something weak, because you'll be testing with your own character.

## 6. Add a return door

A door back appears beside the arrival point by itself. To choose its place, click **Add Return Door** and stand the marker on the ground, with its small box on the side players walk up to.

## 7. Check the spawn point

Select `SpawnPointManager/Spawn0` in the Hierarchy and make sure it sits just above the ground. Save the scene.

## 8. Export

Click **Export Region**. The message at the top of the browser's tools says what was exported and where. It should say no props were embedded, unless you meant them to be.

## 9. Visit it

Click **Test Region** in the browser, which exports your region again, starts the game with your mods, loads your character and takes you straight there. It's normal play from then on, so your character takes damage and saves as usual. Name the character to use in the box beside the button, or leave it empty for the first one the game lists.

To see the way in that players will use, click **Launch Game** instead, or start the game from the profile you chose with r2modman's **Start modded**. Then, in a town or anywhere else outdoors:

1. Press **Ctrl+F8** until your region is the one chosen. The screen shows its name.
2. Press **Ctrl+F10** to place its door in front of you.
3. Walk up and use the door. You get a loading screen, then your region.

If something's missing, look at the log. In r2modman, your profile's folder is under **Settings > Browse profile folder**, and the log is `BepInEx\LogOutput.log` inside it. Search it for your region's ID and for `[Error`. For a checklist of each region in the log, as `[CHECK]` lines, set `SelfCheck = true` under `[Dev]` in `BepInEx\config\catalysten.outwardconstructionkit.cfg` in the same folder.

## 10. Share it

Back in Unity, click **Use door placed in game**, so your entrance ships with the mod, then follow [Sharing your region](making-a-region.md#sharing-your-region).

[Making a region](making-a-region.md) goes into more detail on each of these.
