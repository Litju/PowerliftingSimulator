using System.Collections;
using NUnit.Framework;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using H = PowerliftingSimulator.Bench.BenchTestHarness;

namespace PowerliftingSimulator.Bench
{
    /// <summary>Gate E: truthful results, retry, abort/spotter recovery and the return to the gym.</summary>
    public sealed partial class BenchGameFlowTests
    {
        [UnityTest]
        public IEnumerator GATE_E_GOOD_LIFT_RESULT_THEN_RETRY_GIVES_A_FRESH_ATTEMPT()
        {
            yield return H.LoadBench();
            var record = new FlowRecord();
            BenchPressPrototypeController bench = H.Bench;
            DrivePolicy(bench, abort: false);
            record.Sample(bench);
            Ui.RequestLiftOff();
            yield return RunUntilResult(record, bench);

            string report = record.Describe(bench);
            H.Write("gateE-good-lift-flow.txt", report);
            Assert.That(Ui.Screen, Is.EqualTo(BenchScreen.Result), report);
            Assert.That(bench.Referee.IsGoodLift, report);
            Assert.That(bench.AttemptFinished, "result before the bar was back in the rests\n" + report);
            Assert.That(Ui.DisplayedResultHeadline, Is.EqualTo("GOOD LIFT"), report);
            Assert.That(Ui.DisplayedResultMetrics, Does.Contain("Chest pause"), report);
            Assert.That(record.Cues, Does.Contain("RACK_CLANK"), report);
            Assert.That(record.Cues, Does.Contain("GOOD_LIFT"), report);
            Assert.That(record.Cues, Does.Not.Contain("NO_LIFT"), report);
            Assert.That(record.Screens, Is.EqualTo(new[] { BenchScreen.Setup, BenchScreen.Lift, BenchScreen.Result }), report);

            Ui.Retry();
            yield return WaitForBench(previous: bench);
            Assert.That(H.Bench, Is.Not.SameAs(bench), "retry did not reload a fresh attempt");
            Assert.That(Ui.Screen, Is.EqualTo(BenchScreen.Setup));
            Assert.That(H.Bench.Referee.State, Is.EqualTo(BenchRefereeState.AwaitingLiftOff));
            Assert.That(H.Bench.Referee.Faults, Is.Empty);
            Assert.That(H.Bench.LoadKg, Is.EqualTo(25f));
        }

        [UnityTest]
        public IEnumerator GATE_E_ABORT_IS_RESCUED_AND_REPORTED_AS_NO_LIFT_THEN_BACK_TO_THE_GYM()
        {
            yield return H.LoadBench();
            var record = new FlowRecord();
            BenchPressPrototypeController bench = H.Bench;
            DrivePolicy(bench, abort: true);
            record.Sample(bench);
            Ui.RequestLiftOff();
            yield return RunUntilResult(record, bench);

            string report = record.Describe(bench);
            H.Write("gateE-abort-flow.txt", report);
            Assert.That(Ui.Screen, Is.EqualTo(BenchScreen.Result), report);
            Assert.That(bench.Referee.IsGoodLift, Is.False, report);
            Assert.That(bench.Referee.HasFault(BenchFault.AttemptAborted), report);
            Assert.That(record.SpotterSeen, "the spotters never took the bar\n" + report);
            Assert.That(bench.AttemptFinished, "the spotters did not return the bar to the rests\n" + report);
            Assert.That(Ui.DisplayedResultHeadline, Is.EqualTo("NO LIFT"), report);
            Assert.That(Ui.DisplayedResultReasons, Does.Contain(BenchReferee.Describe(BenchFault.AttemptAborted)), report);
            Assert.That(record.Cues, Does.Contain("NO_LIFT"), report);
            Assert.That(record.Cues, Does.Not.Contain("GOOD_LIFT"), report);

            Ui.ReturnToGym();
            yield return WaitForScene(BenchGameUiController.GymSceneName);
            yield return null;
            PowerliftingGameUiController gym = Object.FindFirstObjectByType<PowerliftingGameUiController>();
            Assert.That(gym, Is.Not.Null);
            Assert.That(gym.CurrentScreen, Is.EqualTo(PowerliftingScreenState.TrainingGym));
        }

        [UnityTest]
        public IEnumerator GATE_E_PRESSING_BEFORE_THE_SIGNAL_SHOWS_ITS_RULE_ON_THE_RESULT()
        {
            yield return H.LoadBench();
            var record = new FlowRecord();
            BenchPressPrototypeController bench = H.Bench;
            DrivePolicy(bench, abort: false, pressEarly: true);
            record.Sample(bench);
            Ui.RequestLiftOff();
            yield return RunUntilResult(record, bench);

            string report = record.Describe(bench);
            H.Write("gateE-press-early-flow.txt", report);
            Assert.That(Ui.Screen, Is.EqualTo(BenchScreen.Result), report);
            Assert.That(Ui.DisplayedResultHeadline, Is.EqualTo("NO LIFT"), report);
            Assert.That(bench.Referee.HasFault(BenchFault.PressBeforeCommand), report);
            Assert.That(Ui.DisplayedResultReasons, Does.Contain(BenchReferee.RuleReference(BenchFault.PressBeforeCommand)), report);
            Assert.That(Ui.DisplayedResultReasons, Does.Contain(BenchReferee.Describe(BenchFault.PressBeforeCommand)), report);
        }

        private sealed partial class FlowRecord
        {
            partial void DescribeResult(System.Text.StringBuilder b)
            {
                b.AppendLine($"result headline: {Ui?.DisplayedResultHeadline}");
                b.AppendLine($"result reasons: {Ui?.DisplayedResultReasons}");
                b.AppendLine($"result metrics: {Ui?.DisplayedResultMetrics}");
            }
        }

        private static IEnumerator RunUntilResult(FlowRecord record, BenchPressPrototypeController bench)
        {
            float start = Time.realtimeSinceStartup;
            while (Ui.Screen != BenchScreen.Result && Time.realtimeSinceStartup - start < 240f)
            {
                record.Sample(bench);
                yield return null;
            }
            // Let the delayed verdict cue play.
            float shown = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - shown < 1.0f)
            {
                record.Sample(bench);
                yield return null;
            }
        }
    }
}
