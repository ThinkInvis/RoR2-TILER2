using RoR2;
using System;
using System.Runtime.CompilerServices;

namespace TILER2 {
    ///<summary>
    ///Provides safe hooks for the BetterUI mod. Check Compat_BetterUI.enabled before using any other contained members.
    ///</summary>
    public static class Compat_BetterUI {
        public enum ProcEffect {Chance, HP, Range}
        public enum Stacking {None, Linear, Hyperbolic}

        public delegate string EffectFormatterWrapper(float calcValue, float calcCap, int stacks, float luck, float procCoefficient);
        public delegate float StackingFormulaWrapper(float value, float extraStackValue, int stacks);
        public delegate int CapFormulaWrapper(float value, float extraStackValue, float procCoefficient);
        
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void AddEffect(ItemIndex itemIndex, float value, float? extraStackValue = null, EffectFormatterWrapper formatter = null, StackingFormulaWrapper stackFormula = null, CapFormulaWrapper capFormula = null) {
            BetterUI.ProcItemsCatalog.AddEffect(itemIndex, value, extraStackValue,
                formatter != null ? (BetterUI.ProcItemsCatalog.EffectFormatter)((effInfo, stacks, luck, procCoef) => {
                    return formatter(effInfo.GetValue(stacks), effInfo.GetCap(procCoef), stacks, luck, procCoef);
                }) : null,
                stackFormula != null ? (BetterUI.ProcItemsCatalog.StackingFormula)((val, esv, stacks) => {
                    return stackFormula(val, esv, stacks);
                }) : null,
                capFormula != null ? (BetterUI.ProcItemsCatalog.CapFormula)((val, esv, pCoef) => {
                    return capFormula(val, esv, pCoef);
                }) : null);
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        [Obsolete("Hook for deprecated form of AddEffect. Use delegate form instead.")]
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
