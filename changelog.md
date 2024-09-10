# TILER2 Changelog

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

**7.3.4**

- CatalogBoilerplate language setup should no longer be able to cause a game-load-halting error if a token requests an unexpected number of sub-tokens.
- Fixed incorrect assembly version string from last version (kind of a moot point now).

**7.3.3**

- Fixed a potential cascading error while trying to set up language for a CatalogBoilerplate which never received name tokens.
- Updated R2API dependency to 5.0.6 (now using split assembly).
	- Replaced deprecated R2API.CommandHelper with base RoR2's SearchableAttribute.
- Updated BepInExPack dependency to 5.4.2103.

**7.3.2**

- Various fixes and improvements to ConCmds `ir_sim` and `ir_sqm`:
	- Fixed hiding the entire model placeholder instead of individual models.
	- Fixed failing if the model placeholder is inactive but present.
	- Now searches by internal code name instead of by language token lookup (latter failed inexplicably in some cases).
- For developers: NuGet config is now localized (building project no longer requires end-user modification of system or directory NuGet config).

**7.3.1**

- Migrated private method CatalogBoilerplate.GetBestLanguage to public in MiscUtil.

**7.3.0**

- Refactored AutoConfig Risk of Options integration into a much more extensible pattern.
	- Attributes inheriting from `BaseAutoConfigRoOAttribute` will be automatically scanned during `AutoConfigContainer.BindRoO()`.
- Overhauled CatalogBoilerplate language systems to work with R2API.LanguageAPI's language file auto-loading.
	- Non-breaking change: `GetNameString()` et. al. remain in place, and are now virtual instead of abstract.
	- Default behavior is to read tokens with old names ("ModName_ItemName_NAME"), and use the new methods `GetNameStringArgs()` et. al. to retrieve strings to insert during interpolation into a rendered-at-runtime token ("ModName_ItemName_NAME_RENDERED").
		- ItemDefs, EquipmentDefs, etc. now use these rendered tokens.
- Language tokens managed by TILER2 are now completely rebuilt and reloaded during ConCmd language_reload.
- Also migrated some internal strings (mostly in NetConfig/AutoConfig) to language tokens.

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

**6.3.0**

- FakeInventory.ignoreFakes is now exposed to public API, and is now an int instead of a bool.
	- Increment FakeInventory.ignoreFakes whenever you enter a method where you don't want fake items to be considered as part of item count (e.g. while removing or upgrading items). Decrement it before leaving the method.
- Added more sources of IgnoreFakes. FakeInventory should now have better interaction with Egocentrism, Benthic Bloom, Bulwark's Ambry (if player has fake artifact keys *somehow*), and ItemStealController.

**6.2.0**

- Migrated some math/util methods from other mods.
	- `MiscUtil.Remap`: remaps a float from one range to another.
	- `MiscUtil.CalculateVelocityForFinalPosition`: calculates the initial velocity and final time required for a jump-pad-like trajectory between two points.
	- `MiscUtil.TrajectorySphereCast`: performs a spherecast over a parabolic trajectory.
	- `MiscUtil.CollectNearestNodeLaunchVelocities`: collects a specified number of launch velocities that will reach (without hitting anything else) the nearest free navnodes outside a minimum range.
	- `MiscUtil.SteepSigmoid01`: sigmoid-like curve as a function of x with fixed points at (0, 0), (0.5, 0.5), and (1, 1). Has flatter ends and steeper midpoint as b increases.
- Some improvements to ItemWard.
	- No longer hard requires a TeamFilter component. Will instead try to find team from TeamFilter or TeamComponent on the same object, and default to TeamIndex.None otherwise.
	- Fixed a potential net message loop caused by setting radius.
	- Now exposes a stock indicator prefab based on the warbanner area indicator: `TILER2.ItemWard.stockIndicatorPrefab`. Will *not* be created by default if an ItemWard's indicator is unset.
	- Now exposes some extra fields to customize display:
		- `displayRadiusFracH`: horizontal distance at which displays will orbit as a fraction of `radius`.
		- `displayRadiusFracV`: vertical distance at which displays will orbit as a fraction of `radius`.
		- `displayIndivScale`: multiplies scale of individual display prefabs.
		- `displayRadiusOffset`: fixed offset applied to local position of each individual display prefab.
