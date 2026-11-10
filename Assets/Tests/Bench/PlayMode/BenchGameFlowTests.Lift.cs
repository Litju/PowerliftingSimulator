using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Bench.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using H = PowerliftingSimulator.Bench.BenchTestHarness;

namespace PowerliftingSimulator.Bench
{
    /// <summary>Gate E: the active-lift HUD, referee signals, audio and camera during a real 25 kg attempt.</summary>
    public sealed partial class BenchGameFlowTests
    {
        [UnityTest]
        public IEnumerator GATE_E_HUD_SHOWS_AND_SOUNDS_START_PRESS_RACK_DURING_A_PHYSICAL_LIFT()
        {
            yield return H.LoadBench();
            var record = new FlowRecord();
            BenchPressPrototypeController bench = H.Bench;
            Assert.That(Ui.Screen, Is.EqualTo(BenchScreen.Setup));
            DrivePolicy(bench, abort: false);

            BenchCameraDirector cam = Object.FindFirstObjectByType<BenchCameraDirector>();
            Assert.That(cam, Is.Not.Null);
            var angles = new HashSet<BenchCameraAngle> { cam.Angle };
            for (int i = 0; i < 4; i++)
            {
                Ui.CycleCamera();
                angles.Add(cam.Angle);
            }
            Assert.That(angles.Count, Is.EqualTo(4), "the camera should cycle through four views");

            record.Sample(bench);
            Ui.RequestLiftOff();
            float start = Time.realtimeSinceStartup;
            while (!bench.AttemptFinished && Time.realtimeSinceStartup - start < 240f)
            {
                record.Sample(bench);
                yield return null;
            }
            record.Sample(bench);
            string report = record.Describe(bench);
            H.Write("gateE-hud-lift.txt", report);

            Assert.That(bench.AttemptFinished, "the bar was not reracked\n" + report);
            Assert.That(bench.Referee.IsGoodLift, report);
            Assert.That(record.Screens.Take(2), Is.EqualTo(new[] { BenchScreen.Setup, BenchScreen.Lift }), report);
            // Each signal is shown in the banner and sounded, in order.
            Assert.That(record.Banners.Where(b => b == "START" || b == "PRESS" || b == "RACK"), Is.EqualTo(new[] { "START", "PRESS", "RACK" }), report);
            Assert.That(record.Cues.Where(c => c.StartsWith("REFEREE_")), Is.EqualTo(new[] { "REFEREE_START", "REFEREE_PRESS", "REFEREE_RACK" }), report);
            // The instructions followed the lift: wait, lower, hold on the chest, press.
            Assert.That(record.Instructions.Any(s => s.Contains("wait for START")), report);
            Assert.That(record.Instructions.Any(s => s.Contains("lower the bar")), report);
            Assert.That(record.Instructions.Any(s => s.Contains("wait for PRESS")), report);
            Assert.That(record.Instructions.Any(s => s.Contains("press to locked elbows")), report);
            Assert.That(record.BarKinematicEver, Is.False, "a kinematic bar writer appeared\n" + report);
            Assert.That(record.ForeignBarJointsDuringJudgedLift, Is.EqualTo(0), "a joint other than the grips held the bar during the judged lift\n" + report);
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// The lifter: a per-tick policy over the previous tick's physical state
        /// and the referee's signals, entering through the same intent path as
        /// the keyboard (lower = S, press = W, confirm = Space).
        /// </summary>
        private static void DrivePolicy(BenchPressPrototypeController bench, bool abort, bool pressEarly = false)
        {
            BenchPressController controller = bench.Controller;
            BenchReferee referee = bench.Referee;
            float midY = (bench.Model.BarLockout.y + bench.Model.BarTouch.y) * 0.5f;
            bool aborted = false;
            controller.ScriptedIntent = tick =>
            {
                float lower = 0f, press = 0f;
                bool confirm = false;
                BenchMotionPhase phase = controller.Phase;
                if (referee.StartGiven && !referee.IsComplete)
                {
                    bool touched = phase == BenchMotionPhase.OnChest || referee.State == BenchRefereeState.OnChest || referee.State == BenchRefereeState.PressOut;
                    if (!touched)
                        lower = 0.8f;
                    else if (pressEarly || referee.PressGiven)
                        press = 1f;
                    if (abort && !aborted && phase == BenchMotionPhase.Lowering && bench.Barbell.Body.position.y < midY)
                    {
                        aborted = true;
                        bench.RequestAbort();
                    }
                }
                if (referee.IsComplete && !bench.SpotterActive && phase == BenchMotionPhase.Holding)
                    confirm = tick % 2 == 0;
                return new BenchIntent(lower, press, confirm, false);
            };
        }

        private sealed partial class FlowRecord
        {
            public readonly List<string> Banners = new List<string>();
            public readonly List<string> Instructions = new List<string>();
            public readonly List<string> Cues = new List<string>();
            public readonly List<BenchScreen> Screens = new List<BenchScreen>();
            public bool BarKinematicEver;
            public bool SpotterSeen;
            public int ForeignBarJointsDuringJudgedLift;

            public void Sample(BenchPressPrototypeController bench)
            {
                BenchGameUiController ui = Ui;
                if (ui == null)
                    return;
                AddChanged(Screens, ui.Screen);
                if (ui.Screen == BenchScreen.Lift)
                {
                    AddChanged(Banners, ui.DisplayedBanner);
                    AddChanged(Instructions, ui.DisplayedInstruction);
                }
                BenchAudioCues audio = Object.FindFirstObjectByType<BenchAudioCues>();
                if (audio != null && audio.CueHistory.Count >= Cues.Count)
                {
                    Cues.Clear();
                    Cues.AddRange(audio.CueHistory);
                }
                BarKinematicEver |= bench.Barbell.Body.isKinematic || !bench.Barbell.Body.useGravity;
                SpotterSeen |= bench.SpotterActive;
                if (bench.Referee.StartGiven && !bench.Referee.IsComplete)
                {
                    // Any joint owned by the bar or connected to it, other than the two grips.
                    Rigidbody bar = bench.Barbell.Body;
                    foreach (Joint j in Object.FindObjectsByType<Joint>(FindObjectsSortMode.None))
                    {
                        bool touchesBar = j.connectedBody == bar || j.GetComponent<Rigidbody>() == bar;
                        if (touchesBar && j != bench.LeftGrip.Joint && j != bench.RightGrip.Joint)
                            ForeignBarJointsDuringJudgedLift++;
                    }
                }
            }

            partial void DescribeResult(StringBuilder b);

            private static void AddChanged<T>(List<T> list, T value)
            {
                if (list.Count == 0 || !EqualityComparer<T>.Default.Equals(list[list.Count - 1], value))
                    list.Add(value);
            }

            public string Describe(BenchPressPrototypeController bench)
            {
                var b = new StringBuilder();
                b.AppendLine("screens: " + string.Join(" -> ", Screens));
                b.AppendLine("banners: " + string.Join(" | ", Banners));
                b.AppendLine("instructions: " + string.Join(" | ", Instructions));
                b.AppendLine("audio cues: " + string.Join(" | ", Cues));
                DescribeResult(b);
                foreach (BenchCommandRecord c in bench.Referee.Commands)
                    b.AppendLine(string.Format(C, "  command {0} tick {1} t={2:F2}s {3}", c.Command, c.Tick, c.TimeSeconds, c.Basis));
                foreach (BenchFaultRecord f in bench.Referee.Faults)
                    b.AppendLine(string.Format(C, "  FAULT {0} {1} t={2:F2}s {3}", f.RuleReference, f.Fault, f.TimeSeconds, f.Detail));
                b.AppendLine($"spotter seen {SpotterSeen}, bar kinematic ever {BarKinematicEver}, joints on the bar during the judged lift {ForeignBarJointsDuringJudgedLift}, reracked {bench.AttemptFinished}");
                return b.ToString();
            }
        }
    }
}
