# Making a region

This goes into more detail on what [Your first region](tutorial.md) walks through. It assumes you've finished [Getting started](getting-started.md).

## The Prop Browser

**Construction Kit > Prop Browser** has tabs for **Enemies**, **Water**, **Ground**, **Props** and **Items**. Each can be narrowed to one location, or show **All locations**, which reads every rip and so takes longer. Click **Add** beside something, or drag a prop's name into the Scene view. The first use of a prop copies its model from the rip.

Move, rotate, scale and alter props as you like. Export decides for each one: props you only moved, rotated or scaled evenly become references to the game's own, and props with any other change, including stretching, are embedded in the mod. Embedded props contain Outward's art, so check the rights holder's and the mod site's rules before sharing such a mod.

Don't change a region's ID after sharing it, because saves and door placements depend on it. **Export Region** writes it to its own mod folder in your profile, `BepInEx\plugins\OCK-Region-<id>`, and players can have any number of regions installed.

A scene made with **New Region Scene** has what it needs. If you start from your own, it needs a root object named exactly `SpawnPointManager`, with at least one child as a spawn point.

Props are decoration for now. Their scripts are left behind, so anything players can use in its own scene, such as a chest or gathering spot, is only for show in a region. The Prop Browser marks those **decoration**.

## Items

The **Items** tab lists the game's own items, which the game writes out for the kit when it starts, so start it once with the kit installed. The names are in the game's language. Search by name or item number, or filter by type, then click **Add** and move the orange marker to where the item should lie. It's only a marker in Unity, and in game it becomes the real item, which players can pick up.

Each item is placed once for a save. After a player takes it, it stays gone until the region resets, as the game's own areas do, when it comes back. An item you add later appears even for a character who's been there before, so you can keep testing with the same one. A prop taken from a rip that's really an item, such as a cooking pot kit, also becomes the real item on export, unless you've stretched it.

## How props look in the editor

What you see in the editor are stand-ins: copies of the props with a simple look of our own, because the game's real shaders don't survive ripping. They never ship in your mod. Some may look different from the game (the triple-ladies statue renders blue, and some tree leaves show as dark cards), but the game always uses its real assets. The Prop Browser's **Textures** setting picks **Fast** (the default, with smaller uncompressed textures for quicker imports) or **Detailed** (full resolution plus the materials' surface detail maps). Neither can show the game's real shaders or lighting, so the game still looks much better than either. Embedded props always ship at full quality, so exporting them takes longer in Fast mode.

## Sharing your region

Set your region's entrance, then click **Package** in the Prop Browser (or **Construction Kit > Package Region**). Fill in a mod name, version and short description, and click **Make Upload Zip**. It writes `Build/Upload/<name>-<version>.zip` in your project, ready to upload at [thunderstore.io](https://thunderstore.io) under the Outward community, and it depends on the kit, so a mod manager installs that too. Raise the version for each upload.

To try the zip first, use **Settings > Import local mod** in r2modman. Remove your own exported copy of the region from that profile first, because two copies share one ID.

## Enemies and the entrance door

Export works out where enemies can walk from the ground and props in your scene. Unity calls this a NavMesh. To place an enemy, add it from the **Enemies** tab: it shows as its real model in a default pose, or a red capsule if the model isn't in the rip. Enemies are always references, built from the player's own game files and given their own identity.

Your entrance door ships with the region too: after placing it in game with **Ctrl+F10**, click **Use Door Placed in Game** in the Prop Browser, then export. A door a player places themselves overrides the shipped one.

## Ground and water

**Add Ground** on the Prop Browser's **Ground** tab (or the Construction Kit menu) replaces the starter room with a flat 200 by 200 m terrain at height 0, with invisible walls around its edge. It's Unity's own Terrain, so select it and use the Inspector's Paint Terrain tool: **Raise or Lower Terrain** and **Smooth Height** make hills and hollows, and **Paint Texture** paints the four layers: grass, dirt, sand and rock. Move the spawn point and props to sit on the shapes you make. Heights and paint ship in the mod, and vanilla textures are references, so they add almost nothing to it. A layer using your own texture ships that texture.

