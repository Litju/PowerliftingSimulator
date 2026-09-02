using System;
using System.Collections.Generic;
using PowerliftingSimulator.Foundation;
using UnityEngine;

namespace PowerliftingSimulator.Athlete
{
    public enum PoweredAthleteMode : byte
    {
        Passive,
        PoweredNeutral,
        ZeroActivation,
        SelectedJointPulse
    }

    public readonly struct JointFamilyProfile
    {
        public JointFamilyProfile(string id, float spring, float damper, float baseCapacityNm, float maxTargetRateRadS)
        {
            Id = id;
            Spring = spring;
            Damper = damper;
            BaseCapacityNm = baseCapacityNm;
            MaxTargetRateRadS = maxTargetRateRadS;
        }

        public string Id { get; }
        public float Spring { get; }
        public float Damper { get; }
        public float BaseCapacityNm { get; }
        public float MaxTargetRateRadS { get; }
    }

    public readonly struct JointCommand
    {
        public JointCommand(
            Quaternion targetRelativeRotation,
            Vector3 targetRelativeAngularVelocityRadS,
            float activation,
            float capacityScale)
        {
            if (!PoweredJointController.IsFinite(targetRelativeRotation))
                throw new ArgumentOutOfRangeException(nameof(targetRelativeRotation));
            if (!PoweredJointController.IsFinite(targetRelativeAngularVelocityRadS))
                throw new ArgumentOutOfRangeException(nameof(targetRelativeAngularVelocityRadS));
            if (!float.IsFinite(activation))
                throw new ArgumentOutOfRangeException(nameof(activation));
            if (!float.IsFinite(capacityScale) || capacityScale < 0f)
                throw new ArgumentOutOfRangeException(nameof(capacityScale));

            TargetRelativeRotation = PoweredJointController.NormalizeCanonical(targetRelativeRotation);
            TargetRelativeAngularVelocityRadS = targetRelativeAngularVelocityRadS;
            Activation = Mathf.Clamp01(activation);
            CapacityScale = capacityScale;
        }

        public Quaternion TargetRelativeRotation { get; }
        public Vector3 TargetRelativeAngularVelocityRadS { get; }
        public float Activation { get; }
        public float CapacityScale { get; }

        public static JointCommand Neutral(float activation, float capacityScale = 1f) =>
            new JointCommand(Quaternion.identity, Vector3.zero, activation, capacityScale);
    }

    public readonly struct PoweredJointDiagnostic
    {
        public PoweredJointDiagnostic(
            Quaternion requestedTarget,
            Quaternion appliedTarget,
            Quaternion actualRelative,
            Vector3 errorRad,
            Vector3 targetAngularVelocityRadS,
            Vector3 actualAngularVelocityRadS,
            float maximumForceNm,
            float activation,
            float capacityScale,
            float modeledDemand,
            float limitProximity)
        {
            RequestedTarget = requestedTarget;
            AppliedTarget = appliedTarget;
            ActualRelative = actualRelative;
            ErrorRad = errorRad;
            TargetAngularVelocityRadS = targetAngularVelocityRadS;
            ActualAngularVelocityRadS = actualAngularVelocityRadS;
            MaximumForceNm = maximumForceNm;
            Activation = activation;
            CapacityScale = capacityScale;
            ModeledDemand = modeledDemand;
            LimitProximity = limitProximity;
        }

        public Quaternion RequestedTarget { get; }
        public Quaternion AppliedTarget { get; }
        public Quaternion ActualRelative { get; }
        public Vector3 ErrorRad { get; }
        public Vector3 TargetAngularVelocityRadS { get; }
        public Vector3 ActualAngularVelocityRadS { get; }
        public float MaximumForceNm { get; }
        public float Activation { get; }
        public float CapacityScale { get; }
        public float ModeledDemand { get; }
        public float LimitProximity { get; }
    }

    public sealed class PoweredJointController
    {
        public const string WriterId = "PoweredJointController";
        public const string CalibrationVersion = "GAM7_CONFIGURABLE_JOINT_LOCAL_V1";
        public const string SourceClass = "GAME_CALIBRATION";
        public const float PulseRadians = 20f * Mathf.Deg2Rad;

