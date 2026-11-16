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
                Runtime = new DeadliftAttemptRuntime(foundation, athleteRig, barbell);
                Runtime.Initialize();
                SquatPresentationGrip grip = athleteRig.GetComponent<SquatPresentationGrip>() ?? athleteRig.gameObject.AddComponent<SquatPresentationGrip>();
                grip.Configure(athleteRig, barbell);
                grip.ThumbAroundShaft = true;   // IPF 4.3(1): the grip is optional; thumbs around the bar
                PlaceCamera();
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

        private static void PlaceCamera()
        {
            Camera cam = Camera.main ?? FindFirstObjectByType<Camera>();
            if (cam == null)
                return;
            // Three-quarter side view, as a side judge sees the lift.
            cam.transform.position = new Vector3(2.6f, 1.25f, 1.6f);
            cam.transform.LookAt(new Vector3(0f, 0.75f, -0.05f));
            cam.fieldOfView = 45f;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            if (Runtime == null)
                return;
            DeadliftPullController pull = Runtime.Pull;
            DeadliftReferee referee = Runtime.Referee;
            switch (pull.Phase)
            {
                case DeadliftPullPhase.Grounded:
                    _status = Runtime.Tick < (ulong)(DeadliftAttemptRuntime.PretensionStartTick + DeadliftAttemptRuntime.PretensionRampTicks)
                        ? "Setting the grip and taking the slack out of the bar..."
                        : "Hold SPACE (brace) + LEFT SHIFT (grip), then hold W to pull.";
                    break;
                case DeadliftPullPhase.Pulling: _status = "Pulling - the plates are unloading."; break;
                case DeadliftPullPhase.Aborted: _status = "Pull released before the bar left the floor. R to reset."; break;
                case DeadliftPullPhase.Separated:
                case DeadliftPullPhase.Lifting:
                    _status = pull.LiftHeld ? "Hold position (" + pull.LiftHoldReason + ") - keep W held."
                        : pull.Intent01 > 0.05f ? "Driving the bar up." : "Bar held - hold W to keep driving.";
                    break;
                case DeadliftPullPhase.Lockout: _status = "Locked out - stand still for the referee."; break;
                case DeadliftPullPhase.Lowering: _status = "DOWN - lowering under control."; break;
                case DeadliftPullPhase.Returned: _status = referee.Completed ? "Attempt complete. R for another attempt." : "Bar on the platform."; break;
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
            GUI.Box(new Rect(16, 16, 560, 210), GUIContent.none);
            GUI.Label(new Rect(30, 24, 540, 30), "DEADLIFT  -  " + DeadliftQualifiedSetup.BarKg.ToString("F0", CultureInfo.InvariantCulture) + " kg  (sumo, pronated)", _body);
            if (!string.IsNullOrEmpty(_failure))
            {
                GUI.Label(new Rect(30, 60, 540, 140), "Station failed to start:\n" + _failure, _body);
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
            GUI.Label(new Rect(30, 148, 540, 70), "SPACE brace  |  LEFT SHIFT grip  |  W pull  |  ESC abort  |  R reset", _body);

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
