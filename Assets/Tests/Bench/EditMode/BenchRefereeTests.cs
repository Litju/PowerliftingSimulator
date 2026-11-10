using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace PowerliftingSimulator.Bench
{
    /// <summary>
    /// Deterministic referee contract. Every negative case differs from the
    /// legal reference lift by one measured quantity, so a failure names one
    /// rule. Controls next to each negative case show that legal variation of
    /// the same quantity (chatter, foot movement while flat, a tilting bar)
    /// is not penalised.
    /// </summary>
    public sealed class BenchRefereeTests
    {
        private const double Dt = 0.01;

        [Test]
        public void LEGAL_LIFT_RECEIVES_START_PRESS_RACK_AND_GOOD_LIFT()
        {
            BenchReferee referee = Run(LegalLift());
            Assert.That(referee.Faults, Is.Empty, Faults(referee));
            Assert.That(referee.Commands.Select(c => c.Command),
                Is.EqualTo(new[] { BenchCommand.Start, BenchCommand.Press, BenchCommand.Rack }));
            Assert.That(referee.IsGoodLift, Is.True);
        }

        [Test]
        public void SAME_OBSERVATIONS_GIVE_THE_SAME_RECORD()
        {
            BenchReferee a = Run(LegalLift(o => o.BarDownwardMidPress = true));
            BenchReferee b = Run(LegalLift(o => o.BarDownwardMidPress = true));
            Assert.That(a.Commands.Select(c => (c.Command, c.Tick)), Is.EqualTo(b.Commands.Select(c => (c.Command, c.Tick))));
            Assert.That(a.Faults.Select(f => (f.Fault, f.Tick)), Is.EqualTo(b.Faults.Select(f => (f.Fault, f.Tick))));
        }

        [Test]
        public void START_IS_WITHHELD_UNTIL_THE_BAR_IS_MOTIONLESS_AND_ELBOWS_LOCKED()
        {
            var lift = new LiftScript();
            lift.Rack(20);
            lift.Hold(40, s => { s.ElbowFlex = 25; });            // bent elbows, still
            lift.Hold(40, s => { s.ElbowFlex = 2; s.BarSpeed = 0.05; }); // locked, moving
            BenchReferee referee = Run(lift.Samples);
            Assert.That(referee.StartGiven, Is.False);
            Assert.That(referee.State, Is.EqualTo(BenchRefereeState.AwaitingStart));
        }

        [Test]
        public void BRUSHING_THE_HOOK_LIPS_WHILE_LEAVING_THE_RESTS_IS_NOT_A_RETURN()
        {
            var lift = new LiftScript();
            lift.Rack(20);
            lift.Move(8, 1.00, 1.03, s => s.ElbowFlex = 15);
            lift.Hold(2, s => s.RackN = 30);                        // lip brush
            lift.Move(20, 1.03, 1.10, s => s.ElbowFlex = 2);
            lift.Hold(80, s => s.ElbowFlex = 2);
            BenchReferee referee = Run(lift.Samples);
            Assert.That(referee.HasFault(BenchFault.NotInPositionReplace), Is.False, Faults(referee));
            Assert.That(referee.StartGiven, Is.True, Faults(referee));
        }

        [Test]
        public void A_SUSTAINED_RETURN_TO_THE_RESTS_BEFORE_START_IS_REPLACE()
        {
            var lift = new LiftScript();
            lift.Rack(20);
            lift.Move(40, 1.00, 1.10, s => s.ElbowFlex = 2);
            lift.Hold(5, s => s.ElbowFlex = 25);
            lift.Hold(15, s => { s.ElbowFlex = 25; s.RackN = 200; });
            BenchReferee referee = Run(lift.Samples);
            Assert.That(referee.LastCommand, Is.EqualTo(BenchCommand.Replace), Faults(referee));
        }

        [Test]
        public void NOT_IN_POSITION_FOR_FIVE_SECONDS_IS_REPLACE()
        {
            var lift = new LiftScript();
            lift.Rack(20);
            lift.Hold(560, s => s.ElbowFlex = 25);
            BenchReferee referee = Run(lift.Samples);
            Assert.That(referee.LastCommand, Is.EqualTo(BenchCommand.Replace));
            Assert.That(referee.HasFault(BenchFault.NotInPositionReplace));
            Assert.That(referee.IsGoodLift, Is.False);
        }

        [TestCase(0.83, false)]
        [TestCase(0.80, true)]
        public void FOREFINGER_SPACING_ABOVE_81_CM_PREVENTS_START(double spacing, bool expectStart)
        {
            BenchReferee referee = Run(LegalLift(o => o.ForefingerSpacing = spacing));
            Assert.That(referee.StartGiven, Is.EqualTo(expectStart));
        }

        [Test]
        public void REVERSE_GRIP_PREVENTS_START()
        {
            BenchReferee referee = Run(LegalLift(o => o.ReverseGrip = true));
            Assert.That(referee.StartGiven, Is.False);
        }

        [Test]
        public void LOWERING_BEFORE_START_IS_A_SIGNAL_FAULT()
        {
            BenchReferee referee = Run(LegalLift(o => o.DescendBeforeStart = true));
            Assert.That(referee.HasFault(BenchFault.DescentBeforeStart), Faults(referee));
            Assert.That(referee.IsGoodLift, Is.False);
        }

        [Test]
        public void PRESSING_BEFORE_PRESS_IS_A_SIGNAL_FAULT()
        {
            BenchReferee referee = Run(LegalLift(o => o.SkipChestPause = true));
            Assert.That(referee.HasFault(BenchFault.PressBeforeCommand), Faults(referee));
            Assert.That(referee.PressGiven, Is.False);
            Assert.That(referee.IsGoodLift, Is.False);
        }

        [Test]
        public void BAR_MOVING_ON_THE_CHEST_NEVER_RECEIVES_PRESS()
        {
            BenchReferee referee = Run(LegalLift(o => o.WobbleOnChestSeconds = 7));
            Assert.That(referee.PressGiven, Is.False);
            Assert.That(referee.HasFault(BenchFault.NotMotionlessOnChest), Faults(referee));
        }

        [TestCase(3, true)]
        [TestCase(2, false)]
        public void BUTTOCKS_RAISED_FOR_THREE_TICKS_IS_A_FAULT_TWO_TICK_CHATTER_IS_NOT(int ticks, bool fault)
        {
            BenchReferee referee = Run(LegalLift(o => o.ButtocksLossTicksMidPress = ticks));
            Assert.That(referee.HasFault(BenchFault.ButtocksRaised), Is.EqualTo(fault), Faults(referee));
            Assert.That(referee.IsGoodLift, Is.EqualTo(!fault));
        }

        [Test]
        public void HEAD_AND_SHOULDERS_RAISED_ARE_SEPARATE_FAULTS()
        {
            BenchReferee head = Run(LegalLift(o => o.HeadLossTicksMidPress = 5));
            BenchReferee shoulders = Run(LegalLift(o => o.ShoulderLossTicksMidPress = 5));
            Assert.That(head.HasFault(BenchFault.HeadRaised) && !head.HasFault(BenchFault.ShouldersRaised));
            Assert.That(shoulders.HasFault(BenchFault.ShouldersRaised) && !shoulders.HasFault(BenchFault.HeadRaised));
        }

        [Test]
        public void A_FOOT_THAT_MOVES_WHILE_FLAT_IS_LEGAL_A_LIFTED_FOOT_IS_NOT()
        {
            BenchReferee moved = Run(LegalLift(o => o.FootMovesWhileFlat = true));
            BenchReferee lifted = Run(LegalLift(o => o.FootLiftedTicks = 6));
            BenchReferee tilted = Run(LegalLift(o => o.FootNotFlatTicks = 6));
            Assert.That(moved.IsGoodLift, Is.True, Faults(moved));
            Assert.That(lifted.HasFault(BenchFault.FootLifted), Faults(lifted));
            Assert.That(tilted.HasFault(BenchFault.FootLifted), Faults(tilted));
        }

        [Test]
        public void CONTACT_IS_GEOMETRIC_AN_UNLOADED_FLAT_FOOT_OR_HEAD_IS_STILL_IN_CONTACT()
        {
            // Zero normal force alone does not prove separation (GAM-69 review).
            BenchReferee unloadedFoot = Run(LegalLift(o => o.UnloadedFlatFootTicks = 40));
            BenchReferee unloadedHead = Run(LegalLift(o => o.UnloadedHeadTicks = 40));
            BenchReferee raised = Run(LegalLift(o => o.RaisedFootTicks = 6));
            Assert.That(unloadedFoot.IsGoodLift, Faults(unloadedFoot));
            Assert.That(unloadedHead.IsGoodLift, Faults(unloadedHead));
            Assert.That(raised.HasFault(BenchFault.FootLifted), Faults(raised));
            Assert.That(raised.IsGoodLift, Is.False);
        }

        [Test]
        public void UNOBSERVED_GEOMETRY_FALLS_BACK_TO_FORCE_EVIDENCE()
        {
            var flat = new BenchObservation(0, 0, 1, 0, 0, 0, 0, 1, 1, 2, 2, false, false,
                0, 300, 250, 0, 150, true, true, 0, 0, 0, 0, 0, true, true, 0, 0, 0.74, false, false, 0);
            Assert.That(flat.HeadInContact, Is.False);
            Assert.That(flat.LeftFootPlanted, Is.False);
            Assert.That(flat.ShouldersInContact, Is.True);
            var touching = new BenchObservation(0, 0, 1, 0, 0, 0, 0, 1, 1, 2, 2, false, false,
                0, 300, 250, 0, 150, true, true, 0, 0, 0, 0, 0, true, true, 0, 0, 0.74, false, false, 0,
                0.0, double.NaN, double.NaN, 0.004, double.NaN);
            Assert.That(touching.HeadInContact, Is.True);
            Assert.That(touching.HeadLoadBearing, Is.False);
            Assert.That(touching.LeftFootPlanted, Is.True);
            Assert.That(touching.LeftFootLoadBearing, Is.False);
        }

        [Test]
        public void FOOT_ON_THE_BENCH_IS_A_FAULT()
        {
            BenchReferee referee = Run(LegalLift(o => o.FootOnBenchTicks = 5));
            Assert.That(referee.HasFault(BenchFault.FootOnBench), Faults(referee));
        }

        [Test]
        public void WHOLE_BAR_DOWNWARD_DURING_PRESS_OUT_IS_A_FAULT_A_TILT_IS_NOT()
        {
            BenchReferee whole = Run(LegalLift(o => o.BarDownwardMidPress = true));
            BenchReferee tilt = Run(LegalLift(o => o.OneEndDownwardMidPress = true));
            Assert.That(whole.HasFault(BenchFault.BarDownwardDuringPressOut), Faults(whole));
            Assert.That(tilt.HasFault(BenchFault.BarDownwardDuringPressOut), Is.False, Faults(tilt));
        }

        [Test]
        public void DOWNWARD_MOVEMENT_DURING_THE_DESCENT_IS_NOT_A_PRESS_OUT_FAULT()
        {
            BenchReferee referee = Run(LegalLift());
            Assert.That(referee.HasFault(BenchFault.BarDownwardDuringPressOut), Is.False);
        }

        [Test]
        public void NO_CHEST_TOUCH_GIVES_RACK_AND_NO_LIFT()
        {
            BenchReferee referee = Run(LegalLift(o => o.MissChest = true));
            Assert.That(referee.HasFault(BenchFault.NoChestTouch), Faults(referee));
            Assert.That(referee.LastCommand, Is.EqualTo(BenchCommand.Rack));
            Assert.That(referee.PressGiven, Is.False);
            Assert.That(referee.IsGoodLift, Is.False);
        }

        [Test]
        public void BAR_ON_THE_BELT_GIVES_RACK_AND_NO_LIFT()
        {
            BenchReferee referee = Run(LegalLift(o => o.TouchBelt = true));
            Assert.That(referee.HasFault(BenchFault.BarOnBelt), Faults(referee));
            Assert.That(referee.LastCommand, Is.EqualTo(BenchCommand.Rack));
        }

        [Test]
        public void ELBOWS_ABOVE_SHOULDER_LEVEL_AT_THE_CHEST_IS_A_FAULT()
        {
            BenchReferee referee = Run(LegalLift(o => o.ElbowsReachDepth = false));
            Assert.That(referee.HasFault(BenchFault.ElbowDepthNotReached), Faults(referee));
            Assert.That(referee.IsGoodLift, Is.False);
        }

        [Test]
        public void SINKING_AFTER_THE_PAUSE_IS_A_FAULT()
        {
            BenchReferee referee = Run(LegalLift(o => o.SinkAfterPress = true));
            Assert.That(referee.HasFault(BenchFault.SinkAfterMotionless), Faults(referee));
        }

        [Test]
        public void UPPER_BODY_THRUST_IS_A_FAULT()
        {
            BenchReferee referee = Run(LegalLift(o => o.ThrustAfterPress = true));
            Assert.That(referee.HasFault(BenchFault.UpperBodyThrust), Faults(referee));
        }

        [Test]
        public void STALLED_PRESS_IS_NOT_LOCKED_OUT()
        {
            BenchReferee referee = Run(LegalLift(o => o.StallMidPress = true));
            Assert.That(referee.HasFault(BenchFault.NotLockedOut), Faults(referee));
            Assert.That(referee.LastCommand, Is.EqualTo(BenchCommand.Rack));
            Assert.That(referee.IsGoodLift, Is.False);
        }

        [Test]
        public void RACK_CONTACT_SPOTTER_CONTACT_SAFETIES_AND_GRIP_LOSS_ARE_FAULTS()
        {
            Assert.That(Run(LegalLift(o => o.RackTouchMidPress = true)).HasFault(BenchFault.RackContact));
            Assert.That(Run(LegalLift(o => o.SpotterMidPress = true)).HasFault(BenchFault.SpotterAssistance));
            BenchReferee safety = Run(LegalLift(o => o.SafetyOnDescent = true));
            Assert.That(safety.HasFault(BenchFault.BarOnSafeties));
            Assert.That(safety.LastCommand, Is.EqualTo(BenchCommand.Rack));
            BenchReferee grip = Run(LegalLift(o => o.GripLostMidPress = true));
            Assert.That(grip.HasFault(BenchFault.GripLost));
            Assert.That(grip.IsGoodLift, Is.False);
        }

        [Test]
        public void HAND_SLIP_ON_THE_BAR_IS_A_FAULT()
        {
            BenchReferee referee = Run(LegalLift(o => o.HandSlip = 0.015));
            Assert.That(referee.HasFault(BenchFault.HandMovedOnBar), Faults(referee));
        }

        [Test]
        public void RACK_REQUIRES_LOCKED_ELBOWS()
        {
            BenchReferee referee = Run(LegalLift(o => o.FinalElbowFlex = 18));
            Assert.That(referee.RackGiven && referee.IsGoodLift, Is.False);
            Assert.That(referee.HasFault(BenchFault.NotLockedOut), Faults(referee));
        }

        private static string Faults(BenchReferee referee) =>
            string.Join("; ", referee.Faults.Select(f => $"{f.Fault}@{f.Tick}: {f.Detail}"));

        private static BenchReferee Run(IEnumerable<BenchObservation> samples)
        {
            var referee = new BenchReferee(Dt);
            foreach (BenchObservation sample in samples)
                referee.Observe(sample);
            return referee;
        }

        private sealed class LiftOptions
        {
            public double ForefingerSpacing = 0.74;
            public bool ReverseGrip;
            public bool DescendBeforeStart;
            public bool SkipChestPause;
            public double WobbleOnChestSeconds;
            public int ButtocksLossTicksMidPress;
            public int HeadLossTicksMidPress;
            public int ShoulderLossTicksMidPress;
            public bool FootMovesWhileFlat;
            public int FootLiftedTicks;
            public int FootNotFlatTicks;
            public int FootOnBenchTicks;
            public int UnloadedFlatFootTicks;
            public int RaisedFootTicks;
            public int UnloadedHeadTicks;
            public bool BarDownwardMidPress;
            public bool OneEndDownwardMidPress;
            public bool MissChest;
            public bool TouchBelt;
            public bool ElbowsReachDepth = true;
            public bool SinkAfterPress;
            public bool ThrustAfterPress;
            public bool StallMidPress;
            public bool RackTouchMidPress;
            public bool SpotterMidPress;
            public bool SafetyOnDescent;
            public bool GripLostMidPress;
            public double HandSlip;
            public double FinalElbowFlex = 2;
        }

        private static IEnumerable<BenchObservation> LegalLift(Action<LiftOptions> configure = null)
        {
            var o = new LiftOptions();
            configure?.Invoke(o);
            var lift = new LiftScript();
            lift.Spec.ForefingerSpacing = o.ForefingerSpacing;
            lift.Spec.ReverseGrip = o.ReverseGrip;

            const double lockY = 1.10, chestY = 0.70;
            lift.Spec.BarY = 1.00;
            lift.Rack(20);
            lift.Move(20, 1.00, lockY, s => s.ElbowFlex = 2);
            if (o.DescendBeforeStart)
                lift.Move(10, lockY, lockY - 0.05, s => s.ElbowFlex = 8);
            lift.Hold(45, s => s.ElbowFlex = 2);

            double bottom = o.MissChest ? chestY + 0.06 : chestY;
            lift.Move(80, lift.Spec.BarY, bottom, s =>
            {
                s.ElbowFlex = 2 + 85 * Progress(s.BarY, lockY, chestY);
                s.ElbowDepth = o.ElbowsReachDepth && s.BarY < chestY + 0.03;
                if (o.SafetyOnDescent && s.BarY < chestY + 0.05) s.SafetyN = 80;
            });

            if (o.MissChest)
            {
                lift.Move(40, bottom, lockY, s => s.ElbowFlex = 2 + 85 * Progress(s.BarY, lockY, chestY));
                lift.Hold(40, s => s.ElbowFlex = 2);
                return lift.Samples;
            }

            lift.Spec.ChestN = o.TouchBelt ? 0 : 60;
            lift.Spec.BeltN = o.TouchBelt ? 60 : 0;
            if (o.SkipChestPause)
            {
                lift.Hold(3, s => s.BarSpeed = 0.15);
            }
            else if (o.WobbleOnChestSeconds > 0)
            {
                lift.Hold((int)(o.WobbleOnChestSeconds / Dt), s => s.BarSpeed = 0.05);
                return lift.Samples;
            }
            else
            {
                lift.Hold(40, null);
            }

            if (o.SinkAfterPress)
                lift.Move(10, lift.Spec.BarY, lift.Spec.BarY - 0.02, null);
            if (o.ThrustAfterPress)
                lift.Hold(3, s => s.ThoraxVy = 0.2);

            lift.Spec.ChestN = 0;
            lift.Spec.BeltN = 0;
            double finalY = lockY - (o.FinalElbowFlex > 10 ? 0.05 : 0);
            double midY = 0.5 * (chestY + lockY);
            lift.Move(40, chestY, midY, s => s.ElbowFlex = 2 + 85 * Progress(s.BarY, lockY, chestY));

            if (o.BarDownwardMidPress)
                lift.Move(10, midY, midY - 0.03, null);
            if (o.OneEndDownwardMidPress)
                lift.Hold(10, s => { s.LeftEndVy = -0.06; s.LeftEndOffset -= 0.006; });
            if (o.ButtocksLossTicksMidPress > 0)
                lift.Hold(o.ButtocksLossTicksMidPress, s => s.ButtocksN = 0);
            if (o.HeadLossTicksMidPress > 0)
                lift.Hold(o.HeadLossTicksMidPress, s => s.HeadN = 0);
            if (o.ShoulderLossTicksMidPress > 0)
                lift.Hold(o.ShoulderLossTicksMidPress, s => s.ShouldersN = 0);
            if (o.FootMovesWhileFlat)
                lift.Hold(10, s => s.BarSpeed = 0.0);
            if (o.FootLiftedTicks > 0)
                lift.Hold(o.FootLiftedTicks, s => s.LeftFootN = 0);
            // Geometry observed: a flat foot touching the platform with no load,
            // a foot 20 mm above it, and a head resting on the pad unloaded.
            if (o.UnloadedFlatFootTicks > 0)
                lift.Hold(o.UnloadedFlatFootTicks, s => { s.LeftFootN = 0; s.LeftFootGap = 0.0005; });
            if (o.RaisedFootTicks > 0)
                lift.Hold(o.RaisedFootTicks, s => { s.LeftFootN = 0; s.LeftFootGap = 0.020; });
            if (o.UnloadedHeadTicks > 0)
                lift.Hold(o.UnloadedHeadTicks, s => { s.HeadN = 0; s.HeadGap = 0.001; });
            if (o.FootNotFlatTicks > 0)
                lift.Hold(o.FootNotFlatTicks, s => s.LeftFootFlat = false);
            if (o.FootOnBenchTicks > 0)
                lift.Hold(o.FootOnBenchTicks, s => s.FeetOnBenchN = 40);
            if (o.RackTouchMidPress)
                lift.Hold(4, s => s.RackN = 30);
            if (o.SpotterMidPress)
                lift.Hold(2, s => s.Spotter = true);
            if (o.GripLostMidPress)
            {
                lift.Hold(2, s => s.RightGrip = false);
                return lift.Samples;
            }
            if (o.HandSlip > 0)
                lift.Hold(2, s => s.LeftSlip = o.HandSlip);
            if (o.StallMidPress)
            {
                lift.Hold(350, s => s.ElbowFlex = 40);
                return lift.Samples;
            }

            lift.Move(40, lift.Spec.BarY, finalY, s => s.ElbowFlex = Math.Max(o.FinalElbowFlex, 2 + 85 * Progress(s.BarY, lockY, chestY)));
            lift.Hold(o.FinalElbowFlex > 10 ? 400 : 45, s => s.ElbowFlex = o.FinalElbowFlex);
            return lift.Samples;
        }

        private static double Progress(double y, double top, double bottom) =>
            Math.Max(0, Math.Min(1, (top - y) / (top - bottom)));

        private sealed class Spec
        {
            public double BarY = 1.0;
            public double BarSpeed;
            public double LeftEndVy, RightEndVy;
            public double LeftEndOffset, RightEndOffset;
            public double ElbowFlex = 20;
            public bool ElbowDepth;
            public double HeadN = 60, ShouldersN = 300, ButtocksN = 250;
            public double LeftFootN = 150, RightFootN = 150;
            public bool LeftFootFlat = true, RightFootFlat = true;
            public double FeetOnBenchN;
            public double HeadGap = double.NaN, ShouldersGap = double.NaN, ButtocksGap = double.NaN;
            public double LeftFootGap = double.NaN, RightFootGap = double.NaN;
            public double ChestN, BeltN, RackN, SafetyN;
            public bool LeftGrip = true, RightGrip = true;
            public double LeftSlip, RightSlip;
            public double ForefingerSpacing = 0.74;
            public bool ReverseGrip;
            public bool Spotter;
            public double ThoraxVy;
            public double Offset;

            public Spec Clone() => (Spec)MemberwiseClone();
        }

        private sealed class LiftScript
        {
            private ulong _tick;
            public readonly Spec Spec = new Spec();
            public readonly List<BenchObservation> Samples = new List<BenchObservation>();

            public void Rack(int ticks)
            {
                Spec.RackN = 120;
                Hold(ticks, null);
                Spec.RackN = 0;
            }

            /// <summary>Holds the current spec; per-tick overrides are temporary.</summary>
            public void Hold(int ticks, Action<Spec> overrideTick)
            {
                for (int i = 0; i < ticks; i++)
                {
                    Spec s = Spec.Clone();
                    overrideTick?.Invoke(s);
                    Emit(s);
                    if (overrideTick != null)
                        Spec.ElbowFlex = s.ElbowFlex;
                }
            }

            /// <summary>Moves the bar linearly; the speed is the real finite difference.</summary>
            public void Move(int ticks, double fromY, double toY, Action<Spec> overrideTick)
            {
                double v = (toY - fromY) / (ticks * Dt);
                for (int i = 1; i <= ticks; i++)
                {
                    Spec.BarY = fromY + (toY - fromY) * i / ticks;
                    Spec s = Spec.Clone();
                    s.BarSpeed = Math.Abs(v);
                    s.LeftEndVy = v;
                    s.RightEndVy = v;
                    overrideTick?.Invoke(s);
                    Emit(s);
                    Spec.ElbowFlex = s.ElbowFlex;
                }
            }

            private void Emit(Spec s)
            {
                Samples.Add(new BenchObservation(
                    _tick, _tick * Dt,
                    s.BarY, s.Offset, s.BarSpeed, s.LeftEndVy, s.RightEndVy,
                    s.BarY + s.LeftEndOffset, s.BarY + s.RightEndOffset,
                    s.ElbowFlex, s.ElbowFlex, s.ElbowDepth, s.ElbowDepth,
                    s.HeadN, s.ShouldersN, s.ButtocksN, s.LeftFootN, s.RightFootN,
                    s.LeftFootFlat, s.RightFootFlat, s.FeetOnBenchN,
                    s.ChestN, s.BeltN, s.RackN, s.SafetyN,
                    s.LeftGrip, s.RightGrip, s.LeftSlip, s.RightSlip,
                    s.ForefingerSpacing, s.ReverseGrip, s.Spotter, s.ThoraxVy,
                    s.HeadGap, s.ShouldersGap, s.ButtocksGap, s.LeftFootGap, s.RightFootGap));
                _tick++;
            }
        }
    }
}
