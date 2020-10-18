using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using System;
using UnityEngine;

namespace TILER2 {
    /// <summary>
    /// Provides one consolidated IL patch for several commonly-added hooks to RecalculateStats.
    /// </summary>
    public class StatHooks : T2Module<StatHooks> {
        public override bool managedEnable => false;

        public override void SetupConfig() {
            base.SetupConfig();
            IL.RoR2.CharacterBody.RecalculateStats += IL_CBRecalcStats;
        }

        /// <summary>
        /// A collection of modifiers for various stats. Will be passed down the event chain of GetStatCoefficients; add to the contained values to modify stats.
        /// </summary>
        public class StatHookEventArgs : EventArgs {
            /// <summary>Added to the direct multiplier to base health. MAX_HEALTH ~ (BASE_HEALTH + baseHealthAdd) * (HEALTH_MULT + healthMultAdd).</summary>
            public float healthMultAdd = 0f;
            /// <summary>Added to base health. MAX_HEALTH ~ (BASE_HEALTH + baseHealthAdd) * (HEALTH_MULT + healthMultAdd).</summary>
            public float baseHealthAdd = 0f;
            /// <summary>Added to the direct multiplier to base health regen. HEALTH_REGEN ~ (BASE_REGEN + baseRegenAdd) * (REGEN_MULT + regenMultAdd).</summary>
            public float regenMultAdd = 0f;
            /// <summary>Added to base health regen. HEALTH_REGEN ~ (BASE_REGEN + baseRegenAdd) * (REGEN_MULT + regenMultAdd).</summary>
            public float baseRegenAdd = 0f;
            /// <summary>Added to the direct multiplier to move speed. MOVE_SPEED ~ BASE_MOVE_SPEED * (MOVE_SPEED_MULT + moveSpeedMultAdd)</summary>
            public float moveSpeedMultAdd = 0f;
            /// <summary>Added to the direct multiplier to jump power. JUMP_POWER ~ BASE_JUMP_POWER * (JUMP_POWER_MULT + jumpPowerMultAdd)</summary>
            public float jumpPowerMultAdd = 0f;
            /// <summary>Added to the direct multiplier to base damage. DAMAGE ~ (BASE_DAMAGE + baseDamageAdd) * (DAMAGE_MULT + damageMultAdd).</summary>
            public float damageMultAdd = 0f;
            /// <summary>Added to base damage. DAMAGE ~ (BASE_DAMAGE + baseDamageAdd) * (DAMAGE_MULT + damageMultAdd).</summary>
            public float baseDamageAdd = 0f;
            /// <summary>Added to the direct multiplier to attack speed. ATTACK_SPEED ~ BASE_ATTACK_SPEED * (ATTACK_SPEED_MULT + attackSpeedMultAdd).</summary>
            public float attackSpeedMultAdd = 0f;
            /// <summary>Added to crit chance. CRIT_CHANCE ~ BASE_CRIT_CHANCE + critAdd.</summary>
            public float critAdd = 0f;
            /// <summary>Added to armor. ARMOR ~ BASE_ARMOR + armorAdd.</summary>
            public float armorAdd = 0f;
        }

        /// <summary>
        /// Used as the delegate type for the GetStatCoefficients event.
        /// </summary>
        /// <param name="sender">The CharacterBody which RecalculateStats is being called for.</param>
        /// <param name="args">An instance of StatHookEventArgs, passed to each subscriber to this event in turn for modification.</param>
        public delegate void StatHookEventHandler(CharacterBody sender, StatHookEventArgs args);

        /// <summary>
        /// Subscribe to this event to modify one of the stat hooks which TILER2.StatHooks covers (see StatHookEventArgs). Fired during CharacterBody.RecalculateStats.
        /// </summary>
        public static event StatHookEventHandler GetStatCoefficients;

