using RoR2;
using System.Runtime.CompilerServices;

namespace TILER2 {
    ///<summary>
    ///Provides safe hooks for the BetterUI mod. Check Compat_BetterUI.enabled before using any other contained members.
    ///</summary>
    public static class Compat_BetterUI {
        public enum ProcEffect {Chance, HP, Range}
        public enum Stacking {None, Linear, Hyperbolic}

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void AddEffect(ItemIndex itemIndex, ProcEffect procEffect, float value, float stackAmount, Stacking stacking = Stacking.Linear) {
            BetterUI.ProcItemsCatalog.AddEffect(itemIndex, (BetterUI.ProcItemsCatalog.ProcEffect)procEffect, value, stackAmount, (BetterUI.ProcItemsCatalog.Stacking)stacking);
        }

        private static bool? _enabled;
        public static bool enabled {
            get {
                if(_enabled == null) _enabled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.xoxfaby.BetterUI");
                return (bool)_enabled;
            }
        }
    }
}
