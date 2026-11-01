# Outward Construction Kit

A toolkit for adding new regions to Outward: Definitive Edition, built from the game's own assets and authored in Unity. Exported regions contain references to vanilla assets, not copies of them.

**Status: in development, not yet released.** Windows only.

## Start here

- **To play a region:** see the [mod's page](thunderstore/README.md). You don't need anything from this repository.
- **To make a region:** follow [Getting started](docs/getting-started.md), then [Your first region](docs/tutorial.md).
- **To work on the kit itself:** see [Working on the kit](#working-on-the-kit).

## Current limitations

- Co-op hasn't been tested yet.
- Props and items are decoration for now: chests, gathering spots and pick-ups don't work.
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
