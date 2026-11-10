using System;
using System.Collections.Generic;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Bench.Unity
{
    /// <summary>
    /// Forward and inverse kinematics over the canonical physical athlete's
    /// actual joints: the same anchors, neutral parent-to-child rotations and
    /// joint-space frames the powered writer drives. A pose computed here is
    /// exactly the pose the drives would realize with zero tracking error, so
    /// it can be used both to place the athlete before tick zero and to build
    /// joint references. It never writes a body.
    /// </summary>
    public sealed class BenchKinematics
    {
        private readonly PhysicalAthleteRig _rig;
        private readonly Dictionary<string, JointInfo> _joints = new Dictionary<string, JointInfo>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();

        public BenchKinematics(PhysicalAthleteRig rig)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            if (rig.PoweredController == null)
                throw new InvalidOperationException("The physical rig must be built before its kinematics are captured.");

            foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
            {
                _order.Add(recipe.Id);
                if (recipe.ParentId == null)
                    continue;
                PoweredJointController.PoweredJointRuntime powered = rig.PoweredController.GetJoint(recipe.Id);
                _joints.Add(recipe.Id, new JointInfo(
                    recipe.ParentId,
                    powered.Recipe,
                    powered.Joint.anchor,
                    powered.Joint.connectedAnchor,
                    powered.NeutralParentToChild,
                    powered.JointSpace));
            }
        }

        public IReadOnlyList<string> SegmentOrder => _order;

        public PhysicalAthleteRig Rig => _rig;

        public bool TryGetJoint(string childId, out JointInfo info) => _joints.TryGetValue(childId, out info);

        public JointInfo Joint(string childId) => _joints[childId];

        /// <summary>World pose of every segment for a root pose and logical joint rotations (missing = neutral).</summary>
        public Dictionary<string, Pose> Solve(Pose pelvisWorld, IReadOnlyDictionary<string, Quaternion> logical)
        {
            var poses = new Dictionary<string, Pose>(_order.Count, StringComparer.Ordinal);
            foreach (string id in _order)
            {
                if (!_joints.TryGetValue(id, out JointInfo joint))
                {
                    poses[id] = pelvisWorld;
                    continue;
                }

                Quaternion q = logical != null && logical.TryGetValue(id, out Quaternion value) ? value : Quaternion.identity;
                poses[id] = ChildPose(poses[joint.ParentId], joint, q);
            }
            return poses;
        }

        public static Pose ChildPose(Pose parent, JointInfo joint, Quaternion logical)
        {
            Quaternion parentToChild = PoweredJointController.ToParentChildRelativeRotation(
                joint.NeutralParentToChild, joint.JointSpace, logical);
            Quaternion rotation = parent.rotation * parentToChild;
            Vector3 anchor = parent.position + parent.rotation * joint.ConnectedAnchor;
            return new Pose(anchor - rotation * joint.Anchor, rotation);
        }

        /// <summary>Logical joint-space rotation currently realized between two bodies.</summary>
        public static Quaternion RealizedLogical(JointInfo joint, Quaternion parentWorld, Quaternion childWorld)
        {
            Quaternion parentToChild = Quaternion.Inverse(parentWorld) * childWorld;
            return PoweredJointController.ToLogicalTargetRotation(joint.NeutralParentToChild, joint.JointSpace, parentToChild);
        }

        public Vector3 JointAnchorWorld(IReadOnlyDictionary<string, Pose> poses, string childId)
        {
            JointInfo joint = _joints[childId];
            Pose child = poses[childId];
            return child.position + child.rotation * joint.Anchor;
        }

        // ---------------- Inverse kinematics ----------------

        /// <summary>One joint of a chain and how many parameters it carries.</summary>
        public static int ParameterCount(JointInfo joint) => joint.Recipe.Kind == PhysicalJointKind.Hinge ? 1 : 3;

        public static Quaternion FromParameters(JointInfo joint, double[] p, int offset)
        {
            if (joint.Recipe.Kind == PhysicalJointKind.Hinge)
                return Quaternion.AngleAxis((float)(p[offset] * Mathf.Rad2Deg), Vector3.right);
            Vector3 v = new Vector3((float)p[offset], (float)p[offset + 1], (float)p[offset + 2]);
            float angle = v.magnitude;
            return angle < 1e-9f ? Quaternion.identity : Quaternion.AngleAxis(angle * Mathf.Rad2Deg, v / angle);
        }

        public static void ToParameters(JointInfo joint, Quaternion q, double[] p, int offset)
        {
            q = PoweredJointController.NormalizeCanonical(q);
            if (joint.Recipe.Kind == PhysicalJointKind.Hinge)
            {
                p[offset] = PoweredJointController.SignedTwistRadians(q, Vector3.right);
                return;
            }
            q.ToAngleAxis(out float degrees, out Vector3 axis);
            if (degrees > 180f)
                degrees -= 360f;
            Vector3 v = float.IsFinite(axis.x) ? axis * (degrees * Mathf.Deg2Rad) : Vector3.zero;
            p[offset] = v.x;
            p[offset + 1] = v.y;
            p[offset + 2] = v.z;
        }

        /// <summary>
        /// Penalty residuals that keep a joint inside its authored range with a
        /// margin: twist about the calibrated X axis against [low, high], and
        /// each swing component against the secondary limit.
        /// </summary>
        public static void LimitResiduals(JointInfo joint, Quaternion q, float marginDeg, List<double> residuals, double weight)
        {
            q = PoweredJointController.NormalizeCanonical(q);
            float twistDeg = PoweredJointController.SignedTwistRadians(q, Vector3.right) * Mathf.Rad2Deg;
            float low = joint.Recipe.LowDegrees + marginDeg;
            float high = joint.Recipe.HighDegrees - marginDeg;
            residuals.Add(weight * (twistDeg < low ? (low - twistDeg) : twistDeg > high ? (twistDeg - high) : 0d) * Mathf.Deg2Rad);
            if (joint.Recipe.Kind == PhysicalJointKind.Hinge)
                return;

            // PhysX D6 swing limits form an elliptical cone, not a box:
            // (swingY/limitY)^2 + (swingZ/limitZ)^2 <= 1. Both limits are equal
            // in this recipe, so the cone is circular.
            float cone = SwingConeDegrees(q);
            float limit = Mathf.Max(0f, joint.Recipe.SecondaryLimitDegrees - marginDeg);
            residuals.Add(weight * Mathf.Max(0f, cone - limit) * Mathf.Deg2Rad);
        }

        /// <summary>Total swing angle of a twist-swing decomposition about X (the cone angle).</summary>
        public static float SwingConeDegrees(Quaternion q)
        {
            SwingDegrees(PoweredJointController.NormalizeCanonical(q), out float y, out float z);
            return Mathf.Sqrt(y * y + z * z);
        }

        /// <summary>Swing part of a twist-swing decomposition about X, as Y and Z angles.</summary>
        public static void SwingDegrees(Quaternion q, out float swingY, out float swingZ)
        {
            Vector3 v = new Vector3(q.x, q.y, q.z);
            Vector3 projection = Vector3.Project(v, Vector3.right);
            Quaternion twist = new Quaternion(projection.x, projection.y, projection.z, q.w);
            float norm = Mathf.Sqrt(twist.x * twist.x + twist.w * twist.w);
            twist = norm < 1e-9f ? Quaternion.identity : new Quaternion(twist.x / norm, 0f, 0f, twist.w / norm);
            Quaternion swing = q * Quaternion.Inverse(twist);
            swing = PoweredJointController.NormalizeCanonical(swing);
            float s = Mathf.Sqrt(swing.y * swing.y + swing.z * swing.z);
            float angle = 2f * Mathf.Atan2(s, Mathf.Clamp(swing.w, -1f, 1f)) * Mathf.Rad2Deg;
            if (s < 1e-9f)
            {
                swingY = swingZ = 0f;
                return;
            }
            swingY = angle * swing.y / s;
            swingZ = angle * swing.z / s;
        }

        /// <summary>
        /// Damped least-squares over a residual function. Small, dense and
        /// numeric: the chains have at most seven parameters.
        /// </summary>
        public static double[] LevenbergMarquardt(
            double[] initial,
            Func<double[], List<double>> residualFunction,
            int iterations,
            out double finalCost)
        {
            int n = initial.Length;
            double[] p = (double[])initial.Clone();
            List<double> r = residualFunction(p);
            double cost = Cost(r);
            double lambda = 1e-3;
            const double h = 1e-5;

            for (int iteration = 0; iteration < iterations && cost > 1e-14; iteration++)
            {
                int m = r.Count;
                var jacobian = new double[m, n];
                for (int j = 0; j < n; j++)
                {
                    double saved = p[j];
                    p[j] = saved + h;
                    List<double> rp = residualFunction(p);
                    p[j] = saved - h;
                    List<double> rm = residualFunction(p);
                    p[j] = saved;
                    for (int i = 0; i < m; i++)
                        jacobian[i, j] = (rp[i] - rm[i]) / (2d * h);
                }

                var jtj = new double[n, n];
                var jtr = new double[n];
                for (int a = 0; a < n; a++)
                {
                    for (int i = 0; i < m; i++)
                        jtr[a] += jacobian[i, a] * r[i];
                    for (int b = 0; b < n; b++)
                    {
                        double sum = 0d;
                        for (int i = 0; i < m; i++)
                            sum += jacobian[i, a] * jacobian[i, b];
                        jtj[a, b] = sum;
                    }
                }

                bool improved = false;
                for (int attempt = 0; attempt < 8 && !improved; attempt++)
                {
                    var system = new double[n, n];
                    var rhs = new double[n];
                    for (int a = 0; a < n; a++)
                    {
                        for (int b = 0; b < n; b++)
                            system[a, b] = jtj[a, b];
                        system[a, a] += lambda * (1d + jtj[a, a]);
                        rhs[a] = -jtr[a];
                    }

                    double[] delta = SolveLinear(system, rhs);
                    if (delta == null)
                    {
                        lambda *= 10d;
                        continue;
                    }

                    double[] candidate = new double[n];
                    for (int a = 0; a < n; a++)
                        candidate[a] = p[a] + delta[a];
                    List<double> rc = residualFunction(candidate);
                    double candidateCost = Cost(rc);
                    if (candidateCost < cost)
                    {
                        p = candidate;
                        r = rc;
                        cost = candidateCost;
                        lambda = Math.Max(1e-9, lambda * 0.3);
                        improved = true;
                    }
                    else
                    {
                        lambda *= 10d;
                    }
                }

                if (!improved)
                    break;
            }

            finalCost = cost;
            return p;
        }

        private static double Cost(List<double> r)
        {
            double sum = 0d;
            foreach (double value in r)
                sum += value * value;
            return sum;
        }

        private static double[] SolveLinear(double[,] a, double[] b)
        {
            int n = b.Length;
            var m = (double[,])a.Clone();
            var x = (double[])b.Clone();
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < n; row++)
                {
                    if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                        pivot = row;
                }
                if (Math.Abs(m[pivot, col]) < 1e-14)
                    return null;
                if (pivot != col)
                {
                    for (int k = 0; k < n; k++)
                    {
                        double t = m[col, k];
                        m[col, k] = m[pivot, k];
                        m[pivot, k] = t;
                    }
                    double tb = x[col];
                    x[col] = x[pivot];
                    x[pivot] = tb;
                }
                for (int row = col + 1; row < n; row++)
                {
                    double f = m[row, col] / m[col, col];
                    for (int k = col; k < n; k++)
                        m[row, k] -= f * m[col, k];
                    x[row] -= f * x[col];
                }
            }
            for (int row = n - 1; row >= 0; row--)
            {
                double sum = x[row];
                for (int k = row + 1; k < n; k++)
                    sum -= m[row, k] * x[k];
                x[row] = sum / m[row, row];
            }
            return x;
        }

        public readonly struct JointInfo
        {
            public JointInfo(
                string parentId,
                PhysicalJointRecipe recipe,
                Vector3 anchor,
                Vector3 connectedAnchor,
                Quaternion neutralParentToChild,
                Quaternion jointSpace)
            {
                ParentId = parentId;
                Recipe = recipe;
                Anchor = anchor;
                ConnectedAnchor = connectedAnchor;
                NeutralParentToChild = neutralParentToChild;
                JointSpace = jointSpace;
            }

            public string ParentId { get; }
            public PhysicalJointRecipe Recipe { get; }
            public Vector3 Anchor { get; }
            public Vector3 ConnectedAnchor { get; }
            public Quaternion NeutralParentToChild { get; }
            public Quaternion JointSpace { get; }
        }
    }
}
