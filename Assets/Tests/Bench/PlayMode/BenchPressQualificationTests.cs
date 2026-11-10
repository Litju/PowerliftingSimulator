using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using H = PowerliftingSimulator.Bench.BenchTestHarness;

namespace PowerliftingSimulator.Bench
{
    /// <summary>
    /// GAM-69 Gate C/D on the real solver: complete 25 kg attempts driven
    /// through the player intent path, judged by the independent referee from
    /// physical observations. The scripted lifter reacts to the referee's
    /// commands exactly as a player would; the negative cases make the lifter
    /// commit one physical fault and require the referee to call it.
    /// </summary>
    public sealed class BenchPressQualificationTests
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private enum Fault
        {
            None,
            PressBeforeCommand,
            NoTouch,
            Stall,
            Abort,
            DescentBeforeStart,
            ReversalDuringPressOut
        }

        [UnityTest]
        public IEnumerator GATE_C_LEGAL_25KG_PRESS_IS_PHYSICAL_AND_GOOD_LIFT()
        {
            yield return RunAttempt(Fault.None);
            BenchReferee referee = H.Bench.Referee;
            List<BenchTelemetrySample> t = H.Bench.Telemetry.Samples.ToList();
            string report = DescribeAttempt("gateC-legal", t, referee);

            Assert.That(referee.Faults, Is.Empty, report);
            Assert.That(referee.Commands.Select(c => c.Command), Is.EqualTo(new[] { BenchCommand.Start, BenchCommand.Press, BenchCommand.Rack }), report);
            Assert.That(referee.IsGoodLift, report);
            Assert.That(H.Bench.AttemptFinished, "bar was not safely reracked\n" + report);

            ulong start = referee.Commands[0].Tick, press = referee.Commands[1].Tick, rack = referee.Commands[2].Tick;
            List<BenchTelemetrySample> descent = t.Where(s => s.Tick > start && s.Tick < press).ToList();
            List<BenchTelemetrySample> pressOut = t.Where(s => s.Tick > press && s.Tick <= rack).ToList();
            // The bar descends and rises under solver forces: real displacement and momentum.
            float drop = t.First(s => s.Tick == start).BarPosition.y - descent.Min(s => s.BarPosition.y);
            float rise = pressOut.Max(s => s.BarPosition.y) - descent.Min(s => s.BarPosition.y);
            Assert.That(drop, Is.GreaterThan(0.18f), "the bar did not travel to the chest\n" + report);
            Assert.That(rise, Is.GreaterThan(0.18f), "the bar was not pressed back up\n" + report);
            Assert.That(descent.Min(s => s.BarVelocity.y), Is.LessThan(-0.10f), report);
            Assert.That(pressOut.Max(s => s.BarVelocity.y), Is.GreaterThan(0.10f), report);
            // During the press-out the hands push up harder than the bar weighs (they accelerate it).
            float barWeight = H.Bench.Barbell.Body.mass * 9.81f;
            Assert.That(pressOut.Max(s => -(s.LeftHandForceRaw.y + s.RightHandForceRaw.y)), Is.GreaterThan(barWeight), report);
            // Real chest touch with load, then lift-off of the chest.
            Assert.That(t.Where(s => s.Tick >= start && s.Tick <= press).Max(s => s.BarChestN), Is.GreaterThan(5f), report);
            // Finite actuators: on the qualified load the arm drives work below
            // saturation. Measured: the wrists brush their 45 N m ceiling for a
            // couple of ticks as the press decelerates into lockout (peak
            // ~1.06 of capacity), nowhere else. Bounded, not hidden: at most 1%
            // of lift-proper ticks and a peak of 1.25.
            List<BenchTelemetrySample> liftProper = t.Where(s => s.Tick >= start && s.Tick <= rack).ToList();
            float saturatedShare = liftProper.Count(s => s.ArmDemand.Max() > PoweredJointController.ModeledDemandSaturationThreshold) / (float)liftProper.Count;
            Assert.That(saturatedShare, Is.LessThanOrEqualTo(0.01f), report);
            Assert.That(liftProper.Max(s => s.ArmDemand.Max()), Is.LessThan(1.25f), report);
            // Supports held throughout the lift proper.
            List<BenchTelemetrySample> proper = t.Where(s => s.Tick >= start && s.Tick <= rack).ToList();
            Assert.That(proper.Min(s => s.HeadN), Is.GreaterThan(BenchRulebook.ContactForceFloorN), report);
            Assert.That(proper.Min(s => s.ShouldersN), Is.GreaterThan(BenchRulebook.ContactForceFloorN), report);
            Assert.That(proper.Min(s => s.ButtocksN), Is.GreaterThan(BenchRulebook.ContactForceFloorN), report);
            Assert.That(proper.Max(s => Mathf.Max(s.LeftGripSeparationM, s.RightGripSeparationM)), Is.LessThan(0.002f), report);
        }

