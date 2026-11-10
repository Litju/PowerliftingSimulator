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

    /// <summary>
    /// Authored support assignment. This is the kinematic phase's authored
    /// statement of which foot is planted; it is not a measured dynamic load
    /// share and fabricates no contact impulse.
    /// </summary>
    public enum SquatAuthoredSupport : byte
    {
        NONE,
        BOTH,
        LEFT,
        RIGHT
    }

    /// <summary>Owns the animation/physics authority switches for the player-facing squat flow.</summary>
    public sealed class SquatHybridGameFlow
    {
        private const float WalkoutDurationSeconds = 2.9f;
        private const float RerackDurationSeconds = 2.4f;
        private const float RequiredReadySeconds = 0.2f;

        // Phase cues. The mechanics are driven by the authored root motion,
        // not by these clocks; they only select the player-facing phase label.
        private const float FirstStepStartSeconds = 0.8f;
        private const float SecondStepStartSeconds = 1.45f;
        private const float SettleStartSeconds = 2.1f;

        // The existing WalkoutSequence root track. The unrack clears the hooks
        // by 0.05 m and each of the two steps is 0.155 m, for the accepted
        // 0.36 m overall displacement. These are the authored keyframe values;
        // the foot targets and support transfers are derived from them.
        private const float AuthoredUnrackClearanceMeters = 0.05f;
        private const float AuthoredStepDisplacementMeters = 0.155f;
        private const float AuthoredWalkoutDistanceMeters =
            AuthoredUnrackClearanceMeters + 2f * AuthoredStepDisplacementMeters;

        // The authored pelvis dips just enough for the most-extended leg to
        // retain its measured setup length; this is a reachability margin in
        // the kinematic solve, not a joint limit or a balance rule.
        private const float MinimumReachMarginMeters = 0.005f;

        private const string SupportBoth = "DOUBLE_SUPPORT";
        private const string LeftSwingLifted = "LEFT_SWING_FOOT_LIFTED_AT_AUTHORED_ROOT_DISPLACEMENT";
        private const string RightSwingLifted = "RIGHT_SWING_FOOT_LIFTED_AT_AUTHORED_ROOT_DISPLACEMENT";
        private const string LeftSwingPlanted = "LEFT_SWING_FOOT_PLANTED_AT_AUTHORED_PLANTAR_TARGET";
        private const string RightSwingPlanted = "RIGHT_SWING_FOOT_PLANTED_AT_AUTHORED_PLANTAR_TARGET";

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
        private readonly Vector3[] _handoffPositions;
        private readonly Quaternion[] _handoffRotations;
        private readonly bool[] _rootAuthoredBody;

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
        private Vector3 _handoffBarPosition;
        private Quaternion _handoffBarRotation;

        private LegKinematics _leftLeg;
        private LegKinematics _rightLeg;
        private Vector3 _basePelvisPosition;
        private Quaternion _basePelvisRotation;
        private Vector3 _authoredWalkDirection;
        private Vector3 _authoredForward;
        private float _swingClearanceMeters;
        private bool _authoredGeometryCaptured;

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
            _handoffPositions = new Vector3[_segments.Count];
            _handoffRotations = new Quaternion[_segments.Count];
            _rootAuthoredBody = new bool[_segments.Count];
            for (int index = 0; index < _segments.Count; index++)
                _segmentIndexes.Add(_segments[index].Recipe.Id, index);

            Phase = SquatPlayerGamePhase.LOAD;
            AuthoredSupport = SquatAuthoredSupport.NONE;
            if (locomotionController != null)
                CreateRootMotionAnimator(locomotionController);
        }

        public SquatPlayerGamePhase Phase { get; private set; }
        public bool CanBeginSetup => Phase == SquatPlayerGamePhase.LOAD && _animator != null && _readySamples >= RequiredReadySampleCount;
        public int ReadySamples => _readySamples;
        public float WalkoutDistanceMeters => _rootMotionDisplacement.magnitude;
        public float MaximumHandoffPoseErrorMeters { get; private set; }
        public float MaximumHandoffBarAngularErrorDegrees { get; private set; }
        public float MaximumHandoffBodyAngularErrorDegrees { get; private set; }
        public bool HasLocomotionAuthority => _locomotionAuthority;
        public SquatAuthoredSupport AuthoredSupport { get; private set; }
        public int AuthoredSupportTransferCount { get; private set; }
        public int AuthoredTouchdownCount { get; private set; }
        public float LastAuthoredTouchdownSeconds { get; private set; }
        public string LastAuthoredSupportBasis { get; private set; } = string.Empty;

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
            Array.Copy(_basePositions, _handoffPositions, _basePositions.Length);
            Array.Copy(_baseRotations, _handoffRotations, _baseRotations.Length);
            CaptureBarPresentationAttachment();
            CaptureAuthoredLocomotionGeometry();
            _athleteRig.SetCommandSource(null);
            SetKinematicAuthority(true);
            _locomotionAuthority = true;
            _rootMotionDisplacement = Vector3.zero;
            _walkoutStarted = false;
            _handoffPending = false;
            _phaseElapsedSeconds = 0f;
            AuthoredSupport = SquatAuthoredSupport.BOTH;
            AuthoredSupportTransferCount = 0;
            AuthoredTouchdownCount = 0;
            LastAuthoredTouchdownSeconds = 0f;
            LastAuthoredSupportBasis = SupportBoth;
            Phase = SquatPlayerGamePhase.SETUP;
            return true;
        }

        public bool BeginWalkout()
        {
            if (Phase != SquatPlayerGamePhase.SETUP || !_locomotionAuthority || _animator == null ||
                !_authoredGeometryCaptured)
                return false;

            _rootMotionDisplacement = Vector3.zero;
            _phaseElapsedSeconds = 0f;
            _walkoutStarted = true;
            AuthoredSupport = SquatAuthoredSupport.BOTH;
            AuthoredSupportTransferCount = 0;
            AuthoredTouchdownCount = 0;
            LastAuthoredTouchdownSeconds = 0f;
            LastAuthoredSupportBasis = SupportBoth;
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
                else if (_phaseElapsedSeconds < SecondStepStartSeconds)
                    Phase = SquatPlayerGamePhase.WALKOUT_STEP_1;
                else if (_phaseElapsedSeconds < SettleStartSeconds)
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
                SetKinematicAuthority(false);
                _athleteRig.SetCommandSource(_adapter);
                MaximumHandoffPoseErrorMeters = 0f;
                MaximumHandoffBarAngularErrorDegrees = 0f;
                MaximumHandoffBodyAngularErrorDegrees = 0f;
                for (int index = 0; index < _segments.Count; index++)
                {
                    MaximumHandoffPoseErrorMeters = Mathf.Max(
                        MaximumHandoffPoseErrorMeters,
                        Vector3.Distance(_segments[index].Body.position, _handoffPositions[index]));
                    MaximumHandoffBodyAngularErrorDegrees = Mathf.Max(
                        MaximumHandoffBodyAngularErrorDegrees,
                        Quaternion.Angle(_segments[index].Body.rotation, _handoffRotations[index]));
                }
                if (barBody != null)
                {
                    MaximumHandoffPoseErrorMeters = Mathf.Max(
                        MaximumHandoffPoseErrorMeters,
                        Vector3.Distance(barBody.position, _handoffBarPosition));
                    MaximumHandoffBarAngularErrorDegrees = Quaternion.Angle(barBody.rotation, _handoffBarRotation);
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

            if (Phase == SquatPlayerGamePhase.RERACK)
                ApplyKinematicPose();
            else
                ApplyAuthoredPose();
            UpdateAuthoredSupport();
            FollowBarPresentationAttachment();

            if (_walkoutStarted && _phaseElapsedSeconds >= WalkoutDurationSeconds && IsWalkoutPhase(Phase))
            {
                CaptureHandoffPose();
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

            Rigidbody carrier = thorax.Body;
            _barLocalPosition = Quaternion.Inverse(carrier.rotation) * (_barbell.Body.position - carrier.position);
            _barLocalRotation = Quaternion.Inverse(carrier.rotation) * _barbell.Body.rotation;
        }

        /// <summary>
        /// Captures the accepted setup pose's leg anchors and measured segment
        /// lengths once. The authored walkout then keeps those physical lengths
        /// and joint coincidences; it never stretches a bone to reach a target.
        /// </summary>
        private void CaptureAuthoredLocomotionGeometry()
        {
            if (!_segmentIndexes.ContainsKey("pelvis"))
                throw new InvalidOperationException("The authored walkout requires the canonical pelvis body.");
            _basePelvisPosition = _basePositions[_segmentIndexes["pelvis"]];
            _basePelvisRotation = _baseRotations[_segmentIndexes["pelvis"]];

            _leftLeg = CaptureLeg("left");
            _rightLeg = CaptureLeg("right");
            _authoredWalkDirection = HorizontalDirection(_animator.transform.forward, Vector3.forward);
            _authoredForward = MeasureAthleteForward();

            _leftLeg.LandingAnkle = _leftLeg.BaseAnkle + _authoredWalkDirection * AuthoredWalkoutDistanceMeters;
            _rightLeg.LandingAnkle = _rightLeg.BaseAnkle + _authoredWalkDirection * AuthoredWalkoutDistanceMeters;
            _swingClearanceMeters = Mathf.Clamp(_leftLeg.FootHeightMeters * 0.75f, 0.04f, 0.10f);

            for (int index = 0; index < _segments.Count; index++)
            {
                string id = _segments[index].Recipe.Id;
                _rootAuthoredBody[index] = !IsLegSegment(id);
            }
            _authoredGeometryCaptured = true;
        }

        /// <summary>
        /// The imported reference rig's root forward is not the visible
        /// athlete's facing: the canonical import carries a 180-degree bind
        /// correction. The toes are the measured facing. The walkout moves
        /// backward along -Z, so a knee that used the root forward would bend
        /// away from the toes. Deriving the bend side from the toe bones keeps
        /// the authored swing knee on the athlete's anterior side.
        /// </summary>
        private Vector3 MeasureAthleteForward()
        {
            Animator reference = _athleteRig.ReferenceAnimator;
            Transform leftFoot = reference.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform leftToes = reference.GetBoneTransform(HumanBodyBones.LeftToes);
            Transform rightFoot = reference.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform rightToes = reference.GetBoneTransform(HumanBodyBones.RightToes);
            if (leftFoot != null && leftToes != null && rightFoot != null && rightToes != null)
            {
                Vector3 toeDirection = Vector3.ProjectOnPlane(
                    (leftToes.position - leftFoot.position) +
                    (rightToes.position - rightFoot.position),
                    Vector3.up);
                if (toeDirection.sqrMagnitude > 1e-8f)
                    return toeDirection.normalized;
            }
            return HorizontalDirection(reference.transform.forward, _authoredWalkDirection);
        }

        private LegKinematics CaptureLeg(string side)
        {
            string thighId = side + "_thigh";
            string shankId = side + "_shank";
            string footId = side + "_foot";
            if (!_segmentIndexes.TryGetValue(thighId, out int thighIndex) ||
                !_segmentIndexes.TryGetValue(shankId, out int shankIndex) ||
                !_segmentIndexes.TryGetValue(footId, out int footIndex))
                throw new InvalidOperationException($"The authored walkout could not resolve the {side} leg segments.");

            Rigidbody thigh = _segments[thighIndex].Body;
            Rigidbody shank = _segments[shankIndex].Body;
            Rigidbody foot = _segments[footIndex].Body;
            ConfigurableJoint hip = thigh.GetComponent<ConfigurableJoint>();
            ConfigurableJoint knee = shank.GetComponent<ConfigurableJoint>();
            ConfigurableJoint ankle = foot.GetComponent<ConfigurableJoint>();
            if (hip == null || knee == null || ankle == null ||
                hip.connectedBody == null || knee.connectedBody != thigh || ankle.connectedBody != shank)
                throw new InvalidOperationException($"The authored walkout requires the canonical {side} hip/knee/ankle joint chain.");
            if (!(foot.GetComponent<Collider>() is BoxCollider footBox))
                throw new InvalidOperationException($"The authored walkout requires a box foot collider on {footId}.");

            Vector3 baseHip = thigh.position + thigh.rotation * hip.anchor;
            Vector3 baseKnee = shank.position + shank.rotation * knee.anchor;
            Vector3 baseAnkle = foot.position + foot.rotation * ankle.anchor;
            float thighLength = Vector3.Distance(baseHip, baseKnee);
            float shankLength = Vector3.Distance(baseKnee, baseAnkle);
            if (!float.IsFinite(thighLength) || !float.IsFinite(shankLength) ||
                thighLength < 0.10f || shankLength < 0.10f)
                throw new InvalidOperationException($"The authored walkout measured an invalid {side} leg length.");

            return new LegKinematics
            {
                Side = side,
                Thigh = thigh,
                Shank = shank,
                Foot = foot,
                HipInPelvis = hip.connectedAnchor,
                HipInThigh = hip.anchor,
                KneeInThigh = knee.connectedAnchor,
                KneeInShank = knee.anchor,
                AnkleInShank = ankle.connectedAnchor,
                AnkleInFoot = ankle.anchor,
                BaseHip = baseHip,
                BaseKnee = baseKnee,
                BaseAnkle = baseAnkle,
                BaseThighRotation = _baseRotations[thighIndex],
                BaseShankRotation = _baseRotations[shankIndex],
                BaseFootRotation = _baseRotations[footIndex],
                BaseHipToKneeDirection = (baseKnee - baseHip).normalized,
                BaseKneeToAnkleDirection = (baseAnkle - baseKnee).normalized,
                ThighLength = thighLength,
                ShankLength = shankLength,
                BaseHipAnkleDistance = Vector3.Distance(baseHip, baseAnkle),
                MaximumReach = thighLength + shankLength - MinimumReachMarginMeters,
                FootHeightMeters = footBox.size.y,
                LandingAnkle = baseAnkle
            };
        }

        private void ApplyAuthoredPose()
        {
            if (!_authoredGeometryCaptured)
                return;

            Vector3 horizontalRoot = new Vector3(_rootMotionDisplacement.x, 0f, _rootMotionDisplacement.z);
            float rootTravel = Mathf.Max(0f, Vector3.Dot(horizontalRoot, _authoredWalkDirection));
            float leftProgress = Mathf.Clamp01(
                (rootTravel - AuthoredUnrackClearanceMeters) / AuthoredStepDisplacementMeters);
            float rightProgress = Mathf.Clamp01(
                (rootTravel - AuthoredUnrackClearanceMeters - AuthoredStepDisplacementMeters) /
                AuthoredStepDisplacementMeters);
            Vector3 leftAnkle = EvaluateSwingAnkle(_leftLeg, leftProgress);
            Vector3 rightAnkle = EvaluateSwingAnkle(_rightLeg, rightProgress);
            float pelvisY = SolveAuthoredPelvisY(
                horizontalRoot, leftAnkle, rightAnkle, leftProgress, rightProgress);
            Vector3 offset = new Vector3(
                horizontalRoot.x,
                pelvisY - _basePelvisPosition.y,
                horizontalRoot.z);

            for (int index = 0; index < _segments.Count; index++)
            {
                if (!_rootAuthoredBody[index])
                    continue;
                SetBodyPose(
                    _segments[index].Body,
                    _basePositions[index] + offset,
                    _baseRotations[index]);
            }

            PlaceLeg(_leftLeg, leftAnkle, offset);
            PlaceLeg(_rightLeg, rightAnkle, offset);
        }

        /// <summary>
        /// The swing foot follows the accepted root track: it leaves its
        /// authored plantar anchor, travels the full walkout displacement with
        /// zero endpoint velocity, and clears the platform by a height derived
        /// from the measured foot collider. Both feet end level at the
        /// walkout's accepted displacement.
        /// </summary>
        private Vector3 EvaluateSwingAnkle(LegKinematics leg, float progress)
        {
            if (progress <= 0f)
                return leg.BaseAnkle;
            if (progress >= 1f)
                return leg.LandingAnkle;

            float smooth = progress * progress * (3f - 2f * progress);
            float lift = _swingClearanceMeters * Mathf.Sin(progress * Mathf.PI) * Mathf.Sin(progress * Mathf.PI);
            return leg.BaseAnkle +
                _authoredWalkDirection * (AuthoredWalkoutDistanceMeters * smooth) +
                Vector3.up * lift;
        }

        /// <summary>
        /// The pelvis height is not animated arbitrarily. It is the lowest
        /// height at which the most-extended leg still holds the measured setup
        /// hip-to-ankle length (or, for a lifted swing leg, its measured
        /// maximum reach). This keeps a planted foot's leg geometry possible
        /// while the pelvis travels between the two authored foot placements.
        /// </summary>
        private float SolveAuthoredPelvisY(
            Vector3 horizontalRoot,
            Vector3 leftAnkle,
            Vector3 rightAnkle,
            float leftProgress,
            float rightProgress)
        {
            float leftY = SolveLegPelvisY(_leftLeg, leftAnkle, horizontalRoot, leftProgress);
            float rightY = SolveLegPelvisY(_rightLeg, rightAnkle, horizontalRoot, rightProgress);
            return Mathf.Min(_basePelvisPosition.y, Mathf.Min(leftY, rightY));
        }

        private float SolveLegPelvisY(
            LegKinematics leg,
            Vector3 ankle,
            Vector3 horizontalRoot,
            float swingProgress)
        {
            Vector3 hipHorizontal = new Vector3(
                leg.BaseHip.x + horizontalRoot.x,
                0f,
                leg.BaseHip.z + horizontalRoot.z);
            Vector3 ankleHorizontal = new Vector3(ankle.x, 0f, ankle.z);
            float horizontal = Vector3.Distance(hipHorizontal, ankleHorizontal);
            float lift = Mathf.Max(0f, ankle.y - leg.BaseAnkle.y);
            float liftFraction = _swingClearanceMeters > 1e-4f
                ? Mathf.Clamp01(lift / _swingClearanceMeters)
                : 0f;
            bool swinging = swingProgress > 0f && swingProgress < 1f;
            float effectiveDistance = swinging
                ? Mathf.Lerp(leg.BaseHipAnkleDistance, leg.MaximumReach, liftFraction)
                : leg.BaseHipAnkleDistance;
            float verticalSquared = effectiveDistance * effectiveDistance - horizontal * horizontal;
            float vertical = verticalSquared > 0f ? Mathf.Sqrt(verticalSquared) : 0f;
            float hipOffsetY = leg.BaseHip.y - _basePelvisPosition.y;
            return ankle.y + vertical - hipOffsetY;
        }

        /// <summary>
        /// Analytic two-bone placement in the sagittal plane. The knee is
        /// placed toward the athlete's measured forward direction, and each
        /// body is positioned from its own captured joint anchor so the hip,
        /// knee and ankle anchors coincide exactly at the measured segment
        /// lengths.
        /// </summary>
        private void PlaceLeg(LegKinematics leg, Vector3 ankle, Vector3 offset)
        {
            Vector3 hip = leg.BaseHip + offset;
            Vector3 hipToAnkle = ankle - hip;
            float distance = hipToAnkle.magnitude;
            float reach = leg.ThighLength + leg.ShankLength;
            float clampedDistance = Mathf.Clamp(
                distance,
                Mathf.Abs(leg.ThighLength - leg.ShankLength) + 1e-4f,
                reach - 1e-4f);
            Vector3 direction = distance > 1e-6f ? hipToAnkle / distance : Vector3.down;
            float along = (clampedDistance * clampedDistance +
                leg.ThighLength * leg.ThighLength -
                leg.ShankLength * leg.ShankLength) / (2f * clampedDistance);
            float heightSquared = leg.ThighLength * leg.ThighLength - along * along;
            float height = heightSquared > 0f ? Mathf.Sqrt(heightSquared) : 0f;
            Vector3 bend = Vector3.ProjectOnPlane(_authoredForward, direction);
            if (bend.sqrMagnitude < 1e-6f)
                bend = Vector3.ProjectOnPlane(Vector3.forward, direction);
            if (bend.sqrMagnitude < 1e-6f)
                bend = Vector3.right;
            bend.Normalize();
            Vector3 knee = hip + direction * along + bend * height;

            Quaternion thighRotation = Quaternion.FromToRotation(
                leg.BaseHipToKneeDirection,
                (knee - hip).normalized) * leg.BaseThighRotation;
            Vector3 thighPosition = hip - thighRotation * leg.HipInThigh;
            Quaternion shankRotation = Quaternion.FromToRotation(
                leg.BaseKneeToAnkleDirection,
                (ankle - knee).normalized) * leg.BaseShankRotation;
            Vector3 shankPosition = knee - shankRotation * leg.KneeInShank;
            Quaternion footRotation = leg.BaseFootRotation;
            Vector3 footPosition = ankle - footRotation * leg.AnkleInFoot;

            SetBodyPose(leg.Thigh, thighPosition, thighRotation);
            SetBodyPose(leg.Shank, shankPosition, shankRotation);
            SetBodyPose(leg.Foot, footPosition, footRotation);
        }

        private void UpdateAuthoredSupport()
        {
            if (Phase == SquatPlayerGamePhase.RERACK || !_walkoutStarted)
            {
                SetAuthoredSupport(SquatAuthoredSupport.BOTH, SupportBoth);
                return;
            }

            float rootTravel = Mathf.Max(
                0f,
                Vector3.Dot(
                    new Vector3(_rootMotionDisplacement.x, 0f, _rootMotionDisplacement.z),
                    _authoredWalkDirection));
            float leftProgress = Mathf.Clamp01(
                (rootTravel - AuthoredUnrackClearanceMeters) / AuthoredStepDisplacementMeters);
            float rightProgress = Mathf.Clamp01(
                (rootTravel - AuthoredUnrackClearanceMeters - AuthoredStepDisplacementMeters) /
                AuthoredStepDisplacementMeters);
            bool leftSwinging = leftProgress > 0f && leftProgress < 1f;
            bool rightSwinging = rightProgress > 0f && rightProgress < 1f;
            if (leftSwinging)
            {
                SetAuthoredSupport(SquatAuthoredSupport.RIGHT, LeftSwingLifted);
                return;
            }
            if (rightSwinging)
            {
                SetAuthoredSupport(SquatAuthoredSupport.LEFT, RightSwingLifted);
                return;
            }

            string basis = AuthoredSupport == SquatAuthoredSupport.RIGHT
                ? LeftSwingPlanted
                : AuthoredSupport == SquatAuthoredSupport.LEFT
                    ? RightSwingPlanted
                    : SupportBoth;
            SetAuthoredSupport(SquatAuthoredSupport.BOTH, basis);
        }

        private void SetAuthoredSupport(SquatAuthoredSupport support, string basis)
        {
            if (AuthoredSupport == support)
                return;

            SquatAuthoredSupport previous = AuthoredSupport;
            AuthoredSupport = support;
            AuthoredSupportTransferCount++;
            LastAuthoredSupportBasis = basis;
            if (support == SquatAuthoredSupport.BOTH &&
                (previous == SquatAuthoredSupport.LEFT || previous == SquatAuthoredSupport.RIGHT))
            {
                AuthoredTouchdownCount++;
                LastAuthoredTouchdownSeconds = _phaseElapsedSeconds;
            }
        }

        private void CaptureHandoffPose()
        {
            for (int index = 0; index < _segments.Count; index++)
            {
                _handoffPositions[index] = _segments[index].Body.position;
                _handoffRotations[index] = _segments[index].Body.rotation;
            }
            if (_barbell != null && _barbell.Body != null && _barbell.Body.gameObject.activeInHierarchy)
            {
                _handoffBarPosition = _barbell.Body.position;
                _handoffBarRotation = _barbell.Body.rotation;
            }
        }

        private void ApplyKinematicPose()
        {
            Vector3 displacement = _rootMotionDisplacement;
            for (int index = 0; index < _segments.Count; index++)
            {
                SetBodyPose(
                    _segments[index].Body,
                    _basePositions[index] + displacement,
                    _baseRotations[index]);
            }
        }

        private void FollowBarPresentationAttachment()
        {
            if (_barbell == null || _barbell.Body == null || !_barbell.Body.gameObject.activeInHierarchy ||
                !_athleteRig.Segments.TryGetValue("thorax", out PhysicalAthleteRig.SegmentRuntime thorax))
                return;

            Rigidbody carrier = thorax.Body;
            SetBodyPose(
                _barbell.Body,
                carrier.position + carrier.rotation * _barLocalPosition,
                carrier.rotation * _barLocalRotation);
        }

        private static void SetBodyPose(Rigidbody body, Vector3 position, Quaternion rotation)
        {
            body.position = position;
            body.rotation = rotation;
            body.transform.SetPositionAndRotation(position, rotation);
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

        private static bool IsLegSegment(string id) =>
            id.EndsWith("_thigh", StringComparison.Ordinal) ||
            id.EndsWith("_shank", StringComparison.Ordinal) ||
            id.EndsWith("_foot", StringComparison.Ordinal);

        private static Vector3 HorizontalDirection(Vector3 value, Vector3 fallback)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(value, Vector3.up);
            if (horizontal.sqrMagnitude < 1e-8f)
                horizontal = Vector3.ProjectOnPlane(fallback, Vector3.up);
            return horizontal.sqrMagnitude < 1e-8f ? Vector3.forward : horizontal.normalized;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static bool IsWalkoutPhase(SquatPlayerGamePhase phase) =>
            phase == SquatPlayerGamePhase.UNRACK ||
            phase == SquatPlayerGamePhase.WALKOUT_STEP_1 ||
            phase == SquatPlayerGamePhase.WALKOUT_STEP_2 ||
            phase == SquatPlayerGamePhase.SETTLE;

        private sealed class LegKinematics
        {
            public string Side;
            public Rigidbody Thigh;
            public Rigidbody Shank;
            public Rigidbody Foot;
            public Vector3 HipInPelvis;
            public Vector3 HipInThigh;
            public Vector3 KneeInThigh;
            public Vector3 KneeInShank;
            public Vector3 AnkleInShank;
            public Vector3 AnkleInFoot;
            public Vector3 BaseHip;
            public Vector3 BaseKnee;
            public Vector3 BaseAnkle;
            public Quaternion BaseThighRotation;
            public Quaternion BaseShankRotation;
            public Quaternion BaseFootRotation;
            public Vector3 BaseHipToKneeDirection;
            public Vector3 BaseKneeToAnkleDirection;
            public float ThighLength;
            public float ShankLength;
            public float BaseHipAnkleDistance;
            public float MaximumReach;
            public float FootHeightMeters;
            public Vector3 LandingAnkle;
        }
    }
}
