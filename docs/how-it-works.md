# How it works

## Loading a region

The door is the game's own `InteractionSwitchArea`, so travel uses the real level loader and loading screen. A new scene needs a few things vanilla scenes carry themselves. `SpawnPointManager`, `AISquadManager` and `AISceneManager` are added by the plugin (the squad manager takes its enemy wake-up distances from the enemies' donor scenes, without which they never move), and `EnvironmentConditions` with the sky, a camera named exactly `Main Camera`, and the audio manager are borrowed from a vanilla scene.

## The asset cache

A region names vanilla objects by scene and path, and nothing of the game's ships with it. On each player's PC, the plugin builds what installed regions name from that player's own game files, once, as they launch the game. `AssetCache` keeps the results in `BepInEx/OCK_Cache`, named for the game build and the extractor, so an update to either starts afresh and removes the old one. Deleting the folder is always safe.

`BundleBuilder` copies an object's scene data byte for byte into a small asset bundle. Its art stays where it is: the bundle's materials and textures are pointers into the player's `sharedassets` files, and only meshes combined by the level's static batching are copied, because they live in the level itself. So five test regions take about 26 MB and 13 seconds to build, and loading a region's objects costs no more than the objects themselves. Everything loads on entering a region and is released on leaving.

It works on objects (props and enemies, by path), a scene's main water plane and first water source (found by their scripts), its sky, camera and audio rigs (with the scene's terrain and props left out), ground textures (by name) and shaders (by name, the first time one is needed and isn't in memory).

`src/OCK.Extractor/template/ock_template` is a bundle built by Unity, which supplies the descriptions of Unity's built-in classes. A class it lacks makes the extractor refuse that object. To add one, add it to `tools/template-maker` and run `tools/make-template.ps1`.

### Donor scenes

Anything that can't be cached falls back to its scene, and the log says why. `SceneExtractor` loads each such donor scene additively once per session, behind the first loading screen while the game sits in its empty transition scene, switches it off before any `Start` runs, clones what's needed under an inactive parent, and unloads the scene. It logs load times and memory. A donor scene costs about 750 MB for the session, so this should be rare. The `UseCache` setting turns the cache off, which is useful for comparing the two.

## Props

A prop reference names the whole prop, so its LODs and colliders come with it. Most props are static-batched, sharing an unreadable combined mesh with their neighbours. The plugin moves and evenly scales those with a batch root rather than through their own transform, and gives each its own material copies so Unity can't draw repeats at one position.

Embedded props use exact copies of the vanilla materials. Their placeholder shaders have the real shaders' names, so `ShaderSwap` puts the real ones back at load, finding them in memory or else through the cache. Only the most detailed mesh is embedded, so embedded props have no LODs yet. Editing a library material itself isn't detected as a change, so make any material changes on an instance.

References carry a check, and the manifest records a fingerprint of the game build, so a game update produces a clear error or warning rather than a wrong object.

## Dev settings

These are under `[Dev]` in the plugin's config. All but `UseCache` are off by default.

| Setting | What it does |
|---|---|
| `UseCache` | Builds and uses the asset cache. Off, everything comes from donor scenes. |
| `SelfCheck` | Logs `[CHECK]` lines after entering a region. See [Testing](testing.md). |
| `MemoryReport` | Logs what's holding on to assets kept from donor scenes. Adds many seconds to the first load, and inflates the memory it measures. |
| `ProbeScenes` | Writes what each loaded scene contains to a `probes` folder beside the plugin. |
