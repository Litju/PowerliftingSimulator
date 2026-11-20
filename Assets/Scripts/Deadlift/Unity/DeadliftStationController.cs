using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 playable deadlift station. It owns no physics: it composes the
    /// qualified <see cref="DeadliftAttemptRuntime"/> on the scene's athlete,
    /// bar and platform, lets the foundation's authoritative clock run, and
    /// presents the attempt (instructions, preparation, phase, bar height,
    /// referee signal and decision).
    ///
    /// The shipped attempt is the close-bar deadlift: the offline-generated
    /// reference family is validated while the station loads, and admitted on
    /// the measured plant at bar separation and knee passage without pausing
    /// the simulation. The player's Drive (W / Up) reaches the pull controller
    /// through the foundation's intent pipeline, exactly as the squat and
    /// bench; the internal preparation (grip pretension and shoulder set)
    /// starts the pull only when the measured plant is ready. R reloads the
    /// scene, V toggles the side view, ESC aborts (before liftoff) or returns
    /// the bar (after liftoff). "-deadliftStockPath" runs the stock qualified
    /// path as a negative control.
    /// </summary>
    public sealed class DeadliftStationController : MonoBehaviour
    {
        public const string ScenePath = "Assets/Scenes/Prototype/DeadliftPrototype.unity";
        public const string SceneName = "DeadliftPrototype";

        [SerializeField] private FoundationBootstrap foundation;
        [SerializeField] private PhysicalAthleteRig athleteRig;
        [SerializeField] private PhysicalBarbell barbell;

        private string _status = "Preparing the lifter...";
        private string _failure = string.Empty;
        private GUIStyle _big, _body, _lightOn, _lightOff;
        private bool _sideView;
        private bool _stockPath;
        private bool _reloading;
        private DeadliftPullPhase _loggedPhase = DeadliftPullPhase.Grounded;
        private ulong _loggedRejection;
        private string _loggedSetup;

        /// <summary>Negative control: the stock qualified ascent path instead of the close-bar reference.</summary>
        public const string StockPathArgument = "-deadliftStockPath";
        /// <summary>Test seam: selects the stock control without a command-line argument (null: use the arguments).</summary>
        public static bool? StockPathOverride { get; set; }
        /// <summary>Start in the low side view.</summary>
        public const string SideViewArgument = "-deadliftSideView";

        public DeadliftAttemptRuntime Runtime { get; private set; }
        public bool IsInitialized { get; private set; }
        public string StartupFailure => _failure;
        public string Status => _status;
        /// <summary>Wall time of the loading stage (scene composition), ms.</summary>
        public double LoadMilliseconds { get; private set; }

        private void Awake()
        {
            if (foundation == null)
                foundation = FindFirstObjectByType<FoundationBootstrap>();
            if (foundation != null && foundation.Runtime != null && foundation.Runtime.IsInitialized)
                foundation.Runtime.HoldPhysicsUntilInitialStateValidated();
            // Loading stage: the render clock must not step a held plant while the attempt is composed.
            if (foundation != null)
                foundation.enabled = false;
        }

        private IEnumerator Start()
        {
            // Render the loading message before the composition frame.
            yield return null;
            yield return null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (foundation == null) foundation = FindFirstObjectByType<FoundationBootstrap>();
                if (athleteRig == null) athleteRig = FindFirstObjectByType<PhysicalAthleteRig>();
                if (barbell == null) barbell = FindFirstObjectByType<PhysicalBarbell>();
                if (foundation == null || !foundation.Runtime.IsInitialized || athleteRig == null || barbell == null)
                    throw new InvalidOperationException("The deadlift scene needs a foundation, a physical athlete and a barbell.");
                if (athleteRig.PoweredController == null)
                    athleteRig.Build();
                if (!barbell.IsBuilt)
                    barbell.Build();
                barbell.SetGameplayPerformanceProfile(true);
                string[] args = Environment.GetCommandLineArgs();
                _stockPath = StockPathOverride ?? Array.IndexOf(args, StockPathArgument) >= 0;
                _sideView = Array.IndexOf(args, SideViewArgument) >= 0;
                Runtime = new DeadliftAttemptRuntime(foundation, athleteRig, barbell)
                {
                    PreparedCloseLockout = !_stockPath,
                    UseReferenceCache = true,
                    PlayerSetup = !_stockPath
                };
                Runtime.Initialize();
                SquatPresentationGrip grip = athleteRig.GetComponent<SquatPresentationGrip>() ?? athleteRig.gameObject.AddComponent<SquatPresentationGrip>();
                grip.Configure(athleteRig, barbell);
                grip.ThumbAroundShaft = true;   // IPF 4.3(1): the grip is optional; thumbs around the bar
                PlaceCamera(_sideView);
                IsInitialized = true;
                LoadMilliseconds = clock.Elapsed.TotalMilliseconds;
                foundation.enabled = true;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "GAM-70 deadlift station ready ({0}) in {1:F0} ms: {2}. Tick-0 penetration {3:F2} mm\n{4}",
                    _stockPath ? "stock path control" : "close-bar reference", LoadMilliseconds,
                    string.Join(", ", Runtime.InitializationMilliseconds.Select(kv => $"{kv.Key} {kv.Value:F0} ms")),
                    Runtime.TickZeroMaxPenetrationM * 1000f, Runtime.PathReport));
            }
            catch (Exception e)
            {
                _failure = e.Message;
                Debug.LogError("GAM-70 deadlift station startup failed: " + e);
            }
        }

        private static void PlaceCamera(bool side)
        {
            Camera cam = Camera.main ?? FindFirstObjectByType<Camera>();
            if (cam == null)
                return;
            if (side)
            {
                // Low side-quarter view, ~50 deg off the bar axis (athlete faces +Z): a true side
                // view looks straight through the near 450 mm plate, which hides the bar-to-leg gap.
                cam.transform.position = new Vector3(2.0f, 0.95f, 1.9f);
                cam.transform.LookAt(new Vector3(0.15f, 0.75f, -0.1f));
            }
            else
            {
                // Three-quarter side view, as a side judge sees the lift.
                cam.transform.position = new Vector3(2.6f, 1.25f, 1.6f);
                cam.transform.LookAt(new Vector3(0f, 0.75f, -0.05f));
            }
            cam.fieldOfView = 45f;
        }

        /// <summary>Reloads the station once; further requests while the load is pending are ignored.</summary>
        public void RequestRestart()
        {
            if (_reloading)
                return;
            _reloading = true;
            Debug.Log($"GAM-70 station: restart requested at tick {Runtime?.Tick ?? 0}");
            SceneManager.LoadScene(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (_reloading)
                return;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                RequestRestart();
                return;
            }
            if (keyboard != null && keyboard.vKey.wasPressedThisFrame && IsInitialized)
            {
                _sideView = !_sideView;
                PlaceCamera(_sideView);
            }
            if (Runtime == null || !string.IsNullOrEmpty(_failure) || !IsInitialized)
                return;

            DeadliftPullController pull = Runtime.Pull;
            DeadliftReferee referee = Runtime.Referee;
            bool driving = pull.LastRawDrive01 > 0.05f;
            switch (pull.Phase)
            {
                case DeadliftPullPhase.Grounded:
                    _status = Runtime.PlayerSetup
                        ? (!Runtime.LastBraceHeld ? "Tap Space to brace."
                            : !Runtime.PretensionEstablished ? "Braced. Hold Left Shift: grip and take the slack out of the bar."
                            : driving ? "Preparing the pull: " + Runtime.PreparationStatus
                            : "Slack out. Hold W to pull (keep Shift held).")
                        : !Runtime.PretensionEstablished ? "Setting the grip and taking the slack out of the bar..."
                        : driving ? "Preparing the pull: " + Runtime.PreparationStatus
                        : "Ready. Hold W (or Up arrow) to pull.";
                    break;
                case DeadliftPullPhase.Pulling: _status = "Pulling - the plates are unloading."; break;
                case DeadliftPullPhase.Aborted: _status = "Pull released before the bar left the floor. Release W, then pull again."; break;
                case DeadliftPullPhase.Separated:
                case DeadliftPullPhase.Lifting:
                    _status = pull.LiftHeld ? "Hold position (" + pull.LiftHoldReason + ")."
                        : pull.AdmissionLimited ? "Hold position (reference admission)."
                        : pull.Intent01 > 0.05f ? "Driving the bar up." : "Bar held. Keep W held to drive.";
                    break;
                case DeadliftPullPhase.Lockout: _status = "Locked out - stand still for the referee."; break;
                case DeadliftPullPhase.Lowering:
                    _status = pull.DownReceived ? "DOWN - lowering under control."
                        : !string.IsNullOrEmpty(Runtime.ReferenceRejection) ? "Returning the bar: " + Runtime.ReferenceRejection
                        : "Returning the bar (attempt abandoned).";
                    break;
                case DeadliftPullPhase.Returned: _status = referee.Completed ? "Attempt complete. R for another attempt." : "Bar on the platform. R for another attempt."; break;
            }
            string setup = string.Format(CultureInfo.InvariantCulture, "grip {0} brace {1} drive {2} | slack out {3} | {4} | {5}",
                pull.LastGrip01 >= 0.5f ? "held" : "off", Runtime.LastBraceHeld ? "set" : "off", pull.LastRawDrive01 > 0.05f ? "held" : "off",
                Runtime.PretensionEstablished, Runtime.PreparationStatus, _status);
            if (setup != _loggedSetup)
            {
                Debug.Log($"GAM-70 station setup at tick {Runtime.Tick}: {setup}");
                _loggedSetup = setup;
            }
            if (pull.Phase != _loggedPhase)
            {
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "GAM-70 station: tick {0} phase {1} -> {2} | drive {3:F2} | bar {4:F3} m | {5}",
                    Runtime.Tick, _loggedPhase, pull.Phase, pull.LastRawDrive01, Runtime.LastObservation.BarY, referee.Summary()));
                _loggedPhase = pull.Phase;
            }
            if (Runtime.ReferenceRejectionTick != 0 && _loggedRejection == 0)
            {
                _loggedRejection = Runtime.ReferenceRejectionTick;
                Debug.LogWarning($"GAM-70 station: reference not admitted at tick {_loggedRejection}: {Runtime.ReferenceRejection}");
            }
        }

        private void EnsureStyles()
        {
            if (_big != null)
                return;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _big.normal.textColor = Color.white;
            _body = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            _body.normal.textColor = Color.white;
            _lightOn = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            _lightOff = new GUIStyle(_lightOn);
        }

        private void OnGUI()
        {
            EnsureStyles();
            float s = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float w = Screen.width / s;
            GUI.Box(new Rect(16, 16, 600, 240), GUIContent.none);
            GUI.Label(new Rect(30, 24, 580, 30), "DEADLIFT  -  " + DeadliftQualifiedSetup.BarKg.ToString("F0", CultureInfo.InvariantCulture) + " kg  (sumo, pronated)", _body);
            if (_stockPath)
                GUI.Label(new Rect(30, 222, 580, 30), "CONTROL: stock ascent path", _body);
            if (!string.IsNullOrEmpty(_failure))
            {
                GUI.Label(new Rect(30, 60, 580, 160), "Station error. Press R to restart.\n" + _failure, _body);
                return;
            }
            if (Runtime == null || !IsInitialized)
            {
                GUI.Label(new Rect(30, 60, 580, 30), _status, _body);
                return;
            }
            DeadliftObservation o = Runtime.LastObservation;
            DeadliftPullController pull = Runtime.Pull;
            GUI.Label(new Rect(30, 56, 580, 52), _status, _body);
            GUI.Label(new Rect(30, 110, 580, 30), string.Format(CultureInfo.InvariantCulture, "Bar height {0:F2} m   rise {1:F0} mm   drive {2:F0} %",
                o.BarY, Runtime.Referee.MaxBarRiseM * 1000.0, pull.Intent01 * 100f), _body);
            GUI.Label(new Rect(30, 140, 580, 30), string.Format(CultureInfo.InvariantCulture, "Knees {0:F0}/{1:F0} deg   torso {2:F0} deg   phase {3}",
                o.KneeFlexionLDeg, o.KneeFlexionRDeg, o.TorsoDeg, pull.Phase), _body);
            GUI.Label(new Rect(30, 170, 580, 30), "Space brace (tap)  |  Shift grip/slack  |  W pull  |  ESC  |  R  |  V", _body);
            GUI.Label(new Rect(30, 196, 580, 30), string.Format(CultureInfo.InvariantCulture, "input  drive {0:F2}  brace {1:F2}  grip {2:F2}",
                pull.LastRawDrive01, pull.LastBrace01, pull.LastGrip01), _body);

            DeadliftReferee referee = Runtime.Referee;
            if (referee.DownGiven && !referee.Completed)
                GUI.Label(new Rect(0, 60, w, 60), "DOWN!", _big);
            if (referee.Completed)
            {
                bool good = referee.GoodLift;
                Color c = good ? Color.white : new Color(0.85f, 0.1f, 0.1f);
                Color old = GUI.backgroundColor;
                for (int i = 0; i < 3; i++)
                {
                    GUI.backgroundColor = c;
                    GUI.Box(new Rect(w * 0.5f - 150 + i * 110, 140, 80, 80), GUIContent.none, _lightOn);
                }
                GUI.backgroundColor = old;
                GUI.Label(new Rect(0, 230, w, 50), good ? "GOOD LIFT" : "NO LIFT", _big);
                int line = 0;
                foreach (DeadliftFaultRecord f in referee.Faults)
                    GUI.Label(new Rect(w * 0.5f - 300, 290 + 26 * line++, 600, 26), f.Fault + " (" + f.RuleReference + "): " + f.Detail, _body);
            }
        }
    }
}
