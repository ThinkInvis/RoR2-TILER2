using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Collections;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using System.Collections.ObjectModel;
using RoR2.Navigation;

namespace TILER2 {
    /// <summary>
    /// Contains miscellaneous utilities for working with a myriad of RoR2/R2API features, as well as some other math/reflection standalones.
    /// </summary>
    public static class MiscUtil {
        internal static void Setup() {
            IL.RoR2.DirectorCore.TrySpawnObject += IL_DCTrySpawnObject;
            On.RoR2.OccupyNearbyNodes.OnSceneDirectorPrePopulateSceneServer += OccupyNearbyNodes_OnSceneDirectorPrePopulateSceneServer;
        }

        private static void OccupyNearbyNodes_OnSceneDirectorPrePopulateSceneServer(On.RoR2.OccupyNearbyNodes.orig_OnSceneDirectorPrePopulateSceneServer orig, SceneDirector sceneDirector) {
            //orig(self); //nope
			NodeGraph graph = SceneInfo.instance.GetNodeGraph(MapNodeGroup.GraphType.Ground);
            foreach(var onn in OccupyNearbyNodes.instancesList) {
                var noi = onn.GetComponent<NodeOccupationInfo>();
                if(!noi) noi = onn.gameObject.AddComponent<NodeOccupationInfo>();
                var nodes = graph.FindNodesInRange(onn.transform.position, 0f, onn.radius, HullMask.None);
                foreach(var node in nodes) {
                    //TODO: make absolutely sure leaving this out doesn't screw with anything (it's a direct difference from vanilla behavior)
                    //if(Array.Exists(DirectorCore.instance.occupiedNodes, x => {return x.nodeGraph == graph && x.nodeIndex == node;})) continue;
                    noi._indices.Add(new KeyValuePair<NodeGraph, NodeGraph.NodeIndex>(graph, node));
                    DirectorCore.instance.AddOccupiedNode(graph, node);
                }
            }
        }

        private static void IL_DCTrySpawnObject(ILContext il) {
            ILCursor c = new ILCursor(il);
            
            int graphind = -1;
            if(!c.TryGotoNext(
                x=>x.MatchCallOrCallvirt<SceneInfo>("GetNodeGraph"),
                x=>x.MatchStloc(out graphind)
                )) {
                TILER2Plugin._logger.LogError("MiscUtil: failed to apply IL patch (DCTrySpawnObject => graphind), RemoveAllOccupiedNodes will not work for single objects");   
                return;
            }

            c.Index = 0;

            int instind = -1;
            if(!c.TryGotoNext(
                x=>x.MatchLdfld<SpawnCard.SpawnResult>("spawnedInstance"),
                x=>x.MatchStloc(out instind)
                )) {
                TILER2Plugin._logger.LogError("MiscUtil: failed to apply IL patch (DCTrySpawnObject => instind), RemoveAllOccupiedNodes will not work for single objects");   
                return;
            }

            c.Index = 0;

            while(c.TryGotoNext(x=>x.MatchCallOrCallvirt<DirectorCore>("AddOccupiedNode"))) {
                c.Emit(OpCodes.Dup);
                c.Emit(OpCodes.Ldloc, graphind);
                c.Emit(OpCodes.Ldloc, instind);
                c.EmitDelegate<Action<NodeGraph.NodeIndex, NodeGraph, GameObject>>((ind,graph,res)=>{
                    var cpt = res.gameObject.GetComponent<NodeOccupationInfo>();
                    if(!cpt) cpt = res.gameObject.AddComponent<NodeOccupationInfo>();
                    cpt._indices.Add(new KeyValuePair<NodeGraph, NodeGraph.NodeIndex>(graph, ind));
                });
                c.Index++;
            }
        }

