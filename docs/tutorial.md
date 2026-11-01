# Your first region

This builds a small region and walks into it in the game. It assumes you've finished [Getting started](getting-started.md) and ripped **Monsoon**, whose textures the ground uses. If you also rip **ChersoneseNewTerrain**, you'll have hyenas to add.

Before starting, you can run **Construction Kit > Create Example Region** to see a finished one. Export it and visit it the same way as in steps 8 and 9.

## 1. Make the scene

Run **Construction Kit > New Region Scene**. You get a walled room, a sun and a spawn point, which is already enough to load in the game. Open **Construction Kit > Prop Browser** and dock it somewhere handy.

In the browser, set **Region ID** to something short and unique, such as `yourname_firstregion`, and **Name** to what players will see. Don't change the ID after sharing a region, because saves depend on it.

## 2. Add ground

Click **Add Ground**, which replaces the room with a flat 200 by 200 m terrain. Keep it selected, and in the Inspector's Terrain component choose the **Paint Terrain** tool:

- **Raise or Lower Terrain**, then drag in the Scene view to make a hill. Hold Shift to lower.
- **Smooth Height** softens the result.
- **Paint Texture** paints the four layers: grass, dirt, sand and rock.

Keep the middle fairly flat for now, because the spawn point is there.

## 3. Add water

Lower a hollow somewhere, click **Add Water**, and move the blue marker to just below the ground's level there. Scale it to fit. In the game it becomes real water you can drink from.

## 4. Add props

Pick a scene and category in the browser, then click **Add** beside a prop, or drag its name into the Scene view. Move, rotate and scale it with Unity's usual tools. All of that stays a reference to the player's own game files. Stretching it unevenly, or changing it in other ways, makes export embed a copy of its art in your mod.

Props are decoration for now: chests and gathering spots won't work in the game.

## 5. Add an enemy

Choose the **Enemies** category and add one. It stands where you put it and wakes when a player comes near. Start with something weak, because you'll be testing with your own character.

## 6. Add a return door

A door back appears beside the arrival point by itself. To choose its place, click **Add Return Door** and stand the marker on the ground, with its small box on the side players walk up to.

## 7. Check the spawn point

Select `SpawnPointManager/Spawn0` in the Hierarchy and make sure it sits just above the ground. Save the scene.

## 8. Export

Click **Export Region**. The message at the top of the browser's tools says what was exported and where. It should say no props were embedded, unless you meant them to be.

## 9. Visit it

Launch the game from the profile you chose. Then, in a town or anywhere else outdoors:

1. Press **Ctrl+F8** until your region is the one chosen.
2. Press **Ctrl+F10** to place its door in front of you.
3. Walk up and use the door. You get a loading screen, then your region.

If something's missing, search `BepInEx\LogOutput.log` for your region's ID and for `[Error`. Setting `SelfCheck = true` under `[Dev]` in the kit's config also logs a checklist for each region, as `[CHECK]` lines.

## 10. Share it

Back in Unity, click **Use door placed in game**, so your entrance ships with the mod, then follow [Sharing your region](making-a-region.md#sharing-your-region).

[Making a region](making-a-region.md) goes into more detail on each of these.
