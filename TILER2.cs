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
        public const string ModVer = "1.0.1";
        public const string ModName = "TILER2";
        public const string ModGuid = "com.ThinkInvisible.TILER2";
        
        internal static Type nodeRefType;
        internal static Type nodeRefTypeArr;

        internal static FilingDictionary<ItemBoilerplate> masterItemList = new FilingDictionary<ItemBoilerplate>();

        internal ConfigFile cfgFile;

        internal static ConfigEntry<bool> gCfgMismatchKick;
        internal static ConfigEntry<bool> gCfgMismatchTimeout;
        internal static ConfigEntry<bool> gCfgMismatchCheck;

        internal TILER2Plugin() {
            cfgFile = new ConfigFile(System.IO.Path.Combine(Paths.ConfigPath, ModGuid + ".cfg"), true);
            
            gCfgMismatchCheck = cfgFile.Bind(new ConfigDefinition("NetConfig", "EnableCheck"), true, new ConfigDescription(
                "If false, NetConfig will not check for config mismatches at all."));
            gCfgMismatchKick = cfgFile.Bind(new ConfigDefinition("NetConfig", "MismatchKick"), false, new ConfigDescription(
                "If false, NetConfig will not kick clients that fail config checks."));
            gCfgMismatchTimeout = cfgFile.Bind(new ConfigDefinition("NetConfig", "MismatchTimeoutKick"), false, new ConfigDescription(
                "If false, NetConfig will not kick clients that take too long to respond to config checks."));
        }

        public void Awake() {
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

            On.RoR2.Run.BuildDropTable += (orig, self) => {
                foreach(ItemBoilerplate bpl in masterItemList) {
                    if(!bpl.enabled) {
                        if(bpl is Equipment) self.availableEquipment.RemoveEquipment(((Equipment)bpl).regIndex);
                        else if(bpl is Item) self.availableItems.RemoveItem(((Item)bpl).regIndex);
                    }
                }
                orig(self);
                var pickerOptions = typeof(PickupPickerController).GetFieldCached("options");
                foreach(var picker in FindObjectsOfType<PickupPickerController>()) {
                    var oldOpt = ((PickupPickerController.Option[])pickerOptions.GetValue(picker))[0];
                    picker.SetOptionsFromPickupForCommandArtifact(oldOpt.pickupIndex);
                }
                //TODO: reroll (removed) items in choice boxes
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
                if(!gCfgMismatchCheck.Value || Util.ConnectionIsLocal(conn) || NetConfigOrchestrator.checkedConnections.Contains(conn)) return;
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