        private static readonly JointFamilyProfile[] Profiles =
        {
            new JointFamilyProfile("ankle", 650f, 70f, 180f, 2.0f),
            new JointFamilyProfile("knee", 800f, 80f, 300f, 2.5f),
            new JointFamilyProfile("hip", 900f, 90f, 360f, 2.2f),
            new JointFamilyProfile("trunk", 800f, 85f, 260f, 1.8f),
            new JointFamilyProfile("shoulder", 500f, 55f, 130f, 2.5f),
            new JointFamilyProfile("elbow", 450f, 45f, 100f, 3.0f),
            new JointFamilyProfile("wrist", 250f, 30f, 45f, 2.5f)
        };

        private static readonly string[] PulseJointIds =
        {
            "left_shank", "right_shank", "left_forearm", "right_forearm"
        };

        private readonly List<PoweredJointRuntime> _joints = new List<PoweredJointRuntime>();
        private readonly Dictionary<string, PoweredJointRuntime> _jointById =
            new Dictionary<string, PoweredJointRuntime>(StringComparer.Ordinal);
        private int _selectedPulseJointIndex;

        public PoweredJointController(IReadOnlyList<PhysicalAthleteRig.JointRuntime> joints)
        {
            if (joints == null)
                throw new ArgumentNullException(nameof(joints));

            foreach (PhysicalAthleteRig.JointRuntime runtime in joints)
            {
                JointFamilyProfile? profile = ResolveProfile(runtime.Recipe.Family);
                var powered = new PoweredJointRuntime(runtime, profile);
                _joints.Add(powered);
                _jointById.Add(runtime.Recipe.ChildId, powered);
            }

            Reset(PoweredAthleteMode.Passive);
        }

        public PoweredAthleteMode Mode { get; private set; }
        public float GlobalActivation { get; private set; }
        public string SelectedJointId => PulseJointIds[_selectedPulseJointIndex];
        public int PoweredJointCount => ProfilesForRuntimeCount(true);
        public int PassiveJointCount => ProfilesForRuntimeCount(false);
        public static IReadOnlyList<JointFamilyProfile> FamilyProfiles => Profiles;
        public IReadOnlyList<PoweredJointRuntime> Joints => _joints;

        public void Reset(PoweredAthleteMode mode)
        {
            Mode = mode;
            GlobalActivation = mode == PoweredAthleteMode.Passive || mode == PoweredAthleteMode.ZeroActivation ? 0f : 1f;
            foreach (PoweredJointRuntime joint in _joints)
                joint.Reset();
            ApplyInitialDriveState();
        }

        public void SetGlobalActivation(float activation)
        {
            if (!float.IsFinite(activation))
                throw new ArgumentOutOfRangeException(nameof(activation));
            GlobalActivation = Mathf.Clamp01(activation);
            if (Mode == PoweredAthleteMode.ZeroActivation && GlobalActivation > 0f)
                Mode = PoweredAthleteMode.PoweredNeutral;
            foreach (PoweredJointRuntime joint in _joints)
            {
                JointCommand command = joint.RequestedCommand;
                joint.RequestedCommand = new JointCommand(
                    command.TargetRelativeRotation,
                    command.TargetRelativeAngularVelocityRadS,
                    GlobalActivation,
                    command.CapacityScale);
            }
        }

        public void SelectNextPulseJoint(int direction)
        {
            int count = PulseJointIds.Length;
            _selectedPulseJointIndex = (_selectedPulseJointIndex + Math.Sign(direction) + count) % count;
        }

        public void SetPulse(bool positive)
        {
            Mode = PoweredAthleteMode.SelectedJointPulse;
            GlobalActivation = 1f;
            foreach (PoweredJointRuntime joint in _joints)
                joint.RequestedCommand = JointCommand.Neutral(GlobalActivation);

            float angle = positive ? PulseRadians : -PulseRadians;
            _jointById[SelectedJointId].RequestedCommand = new JointCommand(
                Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.right),
                Vector3.zero,
                GlobalActivation,
                1f);
        }

