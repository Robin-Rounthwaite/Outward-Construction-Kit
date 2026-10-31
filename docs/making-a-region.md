# Making a region

Keep the Unity project and your rips off OneDrive. It locks Unity's working files, which breaks bundle builds, and rips hold game art that shouldn't sync anywhere.

1. Copy `unity/RegionProjectTemplate` somewhere off OneDrive, and in its `Packages/manifest.json` point the `com.outwardconstructionkit.editor` entry at this repository's package, as `file:<full path>/unity/com.outwardconstructionkit.editor`. Open the copy in Unity 2020.3.26f1. The version must match the game's exactly, and the template's settings matter: Color Space must be Linear like the game's, because Unity compiles that into every shader a bundle carries, and shaders keep only the variants the game's scenes use (exponential squared fog and no lightmaps), which keeps exports quick.
2. Run **Construction Kit > New Region Scene**, which makes a walled room with a sun and a spawn point.
3. Open **Construction Kit > Prop Browser**. Click **Add** on a prop, or drag its name into the Scene view. The first use of a prop copies its model from the rip.
4. Move, rotate and alter props as you like. Export decides for each one: props you only moved or rotated become references to the game's own, and props with any other change are embedded in the mod.
5. Set the region's ID and name in the Prop Browser. Don't change the ID after sharing a region, because saves and door placements depend on it.
6. Click **Export Region**, which writes `<id>` and `<id>.refs.txt` to their own mod folder, `BepInEx\plugins\OCK-Region-<id>`. Embedded props contain Outward's art, so check the rights holder's and the mod site's rules before sharing such a mod.
7. Launch the game. **Ctrl+F8** chooses a region, and **Ctrl+F10** places its door in front of you.

A region needs a root object named exactly `SpawnPointManager`, with at least one child as a spawn point.

Any number of regions can be installed: the plugin finds every `*.refs.txt` under `BepInEx\plugins` with a bundle beside it of the same name. Each needs a unique ID, scene name and bundle build name, and the exporter derives all three from the ID.

## How props look in the editor

Props are stand-ins on a simple shader of our own, because ripped shaders are only placeholders. Some may look different from the game (the triple-ladies statue renders blue, and some tree leaves show as dark cards), but the game always uses its real assets. The Prop Browser's **Textures** setting picks **Fast** (the default, with smaller uncompressed textures for quicker imports) or **Detailed** (full resolution plus the materials' surface detail maps). Neither can show the game's real shaders or lighting, so the game still looks much better than either. Embedded props always ship at full quality, so exporting them takes longer in Fast mode.

## Enemies and the entrance door

Export bakes a NavMesh from the scene and its stand-ins, so enemies can walk the region. To place an enemy, add it from the **Enemies** category: it shows as a red capsule, with a small box marking its facing. Enemies are always references, cloned from a donor scene and given their own identity.

Your entrance door ships with the region too: after placing it in game with **Ctrl+F10**, click **Use door placed in game** in the Prop Browser, then export. A door a player places themselves overrides the shipped one.

## Ground and water

**Add Ground** in the Prop Browser (or the Construction Kit menu) replaces the starter room with a flat 200 by 200 m terrain at height 0, with invisible walls around its edge. It's Unity's own Terrain, so select it and use the Inspector's Paint Terrain tool: **Raise or Lower Terrain** and **Smooth Height** make hills and hollows, and **Paint Texture** paints the four layers: grass, dirt, sand and rock. Move the spawn point and props to sit on the shapes you make. Heights and paint ship in the mod, and vanilla textures are references, so they add almost nothing to it. A layer using your own texture ships that texture.

It starts with room for 10 m below 0 and 50 m above. For more, raise **Terrain Height** in the Terrain's settings, which stretches existing hills in proportion, or lower the object and add the same amount to the height. Resize `Ground_Edges` to match, because the invisible walls don't follow.

**Add Water** adds a blue marker for the scene chosen in the browser, whose own water it will use in game. Move it to the water's height and scale it to size. It only looks like water in game. Standing in or over it lets players drink and fill waterskins, with the water type of the donor scene's first water source, and shallow water (about 1.2 m or less) splashes underfoot. Enemies walk on the ground beneath it, since the NavMesh ignores water, and it has no collision.

## Return doors

A door back to where players entered appears beside the arrival point automatically. **Add Return Door** places it yourself: stand the marker on the ground with its small box on the side players walk up to, and rotate it to suit. With one or more markers, the automatic door is left out. Return doors stay plain slabs for now, and all of them lead to the same place: where the region's entrance door is.

## Ripping more scenes

AssetRipper's free edition doesn't separate static-batched meshes, and the importer here does that itself. Run it with `--headless --port <port>`, then POST to `/LoadFile` with the scene's `levelN` and `sharedassetsN.assets`, followed by `/Export/UnityProject`. **Headless export deletes a non-empty target folder, so always give it a new one**, inside the rip folder: the Prop Browser reads every `ExportedProject` under it.

A scene's number is its position in the build list, so Monsoon is 52 and ChersoneseNewTerrain is 5. Monsoon came to 15 GB in about 18 minutes and Chersonese to 7.3 GB in about 8, because a scene pulls in much of the shared game data. Each rip gives the same asset a different GUID, so a texture used from two rips is imported twice in the editor. That only costs editor disk space.
