using System;
using System.Globalization;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PowerliftingSimulator.Squat.Unity
{
    // Ahead of PhysicalAthleteRig, so the athlete is registered to the
    // platform before the rig builds any body from the authored pose.
    [DefaultExecutionOrder(-950)]
    [DisallowMultipleComponent]
    public sealed class SquatPhysicalPrototypeController : MonoBehaviour
    {
        [SerializeField] private FoundationBootstrap foundation;
        [SerializeField] private PhysicalAthleteRig athleteRig;
        [SerializeField] private PhysicalBarbell barbell;
        [SerializeField] private float barLoadKg = 25f;
        [SerializeField] private bool autoSquatOnStart = false;
        [SerializeField] private bool attachBarSaddle = true;
        [SerializeField] private bool showDebugGui = true;

        private const float PlantarSymmetryToleranceM = 0.001f;

        private SquatPhysicalAdapter _adapter;
        private SquatBarSaddle _saddle;
        private PhysicalFootContactDetector _leftFootContact;
        private PhysicalFootContactDetector _rightFootContact;
        private bool _isInitialized;
        private float _selectedLoadKg = 25f;
        private string _startupFailure = string.Empty;
        private GUIStyle _hudLabelStyle;
        private GUIStyle _hudTitleStyle;

        public SquatPhysicalAdapter Adapter => _adapter;
        public SquatBarSaddle Saddle => _saddle;
        public PhysicalFootContactDetector LeftFootContact => _leftFootContact;
        public PhysicalFootContactDetector RightFootContact => _rightFootContact;
        public float CurrentLoadKg => _selectedLoadKg;
        public bool IsInitialized => _isInitialized;
        public bool RuntimeWired => _isInitialized && foundation != null && athleteRig != null &&
            barbell != null && _adapter != null && (_selectedLoadKg <= 0f || _saddle != null);
        public string StartupFailure => _startupFailure;

        private void Awake()
        {
            _selectedLoadKg = Mathf.Max(0f, barLoadKg);
        }

        private void Start()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (_isInitialized)
                return;

            if (foundation == null)
                foundation = FindFirstObjectByType<FoundationBootstrap>();
            if (athleteRig == null)
                athleteRig = FindFirstObjectByType<PhysicalAthleteRig>();
            if (barbell == null)
                barbell = FindFirstObjectByType<PhysicalBarbell>();

            if (foundation == null || foundation.Runtime == null || !foundation.Runtime.IsInitialized)
            {
                _startupFailure = "Foundation runtime is not initialized.";
                return;
            }
            if (athleteRig == null)
            {
                _startupFailure = "PhysicalAthleteRig is missing.";
                return;
            }

            if (athleteRig.PoweredController == null)
            {
                RegisterAthleteToGround();
                athleteRig.Build();
            }

            _adapter = new SquatPhysicalAdapter(athleteRig);
            AddFootContactDetectors();
            _adapter.SetFootContactDetectors(_leftFootContact, _rightFootContact);

            if (barbell != null)
            {
                if (!barbell.IsBuilt)
                    barbell.Build();
                barbell.SetGameplayPerformanceProfile(true);
                ApplySelectedLoadWithoutSaddle();
            }

            if (attachBarSaddle && _selectedLoadKg > 0f)
                EnsureSaddle();

            _adapter.Reset();
            _adapter.SetSaddle(_saddle);
            athleteRig.SetGameplayPerformanceProfile(true);
            athleteRig.PrimeCommandSource();

            if (autoSquatOnStart)
                _adapter.StartSquat();

            _isInitialized = true;
            _startupFailure = string.Empty;
        }

        /// <summary>
        /// Places the authored athlete so the canonical sole rests on the
        /// platform before any body exists. The plantar plane comes from the
        /// GAM-10 reference calibration, which owns that definition; this only
        /// consumes it.
        /// </summary>
        private void RegisterAthleteToGround()
        {
            Animator reference = athleteRig.ReferenceAnimator;
            if (reference == null)
                throw new InvalidOperationException("Ground registration requires the canonical reference animator.");

            SquatReferenceRigCalibration calibration = SquatReferenceRigCalibration.Build(
                reference,
                reference.transform.root,
                "Assets/Scenes/Prototype/SquatPhysicalPrototype.unity");

            float leftPlantarY = calibration.LeftFoot.PlantarAnchorWorld.y;
            float rightPlantarY = calibration.RightFoot.PlantarAnchorWorld.y;
            float asymmetry = Mathf.Abs(leftPlantarY - rightPlantarY);
            if (asymmetry > PlantarSymmetryToleranceM)
            {
                throw new InvalidOperationException(
                    $"The canonical plantar anchors are asymmetric by {asymmetry * 1000f:F2} mm (left {leftPlantarY:F4}, right {rightPlantarY:F4}). Ground registration would tilt the athlete; fix the reference calibration first.");
            }

            athleteRig.RegisterCanonicalGround(0.5f * (leftPlantarY + rightPlantarY));
        }

        private void AddFootContactDetectors()
        {
            if (athleteRig.Segments.TryGetValue("left_foot", out PhysicalAthleteRig.SegmentRuntime leftFoot) && leftFoot.Body != null)
            {
                _leftFootContact = leftFoot.Body.gameObject.GetComponent<PhysicalFootContactDetector>();
                if (_leftFootContact == null)
                    _leftFootContact = leftFoot.Body.gameObject.AddComponent<PhysicalFootContactDetector>();
            }
            if (athleteRig.Segments.TryGetValue("right_foot", out PhysicalAthleteRig.SegmentRuntime rightFoot) && rightFoot.Body != null)
            {
                _rightFootContact = rightFoot.Body.gameObject.GetComponent<PhysicalFootContactDetector>();
                if (_rightFootContact == null)
                    _rightFootContact = rightFoot.Body.gameObject.AddComponent<PhysicalFootContactDetector>();
            }
        }

        private void Update()
        {
            if (!_isInitialized)
            {
                Initialize();
                return;
            }

            int ticks = foundation.Runtime.LastCatchUpTicks;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            for (int tick = 0; tick < ticks; tick++)
            {
                if (_leftFootContact != null)
                    _leftFootContact.PhysicsTickUpdate(dt);
                if (_rightFootContact != null)
                    _rightFootContact.PhysicsTickUpdate(dt);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.digit1Key.wasPressedThisFrame)
                SetLoad(0f);
            else if (keyboard.digit2Key.wasPressedThisFrame)
                SetLoad(25f);
            else if (keyboard.digit3Key.wasPressedThisFrame)
                SetLoad(105f);
            else if (keyboard.bKey.wasPressedThisFrame)
                ReleaseSaddleForFailureInspection();
            else if (keyboard.rKey.wasPressedThisFrame)
                ResetPrototype();
        }

        public void SetLoad(float loadKg)
        {
            if (!_isInitialized || barbell == null)
                return;

            _selectedLoadKg = Mathf.Max(0f, loadKg);
            if (_saddle != null)
            {
                _saddle.BreakSaddle();
                _saddle = null;
            }

            foundation.Reset();
            if (_selectedLoadKg <= 0f)
            {
                if (barbell.Body != null)
                    barbell.Body.gameObject.SetActive(false);
            }
            else
            {
                if (barbell.Body != null)
                    barbell.Body.gameObject.SetActive(true);
                barbell.ConfigureLoad(_selectedLoadKg);
                EnsureSaddle();
            }

            _adapter.Reset();
            _adapter.SetSaddle(_saddle);
            athleteRig.SetCommandSource(_adapter);
            athleteRig.PrimeCommandSource();
        }

        public void ResetPrototype()
        {
            if (!_isInitialized)
                return;

            // A gameplay reset rebuilds the production scene so every dynamic
            // body returns to its authored spawn pose without writing a
            // physical transform or velocity from the control path.
            SceneManager.LoadScene(SceneManager.GetActiveScene().path, LoadSceneMode.Single);
        }

        private void ApplySelectedLoadWithoutSaddle()
        {
            if (barbell == null || barbell.Body == null)
                return;

            if (_selectedLoadKg <= 0f)
            {
                barbell.Body.gameObject.SetActive(false);
                return;
            }

            barbell.Body.gameObject.SetActive(true);
            barbell.ConfigureLoad(_selectedLoadKg);
        }

        private void EnsureSaddle()
        {
            if (!attachBarSaddle || _selectedLoadKg <= 0f || barbell == null || barbell.Body == null ||
                athleteRig == null || !barbell.Body.gameObject.activeInHierarchy)
                return;
            if (_saddle != null && _saddle.IsAttached)
                return;
            if (!athleteRig.Segments.TryGetValue("thorax", out PhysicalAthleteRig.SegmentRuntime thorax) || thorax.Body == null)
                return;

            _saddle = new SquatBarSaddle(barbell, thorax.Body);
            _adapter?.SetSaddle(_saddle);
        }

        private void ReleaseSaddleForFailureInspection()
        {
            if (_saddle == null)
                return;
            _saddle.BreakSaddle();
            _adapter?.SetSaddle(_saddle);
        }

        private void OnGUI()
        {
            if (!showDebugGui)
                return;

            EnsureHudStyles();
            GUILayout.BeginArea(new Rect(12f, 12f, 630f, 292f), GUI.skin.box);
            GUILayout.Label("GAM-11 PHYSICAL SQUAT", _hudTitleStyle, GUILayout.Height(16f));
            if (!_isInitialized)
            {
                GUILayout.Label("STARTUP: " + (_startupFailure.Length == 0 ? "waiting" : _startupFailure), _hudLabelStyle, GUILayout.Height(13f));
                GUILayout.EndArea();
                return;
            }

            float pelvisHeight = athleteRig.Segments.TryGetValue("pelvis", out PhysicalAthleteRig.SegmentRuntime pelvis) && pelvis.Body != null
                ? pelvis.Body.position.y
                : 0f;
            string leftContact = _leftFootContact != null && _leftFootContact.IsInContact ? "YES" : "NO";
            string rightContact = _rightFootContact != null && _rightFootContact.IsInContact ? "YES" : "NO";
            float leftSlip = _leftFootContact != null ? _leftFootContact.SlipSpeed : 0f;
            float rightSlip = _rightFootContact != null ? _rightFootContact.SlipSpeed : 0f;
            float saddleError = _saddle != null ? _saddle.SaddleSeparationMeters : float.PositiveInfinity;
            string failure = _adapter.FailureReason;
            if (_saddle != null && !_saddle.SpawnAlignmentWithinTolerance && failure == "NONE")
                failure = "SPAWN_ALIGNMENT";

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(315f));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "STATE: {0}", _adapter.State));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "s_q: {0:F3}", _adapter.Sq));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "LOAD_KG: {0:F1}", _selectedLoadKg));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "PELVIS_HEIGHT: {0:F3} m", pelvisHeight));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "SYSTEM_COM_AP_ERROR: {0:+0.000;-0.000} m", _adapter.ApComError));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "SYSTEM_COM_ML_ERROR: {0:+0.000;-0.000} m", _adapter.MlComError));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "MAX_DRIVE_SATURATION: {0:F2}", _adapter.MaxDriveSaturation));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "FAILURE_REASON: {0}", failure));
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(300f));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "LEFT_FOOT_CONTACT: {0}", leftContact));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "RIGHT_FOOT_CONTACT: {0}", rightContact));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "LEFT_FOOT_SLIP: {0:F4} m/s", leftSlip));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "RIGHT_FOOT_SLIP: {0:F4} m/s", rightSlip));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "BAR_SADDLE_ATTACHED: {0}", _saddle != null && _saddle.IsAttached ? "YES" : "NO"));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "BAR_SADDLE_ERROR: {0}", float.IsInfinity(saddleError) ? "N/A" : saddleError.ToString("F4", CultureInfo.InvariantCulture) + " m"));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            HudLabel("CONTROLS: SPACE Brace/Confirm | S Yield/descend | W Drive/ascend | A/D Balance | R Reset | B Release saddle");
            HudLabel("LOAD_CONTROLS: 1 Unloaded | 2 25 kg | 3 105 kg");
            GUILayout.EndArea();
        }

        private void HudLabel(string text)
        {
            GUILayout.Label(text, _hudLabelStyle, GUILayout.Height(13f));
        }

        private void EnsureHudStyles()
        {
            if (_hudLabelStyle != null)
                return;

            _hudLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fixedHeight = 13f,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            _hudTitleStyle = new GUIStyle(_hudLabelStyle)
            {
                fontSize = 13,
                fixedHeight = 16f,
                fontStyle = FontStyle.Bold
            };
        }
    }
}
