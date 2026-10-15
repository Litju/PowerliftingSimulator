using System;
using System.Collections.Generic;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    public enum SquatPlayerGamePhase : byte
    {
        LOAD,
        SETUP,
        UNRACK,
        WALKOUT_STEP_1,
        WALKOUT_STEP_2,
        SETTLE,
        PHYSICS_HANDOFF,
        JUDGED_SQUAT,
        LOCKOUT,
        RERACK,
        RESULT,
        RETRY
    }

    /// <summary>Owns the animation/physics authority switches for the player-facing squat flow.</summary>
    public sealed class SquatHybridGameFlow
    {
        private const float WalkoutDurationSeconds = 2.9f;
        private const float RerackDurationSeconds = 2.4f;
        private const float RequiredReadySeconds = 0.2f;
        private const float FirstStepStartSeconds = 0.8f;
        private const float FirstStepEndSeconds = 1.45f;
        private const float SecondStepStartSeconds = 1.45f;
        private const float SecondStepEndSeconds = 2.1f;
        private const float SettleStartSeconds = 2.1f;

        private readonly Transform _owner;
        private readonly PhysicalAthleteRig _athleteRig;
        private readonly PhysicalBarbell _barbell;
        private readonly SquatPhysicalAdapter _adapter;
        private readonly SquatObservationCollector _collector;
        private readonly SquatAttemptOrchestrator _orchestrator;
        private readonly List<PhysicalAthleteRig.SegmentRuntime> _segments;
        private readonly Dictionary<string, int> _segmentIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Vector3[] _basePositions;
        private readonly Quaternion[] _baseRotations;

        private Animator _animator;
        private SquatRootMotionRelay _rootMotionRelay;
        private Vector3 _barLocalPosition;
        private Quaternion _barLocalRotation;
        private Vector3 _rootMotionDisplacement;
        private float _phaseElapsedSeconds;
        private int _readySamples;
        private ulong _lastReadyTick = ulong.MaxValue;
        private bool _locomotionAuthority;
        private bool _walkoutStarted;
        private bool _handoffPending;

        public SquatHybridGameFlow(
            Transform owner,
            PhysicalAthleteRig athleteRig,
            PhysicalBarbell barbell,
            SquatPhysicalAdapter adapter,
            SquatObservationCollector collector,
            SquatAttemptOrchestrator orchestrator,
            RuntimeAnimatorController locomotionController)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _athleteRig = athleteRig ?? throw new ArgumentNullException(nameof(athleteRig));
            _barbell = barbell ?? throw new ArgumentNullException(nameof(barbell));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));

            _segments = new List<PhysicalAthleteRig.SegmentRuntime>(athleteRig.Segments.Values);
            _segments.Sort((left, right) => string.CompareOrdinal(left.Recipe.Id, right.Recipe.Id));
            _basePositions = new Vector3[_segments.Count];
            _baseRotations = new Quaternion[_segments.Count];
            for (int index = 0; index < _segments.Count; index++)
                _segmentIndexes.Add(_segments[index].Recipe.Id, index);

            Phase = SquatPlayerGamePhase.LOAD;
            if (locomotionController != null)
                CreateRootMotionAnimator(locomotionController);
        }

        public SquatPlayerGamePhase Phase { get; private set; }
        public bool CanBeginSetup => Phase == SquatPlayerGamePhase.LOAD && _animator != null && _readySamples >= RequiredReadySampleCount;
        public int ReadySamples => _readySamples;
        public float WalkoutDistanceMeters => _rootMotionDisplacement.magnitude;
        public float MaximumHandoffPoseErrorMeters { get; private set; }
        public float MaximumHandoffBarAngularErrorDegrees { get; private set; }
        public bool HasLocomotionAuthority => _locomotionAuthority;

        private int RequiredReadySampleCount => Mathf.Max(1, Mathf.CeilToInt(RequiredReadySeconds / (float)SimulationConstants.FixedDeltaTimeSeconds));

        public void ResetReadiness()
        {
            _readySamples = 0;
            _lastReadyTick = ulong.MaxValue;
        }

        public bool BeginSetup()
        {
            if (Phase != SquatPlayerGamePhase.LOAD || _animator == null ||
                _adapter.Saddle == null || !_adapter.Saddle.IsAttached || !CanBeginSetup)
                return false;

            CaptureBodyPose();
            CaptureBarPresentationAttachment();
            _athleteRig.SetCommandSource(null);
            SetKinematicAuthority(true);
            _locomotionAuthority = true;
            _rootMotionDisplacement = Vector3.zero;
            _walkoutStarted = false;
            _handoffPending = false;
            _phaseElapsedSeconds = 0f;
            Phase = SquatPlayerGamePhase.SETUP;
            return true;
        }

        public bool BeginWalkout()
        {
            if (Phase != SquatPlayerGamePhase.SETUP || !_locomotionAuthority || _animator == null)
                return false;

            _rootMotionDisplacement = Vector3.zero;
            _phaseElapsedSeconds = 0f;
            _walkoutStarted = true;
            _animator.Play("Base Layer.WALKOUT_SEQUENCE", 0, 0f);
            Phase = SquatPlayerGamePhase.UNRACK;
            return true;
        }

        public void Update(float deltaTimeSeconds)
        {
            UpdateReadyWindow();
            if (_walkoutStarted && IsWalkoutPhase(Phase))
            {
                _phaseElapsedSeconds += Mathf.Max(0f, deltaTimeSeconds);
                if (_phaseElapsedSeconds < FirstStepStartSeconds)
                    Phase = SquatPlayerGamePhase.UNRACK;
                else if (_phaseElapsedSeconds < FirstStepEndSeconds)
                    Phase = SquatPlayerGamePhase.WALKOUT_STEP_1;
                else if (_phaseElapsedSeconds < SecondStepEndSeconds)
                    Phase = SquatPlayerGamePhase.WALKOUT_STEP_2;
                else
                    Phase = SquatPlayerGamePhase.SETTLE;
                return;
            }

            if (_handoffPending)
            {
                _handoffPending = false;
                Rigidbody barBody = _barbell.Body != null && _barbell.Body.gameObject.activeInHierarchy
                    ? _barbell.Body
                    : null;
                Vector3 barPositionBeforeHandoff = barBody == null ? Vector3.zero : barBody.position;
                Quaternion barRotationBeforeHandoff = barBody == null ? Quaternion.identity : barBody.rotation;
                SetKinematicAuthority(false);
                _athleteRig.SetCommandSource(_adapter);
                MaximumHandoffPoseErrorMeters = 0f;
                MaximumHandoffBarAngularErrorDegrees = 0f;
                for (int index = 0; index < _segments.Count; index++)
                    MaximumHandoffPoseErrorMeters = Mathf.Max(
                        MaximumHandoffPoseErrorMeters,
                        Vector3.Distance(_segments[index].Body.position, _basePositions[index] + _rootMotionDisplacement));
                if (barBody != null)
                {
                    MaximumHandoffPoseErrorMeters = Mathf.Max(
                        MaximumHandoffPoseErrorMeters,
                        Vector3.Distance(barBody.position, barPositionBeforeHandoff));
                    MaximumHandoffBarAngularErrorDegrees = Quaternion.Angle(barBody.rotation, barRotationBeforeHandoff);
                }
                _orchestrator.BeginAttempt();
                Phase = SquatPlayerGamePhase.JUDGED_SQUAT;
                return;
            }

            if (Phase == SquatPlayerGamePhase.JUDGED_SQUAT || Phase == SquatPlayerGamePhase.LOCKOUT)
            {
                if (_orchestrator.Lifecycle.State == SquatAttemptLifecycleState.PHYSICAL_TERMINAL_CONDITION &&
                    _orchestrator.Lifecycle.TerminalReason == SquatAttemptTerminalReason.PHYSICAL_LOCKOUT)
                    Phase = SquatPlayerGamePhase.LOCKOUT;

                SquatAttemptRecord record = _orchestrator.Record;
                if (record != null)
                {
                    if (record.TerminalReason == SquatAttemptTerminalReason.PHYSICAL_LOCKOUT &&
                        record.PhysicalFailureOutcome == SquatFailureResultKind.NO_PHYSICAL_FAILURE)
                        BeginRerack();
                    else
                        Phase = SquatPlayerGamePhase.RESULT;
                    return;
                }

                return;
            }

            if (Phase == SquatPlayerGamePhase.RERACK)
            {
                _phaseElapsedSeconds += Mathf.Max(0f, deltaTimeSeconds);
            }
        }

        public void LateUpdate()
        {
            if (!_locomotionAuthority)
                return;

            ApplyKinematicPose();
            ApplyStepPose();
            FollowBarPresentationAttachment();

            if (_walkoutStarted && _phaseElapsedSeconds >= WalkoutDurationSeconds && IsWalkoutPhase(Phase))
            {
                _locomotionAuthority = false;
                _handoffPending = true;
                Phase = SquatPlayerGamePhase.PHYSICS_HANDOFF;
            }
            else if (Phase == SquatPlayerGamePhase.RERACK && _phaseElapsedSeconds >= RerackDurationSeconds)
            {
                _locomotionAuthority = false;
                Phase = SquatPlayerGamePhase.RESULT;
            }
        }

        public void ReceiveRootMotion(Vector3 deltaPosition)
        {
            if (_locomotionAuthority && IsFinite(deltaPosition))
                _rootMotionDisplacement += deltaPosition;
        }

        public void MarkRetry()
        {
            Phase = SquatPlayerGamePhase.RETRY;
        }

        private void CreateRootMotionAnimator(RuntimeAnimatorController controller)
        {
            var animatorObject = new GameObject("GAM63 Root Motion Animator")
            {
                hideFlags = HideFlags.HideInHierarchy
            };
            animatorObject.transform.SetParent(_owner, false);
            animatorObject.transform.rotation = _athleteRig.ReferenceAnimator.transform.rotation;
            _animator = animatorObject.AddComponent<Animator>();
            _animator.runtimeAnimatorController = controller;
            _animator.applyRootMotion = true;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.updateMode = AnimatorUpdateMode.Normal;
            _rootMotionRelay = animatorObject.AddComponent<SquatRootMotionRelay>();
            _rootMotionRelay.Bind(ReceiveRootMotion);
            _animator.Play("Base Layer.SETUP_IDLE", 0, 0f);
        }

        private void UpdateReadyWindow()
        {
            if (Phase != SquatPlayerGamePhase.LOAD || !_collector.HasLastSnapshot)
                return;

            SquatObservationSnapshot snapshot = _collector.LastSnapshot;
            if (snapshot.SimulationTick == _lastReadyTick)
                return;
            _lastReadyTick = snapshot.SimulationTick;
            _readySamples = _orchestrator.EvaluateStartPredicate(snapshot).OverallStartCandidate
                ? _readySamples + 1
                : 0;
        }

        private void BeginRerack()
        {
            CaptureBodyPose();
            CaptureBarPresentationAttachment();
            _athleteRig.SetCommandSource(null);
            SetKinematicAuthority(true);
            _locomotionAuthority = true;
            _rootMotionDisplacement = Vector3.zero;
            _phaseElapsedSeconds = 0f;
            _animator.Play("Base Layer.RERACK_SEQUENCE", 0, 0f);
            Phase = SquatPlayerGamePhase.RERACK;
        }

        private void CaptureBodyPose()
        {
            for (int index = 0; index < _segments.Count; index++)
            {
                Rigidbody body = _segments[index].Body;
                _basePositions[index] = body.position;
                _baseRotations[index] = body.rotation;
            }
        }

        private void CaptureBarPresentationAttachment()
        {
            if (_barbell == null || _barbell.Body == null || !_barbell.Body.gameObject.activeInHierarchy ||
                !_athleteRig.Segments.TryGetValue("thorax", out PhysicalAthleteRig.SegmentRuntime thorax))
                return;

            Transform carrier = thorax.Body.transform;
            _barLocalPosition = carrier.InverseTransformPoint(_barbell.Body.position);
            _barLocalRotation = Quaternion.Inverse(carrier.rotation) * _barbell.Body.rotation;
        }

        private void ApplyKinematicPose()
        {
            Vector3 displacement = _rootMotionDisplacement;
            for (int index = 0; index < _segments.Count; index++)
            {
                Rigidbody body = _segments[index].Body;
                body.position = _basePositions[index] + displacement;
                body.rotation = _baseRotations[index];
            }
        }

        private void ApplyStepPose()
        {
            if (Phase != SquatPlayerGamePhase.WALKOUT_STEP_1 && Phase != SquatPlayerGamePhase.WALKOUT_STEP_2)
                return;

            bool left = Phase == SquatPlayerGamePhase.WALKOUT_STEP_1;
            float start = left ? FirstStepStartSeconds : SecondStepStartSeconds;
            float end = left ? FirstStepEndSeconds : SecondStepEndSeconds;
            float progress = Mathf.Clamp01((_phaseElapsedSeconds - start) / (end - start));
            float flexionDegrees = 34f * Mathf.Sin(progress * Mathf.PI);
            ApplyKneeFlexion(left ? "left" : "right", flexionDegrees);
        }

        private void ApplyKneeFlexion(string side, float flexionDegrees)
        {
            if (!_segmentIndexes.TryGetValue(side + "_thigh", out int thighIndex) ||
                !_segmentIndexes.TryGetValue(side + "_shank", out int shankIndex) ||
                !_segmentIndexes.TryGetValue(side + "_foot", out int footIndex))
                return;

            Rigidbody thigh = _segments[thighIndex].Body;
            Rigidbody shank = _segments[shankIndex].Body;
            Rigidbody foot = _segments[footIndex].Body;
            ConfigurableJoint knee = shank.GetComponent<ConfigurableJoint>();
            ConfigurableJoint ankle = foot.GetComponent<ConfigurableJoint>();
            if (knee == null || ankle == null || knee.connectedBody == null)
                return;

            Vector3 displacement = _rootMotionDisplacement;
            Vector3 thighPosition = _basePositions[thighIndex] + displacement;
            Quaternion thighRotation = _baseRotations[thighIndex];
            Vector3 shankPosition = _basePositions[shankIndex] + displacement;
            Quaternion shankRotation = _baseRotations[shankIndex];
            Vector3 footPosition = _basePositions[footIndex] + displacement;
            Quaternion footRotation = _baseRotations[footIndex];

            Vector3 pivot = thighPosition + thighRotation * knee.connectedAnchor;
            Vector3 axis = (shankRotation * knee.axis).normalized;
            Vector3 ankleOffset = shankRotation * ankle.connectedAnchor;
            Vector3 positiveStep = Quaternion.AngleAxis(1f, axis) * ankleOffset;
            float flexionSign = positiveStep.y >= ankleOffset.y ? 1f : -1f;
            Quaternion flexedRotation = Quaternion.AngleAxis(flexionSign * flexionDegrees, axis) * shankRotation;
            Vector3 flexedPosition = pivot - flexedRotation * knee.anchor;
            Vector3 anklePivot = flexedPosition + flexedRotation * ankle.connectedAnchor;

            shank.position = flexedPosition;
            shank.rotation = flexedRotation;
            foot.position = anklePivot - footRotation * ankle.anchor;
            foot.rotation = footRotation;
        }

        private void FollowBarPresentationAttachment()
        {
            if (_barbell == null || _barbell.Body == null || !_barbell.Body.gameObject.activeInHierarchy ||
                !_athleteRig.Segments.TryGetValue("thorax", out PhysicalAthleteRig.SegmentRuntime thorax))
                return;

            Transform carrier = thorax.Body.transform;
            _barbell.Body.position = carrier.TransformPoint(_barLocalPosition);
            _barbell.Body.rotation = carrier.rotation * _barLocalRotation;
        }

        private void SetKinematicAuthority(bool animationAuthority)
        {
            foreach (PhysicalAthleteRig.SegmentRuntime segment in _segments)
                SetBodyAuthority(segment.Body, animationAuthority);
            if (_barbell != null && _barbell.Body != null && _barbell.Body.gameObject.activeInHierarchy)
                SetBodyAuthority(_barbell.Body, animationAuthority);
        }

        private static void SetBodyAuthority(Rigidbody body, bool animationAuthority)
        {
            if (animationAuthority)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                body.isKinematic = true;
            }
            else
            {
                body.isKinematic = false;
                body.useGravity = true;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static bool IsWalkoutPhase(SquatPlayerGamePhase phase) =>
            phase == SquatPlayerGamePhase.UNRACK ||
            phase == SquatPlayerGamePhase.WALKOUT_STEP_1 ||
            phase == SquatPlayerGamePhase.WALKOUT_STEP_2 ||
            phase == SquatPlayerGamePhase.SETTLE;
    }
}
