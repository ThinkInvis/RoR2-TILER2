using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Collections;
using System.Reflection;
using System.Linq.Expressions;
using RoR2.Navigation;

namespace TILER2 {
    /// <summary>
    /// Contains miscellaneous utilities for working with a myriad of RoR2/R2API features, as well as some other math/reflection standalones.
    /// </summary>
    public static class MiscUtil {
        /// <summary>
        /// Returns a list of all CharacterMasters which have living CharacterBodies.
        /// </summary>
        /// <param name="playersOnly">If true, only CharacterMasters which are linked to a PlayerCharacterMasterController will be matched.</param>
        /// <returns>A list of all CharacterMasters which have living CharacterBodies (and PlayerCharacterMasterControllers, if playersOnly is true).</returns>
        public static List<CharacterMaster> AliveList(bool playersOnly = false) {
            if(playersOnly) return PlayerCharacterMasterController.instances.Where(x => x.isConnected && x.master && x.master.hasBody && x.master.GetBody().healthComponent.alive).Select(x => x.master).ToList();
            else return CharacterMaster.readOnlyInstancesList.Where(x => x.hasBody && x.GetBody().healthComponent.alive).ToList();
        }

        /// <summary>
        /// Iterates towards the root of a GameObject, including jumping through EntityLocators.
        /// </summary>
        /// <param name="target">The GameObject to search for the 'true' root of.</param>
        /// <param name="maxSearch">The maximum amount of recursion to go through.</param>
        /// <returns>Null if the given object was null; the most top-level object with the given constraints otherwise.</returns>
        public static GameObject GetRootWithLocators(GameObject target, int maxSearch = 5) {
            if(!target) return null;
            GameObject scan = target;
            for(int i = 0; i < maxSearch; i++) {
                var cpt = scan.GetComponent<EntityLocator>();

                if(cpt) {
                    scan = cpt.entity;
                    continue;
                }

                var next = scan.transform.root;
                if(next && next.gameObject != scan)
                    scan = next.gameObject;
                else
                    return scan;
            }
            return scan;
        }

