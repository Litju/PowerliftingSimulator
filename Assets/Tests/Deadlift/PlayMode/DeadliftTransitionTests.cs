using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Equipment;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    public sealed class DeadliftTransitionTests
    {
        private static readonly string[] Sides = { "left", "right" };
        private static readonly string[] Landmarks = { "upper_arm", "thigh", "shank", "foot" };
        private DeadliftPlantWorld _world;

        private sealed class Run
        {
            public ulong Pull, PhysicalSeparation, ConfirmedSeparation, FirstPenetration, Stop;
            public string StopReason;
            public bool Good, Released;
            public ulong Down;
            public string RefereeSummary;
            public ulong ContinuationTick;
            public float PrefixSweptGap;
            public float MinGap = float.PositiveInfinity;
            public float MinSupport = float.PositiveInfinity;
            public float MinAirborneFootN = float.PositiveInfinity;
            public double LockoutGapSum;
            public int LockoutSamples;
            public double MeanLockoutGap => LockoutGapSum / LockoutSamples;
            public DeadliftRealizedClearanceSweep Sweep;
            public string SeparationState, SeparationChannels;
            public Quaternion LeftRequestAfterSeparation, RightRequestAfterSeparation;
            public DeadliftReferenceAdmission.Result AscentAdmission, PostKneeAdmission;
            public string Rejection = string.Empty;
            public ulong RejectionTick;
            public bool Suspended;
            public double MaxTickMs;
            public ulong MaxTickMsTick;
            /// <summary>GAM-70 §4 mechanism comparison: task-space state at the settled arrival (progress ≥ 0.99).</summary>
            public float ArrivalBarZ, ArrivalBarVz, ArrivalHipZ, ArrivalComZ, ArrivalComVz, ArrivalGap, ArrivalDeltoidM;
            /// <summary>Peak drive demands (fraction of ceiling) over the continuation window.</summary>
            public float MaxAnkleX, MaxAnkleZ, MaxAbdomen, MaxThorax, MaxShoulder;
            /// <summary>Measured vertical thigh assistance and shin contact over the continuation window (N).</summary>
            public float MaxThighAssistN, MaxShinN;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_world != null)
                yield return _world.Teardown();
            _world = null;
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 matched B2 transition reconstruction")]
        public IEnumerator GAM70_PREPARED_TRANSITION_RECONSTRUCTION()
        {
            var quiet = new Run();
            yield return Trace("quiet", 301, quiet);
            var prepared = new Run();
            yield return Trace("release", 284, prepared);

            Assert.That(quiet.Pull, Is.EqualTo(301ul));
            Assert.That(prepared.Pull, Is.EqualTo(quiet.Pull));
            Assert.That(quiet.PhysicalSeparation, Is.GreaterThan(quiet.Pull));
            Assert.That(prepared.PhysicalSeparation, Is.GreaterThan(prepared.Pull));
            Assert.That(quiet.StopReason, Is.EqualTo("knee-region end"));
            Assert.That(prepared.StopReason, Is.EqualTo("bar-leg impulse"));
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 prepared offset-only A/B through first failure")]
        public IEnumerator GAM70_PREPARED_SHOULDER_RELEASE_AB()
        {
            var release = new Run();
            yield return Trace("ab-release", 284, release);
            Assert.That(release.Pull, Is.EqualTo(301ul));
            Assert.That(release.StopReason, Is.EqualTo("bar-leg impulse"));

            var continuous = new Run();
            yield return Trace("ab-continuous", 284, continuous, retainPreparation: true, lastTick: (int)release.Stop);
            Assert.That(continuous.Pull, Is.EqualTo(release.Pull));
            Assert.That(continuous.ConfirmedSeparation, Is.EqualTo(release.ConfirmedSeparation));
            Assert.That(continuous.SeparationState == release.SeparationState, Is.True, "Plant matches through confirmed separation.");
            Assert.That(continuous.SeparationChannels == release.SeparationChannels, Is.True, "Commands match through confirmed separation.");
            Assert.That(Quaternion.Angle(continuous.LeftRequestAfterSeparation, release.LeftRequestAfterSeparation), Is.EqualTo(2f).Within(0.005f));
            Assert.That(Quaternion.Angle(continuous.RightRequestAfterSeparation, release.RightRequestAfterSeparation), Is.EqualTo(2f).Within(0.005f));
            Assert.That(continuous.Stop, Is.InRange(release.ConfirmedSeparation + 1, release.Stop));
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 physically closed prepared first ascent segment")]
        public IEnumerator GAM70_PREPARED_CLOSED_FIRST_SEGMENT()
        {
            var prepared = new Run();
            yield return Trace("closed-prefix", 284, prepared, retainPreparation: true, closedPrefix: true, lastTick: 470);
            AssertClearance(prepared, "first segment end");
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 extend the prepared reference through the knee region")]
        public IEnumerator GAM70_PREPARED_CLOSED_KNEE_PASSAGE()
        {
            var prepared = new Run();
            yield return Trace("closed-knee", 284, prepared, retainPreparation: true, closedPrefix: true, throughKnee: true);
            AssertClearance(prepared, "knee-region end");
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 composed prepared-ascent qualification probe")]
        public IEnumerator GAM70_PREPARED_COMPOSED_ATTEMPT()
        {
            var prepared = new Run();
            yield return Trace("closed-composed", 284, prepared, retainPreparation: true, closedPrefix: true,
                throughKnee: true, complete: true, lastTick: 2100);
            Assert.That(prepared.Pull, Is.EqualTo(301ul));
            Assert.That(prepared.Stop, Is.GreaterThan(538ul));
            Assert.That(prepared.StopReason, Is.Not.EqualTo("comparison bound"));
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 closed-reference COM placement probe at lockout")]
        public IEnumerator GAM70_PREPARED_CENTERED_LOCKOUT_PROBE()
        {
            var prepared = new Run();
            yield return Trace("closed-centered", 284, prepared, retainPreparation: true, closedPrefix: true,
                throughKnee: true, complete: true, centered: true, lastTick: 2100);
            Assert.That(prepared.Pull, Is.EqualTo(301ul));
            Assert.That(prepared.Stop, Is.GreaterThan(538ul));
            Assert.That(prepared.StopReason, Is.Not.EqualTo("comparison bound"));
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 rejoin the stock lockout reference after measured knee passage")]
        public IEnumerator GAM70_PREPARED_STOCK_REJOIN()
        {
            foreach (float join in new[] { 0.80f, 0.94f })
            {
                var prepared = new Run();
                yield return Trace($"stock-rejoin-{join:F2}", 284, prepared, retainPreparation: true, closedPrefix: true,
                    throughKnee: true, complete: true, rejoinEnd: join, lastTick: 2100);
                Assert.That(prepared.Good && prepared.Released, Is.True, prepared.RefereeSummary);
                Assert.That(prepared.MinGap, Is.GreaterThan(0f));
                Assert.That(prepared.MinSupport, Is.GreaterThan(0.04f));
            }
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 signed shoulder reference at the recovered lockout")]
        public IEnumerator GAM70_REJOIN_LATE_SHOULDER_AB()
        {
            var positive = new Run();
            yield return Trace("rejoin-shoulder-plus", 284, positive, retainPreparation: true, closedPrefix: true,
                throughKnee: true, complete: true, rejoinEnd: 0.80f, lateShoulderDeg: 0.5f, lastTick: 2100);
            var negative = new Run();
            yield return Trace("rejoin-shoulder-minus", 284, negative, retainPreparation: true, closedPrefix: true,
                throughKnee: true, complete: true, rejoinEnd: 0.80f, lateShoulderDeg: -0.5f, lastTick: 2100);
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 small shoulder trim after the stock rejoin settles")]
        public IEnumerator GAM70_REJOIN_SETTLED_SHOULDER()
        {
            foreach (float amplitude in new[] { 0.25f, 0.50f })
            {
                var prepared = new Run();
                yield return Trace($"rejoin-settled-{amplitude:F2}", 284, prepared, retainPreparation: true, closedPrefix: true,
                    throughKnee: true, complete: true, rejoinEnd: 0.80f, lateShoulderDeg: amplitude,
                    settledShoulder: true, lastTick: 2100);
            }
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 prepared rejoin vs the stock judged runtime")]
        public IEnumerator GAM70_PREPARED_REJOIN_RUNTIME_QUALIFICATION()
        {
            var stock = new Run();
            yield return Trace("rejoin-stock-control", 301, stock, complete: true, lastTick: 2100);
            Assert.That(stock.Good && stock.Released, Is.True, stock.RefereeSummary);
            var candidate = new Run();
            yield return Trace("rejoin-runtime-301", 284, candidate, complete: true, runtimeRejoin: true, lastTick: 2100);
            Assert.That(candidate.Pull, Is.EqualTo(301ul));
            Assert.That(candidate.Good && candidate.Released, Is.True, candidate.RefereeSummary);
            Assert.That(candidate.FirstPenetration, Is.Zero);
            Assert.That(candidate.MinAirborneFootN, Is.GreaterThan(300f));
            Assert.That(candidate.Sweep.MinBound, Is.GreaterThan(0f));
            Assert.That(candidate.MeanLockoutGap, Is.LessThan(stock.MeanLockoutGap - 0.001));
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 prepared rejoin on the existing onset cases")]
        public IEnumerator GAM70_PREPARED_REJOIN_ONSET_SWEEP()
        {
            foreach (int onset in new[] { 301, 302, 305, 320, 350 })
            {
                var candidate = new Run();
                yield return Trace($"rejoin-onset-{onset}", 284, candidate, complete: true, runtimeRejoin: true,
                    earliestPull: onset, lastTick: 2100);
                Assert.That(candidate.Pull, Is.EqualTo((ulong)onset));
                Assert.That(candidate.Stop, Is.GreaterThan(candidate.ConfirmedSeparation));
            }
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 vary Drive onset with the natural preparation readiness gate")]
        public IEnumerator GAM70_PREPARED_REJOIN_DRIVE_LEAD_SWEEP()
        {
            foreach (int target in new[] { 302, 305, 320, 350 })
            {
                int drive = target - 17;
                var candidate = new Run();
                yield return Trace($"rejoin-drive-{drive}", drive, candidate, complete: true, runtimeRejoin: true, lastTick: 2100);
                Assert.That(candidate.Pull, Is.GreaterThan((ulong)drive));
                Assert.That(candidate.Stop, Is.GreaterThan(candidate.ConfirmedSeparation));
            }
        }

        [UnityTest, Timeout(600000), Explicit("GAM-70 measured post-knee approach into the suspended endpoint")]
        public IEnumerator GAM70_POST_KNEE_TO_CLOSE_LOCKOUT()
        {
            var candidate = new Run();
            yield return Trace("post-knee-to-close", 284, candidate, retainPreparation: true, closedPrefix: true,
                throughKnee: true, postKneeProbe: true, lastTick: 1150);
            Assert.That(candidate.ContinuationTick, Is.EqualTo(538ul));
            Assert.That(candidate.PrefixSweptGap, Is.GreaterThan(0f));
            Assert.That(candidate.Stop, Is.GreaterThan(candidate.ContinuationTick));
        }

        [UnityTest, Timeout(600000), Explicit("GAM-70 post-knee approach using the planned suspended preload")]
        public IEnumerator GAM70_POST_KNEE_SUSPENDED_PRELOAD()
        {
            var candidate = new Run();
            yield return Trace("post-knee-suspended-preload", 284, candidate, retainPreparation: true, closedPrefix: true,
                throughKnee: true, postKneeProbe: true, suspendedPreload: true, lastTick: 1150);
            Assert.That(candidate.ContinuationTick, Is.EqualTo(538ul));
            Assert.That(candidate.PrefixSweptGap, Is.GreaterThan(0f));
            Assert.That(candidate.StopReason, Is.EqualTo("motionless lockout"), candidate.RefereeSummary);
            Assert.That(candidate.Down, Is.EqualTo(candidate.Stop));
            Assert.That(candidate.FirstPenetration, Is.Zero);
            Assert.That(candidate.Sweep.MinBound, Is.GreaterThan(0f));
            Assert.That(candidate.MinAirborneFootN, Is.GreaterThan(300f));
            Assert.That(candidate.MinSupport, Is.GreaterThan(0.04f));
            Assert.That(candidate.MeanLockoutGap, Is.InRange(0d, 0.12d));
        }

        [UnityTest, Timeout(600000), Explicit("GAM-70 close-bar attempt through controlled return")]
        public IEnumerator GAM70_RUNTIME_CLOSE_BAR_ATTEMPT()
        {
            var candidate = new Run();
            yield return Trace("runtime-close-bar", 284, candidate, throughKnee: true, complete: true, runtimeClose: true, lastTick: 2100);
            Assert.That(candidate.Pull, Is.EqualTo(301ul));
            Assert.That(candidate.ContinuationTick, Is.EqualTo(538ul));
            Assert.That(candidate.Down, Is.EqualTo(762ul));
            Assert.That(candidate.Good && candidate.Released, Is.True, candidate.RefereeSummary);
            Assert.That(candidate.FirstPenetration, Is.Zero);
            Assert.That(candidate.Sweep.MinBound, Is.GreaterThan(0f));
            Assert.That(candidate.MeanLockoutGap, Is.InRange(0d, 0.12d));
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 close-bar runtime onset sensitivity")]
        public IEnumerator GAM70_RUNTIME_CLOSE_BAR_ONSETS()
        {
            var summary = new StringBuilder();
            foreach (int onset in new[] { 302, 305, 320, 350 })
            {
                var candidate = new Run();
                yield return Trace($"runtime-close-onset-{onset}", 284, candidate, throughKnee: true, complete: true,
                    earliestPull: onset, runtimeClose: true, lastTick: 2100);
                summary.AppendLine($"{onset}: pull {candidate.Pull}, DOWN {candidate.Down}, stop {candidate.Stop} ({candidate.StopReason}), " +
                    $"release {candidate.Released}, nearer thigh {candidate.MeanLockoutGap * 1000d:F2} mm. {candidate.RefereeSummary}");
                F.WriteEvidence("runtime-close-onsets.txt", summary.ToString());
                Assert.That(candidate.Pull, Is.EqualTo((ulong)onset));
            }
        }

        [UnityTest, Timeout(900000), Explicit("GAM-70 offline generation of the close-bar reference artifact")]
        public IEnumerator GAM70_GENERATE_CLOSE_BAR_REFERENCE()
        {
            var generated = new Run();
            DeadliftCloseBarReference artifact = null;
            yield return Trace("generate-close-bar", 284, generated, throughKnee: true, complete: true, runtimeClose: true, lastTick: 2100,
                atContinuation: rt =>
                {
                    artifact = rt.RecordReference("GAM70_GENERATE_CLOSE_BAR_REFERENCE: B2, Drive held from tick 284, measured PULL 301");
                    // The stored stock path must rebuild the solved one exactly.
                    var restored = new DeadliftLiftPath(_world.Rig, rt.Measurements, rt.Model, rt.Bar.mass) { RollPlanLimitDeg = rt.Path.RollPlanLimitDeg };
                    restored.Restore(DeadliftCloseBarReference.FromJson(artifact.ToJson()).StockPath);
                    Assert.That(restored.Keys.Count, Is.EqualTo(rt.Path.Keys.Count));
                    for (int i = 0; i < restored.Keys.Count; i++)
                        foreach (var q in rt.Path.Keys[i].Logical)
                            Assert.That(restored.Keys[i].Logical[q.Key].Equals(q.Value) && restored.Keys[i].BarRiseM.Equals(rt.Path.Keys[i].BarRiseM) &&
                                restored.Keys[i].SystemComZ.Equals(rt.Path.Keys[i].SystemComZ), Is.True, $"stock key {i} {q.Key}");
                    Assert.That(restored.KneePassageRiseM, Is.EqualTo(rt.Path.KneePassageRiseM));
                    Assert.That(restored.JointPathLengthDeg, Is.EqualTo(rt.Path.JointPathLengthDeg));
                });
            Assert.That(generated.Pull, Is.EqualTo(301ul));
            Assert.That(generated.ContinuationTick, Is.EqualTo(538ul));
            Assert.That(generated.Down, Is.EqualTo(762ul));
            Assert.That(generated.Good && generated.Released, Is.True, generated.RefereeSummary);
            Assert.That(artifact, Is.Not.Null);

            string json = artifact.ToJson();
            DeadliftCloseBarReference reread = DeadliftCloseBarReference.FromJson(json);
            Assert.That(reread.ToJson(), Is.EqualTo(json), "bit-exact round trip");
            Assert.That(reread.Prefix.State.IdenticalTo(artifact.Prefix.State) && reread.PostKnee.State.IdenticalTo(artifact.PostKnee.State), Is.True);
            string path = Path.Combine(Directory.GetCurrentDirectory(), DeadliftCloseBarReference.AssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
            Debug.Log($"GAM-70 close-bar reference written to {path}: fingerprint {artifact.Fingerprint}, ascent {artifact.Prefix.Keys.Count} keys " +
                $"(min gap {artifact.Prefix.MinGapM * 1000f:F2} mm, swept {artifact.Prefix.SweptGapM * 1000f:F2} mm), post-knee {artifact.PostKnee.Keys.Count} keys " +
                $"(swept {artifact.PostKnee.SweptGapM * 1000f:F2} mm)");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator GAM70_CACHED_CLOSE_BAR_ATTEMPT_RUNS_WITHOUT_SUSPENSION()
        {
            var cached = new Run();
            yield return Trace("cached-close-bar", 284, cached, throughKnee: true, complete: true, runtimeClose: true, lastTick: 2100, useCache: true);
            Debug.Log($"Cached ascent admission: {cached.AscentAdmission?.Describe()}\nCached post-knee admission: {cached.PostKneeAdmission?.Describe()}\n" +
                $"Max attempt post-physics work {cached.MaxTickMs:F2} ms at tick {cached.MaxTickMsTick}");
            Assert.That(cached.Suspended, Is.False, "the simulation was suspended");
            Assert.That(cached.AscentAdmission.Outcome, Is.EqualTo(DeadliftReferenceAdmission.Outcome.Exact), cached.AscentAdmission.Describe());
            Assert.That(cached.PostKneeAdmission.Outcome, Is.EqualTo(DeadliftReferenceAdmission.Outcome.Exact), cached.PostKneeAdmission.Describe());
            // The exact receipts of the solved composition (GAM70_RUNTIME_CLOSE_BAR_ATTEMPT).
            Assert.That(cached.Pull, Is.EqualTo(301ul));
            Assert.That(cached.ContinuationTick, Is.EqualTo(538ul));
            Assert.That(cached.Down, Is.EqualTo(762ul));
            Assert.That(cached.Good && cached.Released, Is.True, cached.RefereeSummary);
            Assert.That(cached.FirstPenetration, Is.Zero);
            Assert.That(cached.Sweep.MinBound, Is.GreaterThan(0f));
            Assert.That(cached.MeanLockoutGap, Is.InRange(0d, 0.12d));
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 cached close-bar admission across PULL onsets")]
        public IEnumerator GAM70_CACHED_CLOSE_BAR_ONSETS()
        {
            var summary = new StringBuilder();
            foreach (int onset in OnsetsFromEnvironment(new[] { 302, 305, 320, 350 }))
            {
                var candidate = new Run();
                yield return Trace($"cached-close-onset-{onset}", 284, candidate, throughKnee: true, complete: true,
                    earliestPull: onset, runtimeClose: true, lastTick: 2100, useCache: true);
                summary.AppendLine($"{onset}: pull {candidate.Pull}, separation {candidate.ConfirmedSeparation}, knee {candidate.ContinuationTick}, DOWN {candidate.Down}, " +
                    $"stop {candidate.Stop} ({candidate.StopReason}), release {candidate.Released}, nearer thigh {candidate.MeanLockoutGap * 1000d:F2} mm, " +
                    $"suspended {candidate.Suspended}, max tick {candidate.MaxTickMs:F2} ms @ {candidate.MaxTickMsTick}. {candidate.RefereeSummary}\n" +
                    $"   ascent: {candidate.AscentAdmission?.Describe()}\n   post-knee: {candidate.PostKneeAdmission?.Describe()}\n   rejection: {candidate.Rejection}");
                F.WriteEvidence("cached-close-onsets.txt", summary.ToString());
                Assert.That(candidate.Pull, Is.EqualTo((ulong)onset));
                Assert.That(candidate.Suspended, Is.False);
            }
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator GAM70_REFERENCE_CACHE_REJECTS_AN_INCOMPATIBLE_MODEL_AND_STATE()
        {
            DeadliftCloseBarReference artifact = DeadliftCloseBarReference.LoadResource(out string failure);
            Assert.That(artifact, Is.Not.Null, failure);

            // A different declared model (fingerprint) is refused before the attempt is armed.
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            DeadliftCloseBarReference foreign = DeadliftCloseBarReference.FromJson(artifact.ToJson());
            foreign.Fingerprint = new string('0', 64);
            var refused = new DeadliftAttemptRuntime(_world.Foundation, _world.Rig, _world.Bar)
            {
                UpperChainPreparation = true, PreparedCloseLockout = true, UseReferenceCache = true, ReferenceArtifact = foreign
            };
            var miss = Assert.Throws<System.InvalidOperationException>(refused.Initialize);
            StringAssert.Contains("fingerprint", miss.Message);
            yield return _world.Teardown();
            _world = null;

            // A self-consistent family (it passes its contract when the attempt is prepared) generated for
            // a plant turned 120 deg about the vertical: the measured plant at separation cannot be corrected
            // onto it, so it is rejected there. The bar is returned without Down and no lift is awarded.
            DeadliftCloseBarReference displaced = DeadliftCloseBarReference.FromJson(artifact.ToJson());
            Quaternion turn = Quaternion.AngleAxis(120f, Vector3.up);
            Pose Turn(Pose p) => new Pose(turn * p.position, turn * p.rotation);
            var state = displaced.Prefix.State;
            state.Root = Turn(state.Root);
            state.Bar = Turn(state.Bar);
            state.Feet = state.Feet.Select(Turn).ToArray();
            for (int i = 0; i < displaced.Prefix.Keys.Count; i++)
            {
                var key = displaced.Prefix.Keys[i];
                key.Root = Turn(key.Root);
                key.Bar = Turn(key.Bar);
                displaced.Prefix.Keys[i] = key;
            }
            var rejected = new Run();
            yield return Trace("cached-close-rejected", 284, rejected, throughKnee: true, complete: true, runtimeClose: true,
                lastTick: 1200, useCache: true, artifact: displaced);
            Debug.Log($"Displaced-family admission: {rejected.AscentAdmission?.Describe()} | {rejected.Rejection} | {rejected.RefereeSummary}");
            Assert.That(rejected.AscentAdmission.Outcome, Is.EqualTo(DeadliftReferenceAdmission.Outcome.Rejected));
            Assert.That(rejected.Rejection, Does.StartWith("ascent reference"));
            Assert.That(rejected.RejectionTick, Is.EqualTo(rejected.ConfirmedSeparation));
            Assert.That(rejected.Suspended, Is.False);
            Assert.That(rejected.Down, Is.Zero);
            Assert.That(rejected.Good, Is.False);
        }

        /// <summary>
        /// Realistic input timing under the measured readiness contract: Drive first given at different
        /// ticks after the grip is set; the internal preparation decides when the pull starts.
        /// </summary>
        [UnityTest, Timeout(3600000), Explicit("GAM-70 cached close-bar attempts across Drive onsets after measured readiness")]
        public IEnumerator GAM70_CACHED_CLOSE_BAR_DRIVE_ONSETS()
        {
            var summary = new StringBuilder();
            foreach (int drive in OnsetsFromEnvironment(new[] { 1, 228, 260, 284, 300, 320, 350, 400 }))
            {
                var candidate = new Run();
                yield return Trace($"cached-close-drive-{drive}", drive, candidate, throughKnee: true, complete: true,
                    runtimeClose: true, lastTick: 2400, useCache: true);
                summary.AppendLine($"Drive {drive}: pull {candidate.Pull}, separation {candidate.ConfirmedSeparation}, knee {candidate.ContinuationTick}, DOWN {candidate.Down}, " +
                    $"stop {candidate.Stop} ({candidate.StopReason}), release {candidate.Released}, nearer thigh {candidate.MeanLockoutGap * 1000d:F2} mm, " +
                    $"min airborne foot {candidate.MinAirborneFootN:F1} N, suspended {candidate.Suspended}, max tick {candidate.MaxTickMs:F2} ms. {candidate.RefereeSummary}\n" +
                    $"   ascent: {candidate.AscentAdmission?.Describe()}\n   post-knee: {candidate.PostKneeAdmission?.Describe()}");
                F.WriteEvidence("cached-close-drive-onsets.txt", summary.ToString());
                Assert.That(candidate.Suspended, Is.False);
            }
        }

        private static IEnumerable<int> OnsetsFromEnvironment(int[] fallback)
        {
            string value = System.Environment.GetEnvironmentVariable("GAM70_ONSETS");
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Split(',').Select(v => int.Parse(v.Trim(), CultureInfo.InvariantCulture));
        }

        private static void AssertClearance(Run prepared, string endpoint)
        {
            Assert.That(prepared.Pull, Is.EqualTo(301ul));
            Assert.That(prepared.StopReason, Is.EqualTo(endpoint));
            Assert.That(prepared.FirstPenetration, Is.Zero);
            Assert.That(prepared.MinGap, Is.GreaterThan(0f));
            Assert.That(prepared.MinSupport, Is.GreaterThan(0.04f));
            Assert.That(prepared.Sweep.Samples, Is.EqualTo((prepared.Stop - prepared.ConfirmedSeparation) * SimulationConstants.PhysicsSubstepsPerTick));
            Assert.That(prepared.Sweep.MinBound, Is.GreaterThan(0f));
            Debug.Log($"Prepared realized substeps: {prepared.Sweep.Samples}, min gap {prepared.Sweep.MinGap * 1000f:F2} mm, swept bound {prepared.Sweep.MinBound * 1000f:F2} mm");
        }

        private IEnumerator Trace(string arm, int driveTick, Run result, bool retainPreparation = false, bool closedPrefix = false, bool throughKnee = false, bool complete = false, bool centered = false, float rejoinEnd = 0f, float lateShoulderDeg = 0f, bool settledShoulder = false, bool runtimeRejoin = false, int earliestPull = 0, int lastTick = 800, bool postKneeProbe = false, bool suspendedPreload = false, bool runtimeClose = false, bool useCache = false, DeadliftCloseBarReference artifact = null, System.Action<DeadliftAttemptRuntime> atContinuation = null)
        {
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            Assert.That(DeadliftAttemptRuntime.BarProximityPath, Is.False);
            var rt = new DeadliftAttemptRuntime(_world.Foundation, _world.Rig, _world.Bar)
            {
                UpperChainPreparation = driveTick < 301,
                PreparedClearanceRejoin = runtimeRejoin, PreparedCloseLockout = runtimeClose,
                UseReferenceCache = useCache, ReferenceArtifact = artifact
            };
            rt.Initialize();
            rt.Pull.EarliestPullTick = (ulong)earliestPull;
            rt.Pull.IntentOverride01 = 0f;
            Vector3 bar0 = rt.Bar.position;
            var feet = Sides.Select(side => (BoxCollider)_world.Rig.Segments[side + "_foot"].Collider).ToArray();
            var sensors = feet.Select(foot => foot.GetComponent<DeadliftFootPressureSensor>()).ToArray();
            var joints = _world.Rig.PoweredController.Joints;
            var rows = new StringBuilder("tick,phase,progress,pullApplied,referenceOwner,handReferenceN,bar,barQ,barV,barW,planBar,pelvis,pelvisQ,pelvisV,pelvisW,planPelvis,planPelvisQ,plateL,plateR,legN,gapL,gapR,planGapL,planGapR,thighL,thighR,planThighL,planThighR,capture,margin,polygon");
            foreach (string side in Sides)
            {
                foreach (string id in Landmarks)
                    rows.Append($",{side}_{id},{side}_{id}_v,{side}_{id}_plan");
                rows.Append($",{side}_handAnchor,{side}_handAnchorV,{side}_barAnchor,{side}_planHandAnchor,{side}_planBarAnchor,{side}_forceOnBar,{side}_footN,{side}_cop,{side}_loadedAxisMin,{side}_loadedAxisMax");
            }
            rows.AppendLine();
            var channels = new StringBuilder("tick,joint,planQ,requestedQ,appliedQ,actualQ,error,targetW,actualW,xDemandNm,xMaxNm,xFraction,xSpring,xDamper,yzDemandNm,yzMaxNm,yzFraction,yzSpring,yzDamper,zDemandNm,zMaxNm,zFraction,zSpring,zDamper,zRequestedDeg,zAppliedDeg,zActualDeg,zActualW,zRateLimited,limitProximity,zLimitProximity,solverTorque,staticReferenceNm\n");
            var leftShoulder = _world.Rig.PoweredController.GetJoint("left_upper_arm");
            var rightShoulder = _world.Rig.PoweredController.GetJoint("right_upper_arm");
            DeadliftPreparedAscentPlan closed = null, continuation = null;
            DeadliftFootCopSensor substepContact = null;
            float startBarY = 0f, maxProgress = 0f;
            Quaternion leftStart = Quaternion.identity, rightStart = Quaternion.identity;
            Vector3 leftLateAxis = Vector3.zero, rightLateAxis = Vector3.zero;
            ulong settledShoulderStart = 0;
            Vector3 ExtensionAxis(PoweredJointController.PoweredJointRuntime shoulder, string side)
            {
                ConfigurableJoint joint = shoulder.Joint;
                Vector3 arm = _world.Rig.Segments[side + "_forearm"].Body.worldCenterOfMass - joint.transform.TransformPoint(joint.anchor);
                return Quaternion.Inverse(joint.transform.rotation * shoulder.JointSpace) * Vector3.Cross(arm, Vector3.back).normalized;
            }
            void StartSweep()
            {
                result.Sweep = new DeadliftRealizedClearanceSweep(rt.Bar, _world.Rig);
                substepContact = feet[0].gameObject.AddComponent<DeadliftFootCopSensor>();
                substepContact.Ground = _world.Rig.PlatformCollider;
                substepContact.PlatformContactObserved = result.Sweep.Read;
                startBarY = rt.Bar.position.y;
            }

            for (int tick = 1; tick <= lastTick; tick++)
            {
                if (tick == driveTick)
                    rt.Pull.IntentOverride01 = 1f;
                Quaternion leftOffset = rt.Hold.PreparationOffsetFor(leftShoulder.Id);
                Quaternion rightOffset = rt.Hold.PreparationOffsetFor(rightShoulder.Id);
                _world.Step(1);
                substepContact?.CompleteTick();
                result.Suspended |= _world.Foundation.Runtime.TicksSuspended;
                if (rt.ReferenceRejectionTick != 0 && result.RejectionTick == 0)
                {
                    result.Rejection = rt.ReferenceRejection;
                    result.RejectionTick = rt.ReferenceRejectionTick;
                }
                if ((runtimeRejoin || runtimeClose) && closed == null && rt.PreparedAscentPlan != null)
                {
                    closed = rt.PreparedAscentPlan;
                    StartSweep();
                }
                if (retainPreparation && rt.Tick == rt.Pull.SeparationTick)
                {
                    // Continue the exact two factors the production handoff just dropped.
                    rt.Hold.IdentificationOffsets.Add(leftShoulder.Id, leftOffset);
                    rt.Hold.IdentificationOffsets.Add(rightShoulder.Id, rightOffset);
                    if (closedPrefix)
                    {
                        closed = new DeadliftPreparedAscentPlan(rt, _world.Rig, throughKnee, complete, centered);
                        var plan = closed.MeasureInterpolation();
                        Debug.Log($"Prepared closed plan: min gap {plan.MinGap * 1000f:F2} mm, swept bound {plan.SweptGap * 1000f:F2} mm, max closure {plan.PositionError * 1000f:F3} mm / {plan.RotationError:F3} deg");
                        Assert.That(plan.MinGap, Is.GreaterThan(0.020f));
                        Assert.That(plan.SweptGap, Is.GreaterThan(0f));
                        Assert.That(plan.PositionError, Is.LessThan(0.001f));
                        Assert.That(plan.RotationError, Is.LessThan(0.3f));
                        rt.Hold.AscentReferenceOverride = rejoinEnd <= 0f ? closed.Target : (joint, s) => closed.RejoinTarget(joint, s, rejoinEnd);
                        StartSweep();
                        leftStart = leftOffset;
                        rightStart = rightOffset;
                    }
                }
                if (closed != null && !runtimeRejoin && !runtimeClose)
                {
                    maxProgress = Mathf.Max(maxProgress, rt.Pull.LiftProgress01);
                    float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(maxProgress / 0.07f));
                    rt.Hold.IdentificationOffsets[leftShoulder.Id] = Quaternion.Slerp(leftStart, Quaternion.identity, blend);
                    rt.Hold.IdentificationOffsets[rightShoulder.Id] = Quaternion.Slerp(rightStart, Quaternion.identity, blend);
                    if (settledShoulder && settledShoulderStart == 0 && rt.Pull.Phase == DeadliftPullPhase.Lockout &&
                        rt.Bar.linearVelocity.sqrMagnitude < 0.045f * 0.045f)
                        settledShoulderStart = rt.Tick;
                    bool lateActive = settledShoulder ? settledShoulderStart != 0 : maxProgress > 0.82f;
                    if (lateShoulderDeg != 0f && lateActive)
                    {
                        if (leftLateAxis == Vector3.zero)
                        {
                            leftLateAxis = ExtensionAxis(leftShoulder, "left");
                            rightLateAxis = ExtensionAxis(rightShoulder, "right");
                        }
                        float late = lateShoulderDeg * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 0.98f, rt.Pull.LiftProgress01));
                        if (settledShoulder)
                            late *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rt.Tick - settledShoulderStart) / 35f));
                        if (late != 0f)
                        {
                            rt.Hold.IdentificationOffsets[leftShoulder.Id] *= Quaternion.AngleAxis(late, leftLateAxis);
                            rt.Hold.IdentificationOffsets[rightShoulder.Id] *= Quaternion.AngleAxis(late, rightLateAxis);
                        }
                    }
                }
                if (postKneeProbe && closed != null && continuation == null &&
                    rt.Bar.position.y - bar0.y >= rt.Path.KneePassageRiseM)
                {
                    result.ContinuationTick = rt.Tick;
                    result.PrefixSweptGap = result.Sweep.MinBound;
                    var endPath = new DeadliftLiftPath(_world.Rig, rt.Measurements, rt.Model, rt.Bar.mass) { BarProximity = true };
                    var endpoint = endPath.SolveEndpoint();
                    Quaternion barOrientation = Quaternion.AngleAxis(180f / PhysicalBarbell.DefaultPlateColliderSides, Vector3.right);
                    continuation = DeadliftPreparedAscentPlan.ContinueToLockout(rt, _world.Rig, endpoint, barOrientation);
                    var constraints = continuation.MeasureInterpolation();
                    Debug.Log($"Post-knee plan from tick {rt.Tick}: minimum {constraints.MinGap * 1000f:F2} mm, swept {constraints.SweptGap * 1000f:F2} mm, closure {constraints.PositionError * 1000f:F3} mm / {constraints.RotationError:F3} deg");
                    Assert.That(constraints.SweptGap, Is.GreaterThan(0f));
                    Assert.That(constraints.PositionError, Is.LessThan(0.001f));
                    Assert.That(constraints.RotationError, Is.LessThan(0.3f));
                    rt.Hold.AscentReferenceOverride = continuation.Target;
                    rt.Regulator.ContactConsistentControl = true;
                    rt.Regulator.ComPositionAnchor = true;
                    var centers = feet.Select(box => box.transform.TransformPoint(box.center - 0.5f * box.size.y * Vector3.up)).ToArray();
                    float entryZ = continuation.SystemComZ(rt.Pull.LiftProgress01);
                    float entryProgress = rt.Pull.LiftProgress01;
                    (float Z, float Vz) ComReference()
                    {
                        float s0 = rt.Pull.LiftProgress01;
                        float s1 = Mathf.Min(1f, s0 + 0.001f);
                        float z0 = continuation.SystemComZ(s0);
                        float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s0 - entryProgress) / 0.035f));
                        float velocity = s1 > s0 ? (continuation.SystemComZ(s1) - z0) / (s1 - s0) * rt.Pull.LiftRate01PerSecond : 0f;
                        return (Mathf.Lerp(entryZ, z0, blend), velocity);
                    }
                    rt.Regulator.PlannedCom = ComReference;
                    var previousTarget = rt.Regulator.FootCopTarget;
                    rt.Regulator.FootCopTarget = id =>
                    {
                        int i = id == "left_foot" ? 0 : 1;
                        float s0 = rt.Pull.LiftProgress01;
                        float z = continuation.SystemComZ(s0);
                        Vector3 axis = sensors[i].Axis;
                        Vector3 target = centers[i] + axis * ((z - centers[i].z) / axis.z);
                        float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s0 - entryProgress) / 0.035f));
                        return Vector3.Lerp(previousTarget(id), target, blend);
                    };
                    if (suspendedPreload)
                    {
                        rt.Hold.SuspendedPlanReference = progress =>
                        {
                            var sample = continuation.At(progress);
                            Vector3 Anchor(string id, BenchGrip grip)
                            {
                                Pose pose = sample.Poses[id];
                                return pose.position + pose.rotation * grip.HandAnchorLocal;
                            }
                            var statics = DeadliftContactStatics.SolveSuspended(_world.Rig, rt.Bar, sample.Bar, sample.Poses,
                                Anchor("left_hand", rt.LeftGrip), Anchor("right_hand", rt.RightGrip));
                            Assert.That(statics.Feasible, Is.True, statics.Describe());
                            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - entryProgress) / 0.035f));
                            return (statics, blend);
                        };
                    }
                    var ankles = joints.Where(j => j.Recipe.Family == "ankle").ToArray();
                    rt.Pull.ReferenceDemand = () => Mathf.Max(rt.Hold.HandLoadReference.MaxDemandFraction,
                        ankles.Max(j => Mathf.Max(j.PostPhysicsDiagnostic.TwistDriveDemandFraction, j.PostPhysicsDiagnostic.SecondaryDriveDemandFraction)));
                }
                if (runtimeClose && continuation == null && rt.PostKneeAscentPlan != null)
                {
                    continuation = rt.PostKneeAscentPlan;
                    result.ContinuationTick = rt.Tick;
                    result.PrefixSweptGap = result.Sweep.MinBound;
                    atContinuation?.Invoke(rt);
                }
                if (tick < Mathf.Min(280, driveTick))
                    continue;

                rt.Monitor.Update(); // Read current capture point, then use this tick's foot impulses for the hull.
                var pressure = sensors.Select(sensor => sensor.PendingContact).ToArray();
                var load = pressure.Select(foot => (float)(foot.NormalImpulse / SimulationConstants.FixedDeltaTimeSeconds)).ToArray();
                var points = new List<Vector2>(8);
                for (int side = 0; side < feet.Length; side++)
                {
                    if (load[side] <= rt.Monitor.LoadedFootN)
                        continue;
                    var box = feet[side];
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 corner = box.transform.TransformPoint(box.center + Vector3.Scale(box.size,
                            new Vector3((i & 1) == 0 ? -0.5f : 0.5f, -0.5f, (i & 2) == 0 ? -0.5f : 0.5f)));
                        points.Add(new Vector2(corner.x, corner.z));
                    }
                }
                List<Vector2> hull = DeadliftSupportGeometry.Hull(points);
                Vector2 capture = rt.Monitor.CapturePoint;
                float margin = DeadliftSupportGeometry.SignedDistance(hull, capture);

                float s = rt.Pull.LiftProgress01;
                DeadliftBarLegGeometry.Realized(_world.Rig, rt.Bar, out var gap);
                if (continuation != null)
                {
                    // GAM-70 mechanism comparison: peak drive demands and contact channels over the
                    // continuation window, and the settled arrival's task-space state.
                    foreach (var j in new[] { "left_foot", "right_foot" })
                    {
                        var d = _world.Rig.PoweredController.GetJoint(j).PostPhysicsDiagnostic;
                        result.MaxAnkleX = Mathf.Max(result.MaxAnkleX, d.TwistDriveDemandFraction);
                        result.MaxAnkleZ = Mathf.Max(result.MaxAnkleZ, d.SecondaryDriveDemandFraction);
                    }
                    result.MaxAbdomen = Mathf.Max(result.MaxAbdomen, _world.Rig.PoweredController.GetJoint("abdomen").PostPhysicsDiagnostic.TwistDriveDemandFraction);
                    result.MaxThorax = Mathf.Max(result.MaxThorax, _world.Rig.PoweredController.GetJoint("thorax").PostPhysicsDiagnostic.TwistDriveDemandFraction);
                    foreach (var j in new[] { "left_upper_arm", "right_upper_arm" })
                        result.MaxShoulder = Mathf.Max(result.MaxShoulder, _world.Rig.PoweredController.GetJoint(j).PostPhysicsDiagnostic.ModeledDemand);
                    result.MaxThighAssistN = Mathf.Max(result.MaxThighAssistN, rt.ThighAssistN);
                    result.MaxShinN = Mathf.Max(result.MaxShinN, rt.ShinContactN);
                    if (s >= 0.99f)
                    {
                        result.ArrivalBarZ = rt.Bar.position.z;
                        result.ArrivalBarVz = rt.Bar.linearVelocity.z;
                        result.ArrivalHipZ = rt.Pelvis.position.z;
                        result.ArrivalComZ = rt.Monitor.SystemCom.z;
                        result.ArrivalComVz = rt.Monitor.SystemVelocity.z;
                        result.ArrivalGap = gap.Thigh;
                        result.ArrivalDeltoidM = rt.Observer.DeltoidBehindBarM;
                    }
                }
                rt.Path.KeySpan(s, out int a, out int b, out float t);
                Pose rootA = rt.Measurements.PelvisPoseOf(rt.Path.Keys[a].X);
                Pose rootB = rt.Measurements.PelvisPoseOf(rt.Path.Keys[b].X);
                var root = new Pose(Vector3.Lerp(rootA.position, rootB.position, t), Quaternion.Slerp(rootA.rotation, rootB.rotation, t));
                var logical = rt.Model.SetupLogical.Keys.ToDictionary(id => id, id => rt.Path.Target(id, s));
                var kin = rt.Measurements.Kinematics;
                var poses = kin.Solve(root, logical);
                Vector3 planBar = new Vector3(bar0.x, bar0.y + rt.Path.BarRiseAt(s), Mathf.Lerp(rt.Path.Keys[a].BarZ, rt.Path.Keys[b].BarZ, t));
                Quaternion planBarRotation = Quaternion.AngleAxis(180f / PhysicalBarbell.DefaultPlateColliderSides, Vector3.right);
                var planGap = DeadliftBarLegGeometry.Planned(rt.Measurements, poses, planBar);
                if (closed != null && (!runtimeRejoin || s <= closed.LateStart))
                {
                    DeadliftPreparedAscentPlan activePlan = continuation != null && (s >= closed.LateStart || !runtimeClose) ? continuation : closed;
                    DeadliftPreparedAscentPlan.Sample sample = activePlan.At(s);
                    root = sample.Root;
                    logical = sample.Logical;
                    poses = sample.Poses;
                    planBar = sample.Bar.position;
                    planBarRotation = sample.Bar.rotation;
                    planGap = activePlan.Gaps(sample);
                }
                if (result.Pull == 0 && rt.Pull.Phase == DeadliftPullPhase.Pulling)
                    result.Pull = rt.Tick;
                if (result.Pull > 0 && result.PhysicalSeparation == 0 && rt.PlateLeftN == 0f && rt.PlateRightN == 0f && rt.Bar.linearVelocity.y > 0.005f)
                    result.PhysicalSeparation = rt.Tick;
                result.ConfirmedSeparation = rt.Pull.SeparationTick;
                if (result.FirstPenetration == 0 && gap.Leg < 0f)
                    result.FirstPenetration = rt.Tick;

                Row(rows, rt.Tick, rt.Pull.Phase, s, rt.Pull.Applied01, rt.Hold.ReferenceOwner, rt.Hold.HandLoadReference.HandPullN,
                    V(rt.Bar.position), Q(rt.Bar.rotation), V(rt.Bar.linearVelocity), V(rt.Bar.angularVelocity), V(planBar),
                    V(rt.Pelvis.position), Q(rt.Pelvis.rotation), V(rt.Pelvis.linearVelocity), V(rt.Pelvis.angularVelocity), V(root.position), Q(root.rotation),
                    rt.PlateLeftN, rt.PlateRightN, rt.BarOnLegN, gap.LeftShin, gap.RightShin, planGap.LeftShin, planGap.RightShin,
                    gap.LeftThigh, gap.RightThigh, planGap.LeftThigh, planGap.RightThigh, V(capture),
                    margin, string.Join(" ", hull.Select(point => V(point))));
                for (int side = 0; side < Sides.Length; side++)
                {
                    string prefix = Sides[side] + "_";
                    foreach (string part in Landmarks)
                    {
                        var joint = _world.Rig.PoweredController.GetJoint(prefix + part).Joint;
                        Vector3 anchor = joint.transform.TransformPoint(joint.anchor);
                        rows.Append(',');
                        Row(rows, V(anchor), V(_world.Rig.Segments[prefix + part].Body.GetPointVelocity(anchor)), V(kin.JointAnchorWorld(poses, prefix + part)));
                    }
                    BenchGrip grip = side == 0 ? rt.LeftGrip : rt.RightGrip;
                    Rigidbody hand = _world.Rig.Segments[prefix + "hand"].Body;
                    Pose planHand = poses[prefix + "hand"];
                    Vector3 anchorWorld = hand.transform.TransformPoint(grip.HandAnchorLocal);
                    rows.Append(',');
                    Row(rows, V(anchorWorld), V(hand.GetPointVelocity(anchorWorld)), V(rt.Bar.transform.TransformPoint(grip.BarAnchorLocal)),
                        V(planHand.position + planHand.rotation * grip.HandAnchorLocal), V(planBar + planBarRotation * grip.BarAnchorLocal),
                        V(grip.ForceOnBarWorld), load[side], V(pressure[side].Cop), pressure[side].LoadedAxisMin, pressure[side].LoadedAxisMax);
                }
                rows.AppendLine();

                foreach (var joint in joints)
                {
                    PoweredJointDiagnostic d = joint.PostPhysicsDiagnostic;
                    JointDrive x = joint.Joint.angularXDrive, yz = joint.Joint.angularYZDrive;
                    Row(channels, rt.Tick, joint.Id, Q(logical.TryGetValue(joint.Id, out Quaternion plan) ? plan : Quaternion.identity),
                        Q(d.RequestedTarget), Q(d.AppliedTarget), Q(d.ActualRelative), V(d.ErrorRad),
                        V(d.TargetAngularVelocityRadS), V(d.ActualAngularVelocityRadS),
                        d.TwistDriveDemandNm, x.maximumForce, d.TwistDriveDemandFraction, x.positionSpring, x.positionDamper,
                        d.HasSecondaryChannel ? 0f : d.SwingDriveDemandNm, d.HasSecondaryChannel ? 0f : yz.maximumForce,
                        d.HasSecondaryChannel ? 0f : d.SwingDriveDemandFraction, d.HasSecondaryChannel ? 0f : yz.positionSpring, d.HasSecondaryChannel ? 0f : yz.positionDamper,
                        d.SecondaryDriveDemandNm, d.SecondaryMaximumForceNm, d.SecondaryDriveDemandFraction,
                        d.HasSecondaryChannel ? yz.positionSpring : 0f, d.HasSecondaryChannel ? yz.positionDamper : 0f,
                        d.SecondaryRequestedAngleDeg, d.SecondaryAppliedAngleDeg, d.SecondaryActualAngleDeg, d.SecondaryActualAngularVelocityRadS,
                        d.SecondaryTargetRateLimited, d.LimitProximity, d.SecondaryLimitProximity, V(d.SolverTorqueJointSpaceNm),
                        V(rt.Hold.AppliedReferenceAxesFor(joint.Id)));
                    channels.AppendLine();
                }

                if (rt.Tick == result.ConfirmedSeparation)
                {
                    result.SeparationState = rows.ToString();
                    result.SeparationChannels = channels.ToString();
                }
                if (result.ConfirmedSeparation != 0 && rt.Tick == result.ConfirmedSeparation + 1)
                {
                    result.LeftRequestAfterSeparation = leftShoulder.PostPhysicsDiagnostic.RequestedTarget;
                    result.RightRequestAfterSeparation = rightShoulder.PostPhysicsDiagnostic.RequestedTarget;
                }

                if (result.Pull == 0)
                    continue;
                result.MinGap = Mathf.Min(result.MinGap, gap.Leg);
                if (rt.Pull.Phase == DeadliftPullPhase.Lockout)
                {
                    result.LockoutGapSum += gap.Thigh;
                    result.LockoutSamples++;
                }
                if (result.ConfirmedSeparation != 0 && rt.PlateLeftN + rt.PlateRightN < 600f)
                    result.MinAirborneFootN = Mathf.Min(result.MinAirborneFootN, load.Min());
                result.MinSupport = Mathf.Min(result.MinSupport, margin);
                result.StopReason = rt.BarOnLegN > 0f ? "bar-leg impulse"
                    : closed != null && gap.Leg <= 0f ? "shaft penetration"
                    : load.Min() <= rt.Monitor.LoadedFootN || margin < 0f ? "support loss"
                    : closed != null && !throughKnee && !complete && rt.Bar.position.y - startBarY >= DeadliftPreparedAscentPlan.QualifiedRiseM ? "first segment end"
                    : postKneeProbe && rt.Referee.DownGiven ? "motionless lockout"
                    : postKneeProbe && rt.Referee.Faults.Count > 0 ? "referee fault"
                    : complete && rt.Referee.Completed && rt.GripsReleased ? "judged return"
                    : complete && rt.Referee.Faults.Count > 0 ? "referee fault"
                    : !complete && !postKneeProbe && rt.Bar.position.y - bar0.y >= rt.Path.KneePassageRiseM ? "knee-region end"
                    : tick == lastTick ? "comparison bound" : null;
                if (result.StopReason != null)
                {
                    result.Stop = rt.Tick;
                    break;
                }
            }

            result.Good = rt.Referee.GoodLift;
            result.Released = rt.GripsReleased;
            result.AscentAdmission = rt.AscentAdmission;
            result.PostKneeAdmission = rt.PostKneeAdmission;
            result.MaxTickMs = rt.MaxTickMilliseconds;
            result.MaxTickMsTick = rt.MaxTickMillisecondsTick;
            if (rt.InitializationMilliseconds.Count > 0)
                Debug.Log($"{arm} initialization: " + string.Join(", ", rt.InitializationMilliseconds.Select(kv => $"{kv.Key} {kv.Value:F0} ms")));
            result.Down = rt.Pull.DownTick;
            result.RefereeSummary = rt.Referee.Summary();
            if (complete)
                Debug.Log($"Composed prepared attempt: {result.RefereeSummary}; DOWN {result.Down}; released {result.Released}; good {result.Good}; min airborne foot {result.MinAirborneFootN:F1} N; mean lockout thigh {result.MeanLockoutGap * 1000d:F2} mm");
            File.WriteAllText(Path.Combine(F.EvidenceDirectory, $"transition-{arm}.csv"), rows.ToString());
            File.WriteAllText(Path.Combine(F.EvidenceDirectory, $"transition-{arm}-channels.csv"), channels.ToString());
            Debug.Log($"{arm}: PULL {result.Pull}, physical separation {result.PhysicalSeparation}, confirmed {result.ConfirmedSeparation}, first penetration {result.FirstPenetration}, stop {result.Stop} ({result.StopReason}), min gap {result.MinGap * 1000f:F2} mm, min margin {result.MinSupport * 1000f:F1} mm");
            yield return _world.Teardown();
            _world = null;
        }

        private static void Row(StringBuilder b, params object[] cells) =>
            b.AppendJoin(",", cells.Select(cell => cell is float f ? N(f) : cell.ToString()));

        private static string N(float x) => x.ToString("G9", CultureInfo.InvariantCulture);
        private static string V(Vector2 v) => $"{N(v.x)}|{N(v.y)}";
        private static string V(Vector3 v) => $"{N(v.x)}|{N(v.y)}|{N(v.z)}";
        private static string Q(Quaternion q) => $"{N(q.x)}|{N(q.y)}|{N(q.z)}|{N(q.w)}";
    }
}
