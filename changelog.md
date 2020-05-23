# TILER2 Changelog

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

**1.0.1**

- Added config options to partially/completely disable NetConfig mismatch checking, and increased the timeout time from 10s to 30s. This is a holdover until the root cause of the relevant issue can be found and addressed.

**1.0.0**

- Initial release. Transferred ItemBoilerplate, MiscUtil, and DebugUtil from ClassicItems to here. Added AutoItemConfig and NetConfig.