        [UnityTest]
        public IEnumerator GATE_D_PRESSING_BEFORE_PRESS_IS_NO_LIFT()
        {
            yield return RunAttempt(Fault.PressBeforeCommand);
            AssertNoLift(BenchFault.PressBeforeCommand, "gateD-press-before-command");
        }

        [UnityTest]
        public IEnumerator GATE_D_MISSING_THE_CHEST_IS_NO_LIFT_AND_RACK()
        {
            yield return RunAttempt(Fault.NoTouch);
            AssertNoLift(BenchFault.NoChestTouch, "gateD-no-touch");
            Assert.That(H.Bench.Referee.PressGiven, Is.False);
        }

        [UnityTest]
        public IEnumerator GATE_D_STALLED_PRESS_IS_NO_LIFT_AND_THE_SPOTTERS_RERACK()
        {
            yield return RunAttempt(Fault.Stall);
            AssertNoLift(BenchFault.NotLockedOut, "gateD-stall");
            Assert.That(H.Bench.Telemetry.Samples.Any(s => s.SpotterActive), "spotters never took the bar");
            Assert.That(H.Bench.AttemptFinished, "the spotted bar was not reracked");
        }

        [UnityTest]
        public IEnumerator GATE_D_ABORT_CALLS_THE_SPOTTERS_AND_RECOVERS()
        {
            yield return RunAttempt(Fault.Abort);
            AssertNoLift(BenchFault.AttemptAborted, "gateD-abort");
            Assert.That(H.Bench.AttemptFinished, "the aborted bar was not reracked");
        }

        [UnityTest]
        public IEnumerator GATE_D_LOWERING_BEFORE_START_IS_A_FAULT()
        {
            yield return RunAttempt(Fault.DescentBeforeStart);
            BenchReferee referee = H.Bench.Referee;
            string report = DescribeAttempt("gateD-descent-before-start", H.Bench.Telemetry.Samples.ToList(), referee);
            Assert.That(referee.IsGoodLift, Is.False, report);
            Assert.That(referee.HasFault(BenchFault.DescentBeforeStart) || referee.HasFault(BenchFault.NotInPositionReplace), report);
        }

        [UnityTest]
        public IEnumerator GATE_D_DOWNWARD_BAR_DURING_PRESS_OUT_IS_NO_LIFT()
        {
            yield return RunAttempt(Fault.ReversalDuringPressOut);
            AssertNoLift(BenchFault.BarDownwardDuringPressOut, "gateD-reversal");
        }

        // ------------------------------------------------------------------

        private static void AssertNoLift(BenchFault expected, string name)
        {
            BenchReferee referee = H.Bench.Referee;
            string report = DescribeAttempt(name, H.Bench.Telemetry.Samples.ToList(), referee);
            Assert.That(referee.IsComplete, report);
            Assert.That(referee.IsGoodLift, Is.False, "spurious GOOD LIFT\n" + report);
            Assert.That(referee.HasFault(expected), $"expected {expected}\n" + report);
        }

        /// <summary>
        /// Runs one attempt. The lifter is a per-tick policy over the previous
        /// tick's physical state and referee commands, so a run is the same
        /// tick-for-tick however the render frames fall.
        /// </summary>
        private static IEnumerator RunAttempt(Fault fault)
        {
            yield return H.LoadBench();
            BenchPressPrototypeController bench = H.Bench;
            BenchPressController controller = bench.Controller;
            BenchReferee referee = bench.Referee;
            ulong liftOffTick = H.Tick + 60;
            ulong pressHoldUntil = 0;
            bool aborted = false, reversed = false;
            float midY = (bench.Model.BarLockout.y + bench.Model.BarTouch.y) * 0.5f;

            controller.ScriptedIntent = tick =>
            {
                bool confirm = false;
                float lower = 0f, press = 0f;
                BenchMotionPhase phase = controller.Phase;
                float barY = bench.Barbell.Body.position.y;

                if (phase == BenchMotionPhase.Racked && tick >= liftOffTick)
                    confirm = (tick - liftOffTick) % 2 == 0;

                switch (fault)
                {
                    case Fault.DescentBeforeStart:
                        if (phase == BenchMotionPhase.Holding || phase == BenchMotionPhase.Lowering)
                            lower = referee.StartGiven ? 0f : 0.6f;
                        break;
                    default:
                        if (referee.StartGiven && !referee.IsComplete)
                        {
                            bool touched = phase == BenchMotionPhase.OnChest || referee.State == BenchRefereeState.OnChest ||
                                referee.State == BenchRefereeState.PressOut;
                            if (fault == Fault.NoTouch)
                            {
                                if (barY > midY - 0.03f && phase != BenchMotionPhase.Pressing)
                                    lower = 0.8f;
                                else
                                    press = 1f;
                            }
                            else if (!touched)
                            {
                                lower = 0.8f;
                            }
                            else if (fault == Fault.PressBeforeCommand)
                            {
                                press = 1f;
                            }
                            else if (referee.PressGiven && fault != Fault.Stall)
                            {
                                press = 1f;
                                if (fault == Fault.ReversalDuringPressOut && !reversed && barY > midY)
                                {
                                    if (pressHoldUntil == 0)
                                        pressHoldUntil = tick + 70;
                                    if (tick < pressHoldUntil)
                                    {
                                        press = 0f;
                                        lower = 1f;
                                    }
                                    else
                                    {
                                        reversed = true;
                                    }
                                }
                            }
                            if (fault == Fault.Abort && phase == BenchMotionPhase.Lowering && barY < midY && !aborted)
                            {
                                aborted = true;
                                bench.RequestAbort();
                            }
                        }
                        break;
                }

                if (referee.IsComplete && !bench.SpotterActive && phase == BenchMotionPhase.Holding)
                    confirm = tick % 2 == 0;
                return new BenchIntent(lower, press, confirm, false);
            };

            ulong deadline = H.Tick + 4000;
            while (!(bench.AttemptFinished || (referee.IsComplete && TimeSinceComplete(referee) > 12.0)) && H.Tick < deadline)
                yield return null;
            yield return H.WaitTicks(20);
        }