- Updated R2API dependency to 4.3.5.

**6.1.3**

- CatalogBoilerplate > Equipment now exposes canBeRandomlyTriggered on its EquipmentDef.
- CatalogBoilerplateModule now updates Enigma and random-trigger equipment lists to remove disabled equipment.

**6.1.2**

- Temporarily disabled BetterUI.ItemStats support due to item load failures, caused by TILER2 attempting to use an older API (recent update caused breaking changes).

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

**5.0.3**

- Tentative fix for FakeInventory spam-cloning Void items.

**5.0.2**

- Compatibility update for Risk of Rain 2 Expansion 1 (SotV).
- Updated R2API dependency to 4.0.11.
- Updated BepInEx dependency to 5.4.1902.
- Updated BetterUI and ShareSuite compat hooks; no changes appeared to be necessary. ItemStats hook pending update of the mod in question.

**5.0.1**

- Fixed missing config on several modules (AutoConfig, CatalogBoilerplate).

**5.0.0**

- Removed the StatHooks module (now migrated to R2API).

**4.0.7**

- Increased FakeInventory GetItemCount hook safety to parallel vanilla code.
- Disabled items are now hidden in Command droplets.

**4.0.6**

- Reverted from R2API.ItemDropAPI to an internal implementation for drop table management. Resolves the command droplet issue, in addition to several other drop table errors (e.g. duplication --> chance skewing).

**4.0.5**

- Fixed CatalogBoilerplate equipments being added to both Lunar and non-Lunar sources regardless of actual IsLunar flag.

**4.0.4**

- Fixed CatalogBoilerplate items being added to all tiers instead of only the intended tier.

**4.0.3**

- Compatibility updates for recent Risk of Rain 2 patches.
- Updated R2API dependency to 3.0.30.
- Updated BetterUI compat hooks for 2.0.2.
- KNOWN ISSUE: Disabled items/equipments will appear in command droplets.

**4.0.2**

- Fixed defaulting to null values instead of empty arrays in ItemDisplayRuleDict.
- Updated R2API dependency to 3.0.11. Additional removal of a mostly unused feature may have also assisted in resolving issues with Artifact of Command.

**4.0.1**

- Changed FakeInventory.blacklist from a HashSet<ItemIndex> to a HashSet<ItemDef>. ItemIndex now appears to be populated later in setup; ItemDef is more reliable.
- Fixed duplicate hook in FakeInventory.GetItemCount. No related issues were observed, but some probably existed.

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

**3.0.0**

- Introduces the T2Module class.
- Makes sweeping cosmetic renames to several modules.
- ItemBoilerplate (now CatalogBoilerplate) main plugin setup now flows slightly differently.
- CatalogBoilerplate language handling was overhauled to take advantage of R2API Language Overlays.
- AutoConfig now supports using fields as nametags, not just properties.
- SkillUtil.ReplaceVariant/RemoveVariant no longer require SkillCatalog to be initialized.

**2.2.3**

- FakeInventory now provides a blacklist for modded items to use.
- Fixed some potential NullReferenceExceptions caused by using the `?.` operator on Unity objects.
- Fixed TILER2-managed Lunar equipments having the wrong color on some highlights/outlines.

**2.2.2**

- Made FakeInventory more compatible with BetterUI and other item sorting mods.

**2.2.1**

- Updated BetterUI hooks for v1.5.7.

**2.2.0**

- Migrated and publicized mod compat classes from ClassicItems. Now provides public hooks for BetterUI, ItemStats, and ShareSuite.

**2.1.3**

- Fixed items dropping while disabled when R2API.ItemDropAPI was loaded by another mod.

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