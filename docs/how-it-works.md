# How it works

## Loading a region

The door is the game's own `InteractionSwitchArea`, so travel uses the real level loader and loading screen. A new scene needs a few things vanilla scenes carry themselves. `SpawnPointManager`, `AISquadManager` and `AISceneManager` are added by the plugin (the squad manager takes its enemy wake-up distances from the enemies' donor scenes, without which they never move), and `EnvironmentConditions` with the sky, a camera named exactly `Main Camera`, and the audio manager are borrowed from a vanilla scene.

Borrowing happens once per session, behind the first loading screen. `SceneExtractor` loads each donor scene additively while the game sits in its empty transition scene, switches it off before any `Start` runs, clones what's listed in the manifest under an inactive parent, and unloads the scene. A run logs load times and memory. Each donor scene costs about 750 MB for the session, which is why this is due to be replaced by a cache of extracted assets.

## Props

A prop reference names the whole prop, so its LODs and colliders come with it. Most props are static-batched, sharing an unreadable combined mesh with their neighbours. The plugin moves those with a batch root rather than through their own transform, and gives each its own material copies so Unity can't draw repeats at one position.

Embedded props use exact copies of the vanilla materials. Their placeholder shaders have the real shaders' names, so `ShaderSwap` puts the real ones back at load, using shaders collected from donor scenes. Only the most detailed mesh is embedded, so embedded props have no LODs yet. Editing a library material itself isn't detected as a change, so make any material changes on an instance.

References carry a check, and the manifest records a fingerprint of the game build, so a game update produces a clear error or warning rather than a wrong object.

## Dev settings

These are under `[Dev]` in the plugin's config, and all are off by default.

| Setting | What it does |
|---|---|
| `SelfCheck` | Logs `[CHECK]` lines after entering a region. See [Testing](testing.md). |
| `MemoryReport` | Logs what's holding on to assets kept from donor scenes. Adds many seconds to the first load, and inflates the memory it measures. |
| `ProbeScenes` | Writes what each loaded scene contains to a `probes` folder beside the plugin. |