        public void SetJointCommand(string childId, JointCommand command)
        {
            PoweredJointRuntime joint = GetJoint(childId);
            if (!joint.Profile.HasValue)
                throw new InvalidOperationException($"Joint '{childId}' is passive in GAM-7.");
            Mode = PoweredAthleteMode.SelectedJointPulse;
            joint.RequestedCommand = command;
        }

        public PoweredJointRuntime GetJoint(string childId) =>
            _jointById.TryGetValue(childId, out PoweredJointRuntime joint)
                ? joint
                : throw new ArgumentException($"Unknown physical joint '{childId}'.", nameof(childId));

        public void Step(SimulationTime _, PlayerIntentFrame __)
        {
            if (Mode == PoweredAthleteMode.PoweredNeutral || Mode == PoweredAthleteMode.ZeroActivation)
            {
                foreach (PoweredJointRuntime joint in _joints)
                    joint.RequestedCommand = JointCommand.Neutral(GlobalActivation);
            }

            float stepSeconds = (float)SimulationConstants.FixedDeltaTimeSeconds;
            foreach (PoweredJointRuntime joint in _joints)
            {
                if (!joint.Profile.HasValue)
                {
                    WritePassiveJoint(joint.Joint);
                    continue;
                }

                JointCommand command = joint.RequestedCommand;
                JointFamilyProfile profile = joint.Profile.Value;
                joint.AppliedTarget = RateLimitShortestArc(
                    joint.AppliedTarget,
                    command.TargetRelativeRotation,
                    profile.MaxTargetRateRadS * stepSeconds);

                Vector3 targetVelocity = Vector3.ClampMagnitude(
                    command.TargetRelativeAngularVelocityRadS,
                    profile.MaxTargetRateRadS);
                float activation = Mode == PoweredAthleteMode.Passive ? 0f : command.Activation;
                float maximumForce = profile.BaseCapacityNm * command.CapacityScale * activation;
                if (!float.IsFinite(maximumForce))
                    throw new InvalidOperationException($"Joint '{joint.Id}' produced non-finite authority.");

                WritePoweredJoint(joint, targetVelocity, maximumForce);
                joint.Diagnostic = BuildDiagnostic(joint, targetVelocity, maximumForce, activation, command.CapacityScale);
            }
        }

        public static Quaternion ToUnityTargetRotation(Quaternion targetRelativeInJointFrame) =>
            NormalizeCanonical(Quaternion.Inverse(NormalizeCanonical(targetRelativeInJointFrame)));

        public static Quaternion RateLimitShortestArc(Quaternion previous, Quaternion requested, float maxDeltaRadians)
        {
            previous = NormalizeCanonical(previous);
            requested = NormalizeCanonical(requested);
            if (!float.IsFinite(maxDeltaRadians) || maxDeltaRadians < 0f)
                throw new ArgumentOutOfRangeException(nameof(maxDeltaRadians));

            float angleRadians = Quaternion.Angle(previous, requested) * Mathf.Deg2Rad;
            if (angleRadians <= maxDeltaRadians || angleRadians <= 0.000001f)
                return requested;
            return NormalizeCanonical(Quaternion.Slerp(previous, requested, maxDeltaRadians / angleRadians));
        }

