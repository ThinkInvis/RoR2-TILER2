using RoR2;
using RoR2.Artifacts;
using System.Linq;
using UnityEngine;
using System.Reflection;
using R2API.Utils;
using System.Collections.Generic;

namespace TILER2 {
    internal static class DebugUtil {
        internal static void Setup() {
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