        /// <summary>
        /// Wraps a float within the bounds of two other floats.
        /// </summary>
        /// <param name="x">The number to perform a wrap operation on.</param>
        /// <param name="min">The lower bound of the wrap operation.</param>
        /// <param name="max">The upper bound of the wrap operation.</param>
        /// <returns>The result of the wrap operation of x within min, max.</returns>
        public static float Wrap(float x, float min, float max) {
            if(x < min)
                return max - (min - x) % (max - min);
            else
                return min + (x - min) % (max - min);
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

        /// <summary>
        /// Formats a float as a fixed-precision percentage string.
        /// </summary>
        /// <param name="tgt">The number to format.</param>
        /// <param name="prec">The fixed decimal precision of the output.</param>
        /// <param name="mult">The amount to multiply the number by before formatting.</param>
        /// <returns></returns>
        public static string Pct(float tgt, uint prec = 0, float mult = 100f) {
            return (tgt*mult).ToString("N" + prec) + "%";
        }

        /// <summary>
        /// Retrieves the plural form to use in references to a certain number, using a fixed precision to help with epsilon cases.
        /// </summary>
        /// <param name="tgt">The number to analyze.</param>
        /// <param name="prec">The fixed decimal precision to analyze at.</param>
        /// <returns>"" if tgt is approximately -1 or 1; "s" otherwise</returns>
        public static string NPlur(float tgt, uint prec = 0) {
            if(prec == 0)
                return (tgt == 1 || tgt == -1) ? "" : "s";
            else
                return (Math.Abs(Math.Abs(tgt)-1) < Math.Pow(10,-prec)) ? "" : "s";
        }

        /// <summary>
        /// Calculates the projected increase in RoR2.Run.instance.difficultyCoefficient after a certain amount of passed time and completed stages.
        /// </summary>
        /// <param name="time">The amount of passed time to simulate.</param>
        /// <param name="stages">The number of completed stages to simulate.</param>
        /// <returns>A float representing the increase in difficulty coefficient that the given parameters would cause.</returns>
        public static float GetDifficultyCoeffIncreaseAfter(float time, int stages) {
			DifficultyDef difficultyDef = DifficultyCatalog.GetDifficultyDef(Run.instance.selectedDifficulty);
			float num2 = Mathf.Floor((Run.instance.GetRunStopwatch() + time) * 0.0166666675f);
			float num4 = 0.7f + (float)Run.instance.participatingPlayerCount * 0.3f;
			float num7 = 0.046f * difficultyDef.scalingValue * Mathf.Pow((float)Run.instance.participatingPlayerCount, 0.2f);
			float num9 = Mathf.Pow(1.15f, (float)Run.instance.stageClearCount + (float)stages);
			return (num4 + num7 * num2) * num9 - Run.instance.difficultyCoefficient;
        }

        /// <summary>
        /// Returns a list of all CharacterMasters which have living CharacterBodies.
        /// </summary>
        /// <param name="playersOnly">If true, only CharacterMasters which are linked to a PlayerCharacterMasterController will be matched.</param>
        /// <returns>A list of all CharacterMasters which have living CharacterBodies (and PlayerCharacterMasterControllers, if playersOnly is true).</returns>
        public static List<CharacterMaster> AliveList(bool playersOnly = false) {
            if(playersOnly) return PlayerCharacterMasterController.instances.Where(x=>x.isConnected && x.master && x.master.hasBody && x.master.GetBody().healthComponent.alive).Select(x=>x.master).ToList();
            else return CharacterMaster.readOnlyInstancesList.Where(x=>x.hasBody && x.GetBody().healthComponent.alive).ToList();
        }

        /// <summary>
        /// Spawn an item of the given tier at the position of the given CharacterBody.
        /// </summary>
        /// <param name="src">The body to spawn an item from.</param>
        /// <param name="tier">The tier of item to spawn. Must be within 0 and 5, inclusive (Tier 1, Tier 2, Tier 3, Lunar, Equipment, Lunar Equipment).</param>
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

        /// <summary>
        /// Removes a node from a NodeGraph. See the NodeOccupationInfo component.
        /// </summary>
        /// <param name="self">The DirectorCore to perform NodeGraph operations with.</param>
        /// <param name="nodeGraph">The NodeGraph to remove nodes from.</param>
        /// <param name="nodeIndex">The NodeIndex within the given NodeGraph to remove.</param>
        /// <returns>True on successful removals; false otherwise.</returns>
        public static bool RemoveOccupiedNode(this DirectorCore self, NodeGraph nodeGraph, NodeGraph.NodeIndex nodeIndex) {
            var oldLen = self.occupiedNodes.Length;

            self.occupiedNodes = self.occupiedNodes.Where(x => x.nodeGraph != nodeGraph || x.nodeIndex != nodeIndex).ToArray();

            if(oldLen == self.occupiedNodes.Length) {
                TILER2Plugin._logger.LogWarning("RemoveOccupiedNode was passed an already-removed or otherwise nonexistent node");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Removes all NodeGraph nodes that a given GameObject occupies. See the NodeOccupationInfo component.
        /// </summary>
        /// <param name="self">The DirectorCore to perform NodeGraph operations with.</param>
        /// <param name="obj">The GameObject to remove linked nodes with.</param>
        /// <returns>True if no known nodes remain registered to the GameObject after execution; false otherwise.</returns>
        public static bool RemoveAllOccupiedNodes(this DirectorCore self, GameObject obj) {
            var cpt = obj.GetComponent<NodeOccupationInfo>();
            if(!cpt) return true;
            cpt._indices.RemoveAll(i => self.RemoveOccupiedNode(i.Key, i.Value));
            return cpt._indices.Count == 0;
        }

        /// <summary>
        /// Transfers NodeOccupationInfo from one object to another. For use while destroying a GameObject and creating another one in its place (e.g. runtime replacement of a placeholder with an unknown prefab).
        /// </summary>
        /// <param name="self">The DirectorCore to perform NodeGraph operations with.</param>
        /// <param name="oldObj">The to-dispose object to retrieve NodeGraph info from.</param>
        /// <param name="newObj">The new object to add NodeGraph info to.</param>
        public static void UpdateOccupiedNodesReference(this DirectorCore self, GameObject oldObj, GameObject newObj) {
            var oldcpt = oldObj.GetComponent<NodeOccupationInfo>();
            var newcpt = newObj.GetComponent<NodeOccupationInfo>();
            if(!oldcpt || newcpt) return;
            newcpt = newObj.AddComponent<NodeOccupationInfo>();
            newcpt._indices.AddRange(oldcpt._indices);
        }
    }

    /// <summary>
    /// Contains information about which nodes in various NodeGraphs the attached GameObject occupies. Cannot track the OccupyNearbyNodes component.
    /// </summary>
    public class NodeOccupationInfo : MonoBehaviour {
        internal readonly List<KeyValuePair<NodeGraph, NodeGraph.NodeIndex>> _indices;
        /// <summary>A list of all node indices which this GameObject occupies.</summary>
        public readonly ReadOnlyCollection<KeyValuePair<NodeGraph, NodeGraph.NodeIndex>> indices;
        public NodeOccupationInfo() {
            _indices = new List<KeyValuePair<NodeGraph, NodeGraph.NodeIndex>>();
            indices = _indices.AsReadOnly();
        }
    }
}
