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

**6.1.1**

- Now defers initial language reload from AutoConfig until after game content has loaded. Fixes a minor conflict with ShowDeathCause.
- Updated R2API dependency to 4.2.1.
- Switched to NuGet as lib source.

**6.1.0**

- Migrated some util methods from other mods into MiscUtil (`GatherEnemies`, `GetRootWithLocators`).
- CatalogBoilerplate implementations (Item, Equipment, Artifact):
	- Now automatically retrieves and stores rulebook entries in the `ruleDef` field.
	- Now applies main tokens (name, pickup, desc, lore) as permanent language.
	- Now displays a lock icon while disabled.
	- Disabled entries will no longer present as usable in the rulebook.
- T2Module: Added support for permanently-installed language via `permanentLanguageOverlays`, `permanentGenericLanguageTokens`, `permanentSpecificLanguageTokens`, `permanentLanguageInstalled`, `virtual void RefreshPermanentLanguage()`.
- Removed some internal/logging references to old "AutoItemConfig" name in favor of "AutoConfig".
- BindDict AutoConfig option now displays an error if used on an empty dictionary.

**6.0.2**

- Made CatalogBoilerplate enable/disable more compatible with RuleBook.

**6.0.1**

- FakeInventory now properly handles contagious (e.g. Void-tier) items.
- Logbook entries of disabled items and equipment are now hidden (only works on game launch).
- Item.CatalogIndex and Equipment.CatalogIndex no longer cause exceptions if the relevant ItemDef/EquipmentDef was never created.

**6.0.0**

- Major rewrite of the NetConfig module incl. breaking API changes.
	- ConCmd/ConVar prefix renamed from "aic_" to "ncfg_".
	- Fixed many cases of ConCmd output not being visible in ingame console.
	- Bandwidth use is greatly reduced (config syncs are now compressed, and less password info is exchanged).
	- Project no longer uses UNetWeaver; all networking is now handled by R2API.NetworkingAPI.
	- Syncs to clients are now queued (fixes a theoretical bug wherein an older change could happen after a newer one, leaving the older config value in place).
	- Main module class renamed from NetConfig to NetConfigModule.
	- Now split into several other non-module members of the TILER2 namespace.
- Removed deprecated CatalogBoilerplate implementation names.
- MiscUtil.SpawnItemFromBody now has 3 more tiers to cover Void items.