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

The DebugUtil module adds the console command `evo_setitem`.

- `evo_setitem itemIndexOrName count`: Sets the count of the target item in the Artifact of Evolution item pool. Marked as a cheat command.

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
- See the GitHub repo for more!

## Changelog

The 5 latest updates are listed below. For a full changelog, see: https://github.com/ThinkInvis/RoR2-TILER2/blob/master/changelog.md

**1.3.0**

- Added StatHooks module.
- Added `Artifact : ItemBoilerplate`.
- Migrated some extension methods from ClassicItems (`CharacterBody.SetBuffCount`).
- GitHub repo is now licensed (GNU GPL3).

**1.2.1**

- ItemBoilerplate: Added member `public Xoroshiro128Plus itemRng {get; internal set;}`. This is initialized at the start of every run, based on the run's main RNG seed.

**1.2.0**

- ItemBoilerplate:
	- *Important:* Fixed disabled items dropping if another currently loaded mod uses R2API.ItemDropAPI.
		- Note: this was due to a bug in R2API. There is another related bug which may make items with custom drop behavior become less common every time the drop table is rebuilt. Both bugs may or may not be fixed in the next R2API update.
	- Added basic support for display rules.
- NetConfig:
	- *Important:* Fixed an issue where NetConfig would use the wrong sender for responses to mismatch checks, causing send failure and subsequent timeout.
	- NetConfigOrchestrator now exposes the following public methods: `void SendConMsg(NetworkUser user, string msg, int severity = 0)`, `void ServerSendGlobalChatMsg(string msg)`.
	- While timeout kick option is disabled, NetConfig now logs a warning to console on timeout instead of doing nothing.
	- The aic_get concmd now provides information on deferred changes and temporary overrides.
- AutoItemConfig:
	- Mid-run changes to AnnounceToRun options (e.g. ItemBoilerplate.enabled) should no longer cause console errors/warnings in singleplayer.
	- Added `AutoUpdateEventFlags.InvalidateDropTable`, replacing the hardcoded droptable update on ItemBoilerplate.enabled.
	- InvalidateStats and InvalidateDropTable now both set a relevant dirty flag, causing an update on the next frame (once per batch of config changes) instead of updating immediately (potentially many times at once).

**1.1.1**

- Added MiscUtil.GlobalUpdateSkillDef.
- ItemBoilerplate: Equipment cooldown is now configurable.

**1.1.0**

- NetConfig mismatch checking now has custom kick messages, and a third option for kicking clients that have missing config entries (likely due to different mod versions). All kick options are now enabled by default, and the timeout time has been reduced to 15s.
- Added MiscUtil.CloneSkillDef.
- Added concmd to debug builds only: aic_scramble.