using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerliftingSimulator.PhysicsBenchmarks
{
    /// <summary>
    /// One fresh production squat scene: FoundationBootstrap, the shared
    /// PhysicalAthleteRig, the PhysicalBarbell and the production controller,
    /// stepped one authoritative tick at a time exactly as the GAM-13
    /// mechanics probe steps it.
    /// </summary>
    public sealed class AthleteBenchmarkSession
    {
        public const string SceneName = "SquatPhysicalPrototype";

        public static readonly string[] SagittalJoints =
        {
            "left_foot", "right_foot", "left_shank", "right_shank",
            "left_thigh", "right_thigh", "abdomen", "thorax"
        };

        public FoundationBootstrap Bootstrap;
        public PhysicalAthleteRig Rig;
        public PhysicalBarbell Barbell;
        public SquatPhysicalPrototypeController Controller;
        public FoundationRuntime Runtime;
        public float LoadKg;

        public SquatPhysicalAdapter Adapter => Controller.Adapter;
        public float Dt => (float)SimulationConstants.FixedDeltaTimeSeconds;
        public SquatObservationSnapshot Snapshot => Controller.ObservationCollector.LastSnapshot;

        public static IEnumerator Load(AthleteBenchmarkSession session, float loadKg)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The production squat scene is missing.");
            while (!load.isDone)
                yield return null;
            yield return null;
            session.Bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            session.Rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            session.Barbell = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            session.Controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(session.Bootstrap, Is.Not.Null);
            Assert.That(session.Rig, Is.Not.Null);
            Assert.That(session.Barbell, Is.Not.Null);
            Assert.That(session.Controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !session.Controller.IsInitialized; frame++)
                yield return null;
            Assert.That(session.Controller.IsInitialized, Is.True, session.Controller.StartupFailure);
            session.Controller.enabled = false;
            session.Bootstrap.enabled = false;
            session.Controller.SetLoad(loadKg);
            session.Runtime = session.Bootstrap.Runtime;
            session.LoadKg = loadKg;
            Assert.That(session.Runtime.CurrentTime.Tick, Is.EqualTo(0ul));
            session.ApplySolverOverride();
        }

        public const string PositionIterationsVariable = "PHYSICS_BENCHMARK_POS_ITERS";
        public const string VelocityIterationsVariable = "PHYSICS_BENCHMARK_VEL_ITERS";

        public int PositionIterations { get; private set; } = PhysicalAthleteSolverProfile.PositionIterations;
        public int VelocityIterations { get; private set; } = PhysicalAthleteSolverProfile.VelocityIterations;

        /// <summary>
        /// Convergence sweep seam (benchmark only, tick 0 only): overrides the
        /// solver budget of every athlete body and the bar. PhysX solves an
        /// island at its maximum per-body count, so the bar's own 12/6 is
        /// raised with it rather than left to dominate.
        /// </summary>
        private void ApplySolverOverride()
        {
            int? position = ReadInt(PositionIterationsVariable);
            int? velocity = ReadInt(VelocityIterationsVariable);
            if (!position.HasValue && !velocity.HasValue)
                return;
            foreach (PhysicalAthleteRig.SegmentRuntime segment in Rig.Segments.Values)
            {
                if (position.HasValue) segment.Body.solverIterations = position.Value;
                if (velocity.HasValue) segment.Body.solverVelocityIterations = velocity.Value;
            }
            if (Barbell.Body != null)
            {
                if (position.HasValue) Barbell.Body.solverIterations = Math.Max(position.Value, Barbell.Body.solverIterations);
                if (velocity.HasValue) Barbell.Body.solverVelocityIterations = velocity.Value;
            }
            PositionIterations = position ?? PositionIterations;
            VelocityIterations = velocity ?? VelocityIterations;
        }

        private static int? ReadInt(string variable) =>
            int.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : (int?)null;

        private readonly System.Diagnostics.Stopwatch _stepWatch = new System.Diagnostics.Stopwatch();
        private int _steps;

        /// <summary>Mean wall time of one authoritative tick (commands + PhysX + observation), ms.</summary>
        public double MeanStepMilliseconds => _steps == 0 ? double.NaN : _stepWatch.Elapsed.TotalMilliseconds / _steps;

        public void Step()
        {
            _stepWatch.Start();
            int advanced = Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
            _stepWatch.Stop();
            _steps++;
            Assert.That(advanced, Is.EqualTo(1));
            Controller.LeftFootContact?.PhysicsTickUpdate(Dt);
            Controller.RightFootContact?.PhysicsTickUpdate(Dt);
        }

        public static SquatState StateForPhase(float phase) =>
            phase <= 0f ? SquatState.SETUP : phase >= 1f ? SquatState.BOTTOM : SquatState.DESCENT;

        public static SquatPhaseDirection DirectionForPhase(float phase) =>
            phase <= 0f ? SquatPhaseDirection.None : SquatPhaseDirection.Descent;

        /// <summary>
        /// Pre-simulation only. Places every athlete body at the forward
        /// kinematics of the canonical logical targets at <paramref name="phase"/>
        /// (the production SquatPhysicalTargetForwardKinematics construction),
        /// translated so the feet keep their registered standing placement,
        /// and seats the bar on the saddle anchor. All velocities are zero.
        /// Returns the largest body displacement and the foot orientation
        /// mismatch against the registered standing feet.
        /// </summary>
        public (float maxDisplacementM, float footRotationMismatchDeg) PrePose(float phase)
        {
            if (Runtime.CurrentTime.Tick != 0ul)
                throw new InvalidOperationException("Canonical poses may only be initialised before simulation.");
            SquatPhaseDirection direction = DirectionForPhase(phase);
            var poses = new Dictionary<string, (Vector3 p, Quaternion r)>();
            foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
            {
                if (recipe.ParentId == null)
                {
                    poses[recipe.Id] = (Vector3.zero, Adapter.ReferencePelvisBodyRotation(phase, direction));
                    continue;
                }
                Quaternion target = recipe.Id == "head_neck"
                    ? Quaternion.identity
                    : Adapter.ReferenceLogicalTarget(recipe.Id, phase, direction);
                PoweredJointController.PoweredJointRuntime powered = Rig.PoweredController.GetJoint(recipe.Id);
                ConfigurableJoint joint = powered.Joint;
                Quaternion parentToChild = PoweredJointController.ToParentChildRelativeRotation(
                    powered.NeutralParentToChild, powered.JointSpace, target);
                (Vector3 p, Quaternion r) parent = poses[recipe.ParentId];
                Quaternion childRotation = parent.r * parentToChild;
                Vector3 parentAnchor = parent.p + parent.r * joint.connectedAnchor;
                poses[recipe.Id] = (parentAnchor - childRotation * joint.anchor, childRotation);
            }

            Rigidbody leftFoot = Rig.Segments["left_foot"].Body;
            Rigidbody rightFoot = Rig.Segments["right_foot"].Body;
            Vector3 shift = 0.5f * (leftFoot.position + rightFoot.position) -
                0.5f * (poses["left_foot"].p + poses["right_foot"].p);
            float footMismatch = Mathf.Max(
                Quaternion.Angle(leftFoot.rotation, poses["left_foot"].r),
                Quaternion.Angle(rightFoot.rotation, poses["right_foot"].r));
            float maxDisplacement = 0f;
            foreach (KeyValuePair<string, (Vector3 p, Quaternion r)> pose in poses)
            {
                Rigidbody body = Rig.Segments[pose.Key].Body;
                Vector3 position = pose.Value.p + shift;
                maxDisplacement = Mathf.Max(maxDisplacement, Vector3.Distance(body.position, position));
                Teleport(body, position, pose.Value.r);
            }
            if (Controller.Saddle != null && Barbell.Body != null && Barbell.Body.gameObject.activeInHierarchy)
            {
                Rigidbody thorax = Rig.Segments["thorax"].Body;
                Quaternion barRotation = Barbell.Body.rotation;
                Teleport(Barbell.Body, SquatBarSaddle.AlignedBarRootPosition(thorax, barRotation), barRotation);
            }
            return (maxDisplacement, footMismatch);
        }

        private static void Teleport(Rigidbody body, Vector3 position, Quaternion rotation)
        {
            body.transform.SetPositionAndRotation(position, rotation);
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>Holds the reference clock at a phase and primes/snaps the drives to it (tick 0 only).</summary>
        public void HoldPhase(float phase)
        {
            Adapter.HoldReferencePhaseForQualification(phase, DirectionForPhase(phase), StateForPhase(phase), 0f);
            Rig.PrimeCommandSource();
        }

        public static float TwistLog(Quaternion q)
        {
            q = PoweredJointController.NormalizeCanonical(q);
            float v = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z);
            if (v <= 1e-7f)
                return 0f;
            float angle = 2f * Mathf.Atan2(v, Mathf.Clamp(q.w, -1f, 1f));
            return q.x * angle / v;
        }

        public float MaxAnchorSeparation()
        {
            float worst = 0f;
            foreach (PoweredJointController.PoweredJointRuntime joint in Rig.PoweredController.Joints)
            {
                ConfigurableJoint j = joint.Joint;
                if (j == null || j.connectedBody == null)
                    continue;
                worst = Mathf.Max(worst, Vector3.Distance(j.transform.TransformPoint(j.anchor),
                    j.connectedBody.transform.TransformPoint(j.connectedAnchor)));
            }
            return worst;
        }

        /// <summary>
        /// Order-stable hash of every registered rigid body's full state, bit
        /// exact. Used for same-platform determinism comparison.
        /// </summary>
        public ulong StateHash()
        {
            ulong hash = 1469598103934665603UL;
            void Mix(float value)
            {
                uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
                for (int i = 0; i < 4; i++)
                {
                    hash ^= (bits >> (8 * i)) & 0xFF;
                    hash *= 1099511628211UL;
                }
            }
            void MixBody(Rigidbody body)
            {
                Vector3 p = body.position; Quaternion r = body.rotation;
                Vector3 v = body.linearVelocity; Vector3 w = body.angularVelocity;
                Mix(p.x); Mix(p.y); Mix(p.z); Mix(r.x); Mix(r.y); Mix(r.z); Mix(r.w);
                Mix(v.x); Mix(v.y); Mix(v.z); Mix(w.x); Mix(w.y); Mix(w.z);
            }
            foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
                MixBody(Rig.Segments[recipe.Id].Body);
            if (Barbell.Body != null && Barbell.Body.gameObject.activeInHierarchy)
                MixBody(Barbell.Body);
            return hash;
        }

        public string Cfg(string label) => string.Format(CultureInfo.InvariantCulture,
            "load={0};{1};dt={2:R};pos_iter={3};vel_iter={4}", LoadKg, label, Dt,
            PositionIterations, VelocityIterations);
    }

    /// <summary>Per-tick physical sample of the shared athlete.</summary>
    public struct AthleteSample
    {
        public double Time;
        public Vector3 Com;
        public Vector3 ComVelocity;
        public float SupportMargin;
        public bool BothFeet;
        public float MaxSlip;
        public float PelvisY;
        public Vector3 Bar;
        public Vector3 BarVelocity;
        public float BarAngularSpeed;
        public float SaddleSeparation;
        public float SaddleOccupancy;
        public float MaxAnchorSeparation;
        public float MaxDemandFraction;
        public string MaxDemandJoint;
        public float MaxLimitProximity;
        public float MaxNominalError;
        public string MaxNominalErrorJoint;
        public float MaxAppliedError;
        public Vector3 LeftFoot;
        public Vector3 RightFoot;
        public bool CopAvailable;
        public Vector3 Cop;
        public string FailureReason;

        public static AthleteSample Capture(AthleteBenchmarkSession s)
        {
            SquatObservationSnapshot snap = s.Snapshot;
            var sample = new AthleteSample
            {
                Time = s.Runtime.CurrentTime.SimulationTimeSeconds,
                Com = V(snap.Support.SystemComWorldMeters),
                ComVelocity = V(snap.Support.SystemComVelocityWorldMetersPerSecond),
                SupportMargin = Mathf.Min(Mathf.Min(snap.Support.ComToSupportApFrontMarginM, snap.Support.ComToSupportApRearMarginM),
                    Mathf.Min(snap.Support.ComToSupportMlLeftMarginM, snap.Support.ComToSupportMlRightMarginM)),
                BothFeet = snap.LeftFoot.IsInContact && snap.RightFoot.IsInContact,
                MaxSlip = Mathf.Max(snap.LeftFoot.SlipSpeedMetersPerSecond, snap.RightFoot.SlipSpeedMetersPerSecond),
                PelvisY = snap.PelvisPositionWorldMeters.Y,
                MaxAnchorSeparation = s.MaxAnchorSeparation(),
                LeftFoot = s.Rig.Segments["left_foot"].Body.position,
                RightFoot = s.Rig.Segments["right_foot"].Body.position,
                CopAvailable = s.Adapter.Balance.HasCopEstimate,
                Cop = s.Adapter.Balance.CopEstimate,
                FailureReason = s.Adapter.FailureReason,
                MaxDemandJoint = "NONE",
                MaxNominalErrorJoint = "NONE"
            };
            if (snap.Bar.IsAvailable)
            {
                sample.Bar = V(snap.Bar.PositionWorldMeters);
                sample.BarVelocity = V(snap.Bar.LinearVelocityWorldMetersPerSecond);
                sample.BarAngularSpeed = V(snap.Bar.AngularVelocityBarRadiansPerSecond).magnitude;
            }
            else
            {
                sample.Bar = new Vector3(float.NaN, float.NaN, float.NaN);
            }
            if (s.Controller.Saddle != null)
            {
                sample.SaddleSeparation = s.Controller.Saddle.SaddleSeparationMeters;
                sample.SaddleOccupancy = s.Controller.Saddle.CurrentLinearLimitOccupancy;
            }
            foreach (PoweredJointController.PoweredJointRuntime joint in s.Rig.PoweredController.Joints)
            {
                if (!joint.Profile.HasValue || !joint.HasPostPhysicsDiagnostic)
                    continue;
                PoweredJointDiagnostic d = joint.PostPhysicsDiagnostic;
                if (d.ModeledDemand > sample.MaxDemandFraction)
                {
                    sample.MaxDemandFraction = d.ModeledDemand;
                    sample.MaxDemandJoint = joint.Id;
                }
                sample.MaxLimitProximity = Mathf.Max(sample.MaxLimitProximity, d.LimitProximity);
                if (Array.IndexOf(AthleteBenchmarkSession.SagittalJoints, joint.Id) < 0)
                    continue;
                sample.MaxAppliedError = Mathf.Max(sample.MaxAppliedError, Mathf.Abs(d.ErrorRad.x));
                if (s.Adapter.TryGetTargetComposition(joint.Id, out SquatPhysicalAdapter.JointTargetComposition comp))
                {
                    float nominalError = Mathf.Abs(AthleteBenchmarkSession.TwistLog(comp.Nominal * Quaternion.Inverse(d.ActualRelative)));
                    if (nominalError > sample.MaxNominalError)
                    {
                        sample.MaxNominalError = nominalError;
                        sample.MaxNominalErrorJoint = joint.Id;
                    }
                }
            }
            return sample;
        }

        public static Vector3 V(Vector3Value v) => new Vector3(v.X, v.Y, v.Z);

        public const string CsvHeader =
            "t,com_x,com_y,com_z,com_vx,com_vy,com_vz,support_margin_m,both_feet,max_slip_mps,pelvis_y,bar_x,bar_y,bar_z,bar_vx,bar_vy,bar_vz,bar_w,saddle_sep_m,saddle_occ,anchor_sep_m,max_demand,max_demand_joint,max_limit_prox,max_nominal_err_rad,max_nominal_err_joint,max_applied_err_rad,cop_available,cop_x,cop_z,failure";

        public string ToCsv() => string.Join(",", new[]
        {
            F(Time), F(Com.x), F(Com.y), F(Com.z), F(ComVelocity.x), F(ComVelocity.y), F(ComVelocity.z),
            F(SupportMargin), BothFeet ? "1" : "0", F(MaxSlip), F(PelvisY), F(Bar.x), F(Bar.y), F(Bar.z),
            F(BarVelocity.x), F(BarVelocity.y), F(BarVelocity.z), F(BarAngularSpeed), F(SaddleSeparation), F(SaddleOccupancy),
            F(MaxAnchorSeparation), F(MaxDemandFraction), MaxDemandJoint, F(MaxLimitProximity), F(MaxNominalError),
            MaxNominalErrorJoint, F(MaxAppliedError), CopAvailable ? "1" : "0", F(Cop.x), F(Cop.z), FailureReason
        });

        private static string F(double value) => value.ToString("G9", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Writes the exact physical model of the current athlete state for the
    /// independent dynamics oracle (Tools/Benchmarks/PhysicsOracle.py).
    /// Geometry, masses and joint frames are read from the live Rigidbodies
    /// and ConfigurableJoints; nothing is derived from the oracle.
    /// </summary>
    public static class OracleExport
    {
        public static void Write(AthleteBenchmarkSession s, string caseId, string stage, float phase, string variant)
        {
            string directory = Path.Combine(PhysicsBenchmarkRecorder.RawDirectory(), "oracle");
            Directory.CreateDirectory(directory);
            var json = new StringBuilder();
            json.Append("{\n  \"case\": ").Append(PhysicsBenchmarkRecorder.Quote(caseId));
            json.Append(",\n  \"stage\": ").Append(PhysicsBenchmarkRecorder.Quote(stage));
            json.Append(",\n  \"variant\": ").Append(PhysicsBenchmarkRecorder.Quote(variant));
            json.Append(",\n  \"load_kg\": ").Append(PhysicsBenchmarkRecorder.Number(s.LoadKg));
            json.Append(",\n  \"phase\": ").Append(PhysicsBenchmarkRecorder.Number(phase));
            json.Append(",\n  \"tick\": ").Append(s.Runtime.CurrentTime.Tick.ToString(CultureInfo.InvariantCulture));
            json.Append(",\n  \"gravity\": ").Append(Vec(Physics.gravity));
            json.Append(",\n  \"bodies\": [");
            bool first = true;
            foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
            {
                Rigidbody body = s.Rig.Segments[recipe.Id].Body;
                json.Append(first ? "\n    " : ",\n    ");
                first = false;
                json.Append("{\"id\": ").Append(PhysicsBenchmarkRecorder.Quote(recipe.Id));
                json.Append(", \"parent\": ").Append(PhysicsBenchmarkRecorder.Quote(recipe.ParentId));
                json.Append(", \"mass\": ").Append(PhysicsBenchmarkRecorder.Number(body.mass));
                json.Append(", \"com_world\": ").Append(Vec(body.worldCenterOfMass));
                json.Append(", \"rotation_world\": ").Append(Quat(body.rotation));
                json.Append(", \"inertia_principal\": ").Append(Vec(body.inertiaTensor));
                json.Append(", \"inertia_rotation\": ").Append(Quat(body.rotation * body.inertiaTensorRotation));
                json.Append(", \"velocity\": ").Append(Vec(body.linearVelocity));
                json.Append(", \"angular_velocity\": ").Append(Vec(body.angularVelocity));
                json.Append('}');
            }
            json.Append("\n  ]");
            Rigidbody bar = s.Barbell.Body;
            bool barActive = bar != null && bar.gameObject.activeInHierarchy && s.Controller.Saddle != null;
            json.Append(",\n  \"bar\": ");
            if (barActive)
            {
                json.Append("{\"mass\": ").Append(PhysicsBenchmarkRecorder.Number(bar.mass));
                json.Append(", \"com_world\": ").Append(Vec(bar.worldCenterOfMass));
                json.Append(", \"carried_by\": \"thorax\"");
                json.Append(", \"saddle_force_engine\": ").Append(Vec(s.Controller.Saddle.CurrentForceEngine));
                json.Append('}');
            }
            else
            {
                json.Append("null");
            }
            json.Append(",\n  \"joints\": [");
            first = true;
            foreach (PoweredJointController.PoweredJointRuntime joint in s.Rig.PoweredController.Joints)
            {
                ConfigurableJoint j = joint.Joint;
                json.Append(first ? "\n    " : ",\n    ");
                first = false;
                json.Append("{\"id\": ").Append(PhysicsBenchmarkRecorder.Quote(joint.Id));
                json.Append(", \"parent\": ").Append(PhysicsBenchmarkRecorder.Quote(joint.Recipe.ParentIdOrNull()));
                json.Append(", \"kind\": ").Append(PhysicsBenchmarkRecorder.Quote(joint.Recipe.Kind.ToString()));
                json.Append(", \"anchor_world\": ").Append(Vec(j.transform.TransformPoint(j.anchor)));
                json.Append(", \"connected_anchor_world\": ").Append(Vec(j.connectedBody.transform.TransformPoint(j.connectedAnchor)));
                json.Append(", \"axis_world\": ").Append(Vec(j.transform.TransformDirection(j.axis).normalized));
                json.Append(", \"powered\": ").Append(joint.Profile.HasValue ? "true" : "false");
                if (joint.Profile.HasValue && joint.HasPostPhysicsDiagnostic)
                {
                    JointFamilyProfile p = joint.Profile.Value;
                    PoweredJointDiagnostic d = joint.PostPhysicsDiagnostic;
                    Vector3 velocityError = d.TargetAngularVelocityRadS - d.ActualAngularVelocityRadS;
                    json.Append(", \"spring\": ").Append(PhysicsBenchmarkRecorder.Number(p.Spring));
                    json.Append(", \"damper\": ").Append(PhysicsBenchmarkRecorder.Number(p.Damper));
                    json.Append(", \"max_force\": ").Append(PhysicsBenchmarkRecorder.Number(d.MaximumForceNm));
                    json.Append(", \"error_x\": ").Append(PhysicsBenchmarkRecorder.Number(d.ErrorRad.x));
                    json.Append(", \"velocity_error_x\": ").Append(PhysicsBenchmarkRecorder.Number(velocityError.x));
                    json.Append(", \"modeled_twist_torque\": ").Append(PhysicsBenchmarkRecorder.Number(p.Spring * d.ErrorRad.x + p.Damper * velocityError.x));
                    json.Append(", \"solver_twist_torque\": ").Append(PhysicsBenchmarkRecorder.Number(d.SolverFlexionTorqueNm));
                    json.Append(", \"demand_fraction\": ").Append(PhysicsBenchmarkRecorder.Number(d.ModeledDemand));
                }
                json.Append('}');
            }
            json.Append("\n  ],\n  \"feet\": {\"left_contact\": ")
                .Append(s.Snapshot.LeftFoot.IsInContact ? "true" : "false")
                .Append(", \"right_contact\": ").Append(s.Snapshot.RightFoot.IsInContact ? "true" : "false")
                .Append("}\n}\n");
            File.WriteAllText(Path.Combine(directory, caseId + "." + stage + ".json"), json.ToString(), new UTF8Encoding(false));
        }

        private static string ParentIdOrNull(this PhysicalJointRecipe recipe)
        {
            foreach (PhysicalSegmentRecipe segment in PhysicalAthleteDefinition.Segments)
            {
                if (segment.Id == recipe.ChildId)
                    return segment.ParentId;
            }
            return null;
        }

        private static string Vec(Vector3 v) => "[" + PhysicsBenchmarkRecorder.Number(v.x) + ", " +
            PhysicsBenchmarkRecorder.Number(v.y) + ", " + PhysicsBenchmarkRecorder.Number(v.z) + "]";

        private static string Quat(Quaternion q) => "[" + PhysicsBenchmarkRecorder.Number(q.x) + ", " +
            PhysicsBenchmarkRecorder.Number(q.y) + ", " + PhysicsBenchmarkRecorder.Number(q.z) + ", " +
            PhysicsBenchmarkRecorder.Number(q.w) + "]";
    }
}
