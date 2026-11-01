# Testing

There are four layers, because no single tool can reach all of the kit.

| Layer | What it covers | How to run it |
|---|---|---|
| Plugin logic (`tests/OCK.Runtime.Tests`, NUnit) | Manifest reading, area numbers, number formats under other languages, and building cache bundles from the real game files. Reads the game's files but doesn't run the game. | `tests/run-tests.ps1`, in seconds |
| Unity suite (`OckSelfTest.cs`) | Rips, importing, and exporting real regions, including that no vanilla art reaches a reference-only bundle. Also has the plugin read what was exported. | `tests/run-tests.ps1 -Unity`, in a minute or two, or longer after a change of settings makes Unity reimport. Close Unity first. It opens a window, because batch mode needs a licence activated for it. Needs a Monsoon and a Chersonese rip. |
| In-game checks (`RegionChecks.cs`) | What only the running game can show: props placed, enemies able to move, ground textures, water and doors. | Set `SelfCheck = true` under `[Dev]` in the plugin's config, enter a region and wait about 8 seconds, then search `LogOutput.log` for `[CHECK]`. |
| Automated game run (`AutoTest.cs`, `tests/run-game-test.ps1`) | The same in-game checks with no one at the keyboard: starts the game through Steam, loads a character, travels to each region, checks it, takes a screenshot, travels back so the game saves, and quits. Fails if anything hangs, including saving. | `tests/run-game-test.ps1 -Regions "test_alpha,test_gamma"`, in about a minute a region. Takes over the game window. `-Stay` leaves the game open for a person, and `-Character` picks a character other than Jim of the Doors. |

The automated run makes its character invincible inside regions, because enemies that work kill a character standing still. It still saves, so it does change that character: use a character kept for testing. Saves are copied to `%TEMP%\ock-save-backup` first, replacing the previous copy. Enemies killed in earlier sessions stay dead in a save, and the checks say when that stops them checking movement.

Screenshots land in `BepInEx/OCK_AutoTest`. BepInEx's log can lose its last line as the game closes, so the game's own `output_log.txt`, in the game's folder, is the one to trust at the very end.

Nothing tests how a region looks, or whether interactions such as drinking really work when a player tries them, so those still need a person in game. Comparing a screenshot against a run with `UseCache = false` is a quick way to find a difference the checks can't see.

Unity's newer CLI can't drive the editor here: its Pipeline package refuses projects older than Unity 6, and this project has to stay on the game's version.

These tests can't run on a hosted build service as they stand, because they need the game's assemblies and rips, which must not be uploaded.
