using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using R2API.Utils;
using System.Collections;
using System.Collections.Specialized;
using System.Reflection;
using System.Linq.Expressions;
using RoR2.Skills;

namespace TILER2 {
    public static class MiscUtil {
        private static Type nodeRefType;
        private static Type nodeRefTypeArr;

        internal static void Setup() {
            nodeRefType = typeof(DirectorCore).GetNestedTypes(BindingFlags.NonPublic).First(t=>t.Name == "NodeReference");
            nodeRefTypeArr = nodeRefType.MakeArrayType();
        }

        /// <summary>Calls RecalculateValues on all GenericSkill instances (on living CharacterBodies) which have the target SkillDef.</summary>
        public static void GlobalUpdateSkillDef(SkillDef targetDef) {
            AliveList().ForEach(cb => {
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

        public static SkillDef CloneSkillDef(SkillDef oldDef) {
            var newDef = ScriptableObject.CreateInstance<SkillDef>();

            //newDef.skillName = oldDef.skillName;
            //newDef.skillNameToken = oldDef.skillNameToken;
            //newDef.skillDescriptionToken = oldDef.skillDescriptionToken;
            //newDef.icon = oldDef.icon;
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

        public static float Wrap(float x, float min, float max) {
            if(x < min)
                return max - (min - x) % (max - min);
            else
                return min + (x - min) % (max - min);
        }

        public static void ReflAddEventHandler(this EventInfo evt, object o, Action<object, EventArgs> lam) {
            var pArr = evt.EventHandlerType.GetMethod("Invoke").GetParameters().Select(p=>Expression.Parameter(p.ParameterType)).ToArray();
            var h = Expression.Lambda(evt.EventHandlerType, Expression.Call(Expression.Constant(lam),lam.GetType().GetMethod("Invoke"),pArr[0],pArr[1]),pArr).Compile();
            evt.AddEventHandler(o, h);
        }

        //Collection of unique class instances which all inherit the same type
        public class FilingDictionary<T> : IEnumerable<T> {
            private readonly Dictionary<Type, T> _dict = new Dictionary<Type, T>();

            public int Count => _dict.Count;

            public void Add(T inst) {
                _dict.Add(inst.GetType(), inst);
            }

            public void Add<subT>(subT inst) where subT : T {
                _dict.Add(typeof(subT), inst);
            }

            public void Set<subT>(subT inst) where subT : T {
                _dict[typeof(subT)] = inst;
            }

            public subT Get<subT>() where subT : T {
                return (subT)_dict[typeof(subT)];
            }

            public void Remove(T inst) {
                _dict.Remove(inst.GetType());
            }

            public void RemoveWhere(Func<T, bool> predicate) {
                foreach (var key in _dict.Values.Where(predicate).ToList()) {
                    _dict.Remove(key.GetType());
                }
            }

            public IEnumerator<T> GetEnumerator() {
                return _dict.Values.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() {
                return GetEnumerator();
            }
        }

        public static string Pct(float tgt, uint prec = 0, float mult = 100f) {
            return (tgt*mult).ToString("N" + prec) + "%";
        }
        public static string NPlur(float tgt, uint prec = 0) {
            if(prec == 0)
                return (tgt == 1 || tgt == -1) ? "" : "s";
            else
                return (Math.Abs(Math.Abs(tgt)-1) < Math.Pow(10,-prec)) ? "" : "s";
        }
        public static float GetDifficultyCoeffIncreaseAfter(float time, int stages) {
			DifficultyDef difficultyDef = DifficultyCatalog.GetDifficultyDef(Run.instance.selectedDifficulty);
			float num2 = Mathf.Floor((Run.instance.GetRunStopwatch() + time) * 0.0166666675f);
			float num4 = 0.7f + (float)Run.instance.participatingPlayerCount * 0.3f;
			float num7 = 0.046f * difficultyDef.scalingValue * Mathf.Pow((float)Run.instance.participatingPlayerCount, 0.2f);
			float num9 = Mathf.Pow(1.15f, (float)Run.instance.stageClearCount + (float)stages);
			return (num4 + num7 * num2) * num9 - Run.instance.difficultyCoefficient;
        }
        public static List<CharacterMaster> AliveList(bool playersOnly = false) {
            if(playersOnly) return PlayerCharacterMasterController.instances.Where(x=>x.isConnected && x.master && x.master.hasBody && x.master.GetBody().healthComponent.alive).Select(x=>x.master).ToList();
            else return CharacterMaster.readOnlyInstancesList.Where(x=>x.hasBody && x.GetBody().healthComponent.alive).ToList();
        }
        public static void SpawnItemFromBody(CharacterBody src, int tier, Xoroshiro128Plus rng) {
            List<PickupIndex> spawnList;
            switch(tier) {
                case 1:
                    spawnList = Run.instance.availableTier2DropList;
                    break;
                case 2:
                    spawnList = Run.instance.availableTier3DropList;
                    break;
                case 3:
                    spawnList = Run.instance.availableLunarDropList;
                    break;
                case 4:
                    spawnList = Run.instance.availableNormalEquipmentDropList;
                    break;
                case 5:
                    spawnList = Run.instance.availableLunarEquipmentDropList;
                    break;
                case 0:
                    spawnList = Run.instance.availableTier1DropList;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("tier", tier, "spawnItemFromBody: Item tier must be between 0 and 5 inclusive");
            }
            PickupDropletController.CreatePickupDroplet(spawnList[rng.RangeInt(0,spawnList.Count)], src.transform.position, new Vector3(UnityEngine.Random.Range(-5.0f, 5.0f), 20f, UnityEngine.Random.Range(-5.0f, 5.0f)));
        }

        public static bool RemoveOccupiedNode(this DirectorCore self, RoR2.Navigation.NodeGraph nodeGraph, RoR2.Navigation.NodeGraph.NodeIndex nodeIndex) {
            var ocnf = self.GetType().GetFieldCached("occupiedNodes");
            Array ocn = (Array)ocnf.GetValue(self);
            if(ocn.Length == 0) {
                Debug.LogWarning("TILER2: RemoveOccupiedNode has no nodes to remove");
                return false;
            }
            Array ocnNew = (Array)Activator.CreateInstance(nodeRefTypeArr, ocn.Length - 1);
            IEnumerable ocne = ocn as IEnumerable;
            int i = 0;
            foreach(object o in ocne) {
                var scanInd = o.GetFieldValue<RoR2.Navigation.NodeGraph.NodeIndex>("nodeIndex");
                var scanGraph = o.GetFieldValue<RoR2.Navigation.NodeGraph>("nodeGraph");
                if(object.Equals(scanGraph, nodeGraph) && scanInd.Equals(nodeIndex))
                    continue;
                else if(i == ocn.Length - 1) {
                    Debug.LogWarning("TILER2: RemoveOccupiedNode was passed an already-removed or otherwise nonexistent node");
                    return false;
                }
                ocnNew.SetValue(o, i);
                i++;
            }
            ocnf.SetValue(self, ocnNew);
            return true;
        }
    }
}
