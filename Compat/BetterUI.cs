using RoR2;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TILER2 {
    ///<summary>
    ///Provides safe hooks for the BetterUI mod. Check Compat_BetterUI.enabled before using any other contained members.
    ///</summary>
    public static class Compat_BetterUI {
        public enum ProcEffect {Chance, HP, Range}
        public enum Stacking {None, Linear, Hyperbolic}

        public delegate string EffectFormatterWrapper(float calcValue, float procCoefficient, float luck, bool canCap, int cap);
        public delegate float StackingFormulaWrapper(float value, float extraStackValue, int stacks);
        public delegate int CapFormulaWrapper(float value, float extraStackValue, float procCoefficient);
        
        public static float LuckCalc(float chance, float luck) {
            return luck == 0 ? chance : (
                luck < 0 ? (Mathf.Pow(chance % 1f, 1f - luck))
                : (1f - Mathf.Pow(1f - (chance % 1f), 1f + luck)));
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void AddEffect(ItemIndex itemIndex, float value, float? extraStackValue = null, EffectFormatterWrapper formatter = null, StackingFormulaWrapper stackFormula = null, CapFormulaWrapper capFormula = null) {
            BetterUI.ProcItemsCatalog.AddEffect(itemIndex, value, extraStackValue,
                formatter != null ? (BetterUI.ProcItemsCatalog.EffectFormatter)((calcValue, procCoef, luck, canCap, cap) => {
                    return formatter(calcValue, procCoef, luck, canCap, cap);
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
        
        public static EffectFormatterWrapper ChanceFormatter;
        public static EffectFormatterWrapper RangeFormatter;
        public static EffectFormatterWrapper HPFormatter;
        public static StackingFormulaWrapper NoStacking;
        public static StackingFormulaWrapper LinearStacking;
        public static StackingFormulaWrapper HyperbolicStacking;
        public static StackingFormulaWrapper ExponentialStacking;
        public static CapFormulaWrapper LinearCap;

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void RetrieveStockWrappers() {
            ChanceFormatter = BetterUI.ProcItemsCatalog.ChanceFormatter;
            RangeFormatter = BetterUI.ProcItemsCatalog.RangeFormatter;
            HPFormatter = BetterUI.ProcItemsCatalog.HPFormatter;
            NoStacking = BetterUI.ProcItemsCatalog.NoStacking;
            LinearStacking = BetterUI.ProcItemsCatalog.LinearStacking;
            HyperbolicStacking = BetterUI.ProcItemsCatalog.HyperbolicStacking;
            ExponentialStacking = BetterUI.ProcItemsCatalog.ExponentialStacking;
            LinearCap = BetterUI.ProcItemsCatalog.LinearCap;
        }

        private static bool? _enabled;
        public static bool enabled {
            get {
                if(_enabled == null) {
                    _enabled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.xoxfaby.BetterUI");
                    if(_enabled.Value) RetrieveStockWrappers();
                }
                return (bool)_enabled;
            }
        }
    }
}
