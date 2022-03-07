using R2API;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Networking;
using static TILER2.MiscUtil;

namespace TILER2 {
    internal class CatalogBoilerplateModule : T2Module<CatalogBoilerplateModule> {
        public override bool managedEnable => false;

        internal static readonly FilingDictionary<CatalogBoilerplate> allInstances = new FilingDictionary<CatalogBoilerplate>();

        public override void SetupConfig() {
            base.SetupConfig();
            On.RoR2.PickupCatalog.Init += On_PickupCatalogInit;
            On.RoR2.UI.LogBook.LogBookController.BuildPickupEntries += On_LogbookBuildPickupEntries;
            On.RoR2.Run.BuildDropTable += On_RunBuildDropTable;
            On.RoR2.PickupPickerController.GetOptionsFromPickupIndex += PickupPickerController_GetOptionsFromPickupIndex;
        }

        private PickupPickerController.Option[] PickupPickerController_GetOptionsFromPickupIndex(On.RoR2.PickupPickerController.orig_GetOptionsFromPickupIndex orig, PickupIndex pickupIndex) {
            var origv = orig(pickupIndex);
            var remv = new HashSet<PickupIndex>();
            foreach(CatalogBoilerplate bpl in allInstances) {
                if((bpl is Item || bpl is Equipment) && !bpl.enabled && bpl.pickupIndex != PickupIndex.none) remv.Add(bpl.pickupIndex);
            }
            return origv.Where(x => !remv.Contains(x.pickupIndex)).ToArray();
        }

        private void On_RunBuildDropTable(On.RoR2.Run.orig_BuildDropTable orig, Run self) {
            var newItemMask = self.availableItems;
            var newEqpMask = self.availableEquipment;
            foreach(CatalogBoilerplate bpl in allInstances) {
                if(bpl is Item item) {
                    bool shouldDrop = item.enabled && item.itemDef.DoesNotContainTag(ItemTag.WorldUnique);
                    if(shouldDrop)
                        newItemMask.Add(item.catalogIndex);
                    else
                        newItemMask.Remove(item.catalogIndex);
                } else if(bpl is Equipment equipment) {
                    bool shouldDrop = equipment.enabled;
                    if(shouldDrop)
                        newEqpMask.Add(equipment.catalogIndex);
                    else
                        newEqpMask.Remove(equipment.catalogIndex);
                }
            }
            self.availableItems = newItemMask;
            self.availableEquipment = newEqpMask;
            orig(self);
            //should force-update most cached drop tables
            PickupDropTable.RegenerateAll(Run.instance);
        }

        private void On_PickupCatalogInit(On.RoR2.PickupCatalog.orig_Init orig) {
            orig();

            foreach(CatalogBoilerplate bpl in allInstances) {
                PickupIndex pind;
                if(bpl is Equipment) pind = PickupCatalog.FindPickupIndex(((Equipment)bpl).catalogIndex);
                else if(bpl is Item) pind = PickupCatalog.FindPickupIndex(((Item)bpl).catalogIndex);
                else continue;
                var pickup = PickupCatalog.GetPickupDef(pind);

                bpl.pickupDef = pickup;
                bpl.pickupIndex = pind;
            }
        }

        private RoR2.UI.LogBook.Entry[] On_LogbookBuildPickupEntries(On.RoR2.UI.LogBook.LogBookController.orig_BuildPickupEntries orig, Dictionary<RoR2.ExpansionManagement.ExpansionDef, bool> expansionAvailability) {
            var retv = orig(expansionAvailability);
            var bplsLeft = allInstances.ToList();
            foreach(var entry in retv) {
                if(!(entry.extraData is PickupIndex)) continue;
                CatalogBoilerplate matchedBpl = null;
                foreach(CatalogBoilerplate bpl in bplsLeft) {
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