**Paint Holes**, among the Terrain's paintbrush tools, cuts see-through, walk-through holes in the ground, such as for cave entrances. Whether holes survive export hasn't been tested yet.

To paint with more of the game's ground, click **Add** beside a texture on the **Ground** tab. The texture joins the Terrain's Paint Texture layers, and ships as a reference. More than four layers costs the game an extra drawing pass, so keep to what you need.

It starts with room for 10 m below 0 and 50 m above. For more, raise **Terrain Height** in the Terrain's settings, which stretches existing hills in proportion, or lower the object and add the same amount to the height. Resize `Ground_Edges` to match, because the invisible walls don't follow.

**Add** on the **Water** tab adds a blue marker for that location, whose own water it will use in game. A location marked **can't be drunk** has no water source to copy. Move it to the water's height and scale it to size. It only looks like water in game. Standing in or over it lets players drink and fill waterskins. It's clean water, unless you select the marker and choose another under **Water type** on the **Water** tab: fresh, salt, rancid, magic, pure or healing. Shallow water (about 1.2 m or less) splashes underfoot. Enemies walk on the ground beneath it, and it has no collision.

## Dying in a region

Players defeated in a region come back where they loaded into it from, alive, rather than beside what killed them. If they loaded a save made inside the region, that's where its entrance is.

## Return doors

A door back to where players entered appears beside the arrival point automatically. **Add Return Door** places it yourself: stand the marker on the ground with its small box on the side players walk up to, and rotate it to suit. With one or more markers, the automatic door is left out. All of them lead to the same place: where the region's entrance door is.

## How doors look

Entrance and return doors use a vanilla door by default. To use any prop instead, add it to your scene, turn it so its front faces along the blue axis, scale it evenly if you like, keep it selected, and click **Use Selected Prop as Door Model** in the Prop Browser. You can delete it from the scene afterwards. **Reset** goes back to the default. If a door's model can't be loaded in game, it's a plain blue slab, so players never lose their way out.

## Ripping more scenes

**Construction Kit > Rip a Scene** lists the game's scenes and rips one in its own window. **Rip All Scenes**, there and in the Prop Browser, rips every scene not yet ripped, regions and towns first. It says how much space and time to expect, stops before the drive fills up, and carries on from where it stopped if run again. Few modders need it. A scene pulls in much of the game's shared data: Monsoon came to 15 GB in about 18 minutes, Chersonese to 7.3 GB in about 8, and even a near-empty scene to 3.1 GB in 3. A new rip then links what's identical to your earlier rips, so it keeps one copy on disk: Chersonese added 1.7 GB after Monsoon, and a small dungeon 1.1 GB of its 7.6 GB. Deleting a rip is still safe, and Windows Explorer shows each at its full size. To do the same for rips made before this, run `Tools~/share-rip-files.ps1` with `-RipFolder` set to your rip folder.

The Prop Browser's list keeps up by itself: a finished rip appears, and **Refresh** reads only what's new or changed and drops what's gone from the folder. **Construction Kit > Read All Rips Again** forces a full read. Each rip gives the same asset a different ID, so the browser matches things by name: one entry per thing, with all its locations in the **All locations** view, and dungeons counted under their regions. A texture with the same name and size is only copied into your project once.

Start with Monsoon, which the ground textures need. Chersonese adds outdoor props and wild enemies such as hyenas. After that, rip the scene whose look you're after: a vanilla scene's rip offers what that scene uses.

The window runs `Tools~/rip-scene.ps1` in the package, which can also be run by hand. It installs AssetRipper 2.0.0 inside your rip folder and checks the download against its published hash. AssetRipper is a separate project under the GPL, and isn't included with the kit.