        public static Quaternion NormalizeCanonical(Quaternion value)
        {
            if (!IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            float magnitudeSquared = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            if (magnitudeSquared <= 0.000000000001f)
                throw new ArgumentOutOfRangeException(nameof(value));

            float inverseMagnitude = 1f / Mathf.Sqrt(magnitudeSquared);
            value = new Quaternion(
                value.x * inverseMagnitude,
                value.y * inverseMagnitude,
                value.z * inverseMagnitude,
                value.w * inverseMagnitude);
            if (value.w < 0f ||
                (Mathf.Abs(value.w) <= 0.000001f && (value.x < 0f ||
                (Mathf.Abs(value.x) <= 0.000001f && (value.y < 0f ||
                (Mathf.Abs(value.y) <= 0.000001f && value.z < 0f))))))
                value = new Quaternion(-value.x, -value.y, -value.z, -value.w);
            return value;
        }

        public static bool IsFinite(Quaternion value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);

        public static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        public static bool IsValidPoweredDrive(JointDrive drive) =>
            !drive.useAcceleration && float.IsFinite(drive.maximumForce) && drive.maximumForce >= 0f;

        public static void ValidatePoweredDrive(string jointId, JointDrive drive)
        {
            if (drive.useAcceleration)
                throw new InvalidOperationException($"Joint '{jointId}' uses acceleration authority; powered drives require useAcceleration=false.");
            if (!float.IsFinite(drive.maximumForce))
                throw new InvalidOperationException($"Joint '{jointId}' has non-finite maximumForce authority.");
            if (drive.maximumForce < 0f)
                throw new InvalidOperationException($"Joint '{jointId}' has negative maximumForce authority.");
        }

        private void ApplyInitialDriveState()
        {
            foreach (PoweredJointRuntime joint in _joints)
            {
                if (!joint.Profile.HasValue || Mode == PoweredAthleteMode.Passive)
                    WritePassiveJoint(joint.Joint);
                else
                    WritePoweredJoint(joint, Vector3.zero, 0f);
            }
        }

        private static void WritePassiveJoint(ConfigurableJoint joint)
        {
            joint.configuredInWorldSpace = false;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.targetRotation = Quaternion.identity;
            joint.targetAngularVelocity = Vector3.zero;
            joint.angularXDrive = ZeroDrive();
            joint.angularYZDrive = ZeroDrive();
            joint.slerpDrive = ZeroDrive();
        }

        private static void WritePoweredJoint(PoweredJointRuntime joint, Vector3 targetVelocity, float maximumForce)
        {
            ConfigurableJoint configurable = joint.Joint;
            JointFamilyProfile profile = joint.Profile.Value;
            configurable.configuredInWorldSpace = false;
            configurable.targetRotation = ToUnityTargetRotation(joint.AppliedTarget);
            configurable.targetAngularVelocity = -targetVelocity;

            if (joint.Recipe.Kind == PhysicalJointKind.Hinge)
            {
                configurable.rotationDriveMode = RotationDriveMode.XYAndZ;
                configurable.angularXDrive = Drive(profile, maximumForce);
                configurable.angularYZDrive = ZeroDrive();
                configurable.slerpDrive = ZeroDrive();
            }
            else
            {
                configurable.rotationDriveMode = RotationDriveMode.Slerp;
                configurable.angularXDrive = ZeroDrive();
                configurable.angularYZDrive = ZeroDrive();
                configurable.slerpDrive = Drive(profile, maximumForce);
            }
        }

        private static JointDrive Drive(JointFamilyProfile profile, float maximumForce) => new JointDrive
        {
            positionSpring = profile.Spring,
            positionDamper = profile.Damper,
            maximumForce = maximumForce,
            useAcceleration = false
        };

        private static JointDrive ZeroDrive() => new JointDrive
        {
            positionSpring = 0f,
            positionDamper = 0f,
            maximumForce = 0f,
            useAcceleration = false
        };

        private static JointFamilyProfile? ResolveProfile(string family)
        {
            string profileId = family == "lumbar" ? "trunk" : family;
            foreach (JointFamilyProfile profile in Profiles)
            {
                if (string.Equals(profile.Id, profileId, StringComparison.Ordinal))
                    return profile;
            }
            return null;
        }

        private static PoweredJointDiagnostic BuildDiagnostic(
            PoweredJointRuntime joint,
            Vector3 targetVelocity,
            float maximumForce,
            float activation,
            float capacityScale)
        {
            Quaternion currentParentToChild = Quaternion.Inverse(joint.Joint.connectedBody.rotation) * joint.Joint.transform.rotation;
            Quaternion childNeutralDelta = Quaternion.Inverse(joint.NeutralParentToChild) * currentParentToChild;
            Quaternion actual = NormalizeCanonical(Quaternion.Inverse(joint.JointSpace) * childNeutralDelta * joint.JointSpace);
            Quaternion error = NormalizeCanonical(joint.AppliedTarget * Quaternion.Inverse(actual));
            Vector3 errorRad = QuaternionLog(error);

            Vector3 relativeWorld = joint.Joint.GetComponent<Rigidbody>().angularVelocity - joint.Joint.connectedBody.angularVelocity;
            Vector3 relativeChild = Quaternion.Inverse(joint.Joint.transform.rotation) * relativeWorld;
            Vector3 actualVelocity = Quaternion.Inverse(joint.JointSpace) * relativeChild;
            JointFamilyProfile profile = joint.Profile.Value;
            Vector3 conceptualTorque = profile.Spring * errorRad + profile.Damper * (targetVelocity - actualVelocity);
            float demand = conceptualTorque.magnitude / Mathf.Max(maximumForce, 0.001f);
            float xDegrees = SignedTwistDegrees(actual, Vector3.right);
            float limit = xDegrees >= 0f ? Mathf.Max(0.001f, joint.Recipe.HighDegrees) : Mathf.Max(0.001f, -joint.Recipe.LowDegrees);
            float proximity = Mathf.Clamp01(Mathf.Abs(xDegrees) / limit);
            return new PoweredJointDiagnostic(
                joint.RequestedCommand.TargetRelativeRotation,
                joint.AppliedTarget,
                actual,
                errorRad,
                targetVelocity,
                actualVelocity,
                maximumForce,
                activation,
                capacityScale,
                demand,
                proximity);
        }

        private static Vector3 QuaternionLog(Quaternion quaternion)
        {
            quaternion = NormalizeCanonical(quaternion);
            float vectorMagnitude = Mathf.Sqrt(quaternion.x * quaternion.x + quaternion.y * quaternion.y + quaternion.z * quaternion.z);
            if (vectorMagnitude <= 0.000001f)
                return Vector3.zero;
            float angle = 2f * Mathf.Atan2(vectorMagnitude, Mathf.Clamp(quaternion.w, -1f, 1f));
            return new Vector3(quaternion.x, quaternion.y, quaternion.z) * (angle / vectorMagnitude);
        }

        private static float SignedTwistDegrees(Quaternion rotation, Vector3 axis)
        {
            Vector3 vector = new Vector3(rotation.x, rotation.y, rotation.z);
            Vector3 projection = Vector3.Project(vector, axis);
            Quaternion twist = NormalizeCanonical(new Quaternion(projection.x, projection.y, projection.z, rotation.w));
            twist.ToAngleAxis(out float angle, out Vector3 twistAxis);
            return angle * Mathf.Sign(Vector3.Dot(twistAxis, axis));
        }

        private int ProfilesForRuntimeCount(bool powered)
        {
            int count = 0;
            foreach (PoweredJointRuntime joint in _joints)
            {
                if (joint.Profile.HasValue == powered)
                    count++;
            }
            return count;
        }

        public sealed class PoweredJointRuntime
        {
            internal PoweredJointRuntime(PhysicalAthleteRig.JointRuntime runtime, JointFamilyProfile? profile)
            {
                Runtime = runtime;
                Profile = profile;
                NeutralParentToChild = Quaternion.Inverse(runtime.Joint.connectedBody.rotation) * runtime.Joint.transform.rotation;
                Vector3 right = runtime.Joint.axis.normalized;
                Vector3 forward = Vector3.Cross(right, runtime.Joint.secondaryAxis).normalized;
                Vector3 up = Vector3.Cross(forward, right).normalized;
                JointSpace = Quaternion.LookRotation(forward, up);
                Reset();
            }

            public string Id => Runtime.Recipe.ChildId;
            public PhysicalJointRecipe Recipe => Runtime.Recipe;
            public ConfigurableJoint Joint => Runtime.Joint;
            public JointFamilyProfile? Profile { get; }
            public Quaternion NeutralParentToChild { get; }
            public Quaternion JointSpace { get; }
            public JointCommand RequestedCommand { get; internal set; }
            public Quaternion AppliedTarget { get; internal set; }
            public PoweredJointDiagnostic Diagnostic { get; internal set; }

            internal PhysicalAthleteRig.JointRuntime Runtime { get; }

            internal void Reset()
            {
                RequestedCommand = JointCommand.Neutral(0f);
                AppliedTarget = Quaternion.identity;
                Diagnostic = default;
            }
        }
    }
}
