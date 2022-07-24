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

**7.2.1**

- Added a performance option to hide duplicate or all Item Ward displays.
- Patched a null safety hole in MiscUtil.GetRootWithLocators.
- Removed some unused BepInEx plugin soft dependency flags.

**7.2.0**

- Added barebones config preset support to the AutoConfig module.
	- See `AutoConfigPresetAttribute`, `AutoConfigContainer.ApplyPreset()`.
- Added support for Risk of Options buttons.
	- No attribute, must use `Compat_RiskOfOptions.AddOption_Button()` manually.
- Publicized `AutoConfigContainer.FindConfig()`.
- Removed remaining unused BetterUI references.
- Updated lang version to C#9 and implemented its features for some minor project cleanup.
- Updated dependencies.

**7.1.0**

- Fixed a typo in `MiscUtil.Remap` that caused incorrect behavior (was adding `maxTo` to result as final step, should have been `minTo`).
- Added method `MiscUtil.ModifyVanillaPrefab(string addressablePath, string newName, bool shouldNetwork, Func&lt;GameObject, GameObject&gt;)` for concise modification of vanilla prefabs using R2API.PrefabAPI.
- Added new ConCmds to DebugUtil module for rendering items:
	- `goto_itemrender`: opens the internal item rendering scene. Cannot be used while a run is active.
	- `ir_sim`: with the item rendering scene open, spawns an item's pickup model in the proper place and hides existing models. Accepts numeric index or display name (NOT name token).
	- `ir_sqm`: with the item rendering scene open, spawns an equipment's pickup model in the proper place and hides existing models. Accepts numeric index or display name (NOT name token).
- Updated R2API dependency to 4.3.21.

**7.0.1**

- Temporarily switched `Item : CatalogBoilerplate` to use ItemDef.deprecatedTier.

**7.0.0**

- BREAKING CHANGES:
	- Removed obsolete ItemStats and BetterUI support code.
	- `Equipment : CatalogBoilerplate` now adds restart-required config entries for `isEnigmaCompatible` and `canBeRandomlyTriggered`. Added setters to these properties, which were previously getter-only; may require a recompile.
- Added RiskOfOptions integration to AutoConfig as a new category of attributes. Apply with e.g. `[AutoConfigRoOCheckbox()]`.
	- Implemented by default on `T2Module.enabled`, `Item.itemIsAIBlacklisted`, `Equipment.isEnigmaCompatible`, `Equipment.canBeRandomlyTriggered`, and `Equipment.cooldown`.
- CatalogBoilerplate implementations now expose a substage for modifying the ItemDef/EquipmentDef/ArtifactDef before registration with R2API (`public virtual void SetupModify[x]Def()`).
- CatalogBoilerplate now exposes a substage for firing an event when the catalog is ready (`public virtual void SetupCatalogReady()`).
- Lots of behind-the-scenes VS warning/message cleanup.
- Updated for latest RoR2 version.