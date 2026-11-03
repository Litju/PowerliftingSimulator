using UnityEngine;
using UnityEngine.InputSystem;

namespace PowerliftingSimulator.Squat.Unity
{
    public enum PowerliftingScreenState
    {
        MainMenu = 0,
        TrainingGym = 1,
        SquatPreparation = 2,
        ActiveLift = 3,
        AttemptResult = 4
    }

    public enum GymStationFocus
    {
        AllStationsOverview = 0,
        SquatStation = 1,
        BenchStation = 2,
        DeadliftStation = 3
    }

    [DisallowMultipleComponent]
    public sealed class PowerliftingCameraDirector : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private SquatPhysicalPrototypeController squatController;
        [SerializeField] private PowerliftingSettingsManager settingsManager;
        [SerializeField] private float positionSmoothTime = 0.28f;
        [SerializeField] private float rotationLerpSpeed = 9f;

        private PowerliftingScreenState _screenState = PowerliftingScreenState.MainMenu;
        private GymStationFocus _stationFocus = GymStationFocus.AllStationsOverview;
        private PowerliftingCameraAngle _activeLiftAngle = PowerliftingCameraAngle.ThreeQuarterCompetition;
        private Vector3 _positionVelocity;
        private Vector3 _desiredPosition;
        private Vector3 _desiredLookAt;
        private float _desiredFov = 38f;
        private float _orbitPhase;

        public Camera TargetCamera => targetCamera;
        public PowerliftingScreenState ScreenState => _screenState;
        public GymStationFocus StationFocus => _stationFocus;
        public PowerliftingCameraAngle ActiveLiftAngle => _activeLiftAngle;

        private void Awake()
        {
            ResolveReferences();
            if (settingsManager != null)
                _activeLiftAngle = settingsManager.PreferredCameraAngle;
            ComputeDesiredFraming(out _desiredPosition, out _desiredLookAt, out _desiredFov);
            SnapToTargetImmediate();
        }

        private void ResolveReferences()
        {
            if (targetCamera == null)
                targetCamera = GetComponent<Camera>();
            if (targetCamera == null)
                targetCamera = Camera.main;
            if (squatController == null)
                squatController = FindFirstObjectByType<SquatPhysicalPrototypeController>();
            if (settingsManager == null)
                settingsManager = FindFirstObjectByType<PowerliftingSettingsManager>();
        }

        public void SetScreenState(PowerliftingScreenState state, bool snapImmediate = false)
        {
            _screenState = state;
            if (state != PowerliftingScreenState.TrainingGym)
                _stationFocus = GymStationFocus.SquatStation;
            else if (_stationFocus == GymStationFocus.SquatStation)
                _stationFocus = GymStationFocus.AllStationsOverview;

            if (snapImmediate)
                SnapToTargetImmediate();
        }

        public void SetGymStationFocus(GymStationFocus focus, bool snapImmediate = false)
        {
            _stationFocus = focus;
            if (snapImmediate)
                SnapToTargetImmediate();
        }

        public void SetActiveLiftCameraAngle(PowerliftingCameraAngle angle, bool snapImmediate = false)
        {
            _activeLiftAngle = angle;
            if (settingsManager != null && settingsManager.PreferredCameraAngle != angle)
                settingsManager.SetPreferredCameraAngle(angle);
            if (snapImmediate)
                SnapToTargetImmediate();
        }

        public void CycleActiveLiftCameraAngle()
        {
            int next = ((int)_activeLiftAngle + 1) % 3;
            SetActiveLiftCameraAngle((PowerliftingCameraAngle)next, false);
        }

        public static string GetCameraAngleLabel(PowerliftingCameraAngle angle)
        {
            switch (angle)
            {
                case PowerliftingCameraAngle.SideDepthReferee:
                    return "SIDE DEPTH JUDGE";
                case PowerliftingCameraAngle.FrontHeadReferee:
                    return "HEAD REFEREE FRONT";
                default:
                    return "3/4 COMPETITION";
            }
        }

        public void SnapToTargetImmediate()
        {
            ResolveReferences();
            if (targetCamera == null)
                return;

            ComputeDesiredFraming(out _desiredPosition, out _desiredLookAt, out _desiredFov);
            _positionVelocity = Vector3.zero;
            targetCamera.transform.position = _desiredPosition;
            targetCamera.transform.rotation = Quaternion.LookRotation(
                (_desiredLookAt - _desiredPosition).normalized,
                Vector3.up);
            targetCamera.fieldOfView = _desiredFov;
        }

        private void LateUpdate()
        {
            ResolveReferences();
            if (targetCamera == null)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && _screenState == PowerliftingScreenState.ActiveLift && keyboard.cKey.wasPressedThisFrame)
                CycleActiveLiftCameraAngle();

