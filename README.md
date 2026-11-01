# Outward Construction Kit

A toolkit for adding new regions to Outward: Definitive Edition, built from the game's own assets and authored in Unity. Exported regions contain references to vanilla assets, not copies of them.

**Status: in development, not yet released.**

## What's here

| Folder | What it is |
|---|---|
| `src/OCK.Runtime` | BepInEx plugin that loads regions. Building it deploys it to the r2modman profile named in `Directory.Build.user.props` (copy it from the `.example` file). |
| `unity/com.outwardconstructionkit.editor` | Unity package with the Prop Browser and exporter. |
| `unity/RegionProjectTemplate` | Project settings for the starter project. For working on the kit, a copy of this linked to the package. |
| `dev` | Scripts for working on the kit: `make-starter.ps1` builds the starter project zip modders download, and `make-template.ps1` rebuilds the extractor's template. |
| `tests` | Automated tests. |
| `docs` | Guides, listed below. |

## Guides

- [Getting started](docs/getting-started.md)
- [Making a region](docs/making-a-region.md)
- [How it works](docs/how-it-works.md)
- [Testing](docs/testing.md)

## Requirements

- Outward: Definitive Edition, with BepInEx
- Unity 2020.3.26f1, which must match the game's version exactly
- [AssetRipper](https://github.com/AssetRipper/AssetRipper), to extract models for display in the editor
- The .NET SDK, to build the plugin

No game files are included here, and none should ever be committed. Everything that uses the game's art reads it from your own copy of the game.

## Licence

The kit's own code is under the [MIT License](LICENSE). That doesn't cover Outward or its art, which belong to their rights holders.
