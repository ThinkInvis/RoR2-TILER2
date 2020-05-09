using UnityEngine;
using RoR2;
using BepInEx;
using System;
using System.Reflection;
using System.Linq;
using static TILER2.MiscUtil;
using R2API.Utils;
using R2API;

namespace TILER2 {
    
    [BepInDependency("com.bepis.r2api")]
    [BepInPlugin(ModGuid, ModName, ModVer)]
    [R2APISubmoduleDependency(nameof(ItemAPI), nameof(LanguageAPI), nameof(ResourcesAPI), nameof(PlayerAPI), nameof(PrefabAPI), nameof(BuffAPI), nameof(CommandHelper))]
    public class TILER2Plugin:BaseUnityPlugin {
        public const string ModVer = "1.0.0";
        public const string ModName = "TILER2";
        public const string ModGuid = "com.ThinkInvisible.TILER2";
        
        internal static Type nodeRefType;
        internal static Type nodeRefTypeArr;

        internal static FilingDictionary<ItemBoilerplate> masterItemList = new FilingDictionary<ItemBoilerplate>();

        public void Awake() {
            //this doesn't seem to fire until the title screen is up, which is good because config file changes shouldn't immediately be read during startup; watch for regression (or just implement a check anyways?)
            On.RoR2.RoR2Application.Update += AutoItemConfigContainer.FilePollUpdateHook;
            On.RoR2.PickupCatalog.Init += On_PickupCatalogInit;
            On.RoR2.UI.LogBook.LogBookController.BuildPickupEntries += On_LogbookBuildPickupEntries;

            CommandHelper.AddToConsoleWhenReady();

            nodeRefType = typeof(DirectorCore).GetNestedTypes(BindingFlags.NonPublic).First(t=>t.Name == "NodeReference");
            nodeRefTypeArr = nodeRefType.MakeArrayType();

            On.RoR2.Run.BuildDropTable += (orig, self) => {
                foreach(ItemBoilerplate bpl in masterItemList) {
                    if(!bpl.enabled) {
                        Debug.Log("Removing " + bpl.itemCodeName);
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
                else pind = PickupCatalog.FindPickupIndex(((Item)bpl).regIndex);
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