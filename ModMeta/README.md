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

The DebugUtil module adds several console commands:

- `evo_setitem itemIndexOrName count`: Sets the count of the target item in the Artifact of Evolution item pool. Marked as a cheat command.
- `goto_itemrender`: Travels to the ingame item rendering scene. Can only be used from the main menu. Best paired with a runtime inspector mod.
- `ir_sim itemIndexOrName`: Spawns an item model while in the ingame item rendering scene.
- `ir_sqm itemIndexOrName`: Spawns an equipment model while in the ingame item rendering scene.

#### NetConfig

The NetConfig module automatically syncs important config settings from the server to any connecting clients, and kicks clients with critical config mismatches which can't be resolved (i.e. settings that can't be changed while the game is running, or client has different mods than server).

NetConfig also adds the console commands `ncfg_get`, `ncfg_set`, `ncfg_settemp`, and `ncfg`; and the convar `ncfg_allowclientset`.

- `ncfg_get "path1" "optional path2" "optional path3"`: Attempts to find a config entry. Path matches, in order: mod name, config section, config key. If you weren't specific enough, it will print all matching paths to console; otherwise, it will print detailed information about one specific config entry.
- `ncfg_set "path1" "optional path2" "optional path3" value`: Attempts to permanently set a config entry (writes to config file AND changes the ingame value), following the same search rules as ncfg_get. Not usable by non-host players; will route to ncfg_settemp instead.
- `ncfg_settemp "path1" "optional path2" "optional path3" value`: Attempts to temporarily set a config entry until the end of the current run, following the same search rules as ncfg_get. Can be blocked from use by non-host players via ncfg_allowclientset.
- `ncfg "cmd" ...`: Routes to ncfg_get, ncfg_set, or ncfg_settemp (for when you forget the underscore).
- `ncfg_allowclientset` (bool): If 1, any player on a server can use ncfg_settemp. If 0, only the host can use ncfg_settemp.

## Issues/TODO

- Items which players have but were disabled mid-run need a UI indicator for such.
- If a client gets kicked by R2API mod mismatch, NetConfig will attempt to kick them again (to no effect) due to timeout.
- See the GitHub repo for more!

## Changelog

The 5 latest updates are listed below. For a full changelog, see: https://github.com/ThinkInvis/RoR2-TILER2/blob/master/changelog.md

**7.4.3**

- Recompiled, remapped signatures, and updated dependencies for recent vanilla updates.
- 
**7.4.2**

- Greatly improved performance of ItemWard and FakeInventory by introducing component caching.

**7.4.1**

- Fixes for Seekers of the Storm:
	- Partially fixed FakeInventory item count display text tweak not working. The new method may cause overlapping text.
	- Prevented an ItemWard prefab waking up during startup (causes errors now).
	- Updated dependencies for new patch.
	- Retargeted changed hook signatures (fixes errors preventing mod load, and notably language token failures).
- Fixed a NullReferenceException while adding node occupation info to spawned objects.
- Implemented some C#9 features made available by SotS.

**7.4.0**

- Added the CatalogUtil static module, containing `TryGetItemDef` and `TryGetEquipmentDef` methods.

**7.3.5**

- All stock CatalogBoilerplate implementations (Item, Equipment, Artifact) now add an automatic readonly config entry displaying the relevant content's name token and internal name.