        /// <summary>
        /// Spawn an item of the given tier at the position of the given CharacterBody.
        /// </summary>
        /// <param name="src">The body to spawn an item from.</param>
        /// <param name="tier">The tier of item to spawn. Must be within 0 and 8, inclusive (Tier 1, Tier 2, Tier 3, Lunar, Equipment, Lunar Equipment, Void Tier 1, Void Tier 2, Void Tier 3).</param>
        /// <param name="rng">An instance of Xoroshiro128Plus to use for random item selection.</param>
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
                    spawnList = Run.instance.availableLunarItemDropList;
                    break;
                case 4:
                    spawnList = Run.instance.availableEquipmentDropList;
                    break;
                case 5:
                    spawnList = Run.instance.availableLunarEquipmentDropList;
                    break;
                case 6:
                    spawnList = Run.instance.availableVoidTier1DropList;
                    break;
                case 7:
                    spawnList = Run.instance.availableVoidTier2DropList;
                    break;
                case 8:
                    spawnList = Run.instance.availableVoidTier3DropList;
                    break;
                case 0:
                    spawnList = Run.instance.availableTier1DropList;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("tier", tier, "spawnItemFromBody: Item tier must be between 0 and 8 inclusive");
            }
            PickupDropletController.CreatePickupDroplet(spawnList[rng.RangeInt(0, spawnList.Count)], src.transform.position, new Vector3(UnityEngine.Random.Range(-5.0f, 5.0f), 20f, UnityEngine.Random.Range(-5.0f, 5.0f)));
        }

        /// <summary>
        /// Returns a list of enemy TeamComponents given an ally team (to ignore while friendly fire is off) and a list of ignored teams (to ignore under all circumstances).
        /// </summary>
        /// <param name="allyIndex">The team to ignore if friendly fire is off.</param>
        /// <param name="ignore">Additional teams to always ignore.</param>
        /// <returns>A list of all TeamComponents that match the provided team constraints.</returns>
        public static List<TeamComponent> GatherEnemies(TeamIndex allyIndex, params TeamIndex[] ignore) {
            var retv = new List<TeamComponent>();
            bool isFF = FriendlyFireManager.friendlyFireMode != FriendlyFireManager.FriendlyFireMode.Off;
            var scan = ((TeamIndex[])Enum.GetValues(typeof(TeamIndex))).Except(ignore);
            foreach(var ind in scan) {
                if(isFF || allyIndex != ind)
                    retv.AddRange(TeamComponent.GetTeamMembers(ind));
            }
            return retv;
        }

        /// <summary>
        /// Uses reflection to subscribe an event handler to an EventInfo.
        /// </summary>
        /// <param name="evt">The EventInfo to subscribe to.</param>
        /// <param name="o">The object instance to apply this subscription to.</param>
        /// <param name="lam">The method to subscribe with.</param>
        public static void ReflAddEventHandler(this EventInfo evt, object o, Action<object, EventArgs> lam) {
            var pArr = evt.EventHandlerType.GetMethod("Invoke").GetParameters().Select(p=>Expression.Parameter(p.ParameterType)).ToArray();
            var h = Expression.Lambda(evt.EventHandlerType, Expression.Call(Expression.Constant(lam),lam.GetType().GetMethod("Invoke"),pArr[0],pArr[1]),pArr).Compile();
            evt.AddEventHandler(o, h);
        }

        /// <summary>
        /// A collection of unique class instances which all inherit the same base type.
        /// </summary>
        /// <typeparam name="T">The type to enforce inheritance from for the contents of the FilingDictionary.</typeparam>
        public class FilingDictionary<T> : IEnumerable<T> {
            private readonly Dictionary<Type, T> _dict = new Dictionary<Type, T>();

            /// <summary>
            /// Gets the number of instances contained in the FilingDictionary.
            /// </summary>
            public int Count => _dict.Count;

            /// <summary>
            /// Add an object inheriting from <typeparamref name="T"/> to the FilingDictionary. Throws an ArgumentException if an element of the object's type is already contained.
            /// </summary>
            /// <param name="inst">The object to add.</param>
            public void Add(T inst) {
                _dict.Add(inst.GetType(), inst);
            }

            /// <summary>
            /// Add an object of type <typeparamref name="subT"/> to the FilingDictionary. Throws an ArgumentException if an element of the object's type is already contained.
            /// </summary>
            /// <typeparam name="subT">The type of the object to add.</typeparam>
            /// <param name="inst">The object to add.</param>
            public void Add<subT>(subT inst) where subT : T {
                _dict.Add(typeof(subT), inst);
            }

            /// <summary>
            /// Add an object of type <typeparamref name="subT"/> to the FilingDictionary, or replace one of the same type if it already exists.
            /// </summary>
            /// <typeparam name="subT">The type of the object to insert.</typeparam>
            /// <param name="inst">The object to insert.</param>
            public void Set<subT>(subT inst) where subT : T {
                _dict[typeof(subT)] = inst;
            }

            /// <summary>
            /// Attempts to get an object of type <typeparamref name="subT"/> from the FilingDictionary. 
            /// </summary>
            /// <typeparam name="subT">The type of the object to retrieve.</typeparam>
            /// <returns>The unique object matching type <typeparamref name="subT"/> within this FilingDictionary if such an object exists; null otherwise.</returns>
            public subT Get<subT>() where subT : T {
                return (subT)_dict[typeof(subT)];
            }

            /// <summary>
            /// Removes the given object from the FilingDictionary.
            /// </summary>
            /// <param name="T">The object to remove.</typeparam>
            public void Remove(T inst) {
                _dict.Remove(inst.GetType());
            }

            /// <summary>
            /// Removes all objects from the FilingDictionary which match a predicate.
            /// </summary>
            /// <param name="predicate">The predicate to filter removals by.</param>
            public void RemoveWhere(Func<T, bool> predicate) {
                foreach(var key in _dict.Values.Where(predicate).ToList()) {
                    _dict.Remove(key.GetType());
                }
            }

            /// <summary>
            /// Returns an enumerator that iterates through the FilingDictionary's contained objects.
            /// </summary>
            /// <returns>An enumerator that iterates through the FilingDictionary's contained objects.</returns>
            public IEnumerator<T> GetEnumerator() {
                return _dict.Values.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() {
                return GetEnumerator();
            }

            /// <summary>
            /// Returns a new ReadOnlyFilingDictionary wrapping this FilingDictionary.
            /// </summary>
            /// <returns>A new ReadOnlyFilingDictionary wrapping this FilingDictionary.</returns>
            public ReadOnlyFilingDictionary<T> AsReadOnly() => new ReadOnlyFilingDictionary<T>(this);
        }
        
        /// <summary>
        /// A readonly wrapper for an instance of FilingDictionary.
        /// </summary>
        /// <typeparam name="T">The type to enforce inheritance from for the contents of the FilingDictionary.</typeparam>
        public class ReadOnlyFilingDictionary<T> : IReadOnlyCollection<T> {
            private readonly FilingDictionary<T> baseCollection;

            /// <summary>
            /// Creates a new ReadOnlyFilingDictionary wrapping a specific FilingDictionary.
            /// </summary>
            /// <param name="baseCollection">The FilingDictionary to create a readonly wrapper for.</param>
            public ReadOnlyFilingDictionary(FilingDictionary<T> baseCollection) {
                this.baseCollection = baseCollection;
            }

            /// <summary>
            /// Gets the number of instances contained in the wrapped FilingDictionary.
            /// </summary>
            public int Count => baseCollection.Count;

            /// <summary>
            /// Returns an enumerator that iterates through the wrapped FilingDictionary's contained objects.
            /// </summary>
            /// <returns>An enumerator that iterates through the wrapped FilingDictionary's contained objects.</returns>
            public IEnumerator<T> GetEnumerator() => baseCollection.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => baseCollection.GetEnumerator();
        }

        #region Migrated - will be removed!
        [Obsolete("Migrated to TILER2.MathUtil.")]
        public static float Wrap(float x, float min, float max) {
            return MathUtil.Wrap(x, min, max);
        }

        [Obsolete("Migrated to TILER2.MathUtil.")]
        public static float Remap(float x, float minFrom, float maxFrom, float minTo, float maxTo) {
            return MathUtil.Remap(x, minFrom, maxFrom, minTo, maxTo);
        }

        [Obsolete("Migrated to TILER2.MathUtil.")]
        public static (Vector3 vInitial, float tFinal) CalculateVelocityForFinalPosition(Vector3 source, Vector3 target, float extraPeakHeight) {
            return MathUtil.CalculateVelocityForFinalPosition(source, target, extraPeakHeight);
        }

        [Obsolete("Migrated to TILER2.MathUtil.")]
        public static bool TrajectorySphereCast(out RaycastHit hit, Vector3 source, Vector3 vInitial, float tFinal, float radius, int resolution, int layerMask = Physics.DefaultRaycastLayers, QueryTriggerInteraction qTI = QueryTriggerInteraction.UseGlobal) {
            return TrajectorySphereCast(out hit, source, vInitial, tFinal, radius, resolution, layerMask, qTI);
        }

        [Obsolete("Migrated to TILER2.NodeUtil.")]
        public static List<Vector3> CollectNearestNodeLaunchVelocities(
            NodeGraph graph, int desiredCount, float minRange, float maxRange,
            Vector3 source, float extraPeakHeight, float radius, float maxDeviation, int trajectoryResolution,
            int layerMask = Physics.DefaultRaycastLayers, QueryTriggerInteraction qTI = QueryTriggerInteraction.UseGlobal, HullMask hullMask = HullMask.Human) {
            return NodeUtil.CollectNearestNodeLaunchVelocities(graph, desiredCount, minRange, maxRange, source, extraPeakHeight, radius, maxDeviation, trajectoryResolution, layerMask, qTI, hullMask);
        }

        [Obsolete("Migrated to TILER2.MathUtil.")]
        public static float SteepSigmoid01(float x, float b) {
            return MathUtil.SteepSigmoid01(x, b);
        }

        [Obsolete("Migrated to TILER2.MathUtil.")]
        public static string Pct(float tgt, uint prec = 0, float mult = 100f) {
            return MathUtil.Pct(tgt, prec, mult);
        }

        [Obsolete("Migrated to TILER2.MathUtil.")]
        public static string NPlur(float tgt, uint prec = 0) {
            return MathUtil.NPlur(tgt, prec);
        }

        [Obsolete("Migrated to TILER2.MathUtil.")]
        public static float GetDifficultyCoeffIncreaseAfter(float time, int stages) {
            return MathUtil.GetDifficultyCoeffIncreaseAfter(time, stages);
        }


        [Obsolete("Migrated to TILER2.NodeUtil.")]
        public static bool RemoveOccupiedNode(this DirectorCore self, NodeGraph nodeGraph, NodeGraph.NodeIndex nodeIndex) {
            return NodeUtil.RemoveOccupiedNode(self, nodeGraph, nodeIndex);
        }

        [Obsolete("Migrated to TILER2.NodeUtil.")]
        public static bool RemoveAllOccupiedNodes(this DirectorCore self, GameObject obj) {
            return NodeUtil.RemoveAllOccupiedNodes(self, obj);
        }

        [Obsolete("Migrated to TILER2.NodeUtil.")]
        public static void UpdateOccupiedNodesReference(this DirectorCore self, GameObject oldObj, GameObject newObj) {
            NodeUtil.UpdateOccupiedNodesReference(self, oldObj, newObj);
        }
        #endregion
    }
}
