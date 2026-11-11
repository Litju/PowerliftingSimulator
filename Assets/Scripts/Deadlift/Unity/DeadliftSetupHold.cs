using System;
using System.Collections.Generic;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// The only joint-command writer of the deadlift setup qualification: it
    /// commands every powered joint to hold the solved setup posture with
    /// full effort at the athlete's finite family capacities, scaled by
    /// <see cref="StrengthScale"/> per joint family (1 = canonical). It never
    /// writes a body, the bar or a force.
    /// </summary>
    public sealed class DeadliftSetupHold : IPhysicalAthleteCommandSource
    {
        private readonly IReadOnlyDictionary<string, Quaternion> _targets;
        private readonly Dictionary<string, float> _scale = new Dictionary<string, float>(StringComparer.Ordinal);

        public DeadliftSetupHold(IReadOnlyDictionary<string, Quaternion> logicalTargets)
        {
            _targets = logicalTargets ?? throw new ArgumentNullException(nameof(logicalTargets));
        }

        /// <summary>Scale on the capacity of one joint (default 1). Zero is the "no actuator" control.</summary>
        public void SetStrengthScale(string childId, float scale) => _scale[childId] = scale;

        public bool Enabled { get; set; } = true;

        public void PrepareCommands(
            PhysicalObservation previousObservation,
            SimulationTime time,
            PlayerIntentFrame intent,
            IPhysicalAthleteJointCommandSink sink)
        {
            foreach (KeyValuePair<string, Quaternion> target in _targets)
            {
                float scale = _scale.TryGetValue(target.Key, out float s) ? s : 1f;
                sink.ApplyCommand(target.Key, new JointCommand(target.Value, Vector3.zero, Enabled ? 1f : 0f, scale), time.Tick);
            }
        }
    }
}
