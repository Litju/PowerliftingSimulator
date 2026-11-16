using NUnit.Framework;
using PowerliftingSimulator.Deadlift.Unity;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70: the deadlift referee's rule logic on synthetic observation
    /// sequences (IPF 2026 s4.3 / 4.3.1). The physical attempt itself is judged
    /// in PlayMode; these pin the decision rules.
    /// </summary>
    public sealed class DeadliftRefereeTests
    {
        private const double Dt = 0.01, RestY = 0.2231, BarN = 735.75;

        private sealed class Script
        {
            public readonly DeadliftReferee Referee = new DeadliftReferee(RestY, BarN, Dt, 12.0);
            public ulong Tick;
            public double BarY = RestY, Knee = 120, Torso = 43, ShoulderAhead = 0.36, FootL, FootR, ThighN;
            public bool GripL = true, GripR = true;

            public void Step(double vy, double plateN, int ticks = 1)
            {
                for (int i = 0; i < ticks; i++)
                {
                    Tick++;
                    BarY += vy * Dt;
                    Referee.Observe(new DeadliftObservation(Tick, Tick * Dt, BarY, vy, System.Math.Abs(vy), plateN, ThighN,
                        Knee, Knee, Torso, ShoulderAhead, FootL, FootR, GripL, GripR));
                }
            }

            public void Finish()
            {
                Knee = 4; Torso = 88; ShoulderAhead = 0.0;
            }

            /// <summary>Lift off, rise 0.8 m at 0.3 m/s, finish, hold motionless.</summary>
            public void Ascend(double hold = 40)
            {
                Step(0.0, BarN, 10);
                Step(0.3, 0.0, 267);
                Finish();
                Step(0.0, 0.0, (int)hold);
            }

            public void LowerAndLand()
            {
                Knee = 120; Torso = 43; ShoulderAhead = 0.36;
                Step(-0.3, 0.0, 266);
                Step(0.0, BarN, 5);
            }
        }

        [Test]
        public void A_FINISHED_MOTIONLESS_BAR_GETS_DOWN_AND_A_CONTROLLED_RETURN_IS_A_GOOD_LIFT()
        {
            var s = new Script();
            s.Ascend();
            Assert.That(s.Referee.DownGiven, Is.True, s.Referee.Summary());
            s.LowerAndLand();
            Assert.That(s.Referee.Completed, Is.True, s.Referee.Summary());
            Assert.That(s.Referee.GoodLift, Is.True, s.Referee.Summary());
        }

        [Test]
        public void NO_DOWN_WITHOUT_LOCKED_KNEES_OR_ERECT_SHOULDERS()
        {
            var s = new Script();
            s.Step(0.0, BarN, 10);
            s.Step(0.3, 0.0, 267);
            s.Knee = 20; s.Torso = 88; s.ShoulderAhead = 0.0;
            s.Step(0.0, 0.0, 60);
            Assert.That(s.Referee.DownGiven, Is.False, "knees at 20 deg are not locked\n" + s.Referee.Summary());
            s.Knee = 4; s.Torso = 70;
            s.Step(0.0, 0.0, 60);
            Assert.That(s.Referee.DownGiven, Is.False, "torso at 70 deg is not erect\n" + s.Referee.Summary());
            s.Torso = 88; s.ShoulderAhead = 0.08;
            s.Step(0.0, 0.0, 60);
            Assert.That(s.Referee.DownGiven, Is.False, "shoulders 80 mm ahead of the hips are not back\n" + s.Referee.Summary());
            s.ShoulderAhead = 0.0;
            s.Step(0.0, 0.0, 40);
            Assert.That(s.Referee.DownGiven, Is.True, s.Referee.Summary());
        }

        [Test]
        public void DOWNWARD_BAR_MOVEMENT_BEFORE_THE_FINAL_POSITION_IS_A_FAULT()
        {
            var s = new Script();
            s.Step(0.0, BarN, 10);
            s.Step(0.3, 0.0, 120);
            s.Step(-0.05, 0.0, 30);      // 15 mm down at 5 cm/s mid-ascent (a hitch)
            s.Step(0.3, 0.0, 160);
            s.Finish();
            s.Step(0.0, 0.0, 40);
            s.LowerAndLand();
            Assert.That(s.Referee.GoodLift, Is.False, s.Referee.Summary());
            Assert.That(s.Referee.Faults[0].Fault, Is.EqualTo(DeadliftFault.DownwardMovement), s.Referee.Summary());
        }

        [Test]
        public void SETTLING_AT_THE_FINISHED_POSITION_IS_NOT_A_FAULT_BUT_LOWERING_BEFORE_DOWN_IS()
        {
            var s = new Script();
            s.Step(0.0, BarN, 10);
            s.Step(0.3, 0.0, 267);
            s.Finish();
            s.Step(-0.08, 0.0, 12);      // 10 mm settle of a locked-out bar
            s.Step(0.0, 0.0, 40);
            Assert.That(s.Referee.Faults, Is.Empty, s.Referee.Summary());
            Assert.That(s.Referee.DownGiven, Is.True, s.Referee.Summary());

            var t = new Script();
            t.Step(0.0, BarN, 10);
            t.Step(0.3, 0.0, 267);
            t.Finish();
            t.Step(-0.3, 0.0, 20);       // 60 mm deliberate lowering with no Down
            Assert.That(t.Referee.Faults, Has.Some.Matches<DeadliftFaultRecord>(f => f.Fault == DeadliftFault.LoweredBeforeSignal), t.Referee.Summary());
        }

        [Test]
        public void THIGH_SUPPORT_STEPPING_AND_AN_UNCONTROLLED_RETURN_ARE_FAULTS()
        {
            var thigh = new Script();
            thigh.Step(0.0, BarN, 10);
            thigh.Step(0.3, 0.0, 100);
            thigh.ThighN = 200;
            thigh.Step(0.3, 0.0, 5);
            thigh.ThighN = 0;
            thigh.Step(0.3, 0.0, 162);
            thigh.Finish();
            thigh.Step(0.0, 0.0, 40);
            Assert.That(thigh.Referee.Faults, Has.Some.Matches<DeadliftFaultRecord>(f => f.Fault == DeadliftFault.ThighSupport), thigh.Referee.Summary());

            var step = new Script();
            step.Step(0.0, BarN, 10);
            step.FootL = 0.05;
            step.Step(0.3, 0.0, 10);
            Assert.That(step.Referee.Faults, Has.Some.Matches<DeadliftFaultRecord>(f => f.Fault == DeadliftFault.FootMoved), step.Referee.Summary());

            var drop = new Script();
            drop.Ascend();
            drop.GripL = drop.GripR = false;   // let go after Down: the bar falls
            drop.Step(-1.5, 0.0, 50);
            drop.Step(-2.0, BarN, 1);
            Assert.That(drop.Referee.Faults, Has.Some.Matches<DeadliftFaultRecord>(f => f.Fault == DeadliftFault.UncontrolledReturn), drop.Referee.Summary());
            Assert.That(drop.Referee.GoodLift, Is.False);
        }

        [Test]
        public void LOSING_THE_GRIP_OR_PUTTING_THE_BAR_BACK_DURING_THE_ASCENT_IS_NOT_A_COMPLETED_LIFT()
        {
            var s = new Script();
            s.Step(0.0, BarN, 10);
            s.Step(0.3, 0.0, 50);
            s.GripR = false;
            s.Step(0.0, 0.0, 1);
            Assert.That(s.Referee.Completed, Is.True);
            Assert.That(s.Referee.GoodLift, Is.False);
            Assert.That(s.Referee.Faults[0].Fault, Is.EqualTo(DeadliftFault.NotCompleted), s.Referee.Summary());
        }
    }
}
