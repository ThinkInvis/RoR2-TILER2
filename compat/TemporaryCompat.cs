using R2API;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TILER2 {
    public static class TemporaryCompat {
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        internal static void ItemDropAPIRemoveAll() {
            ItemDropAPI.RemoveFromDefaultByTier(
                ItemBoilerplateModule.masterItemList.Where(bpl => bpl is Item && !bpl.enabled)
                .Select(bpl => {
                    Debug.Log("Removing: " + bpl.itemCodeName);
                    return new KeyValuePair<ItemIndex, ItemTier>(((Item)bpl).regIndex, ((Item)bpl).itemTier);
                })
                .ToArray());
            ItemDropAPI.RemoveFromDefaultEquipment(
                ItemBoilerplateModule.masterItemList.Where(bpl => bpl is Equipment && !bpl.enabled)
                .Select(bpl => ((Equipment)bpl).regIndex)
                .ToArray());

            ItemDropAPI.AddToDefaultByTier(
                ItemBoilerplateModule.masterItemList.Where(bpl => bpl is Item && bpl.enabled)
                .Select(bpl => {
                    Debug.Log("Adding: " + bpl.itemCodeName);
                    return new KeyValuePair<ItemIndex, ItemTier>(((Item)bpl).regIndex, ((Item)bpl).itemTier);
                })
                .ToArray());
            ItemDropAPI.AddToDefaultEquipment(
                ItemBoilerplateModule.masterItemList.Where(bpl => bpl is Equipment && bpl.enabled)
                .Select(bpl => ((Equipment)bpl).regIndex)
                .ToArray());
        }
    }
}
