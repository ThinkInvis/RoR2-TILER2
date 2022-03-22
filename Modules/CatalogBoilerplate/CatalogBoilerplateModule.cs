using R2API;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using static TILER2.MiscUtil;

namespace TILER2 {
    internal class CatalogBoilerplateModule : T2Module<CatalogBoilerplateModule> {
        public override bool managedEnable => false;

        internal static readonly FilingDictionary<CatalogBoilerplate> allInstances = new FilingDictionary<CatalogBoilerplate>();
        internal static readonly Dictionary<ItemIndex, Item> itemInstances = new Dictionary<ItemIndex, Item>();
        internal static readonly Dictionary<EquipmentIndex, Equipment> equipmentInstances = new Dictionary<EquipmentIndex, Equipment>();
        internal static readonly Dictionary<ArtifactIndex, Artifact> artifactInstances = new Dictionary<ArtifactIndex, Artifact>();

        public static Sprite lockIcon { get; private set; }

        public override void SetupConfig() {
            base.SetupConfig();
            lockIcon = LegacyResourcesAPI.Load<Sprite>("Textures/MiscIcons/texUnlockIcon");
            On.RoR2.PickupCatalog.Init += On_PickupCatalogInit;
            On.RoR2.UI.LogBook.LogBookController.BuildPickupEntries += On_LogbookBuildPickupEntries;
            On.RoR2.Run.BuildDropTable += On_RunBuildDropTable;
            On.RoR2.PickupPickerController.GetOptionsFromPickupIndex += PickupPickerController_GetOptionsFromPickupIndex;
            On.RoR2.UI.LogBook.LogBookController.CanSelectItemEntry += LogBookController_CanSelectItemEntry;
            On.RoR2.UI.LogBook.LogBookController.CanSelectEquipmentEntry += LogBookController_CanSelectEquipmentEntry;
            On.RoR2.RuleDef.FromItem += RuleDef_FromItem;
            On.RoR2.RuleDef.FromEquipment += RuleDef_FromEquipment;
            On.RoR2.RuleDef.FromArtifact += RuleDef_FromArtifact;
        }
        private RuleDef RuleDef_FromArtifact(On.RoR2.RuleDef.orig_FromArtifact orig, ArtifactIndex artifactIndex) {
            var retv = orig(artifactIndex);
            foreach(CatalogBoilerplate bpl in allInstances) {
                if(bpl is Artifact artifact && artifact.catalogIndex == artifactIndex) {
                    artifactInstances[artifactIndex] = artifact;
                    artifact.ruleDef = retv;
                    break;
                }
            }
            return retv;
        }

        private RuleDef RuleDef_FromEquipment(On.RoR2.RuleDef.orig_FromEquipment orig, EquipmentIndex equipmentIndex) {
            var retv = orig(equipmentIndex);
            foreach(CatalogBoilerplate bpl in allInstances) {
                if(bpl is Equipment equipment && equipment.catalogIndex == equipmentIndex) {
                    equipmentInstances[equipmentIndex] = equipment;
                    equipment.ruleDef = retv;
                    break;
                }
            }
            return retv;
        }

        private RuleDef RuleDef_FromItem(On.RoR2.RuleDef.orig_FromItem orig, ItemIndex itemIndex) {
            var retv = orig(itemIndex);
            foreach(CatalogBoilerplate bpl in allInstances) {
                if(bpl is Item item && item.catalogIndex == itemIndex) {
                    itemInstances[itemIndex] = item;
                    item.ruleDef = retv;
                    break;
                }
            }
            return retv;
        }

        private bool LogBookController_CanSelectEquipmentEntry(On.RoR2.UI.LogBook.LogBookController.orig_CanSelectEquipmentEntry orig, EquipmentDef equipmentDef, Dictionary<RoR2.ExpansionManagement.ExpansionDef, bool> expansionAvailability) {
            var retv = orig(equipmentDef, expansionAvailability);
            if(equipmentDef != null && allInstances.Any(x => !x.enabled && x is Equipment eqp && eqp.equipmentDef == equipmentDef))
                return false;
            return retv;
        }

        private bool LogBookController_CanSelectItemEntry(On.RoR2.UI.LogBook.LogBookController.orig_CanSelectItemEntry orig, ItemDef itemDef, Dictionary<RoR2.ExpansionManagement.ExpansionDef, bool> expansionAvailability) {
            var retv = orig(itemDef, expansionAvailability);
            if(itemDef != null && allInstances.Any(x => !x.enabled && x is Item item && item.itemDef == itemDef))
                return false;
            return retv;
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
                if(bpl is Item item && !item.enabled)
                    newItemMask.Remove(item.catalogIndex);
                else if(bpl is Equipment equipment && !equipment.enabled)
                    newEqpMask.Remove(equipment.catalogIndex);
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
