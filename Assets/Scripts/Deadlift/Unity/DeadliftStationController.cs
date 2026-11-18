using System;
using System.Collections;
using System.Globalization;
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
    /// presents the attempt (instructions, phase, bar height, referee signal
    /// and decision). The player's Brace, Grip and Drive reach the pull
    /// controller through the foundation's intent pipeline, exactly as the
    /// squat and bench. R reloads the scene for another attempt.
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
        private ulong _downShownTick;
        private bool _sideView;
        private bool _experimentalPath;
        private bool _preparedRejoin, _preparedRehearsal, _rehearsalStarted;
        private DeadliftPullPhase _rehearsalPhase;
        private float _rehearsalMinGap = float.PositiveInfinity, _rehearsalMinFoot = float.PositiveInfinity;
        private double _rehearsalThighGapSum;
        private int _rehearsalLockoutTicks;

        /// <summary>
        /// Owner comparison only: "-deadliftBarProximity" on the player command line runs the
        /// experimental bar-proximity ascent path (DeadliftLiftPath.BarProximity), which is
        /// measured NOT to complete the lift reliably; "-deadliftSideView" starts in the side view.
        /// The default is the qualified path.
        /// </summary>
        public const string ExperimentalPathArgument = "-deadliftBarProximity", SideViewArgument = "-deadliftSideView";
        public const string PreparedRejoinArgument = "-deadliftPreparedRejoin";
        public const string PreparedRehearsalArgument = "-deadliftPreparedRehearsal";
        private DeadliftPullPhase _loggedPhase = DeadliftPullPhase.Grounded;

        public DeadliftAttemptRuntime Runtime { get; private set; }
        public bool IsInitialized { get; private set; }
        public string StartupFailure => _failure;

        private void Awake()
        {
            if (foundation == null)
                foundation = FindFirstObjectByType<FoundationBootstrap>();
            if (foundation != null && foundation.Runtime != null && foundation.Runtime.IsInitialized)
                foundation.Runtime.HoldPhysicsUntilInitialStateValidated();
            // The render clock must not try to step a held plant while the attempt is composed.
            if (foundation != null)
                foundation.enabled = false;
        }

        private IEnumerator Start()
        {
            // Render the preparing message first: the ascent path solve takes seconds.
            yield return null;
            yield return null;
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
                _preparedRehearsal = Array.IndexOf(args, PreparedRehearsalArgument) >= 0;
                _preparedRejoin = _preparedRehearsal || Array.IndexOf(args, PreparedRejoinArgument) >= 0;
                _experimentalPath = !_preparedRejoin && Array.IndexOf(args, ExperimentalPathArgument) >= 0;
                _sideView = Array.IndexOf(args, SideViewArgument) >= 0;
                DeadliftAttemptRuntime.BarProximityPath = _experimentalPath;
                Runtime = new DeadliftAttemptRuntime(foundation, athleteRig, barbell)
                {
                    PreparedClearanceRejoin = _preparedRejoin,
                    SolvePreparedPlanAcrossFrames = _preparedRejoin
                };
                try { Runtime.Initialize(); }
                finally { DeadliftAttemptRuntime.BarProximityPath = DeadliftAttemptRuntime.BarProximityPathDefault; }
                SquatPresentationGrip grip = athleteRig.GetComponent<SquatPresentationGrip>() ?? athleteRig.gameObject.AddComponent<SquatPresentationGrip>();
                grip.Configure(athleteRig, barbell);
                grip.ThumbAroundShaft = true;   // IPF 4.3(1): the grip is optional; thumbs around the bar
                PlaceCamera(_sideView);
                if (_preparedRehearsal)
                {
                    Runtime.Pull.IntentOverride01 = 0f;
                    Runtime.TickCompleted += RehearsePreparedPull;
                    foundation.Runtime.TicksSuspended = true;
                }
                IsInitialized = true;
                foundation.enabled = true;
                Debug.Log("GAM-70 deadlift station ready. Tick-0 penetration " +
                          (Runtime.TickZeroMaxPenetrationM * 1000f).ToString("F2", CultureInfo.InvariantCulture) + " mm\n" + Runtime.PathReport);
            }
            catch (Exception e)
            {
                _failure = e.Message;
                Debug.LogError("GAM-70 deadlift station startup failed: " + e);
            }
        }

        private void RehearsePreparedPull(DeadliftAttemptRuntime runtime)
        {
            if (runtime.Tick == 283)
                runtime.Pull.IntentOverride01 = 1f;
            if (runtime.Pull.Phase != _rehearsalPhase)
            {
                _rehearsalPhase = runtime.Pull.Phase;
                Debug.Log($"GAM-70 prepared rehearsal tick {runtime.Tick}: {_rehearsalPhase} | {runtime.Referee.Summary()}");
            }
            if (runtime.Pull.SeparationTick != 0)
            {
                DeadliftBarLegGeometry.Realized(athleteRig, runtime.Bar, out var gaps);
                _rehearsalMinGap = Mathf.Min(_rehearsalMinGap, gaps.Leg);
                if (runtime.PlateLeftN + runtime.PlateRightN < 600f)
                    _rehearsalMinFoot = Mathf.Min(_rehearsalMinFoot, runtime.FootLoadMinN);
                if (runtime.Pull.Phase == DeadliftPullPhase.Lockout)
                {
                    _rehearsalThighGapSum += gaps.Thigh;
                    _rehearsalLockoutTicks++;
                }
            }
            if (runtime.GripsReleased)
            {
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "GAM-70 prepared rehearsal returned and released at {0}: {1} | min shaft gap {2:F2} mm | min airborne foot {3:F1} N | mean lockout thigh gap {4:F2} mm",
                    runtime.Tick, runtime.Referee.Summary(), _rehearsalMinGap * 1000f, _rehearsalMinFoot,
                    _rehearsalLockoutTicks == 0 ? double.NaN : _rehearsalThighGapSum * 1000d / _rehearsalLockoutTicks));
                runtime.TickCompleted -= RehearsePreparedPull;
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

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }
            if (keyboard != null && keyboard.vKey.wasPressedThisFrame && IsInitialized)
            {
                _sideView = !_sideView;
                PlaceCamera(_sideView);
            }
            if (Runtime == null || !string.IsNullOrEmpty(_failure))
                return;
            if (_preparedRehearsal && !_rehearsalStarted)
            {
                _status = "Press W or Up to start the rehearsal.";
                if (keyboard != null && (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed))
                {
                    _rehearsalStarted = true;
                    foundation.Runtime.TicksSuspended = false;
                }
                return;
            }
            if (Runtime.PendingPreparedPlan is DeadliftPreparedAscentPlan pending)
            {
                _status = $"Solving the ascent reference ({pending.SolvedKeys}/{pending.KeyCount}).";
                try
                {
                    Runtime.AdvancePreparedSolve();
                }
                catch (Exception e)
                {
                    _failure = e.Message;
                    Debug.LogError("GAM-70 prepared reference failed: " + e);
                }
                return;
            }

            DeadliftPullController pull = Runtime.Pull;
            DeadliftReferee referee = Runtime.Referee;
            switch (pull.Phase)
            {
                case DeadliftPullPhase.Grounded:
                    _status = Runtime.Tick < (ulong)(DeadliftAttemptRuntime.PretensionStartTick + DeadliftAttemptRuntime.PretensionRampTicks)
                        ? "Setting the grip and taking the slack out of the bar..."
                        : _preparedRehearsal ? "Rehearsal started. The bar will pull automatically." : "Ready. Hold W (or Up arrow) to pull.";
                    break;
                case DeadliftPullPhase.Pulling: _status = "Pulling - the plates are unloading."; break;
                case DeadliftPullPhase.Aborted: _status = "Pull released before the bar left the floor. R to reset."; break;
                case DeadliftPullPhase.Separated:
                case DeadliftPullPhase.Lifting:
                    _status = pull.LiftHeld ? "Hold position (" + pull.LiftHoldReason + ")."
                        : pull.Intent01 > 0.05f ? "Driving the bar up." : "Bar held.";
                    if (!_preparedRehearsal && (pull.LiftHeld || pull.Intent01 <= 0.05f))
                        _status += " Keep W held to drive.";
                    break;
                case DeadliftPullPhase.Lockout: _status = "Locked out - stand still for the referee."; break;
                case DeadliftPullPhase.Lowering: _status = "DOWN - lowering under control."; break;
                case DeadliftPullPhase.Returned: _status = referee.Completed ? "Attempt complete. R for another attempt." : "Bar on the platform."; break;
            }
            if (pull.Phase != _loggedPhase)
            {
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "GAM-70 station: tick {0} phase {1} -> {2} | drive {3:F2} | bar {4:F3} m | {5}",
                    Runtime.Tick, _loggedPhase, pull.Phase, pull.LastRawDrive01, Runtime.LastObservation.BarY, referee.Summary()));
                _loggedPhase = pull.Phase;
            }
            if (referee.DownGiven && _downShownTick == 0)
                _downShownTick = Runtime.Tick;
        }

        private void EnsureStyles()
        {
            if (_big != null)
                return;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _big.normal.textColor = Color.white;
            _body = new GUIStyle(GUI.skin.label) { fontSize = 18 };
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
            GUI.Box(new Rect(16, 16, 560, _experimentalPath || _preparedRejoin ? 240 : 210), GUIContent.none);
            if (_preparedRejoin)
                GUI.Label(new Rect(30, 210, 540, 30), _preparedRehearsal ? "PREPARED REHEARSAL  |  scripted Drive" : "EXPERIMENTAL prepared ascent", _body);
            if (_experimentalPath)
                GUI.Label(new Rect(30, 210, 540, 30), "EXPERIMENTAL bar-proximity path (not qualified)", _body);
            GUI.Label(new Rect(30, 24, 540, 30), "DEADLIFT  -  " + DeadliftQualifiedSetup.BarKg.ToString("F0", CultureInfo.InvariantCulture) + " kg  (sumo, pronated)", _body);
            if (!string.IsNullOrEmpty(_failure))
            {
                GUI.Label(new Rect(30, 60, 540, 140), "Station error. Press R to restart.\n" + _failure, _body);
                return;
            }
            if (Runtime == null)
            {
                GUI.Label(new Rect(30, 60, 540, 30), _status, _body);
                return;
            }
            DeadliftObservation o = Runtime.LastObservation;
            DeadliftPullController pull = Runtime.Pull;
            GUI.Label(new Rect(30, 56, 540, 30), _status, _body);
            GUI.Label(new Rect(30, 88, 540, 30), string.Format(CultureInfo.InvariantCulture, "Bar height {0:F2} m   rise {1:F0} mm   drive {2:F0} %",
                o.BarY, Runtime.Referee.MaxBarRiseM * 1000.0, pull.Intent01 * 100f), _body);
            GUI.Label(new Rect(30, 118, 540, 30), string.Format(CultureInfo.InvariantCulture, "Knees {0:F0}/{1:F0} deg   torso {2:F0} deg   phase {3}",
                o.KneeFlexionLDeg, o.KneeFlexionRDeg, o.TorsoDeg, pull.Phase), _body);
            GUI.Label(new Rect(30, 148, 540, 30), _preparedRehearsal
                ? "W / Up start  |  R reset  |  V side view"
                : "W / Up: pull and drive  |  ESC abort  |  R reset  |  V side view", _body);
            if (!_preparedRehearsal)
                GUI.Label(new Rect(30, 178, 540, 30), string.Format(CultureInfo.InvariantCulture, "input  drive {0:F2}  brace {1:F2}  grip {2:F2}",
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
