using System;
using System.Collections.Generic;
using System.Globalization;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Squat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PowerliftingSimulator.Squat.Unity
{
    [DisallowMultipleComponent]
    public sealed class PowerliftingGameUiController : MonoBehaviour
    {
        private static PowerliftingScreenState? s_nextScreenAfterReload;

        [SerializeField] private SquatPhysicalPrototypeController squatController;
        [SerializeField] private PowerliftingCameraDirector cameraDirector;
        [SerializeField] private PowerliftingAudioDirector audioDirector;
        [SerializeField] private PowerliftingSettingsManager settingsManager;
        [SerializeField] private PowerliftingGymEnvironment gymEnvironment;

        private Canvas _canvas;
        private Font _uiFont;
        private PowerliftingScreenState _currentScreen = PowerliftingScreenState.MainMenu;
        private bool _settingsOpen;
        private bool _quitRequested;
        private string _gymStationNotice =
            "Select the Competition Squat Station to begin your training session. Bench Press and Deadlift stations are staged in the 3D gym for upcoming milestones.";

        // Screen root panels
        private GameObject _mainMenuPanel;
        private GameObject _trainingGymPanel;
        private GameObject _squatPrepPanel;
        private GameObject _activeLiftPanel;
        private GameObject _resultPanel;
        private GameObject _settingsModalPanel;

        // Training Gym UI elements
        private Text _gymNoticeText;
        private Image _squatStationCardBg;
        private Image _benchStationCardBg;
        private Image _deadliftStationCardBg;

        // Squat Preparation UI elements
        private Text _prepSelectedWeightLabel;
        private Text _prepPlateBreakdownLabel;
        private Text _prepReadinessBadgeLabel;
        private Image _prepReadinessBadgeBg;
        private Button _prepSetupButton;
        private Text _prepSetupButtonLabel;
        private Button _prepUnrackButton;
        private readonly Dictionary<float, Image> _loadButtonBgs = new Dictionary<float, Image>();

        // Active Lift HUD elements
        private Text _hudLoadBadgeText;
        private Text _hudPhaseBadgeText;
        private Text _hudRefereeBannerText;
        private Image _hudRefereeBannerBg;
        private Text _hudInstructionText;
        private Image _hudDepthFillImage;
        private Text _hudDepthValueText;
        private Text _hudCameraAngleButtonText;

        // Result Screen UI elements
        private Image[] _resultRefereeLamps;
        private Text _resultHeadlineText;
        private Text _resultDetailsText;
        private Text _resultMetricsText;
        private Image _resultVerdictBannerBg;

        // Settings Modal UI elements
        private Text _settingsFullscreenText;
        private Text _settingsResolutionText;
        private Text _settingsVSyncText;
        private Text _settingsMsaaText;
        private Text _settingsShadowsText;
        private Text _settingsVolumeText;
        private Text _settingsMuteText;
        private Text _settingsCameraText;
        private Text _settingsTelemetryText;

        public PowerliftingScreenState CurrentScreen => _currentScreen;
        public bool IsSettingsOpen => _settingsOpen;
        public bool QuitRequested => _quitRequested;
        public string GymStationNotice => _gymStationNotice;
        public string DisplayedResultHeadline => _resultHeadlineText != null ? _resultHeadlineText.text : string.Empty;
        public string DisplayedResultDetails => _resultDetailsText != null ? _resultDetailsText.text : string.Empty;
        public string DisplayedRefereeCommand => _hudRefereeBannerText != null ? _hudRefereeBannerText.text : string.Empty;

        public static void ClearPendingReloadScreen()
        {
            s_nextScreenAfterReload = null;
        }

        public static void SetPendingReloadScreen(PowerliftingScreenState state)
        {
            s_nextScreenAfterReload = state;
        }

        public void RefreshUiImmediate()
        {
            RefreshActiveScreenElements();
        }

        private void Awake()
        {
            ResolveDependencies();
            EnsureEventSystem();
            BuildUserInterface();

            PowerliftingScreenState initialScreen = s_nextScreenAfterReload ?? PowerliftingScreenState.MainMenu;
            s_nextScreenAfterReload = null;
            TransitionToScreen(initialScreen, true);
        }

        private void ResolveDependencies()
        {
            if (squatController == null)
                squatController = FindFirstObjectByType<SquatPhysicalPrototypeController>();
            if (settingsManager == null)
            {
                settingsManager = FindFirstObjectByType<PowerliftingSettingsManager>();
                if (settingsManager == null)
                    settingsManager = gameObject.AddComponent<PowerliftingSettingsManager>();
            }
            if (audioDirector == null)
            {
                audioDirector = FindFirstObjectByType<PowerliftingAudioDirector>();
                if (audioDirector == null)
                    audioDirector = gameObject.AddComponent<PowerliftingAudioDirector>();
            }
            if (cameraDirector == null)
            {
                cameraDirector = FindFirstObjectByType<PowerliftingCameraDirector>();
                if (cameraDirector == null)
                {
                    Camera mainCam = Camera.main ?? FindFirstObjectByType<Camera>();
                    if (mainCam != null)
                        cameraDirector = mainCam.gameObject.AddComponent<PowerliftingCameraDirector>();
                }
            }
            if (gymEnvironment == null && squatController != null)
            {
                PhysicalBarbell bar = FindFirstObjectByType<PhysicalBarbell>();
                gymEnvironment = PowerliftingGymEnvironment.EnsureInScene(squatController, bar);
            }
        }

        private static void EnsureEventSystem()
        {
            if (Application.isBatchMode)
                return;

            EventSystem existing = FindFirstObjectByType<EventSystem>();
            if (existing != null)
            {
                if (existing.GetComponent<InputSystemUIInputModule>() == null)
                    existing.gameObject.AddComponent<InputSystemUIInputModule>();
                return;
            }

            GameObject esObj = new GameObject("EventSystem_GAM65");
            esObj.AddComponent<EventSystem>();
            esObj.AddComponent<InputSystemUIInputModule>();
        }

        private void Update()
        {
            ResolveDependencies();
            HandleHotkeys();
            SyncWithAuthoritativeSquatLifecycle();
            RefreshActiveScreenElements();
        }

        private void HandleHotkeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.f12Key.wasPressedThisFrame && settingsManager != null)
            {
                settingsManager.SetDeveloperTelemetry(!settingsManager.DeveloperTelemetryEnabled);
                RefreshSettingsModalLabels();
            }
        }

        private void SyncWithAuthoritativeSquatLifecycle()
        {
            if (squatController == null || !squatController.IsInitialized)
                return;

            SquatPlayerGamePhase phase = squatController.PlayerGamePhase;
            bool attemptStarted = squatController.AttemptOrchestrator != null && squatController.AttemptOrchestrator.HasStarted;
            SquatAttemptRecord record = squatController.AttemptRecord;

            // If an attempt has reached its final RESULT state (after rerack or terminal abort/failure), present the Result screen.
            if (phase == SquatPlayerGamePhase.RESULT ||
                (record != null && phase == SquatPlayerGamePhase.LOAD))
            {
                if (_currentScreen != PowerliftingScreenState.AttemptResult)
                    TransitionToScreen(PowerliftingScreenState.AttemptResult, false);
                return;
            }

            // If walkout or judged squat is active, ensure the ActiveLift HUD is visible.
            bool inActiveMotion =
                phase == SquatPlayerGamePhase.UNRACK ||
                phase == SquatPlayerGamePhase.WALKOUT_STEP_1 ||
                phase == SquatPlayerGamePhase.WALKOUT_STEP_2 ||
                phase == SquatPlayerGamePhase.SETTLE ||
                phase == SquatPlayerGamePhase.PHYSICS_HANDOFF ||
                phase == SquatPlayerGamePhase.JUDGED_SQUAT ||
                phase == SquatPlayerGamePhase.LOCKOUT ||
                phase == SquatPlayerGamePhase.RERACK ||
                (attemptStarted && record == null);

            if (inActiveMotion && _currentScreen != PowerliftingScreenState.ActiveLift)
            {
                TransitionToScreen(PowerliftingScreenState.ActiveLift, false);
                return;
            }

            // If player triggered SETUP via keyboard while on MainMenu or TrainingGym, bring them to SquatPreparation.
            if (phase == SquatPlayerGamePhase.SETUP &&
                (_currentScreen == PowerliftingScreenState.MainMenu || _currentScreen == PowerliftingScreenState.TrainingGym))
            {
                TransitionToScreen(PowerliftingScreenState.SquatPreparation, false);
            }
        }

        public void TransitionToScreen(PowerliftingScreenState targetScreen, bool snapCamera)
        {
            _currentScreen = targetScreen;
            if (_mainMenuPanel != null) _mainMenuPanel.SetActive(targetScreen == PowerliftingScreenState.MainMenu);
            if (_trainingGymPanel != null) _trainingGymPanel.SetActive(targetScreen == PowerliftingScreenState.TrainingGym);
            if (_squatPrepPanel != null) _squatPrepPanel.SetActive(targetScreen == PowerliftingScreenState.SquatPreparation);
            if (_activeLiftPanel != null) _activeLiftPanel.SetActive(targetScreen == PowerliftingScreenState.ActiveLift);
            if (_resultPanel != null) _resultPanel.SetActive(targetScreen == PowerliftingScreenState.AttemptResult);

            if (cameraDirector != null)
                cameraDirector.SetScreenState(targetScreen, snapCamera);

            RefreshActiveScreenElements();
        }

        // --- Public Player Navigation & Actions ---

        public void EnterTrainingGym()
        {
            audioDirector?.PlayUiClick();
            _settingsOpen = false;
            if (_settingsModalPanel != null) _settingsModalPanel.SetActive(false);

            if (squatController != null && squatController.AttemptRecord != null)
            {
                s_nextScreenAfterReload = PowerliftingScreenState.TrainingGym;
                squatController.RetryFromPlayerUi();
                return;
            }

            _gymStationNotice =
                "Select the Competition Squat Station (Center) to configure the barbell and perform a physically judged squat. Bench Press and Deadlift stations are environmental previews in D0.";
            TransitionToScreen(PowerliftingScreenState.TrainingGym, false);
            cameraDirector?.SetGymStationFocus(GymStationFocus.AllStationsOverview, false);
            UpdateGymStationCardHighlights(GymStationFocus.AllStationsOverview);
        }

        public void StartQuickLift()
        {
            audioDirector?.PlayUiClick();
            _settingsOpen = false;
            if (_settingsModalPanel != null) _settingsModalPanel.SetActive(false);

            if (squatController != null && squatController.IsInitialized &&
                squatController.PlayerGamePhase == SquatPlayerGamePhase.LOAD &&
                !squatController.AttemptOrchestrator.HasStarted &&
                squatController.CurrentLoadKg < 25f)
            {
                squatController.SelectLoadFromPlayerUi(25f);
            }

            TransitionToScreen(PowerliftingScreenState.SquatPreparation, false);
        }

        public void ReturnToMainMenu()
        {
            audioDirector?.PlayUiClick();
            _settingsOpen = false;
            if (_settingsModalPanel != null) _settingsModalPanel.SetActive(false);

            if (squatController != null &&
                (squatController.AttemptRecord != null ||
                 squatController.PlayerGamePhase != SquatPlayerGamePhase.LOAD ||
                 (squatController.AttemptOrchestrator != null && squatController.AttemptOrchestrator.HasStarted)))
            {
                s_nextScreenAfterReload = PowerliftingScreenState.MainMenu;
                squatController.RetryFromPlayerUi();
                return;
            }

            TransitionToScreen(PowerliftingScreenState.MainMenu, false);
        }

        public void SelectSquatStation()
        {
            audioDirector?.PlayUiClick();
            cameraDirector?.SetGymStationFocus(GymStationFocus.SquatStation, false);
            UpdateGymStationCardHighlights(GymStationFocus.SquatStation);
            TransitionToScreen(PowerliftingScreenState.SquatPreparation, false);
        }

        public void InspectBenchStationUnavailable()
        {
            audioDirector?.PlayUiClick();
            cameraDirector?.SetGymStationFocus(GymStationFocus.BenchStation, false);
            UpdateGymStationCardHighlights(GymStationFocus.BenchStation);
            _gymStationNotice =
                "BENCH PRESS STATION — NOT AVAILABLE IN D0: Environmental competition bench rack & 115 kg barbell staged for upcoming lift milestones. Select the Competition Squat Station to lift.";
            if (_gymNoticeText != null)
                _gymNoticeText.text = _gymStationNotice;
        }

        public void InspectDeadliftStationUnavailable()
        {
            audioDirector?.PlayUiClick();
            cameraDirector?.SetGymStationFocus(GymStationFocus.DeadliftStation, false);
            UpdateGymStationCardHighlights(GymStationFocus.DeadliftStation);
            _gymStationNotice =
                "DEADLIFT PLATFORM — NOT AVAILABLE IN D0: Environmental oak/rubber deadlift deck, 165 kg grounded bar & bar jack staged for upcoming lift milestones. Select the Competition Squat Station to lift.";
            if (_gymNoticeText != null)
                _gymNoticeText.text = _gymStationNotice;
        }

        public void SelectBarbellWeight(float loadKg)
        {
            if (squatController == null || !squatController.IsInitialized)
                return;
            if (squatController.PlayerGamePhase != SquatPlayerGamePhase.LOAD ||
                squatController.AttemptOrchestrator.HasStarted)
                return;

            s_nextScreenAfterReload = _currentScreen;
            squatController.SelectLoadFromPlayerUi(loadKg);
            RefreshSquatPreparationPanel();
        }

        public void BeginPlayerSetupAction()
        {
            if (squatController == null || !squatController.IsInitialized)
                return;

            audioDirector?.PlayUiClick();
            if (squatController.PlayerGamePhase == SquatPlayerGamePhase.LOAD && squatController.CanBeginPlayerSetup)
            {
                squatController.StartGameFromPlayerUi();
                RefreshSquatPreparationPanel();
            }
        }

        public void BeginUnrackAndWalkoutAction()
        {
            if (squatController == null || !squatController.IsInitialized)
                return;

            audioDirector?.PlayUiClick();
            if (squatController.PlayerGamePhase == SquatPlayerGamePhase.LOAD && squatController.CanBeginPlayerSetup)
                squatController.StartGameFromPlayerUi();

            if (squatController.PlayerGamePhase == SquatPlayerGamePhase.SETUP)
            {
                squatController.UnrackFromPlayerUi();
                TransitionToScreen(PowerliftingScreenState.ActiveLift, false);
            }
        }

        public void AbortActiveAttemptAction()
        {
            if (squatController == null || !squatController.IsInitialized || squatController.AttemptOrchestrator == null)
                return;

            audioDirector?.PlayUiClick();
            if (squatController.AttemptOrchestrator.HasSquatCommand && !squatController.AttemptOrchestrator.IsTruthFrozen)
            {
                squatController.AbortAttempt();
                TransitionToScreen(PowerliftingScreenState.AttemptResult, false);
            }
            else
            {
                RetryToSquatPreparation();
            }
        }

        public void RetryToSquatPreparation()
        {
            audioDirector?.PlayUiClick();
            s_nextScreenAfterReload = PowerliftingScreenState.SquatPreparation;
            squatController?.RetryFromPlayerUi();
        }

        public void RetryToTrainingGym()
        {
            audioDirector?.PlayUiClick();
            s_nextScreenAfterReload = PowerliftingScreenState.TrainingGym;
            squatController?.RetryFromPlayerUi();
        }

        public void RetryToMainMenu()
        {
            audioDirector?.PlayUiClick();
            s_nextScreenAfterReload = PowerliftingScreenState.MainMenu;
            squatController?.RetryFromPlayerUi();
        }

        public void OpenSettingsModal()
        {
            audioDirector?.PlayUiClick();
            _settingsOpen = true;
            if (_settingsModalPanel != null)
            {
                _settingsModalPanel.SetActive(true);
                _settingsModalPanel.transform.SetAsLastSibling();
            }
            RefreshSettingsModalLabels();
        }

        public void CloseSettingsModal()
        {
            audioDirector?.PlayUiClick();
            _settingsOpen = false;
            if (_settingsModalPanel != null)
                _settingsModalPanel.SetActive(false);
        }

        public void QuitGame()
        {
            audioDirector?.PlayUiClick();
            _quitRequested = true;
#if UNITY_EDITOR
            if (!Application.isBatchMode)
                UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // --- Dynamic UI Refresh ---

        private void RefreshActiveScreenElements()
        {
            switch (_currentScreen)
            {
                case PowerliftingScreenState.TrainingGym:
                    if (_gymNoticeText != null)
                        _gymNoticeText.text = _gymStationNotice;
                    break;
                case PowerliftingScreenState.SquatPreparation:
                    RefreshSquatPreparationPanel();
                    break;
                case PowerliftingScreenState.ActiveLift:
                    RefreshActiveLiftHud();
                    break;
                case PowerliftingScreenState.AttemptResult:
                    RefreshResultScreen();
                    break;
            }

            if (_settingsOpen)
                RefreshSettingsModalLabels();
        }

        private void UpdateGymStationCardHighlights(GymStationFocus focus)
        {
            if (_squatStationCardBg != null)
                _squatStationCardBg.color = focus == GymStationFocus.SquatStation || focus == GymStationFocus.AllStationsOverview
                    ? new Color(0.14f, 0.18f, 0.24f, 0.96f)
                    : new Color(0.08f, 0.10f, 0.14f, 0.92f);
            if (_benchStationCardBg != null)
                _benchStationCardBg.color = focus == GymStationFocus.BenchStation
                    ? new Color(0.22f, 0.16f, 0.12f, 0.96f)
                    : new Color(0.08f, 0.10f, 0.14f, 0.90f);
            if (_deadliftStationCardBg != null)
                _deadliftStationCardBg.color = focus == GymStationFocus.DeadliftStation
                    ? new Color(0.22f, 0.16f, 0.12f, 0.96f)
                    : new Color(0.08f, 0.10f, 0.14f, 0.90f);
        }

        private void RefreshSquatPreparationPanel()
        {
            if (squatController == null)
                return;

            float loadKg = squatController.CurrentLoadKg;
            SquatPlayerGamePhase phase = squatController.PlayerGamePhase;
            bool canSetup = squatController.CanBeginPlayerSetup;

            if (_prepSelectedWeightLabel != null)
                _prepSelectedWeightLabel.text = string.Format(CultureInfo.InvariantCulture, "SELECTED BAR LOAD: {0:0} KG", loadKg);

            if (_prepPlateBreakdownLabel != null)
                _prepPlateBreakdownLabel.text = DescribePlateBreakdown(loadKg);

            foreach (KeyValuePair<float, Image> pair in _loadButtonBgs)
            {
                if (pair.Value == null)
                    continue;
                bool selected = Mathf.Approximately(pair.Key, loadKg);
                pair.Value.color = selected
                    ? new Color(0.76f, 0.15f, 0.18f, 1f)
                    : new Color(0.15f, 0.18f, 0.23f, 0.95f);
            }

            if (phase == SquatPlayerGamePhase.SETUP)
            {
                if (_prepReadinessBadgeLabel != null)
                    _prepReadinessBadgeLabel.text = "STATUS: BRACED UNDER BAR — READY TO UNRACK";
                if (_prepReadinessBadgeBg != null)
                    _prepReadinessBadgeBg.color = new Color(0.14f, 0.52f, 0.34f, 0.96f);
                if (_prepSetupButton != null)
                    _prepSetupButton.interactable = false;
                if (_prepSetupButtonLabel != null)
                    _prepSetupButtonLabel.text = "1. SET UP COMPLETE";
                if (_prepUnrackButton != null)
                    _prepUnrackButton.interactable = true;
            }
            else if (loadKg < 25f)
            {
                if (_prepReadinessBadgeLabel != null)
                    _prepReadinessBadgeLabel.text = "SELECT AT LEAST 25 KG COMPETITION BAR TO BEGIN";
                if (_prepReadinessBadgeBg != null)
                    _prepReadinessBadgeBg.color = new Color(0.58f, 0.34f, 0.10f, 0.96f);
                if (_prepSetupButton != null)
                    _prepSetupButton.interactable = false;
                if (_prepSetupButtonLabel != null)
                    _prepSetupButtonLabel.text = "1. SET UP UNDER BAR";
                if (_prepUnrackButton != null)
                    _prepUnrackButton.interactable = false;
            }
            else if (canSetup)
            {
                if (_prepReadinessBadgeLabel != null)
                    _prepReadinessBadgeLabel.text = "STATUS: ATHLETE STABLE — READY FOR SETUP & WALKOUT";
                if (_prepReadinessBadgeBg != null)
                    _prepReadinessBadgeBg.color = new Color(0.12f, 0.48f, 0.32f, 0.96f);
                if (_prepSetupButton != null)
                    _prepSetupButton.interactable = true;
                if (_prepSetupButtonLabel != null)
                    _prepSetupButtonLabel.text = "1. SET UP UNDER BAR";
                if (_prepUnrackButton != null)
                    _prepUnrackButton.interactable = true;
            }
            else
            {
                if (_prepReadinessBadgeLabel != null)
                    _prepReadinessBadgeLabel.text = "STATUS: STABILIZING STANDING START POSE...";
                if (_prepReadinessBadgeBg != null)
                    _prepReadinessBadgeBg.color = new Color(0.45f, 0.36f, 0.14f, 0.96f);
                if (_prepSetupButton != null)
                    _prepSetupButton.interactable = false;
                if (_prepSetupButtonLabel != null)
                    _prepSetupButtonLabel.text = "1. SET UP UNDER BAR";
                if (_prepUnrackButton != null)
                    _prepUnrackButton.interactable = false;
            }
        }

        private static string DescribePlateBreakdown(float loadKg)
        {
            if (loadKg <= 0f)
                return "Unloaded standing calibration (no bar attached). Select 25 kg, 60 kg, or 105 kg for a competition attempt.";
            if (Mathf.Approximately(loadKg, 25f))
                return "IPF 20.0 kg Competition Power Bar + 2 x 2.5 kg Competition Collars (Empty Bar + Collars).";
            if (Mathf.Approximately(loadKg, 60f))
                return "IPF 20.0 kg Bar + 2 x 2.5 kg Collars + Per Side: 1 x 15 kg (Yellow) + 1 x 2.5 kg (Black).";
            if (Mathf.Approximately(loadKg, 105f))
                return "IPF 20.0 kg Bar + 2 x 2.5 kg Collars + Per Side: 1 x 25 kg (Red) + 1 x 15 kg (Yellow).";
            return string.Format(CultureInfo.InvariantCulture, "IPF 20.0 kg Bar + 2 x 2.5 kg Collars + {0:0.0} kg calibrated competition plates.", Mathf.Max(0f, loadKg - 25f));
        }

        private void RefreshActiveLiftHud()
        {
            if (squatController == null || !squatController.IsInitialized)
                return;

            float loadKg = squatController.CurrentLoadKg;
            SquatPlayerGamePhase gamePhase = squatController.PlayerGamePhase;
            SquatAttemptOrchestrator orchestrator = squatController.AttemptOrchestrator;
            SquatAttemptLifecycleState lifecycleState = orchestrator != null
                ? orchestrator.Lifecycle.State
                : SquatAttemptLifecycleState.IDLE;
            SquatState adapterState = squatController.Adapter != null ? squatController.Adapter.State : SquatState.SETUP;
            float sq = squatController.Adapter != null ? Mathf.Clamp01(squatController.Adapter.Sq) : 0f;

            if (_hudLoadBadgeText != null)
                _hudLoadBadgeText.text = string.Format(CultureInfo.InvariantCulture, "COMPETITION SQUAT  ·  {0:0} KG", loadKg);

            string phaseText = FormatActivePhaseBadge(gamePhase, lifecycleState, adapterState);
            if (_hudPhaseBadgeText != null)
                _hudPhaseBadgeText.text = "PHASE: " + phaseText;

            GetRefereeBannerAndPrompt(
                gamePhase,
                lifecycleState,
                adapterState,
                out string refereeCallout,
                out string playerPrompt,
                out Color bannerColor);

            if (_hudRefereeBannerText != null)
                _hudRefereeBannerText.text = refereeCallout;
            if (_hudRefereeBannerBg != null)
                _hudRefereeBannerBg.color = bannerColor;
            if (_hudInstructionText != null)
                _hudInstructionText.text = playerPrompt;

            if (_hudDepthFillImage != null)
            {
                _hudDepthFillImage.fillAmount = sq;
                bool depthReached = sq >= 0.85f || adapterState == SquatState.BOTTOM || adapterState == SquatState.REVERSAL || adapterState == SquatState.ASCENT || adapterState == SquatState.LOCKOUT;
                _hudDepthFillImage.color = depthReached
                    ? new Color(0.18f, 0.78f, 0.44f, 1f)
                    : new Color(0.92f, 0.62f, 0.14f, 1f);
            }
            if (_hudDepthValueText != null)
                _hudDepthValueText.text = string.Format(CultureInfo.InvariantCulture, "DEPTH\n{0:0}%", sq * 100f);

            if (_hudCameraAngleButtonText != null && cameraDirector != null)
                _hudCameraAngleButtonText.text = "CAMERA [C]: " + PowerliftingCameraDirector.GetCameraAngleLabel(cameraDirector.ActiveLiftAngle);
        }

        private static string FormatActivePhaseBadge(
            SquatPlayerGamePhase gamePhase,
            SquatAttemptLifecycleState lifecycleState,
            SquatState adapterState)
        {
            switch (gamePhase)
            {
                case SquatPlayerGamePhase.UNRACK: return "UNRACKING BAR";
                case SquatPlayerGamePhase.WALKOUT_STEP_1: return "WALKOUT — STEP 1 OF 2";
                case SquatPlayerGamePhase.WALKOUT_STEP_2: return "WALKOUT — STEP 2 OF 2";
                case SquatPlayerGamePhase.SETTLE: return "SETTLING STANCE";
                case SquatPlayerGamePhase.PHYSICS_HANDOFF: return "PHYSICS AUTHORITY HANDOFF";
                case SquatPlayerGamePhase.RERACK: return "RERACKING TO STANDS";
                case SquatPlayerGamePhase.LOCKOUT: return "STANDING LOCKOUT";
            }

            if (lifecycleState == SquatAttemptLifecycleState.START_WINDOW)
                return "REFEREE START CHECK";
            if (lifecycleState == SquatAttemptLifecycleState.PHYSICAL_TERMINAL_CONDITION)
                return "LOCKOUT VERIFICATION";

            return adapterState.ToString().Replace('_', ' ');
        }

        private static void GetRefereeBannerAndPrompt(
            SquatPlayerGamePhase gamePhase,
            SquatAttemptLifecycleState lifecycleState,
            SquatState adapterState,
            out string refereeCallout,
            out string playerPrompt,
            out Color bannerColor)
        {
            if (gamePhase == SquatPlayerGamePhase.UNRACK ||
                gamePhase == SquatPlayerGamePhase.WALKOUT_STEP_1 ||
                gamePhase == SquatPlayerGamePhase.WALKOUT_STEP_2 ||
                gamePhase == SquatPlayerGamePhase.SETTLE ||
                gamePhase == SquatPlayerGamePhase.PHYSICS_HANDOFF)
            {
                refereeCallout = "WALKOUT IN PROGRESS — ESTABLISHING STANCE";
                playerPrompt = "Athlete is stepping clear of the combo rack and stabilizing the bar.";
                bannerColor = new Color(0.16f, 0.24f, 0.36f, 0.95f);
                return;
            }

            if (gamePhase == SquatPlayerGamePhase.RERACK)
            {
                refereeCallout = "RACK! — RETURNING BAR TO CRADLES";
                playerPrompt = "Physical lockout verified. Walking the bar back into the combo rack.";
                bannerColor = new Color(0.14f, 0.48f, 0.34f, 0.96f);
                return;
            }

            if (lifecycleState == SquatAttemptLifecycleState.START_WINDOW)
            {
                refereeCallout = "HOLD STEADY FOR HEAD REFEREE START SIGNAL";
                playerPrompt = "Maintain upright posture and quiet knees while the start predicate verifies.";
                bannerColor = new Color(0.48f, 0.36f, 0.12f, 0.95f);
                return;
            }

            if (lifecycleState == SquatAttemptLifecycleState.PHYSICAL_TERMINAL_CONDITION ||
                gamePhase == SquatPlayerGamePhase.LOCKOUT)
            {
                refereeCallout = "HOLD LOCKOUT — AWAITING RACK COMMAND";
                playerPrompt = "Keep standing tall while the referees finalize lockout stability.";
                bannerColor = new Color(0.14f, 0.48f, 0.34f, 0.96f);
                return;
            }

            switch (adapterState)
            {
                case SquatState.SQUAT_COMMAND:
                    refereeCallout = "REFEREE: \"SQUAT!\" — BEGIN DESCENT";
                    playerPrompt = "Press and hold [S] or [Down Arrow] to yield into the squat descent.";
                    bannerColor = new Color(0.14f, 0.52f, 0.32f, 0.96f);
                    return;
                case SquatState.DESCENT:
                    refereeCallout = "DESCENDING — REACH IPF LEGAL DEPTH";
                    playerPrompt = "Continue holding [S] or [Down Arrow] until the depth meter turns green.";
                    bannerColor = new Color(0.55f, 0.38f, 0.10f, 0.96f);
                    return;
                case SquatState.BOTTOM:
                case SquatState.REVERSAL:
                    refereeCallout = "LEGAL DEPTH REACHED — DRIVE UP!";
                    playerPrompt = "Release [S] and hold [W] or [Up Arrow] to drive out of the hole!";
                    bannerColor = new Color(0.12f, 0.56f, 0.34f, 0.96f);
                    return;
                case SquatState.ASCENT:
                case SquatState.STICKING:
                    refereeCallout = "ASCENDING — DRIVE THROUGH THE STICKING POINT!";
                    playerPrompt = "Keep holding [W] or [Up Arrow] until full standing extension.";
                    bannerColor = new Color(0.16f, 0.44f, 0.62f, 0.96f);
                    return;
                case SquatState.LOCKOUT:
                case SquatState.RACK_COMMAND:
                    refereeCallout = "REFEREE: \"RACK!\" — LOCKOUT COMPLETE";
                    playerPrompt = "Stand tall and hold steady as the attempt concludes.";
                    bannerColor = new Color(0.14f, 0.52f, 0.34f, 0.96f);
                    return;
                default:
                    refereeCallout = "PREPARE FOR SQUAT COMMAND";
                    playerPrompt = "Hold [S] / [↓] to descend on command, then hold [W] / [↑] to drive up.";
                    bannerColor = new Color(0.16f, 0.22f, 0.30f, 0.95f);
                    return;
            }
        }

        private void RefreshResultScreen()
        {
            if (squatController == null)
                return;

            SquatAttemptRecord record = squatController.AttemptRecord;
            string formattedResult = record == null
                ? "RESULT UNAVAILABLE\nThe physical start position was not reached."
                : SquatPhysicalPrototypeController.FormatPlayerResult(record);

            string[] lines = formattedResult.Split('\n');
            string headline = lines.Length > 0 ? lines[0] : formattedResult;
            string sublines = lines.Length > 1 ? string.Join("\n", lines, 1, lines.Length - 1) : string.Empty;

            bool isGoodLift = record != null &&
                record.TerminalReason == SquatAttemptTerminalReason.PHYSICAL_LOCKOUT &&
                record.RuleOutcome == SquatJudgmentOutcome.GOOD_LIFT &&
                record.PhysicalFailureOutcome == SquatFailureResultKind.NO_PHYSICAL_FAILURE;

            if (_resultHeadlineText != null)
                _resultHeadlineText.text = headline;

            if (_resultVerdictBannerBg != null)
            {
                _resultVerdictBannerBg.color = isGoodLift
                    ? new Color(0.12f, 0.48f, 0.30f, 0.98f)
                    : new Color(0.64f, 0.12f, 0.15f, 0.98f);
            }

            if (_resultRefereeLamps != null)
            {
                Color lampColor = isGoodLift
                    ? new Color(0.96f, 0.97f, 1.0f, 1f)
                    : new Color(0.92f, 0.16f, 0.18f, 1f);
                for (int i = 0; i < _resultRefereeLamps.Length; i++)
                {
                    if (_resultRefereeLamps[i] != null)
                        _resultRefereeLamps[i].color = lampColor;
                }
            }

            if (_resultDetailsText != null)
            {
                if (string.IsNullOrEmpty(sublines))
                {
                    _resultDetailsText.text = isGoodLift
                        ? "3 White Lights — Unanimous IPF Good Lift. Legal bilateral hip-below-knee depth and controlled physical lockout verified."
                        : "Attempt did not satisfy competition requirements.";
                }
                else
                {
                    _resultDetailsText.text = sublines;
                }
            }

            if (_resultMetricsText != null)
            {
                if (record != null)
                {
                    string ruleViolation = record.Judgment.PrimaryViolationKind == SquatRuleViolationKind.NONE
                        ? "None"
                        : record.Judgment.PrimaryViolationKind.ToString().Replace('_', ' ');
                    string physFailure = record.FailureResult.PrimaryFailureKind == SquatFailureKind.NONE
                        ? "None"
                        : record.FailureResult.PrimaryFailureKind.ToString().Replace('_', ' ');
                    _resultMetricsText.text = string.Format(
                        CultureInfo.InvariantCulture,
                        "BAR LOAD: {0:0} KG   ·   TERMINAL: {1}   ·   RULE VERDICT: {2} (Violation: {3})   ·   PHYSICAL: {4} ({5})",
                        squatController.CurrentLoadKg,
                        record.TerminalReason,
                        record.RuleOutcome,
                        ruleViolation,
                        record.PhysicalFailureOutcome,
                        physFailure);
                }
                else
                {
                    _resultMetricsText.text = string.Format(
                        CultureInfo.InvariantCulture,
                        "BAR LOAD: {0:0} KG   ·   No finalized attempt record.",
                        squatController.CurrentLoadKg);
                }
            }
        }

        private void RefreshSettingsModalLabels()
        {
            if (settingsManager == null)
                return;

            if (_settingsFullscreenText != null)
                _settingsFullscreenText.text = settingsManager.IsFullscreen ? "FULLSCREEN WINDOW" : "WINDOWED";
            if (_settingsResolutionText != null)
            {
                Vector2Int res = settingsManager.CurrentResolutionPreset;
                _settingsResolutionText.text = $"{res.x} x {res.y}";
            }
            if (_settingsVSyncText != null)
                _settingsVSyncText.text = settingsManager.VSyncEnabled ? "ENABLED (VSYNC)" : $"OFF ({settingsManager.TargetFrameRate} FPS CAP)";
            if (_settingsMsaaText != null)
                _settingsMsaaText.text = settingsManager.AntiAliasingSamples == 0 ? "OFF" : $"{settingsManager.AntiAliasingSamples}x MSAA";
            if (_settingsShadowsText != null)
                _settingsShadowsText.text = settingsManager.SoftShadowsEnabled ? "SOFT SHADOWS (HIGH)" : "HARD SHADOWS (FAST)";
            if (_settingsVolumeText != null)
                _settingsVolumeText.text = string.Format(CultureInfo.InvariantCulture, "{0:0}%", settingsManager.MasterVolume * 100f);
            if (_settingsMuteText != null)
                _settingsMuteText.text = settingsManager.AudioMuted ? "MUTED" : "ACTIVE";
            if (_settingsCameraText != null)
                _settingsCameraText.text = PowerliftingCameraDirector.GetCameraAngleLabel(settingsManager.PreferredCameraAngle);
            if (_settingsTelemetryText != null)
                _settingsTelemetryText.text = settingsManager.DeveloperTelemetryEnabled ? "VISIBLE (DIAGNOSTIC)" : "HIDDEN (PLAYER DEFAULT)";
        }

        // --- Programmatic uGUI Construction ---

        private void BuildUserInterface()
        {
            _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_uiFont == null)
                _uiFont = Font.CreateDynamicFontFromOSFont("Arial", 16);

            _canvas = GetComponent<Canvas>();
            if (_canvas == null)
                _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }

            BuildMainMenuScreen();
            BuildTrainingGymScreen();
            BuildSquatPreparationScreen();
            BuildActiveLiftScreen();
            BuildResultScreen();
            BuildSettingsModal();
        }

        private void BuildMainMenuScreen()
        {
            _mainMenuPanel = CreateScreenContainer("MainMenuScreen");

            // Left-side sleek dark glass card so the 3D gym and athlete remain prominently visible on the right
            RectTransform card = CreatePanel(
                _mainMenuPanel.transform,
                "MainMenuCard",
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(76f, 0f),
                new Vector2(640f, 840f),
                new Color(0.055f, 0.07f, 0.095f, 0.93f));

            CreateAccentBar(card, new Vector2(0f, 416f), new Vector2(640f, 8f), new Color(0.82f, 0.14f, 0.18f, 1f));

            CreateLabel(
                card,
                "FederationTag",
                "IPF TECHNICAL RULES  ·  PHYSICAL BIOMECHANICS ENGINE",
                new Vector2(44f, 355f),
                new Vector2(552f, 28f),
                15,
                FontStyle.Bold,
                new Color(0.88f, 0.66f, 0.22f, 1f),
                TextAnchor.MiddleLeft);

            CreateLabel(
                card,
                "GameTitle",
                "POWERLIFTING\nSIMULATOR",
                new Vector2(44f, 255f),
                new Vector2(552f, 132f),
                48,
                FontStyle.Bold,
                Color.white,
                TextAnchor.MiddleLeft);

            CreateLabel(
                card,
                "GameSubtitle",
                "Playable Windows Slice (D0) — Authentic 16-segment powered humanoid squat, calibrated 2.20 m competition barbell, walkout & referee judgment.",
                new Vector2(44f, 150f),
                new Vector2(552f, 68f),
                17,
                FontStyle.Normal,
                new Color(0.76f, 0.81f, 0.88f, 1f),
                TextAnchor.UpperLeft);

            CreateMenuButton(
                card,
                "PlayEnterGymButton",
                "PLAY  —  ENTER TRAINING GYM",
                new Vector2(44f, 48f),
                new Vector2(552f, 68f),
                new Color(0.78f, 0.14f, 0.18f, 1f),
                21,
                EnterTrainingGym);

            CreateMenuButton(
                card,
                "QuickLiftButton",
                "QUICK LIFT  (25 KG COMPETITION SQUAT)",
                new Vector2(44f, -38f),
                new Vector2(552f, 62f),
                new Color(0.16f, 0.22f, 0.30f, 0.98f),
                19,
                StartQuickLift);

            CreateMenuButton(
                card,
                "SettingsButton",
                "SETTINGS  (GRAPHICS · AUDIO · CAMERA)",
                new Vector2(44f, -118f),
                new Vector2(552f, 58f),
                new Color(0.13f, 0.16f, 0.22f, 0.98f),
                18,
                OpenSettingsModal);

            CreateMenuButton(
                card,
                "QuitButton",
                "QUIT TO DESKTOP",
                new Vector2(44f, -194f),
                new Vector2(552f, 54f),
                new Color(0.18f, 0.11f, 0.13f, 0.95f),
                17,
                QuitGame);

            CreateLabel(
                card,
                "FooterNote",
                "D0 Milestone Build  ·  Unity 6 URP  ·  100 Hz Deterministic Physics Tick\nControls: Mouse UI + [S/↓] Descend  ·  [W/↑] Drive  ·  [Space] Brace  ·  [C] Camera",
                new Vector2(44f, -345f),
                new Vector2(552f, 64f),
                14,
                FontStyle.Normal,
                new Color(0.56f, 0.62f, 0.70f, 1f),
                TextAnchor.MiddleLeft);
        }

        private void BuildTrainingGymScreen()
        {
            _trainingGymPanel = CreateScreenContainer("TrainingGymScreen");

            // Top header bar
            RectTransform header = CreatePanel(
                _trainingGymPanel.transform,
                "GymHeaderBar",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -24f),
                new Vector2(1820f, 82f),
                new Color(0.055f, 0.07f, 0.095f, 0.92f));

            CreateLabel(
                header,
                "GymHeaderTitle",
                "TRAINING GYM  —  SELECT A LIFTING STATION",
                new Vector2(-460f, 0f),
                new Vector2(820f, 54f),
                26,
                FontStyle.Bold,
                Color.white,
                TextAnchor.MiddleLeft);

            CreateMenuButton(
                header,
                "GymSettingsButton",
                "SETTINGS",
                new Vector2(540f, 0f),
                new Vector2(210f, 50f),
                new Color(0.15f, 0.19f, 0.25f, 1f),
                16,
                OpenSettingsModal,
                true);

            CreateMenuButton(
                header,
                "GymBackToMenuButton",
                "MAIN MENU",
                new Vector2(775f, 0f),
                new Vector2(210f, 50f),
                new Color(0.24f, 0.14f, 0.16f, 1f),
                16,
                ReturnToMainMenu,
                true);

            // Bottom station selector deck
            RectTransform bottomDeck = CreatePanel(
                _trainingGymPanel.transform,
                "GymStationDeck",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 24f),
                new Vector2(1820f, 350f),
                new Color(0.04f, 0.05f, 0.07f, 0.88f));

            _gymNoticeText = CreateLabel(
                bottomDeck,
                "GymStationNotice",
                _gymStationNotice,
                new Vector2(0f, 136f),
                new Vector2(1740f, 44f),
                17,
                FontStyle.Bold,
                new Color(0.92f, 0.82f, 0.48f, 1f),
                TextAnchor.MiddleCenter);

            // Left Card: Bench Press (Unavailable in D0)
            RectTransform benchCard = CreateStationCard(
                bottomDeck,
                "BenchStationCard",
                new Vector2(-600f, -24f),
                new Vector2(550f, 248f),
                "BENCH PRESS STATION",
                "NOT AVAILABLE IN D0 BUILD",
                new Color(0.62f, 0.38f, 0.12f, 1f),
                "Competition bench rack & 115 kg display barbell staged in the left bay for upcoming lift milestones.",
                "PREVIEW STATION (UNAVAILABLE IN D0)",
                new Color(0.22f, 0.25f, 0.30f, 1f),
                InspectBenchStationUnavailable,
                out _benchStationCardBg);

            // Center Card: Competition Squat (Available & Qualified)
            RectTransform squatCard = CreateStationCard(
                bottomDeck,
                "SquatStationCard",
                new Vector2(0f, -24f),
                new Vector2(580f, 248f),
                "COMPETITION SQUAT STATION",
                "PLAYABLE & QUALIFIED IN D0",
                new Color(0.14f, 0.56f, 0.34f, 1f),
                "Full physical squat lifecycle: Load Selection (25–105 kg), Setup, 2-Step Walkout, Judged Squat, Lockout & Rerack.",
                "SELECT SQUAT STATION  —  PREPARE LIFT",
                new Color(0.78f, 0.14f, 0.18f, 1f),
                SelectSquatStation,
                out _squatStationCardBg);

            // Right Card: Deadlift Platform (Unavailable in D0)
            RectTransform deadliftCard = CreateStationCard(
                bottomDeck,
                "DeadliftStationCard",
                new Vector2(600f, -24f),
                new Vector2(550f, 248f),
                "DEADLIFT PLATFORM",
                "NOT AVAILABLE IN D0 BUILD",
                new Color(0.62f, 0.38f, 0.12f, 1f),
                "Oak/rubber deadlift deck, 165 kg grounded competition barbell & jack staged in the right bay for future milestones.",
                "PREVIEW STATION (UNAVAILABLE IN D0)",
                new Color(0.22f, 0.25f, 0.30f, 1f),
                InspectDeadliftStationUnavailable,
                out _deadliftStationCardBg);
        }

        private RectTransform CreateStationCard(
            Transform parent,
            string name,
            Vector2 anchoredPos,
            Vector2 size,
            string title,
            string badgeText,
            Color badgeColor,
            string description,
            string buttonText,
            Color buttonColor,
            UnityEngine.Events.UnityAction onClick,
            out Image cardBg)
        {
            RectTransform card = CreatePanel(
                parent,
                name,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                anchoredPos,
                size,
                new Color(0.09f, 0.11f, 0.15f, 0.96f));
            cardBg = card.GetComponent<Image>();

            RectTransform badge = CreatePanel(
                card,
                "StatusBadge",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -18f),
                new Vector2(size.x - 36f, 30f),
                badgeColor);

            CreateLabel(
                badge,
                "BadgeLabel",
                badgeText,
                Vector2.zero,
                new Vector2(size.x - 48f, 28f),
                14,
                FontStyle.Bold,
                Color.white,
                TextAnchor.MiddleCenter);

            CreateLabel(
                card,
                "StationTitle",
                title,
                new Vector2(0f, 52f),
                new Vector2(size.x - 40f, 36f),
                22,
                FontStyle.Bold,
                Color.white,
                TextAnchor.MiddleCenter);

            CreateLabel(
                card,
                "StationDesc",
                description,
                new Vector2(0f, -4f),
                new Vector2(size.x - 44f, 64f),
                15,
                FontStyle.Normal,
                new Color(0.76f, 0.81f, 0.88f, 1f),
                TextAnchor.MiddleCenter);

            CreateMenuButton(
                card,
                "ActionBtn",
                buttonText,
                new Vector2(0f, -82f),
                new Vector2(size.x - 40f, 54f),
                buttonColor,
                16,
                onClick,
                true);

            return card;
        }

        private void BuildSquatPreparationScreen()
        {
            _squatPrepPanel = CreateScreenContainer("SquatPreparationScreen");

            // Top bar
            RectTransform topBar = CreatePanel(
                _squatPrepPanel.transform,
                "PrepTopBar",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -24f),
                new Vector2(1820f, 78f),
                new Color(0.055f, 0.07f, 0.095f, 0.92f));

            CreateLabel(
                topBar,
                "PrepHeaderTitle",
                "SQUAT PREPARATION  —  CONFIGURE BARBELL & START ATTEMPT",
                new Vector2(-420f, 0f),
                new Vector2(900f, 50f),
                24,
                FontStyle.Bold,
                Color.white,
                TextAnchor.MiddleLeft);

            CreateMenuButton(
                topBar,
                "PrepToGymButton",
                "TRAINING GYM",
                new Vector2(330f, 0f),
                new Vector2(200f, 48f),
                new Color(0.15f, 0.19f, 0.25f, 1f),
                15,
                EnterTrainingGym,
                true);

            CreateMenuButton(
                topBar,
                "PrepSettingsButton",
                "SETTINGS",
                new Vector2(550f, 0f),
                new Vector2(180f, 48f),
                new Color(0.15f, 0.19f, 0.25f, 1f),
                15,
                OpenSettingsModal,
                true);

            CreateMenuButton(
                topBar,
                "PrepMainMenuButton",
                "MAIN MENU",
                new Vector2(760f, 0f),
                new Vector2(190f, 48f),
                new Color(0.24f, 0.14f, 0.16f, 1f),
                15,
                ReturnToMainMenu,
                true);

            // Left Panel: Weight Selection & Plate Breakdown
            RectTransform leftPanel = CreatePanel(
                _squatPrepPanel.transform,
                "WeightSelectionPanel",
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(48f, -36f),
                new Vector2(540f, 820f),
                new Color(0.055f, 0.07f, 0.095f, 0.93f));

            CreateAccentBar(leftPanel, new Vector2(0f, 406f), new Vector2(540f, 6f), new Color(0.82f, 0.14f, 0.18f, 1f));

            CreateLabel(
                leftPanel,
                "WeightSectionHeader",
                "1. SELECT BARBELL LOAD",
                new Vector2(32f, 355f),
                new Vector2(476f, 34f),
                20,
                FontStyle.Bold,
                new Color(0.92f, 0.72f, 0.26f, 1f),
                TextAnchor.MiddleLeft);

            _prepSelectedWeightLabel = CreateLabel(
                leftPanel,
                "SelectedWeightValue",
                "SELECTED BAR LOAD: 25 KG",
                new Vector2(32f, 308f),
                new Vector2(476f, 40f),
                26,
                FontStyle.Bold,
                Color.white,
                TextAnchor.MiddleLeft);

            _prepPlateBreakdownLabel = CreateLabel(
                leftPanel,
                "PlateBreakdownLabel",
                DescribePlateBreakdown(25f),
                new Vector2(32f, 238f),
                new Vector2(476f, 66f),
                15,
                FontStyle.Normal,
                new Color(0.76f, 0.82f, 0.88f, 1f),
                TextAnchor.UpperLeft);

            _loadButtonBgs.Clear();
            CreateWeightOptionButton(leftPanel, 25f, "25 KG  —  COMPETITION BAR + COLLARS (RECOMMENDED)", new Vector2(32f, 152f));
            CreateWeightOptionButton(leftPanel, 60f, "60 KG  —  WARMUP STACK (15 + 2.5 KG / SIDE)", new Vector2(32f, 78f));
            CreateWeightOptionButton(leftPanel, 105f, "105 KG  —  HEAVY STACK (25 + 15 KG / SIDE)", new Vector2(32f, 4f));
            CreateWeightOptionButton(leftPanel, 0f, "0 KG  —  UNLOADED STANDING CALIBRATION", new Vector2(32f, -70f));

            // Readiness & Launch Controls at bottom of left panel
            RectTransform readinessPill = CreatePanel(
                leftPanel,
                "ReadinessPill",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -162f),
                new Vector2(476f, 44f),
                new Color(0.12f, 0.48f, 0.32f, 0.96f));
            _prepReadinessBadgeBg = readinessPill.GetComponent<Image>();

            _prepReadinessBadgeLabel = CreateLabel(
                readinessPill,
                "ReadinessText",
                "STATUS: ATHLETE STABLE — READY FOR SETUP",
                Vector2.zero,
                new Vector2(460f, 40f),
                15,
                FontStyle.Bold,
                Color.white,
                TextAnchor.MiddleCenter);

            _prepSetupButton = CreateMenuButton(
                leftPanel,
                "BeginSetupButton",
                "1. SET UP UNDER BAR",
                new Vector2(32f, -242f),
                new Vector2(476f, 60f),
                new Color(0.18f, 0.36f, 0.58f, 1f),
                19,
                BeginPlayerSetupAction);
            _prepSetupButtonLabel = _prepSetupButton.GetComponentInChildren<Text>();

            _prepUnrackButton = CreateMenuButton(
                leftPanel,
                "UnrackAndWalkoutButton",
                "2. UNRACK & WALK OUT  —  START LIFT [ENTER]",
                new Vector2(32f, -322f),
                new Vector2(476f, 68f),
                new Color(0.80f, 0.14f, 0.18f, 1f),
                20,
                BeginUnrackAndWalkoutAction);

            // Right Panel: Controls & Competition Rules Guide
            RectTransform rightPanel = CreatePanel(
                _squatPrepPanel.transform,
                "ControlsGuidePanel",
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-48f, -36f),
                new Vector2(470f, 540f),
                new Color(0.055f, 0.07f, 0.095f, 0.90f));

            CreateAccentBar(rightPanel, new Vector2(0f, 266f), new Vector2(470f, 6f), new Color(0.88f, 0.66f, 0.22f, 1f));

            CreateLabel(
                rightPanel,
                "GuideHeader",
                "ATTEMPT CONTROLS & IPF RULES",
                new Vector2(28f, 218f),
                new Vector2(414f, 34f),
                19,
                FontStyle.Bold,
                new Color(0.92f, 0.72f, 0.26f, 1f),
                TextAnchor.MiddleLeft);

            string guideText =
                "• UNRACK & WALKOUT:\n" +
                "  Click [UNRACK & WALK OUT] or press [Enter]. The athlete unracks the bar, steps back two steps, and settles.\n\n" +
                "• DESCENT (S or Down Arrow):\n" +
                "  After the Head Referee calls \"SQUAT!\", press and hold [S] or [↓] to descend until the hip crease passes below the top of the knees.\n\n" +
                "• ASCENT & LOCKOUT (W or Up Arrow):\n" +
                "  At the bottom, release [S] and hold [W] or [↑] to drive back to full standing lockout.\n\n" +
                "• CAMERA & RESET:\n" +
                "  [C] Cycle Camera Angle (3/4 · Side Depth · Front)\n" +
                "  [Esc] Abort Active Lift   ·   [R] Instant Retry";

            CreateLabel(
                rightPanel,
                "GuideBody",
                guideText,
                new Vector2(28f, -20f),
                new Vector2(414f, 380f),
                15,
                FontStyle.Normal,
                new Color(0.82f, 0.86f, 0.92f, 1f),
                TextAnchor.UpperLeft);
        }

        private void CreateWeightOptionButton(RectTransform parent, float loadKg, string label, Vector2 anchoredPos)
        {
            Button btn = CreateMenuButton(
                parent,
                $"WeightBtn_{loadKg:0}kg",
                label,
                anchoredPos,
                new Vector2(476f, 58f),
                new Color(0.15f, 0.18f, 0.23f, 0.95f),
                16,
                () => SelectBarbellWeight(loadKg));
            _loadButtonBgs[loadKg] = btn.GetComponent<Image>();
        }

        private void BuildActiveLiftScreen()
        {
            _activeLiftPanel = CreateScreenContainer("ActiveLiftScreen");

            // Top broadcast HUD bar
            RectTransform topHud = CreatePanel(
                _activeLiftPanel.transform,
                "ActiveLiftTopBar",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -22f),
                new Vector2(1820f, 84f),
                new Color(0.045f, 0.06f, 0.085f, 0.90f));

            _hudLoadBadgeText = CreateLabel(
                topHud,
                "HudLoadBadge",
                "COMPETITION SQUAT  ·  25 KG",
                new Vector2(-580f, 14f),
                new Vector2(560f, 32f),
                20,
                FontStyle.Bold,
                Color.white,
                TextAnchor.MiddleLeft);

            _hudPhaseBadgeText = CreateLabel(
                topHud,
                "HudPhaseBadge",
                "PHASE: WALKOUT",
                new Vector2(-580f, -16f),
                new Vector2(560f, 28f),
                15,
                FontStyle.Bold,
                new Color(0.92f, 0.72f, 0.24f, 1f),
                TextAnchor.MiddleLeft);

            // Center referee callout pill
            RectTransform calloutBanner = CreatePanel(
                topHud,
                "RefereeCalloutBanner",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(20f, 0f),
                new Vector2(680f, 58f),
                new Color(0.14f, 0.48f, 0.32f, 0.96f));
            _hudRefereeBannerBg = calloutBanner.GetComponent<Image>();

            _hudRefereeBannerText = CreateLabel(
                calloutBanner,
                "RefereeCalloutText",
                "REFEREE: \"SQUAT!\" — BEGIN DESCENT",
                Vector2.zero,
                new Vector2(650f, 52f),
                20,
                FontStyle.Bold,
                Color.white,
                TextAnchor.MiddleCenter);

            // Camera angle switcher button on top right
            Button camBtn = CreateMenuButton(
                topHud,
                "SwitchCameraAngleButton",
                "CAMERA [C]: 3/4 COMPETITION",
                new Vector2(655f, 0f),
                new Vector2(420f, 52f),
                new Color(0.15f, 0.20f, 0.28f, 0.98f),
                15,
                () =>
                {
                    audioDirector?.PlayUiClick();
                    cameraDirector?.CycleActiveLiftCameraAngle();
                },
                true);
            _hudCameraAngleButtonText = camBtn.GetComponentInChildren<Text>();

            // Right-side vertical Squat Depth Meter
            RectTransform depthCard = CreatePanel(
                _activeLiftPanel.transform,
                "DepthMeterCard",
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-36f, 0f),
                new Vector2(126f, 440f),
                new Color(0.045f, 0.06f, 0.085f, 0.90f));

            _hudDepthValueText = CreateLabel(
                depthCard,
                "DepthValueLabel",
                "DEPTH\n0%",
                new Vector2(0f, 174f),
                new Vector2(110f, 54f),
                16,
                FontStyle.Bold,
                Color.white,
                TextAnchor.MiddleCenter);

            RectTransform meterBg = CreatePanel(
                depthCard,
                "DepthBarTrack",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -24f),
                new Vector2(38f, 290f),
                new Color(0.14f, 0.17f, 0.22f, 1f));

            GameObject fillObj = new GameObject("DepthBarFill");
            fillObj.transform.SetParent(meterBg, false);
            RectTransform fillRect = fillObj.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            _hudDepthFillImage = fillObj.AddComponent<Image>();
            _hudDepthFillImage.sprite = CreateSolidWhiteSprite();
            _hudDepthFillImage.type = Image.Type.Filled;
            _hudDepthFillImage.fillMethod = Image.FillMethod.Vertical;
            _hudDepthFillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
            _hudDepthFillImage.fillAmount = 0f;
            _hudDepthFillImage.color = new Color(0.92f, 0.62f, 0.14f, 1f);

            // Legal depth threshold marker line at 85%
            CreatePanel(
                meterBg,
                "LegalDepthMarker",
                new Vector2(0.5f, 0.85f),
                new Vector2(0.5f, 0.85f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(64f, 4f),
                new Color(0.22f, 0.95f, 0.52f, 1f));

            CreateLabel(
                depthCard,
                "LegalDepthTag",
                "IPF DEPTH",
                new Vector2(0f, -192f),
                new Vector2(114f, 26f),
                12,
                FontStyle.Bold,
                new Color(0.32f, 0.92f, 0.56f, 1f),
                TextAnchor.MiddleCenter);

            // Bottom instruction & action bar
            RectTransform bottomHud = CreatePanel(
                _activeLiftPanel.transform,
                "ActiveLiftBottomBar",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 22f),
                new Vector2(1820f, 88f),
                new Color(0.045f, 0.06f, 0.085f, 0.90f));

            _hudInstructionText = CreateLabel(
                bottomHud,
                "HudInstructionText",
                "Press and hold [S] or [Down Arrow] to descend, then hold [W] or [Up Arrow] to drive up.",
                new Vector2(-240f, 10f),
                new Vector2(1240f, 36f),
                19,
                FontStyle.Bold,
                Color.white,
                TextAnchor.MiddleLeft);

            CreateLabel(
                bottomHud,
                "HudControlsSubtext",
                "CONTROLS:   [S / ↓] Yield Descent    ·    [W / ↑] Concentric Drive    ·    [Space] Brace    ·    [A / D] Balance    ·    [C] Camera Angle",
                new Vector2(-240f, -20f),
                new Vector2(1240f, 26f),
                14,
                FontStyle.Normal,
                new Color(0.68f, 0.75f, 0.84f, 1f),
                TextAnchor.MiddleLeft);

            CreateMenuButton(
                bottomHud,
                "AbortAttemptButton",
                "ABORT LIFT [ESC]",
                new Vector2(545f, 0f),
                new Vector2(210f, 52f),
                new Color(0.58f, 0.16f, 0.18f, 1f),
                15,
                AbortActiveAttemptAction,
                true);

            CreateMenuButton(
                bottomHud,
                "ResetLiftButton",
                "RETRY [R]",
                new Vector2(775f, 0f),
                new Vector2(190f, 52f),
                new Color(0.18f, 0.22f, 0.28f, 1f),
                15,
                RetryToSquatPreparation,
                true);
        }

        private void BuildResultScreen()
        {
            _resultPanel = CreateScreenContainer("AttemptResultScreen");

            RectTransform card = CreatePanel(
                _resultPanel.transform,
                "ResultCard",
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(64f, 0f),
                new Vector2(680f, 760f),
                new Color(0.055f, 0.07f, 0.095f, 0.95f));

            CreateLabel(
                card,
                "ResultHeaderTag",
                "OFFICIAL IPF REFEREE DECISION",
                new Vector2(40f, 330f),
                new Vector2(600f, 28f),
                16,
                FontStyle.Bold,
                new Color(0.92f, 0.72f, 0.26f, 1f),
                TextAnchor.MiddleCenter);

            // 3 Referee Decision Lamps
            _resultRefereeLamps = new Image[3];
            float[] lampX = { -150f, 0f, 150f };
            string[] lampTitles = { "LEFT SIDE JUDGE", "HEAD REFEREE", "RIGHT SIDE JUDGE" };
            for (int i = 0; i < 3; i++)
            {
                RectTransform lampFrame = CreatePanel(
                    card,
                    $"RefereeLamp_{i}",
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(lampX[i], 246f),
                    new Vector2(88f, 88f),
                    new Color(0.95f, 0.96f, 1.0f, 1f));
                _resultRefereeLamps[i] = lampFrame.GetComponent<Image>();

                CreateLabel(
                    card,
                    $"RefereeLampLabel_{i}",
                    lampTitles[i],
                    new Vector2(340f + lampX[i] - 70f, 182f),
                    new Vector2(140f, 22f),
                    11,
                    FontStyle.Bold,
                    new Color(0.68f, 0.74f, 0.82f, 1f),
                    TextAnchor.MiddleCenter);
            }

            // Verdict Banner
            RectTransform verdictBanner = CreatePanel(
                card,
                "VerdictHeadlineBanner",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 108f),
                new Vector2(600f, 84f),
                new Color(0.12f, 0.48f, 0.30f, 0.98f));
            _resultVerdictBannerBg = verdictBanner.GetComponent<Image>();

            _resultHeadlineText = CreateLabel(
                verdictBanner,
                "VerdictHeadlineText",
                "GOOD LIFT",
                Vector2.zero,
                new Vector2(576f, 76f),
                38,
                FontStyle.Bold,
                Color.white,
                TextAnchor.MiddleCenter);

            _resultDetailsText = CreateLabel(
                card,
                "VerdictDetailsText",
                "3 White Lights — Unanimous IPF Good Lift.",
                new Vector2(40f, 12f),
                new Vector2(600f, 78f),
                17,
                FontStyle.Bold,
                new Color(0.90f, 0.93f, 0.98f, 1f),
                TextAnchor.MiddleCenter);

            _resultMetricsText = CreateLabel(
                card,
                "VerdictMetricsText",
                "BAR LOAD: 25 KG   ·   TERMINAL: PHYSICAL_LOCKOUT   ·   RULE VERDICT: GOOD_LIFT",
                new Vector2(40f, -58f),
                new Vector2(600f, 54f),
                14,
                FontStyle.Normal,
                new Color(0.68f, 0.75f, 0.84f, 1f),
                TextAnchor.MiddleCenter);

            CreateMenuButton(
                card,
                "ResultRetryButton",
                "RETRY ATTEMPT  (SAME WEIGHT) [R]",
                new Vector2(40f, -142f),
                new Vector2(600f, 64f),
                new Color(0.80f, 0.14f, 0.18f, 1f),
                20,
                RetryToSquatPreparation);

            CreateMenuButton(
                card,
                "ResultReturnGymButton",
                "RETURN TO TRAINING GYM",
                new Vector2(40f, -222f),
                new Vector2(600f, 56f),
                new Color(0.16f, 0.22f, 0.30f, 1f),
                17,
                RetryToTrainingGym);

            CreateMenuButton(
                card,
                "ResultMainMenuButton",
                "MAIN MENU",
                new Vector2(40f, -296f),
                new Vector2(600f, 54f),
                new Color(0.14f, 0.16f, 0.20f, 1f),
                17,
                RetryToMainMenu);
        }

        private void BuildSettingsModal()
        {
            _settingsModalPanel = CreateScreenContainer("SettingsModalOverlay");

            // Dimmed backdrop
            CreatePanel(
                _settingsModalPanel.transform,
                "BackdropDim",
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero,
                new Color(0.02f, 0.03f, 0.05f, 0.78f));

            RectTransform modal = CreatePanel(
                _settingsModalPanel.transform,
                "SettingsCard",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(920f, 820f),
                new Color(0.06f, 0.075f, 0.105f, 0.98f));

            CreateAccentBar(modal, new Vector2(0f, 406f), new Vector2(920f, 6f), new Color(0.82f, 0.14f, 0.18f, 1f));

            CreateLabel(
                modal,
                "SettingsTitle",
                "GAME SETTINGS  —  GRAPHICS, AUDIO & GAMEPLAY",
                new Vector2(48f, 352f),
                new Vector2(824f, 42f),
                26,
                FontStyle.Bold,
                Color.white,
                TextAnchor.MiddleLeft);

            _settingsFullscreenText = CreateSettingsRow(
                modal,
                "DISPLAY MODE",
                276f,
                "TOGGLE WINDOWED / FULLSCREEN",
                () =>
                {
                    audioDirector?.PlayUiClick();
                    settingsManager?.SetFullscreen(!settingsManager.IsFullscreen);
                    RefreshSettingsModalLabels();
                });

            _settingsResolutionText = CreateSettingsRow(
                modal,
                "RESOLUTION PRESET",
                210f,
                "CYCLE RESOLUTION",
                () =>
                {
                    audioDirector?.PlayUiClick();
                    settingsManager?.CycleResolutionPreset();
                    RefreshSettingsModalLabels();
                });

            _settingsVSyncText = CreateSettingsRow(
                modal,
                "VERTICAL SYNC (VSYNC)",
                144f,
                "TOGGLE VSYNC",
                () =>
                {
                    audioDirector?.PlayUiClick();
                    settingsManager?.SetVSync(!settingsManager.VSyncEnabled);
                    RefreshSettingsModalLabels();
                });

            _settingsMsaaText = CreateSettingsRow(
                modal,
                "ANTI-ALIASING (MSAA)",
                78f,
                "CYCLE MSAA",
                () =>
                {
                    audioDirector?.PlayUiClick();
                    settingsManager?.CycleAntiAliasing();
                    RefreshSettingsModalLabels();
                });

            _settingsShadowsText = CreateSettingsRow(
                modal,
                "ARENA LIGHTING SHADOWS",
                12f,
                "TOGGLE SOFT / HARD",
                () =>
                {
                    audioDirector?.PlayUiClick();
                    settingsManager?.CycleQualityShadowMode();
                    RefreshSettingsModalLabels();
                });

            // Master Volume Row with -10% / +10%
            CreateLabel(
                modal,
                "VolumeRowLabel",
                "MASTER AUDIO VOLUME",
                new Vector2(48f, -54f),
                new Vector2(360f, 44f),
                17,
                FontStyle.Bold,
                new Color(0.82f, 0.86f, 0.92f, 1f),
                TextAnchor.MiddleLeft);

            _settingsVolumeText = CreateLabel(
                modal,
                "VolumeRowValue",
                "80%",
                new Vector2(410f, -54f),
                new Vector2(160f, 44f),
                17,
                FontStyle.Bold,
                new Color(0.92f, 0.72f, 0.26f, 1f),
                TextAnchor.MiddleCenter);

            CreateMenuButton(
                modal,
                "VolDownBtn",
                "- 10%",
                new Vector2(590f, -54f),
                new Vector2(130f, 44f),
                new Color(0.16f, 0.21f, 0.28f, 1f),
                15,
                () =>
                {
                    settingsManager?.AdjustMasterVolume(-0.1f);
                    audioDirector?.PlayUiClick();
                    RefreshSettingsModalLabels();
                });

            CreateMenuButton(
                modal,
                "VolUpBtn",
                "+ 10%",
                new Vector2(738f, -54f),
                new Vector2(130f, 44f),
                new Color(0.16f, 0.21f, 0.28f, 1f),
                15,
                () =>
                {
                    settingsManager?.AdjustMasterVolume(0.1f);
                    audioDirector?.PlayUiClick();
                    RefreshSettingsModalLabels();
                });

            _settingsMuteText = CreateSettingsRow(
                modal,
                "AUDIO OUTPUT STATUS",
                -120f,
                "TOGGLE MUTE",
                () =>
                {
                    settingsManager?.SetAudioMuted(!settingsManager.AudioMuted);
                    audioDirector?.PlayUiClick();
                    RefreshSettingsModalLabels();
                });

            _settingsCameraText = CreateSettingsRow(
                modal,
                "DEFAULT LIFT CAMERA ANGLE",
                -186f,
                "CYCLE CAMERA",
                () =>
                {
                    audioDirector?.PlayUiClick();
                    settingsManager?.CyclePreferredCameraAngle();
                    if (cameraDirector != null)
                        cameraDirector.SetActiveLiftCameraAngle(settingsManager.PreferredCameraAngle, false);
                    RefreshSettingsModalLabels();
                });

            _settingsTelemetryText = CreateSettingsRow(
                modal,
                "DEVELOPER TELEMETRY OVERLAY [F12]",
                -252f,
                "TOGGLE TELEMETRY",
                () =>
                {
                    audioDirector?.PlayUiClick();
                    settingsManager?.SetDeveloperTelemetry(!settingsManager.DeveloperTelemetryEnabled);
                    RefreshSettingsModalLabels();
                });

            // Modal Footer Buttons
            CreateMenuButton(
                modal,
                "ResetDefaultsButton",
                "RESET DEFAULTS",
                new Vector2(48f, -342f),
                new Vector2(250f, 56f),
                new Color(0.18f, 0.22f, 0.28f, 1f),
                16,
                () =>
                {
                    audioDirector?.PlayUiClick();
                    settingsManager?.ResetToDefaults();
                    RefreshSettingsModalLabels();
                });

            CreateMenuButton(
                modal,
                "CloseSettingsButton",
                "BACK  /  SAVE & CLOSE",
                new Vector2(320f, -342f),
                new Vector2(310f, 56f),
                new Color(0.78f, 0.14f, 0.18f, 1f),
                17,
                CloseSettingsModal);

            CreateMenuButton(
                modal,
                "SettingsQuitButton",
                "QUIT GAME",
                new Vector2(652f, -342f),
                new Vector2(220f, 56f),
                new Color(0.28f, 0.12f, 0.14f, 1f),
                16,
                QuitGame);

            _settingsModalPanel.SetActive(false);
        }

        private Text CreateSettingsRow(
            RectTransform modal,
            string label,
            float yOffset,
            string buttonText,
            UnityEngine.Events.UnityAction onClick)
        {
            CreateLabel(
                modal,
                label + "_Label",
                label,
                new Vector2(48f, yOffset),
                new Vector2(360f, 44f),
                17,
                FontStyle.Bold,
                new Color(0.82f, 0.86f, 0.92f, 1f),
                TextAnchor.MiddleLeft);

            Text valueText = CreateLabel(
                modal,
                label + "_Value",
                "",
                new Vector2(410f, yOffset),
                new Vector2(200f, 44f),
                16,
                FontStyle.Bold,
                new Color(0.92f, 0.72f, 0.26f, 1f),
                TextAnchor.MiddleCenter);

            CreateMenuButton(
                modal,
                label + "_Button",
                buttonText,
                new Vector2(624f, yOffset),
                new Vector2(244f, 44f),
                new Color(0.16f, 0.21f, 0.28f, 1f),
                14,
                onClick);

            return valueText;
        }

        // --- Low-Level uGUI Helpers ---

        private GameObject CreateScreenContainer(string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        private static RectTransform CreatePanel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;
            Image img = go.AddComponent<Image>();
            img.color = color;
            return rt;
        }

        private static void CreateAccentBar(RectTransform parent, Vector2 pos, Vector2 size, Color color)
        {
            CreatePanel(
                parent,
                "AccentBar",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                pos,
                size,
                color);
        }

        private Text CreateLabel(
            Transform parent,
            string name,
            string content,
            Vector2 anchoredPos,
            Vector2 size,
            int fontSize,
            FontStyle style,
            Color color,
            TextAnchor alignment)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            if (parent is RectTransform parentRt && Mathf.Approximately(parentRt.pivot.x, 0f))
            {
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
            }
            else
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
            }
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            Text text = go.AddComponent<Text>();
            text.font = _uiFont;
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateMenuButton(
            RectTransform parent,
            string name,
            string label,
            Vector2 anchoredPos,
            Vector2 size,
            Color bgColor,
            int fontSize,
            UnityEngine.Events.UnityAction onClick,
            bool centerAnchor = false)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            if (!centerAnchor && Mathf.Approximately(parent.pivot.x, 0f))
            {
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
            }
            else
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
            }
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            Image img = go.AddComponent<Image>();
            img.color = bgColor;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            cb.selectedColor = Color.white;
            cb.disabledColor = new Color(0.45f, 0.45f, 0.48f, 0.65f);
            btn.colors = cb;
            if (onClick != null)
                btn.onClick.AddListener(onClick);

            GameObject textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            RectTransform textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 4f);
            textRt.offsetMax = new Vector2(-12f, -4f);

            Text text = textGo.AddComponent<Text>();
            text.font = _uiFont;
            text.text = label;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;

            return btn;
        }

        private static Sprite s_solidWhiteSprite;

        private static Sprite CreateSolidWhiteSprite()
        {
            if (s_solidWhiteSprite != null)
                return s_solidWhiteSprite;

            Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            s_solidWhiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            return s_solidWhiteSprite;
        }
    }
}
