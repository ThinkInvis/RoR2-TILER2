using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;
using RoR2.Skills;

namespace TILER2 {
    public static class SkillUtil {
        //TODO: replace active skilldefs when overrides happen

        /// <summary>Calls RecalculateValues on all GenericSkill instances (on living CharacterBodies) which have the target SkillDef.</summary>
        public static void GlobalUpdateSkillDef(SkillDef targetDef) {
            MiscUtil.AliveList().ForEach(cb => {
                if(!cb.hasBody) return;
                var sloc = cb.GetBody().skillLocator;
                if(!sloc) return;
                for(var i = 0; i < sloc.skillSlotCount; i++) {
                    var tsk = sloc.GetSkillAtIndex(i);
                    if(tsk.skillDef == targetDef)
                        tsk.RecalculateValues();
                }
            });
        }

        public static SkillFamily FindSkillFamilyFromBody(string bodyName, int slot) {
            var targetBodyIndex = BodyCatalog.FindBodyIndex(bodyName);
            if(targetBodyIndex == -1) {
                TILER2Plugin._logger.LogError($"FindSkillFamilyFromBody: Couldn't find body with name {bodyName}");
                return null;
            }
            var allSlots = BodyCatalog.GetBodyPrefabSkillSlots(targetBodyIndex);
            if(slot < 0 || slot > allSlots.Length) {
                TILER2Plugin._logger.LogError($"FindSkillFamilyFromBody: Skill slot {slot} is invalid for body with name {bodyName}");
                return null;
            }
            return BodyCatalog.GetBodyPrefabSkillSlots(targetBodyIndex)[slot].skillFamily;
        }

        public static void OverrideVariant(this SkillFamily targetFamily, SkillDef origDef, SkillDef newDef) {
            var ind = Array.FindIndex(targetFamily.variants, x => x.skillDef.skillIndex == origDef.skillIndex);
            if(ind < 0) {
                TILER2Plugin._logger.LogError($"SkillFamily.OverrideVariant: couldn't find target skilldef {origDef} in family {targetFamily}; either it's not there or SkillCatalog needs to init first");
                return;
            }
            targetFamily.variants[ind].skillDef = newDef;
        }

        public static void OverrideVariant(string targetBodyName, int targetSlot, SkillDef origDef, SkillDef newDef) {
            var targetFamily = FindSkillFamilyFromBody(targetBodyName, targetSlot);
            if(targetFamily != null)
                targetFamily.OverrideVariant(origDef, newDef);
            else
                TILER2Plugin._logger.LogError("Failed to OverrideVariant for bodyname+slot (target not found)");
        }

        public static void AddVariant(this SkillFamily targetFamily, SkillDef newDef, string unlockableName = "") {
            Array.Resize(ref targetFamily.variants, targetFamily.variants.Length + 1);
            targetFamily.variants[targetFamily.variants.Length - 1] = new SkillFamily.Variant {
                skillDef = newDef,
                viewableNode = new ViewablesCatalog.Node(newDef.skillNameToken, false, null),
                unlockableName = unlockableName
            };
        }

        public static void AddVariant(string targetBodyName, int targetSlot, SkillDef newDef, string unlockableName = "") {
            var targetFamily = FindSkillFamilyFromBody(targetBodyName, targetSlot);
            if(targetFamily != null)
                targetFamily.AddVariant(newDef, unlockableName);
            else
                TILER2Plugin._logger.LogError("Failed to AddVariant for bodyname+slot (target not found)");
        }

        public static void RemoveVariant(this SkillFamily targetFamily, SkillDef targetDef) {
            var trimmedVariants = new List<SkillFamily.Variant>(targetFamily.variants);
            var oldLen = trimmedVariants.Count;
            trimmedVariants.RemoveAll(x => x.skillDef.skillIndex == targetDef.skillIndex);
            if(trimmedVariants.Count - oldLen == 0) TILER2Plugin._logger.LogError($"SkillFamily.RemoveVariant: Couldn't find SkillDef {targetDef} for removal from SkillFamily {targetFamily}; either it's not there or SkillCatalog needs to init first");
            targetFamily.variants = trimmedVariants.ToArray();
        }
        
        public static void RemoveVariant(string targetBodyName, int targetSlot, SkillDef targetDef) {
            var targetFamily = FindSkillFamilyFromBody(targetBodyName, targetSlot);
            if(targetFamily != null)
                targetFamily.RemoveVariant(targetDef);
            else
                TILER2Plugin._logger.LogError("Failed to RemoveVariant for bodyname+slot (target not found)");
        }

        public static SkillDef CloneSkillDef(SkillDef oldDef) {
            var newDef = ScriptableObject.CreateInstance<SkillDef>();

            newDef.skillName = oldDef.skillName;
            newDef.skillNameToken = oldDef.skillNameToken;
            newDef.skillDescriptionToken = oldDef.skillDescriptionToken;
            newDef.icon = oldDef.icon;
            newDef.activationStateMachineName = oldDef.activationStateMachineName;
            newDef.activationState = oldDef.activationState;
            newDef.interruptPriority = oldDef.interruptPriority;
            newDef.baseRechargeInterval = oldDef.baseRechargeInterval;
            newDef.baseMaxStock = oldDef.baseMaxStock;
            newDef.rechargeStock = oldDef.rechargeStock;
            newDef.isBullets = oldDef.isBullets;
            newDef.shootDelay = oldDef.shootDelay;
            newDef.beginSkillCooldownOnSkillEnd = oldDef.beginSkillCooldownOnSkillEnd;
            newDef.requiredStock = oldDef.requiredStock;
            newDef.stockToConsume = oldDef.stockToConsume;
            newDef.isCombatSkill = oldDef.isCombatSkill;
            newDef.noSprint = oldDef.noSprint;
            newDef.canceledFromSprinting = oldDef.canceledFromSprinting;
            newDef.mustKeyPress = oldDef.mustKeyPress;
            newDef.fullRestockOnAssign = oldDef.fullRestockOnAssign;

            return newDef;
        }
    }
}
