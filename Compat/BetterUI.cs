using RoR2;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TILER2 {
    ///<summary>
    ///Provides safe hooks for the BetterUI mod. Check Compat_BetterUI.enabled before using any other contained members.
    ///</summary>
    [Obsolete("Nonfunctional: BetterUI API has undergone major breaking changes. Will be repaired in a future update.")]
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

        public static void AddEffect(ItemDef itemDef, float value, float? extraStackValue = null, EffectFormatterWrapper formatter = null, StackingFormulaWrapper stackFormula = null, CapFormulaWrapper capFormula = null) {
            ItemCatalog.availability.CallWhenAvailable(() => {AddEffect(itemDef.itemIndex, value, extraStackValue, formatter, stackFormula, capFormula);});
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void AddEffect(ItemIndex itemIndex, float value, float? extraStackValue = null, EffectFormatterWrapper formatter = null, StackingFormulaWrapper stackFormula = null, CapFormulaWrapper capFormula = null) {
            return;
        }

        public static EffectFormatterWrapper ChanceFormatter;
        public static EffectFormatterWrapper RangeFormatter;
        public static EffectFormatterWrapper HPFormatter;
        public static StackingFormulaWrapper NoStacking;
        public static StackingFormulaWrapper LinearStacking;
        public static StackingFormulaWrapper HyperbolicStacking;
        public static StackingFormulaWrapper ExponentialStacking;
        public static CapFormulaWrapper LinearCap;

        private static bool? _enabled;
        public static bool enabled {
            get {
                return false;
            }
        }
    }
}
