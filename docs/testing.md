# Testing

There are three layers, because no single tool can reach all of the kit.

| Layer | What it covers | How to run it |
|---|---|---|
| Plugin logic (`tests/OCK.Runtime.Tests`, NUnit) | Manifest reading, area numbers, and number formats under other languages. Uses the game's assemblies but not the game. | `tests/run-tests.ps1`, in seconds |
| Unity suite (`OckSelfTest.cs`) | Rips, importing, and exporting real regions, including that no vanilla art reaches a reference-only bundle. Also has the plugin read what was exported. | `tests/run-tests.ps1 -Unity`, in a minute or two, or longer after a change of settings makes Unity reimport. Close Unity first. It opens a window, because batch mode needs a licence activated for it. Needs a Monsoon and a Chersonese rip. |
| In-game checks (`RegionChecks.cs`) | What only the running game can show: props placed, enemies able to move, ground textures, water and doors. | Set `SelfCheck = true` under `[Dev]` in the plugin's config, enter a region and wait about 8 seconds, then search `LogOutput.log` for `[CHECK]`. |

Nothing tests how a region looks, or whether interactions such as drinking really work when a player tries them, so those still need a person in game.

Unity's newer CLI can't drive the editor here: its Pipeline package refuses projects older than Unity 6, and this project has to stay on the game's version.

These tests can't run on a hosted build service as they stand, because they need the game's assemblies and rips, which must not be uploaded.