            _orbitPhase += Time.unscaledDeltaTime * 0.38f;
            ComputeDesiredFraming(out _desiredPosition, out _desiredLookAt, out _desiredFov);

            float dt = Mathf.Max(0.001f, Time.unscaledDeltaTime);
            targetCamera.transform.position = Vector3.SmoothDamp(
                targetCamera.transform.position,
                _desiredPosition,
                ref _positionVelocity,
                positionSmoothTime,
                Mathf.Infinity,
                dt);

            Vector3 dir = _desiredLookAt - targetCamera.transform.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                targetCamera.transform.rotation = Quaternion.Slerp(
                    targetCamera.transform.rotation,
                    targetRot,
                    1f - Mathf.Exp(-rotationLerpSpeed * dt));
            }

            targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, _desiredFov, 1f - Mathf.Exp(-rotationLerpSpeed * dt));
        }

        private void ComputeDesiredFraming(out Vector3 position, out Vector3 lookAt, out float fov)
        {
            float athleteZ = GetTrackedAthleteZ();

            switch (_screenState)
            {
                case PowerliftingScreenState.MainMenu:
                {
                    float swayX = Mathf.Sin(_orbitPhase) * 0.14f;
                    float swayY = Mathf.Cos(_orbitPhase * 0.7f) * 0.03f;
                    position = new Vector3(2.95f + swayX, 1.72f + swayY, 4.65f);
                    lookAt = new Vector3(-0.15f, 1.05f, -0.25f);
                    fov = 40f;
                    return;
                }

                case PowerliftingScreenState.TrainingGym:
                {
                    switch (_stationFocus)
                    {
                        case GymStationFocus.BenchStation:
                            position = new Vector3(-2.35f, 1.55f, 2.55f);
                            lookAt = new Vector3(-3.85f, 0.78f, -0.35f);
                            fov = 40f;
                            return;
                        case GymStationFocus.DeadliftStation:
                            position = new Vector3(2.35f, 1.48f, 2.55f);
                            lookAt = new Vector3(3.85f, 0.55f, -0.35f);
                            fov = 40f;
                            return;
                        case GymStationFocus.SquatStation:
                            position = new Vector3(2.15f, 1.48f, 3.35f);
                            lookAt = new Vector3(0f, 1.05f, -0.15f);
                            fov = 38f;
                            return;
                        default:
                            position = new Vector3(0.0f, 2.15f, 5.75f);
                            lookAt = new Vector3(0.0f, 0.98f, -0.35f);
                            fov = 46f;
                            return;
                    }
                }

                case PowerliftingScreenState.SquatPreparation:
                {
                    position = new Vector3(1.92f, 1.38f, 2.42f);
                    lookAt = new Vector3(0.05f, 1.12f, -0.10f);
                    fov = 36f;
                    return;
                }

                case PowerliftingScreenState.AttemptResult:
                {
                    position = new Vector3(1.98f, 1.30f, 2.55f + athleteZ * 0.5f);
                    lookAt = new Vector3(-0.10f, 1.00f, -0.12f + athleteZ * 0.5f);
                    fov = 36f;
                    return;
                }

                default:
                {
                    switch (_activeLiftAngle)
                    {
                        case PowerliftingCameraAngle.SideDepthReferee:
                            position = new Vector3(2.48f, 0.94f, 0.48f + athleteZ);
                            lookAt = new Vector3(0f, 0.82f, 0.04f + athleteZ);
                            fov = 33f;
                            return;
                        case PowerliftingCameraAngle.FrontHeadReferee:
                            position = new Vector3(0.18f, 1.18f, 2.85f + athleteZ * 0.65f);
                            lookAt = new Vector3(0f, 0.98f, -0.08f + athleteZ);
                            fov = 34f;
                            return;
                        default:
                            position = new Vector3(2.18f, 1.24f, 2.52f + athleteZ * 0.75f);
                            lookAt = new Vector3(0f, 0.96f, -0.10f + athleteZ);
                            fov = 35f;
                            return;
                    }
                }
            }
        }

        private float GetTrackedAthleteZ()
        {
            if (squatController == null || !squatController.IsInitialized || squatController.AthleteRig == null)
                return 0f;

            if (squatController.AthleteRig.Segments.TryGetValue("pelvis", out Athlete.PhysicalAthleteRig.SegmentRuntime pelvis) &&
                pelvis.Body != null)
            {
                return Mathf.Clamp(pelvis.Body.position.z, -0.65f, 0.35f);
            }

            return 0f;
        }
    }
}