        //TODO: backup modifiers in an On. hook
        internal static void IL_CBRecalcStats(ILContext il) {
            ILCursor c = new ILCursor(il);

            StatHookEventArgs statMods = null;
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Action<CharacterBody>>((cb) => {
                statMods = new StatHookEventArgs();
                GetStatCoefficients?.Invoke(cb, statMods);
            });
            
            int locBaseHealthIndex = -1;
            int locHealthMultIndex = -1;
            bool ILFound = c.TryGotoNext(
                x=>x.MatchLdfld<CharacterBody>("baseMaxHealth"),
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<CharacterBody>("levelMaxHealth"))
                && c.TryGotoNext(
                x => x.MatchStloc(out locBaseHealthIndex))
                && c.TryGotoNext(
                    x => x.MatchLdloc(locBaseHealthIndex),
                    x => x.MatchLdloc(out locHealthMultIndex),
                    x => x.MatchMul(),
                    x => x.MatchStloc(locBaseHealthIndex));

            if(ILFound) {
                c.GotoPrev(x => x.MatchLdfld<CharacterBody>("baseMaxHealth"));
                c.GotoNext(x => x.MatchStloc(locBaseHealthIndex));
                c.EmitDelegate<Func<float, float>>((origMaxHealth) => {
                    return origMaxHealth + statMods.baseHealthAdd;
                });
                c.GotoNext(x => x.MatchStloc(locHealthMultIndex));
                c.EmitDelegate<Func<float, float>>((origHealthMult) => {
                    return origHealthMult + statMods.healthMultAdd;
                });
            } else {
                TILER2Plugin._logger.LogError("StatHooks: failed to apply IL patch (health modifier)");
            }
            
            c.Index = 0;

            int locBaseRegenIndex = -1;
            ILFound = c.TryGotoNext(
                x=>x.MatchLdfld<CharacterBody>("baseRegen"),
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<CharacterBody>("levelRegen"))
                && c.TryGotoNext(
                    x => x.MatchStloc(out locBaseRegenIndex));

            if(ILFound) {
                c.EmitDelegate<Func<float, float>>((origBaseRegen)=>{
                    return origBaseRegen + statMods.baseRegenAdd;
                });
            } else {
                TILER2Plugin._logger.LogError("StatHooks: failed to apply IL patch (base regen modifier)");
            }

            c.Index = 0;

            ILFound = c.TryGotoNext(MoveType.After,
                x=>x.MatchLdloc(locBaseRegenIndex),
                x=>x.MatchAdd(),
                x=>x.OpCode == OpCodes.Ldloc_S,
                x=>x.MatchAdd(),
                x=>x.OpCode == OpCodes.Ldloc_S,
                x=>x.MatchAdd(),
                x=>x.OpCode == OpCodes.Ldloc_S,
                x=>x.MatchAdd());
            if(ILFound) {
                c.EmitDelegate<Func<float>>(()=>{
                    return statMods.regenMultAdd;
                });
                c.Emit(OpCodes.Add);
            } else {
                TILER2Plugin._logger.LogError("StatHooks: failed to apply IL patch (regen multiplier modifier)");
            }
            
            c.Index = 0;

            ILFound = c.TryGotoNext(MoveType.After,
                x=>x.MatchLdfld<CharacterBody>("baseMoveSpeed"),
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<CharacterBody>("levelMoveSpeed"),
                x=>x.MatchLdloc(out _),
                x=>x.MatchMul(),
                x=>x.MatchAdd(),
                x=>x.MatchStloc(out _),
                x=>x.MatchLdcR4(out _));
            if(ILFound) {
                c.EmitDelegate<Func<float, float>>((origMoveSpeedMult) => {
                    return origMoveSpeedMult + statMods.moveSpeedMultAdd;
                });
            } else {
                TILER2Plugin._logger.LogError("StatHooks: failed to apply IL patch (move speed modifier)");
            }
            
            c.Index = 0;

            //Find (parts of): float jumpPower = this.baseJumpPower + this.levelJumpPower * num32;
            ILFound = c.TryGotoNext(MoveType.After,
                x=>x.MatchLdfld<CharacterBody>("baseJumpPower"),
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<CharacterBody>("levelJumpPower"),
                x=>x.MatchLdloc(out _),
                x=>x.MatchMul(),
                x=>x.MatchAdd());

            if(ILFound) {
                c.EmitDelegate<Func<float,float>>((origJumpPower) => {
                    return origJumpPower * (1 + statMods.jumpPowerMultAdd);
                });
            } else {
                TILER2Plugin._logger.LogError("StatHooks: failed to apply IL patch (jump power modifier)");
            }
            
            c.Index = 0;

            ILFound = c.TryGotoNext(MoveType.After,
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<CharacterBody>("baseDamage"),
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<CharacterBody>("levelDamage"),
                x=>x.MatchLdloc(out _),
                x=>x.MatchMul(),
                x=>x.MatchAdd(),
                x=>x.MatchStloc(out _),
                x=>x.MatchLdcR4(out _));
            if(ILFound) {
                c.Index -= 2;
                c.EmitDelegate<Func<float, float>>((origDamage) => {
                    return origDamage + statMods.baseDamageAdd;
                });
                c.Index += 2;
                c.EmitDelegate<Func<float, float>>((origDamageMult) => {
                    return origDamageMult + statMods.damageMultAdd;
                });
            } else {
                TILER2Plugin._logger.LogError("StatHooks: failed to apply IL patch (damage modifier)");
            }
            
            c.Index = 0;

            ILFound = c.TryGotoNext(MoveType.After,
                x=>x.MatchLdfld<CharacterBody>("baseAttackSpeed"),
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<CharacterBody>("levelAttackSpeed"),
                x=>x.MatchLdloc(out _),
                x=>x.MatchMul(),
                x=>x.MatchAdd(),
                x=>x.MatchStloc(out _),
                x=>x.MatchLdcR4(out _));
            if(ILFound) {
                c.EmitDelegate<Func<float, float>>((origAttackSpeedMult) => {
                    return origAttackSpeedMult + statMods.attackSpeedMultAdd;
                });
            } else {
                TILER2Plugin._logger.LogError("StatHooks: failed to apply IL patch (attack speed modifier)");
            }
            
            c.Index = 0;

            int locOrigCrit = -1;
            ILFound = c.TryGotoNext(
                x=>x.MatchLdarg(0),
                x=>x.MatchLdloc(out locOrigCrit),
                x=>x.MatchCallOrCallvirt<CharacterBody>("set_crit"));

            if(ILFound) {
                c.Emit(OpCodes.Ldloc, locOrigCrit);
                c.EmitDelegate<Func<float, float>>((origCrit) => {
                    return origCrit + statMods.critAdd;
                });
                c.Emit(OpCodes.Stloc, locOrigCrit);
            } else {
                TILER2Plugin._logger.LogError("StatHooks: failed to apply IL patch (crit modifier)");
            }
            
            c.Index = 0;

            ILFound = c.TryGotoNext(
                x=>x.MatchLdfld<CharacterBody>("baseArmor"))
                && c.TryGotoNext(
                x=>x.MatchCallOrCallvirt<CharacterBody>("get_armor"))
                && c.TryGotoNext(MoveType.After,
                x=>x.MatchCallOrCallvirt<CharacterBody>("get_armor"));
            if(ILFound) {
                c.EmitDelegate<Func<float,float>>((oldArmor) => {
                    return oldArmor + statMods.armorAdd;
                });
            } else {
                TILER2Plugin._logger.LogError("StatHooks: failed to apply IL patch (armor modifier)");
            }
        }
    }
}
