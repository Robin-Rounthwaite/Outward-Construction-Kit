# Getting started

This takes you from nothing to a Unity project ready to make regions, on Windows. Most of the time goes on downloads.

Once you've unzipped the starter project in step 3, you can double-click `Check My Setup.cmd` in it at any point. It looks at this PC without changing anything, and says what's still missing.

## 1. Install Unity 2020.3.26f1

The version must match the game's exactly, because bundles built with any other can fail to load.

1. Install [Unity Hub](https://unity.com/download).
2. Open the Hub, sign in, and add a licence under **Preferences > Licenses**. The free Personal licence is fine if it fits you. Unity won't open a project without one.
3. Paste this link into your browser, which opens the install in the Hub: `unityhub://2020.3.26f1/7298b473bc1a`. You don't need any extra modules.

The Hub may warn that this version of Unity is dangerous. That warning doesn't apply here, because we're not making executables, and Outward already has its own patch.

## 2. Install the mod

1. Install [r2modman](https://thunderstore.io/package/ebkr/r2modman/), choose Outward: Definitive Edition, and create a profile. This is the profile you'll test your regions in.
2. In that profile, install [Outward_Construction_Kit](https://thunderstore.io/c/outward/p/Blue_Chamber_Collective/Outward_Construction_Kit/) from the **Online** tab.
3. Click **Start modded** once, to check the game starts. Always start it this way, or with the kit's **Launch Game** button later: Steam's own Play button starts the game without mods.

## 3. Open the starter project

1. Download the starter project and unzip it to a folder of its own with a short path, such as `D:\OutwardRegions`. In testing, a copy under a very long path sat idle after opening. Keep it off OneDrive and similar services too: they lock Unity's working files, which breaks exports.
2. In the Hub, click **Open** (**Add** on some versions), and choose the `OutwardRegionProject` folder. The first opening takes a few minutes.
3. The **Construction Kit Paths** window appears. It suggests the game folder and each mod profile it can find on your PC, looking without changing anything. Check them, and pick the profile from step 2.
4. For **Rip folder**, choose or make a folder on a drive with plenty of space, and off OneDrive. Rips hold game art that shouldn't sync anywhere. It doesn't have to be empty: each rip gets a new folder of its own, nothing already there is overwritten, and earlier rips are picked up.
5. Click **Confirm**. You can reopen this window from **Construction Kit > Paths**.

The **First Steps** window then opens, and takes you through the rest. You can reopen it from **Construction Kit > First Steps**.

### If Unity closes by itself

If Unity closes again after a few seconds, with "No valid Unity Editor license found" in its log, you probably have the Microsoft Store version of the Hub. It keeps your licence in a private folder that Unity 2020 doesn't look in.

1. Close Unity, and double-click `Repair Unity Licence.cmd` in the project folder from File Explorer.
2. Open the project from the Hub again. If it closes once more, try a second time: this happened in testing.

The Store Hub only renews its private copy, which Unity Personal needs about every 30 days, so the repaired copy goes out of date. If the error comes back, open the Hub for a moment, then run the repair again.

### Project settings

Leave the project's settings as they are. Color Space has to be Linear like the game's, because Unity compiles that into every shader a bundle carries, and shaders keep only the variants the game's scenes use, which keeps exports quick.

## 4. Choose what to rip

The Prop Browser shows models extracted ("ripped") from your own copy of the game.

If you want to start small, try Monsoon, which has everything the tutorial needs, including its ground textures. Alternatively, you can rip all scenes: all 79 of the game's scenes except two empty ones, so every prop and enemy. You can rip more scenes at any time.

The time and disk space ripping takes are not yet measured, so use a drive with plenty of room. Each rip exports much the same shared game files, so the kit keeps one copy of what's identical. Windows Explorer still shows each rip at its full size, because the files appear in each. Ripping all scenes stops before the drive fills up, with regions and towns done first, and carries on from where it stopped if you run it again.

To rip, use **First Steps**, or **Construction Kit > Rip a Scene**, which has a **Rip** button beside each scene and a **Rip All Scenes** button. The first time, this downloads [AssetRipper](https://github.com/AssetRipper/AssetRipper) from GitHub, about 42 MB, into your rip folder. Ripping runs in its own window, so you can keep working. When it finishes, it appears in the Prop Browser by itself. If you'd rather use an AssetRipper you already have, set its folder in **Construction Kit > Paths**; version 2.0.0 is the tested one.

## 5. Make a region

Continue with [Your first region](tutorial.md).
