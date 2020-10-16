using R2API;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Networking;
using static TILER2.MiscUtil;

namespace TILER2 {
    internal class ItemBoilerplateModule : T2Module<ItemBoilerplateModule> {
        internal static FilingDictionary<ItemBoilerplate> masterItemList = new FilingDictionary<ItemBoilerplate>();

        public override void SetupConfig() {
            On.RoR2.PickupCatalog.Init += On_PickupCatalogInit;
            On.RoR2.UI.LogBook.LogBookController.BuildPickupEntries += On_LogbookBuildPickupEntries;
            On.RoR2.Run.BuildDropTable += On_RunBuildDropTable;
        }

        private void On_RunBuildDropTable(On.RoR2.Run.orig_BuildDropTable orig, Run self) {
            var newItemMask = self.availableItems;
            var newEqpMask = self.availableEquipment;
            foreach(ItemBoilerplate bpl in masterItemList) {
                if(!bpl.enabled) {
                    if(bpl is Equipment eqp) newEqpMask.Remove(eqp.regIndex);
                    else if(bpl is Item item) newItemMask.Remove(item.regIndex);
                } else {
                    if(bpl is Equipment eqp) newEqpMask.Add(eqp.regIndex);
                    else if(bpl is Item item) newItemMask.Add(item.regIndex);
                }
            }

            //ItemDropAPI completely overwrites drop tables; need to perform separate removal
            if(ItemDropAPI.Loaded) {
                ItemDropAPI.RemoveFromDefaultByTier(
                    masterItemList.Where(bpl => bpl is Item && !bpl.enabled)
                    .Select(bpl => {
                        return new KeyValuePair<ItemIndex, ItemTier>(((Item)bpl).regIndex, ((Item)bpl).itemTier);
                    })
                    .ToArray());
                ItemDropAPI.RemoveFromDefaultEquipment(
                    masterItemList.Where(bpl => bpl is Equipment && !bpl.enabled)
                    .Select(bpl => ((Equipment)bpl).regIndex)
                    .ToArray());

                ItemDropAPI.AddToDefaultByTier(
                    masterItemList.Where(bpl => bpl is Item && bpl.enabled)
                    .Select(bpl => {
                        return new KeyValuePair<ItemIndex, ItemTier>(((Item)bpl).regIndex, ((Item)bpl).itemTier);
                    })
                    .ToArray());
                ItemDropAPI.AddToDefaultEquipment(
                    masterItemList.Where(bpl => bpl is Equipment && bpl.enabled)
                    .Select(bpl => ((Equipment)bpl).regIndex)
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
