# Outward Construction Kit

Adds new regions to Outward: Definitive Edition. Walk through a door, get a real loading screen, and arrive somewhere new, built from the game's own props, enemies, water and terrain textures.

**This is a beta.** This mod does nothing by itself: it loads region mods, and lets you make your own.

## For players

Install a region mod with a mod manager such as r2modman, and this comes with it. The first launch after installing a region takes a little longer, while the kit prepares what that region uses from your own copy of the game. It keeps the results in `BepInEx/OCK_Cache`, which is always safe to delete.

If you uninstall a region while your character is inside it, you'll arrive back where you entered it, as long as the kit stays installed. Don't uninstall the kit itself while inside a region.

## For modders

Regions are made in Unity 2020.3.26f1, with the free starter project on [GitHub](https://github.com/Robin-Rounthwaite/Outward-Construction-Kit). You place vanilla props and enemies, sculpt and paint ground, add water, and export. Anything you only move, rotate or evenly scale stays a reference to the player's own game files, so no art ships in your mod.

## Current limitations

- Co-op hasn't been tested yet.
- Only Outward: Definitive Edition is supported.
- Props are decoration for now: chests and gathering spots don't work.
- Dying in a region always returns you to where you entered it. There are no defeat scenarios yet.
- A game update can stop a region's references from matching, which the log reports.

## Reporting problems

Please open an issue on [GitHub](https://github.com/Robin-Rounthwaite/Outward-Construction-Kit/issues), with your `BepInEx/LogOutput.log` attached.

## Use of AI

Claude Code was used in the making of this mod.
