using RoR2;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TILER2 {
    ///<summary>
    ///Provides safe hooks for the ItemStats mod. Check Compat_ItemStats.enabled before using any other contained members.
    ///</summary>
    public static class Compat_ItemStats {
        public delegate float ItemStatFormulaWrapper(float itemCount, Inventory ctxInventory, CharacterMaster ctxMaster);
        public delegate string ItemStatFormatterWrapper(float itemCount, Inventory ctxInventory, CharacterMaster ctxMaster);

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void CreateItemStatDef(ItemDef idef, params (ItemStatFormulaWrapper, ItemStatFormatterWrapper)[] wrappers) {
            List<ItemStats.Stat.ItemStat> stats = new List<ItemStats.Stat.ItemStat>();
            
            foreach(var pair in wrappers) {
                stats.Add(new ItemStats.Stat.ItemStat(
                    (itemCount, ctx) => {return pair.Item1.Invoke(itemCount, ctx.Inventory, ctx.Master);},
                    (itemCount, ctx) => {return pair.Item2.Invoke(itemCount, ctx.Inventory, ctx.Master);}));
            }

            var isd = new ItemStats.ItemStatDef {
                Stats = stats
            };

            ItemStats.ItemStatsMod.AddCustomItemStatDef(idef.itemIndex, isd);
        }

        private static bool? _enabled;
        public static bool enabled {
            get {
                if(_enabled == null) _enabled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("dev.ontrigger.itemstats");
                return (bool)_enabled;
            }
        }
    }
}