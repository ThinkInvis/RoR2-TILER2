using UnityEngine;
using RoR2;
using BepInEx;
using System;
using System.Reflection;
using System.Linq;
using static TILER2.MiscUtil;
using R2API.Utils;
using R2API;
using UnityEngine.Networking;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace TILER2 {
    
    [BepInDependency("com.bepis.r2api")]
    [BepInPlugin(ModGuid, ModName, ModVer)]
    [R2APISubmoduleDependency(nameof(ItemAPI), nameof(LanguageAPI), nameof(ResourcesAPI), nameof(PlayerAPI), nameof(PrefabAPI), nameof(BuffAPI), nameof(CommandHelper))]
    public class TILER2Plugin:BaseUnityPlugin {
        public const string ModVer = "1.2.0";
        public const string ModName = "TILER2";
        public const string ModGuid = "com.ThinkInvisible.TILER2";
        
        internal static Type nodeRefType;
        internal static Type nodeRefTypeArr;

        internal static FilingDictionary<ItemBoilerplate> masterItemList = new FilingDictionary<ItemBoilerplate>();

        internal static bool globalStatsDirty = false;
        internal static bool globalDropsDirty = false;

        internal ConfigFile cfgFile;

        internal static ConfigEntry<bool> gCfgEnableCheck;
        internal static ConfigEntry<bool> gCfgMismatchKick;
        internal static ConfigEntry<bool> gCfgBadVersionKick;
        internal static ConfigEntry<bool> gCfgTimeoutKick;

        internal TILER2Plugin() {
            cfgFile = new ConfigFile(System.IO.Path.Combine(Paths.ConfigPath, ModGuid + ".cfg"), true);
            
            gCfgEnableCheck = cfgFile.Bind(new ConfigDefinition("NetConfig", "EnableCheck"), true, new ConfigDescription(
                "If false, NetConfig will not check for config mismatches at all."));
            gCfgMismatchKick = cfgFile.Bind(new ConfigDefinition("NetConfig", "MismatchKick"), true, new ConfigDescription(
                "If false, NetConfig will not kick clients that fail config checks (caused by config entries internally marked as both DeferForever and DisallowNetMismatch)."));
            gCfgBadVersionKick = cfgFile.Bind(new ConfigDefinition("NetConfig", "BadVersionKick"), true, new ConfigDescription(
                "If false, NetConfig will not kick clients that are missing config entries (may be caused by different mod versions on client)."));
            gCfgTimeoutKick = cfgFile.Bind(new ConfigDefinition("NetConfig", "TimeoutKick"), true, new ConfigDescription(
                "If false, NetConfig will not kick clients that take too long to respond to config checks (may be caused by missing mods on client)."));
        }

        internal const int customKickReasonNCCritMismatch = 859321;
        internal const int customKickReasonNCTimeout = 859322;
        internal const int customKickReasonNCMissingEntry = 859323;

        public void Awake() {
            var kickMsgType = typeof(RoR2.Networking.GameNetworkManager).GetNestedType("KickMessage", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var kickMsgReasonProp = kickMsgType.GetProperty("reason");

            LanguageAPI.Add("TILER2_KICKREASON_NCCRITMISMATCH", "TILER2 NetConfig: unable to resolve some config mismatches. Please check your console.");
            LanguageAPI.Add("TILER2_KICKREASON_NCTIMEOUT", "TILER2 NetConfig: mismatch check timed out. Please check your console, and ask the server host to check theirs.");
            LanguageAPI.Add("TILER2_KICKREASON_NCMISSINGENTRY", "TILER2 NetConfig: mismatch check found missing entries. You are likely using a different version of a mod than the server.");

            On.RoR2.Networking.GameNetworkManager.KickMessage.GetDisplayToken += (orig, self) => {
                try {
                    if(self.GetType() != kickMsgType) return orig(self);
                    RoR2.Networking.GameNetworkManager.KickReason reason = (RoR2.Networking.GameNetworkManager.KickReason)kickMsgReasonProp.GetValue(self);
                    if((int)reason == customKickReasonNCCritMismatch) return "TILER2_KICKREASON_NCCRITMISMATCH";
                    if((int)reason == customKickReasonNCTimeout) return "TILER2_KICKREASON_NCTIMEOUT";
                    if((int)reason == customKickReasonNCMissingEntry) return "TILER2_KICKREASON_NCMISSINGENTRY";
                    return orig(self);
                } catch(Exception ex) {
                    Debug.LogError("TILER2: failed to inject custom kick message");
                    Debug.LogError(ex);
                    return orig(self);
                }
            };

            //this doesn't seem to fire until the title screen is up, which is good because config file changes shouldn't immediately be read during startup; watch for regression (or just implement a check anyways?)
            On.RoR2.RoR2Application.Update += AutoItemConfigContainer.FilePollUpdateHook;
            On.RoR2.PickupCatalog.Init += On_PickupCatalogInit;
            On.RoR2.UI.LogBook.LogBookController.BuildPickupEntries += On_LogbookBuildPickupEntries;

            CommandHelper.AddToConsoleWhenReady();

            nodeRefType = typeof(DirectorCore).GetNestedTypes(BindingFlags.NonPublic).First(t=>t.Name == "NodeReference");
            nodeRefTypeArr = nodeRefType.MakeArrayType();

            var netOrchPrefabPrefab = new GameObject("TILER2NetConfigOrchestratorPrefabPrefab");
            netOrchPrefabPrefab.AddComponent<NetworkIdentity>();
            NetConfig.netOrchPrefab = netOrchPrefabPrefab.InstantiateClone("TILER2NetConfigOrchestratorPrefab");
            NetConfig.netOrchPrefab.AddComponent<NetConfigOrchestrator>();
            
            bool itemDropAPISupportsRemoval = typeof(ItemDropAPI).GetMethods().Where(m => m.Name == "RemoveFromDefaultByTier").Count() > 0;
            On.RoR2.Run.BuildDropTable += (orig, self) => {
                var newItemMask = self.availableItems;
                var newEqpMask = self.availableEquipment;
                foreach(ItemBoilerplate bpl in masterItemList) {
                    if(!bpl.enabled) {
                        if(bpl is Equipment eqp) newEqpMask.RemoveEquipment(eqp.regIndex);
                        else if(bpl is Item item) newItemMask.RemoveItem(item.regIndex);
                    }
                }
                self.availableItems = newItemMask;
                self.availableEquipment = newEqpMask;
                self.NetworkavailableItems = newItemMask;
                self.NetworkavailableEquipment = newEqpMask;
                //ItemDropAPI completely overwrites drop tables; need to perform separate removal
                if(R2API.R2API.IsLoaded("ItemDropAPI")) {
                    //RemoveFromDefaultAllTiers is in a potentially unreleased R2API update
                    if(itemDropAPISupportsRemoval) {
                        TemporaryCompat.ItemDropAPIRemoveAll();
                    } else {
                        //Temporary reflection patch. TODO: remove at some point after R2API updates
                        Dictionary<ItemTier, List<ItemIndex>> ati = (Dictionary<ItemTier, List<ItemIndex>>)typeof(ItemDropAPI).GetFieldCached("AdditionalTierItems").GetValue(null);
                        List<EquipmentIndex> aeqp = (List<EquipmentIndex>)typeof(ItemDropAPI).GetFieldCached("AdditionalEquipment").GetValue(null);
                        foreach(ItemBoilerplate bpl in masterItemList) {
                            if(bpl is Equipment eqp) {
                                if(eqp.enabled) {
                                    if(!aeqp.Contains(eqp.regIndex)) aeqp.Add(eqp.regIndex);
                                } else aeqp.Remove(eqp.regIndex);
                            } else if(bpl is Item item) {
                                if(item.enabled) {
                                    if(!ati[item.itemTier].Contains(item.regIndex)) ati[item.itemTier].Add(item.regIndex);
                                } else ati[item.itemTier].Remove(item.regIndex);
                            }
                        }
                    }
                }
                orig(self);
                //should force-update most cached drop tables
                typeof(PickupDropTable).GetMethodCached("RegenerateAll").Invoke(null, new object[]{Run.instance});
                //update existing Command droplets. part of an effort to disable items mid-stage, may not be necessary while that's prevented
                var pickerOptions = typeof(PickupPickerController).GetFieldCached("options");
                foreach(var picker in FindObjectsOfType<PickupPickerController>()) {
                    var oldOpt = ((PickupPickerController.Option[])pickerOptions.GetValue(picker))[0];
                    picker.SetOptionsFromPickupForCommandArtifact(oldOpt.pickupIndex);
                }
            };
            
            On.RoR2.Networking.GameNetworkManager.Disconnect += (orig, self) => {
                orig(self);
                AutoItemConfig.CleanupDirty(true);
            };
            
            
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => {
                AutoItemConfig.CleanupDirty(false);
            };

            /*On.RoR2.Run.EndStage += (orig, self) => {
                orig(self);
                AutoItemConfig.CleanupDirty(false);
            };*/
            
            On.RoR2.Networking.GameNetworkManager.OnServerAddPlayerInternal += (orig, self, conn, pcid, extraMsg) => {
                orig(self, conn, pcid, extraMsg);
                if(!gCfgEnableCheck.Value || Util.ConnectionIsLocal(conn) || NetConfigOrchestrator.checkedConnections.Contains(conn)) return;
                NetConfigOrchestrator.checkedConnections.Add(conn);
                NetConfig.EnsureOrchestrator();
                NetConfigOrchestrator.AICSyncAllToOne(conn);
            };

            /*On.RoR2.RuleBook.GenerateItemMask += (orig, self) => {
                var retv = orig(self);

                foreach(ItemBoilerplate bpl in masterItemList) {
                    if(bpl.enabled || !(bpl is Item)) continue;
                    Debug.Log("Removing: " + bpl);
                    retv.RemoveItem(((Item)bpl).regIndex);
                }

                return retv;
            };
            On.RoR2.RuleBook.GenerateEquipmentMask += (orig, self) => {
                var retv = orig(self);

                foreach(ItemBoilerplate bpl in masterItemList) {
                    if(bpl.enabled || !(bpl is Equipment)) continue;
                    retv.RemoveEquipment(((Equipment)bpl).regIndex);
                }

                return retv;
            };*/

            On.RoR2.Run.Start += (orig, self) => {
                orig(self);
                if(!NetworkServer.active) return;
                var itemRngGenerator = new Xoroshiro128Plus(self.seed);
                foreach(var bpl in masterItemList)
                    bpl.itemRng = new Xoroshiro128Plus(itemRngGenerator.nextUlong);
            };
        }

        private void Update() {
            if(!(Run.instance?.isActiveAndEnabled ?? false)) {
                globalStatsDirty = false;
                globalDropsDirty = false;
            } else {
                if(globalStatsDirty) {
                    globalStatsDirty = false;
                    MiscUtil.AliveList().ForEach(cm => {if(cm.hasBody) cm.GetBody().RecalculateStats();});
                }
                if(globalDropsDirty) {
                    globalDropsDirty = false;
                    Run.instance.BuildDropTable();
                }
            }
        }
        
        private void On_PickupCatalogInit(On.RoR2.PickupCatalog.orig_Init orig) {
            orig();

            foreach(ItemBoilerplate bpl in masterItemList) {
                PickupIndex pind;
                if(bpl is Equipment) pind = PickupCatalog.FindPickupIndex(((Equipment)bpl).regIndex);
                else if(bpl is Item) pind = PickupCatalog.FindPickupIndex(((Item)bpl).regIndex);
                else continue;
                var pickup = PickupCatalog.GetPickupDef(pind);

                bpl.pickupDef = pickup;
                bpl.pickupIndex = pind;
            }
        }

        private RoR2.UI.LogBook.Entry[] On_LogbookBuildPickupEntries(On.RoR2.UI.LogBook.LogBookController.orig_BuildPickupEntries orig) {
            var retv = orig();
            var bplsLeft = masterItemList.ToList();
            foreach(var entry in retv) {
                if(!(entry.extraData is PickupIndex)) continue;
                ItemBoilerplate matchedBpl = null;
                foreach(ItemBoilerplate bpl in bplsLeft) {
                    if((PickupIndex)entry.extraData == bpl.pickupIndex) {
                        matchedBpl = bpl;
                        break;
                    }
                }
                if(matchedBpl != null) {
                    matchedBpl.logbookEntry = entry;
                    bplsLeft.Remove(matchedBpl);
                }
            }
            return retv;
        }
    }
}