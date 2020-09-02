# TILER2 Changelog

**2.1.2**

- Fixed inability of FakeInventory to prevent item steal.
- Bumped R2API dependency version to 2.5.14.

**2.1.1**

- Preliminary patch for RoR2 v1.0.1.1. Fixes some immediate breaking issues (plugin load failure); others may exist.

**2.1.0**

- FakeInventory should now work properly in multiplayer.
- BREAKING (minor): FakeInventory no longer inherits from Inventory and has had some structural changes. It's no longer a requirement to add items to the sibling inventory in parallel.

**2.0.0**

- BREAKING: Moved the CloneSkillDef and GlobalUpdateSkillDef methods from MiscUtil to SkillUtil.
- BREAKING: Removed temporary transition patch for a newish R2API feature (ItemBoilerplate --> ItemDropAPI removal).
- Added the FakeInventory and ItemWard components, migrated from Admiral and TinkersSatchel.
- AutoItemConfig now adds a warning to config descriptions if both the DeferForever and PreventNetMismatch flags are set.
- Added the SkillUtil module, incl. several methods for working with SkillFamily variants.
- The NodeOccupationInfo component now works with the OccupyNearbyNodes component.
	- Behavior of OccupyNearbyNodes is changed slightly in the process (multiple objects may now occupy the same node using an OccupyNearbyNodes).
- Switched to publicized RoR2 assembly in favor of a lot of reflection (should increase performance, especially with MiscUtil.RemoveOccupiedNodes).
- Added full documentation for MiscUtil, SkillUtil, StatHooks, NetConfig.
- Bumped R2API dependency version to 2.5.7.

**1.5.0**

- Updated to accomodate breaking changes in RoR2 1.0 and the new R2API version.

**1.4.0**

- General refactor/cleanup of main plugin code into module files.
- StatHooks: fixed incorrect order of application of damage modifiers.
- Reworked MiscUtil's NodeGraph tools.
	- The method RemoveOccupiedNodes has been changed internally, but should remain backwards-compatible.
	- The methods RemoveAllOccupiedNodes and UpdateOccupiedNodesReference have been added.
	- The component NodeOccupationInfo has been added. This is automatically added to objects in most ingame cases where nodes are marked as occupied (notable exception: OccupyNearbyNodes component).
- Now uses plugin-specific console logger.

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