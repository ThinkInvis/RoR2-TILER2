using RoR2;
using System;
using UnityEngine;

namespace TILER2 {
    /// <summary>
    /// Contains assorted math and math-related functions.
    /// </summary>
    public static class MathUtil {
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
        /// Sigmoid-like curve as a function of x with fixed points at (0, 0), (0.5, 0.5), and (1, 1). Has flatter ends and steeper midpoint as b increases.
        /// </summary>
        /// <param name="x">The point along the curve to evaluate.</param>
        /// <param name="b">Steepness of the curve.</param>
        /// <returns>The point along the curve with parameter b evaluated at point x.</returns>
        public static float SteepSigmoid01(float x, float b) {
            return 0.5f - (float)Math.Tanh(2 * b * (x - 0.5f)) / (2f * (float)Math.Tanh(-b));
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
    }
}
