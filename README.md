# TILER2

A library mod for Risk of Rain 2. Built with BepInEx and R2API.

Contains a bunch of different utilities for my other mods, mostly having to do with items.

## Installation

Release builds are published to Thunderstore: https://thunderstore.io/package/ThinkInvis/TILER2/

**Use of a mod manager is recommended**. If not using a mod manager: extract ThinkInvis-TILER2-[version].zip into your BepInEx plugins folder such that the following path exists: `[RoR2 game folder]/BepInEx/Plugins/ThinkInvis-TILER2-[version]/TILER2.dll`.

## Building

Building TILER2 locally will require setup of the postbuild event:
- The middle 3 xcopy calls need to either be updated with the path to your copy of RoR2, or removed entirely if you don't want copies of the mod moved for testing.
- Installation of Weaver (postbuild variant) is left as an exercise for the user. https://github.com/risk-of-thunder/R2Wiki/wiki/Networking-with-Weaver:-The-Unity-Way