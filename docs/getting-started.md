# Getting started

This takes you from nothing to a Unity project ready to make regions. Most of the time goes on downloads.

## 1. Install Unity 2020.3.26f1

The version must match the game's exactly, because bundles built with any other can fail to load.

1. Install [Unity Hub](https://unity.com/download).
2. Open the Hub, sign in, and add a licence under **Preferences > Licenses**. The free Personal licence is fine if it fits you. Unity won't open a project without one.
3. Paste this link into your browser, which opens the install in the Hub: `unityhub://2020.3.26f1/7298b473bc1a`. You don't need any extra modules.

## 2. Install the mod

Using r2modman or the Thunderstore app, install Outward_Construction_Kit into a profile for Outward: Definitive Edition, and launch the game from that profile once, to check it starts. This is the profile you'll test your regions in.

## 3. Open the starter project

1. Unzip the starter project to a folder of its own with a short path, such as `D:\OutwardRegions`. In testing, a copy under a very long path sat idle after opening. Keep it off OneDrive and similar services too: they lock Unity's working files, which breaks exports, and your rips will hold game art that shouldn't sync anywhere.
2. In the Hub, click **Open** (**Add** on some versions), and choose the `OutwardRegionProject` folder. The first opening takes a few minutes.
3. The **Construction Kit Paths** window appears. It suggests the game folder and each mod profile it can find on your PC, looking without changing anything. Check them, pick the profile from step 2, and click **Confirm**. You can reopen it from **Construction Kit > Paths**.

If the Hub says the licence can't be validated, sign out of the Hub and back in, and check **Preferences > Licenses** again.

The project's settings matter, so leave them as they are: Color Space has to be Linear like the game's, because Unity compiles that into every shader a bundle carries, and shaders keep only the variants the game's scenes use, which keeps exports quick.

## 4. Rip the scenes you want props from

The Prop Browser shows models extracted from your own copy of the game, using [AssetRipper](https://github.com/AssetRipper/AssetRipper). Export each scene into its own folder under one rip folder, then set that folder in the Prop Browser. A scene takes a while and several gigabytes. An automated way to do this is planned.

## 5. Make a region

Continue with [Making a region](making-a-region.md).
