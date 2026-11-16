using System;
using System.Collections.Generic;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70: the referee's view of one deadlift attempt, measured from the
    /// bodies only (never from the controller's intent or reference): bar
    /// height and velocity, plate load, bar-on-leg contact force, knee flexion
    /// from the hip-knee-ankle joint centres, the hip-to-shoulder line angle
    /// above horizontal, the shoulders' fore-aft position relative to the
    /// hips, each foot's horizontal travel from its starting position, and
    /// whether both grips are attached. Read-only.
    /// </summary>
    public sealed class DeadliftAttemptObserver
    {
        private readonly PhysicalAthleteRig _rig;
        private readonly Rigidbody _bar;
        private readonly Func<float> _plateN, _barOnLegN;
        private readonly Func<bool> _gripL, _gripR;
        private readonly Dictionary<string, ConfigurableJoint> _joints = new Dictionary<string, ConfigurableJoint>(StringComparer.Ordinal);
        private readonly Vector3 _footL0, _footR0;

        public DeadliftAttemptObserver(PhysicalAthleteRig rig, Rigidbody bar, Func<float> plateN, Func<float> barOnLegN, Func<bool> gripLeft, Func<bool> gripRight)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _bar = bar ?? throw new ArgumentNullException(nameof(bar));
            _plateN = plateN ?? throw new ArgumentNullException(nameof(plateN));
            _barOnLegN = barOnLegN ?? (() => 0f);
            _gripL = gripLeft ?? (() => false);
            _gripR = gripRight ?? (() => false);
            foreach (PhysicalAthleteRig.JointRuntime j in rig.Joints)
                _joints[j.Recipe.ChildId] = j.Joint;
            _footL0 = rig.Segments["left_foot"].Body.worldCenterOfMass;
            _footR0 = rig.Segments["right_foot"].Body.worldCenterOfMass;
        }

        private Vector3 Anchor(string childId)
        {
            ConfigurableJoint j = _joints[childId];
            return j.transform.TransformPoint(j.anchor);
        }

        public float KneeFlexionDeg(string side)
        {
            Vector3 hip = Anchor(side + "_thigh"), knee = Anchor(side + "_shank"), ankle = Anchor(side + "_foot");
            return 180f - Vector3.Angle(hip - knee, ankle - knee);
        }

        public float TorsoDeg
        {
            get
            {
                Vector3 hip = 0.5f * (Anchor("left_thigh") + Anchor("right_thigh"));
                Vector3 sh = 0.5f * (Anchor("left_upper_arm") + Anchor("right_upper_arm"));
                Vector3 t = sh - hip;
                return Mathf.Atan2(t.y, Mathf.Abs(t.z) < 1e-6f ? 1e-6f : t.z) * Mathf.Rad2Deg;
            }
        }

        /// <summary>Shoulder joint centres ahead (+Z, the athlete faces +Z) of the hip joint centres, metres.</summary>
        public float ShoulderAheadOfHipM =>
            0.5f * (Anchor("left_upper_arm").z + Anchor("right_upper_arm").z) - 0.5f * (Anchor("left_thigh").z + Anchor("right_thigh").z);

        private static float Horizontal(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        public DeadliftObservation Observe(ulong tick, double timeSeconds)
        {
            Vector3 v = _bar.linearVelocity;
            return new DeadliftObservation(tick, timeSeconds, _bar.worldCenterOfMass.y, v.y, v.magnitude, _plateN(), _barOnLegN(),
                KneeFlexionDeg("left"), KneeFlexionDeg("right"), TorsoDeg, ShoulderAheadOfHipM,
                Horizontal(_rig.Segments["left_foot"].Body.worldCenterOfMass, _footL0),
                Horizontal(_rig.Segments["right_foot"].Body.worldCenterOfMass, _footR0),
                _gripL(), _gripR());
        }
    }
}
