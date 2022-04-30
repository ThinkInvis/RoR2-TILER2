using RoR2;
using RoR2.Artifacts;
using System.Linq;
using UnityEngine;
using System.Reflection;
using R2API.Utils;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;

namespace TILER2 {
    internal static class DebugUtil {
        internal static void Setup() {
        }

        [ConCommand(commandName = "goto_itemrender", helpText = "Opens the item rendering scene.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Code Quality", "IDE0051:Remove unused private members", Justification = "Used by UnityEngine")]
        private static void CCGotoRenderScene(ConCommandArgs args) {
            if(Run.instance) {
                Debug.LogError("Cannot goto render scene while a run is active.");
                return;
            }
            Addressables.LoadSceneAsync("RoR2/Dev/renderitem/renderitem.unity",
                UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        [ConCommand(commandName = "ir_sim", helpText = "Spawns an item's entire pickup model, for use with the item rendering scene and a runtime inspector.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Code Quality", "IDE0051:Remove unused private members", Justification = "Used by UnityEngine")]
        private static void CCSpawnItemModel(ConCommandArgs args) {
            var igh = GameObject.Find("ITEM GOES HERE (can offset from here)");
            if(Run.instance || !igh) {
                Debug.LogError("Cannot spawn an item model outside the item render scene (use concmd goto_itemrender).");
                return;
            }
            if(args.Count < 1) {
                TILER2Plugin._logger.LogError("ir_sim: missing argument 1 (item ID)!");
                return;
            }

            ItemIndex item;
            string itemSearch = args.TryGetArgString(0);
            if(itemSearch == null) {
                TILER2Plugin._logger.LogError("ir_sim: could not read argument 1 (item ID)!");
                return;
            } else if(int.TryParse(itemSearch, out int itemInd)) {
                item = (ItemIndex)itemInd;
                if(!ItemCatalog.IsIndexValid(item)) {
                    TILER2Plugin._logger.LogError("ir_sim: argument 1 (item ID as integer ItemIndex) is out of range; no item with that ID exists!");
                    return;
                }
            } else {
                var results = ItemCatalog.allItems.Where((ind) => {
                    var iNameToken = ItemCatalog.GetItemDef(ind).nameToken;
                    var iName = Language.GetString(iNameToken);
                    return iName.ToUpper().Contains(itemSearch.ToUpper());
                });
                if(results.Count() < 1) {
                    TILER2Plugin._logger.LogError("ir_sim: argument 1 (item ID as string ItemName) not found in ItemCatalog; no item with a name containing that string exists!");
                    return;
                } else {
                    if(results.Count() > 1)
                        TILER2Plugin._logger.LogWarning("ir_sim: argument 1 (item ID as string ItemName) matched multiple items; using first.");
                    item = results.First();
                }
            }

            var idef = ItemCatalog.GetItemDef(item);
            var idefModel = idef.pickupModelPrefab;

            GameObject.Instantiate(idefModel, igh.transform);
        }

        [ConCommand(commandName = "ir_sqm", helpText = "Spawns an equipment's entire pickup model, for use with the item rendering scene and a runtime inspector.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Code Quality", "IDE0051:Remove unused private members", Justification = "Used by UnityEngine")]
        private static void CCSpawnEquipmentModel(ConCommandArgs args) {
            var igh = GameObject.Find("ITEM GOES HERE (can offset from here)");
            if(Run.instance || !igh) {
                Debug.LogError("Cannot spawn an equipment model outside the item render scene (use concmd goto_itemrender).");
                return;
            }
            if(args.Count < 1) {
                TILER2Plugin._logger.LogError("ir_sqm: missing argument 1 (item ID)!");
                return;
            }

            EquipmentIndex item;
            string itemSearch = args.TryGetArgString(0);
            if(itemSearch == null) {
                TILER2Plugin._logger.LogError("ir_sqm: could not read argument 1 (equipment ID)!");
                return;
            } else if(int.TryParse(itemSearch, out int itemInd)) {
                item = (EquipmentIndex)itemInd;
                if(!EquipmentCatalog.IsIndexValid(item)) {
                    TILER2Plugin._logger.LogError("ir_sqm: argument 1 (equipment ID as integer EquipmentIndex) is out of range; no item with that ID exists!");
                    return;
                }
            } else {
                var results = EquipmentCatalog.allEquipment.Where((ind) => {
                    var iNameToken = EquipmentCatalog.GetEquipmentDef(ind).nameToken;
                    var iName = Language.GetString(iNameToken);
                    return iName.ToUpper().Contains(itemSearch.ToUpper());
                });
                if(results.Count() < 1) {
                    TILER2Plugin._logger.LogError("ir_sqm: argument 1 (equipment ID as string EquipmentName) not found in EquipmentCatalog; no equipment with a name containing that string exists!");
                    return;
                } else {
                    if(results.Count() > 1)
                        TILER2Plugin._logger.LogWarning("ir_sqm: argument 1 (equipment ID as string EquipmentName) matched multiple equipments; using first.");
                    item = results.First();
                }
            }

            var idef = EquipmentCatalog.GetEquipmentDef(item);
            var idefModel = idef.pickupModelPrefab;

            GameObject.Instantiate(idefModel, igh.transform);
        }

        [ConCommand(commandName = "evo_setitem", flags = ConVarFlags.ExecuteOnServer
            #if !DEBUG
            | ConVarFlags.Cheat
            #endif
            , helpText = "Sets the count of an item in the monster team's Artifact of Evolution bank. Argument 1: item name/ID. Argument 2: count of item (dft. 1).")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Code Quality", "IDE0051:Remove unused private members", Justification = "Used by UnityEngine")]
        private static void CCEvoSetItem(ConCommandArgs args) {
            if(args.Count < 1) {
                TILER2Plugin._logger.LogError("evo_setitem: missing argument 1 (item ID)!");
                return;
            }
            int icnt;
            if(args.Count > 1) {
                int? icntArg = args.TryGetArgInt(1);
                if(!icntArg.HasValue || icntArg < 0) {
                    TILER2Plugin._logger.LogError("evo_setitem: argument 2 (item count) must be a positive integer!");
                    return;
                }
                icnt = (int)icntArg;
            } else {
                icnt = 1;
            }

            ItemIndex item;
            string itemSearch = args.TryGetArgString(0);
            if(itemSearch == null) {
                TILER2Plugin._logger.LogError("evo_setitem: could not read argument 1 (item ID)!");
                return;
            }
            else if(int.TryParse(itemSearch, out int itemInd)) {
                item = (ItemIndex)itemInd;
                if(!ItemCatalog.IsIndexValid(item)) {
                    TILER2Plugin._logger.LogError("evo_setitem: argument 1 (item ID as integer ItemIndex) is out of range; no item with that ID exists!");
                    return;
                }
            } else {
                var results = ItemCatalog.allItems.Where((ind)=>{
                    var iNameToken = ItemCatalog.GetItemDef(ind).nameToken;
                    var iName = Language.GetString(iNameToken);
                    return iName.ToUpper().Contains(itemSearch.ToUpper());
                });
                if(results.Count() < 1) {
                    TILER2Plugin._logger.LogError("evo_setitem: argument 1 (item ID as string ItemName) not found in ItemCatalog; no item with a name containing that string exists!");
                    return;
                } else {
                    if(results.Count() > 1)
                        TILER2Plugin._logger.LogWarning("evo_setitem: argument 1 (item ID as string ItemName) matched multiple items; using first.");
                    item = results.First();
                }
            }

            Inventory inv = MonsterTeamGainsItemsArtifactManager.monsterTeamInventory;
            if(inv == null) {
                TILER2Plugin._logger.LogError("evo_setitem: Artifact of Evolution must be enabled!");
                return;
            }

            int diffCount = icnt-inv.GetItemCount(item);
            inv.GiveItem(item, diffCount);
            TILER2Plugin._logger.LogMessage($"evo_setitem: {(diffCount > 0 ? "added " : "removed ")}{Mathf.Abs(diffCount)}x {Language.GetString(ItemCatalog.GetItemDef(item).nameToken)}");
        }
    }
}