using R2API;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Networking;
using static TILER2.MiscUtil;

namespace TILER2 {
    internal class ItemBoilerplateModule_V2 : T2Module<ItemBoilerplateModule_V2> {
        internal static FilingDictionary<ItemBoilerplate_V2> masterItemList = new FilingDictionary<ItemBoilerplate_V2>();

        public override void SetupConfig() {
            On.RoR2.PickupCatalog.Init += On_PickupCatalogInit;
            On.RoR2.UI.LogBook.LogBookController.BuildPickupEntries += On_LogbookBuildPickupEntries;
            On.RoR2.Run.BuildDropTable += On_RunBuildDropTable;
        }

        private void On_RunBuildDropTable(On.RoR2.Run.orig_BuildDropTable orig, Run self) {
            var newItemMask = self.availableItems;
            var newEqpMask = self.availableEquipment;
            foreach(ItemBoilerplate_V2 bpl in masterItemList) {
                if(!bpl.enabled) {
                    if(bpl is Equipment_V2 eqp) newEqpMask.Remove(eqp.regIndex);
                    else if(bpl is Item_V2 item) newItemMask.Remove(item.regIndex);
                } else {
                    if(bpl is Equipment_V2 eqp) newEqpMask.Add(eqp.regIndex);
                    else if(bpl is Item_V2 item) newItemMask.Add(item.regIndex);
                }
            }

            //ItemDropAPI completely overwrites drop tables; need to perform separate removal
            if(ItemDropAPI.Loaded) {
                ItemDropAPI.RemoveFromDefaultByTier(
                    masterItemList.Where(bpl => bpl is Item_V2 && !bpl.enabled)
                    .Select(bpl => {
                        return new KeyValuePair<ItemIndex, ItemTier>(((Item_V2)bpl).regIndex, ((Item_V2)bpl).itemTier);
                    })
                    .ToArray());
                ItemDropAPI.RemoveFromDefaultEquipment(
                    masterItemList.Where(bpl => bpl is Equipment_V2 && !bpl.enabled)
                    .Select(bpl => ((Equipment_V2)bpl).regIndex)
                    .ToArray());

                ItemDropAPI.AddToDefaultByTier(
                    masterItemList.Where(bpl => bpl is Item_V2 && bpl.enabled)
                    .Select(bpl => {
                        return new KeyValuePair<ItemIndex, ItemTier>(((Item_V2)bpl).regIndex, ((Item_V2)bpl).itemTier);
                    })
                    .ToArray());
                ItemDropAPI.AddToDefaultEquipment(
                    masterItemList.Where(bpl => bpl is Equipment_V2 && bpl.enabled)
                    .Select(bpl => ((Equipment_V2)bpl).regIndex)
                    .ToArray());
            }
            orig(self);
            //should force-update most cached drop tables
            PickupDropTable.RegenerateAll(Run.instance);
            //update existing Command droplets. part of an effort to disable items mid-stage, may not be necessary while that's prevented
            foreach(var picker in UnityEngine.Object.FindObjectsOfType<PickupPickerController>()) {
                picker.SetOptionsFromPickupForCommandArtifact(picker.options[0].pickupIndex);
            }
        }

        private void On_PickupCatalogInit(On.RoR2.PickupCatalog.orig_Init orig) {
            orig();

            foreach(ItemBoilerplate_V2 bpl in masterItemList) {
                PickupIndex pind;
                if(bpl is Equipment_V2) pind = PickupCatalog.FindPickupIndex(((Equipment_V2)bpl).regIndex);
                else if(bpl is Item_V2) pind = PickupCatalog.FindPickupIndex(((Item_V2)bpl).regIndex);
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
                ItemBoilerplate_V2 matchedBpl = null;
                foreach(ItemBoilerplate_V2 bpl in bplsLeft) {
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
