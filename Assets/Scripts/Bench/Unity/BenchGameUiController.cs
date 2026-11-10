using System;
using System.Globalization;
using System.Text;
using PowerliftingSimulator.Bench;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PowerliftingSimulator.Bench.Unity
{
    public enum BenchScreen : byte
    {
        Setup,
        Lift,
        Result
    }

    /// <summary>
    /// Player-facing bench shell: the station's setup and load selection, the
    /// active-lift HUD with the referee's signals, the result, retry and the
    /// way back to the gym.
    ///
    /// Presentation only. It reads the bench controller, the referee and the
    /// last physical observation, and it may only ask for what a player may
    /// ask for: lift off, reload, return. It never writes a body, the bar, a
    /// joint target or a force. The lift itself is driven through the
    /// foundation's player-intent frame (Space/Enter confirm, S/Down lower,
    /// W/Up press, Esc abort), which the bench controller samples per tick.
    ///
    /// Split by concern: this file is the shell, setup and routing; the HUD
    /// and the result screen live in their own partial files.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class BenchGameUiController : MonoBehaviour
    {
        /// <summary>The gym is the accepted squat scene; the bench routes back to it by name.</summary>
        public const string GymSceneName = "SquatPhysicalPrototype";

        /// <summary>Loads the station offers. Only the physically qualified one can be selected.</summary>
        public static readonly float[] DisplayedLoadsKg = { 25f, 40f, 60f, 80f };

        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private static readonly Color Panel = new Color(0.04f, 0.05f, 0.07f, 0.86f);
        private static readonly Color PanelLight = new Color(0.10f, 0.12f, 0.16f, 0.92f);
        private static readonly Color Gold = new Color(0.92f, 0.82f, 0.48f, 1f);
        private static readonly Color Ok = new Color(0.20f, 0.78f, 0.42f, 1f);
        private static readonly Color Bad = new Color(0.88f, 0.22f, 0.22f, 1f);
        private static readonly Color Idle = new Color(0.32f, 0.35f, 0.40f, 1f);
        private static readonly Color Red = new Color(0.78f, 0.14f, 0.18f, 1f);
        private static readonly Color Slate = new Color(0.22f, 0.25f, 0.30f, 1f);
        private static readonly Color Muted = new Color(0.72f, 0.75f, 0.80f, 1f);

        private BenchPressPrototypeController _bench;
        private BenchAudioCues _audio;
        private BenchCameraDirector _camera;
        private Font _font;

        private GameObject _setupPanel, _liftPanel, _resultPanel;
        private Text _setupChecks, _setupStatus;
        private Button _liftOffButton;
        private BenchMotionPhase _loggedPhase = BenchMotionPhase.Racked;

        public BenchScreen Screen { get; private set; } = BenchScreen.Setup;
        public string DisplayedSetupChecks => _setupChecks != null ? _setupChecks.text : string.Empty;
        public string DisplayedSetupStatus => _setupStatus != null ? _setupStatus.text : string.Empty;
        public bool ReturnToGymRequested { get; private set; }

        public static bool IsQualifiedLoad(float kg) => Mathf.Approximately(kg, BenchPressPrototypeController.QualifiedLoadKg);

        // Concern hooks, implemented by the HUD and result partials.
        partial void BuildHud(RectTransform root);
        partial void RefreshHud();
        partial void BuildResult(RectTransform root);
        partial void RefreshResult();
        partial void OnResultShown();
        partial void OnCommandIssued(BenchCommand command);
        partial void UpdateSignals();
        partial void HandleScreenHotkeys(Keyboard keyboard);
        partial void ResultDue(ref bool due);

        private void Awake()
        {
            _audio = GetComponent<BenchAudioCues>() ?? gameObject.AddComponent<BenchAudioCues>();
            EnsureEventSystem();
            Build();
            Show(BenchScreen.Setup);
        }

        private void OnDestroy()
        {
            if (_bench != null)
                _bench.CommandIssued -= OnCommand;
        }

        private void Resolve()
        {
            if (_bench == null)
            {
                _bench = FindFirstObjectByType<BenchPressPrototypeController>();
                if (_bench != null)
                    _bench.CommandIssued += OnCommand;
            }
            if (_camera == null)
                _camera = FindFirstObjectByType<BenchCameraDirector>();
        }

        // ------------------------------------------------------------------
        // Player actions (UI buttons, hotkeys and the test API)

        /// <summary>Same as pressing Space while racked: asks the controller to lift off.</summary>
        public void RequestLiftOff()
        {
            Resolve();
            if (_bench == null || !_bench.IsInitialized || _bench.Controller.Phase != BenchMotionPhase.Racked)
                return;
            _audio.PlayClick();
            _bench.RequestLiftOff();
            Show(BenchScreen.Lift);
        }

        public void SelectLoad(float kg)
        {
            _audio.PlayClick();
            if (!IsQualifiedLoad(kg))
            {
                SetSetupStatus(string.Format(C,
                    "{0:F0} kg is NOT physically qualified for the bench yet. Only the 25 kg competition bar (20 kg bar + collars) can be lifted.", kg));
                return;
            }
            Resolve();
            if (_bench != null && !Mathf.Approximately(_bench.LoadKg, kg))
                _bench.ReloadWithLoad(kg);
            else
                SetSetupStatus("25 kg selected: competition bar with collars. Physically qualified.");
        }

        public void ReturnToGym()
        {
            _audio.PlayClick();
            ReturnToGymRequested = true;
            Debug.Log("GAM69_UI return-to-gym");
            PowerliftingGameUiController.SetPendingReloadScreen(PowerliftingScreenState.TrainingGym);
            SceneManager.LoadScene(GymSceneName, LoadSceneMode.Single);
        }

        private void OnCommand(BenchCommand command)
        {
            Debug.Log($"GAM69_UI command={command} tick={_bench?.LastObservation.Tick}");
            OnCommandIssued(command);
        }

        // ------------------------------------------------------------------

        private void Update()
        {
            Resolve();
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (Screen == BenchScreen.Setup && keyboard.gKey.wasPressedThisFrame)
                {
                    ReturnToGym();
                    return;
                }
                HandleScreenHotkeys(keyboard);
            }
            if (_bench == null)
                return;
            if (!_bench.IsInitialized)
            {
                if (!string.IsNullOrEmpty(_bench.StartupFailure))
                    SetSetupStatus("Bench unavailable: " + _bench.StartupFailure);
                return;
            }

            BenchMotionPhase phaseNow = _bench.Controller.Phase;
            if (phaseNow != _loggedPhase)
            {
                BenchIntent i = _bench.Controller.LastIntent;
                Debug.Log($"GAM69_UI phase {_loggedPhase}->{phaseNow} tick={_bench.LastObservation.Tick} intent lower={i.Lower01:F2} press={i.Press01:F2} confirm={i.Confirm} abort={i.Abort}");
                _loggedPhase = phaseNow;
            }
            if (Screen == BenchScreen.Setup && phaseNow != BenchMotionPhase.Racked)
                Show(BenchScreen.Lift);
            UpdateSignals();
            bool due = false;
            ResultDue(ref due);
            if (Screen == BenchScreen.Lift && due)
                Show(BenchScreen.Result);

            switch (Screen)
            {
                case BenchScreen.Setup: RefreshSetup(); break;
                case BenchScreen.Lift: RefreshHud(); break;
                case BenchScreen.Result: RefreshResult(); break;
            }
        }

        private void Show(BenchScreen screen)
        {
            Screen = screen;
            Debug.Log($"GAM69_UI screen={screen}");
            _setupPanel?.SetActive(screen == BenchScreen.Setup);
            _liftPanel?.SetActive(screen == BenchScreen.Lift);
            _resultPanel?.SetActive(screen == BenchScreen.Result);
            if (screen == BenchScreen.Result)
                OnResultShown();
        }

        // ------------------------------------------------------------------
        // Setup: the athlete is placed supine with both hands on the racked
        // bar as the validated tick-zero initial condition; this screen shows
        // what the referee will check, measured live.

        private void RefreshSetup()
        {
            BenchObservation o = _bench.LastObservation;
            var b = new StringBuilder();
            b.AppendLine(Check(o.HeadInContact, "Head on the bench"));
            b.AppendLine(Check(o.ShouldersInContact, "Shoulders on the bench"));
            b.AppendLine(Check(o.ButtocksInContact, "Buttocks on the bench"));
            b.AppendLine(Check(o.LeftFootPlanted && o.RightFootPlanted, "Both feet flat on the platform"));
            b.AppendLine(Check(o.BothGripsAttached && !o.ReverseGrip, "Closed thumbs-around grip, both hands"));
            b.AppendLine(Check(o.ForefingerSpacingM <= BenchRulebook.MaxForefingerSpacingM,
                string.Format(C, "Grip width {0:F1} cm between forefingers (max 81 cm)", o.ForefingerSpacingM * 100.0)));
            b.Append(Check(o.BarOnRack, "Bar resting in the uprights"));
            _setupChecks.text = b.ToString();
            _liftOffButton.interactable = _bench.Controller.Phase == BenchMotionPhase.Racked;
        }

        private static string Check(bool ok, string label) =>
            (ok ? "<color=#33c76b>■</color>  " : "<color=#e03838>■</color>  ") + label;

        private void SetSetupStatus(string text)
        {
            if (_setupStatus != null)
                _setupStatus.text = text;
        }

        // ------------------------------------------------------------------
        // Construction

        private void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Font.CreateDynamicFontFromOSFont("Arial", 16);
            var canvasGo = new GameObject("BenchCanvas");
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            RectTransform root = (RectTransform)canvasGo.transform;

            BuildSetup(root);
            BuildHud(root);
            BuildResult(root);
        }

        private void BuildSetup(RectTransform root)
        {
            RectTransform p = Rect(root, "BenchSetup", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(640f, -56f), Panel);
            _setupPanel = p.gameObject;
            Label(p, "Title", "BENCH PRESS STATION", new Vector2(0f, 452f), new Vector2(580f, 54f), 34, FontStyle.Bold, Gold, TextAnchor.MiddleLeft);
            Label(p, "Subtitle", "IPF 2026 rules · physically simulated lift · chief referee signals", new Vector2(0f, 412f), new Vector2(580f, 28f), 17, FontStyle.Italic, Muted, TextAnchor.MiddleLeft);

            Label(p, "LoadHeader", "BAR LOAD", new Vector2(0f, 360f), new Vector2(580f, 28f), 18, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
            for (int i = 0; i < DisplayedLoadsKg.Length; i++)
            {
                float kg = DisplayedLoadsKg[i];
                bool ok = IsQualifiedLoad(kg);
                Button b = MakeButton(p, "Load_" + kg.ToString(C),
                    string.Format(C, ok ? "{0:F0} kg\nQUALIFIED" : "{0:F0} kg\nNOT QUALIFIED", kg),
                    new Vector2(70f + i * 140f, 300f), new Vector2(128f, 70f), ok ? new Color(0.14f, 0.56f, 0.34f, 1f) : Slate, 17,
                    () => SelectLoad(kg));
                if (!ok)
                    b.GetComponentInChildren<Text>().color = new Color(0.6f, 0.6f, 0.65f, 1f);
            }
            _setupStatus = Label(p, "LoadStatus", "25 kg selected: competition bar with collars. Physically qualified.",
                new Vector2(0f, 236f), new Vector2(590f, 44f), 15, FontStyle.Normal, Muted, TextAnchor.MiddleLeft);

            Label(p, "SetupHeader", "SETUP & GRIP  (measured live)", new Vector2(0f, 190f), new Vector2(580f, 28f), 18, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
            _setupChecks = Label(p, "SetupChecks", string.Empty, new Vector2(0f, 60f), new Vector2(590f, 230f), 18, FontStyle.Normal, Color.white, TextAnchor.UpperLeft);
            _setupChecks.supportRichText = true;

            Label(p, "Controls",
                "CONTROLS\nSPACE / ENTER   lift off · rerack\nS / ↓   lower          W / ↑   press\nESC   abort (the spotters take the bar)\nC   camera            G   return to gym",
                new Vector2(0f, -150f), new Vector2(590f, 150f), 17, FontStyle.Normal, Muted, TextAnchor.UpperLeft);

            _liftOffButton = MakeButton(p, "LiftOff", "LIFT OFF  [SPACE]", new Vector2(150f, -330f), new Vector2(300f, 70f), Red, 22, RequestLiftOff);
            MakeButton(p, "ToGym", "RETURN TO GYM  [G]", new Vector2(450f, -330f), new Vector2(260f, 70f), Slate, 17, ReturnToGym);
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
            var es = new GameObject("EventSystem_GAM69");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            Image img = go.AddComponent<Image>();
            img.color = color;
            return rt;
        }

        private Text Label(Transform parent, string name, string content, Vector2 pos, Vector2 size, int fontSize, FontStyle style, Color color, TextAnchor alignment,
            bool leftAnchor = false, bool rightAnchor = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            bool parentLeft = parent is RectTransform prt && Mathf.Approximately(prt.pivot.x, 0f);
            Vector2 anchor = leftAnchor || parentLeft ? new Vector2(0f, 0.5f) : rightAnchor ? new Vector2(1f, 0.5f) : new Vector2(0.5f, 0.5f);
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos + (leftAnchor || parentLeft ? new Vector2(28f, 0f) : Vector2.zero);
            rt.sizeDelta = size;
            Text text = go.AddComponent<Text>();
            text.font = _font;
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

        private Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color color, int fontSize, UnityEngine.Events.UnityAction onClick)
        {
            bool parentLeft = parent is RectTransform prt && Mathf.Approximately(prt.pivot.x, 0f);
            Vector2 anchor = parentLeft ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 0.5f);
            RectTransform rt = Rect(parent, name, anchor, anchor, new Vector2(0.5f, 0.5f), pos + (parentLeft ? new Vector2(28f, 0f) : Vector2.zero), size, color);
            Button button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = rt.GetComponent<Image>();
            // Space/Enter drive the lift itself. Buttons never keep keyboard
            // focus, so one key press cannot both click a button and lift.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(onClick);
            button.onClick.AddListener(() => EventSystem.current?.SetSelectedGameObject(null));
            Label(rt, "Label", label, Vector2.zero, size, fontSize, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            return button;
        }
    }
}
