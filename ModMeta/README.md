# TILER2

## SUPPORT DISCLAIMER

### Use of a mod manager is STRONGLY RECOMMENDED.

Seriously, use a mod manager.

If the versions of TILER2 (or possibly any other mods) are different between your game and other players' in multiplayer, things WILL break. If TILER2 is causing kicks for "unspecified reason", it's likely due to a mod version mismatch. Ensure that all players in a server, including the host and/or dedicated server, are using the same mod versions before reporting a bug.

**While reporting a bug, make sure to post a console log** (`path/to/RoR2/BepInEx/LogOutput.log`) from a run of the game where the bug happened; this often provides important information about why the bug is happening. If the bug is multiplayer-only, please try to include logs from both server and client.

## Description

TILER2 is a library mod. It won't do much on its own, but it may be required for some other mods.

### User-Facing Features

TILER2 mostly contains features that are useful for mod developers, but it also adds some things that normal users can take advantage of.

#### DebugUtil

The DebugUtil module adds the console commands `evo_setitem` and `t2_stat`.

- `evo_setitem itemIndexOrName count`: Sets the count of the target item in the Artifact of Evolution item pool. Marked as a cheat command.
- `t2_stat statType value`: Adds a value to one of the stats supported by the StatHooks module for all players. See source code for valid names (Modules/StatHooks.cs, any member of StatHookEventArgs).

#### NetConfig

The NetConfig module automatically syncs important config settings from the server to any connecting clients, and kicks clients with critical config mismatches which can't be resolved (i.e. settings that can't be changed while the game is running, or client has different mods than server).

NetConfig also adds the console commands `aic_get`, `aic_set`, `aic_settemp`, and `aic`; and the convar `bool aic_allowclientset`.

- `aic_get "path1" "optional path2" "optional path3"`: Attempts to find a config entry. Path matches, in order: mod name, config section, config key. If you weren't specific enough, it will print all matching paths to console; otherwise, it will print detailed information about one specific config entry.
- `aic_set "path1" "optional path2" "optional path3" value`: Attempts to permanently set a config entry (writes to config file AND changes the ingame value), following the same search rules as aic_get. Not usable by non-host players; will route to aic_settemp instead.
- `aic_settemp "path1" "optional path2" "optional path3" value`: Attempts to temporarily set a config entry until the end of the current run, following the same search rules as aic_get. Can be blocked from use by non-host players via aic_allowclientset.
- `aic "cmd" ...`: Routes to aic_get, aic_set, or aic_settemp (for when you forget the underscore).
- `bool aic_allowclientset`: If TRUE, any player on a server can use aic_settemp. If FALSE, only the host can use aic_settemp.

## Issues/TODO

- Items which players have but were disabled mid-run need a UI indicator for such.
- If a client gets kicked by R2API mod mismatch, NetConfig will attempt kick them again (to no effect) due to timeout.
- See the GitHub repo for more!

## Changelog

The 5 latest updates are listed below. For a full changelog, see: https://github.com/ThinkInvis/RoR2-TILER2/blob/master/changelog.md

**4.0.0**

- Compatibility changes for Risk of Rain 2 Anniversary Update.
- Removed deprecated content.
	- Item_V2, Equipment_V2, Artifact_V2 now alias to Item, Equipment, Artifact and these aliases will be removed in the next minor version.
	- AutoItemConfig has been removed.
- Attempted to fix WorldUnique items being added to drop pools.

**3.0.4**

- General stability patch for StatHooks.
	- IL patches should now be less fragile in general, and slightly less prone to breaking if another mod gets to modify RecalculateStats first.
	- Added a handful of new hook locations (baseShieldAdd, baseMoveSpeedAdd, baseAttackSpeedAdd).
	- Fixes an issue with strange and incorrect behavior on both health modifiers.
- Added the concmd t2_stat for debugging StatHooks.

**3.0.3**

- Additional fixes for legacy code. Should resolve NetConfig missing entry kicks.

**3.0.2**

- Fixed legacy code (ItemBoilerplate, AutoItemConfig) not being included in plugin setup.

**3.0.1**

- Added automatic language reloading (fixes some unloaded language string issues in dependents).