        private static double TimeSinceComplete(BenchReferee referee) =>
            referee.Commands.Count == 0 ? 0d : H.Bench.Foundation.Runtime.CurrentTime.SimulationTimeSeconds - referee.Commands.Last().TimeSeconds;

        private static string DescribeAttempt(string name, List<BenchTelemetrySample> t, BenchReferee referee)
        {
            var b = new StringBuilder();
            b.AppendLine($"{name}: goodLift={referee.IsGoodLift} state={referee.State} finished={H.Bench.AttemptFinished}");
            foreach (BenchCommandRecord c in referee.Commands)
                b.AppendLine(string.Format(C, "  command {0,-7} tick {1,5} t={2:F2}s  {3}", c.Command, c.Tick, c.TimeSeconds, c.Basis));
            foreach (BenchFaultRecord f in referee.Faults)
                b.AppendLine(string.Format(C, "  FAULT {0} {1} tick {2} t={3:F2}s  {4}", f.RuleReference, f.Fault, f.Tick, f.TimeSeconds, f.Detail));
            if (t.Count > 0)
            {
                b.AppendLine(string.Format(C, "  bar y range {0:F3}..{1:F3} m, vy range {2:F3}..{3:F3} m/s, max |w| {4:F3} rad/s",
                    t.Min(s => s.BarPosition.y), t.Max(s => s.BarPosition.y), t.Min(s => s.BarVelocity.y), t.Max(s => s.BarVelocity.y),
                    t.Max(s => s.BarAngularVelocity.magnitude)));
                b.AppendLine(string.Format(C, "  max chest {0:F1} N, max belt {1:F1} N, max safety {2:F1} N, min head/shoulders/buttocks {3:F1}/{4:F1}/{5:F1} N, min feet {6:F1}/{7:F1} N",
                    t.Max(s => s.BarChestN), t.Max(s => s.BarBeltN), t.Max(s => s.BarSafetyN),
                    t.Min(s => s.HeadN), t.Min(s => s.ShouldersN), t.Min(s => s.ButtocksN), t.Min(s => s.LeftFootN), t.Min(s => s.RightFootN)));
                b.AppendLine(string.Format(C, "  elbow flex range {0:F1}..{1:F1} deg; max arm demand {2:F3}; max grip drift {3:F2} mm; max hand push {4:F1} N",
                    t.Min(s => Mathf.Min(s.LeftElbowFlexDeg, s.RightElbowFlexDeg)), t.Max(s => Mathf.Max(s.LeftElbowFlexDeg, s.RightElbowFlexDeg)),
                    t.Max(s => s.ArmDemand.Max()), t.Max(s => Mathf.Max(s.LeftGripSeparationM, s.RightGripSeparationM)) * 1000f,
                    t.Max(s => -(s.LeftHandForceRaw.y + s.RightHandForceRaw.y))));
                // Commanded vs realized at the touch.
                BenchTelemetrySample touch = t.OrderByDescending(s => s.BarChestN).First();
                b.Append(string.Format(C, "  at max chest load (tick {0}): ", touch.Tick));
                for (int i = 0; i < 6; i++)
                    b.Append(string.Format(C, "{0} {1:F1}/{2:F1}deg d{3:F2}; ", BenchSetupSolver.ArmJointIds[i], touch.ArmCommandedDeg[i], touch.ArmRealizedDeg[i], touch.ArmDemand[i]));
                b.AppendLine();
            }
            H.Write(name + ".txt", b.ToString());
            H.WriteTelemetry(name + ".csv");
            return b.ToString();
        }
    }
}
