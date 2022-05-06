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
using UnityEngine.AddressableAssets;
using R2API;

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
        /// Loads a prefab from RoR2 addressable assets, clones it without awakening it, applies a modifier function to the clone, then performs a second InstantiateClone operation to freeze the modified version into a new named prefab.
        /// </summary>
        public static GameObject ModifyVanillaPrefab(string addressablePath, string newName, bool shouldNetwork, Func<GameObject, GameObject> modifierCallback) {
            var origObj = Addressables.LoadAssetAsync<GameObject>(addressablePath)
                .WaitForCompletion()
                .InstantiateClone("Temporary Setup Prefab", false);
            var newObj = modifierCallback(origObj);
            GameObject.Destroy(origObj);
            var newObjPrefabified = newObj.InstantiateClone(newName, shouldNetwork);
            GameObject.Destroy(newObj);
            return newObjPrefabified;
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
        /// Remaps a float from one range to another.
        /// </summary>
        /// <param name="x">The number to perform a remap operation on.</param>
        /// <param name="minFrom">The old lower bound of the remap operation.</param>
        /// <param name="maxFrom">The old upper bound of the remap operation.</param>
        /// <param name="minTo">The new lower bound of the remap operation.</param>
        /// <param name="maxTo">The new upper bound of the remap operation.</param>
        /// <returns>The result of the remap operation of x from [minFrom, maxFrom] to [minTo, maxTo].</returns>
        public static float Remap(float x, float minFrom, float maxFrom, float minTo, float maxTo) {
            return maxTo + (maxTo - minTo) * ((x - minFrom) / (maxFrom - minFrom));
        }

        /// <summary>
        /// Calculates the initial velocity and final time required for a jump-pad-like trajectory between two points.
        /// </summary>
        /// <param name="source">The starting point of the trajectory.</param>
        /// <param name="target">The endpoint of the trajectory.</param>
        /// <param name="extraPeakHeight">Extra height to add above the apex of the lowest possible trajectory.</param>
        /// <returns>vInitial: initial velocity of the trajectory. tFinal: time required to reach target from source.</returns>
        public static (Vector3 vInitial, float tFinal) CalculateVelocityForFinalPosition(Vector3 source, Vector3 target, float extraPeakHeight) {
            var deltaPos = target - source;
            var yF = deltaPos.y;
            var yPeak = Mathf.Max(Mathf.Max(yF, 0) + extraPeakHeight, yF, 0);
            //everything will be absolutely ruined if gravity goes in any direction other than -y. them's the breaks.
            var g = -Physics.gravity.y;
            //calculate initial vertical velocity
            float vY0 = Mathf.Sqrt(2f * g * yPeak);
            //calculate total travel time from vertical velocity
            float tF = Mathf.Sqrt(2) / g * (Mathf.Sqrt(g * (yPeak - yF)) + Mathf.Sqrt(g * yPeak));
            //use total travel time to calculate other velocity components
            var vX0 = deltaPos.x / tF;
            var vZ0 = deltaPos.z / tF;
            return (new Vector3(vX0, vY0, vZ0), tF);
        }

        /// <summary>
        /// Performs a spherecast over a parabolic trajectory.
        /// </summary>
        /// <param name="hit">If a hit occurred: the resultant RaycastHit. Otherwise: default(RaycastHit).</param>
        /// <param name="source">The starting point of the trajectory.</param>
        /// <param name="vInitial">The starting velocity of the trajectory.</param>
        /// <param name="tFinal">The total travel time of the trajectory.</param>
        /// <param name="radius">As with straight-line UnityEngine.Physics.Spherecast.</param>
        /// <param name="resolution">How many individual spherecasts to perform over the trajectory path.</param>
        /// <param name="layerMask">As with straight-line UnityEngine.Physics.Spherecast.</param>
        /// <param name="qTI">As with straight-line UnityEngine.Physics.Spherecast.</param>
        /// <returns>True iff a spherecast hit occurred.</returns>
        public static bool TrajectorySphereCast(out RaycastHit hit, Vector3 source, Vector3 vInitial, float tFinal, float radius, int resolution, int layerMask = Physics.DefaultRaycastLayers, QueryTriggerInteraction qTI = QueryTriggerInteraction.UseGlobal) {
            Vector3 p0, p1;
            p1 = source;
            for(var i = 0; i < resolution; i++) {
                p0 = p1;
                p1 = Trajectory.CalculatePositionAtTime(source, vInitial, ((float)i / (float)resolution + 1f) * tFinal);
                var del = (p1 - p0);
                var didHit = Physics.SphereCast(new Ray(p0, del.normalized), radius, out hit, del.magnitude, layerMask, qTI);
                if(didHit) return true;
            }
            hit = default;
            return false;
        }

        /// <summary>
        /// Collects a specified number of launch velocities that will reach (without hitting anything else) the nearest free navnodes outside a minimum range.
        /// </summary>
        /// <param name="graph">The nodegraph to find nodes from.</param>
        /// <param name="desiredCount">The ideal number of nodes to find.</param>
        /// <param name="minRange">The minimum range to find nodes within.</param>
        /// <param name="maxRange">The maximum range to find nodes within.</param>
        /// <param name="source">The starting point of all trajectories, and the point to search for nodes around.</param>
        /// <param name="extraPeakHeight">See CalculateVelocityForFinalPosition.</param>
        /// <param name="radius">See TrajectorySphereCast.</param>
        /// <param name="maxDeviation">Distance any TrajectorySphereCast hit is allowed to be from its target node to count as a valid result.</param>
        /// <param name="trajectoryResolution">See TrajectorySphereCast.</param>
        /// <param name="layerMask">See TrajectorySphereCast.</param>
        /// <param name="qTI">See TrajectorySphereCast.</param>
        /// <param name="hullMask">Passed through to NodeGraph.FindNodesInRange.</param>
        /// <returns>A list of between 0 and desiredCount launch velocities. Less results will be returned if not enough clear paths to open nodes with the given parameters can be found.</returns>
        public static List<Vector3> CollectNearestNodeLaunchVelocities(
            NodeGraph graph, int desiredCount, float minRange, float maxRange,
            Vector3 source, float extraPeakHeight, float radius, float maxDeviation, int trajectoryResolution,
            int layerMask = Physics.DefaultRaycastLayers, QueryTriggerInteraction qTI = QueryTriggerInteraction.UseGlobal, HullMask hullMask = HullMask.Human) {
            var nodeLocs = graph.FindNodesInRange(source, minRange, maxRange, hullMask)
                .Select(x => { graph.GetNodePosition(x, out Vector3 xloc); return xloc; })
                .OrderBy(x => (x - source).sqrMagnitude);

            List<Vector3> retv = new List<Vector3>();

            var mDevSq = maxDeviation * maxDeviation;

            foreach(var loc in nodeLocs) {
                var (vInitial, tFinal) = CalculateVelocityForFinalPosition(source, loc, extraPeakHeight);
                var didHit = TrajectorySphereCast(out RaycastHit hit,
                    source, vInitial, tFinal,
                    radius, trajectoryResolution, layerMask, qTI);
                if(didHit && (hit.point - loc).sqrMagnitude <= mDevSq)
                    retv.Add(vInitial);
                if(retv.Count >= desiredCount)
                    break;
            }

            return retv;
        }

        /// <summary>
        /// Sigmoid-like curve as a function of x with fixed points at (0, 0), (0.5, 0.5), and (1, 1). Has flatter ends and steeper midpoint as b increases.
        /// </summary>
        /// <param name="x">The point along the curve to evaluate.</param>
        /// <param name="b">Steepness of the curve.</param>
        /// <returns>The point along the curve with parameter b evaluated at point x.</returns>
        public static float SteepSigmoid01(float x, float b) {
            return 0.5f - (float)Math.Tanh(2 * b * (x - 0.5f)) / (2f * (float)Math.Tanh(-b));
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
            PickupDropletController.CreatePickupDroplet(spawnList[rng.RangeInt(0,spawnList.Count)], src.transform.position, new Vector3(UnityEngine.Random.Range(-5.0f, 5.0f), 20f, UnityEngine.Random.Range(-5.0f, 5.0f)));
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
        public static void UpdateOccupiedNodesReference(this DirectorCore _, GameObject oldObj, GameObject newObj) {
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
