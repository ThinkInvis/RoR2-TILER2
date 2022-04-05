using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using System.Collections.ObjectModel;
using RoR2.Navigation;

namespace TILER2 {
    /// <summary>
    /// Contains miscellaneous utilities for working with a myriad of RoR2/R2API features, as well as some other math/reflection standalones.
    /// </summary>
    public static class NodeUtil {
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
        /// Collects a specified number of launch velocities that will reach (without hitting anything else) the nearest free navnodes outside a minimum range.
        /// </summary>
        /// <param name="graph">The nodegraph to find nodes from.</param>
        /// <param name="desiredCount">The ideal number of nodes to find.</param>
        /// <param name="minRange">The minimum range to find nodes within.</param>
        /// <param name="maxRange">The maximum range to find nodes within.</param>
        /// <param name="source">The starting point of all trajectories, and the point to search for nodes around.</param>
        /// <param name="extraPeakHeight">See MathUtil.CalculateVelocityForFinalPosition.</param>
        /// <param name="radius">See MathUtil.TrajectorySphereCast.</param>
        /// <param name="maxDeviation">Distance any MathUtil.TrajectorySphereCast hit is allowed to be from its target node to count as a valid result.</param>
        /// <param name="trajectoryResolution">See MathUtil.TrajectorySphereCast.</param>
        /// <param name="layerMask">See MathUtil.TrajectorySphereCast.</param>
        /// <param name="qTI">See MathUtil.TrajectorySphereCast.</param>
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
                var trajectory = MiscUtil.CalculateVelocityForFinalPosition(source, loc, extraPeakHeight);
                var didHit = MiscUtil.TrajectorySphereCast(out RaycastHit hit,
                    source, trajectory.vInitial, trajectory.tFinal,
                    radius, trajectoryResolution, layerMask, qTI);
                if(didHit && (hit.point - loc).sqrMagnitude <= mDevSq)
                    retv.Add(trajectory.vInitial);
                if(retv.Count >= desiredCount)
                    break;
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
            cpt._indices.RemoveAll(i => NodeUtil.RemoveOccupiedNode(self, i.Key, i.Value));
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
