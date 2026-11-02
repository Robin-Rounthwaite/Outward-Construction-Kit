# Outward Construction Kit

A toolkit for adding new regions to Outward: Definitive Edition, built from the game's own assets and authored in Unity. Exported regions contain references to vanilla assets, not copies of them.

**Status: in development, not yet released.** Windows only.

## Start here

- **To play a region:** see the [mod's page](thunderstore/README.md). You don't need anything from this repository.
- **To make a region:** follow [Getting started](docs/getting-started.md), then [Your first region](docs/tutorial.md).
- **To work on the kit itself:** see [Working on the kit](#working-on-the-kit).

**Tell us where the guides lose you.** If you ever feel stuck, or even briefly confused, let Cat know in the Catalysten-Workshop, so they can be made clearer.

## Features

**Building in Unity**
- Rip any of the game's scenes from your own copy, from inside Unity, one at a time or all together.
- Browse what you've ripped in the Prop Browser, by tab (Enemies, Water, Ground, Props and Items), location, category and name.
- Add things with a click, or drag them into the scene.
- Sculpt terrain, and paint it with the game's ground textures.
- Place any of the game's items from its full list, which players can pick up in the game.
- Add water that looks like any ripped location's, and set what type it is: Clean, Fresh, Salt, Rancid, Magic, Pure or Healing.
- Place the game's enemies, which fight as they do in the game.
- Move, rotate and evenly scale props, and alter them further if you need to.
- Choose the region's sky and audio from any ripped location.
- Add return doors, and use any prop as your doors' model.

**In the game**
- Regions load through a door and a loading screen, like the game's own areas.
- Doors and interaction prompts come from the game itself, and the door's location is set from inside the game.
- Water can be drunk from and collected, where the location it's copied from has a water source.
- A defeat in a region shows a message, and returns you to where you came from.
- Removing a region is safe: a save left inside it moves to its entrance or to Cierzo.
- A region that can't load, or a broken one, says so and doesn't stop the game.

**Sharing**
- Package a region as a Thunderstore mod, with the kit as its dependency.
- Unaltered props, enemies, water and ground ship as references to the game's assets, so mods stay small and contain no game art.
- Everything needed from the game is read from each player's own copy, and kept in a cache.

**Help along the way**
- A starter project and a First Steps window, a setup check, and a guide from installing Unity to your first region.
- A Create Example Region command.
- Test Region In-Game and Launch Game buttons, with a choice of which character to test as.

## Planned features

Not promises, and in no fixed order.

- Talking NPCs, then quest givers, then merchants, with dialogue written by modders.
- Telling NPCs where to walk and when, for quests or daily schedules, with animations.
- Animation options for NPCs, plus sound effects and other special effects.
- New quests with item or skill rewards and flags, able to change who's hostile, in their own window.
- Setting up death scenarios for a region: spawn points, penalties and messages.
- Props that work, such as gathering spots and chests.
- Choosing per enemy or item whether it respawns, and checking how resets treat them.
- Opening the game's existing worldspaces in Unity for editing.
- Connected and multi-part regions.
- Patrols, squads and spawn areas for enemies, and items and loot.
- Environment settings in Unity: sky, weather, ambient audio and lighting.
- A region map and loading screen.
- Distant scenery at region edges, and grass and ground clutter.
- Baked lighting and performance tools, and lighting in Unity that matches the game.
- Previews in the Prop Browser, and more filters, including dungeons or exteriors only.
- Making a new test character from the character dropdown.
- Co-op testing, both splitscreen and online.

## Current limitations

- Co-op hasn't been tested yet.
- Props are decoration for now: chests and gathering spots don't work.
- No talking characters or quests yet.
- Dying in a region always returns you to where you entered it.
- A game update can stop a region's references from matching.

## What you need to make a region

- Outward: Definitive Edition, and [r2modman](https://thunderstore.io/package/ebkr/r2modman/)
- Unity 2020.3.26f1, which must match the game's version exactly
- Space for what you rip from the game: 15 GB to start with

The kit downloads [AssetRipper](https://github.com/AssetRipper/AssetRipper) by itself when it's first needed. AssetRipper is a separate project under the GPL, and isn't included with the kit.

## Guides

- [Getting started](docs/getting-started.md)
- [Your first region](docs/tutorial.md)
- [Making a region](docs/making-a-region.md)
- [How it works](docs/how-it-works.md)
- [Testing](docs/testing.md)

## Working on the kit

You'll also need the .NET SDK, to build the plugin.

| Folder | What it is |
|---|---|
| `src/OCK.Runtime` | BepInEx plugin that loads regions. Building it deploys it to the r2modman profile named in `Directory.Build.user.props` (copy it from the `.example` file). |
| `unity/com.outwardconstructionkit.editor` | Unity package with the Prop Browser and exporter. |
| `unity/RegionProjectTemplate` | Project settings for the starter project. For working on the kit, a copy of this linked to the package. |
| `unity/starter` | Files that go beside the project in the starter zip. |
| `dev` | Scripts for working on the kit: `make-starter.ps1` builds the starter project zip modders download, and `make-template.ps1` rebuilds the extractor's template. |
| `tests` | Automated tests. |
| `thunderstore` | The kit's mod page, and `pack.ps1`, which makes its zip. |
| `docs` | Guides, listed above. |

No game files are included here, and none should ever be committed. Everything that uses the game's art reads it from your own copy of the game.

## Licence

The kit's own code is under the [MIT License](LICENSE). That doesn't cover Outward or its art, which belong to their rights holders.
