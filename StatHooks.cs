using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using System;
using UnityEngine;

namespace TILER2 {
    public static class StatHooks {
        public class StatHookEventArgs : EventArgs {
            public float healthMultAdd = 0f;
            public float baseHealthAdd = 0f;
            public float regenMultAdd = 0f;
            public float baseRegenAdd = 0f;
            public float moveSpeedMultAdd = 0f;
            public float jumpPowerMultAdd = 0f;
            public float damageMultAdd = 0f;
            public float baseDamageAdd = 0f;
            public float attackSpeedMultAdd = 0f;
            public float critAdd = 0f;
            public float armorAdd = 0f;
        }

        public delegate void StatHookEventHandler(CharacterBody sender, StatHookEventArgs args);
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
            
            bool ILFound = c.TryGotoNext(MoveType.After,
                x=>x.MatchLdfld<CharacterBody>("baseMaxHealth"),
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<CharacterBody>("levelMaxHealth"),
                x=>x.MatchLdloc(out _),
                x=>x.MatchMul(),
                x=>x.MatchAdd(),
                x=>x.MatchStloc(out _),
                x=>x.MatchLdcR4(out _));

            if(ILFound) {
                c.EmitDelegate<Func<float, float>>((origHealthMult) => {
                    return origHealthMult + statMods.healthMultAdd;
                });
                c.Index -= 3;
                c.EmitDelegate<Func<float, float>>((origMaxHealth) => {
                    return origMaxHealth + statMods.baseHealthAdd;
                });
            } else {
                Debug.LogError("TILER2/StatHooks: failed to apply IL patch (health modifier)");
            }
            
            ILFound = c.TryGotoNext(MoveType.After,
                x=>x.MatchLdfld<CharacterBody>("baseRegen"),
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<CharacterBody>("levelRegen"),
                x=>x.MatchLdloc(out _),
                x=>x.MatchMul(),
                x=>x.MatchAdd());
            if(ILFound) {
                c.EmitDelegate<Func<float>>(()=>{
                    return statMods.baseRegenAdd;
                });
                c.Emit(OpCodes.Add);
            } else {
                Debug.LogError("TILER2/StatHooks: failed to apply IL patch (base regen modifier)");
            }

            ILFound = c.TryGotoNext(MoveType.After,
                x=>x.OpCode == OpCodes.Ldloc_S,
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
                Debug.LogError("TILER2/StatHooks: failed to apply IL patch (regen multiplier modifier)");
            }

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
                Debug.LogError("TILER2/StatHooks: failed to apply IL patch (move speed modifier)");
            }
            
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
                Debug.LogError("TILER2/StatHooks: failed to apply IL patch (jump power modifier)");
            }

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
                c.EmitDelegate<Func<float, float>>((origDamageMult) => {
                    return origDamageMult + statMods.damageMultAdd;
                });
                c.Index -= 3;
                c.EmitDelegate<Func<float, float>>((origDamage) => {
                    return origDamage + statMods.baseDamageAdd;
                });
            } else {
                Debug.LogError("TILER2/StatHooks: failed to apply IL patch (damage modifier)");
            }

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
                Debug.LogError("TILER2/StatHooks: failed to apply IL patch (attack speed modifier)");
            }

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
                Debug.LogError("TILER2/StatHooks: failed to apply IL patch (crit modifier)");
            }
            
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
                Debug.LogError("TILER2/StatHooks: failed to apply IL patch (armor modifier)");
            }
        }
    }
}
