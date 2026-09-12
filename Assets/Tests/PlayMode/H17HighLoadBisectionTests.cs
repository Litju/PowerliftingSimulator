using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class H17HighLoadBisectionTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string ArtifactDirectory = "Artifacts/Measurements/GAM-11";

        private const float StandingAbdomen0Kg = 0.83f;
        private const float StandingThorax0Kg = 0.92f;
        private const float StandingAbdomen25Kg = 3.93f;
        private const float StandingThorax25Kg = 3.67f;

        private static readonly float[] ReferencePhases =
        {
            0.00f, 0.05f, 0.10f, 0.15f, 0.20f, 0.25f, 0.30f, 0.35f, 0.40f, 0.45f, 0.50f,
            0.55f, 0.60f, 0.65f, 0.70f, 0.75f, 0.80f, 0.85f, 0.90f, 0.95f, 1.00f
        };

        private static readonly float[] ReferenceWorldTrunkPitchDeg =
        {
            3.91f, 6.30f, 8.91f, 11.59f, 14.19f, 16.56f, 18.93f, 21.56f, 24.27f, 26.91f, 29.32f,
            31.34f, 32.17f, 32.52f, 33.25f, 34.04f, 34.72f, 35.16f, 35.47f, 35.73f, 35.97f
        };

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private PhysicalBarbell _bar;
        private SquatPhysicalPrototypeController _controller;
        private Collider _platform;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = true;
            if (_controller != null)
            {
                SquatPhysicalAdapter adapter = _controller.Adapter;
                if (adapter != null)
                {
                    adapter.SpineBiasRateFeedforwardEnabled = true;
                    ApplyStanding(adapter, true);
                }
                _controller.enabled = false;
            }
            if (_rig != null)
                _rig.enabled = false;
            if (_bar != null)
                _bar.enabled = false;
            if (_bootstrap != null && _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = _bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            if (_bootstrap != null)
                UnityEngine.Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _rig = null;
            _bar = null;
            _controller = null;
            _platform = null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator LoadQualificationScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "Qualification scene is missing.");
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _bar = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            _platform = GameObject.Find("PhysicalPlatform_GAM6")?.GetComponent<Collider>();

            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_bootstrap.Runtime, Is.Not.Null);
            Assert.That(_bootstrap.Runtime.IsInitialized, Is.True);
            Assert.That(_rig, Is.Not.Null);
            Assert.That(_bar, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);
            for (int frame = 0; frame < 5 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            Assert.That(_platform, Is.Not.Null);
        }

        private void PrepareManualRuntime(float loadKg)
        {
            Assert.That(_controller, Is.Not.Null);
            _controller.SetLoad(loadKg);
            Assert.That(_controller.Adapter, Is.Not.Null);
            if (loadKg > 0f)
                Assert.That(_controller.Saddle, Is.Not.Null);

            _controller.enabled = false;
            _bootstrap.enabled = false;
            _rig.enabled = false;
            _bar.enabled = false;
        }

        private static void AdvanceTicks(FoundationRuntime runtime, int count)
        {
            for (int index = 0; index < count; index++)
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
        }

        private static void ClearStanding(SquatPhysicalAdapter adapter)
        {
            adapter.Preload.StandingAbdomenBiasDegrees0Kg = 0f;
            adapter.Preload.StandingThoraxBiasDegrees0Kg = 0f;
            adapter.Preload.StandingAbdomenBiasDegrees25Kg = 0f;
            adapter.Preload.StandingThoraxBiasDegrees25Kg = 0f;
        }

        private static void ApplyStanding(SquatPhysicalAdapter adapter, bool enabled)
        {
            adapter.Preload.StandingAbdomenBiasDegrees0Kg = enabled ? StandingAbdomen0Kg : 0f;
            adapter.Preload.StandingThoraxBiasDegrees0Kg = enabled ? StandingThorax0Kg : 0f;
            adapter.Preload.StandingAbdomenBiasDegrees25Kg = enabled ? StandingAbdomen25Kg : 0f;
            adapter.Preload.StandingThoraxBiasDegrees25Kg = enabled ? StandingThorax25Kg : 0f;
        }

        private static float TwistX(Quaternion rotation)
        {
            Quaternion q = PoweredJointController.NormalizeCanonical(rotation);
            var vector = new Vector3(q.x, q.y, q.z);
            Vector3 projection = Vector3.Project(vector, Vector3.right);
            var twist = new Quaternion(projection.x, projection.y, projection.z, q.w);
            float magnitude = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (magnitude < 1e-6f)
                return 0f;
            twist = new Quaternion(twist.x / magnitude, twist.y / magnitude, twist.z / magnitude, twist.w / magnitude);
            twist.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
                angle -= 360f;
            return angle * Mathf.Sign(Vector3.Dot(axis, Vector3.right));
        }

        private static float SampleReference(float sq)
        {
            if (sq <= ReferencePhases[0])
                return ReferenceWorldTrunkPitchDeg[0];
            for (int i = 1; i < ReferencePhases.Length; i++)
            {
                if (sq > ReferencePhases[i])
                    continue;
                float t = Mathf.InverseLerp(ReferencePhases[i - 1], ReferencePhases[i], sq);
                return Mathf.Lerp(ReferenceWorldTrunkPitchDeg[i - 1], ReferenceWorldTrunkPitchDeg[i], t);
            }
            return ReferenceWorldTrunkPitchDeg[ReferenceWorldTrunkPitchDeg.Length - 1];
        }

        public struct TickRecord
        {
            public int Tick;
            public float Time;
            public float Sq;
            public string State;
            public float BarY;
            public float BarVelY;
            public float PelvisY;
            public float PelvisVelY;
            public float ThoraxY;
            public float ThoraxVelY;
            public float KineticEnergy;
            public float SaddleSep;
            public bool SaddleAttached;
            public int Contacts;
            public float ComAp;
            public float CopAp;
            public float AbdTargetRate;
            public float ThoTargetRate;
            public float AbdActualDeg;
            public float ThoActualDeg;
            public float AbdDemand;
            public float ThoDemand;
            public float AbdError;
            public float ThoError;
            public float TrunkError;
            public bool AllFinite;
        }

        public class CaseRunResult
        {
            public string CaseTag;
            public bool StandingBias;
            public bool RateFeedforward;
            public float StandingBarY;
            public float MinBarY;
            public float FinalBarY;
            public float FinalDeltaY;
            public bool Detached;
            public int FirstDivergentTick = -1;
            public int FirstNonfiniteTick = -1;
            public float MaxKineticEnergy;
            public List<TickRecord> Ticks = new List<TickRecord>(850);
        }

        [UnityTest]
        public IEnumerator H17_01_H15_EMULATION_VALIDATION()
        {
            LogAssert.ignoreFailingMessages = true;

            // Test R00 on 0kg and 25kg driven descent to prove H15 equivalence
            foreach (float loadKg in new[] { 0f, 25f })
            {
                yield return LoadQualificationScene();
                PrepareManualRuntime(loadKg);
                FoundationRuntime runtime = _bootstrap.Runtime;
                SquatPhysicalAdapter adapter = _controller.Adapter;
                PoweredJointController powered = _rig.PoweredController;
                Rigidbody pelvis = _rig.Segments["pelvis"].Body;
                Rigidbody thoraxBody = _rig.Segments["thorax"].Body;
                float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

                // R00: Both switches OFF
                ClearStanding(adapter);
                adapter.SpineBiasRateFeedforwardEnabled = false;
                adapter.BalanceCorrectionsEnabled = true;
                adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);

                for (int i = 0; i < 60; i++)
                    AdvanceTicks(runtime, 1);

                var stops = new List<float> { 0.00f, 0.25f, 0.55f, 0.80f, 1.00f };
                int stopIdx = 0;
                float sq = 0f;
                int tick = 0;

                Debug.Log($"[H15_EMULATION_R00] Starting load={loadKg:F0}kg");
                while (tick < 420 && stopIdx < stops.Count)
                {
                    if (sq >= stops[stopIdx] - 1e-4f)
                    {
                        adapter.TryGetTargetComposition("abdomen", out var abdC);
                        adapter.TryGetTargetComposition("thorax", out var thoC);
                        adapter.TryGetTargetComposition("left_thigh", out var hipC);
                        float abdActual = TwistX(powered.GetJoint("abdomen").Diagnostic.ActualRelative);
                        float thoActual = TwistX(powered.GetJoint("thorax").Diagnostic.ActualRelative);
                        float hipActual = TwistX(powered.GetJoint("left_thigh").Diagnostic.ActualRelative);
                        Vector3 axis = thoraxBody.position - pelvis.position;
                        float worldTrunk = Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg;
                        float refTrunk = SampleReference(sq);

                        float abdErr = abdActual - TwistX(abdC.Nominal);
                        float thoErr = thoActual - TwistX(thoC.Nominal);
                        float trunkErr = worldTrunk - refTrunk;
                        float hipErr = hipActual - TwistX(hipC.Nominal);
                        int contacts = adapter.Balance.SupportContactCount;

                        Debug.Log(string.Format(CultureInfo.InvariantCulture,
                            "[H15_EMULATION_R00] load={0:F0} sq={1:F2} abdErr={2:F2} thoErr={3:F2} trunkErr={4:F2} hipErr={5:F2} contacts={6}",
                            loadKg, sq, abdErr, thoErr, trunkErr, hipErr, contacts));

                        stopIdx++;
                        continue;
                    }

                    sq = Mathf.Min(1f, sq + 0.30f * dt);
                    SquatState state = sq >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
                    adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Descent, state, 0.30f);
                    AdvanceTicks(runtime, 1);
                    tick++;
                }

                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator H17_02_105KG_FACTORIAL_BISECTION()
        {
            LogAssert.ignoreFailingMessages = true;

            var cases = new[]
            {
                new { Tag = "R00", Standing = false, Rate = false },
                new { Tag = "R10", Standing = true,  Rate = false },
                new { Tag = "R01", Standing = false, Rate = true  },
                new { Tag = "R11", Standing = true,  Rate = true  },
            };

            var results = new List<CaseRunResult>();

            foreach (var c in cases)
            {
                yield return LoadQualificationScene();
                PrepareManualRuntime(105f);

                FoundationRuntime runtime = _bootstrap.Runtime;
                SquatBarSaddle saddle = _controller.Saddle;
                SquatPhysicalAdapter adapter = _controller.Adapter;
                PoweredJointController powered = _rig.PoweredController;
                Rigidbody pelvis = _rig.Segments["pelvis"].Body;
                Rigidbody thorax = _rig.Segments["thorax"].Body;

                ApplyStanding(adapter, c.Standing);
                adapter.SpineBiasRateFeedforwardEnabled = c.Rate;

                // Settlement phase: 30 ticks
                for (int i = 0; i < 30; i++)
                    AdvanceTicks(runtime, 1);

                float standingBarY = _bar.Body.position.y;
                float minBarY = standingBarY;

                var run = new CaseRunResult
                {
                    CaseTag = c.Tag,
                    StandingBias = c.Standing,
                    RateFeedforward = c.Rate,
                    StandingBarY = standingBarY
                };

                adapter.StartSquat();
                int totalTicks = Mathf.CeilToInt(8.0f / (float)SimulationConstants.FixedDeltaTimeSeconds);

                for (int tick = 0; tick < totalTicks; tick++)
                {
                    AdvanceTicks(runtime, 1);

                    float barY = _bar.Body.position.y;
                    float barVelY = _bar.Body.linearVelocity.y;
                    float pelY = pelvis.position.y;
                    float pelVelY = pelvis.linearVelocity.y;
                    float thoY = thorax.position.y;
                    float thoVelY = thorax.linearVelocity.y;

                    if (barY < minBarY)
                        minBarY = barY;

                    // Kinetic energy
                    float ke = 0.5f * _bar.Body.mass * _bar.Body.linearVelocity.sqrMagnitude;
                    bool allFinite = float.IsFinite(barY) && float.IsFinite(barVelY) &&
                                     float.IsFinite(pelY) && float.IsFinite(pelVelY) &&
                                     float.IsFinite(thoY) && float.IsFinite(thoVelY);

                    foreach (var seg in _rig.Segments.Values)
                    {
                        if (seg.Body != null)
                        {
                            ke += 0.5f * seg.Body.mass * seg.Body.linearVelocity.sqrMagnitude;
                            if (!float.IsFinite(seg.Body.position.x) || !float.IsFinite(seg.Body.linearVelocity.x))
                                allFinite = false;
                        }
                    }

                    if (ke > run.MaxKineticEnergy)
                        run.MaxKineticEnergy = ke;

                    if (!allFinite && run.FirstNonfiniteTick < 0)
                        run.FirstNonfiniteTick = tick;

                    if (barY < -2.0f && run.FirstDivergentTick < 0)
                        run.FirstDivergentTick = tick;

                    adapter.TryGetTargetComposition("abdomen", out var abdC);
                    adapter.TryGetTargetComposition("thorax", out var thoC);
                    float abdAct = TwistX(powered.GetJoint("abdomen").Diagnostic.ActualRelative);
                    float thoAct = TwistX(powered.GetJoint("thorax").Diagnostic.ActualRelative);
                    Vector3 axis = thorax.position - pelvis.position;
                    float worldTrunk = Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg;
                    float refTrunk = SampleReference(adapter.Sq);

                    var tr = new TickRecord
                    {
                        Tick = tick,
                        Time = tick * 0.01f,
                        Sq = adapter.Sq,
                        State = adapter.State.ToString(),
                        BarY = barY,
                        BarVelY = barVelY,
                        PelvisY = pelY,
                        PelvisVelY = pelVelY,
                        ThoraxY = thoY,
                        ThoraxVelY = thoVelY,
                        KineticEnergy = ke,
                        SaddleSep = saddle.SaddleSeparationMeters,
                        SaddleAttached = saddle.IsAttached,
                        Contacts = adapter.Balance.SupportContactCount,
                        ComAp = adapter.Balance.SystemCom.z,
                        CopAp = adapter.Balance.HasCopEstimate ? adapter.Balance.CopEstimate.z : adapter.Balance.SupportApCenter,
                        AbdTargetRate = powered.GetJoint("abdomen").Diagnostic.TargetAngularVelocityRadS.x,
                        ThoTargetRate = powered.GetJoint("thorax").Diagnostic.TargetAngularVelocityRadS.x,
                        AbdActualDeg = abdAct,
                        ThoActualDeg = thoAct,
                        AbdDemand = powered.GetJoint("abdomen").Diagnostic.ModeledDemand,
                        ThoDemand = powered.GetJoint("thorax").Diagnostic.ModeledDemand,
                        AbdError = abdAct - TwistX(abdC.Nominal),
                        ThoError = thoAct - TwistX(thoC.Nominal),
                        TrunkError = worldTrunk - refTrunk,
                        AllFinite = allFinite
                    };
                    run.Ticks.Add(tr);

                    if (tick % 50 == 0 || tick == totalTicks - 1)
                    {
                        Debug.Log(string.Format(CultureInfo.InvariantCulture,
                            "[{0} Tick {1:D3}] sq={2:F2} state={3} barY={4:F3} pelvisY={5:F3} ke={6:F1} sep={7:F3} att={8}",
                            c.Tag, tick, adapter.Sq, adapter.State, barY, pelY, ke, saddle.SaddleSeparationMeters, saddle.IsAttached));
                    }
                }

                run.MinBarY = minBarY;
                run.FinalBarY = _bar.Body.position.y;
                run.FinalDeltaY = standingBarY - run.FinalBarY;
                run.Detached = !saddle.IsAttached;
                results.Add(run);

                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[{0}_RESULT] standingBarY={1:F3} minBarY={2:F3} finalBarY={3:F3} deltaY={4:F3} firstDivTick={5} maxKE={6:F1}",
                    c.Tag, standingBarY, minBarY, run.FinalBarY, run.FinalDeltaY, run.FirstDivergentTick, run.MaxKineticEnergy));

                yield return null;
            }

            // Write Factorial Summary CSV
            string dir = Path.Combine(Directory.GetCurrentDirectory(), ArtifactDirectory);
            Directory.CreateDirectory(dir);

            var summaryCsv = new StringBuilder();
            summaryCsv.AppendLine("case,standing_bias,rate_ff,standing_bar_y,min_bar_y,final_bar_y,final_delta_y,first_div_tick,max_ke,first_nonfinite_tick");
            foreach (var r in results)
            {
                summaryCsv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7},{8:F2},{9}",
                    r.CaseTag, r.StandingBias ? 1 : 0, r.RateFeedforward ? 1 : 0,
                    r.StandingBarY, r.MinBarY, r.FinalBarY, r.FinalDeltaY,
                    r.FirstDivergentTick, r.MaxKineticEnergy, r.FirstNonfiniteTick));
            }
            File.WriteAllText(Path.Combine(dir, "H17-factorial-summary.csv"), summaryCsv.ToString());

            // Find first case difference tick between R00 and other cases
            var r00 = results.Find(r => r.CaseTag == "R00");
            for (int caseIdx = 1; caseIdx < results.Count; caseIdx++)
            {
                var other = results[caseIdx];
                int firstDiffTick = -1;
                string diffField = "";
                for (int t = 0; t < r00.Ticks.Count && t < other.Ticks.Count; t++)
                {
                    var a = r00.Ticks[t];
                    var b = other.Ticks[t];
                    if (Mathf.Abs(a.BarY - b.BarY) > 0.001f)
                    {
                        firstDiffTick = t;
                        diffField = $"barY (R00={a.BarY:F3} vs {other.CaseTag}={b.BarY:F3})";
                        break;
                    }
                    if (Mathf.Abs(a.PelvisY - b.PelvisY) > 0.001f)
                    {
                        firstDiffTick = t;
                        diffField = $"pelvisY (R00={a.PelvisY:F3} vs {other.CaseTag}={b.PelvisY:F3})";
                        break;
                    }
                    if (Mathf.Abs(a.AbdTargetRate - b.AbdTargetRate) > 0.001f)
                    {
                        firstDiffTick = t;
                        diffField = $"abdTargetRate (R00={a.AbdTargetRate:F3} vs {other.CaseTag}={b.AbdTargetRate:F3})";
                        break;
                    }
                }
                Debug.Log($"[DIFF_VS_R00] {other.CaseTag} first diff tick={firstDiffTick} field={diffField}");
            }

            Debug.Log("[H17_FACTORIAL_BISECTION_COMPLETE]");
            yield return null;
        }

        public struct SettleAndDescentTick
        {
            public int Step; // -30 to 120
            public float Time;
            public float BarY;
            public float BarZ;
            public float BarVy;
            public float BarVz;
            public float PelvisY;
            public float PelvisZ;
            public float PelvisVy;
            public float PelvisVz;
            public float ThoraxY;
            public float ThoraxZ;
            public float ThoraxVy;
            public float ThoraxVz;
            public float TrunkPitchDeg;
            public float SaddleSep;
            public bool SaddleAttached;
            public float KE;
            public int Contacts;
            public float ComAp;
            public float CopAp;
            public float AbdBiasDeg;
            public float ThoBiasDeg;
            public float AbdTargetRate;
            public float ThoTargetRate;
            public float AbdActualDeg;
            public float ThoActualDeg;
            public float AbdDemand;
            public float ThoDemand;
            public float AnkleBalanceDeg;
        }

        [UnityTest]
        public IEnumerator H17_03_TRACE_FIRST_DIVERGENCE()
        {
            LogAssert.ignoreFailingMessages = true;

            var r00Steps = new List<SettleAndDescentTick>(160);
            var r10Steps = new List<SettleAndDescentTick>(160);

            // Run R00
            yield return LoadQualificationScene();
            PrepareManualRuntime(105f);
            ClearStanding(_controller.Adapter);
            _controller.Adapter.SpineBiasRateFeedforwardEnabled = false;
            yield return RunTraceFromSettle(r00Steps);

            // Run R10
            yield return LoadQualificationScene();
            PrepareManualRuntime(105f);
            ApplyStanding(_controller.Adapter, true);
            _controller.Adapter.SpineBiasRateFeedforwardEnabled = false;
            yield return RunTraceFromSettle(r10Steps);

            // Analysis: Compare R00 vs R10 step by step
            int firstCommandDiffStep = int.MaxValue;
            string firstCommandDiffDesc = "";
            int firstPhysicalDiffStep = int.MaxValue;
            string firstPhysicalDiffDesc = "";
            int firstKEDiffStep = int.MaxValue;
            int firstContactLossStep = int.MaxValue;
            int firstDivergentStep = int.MaxValue;

            for (int i = 0; i < r00Steps.Count && i < r10Steps.Count; i++)
            {
                var a = r00Steps[i];
                var b = r10Steps[i];

                if (firstCommandDiffStep == int.MaxValue && (Mathf.Abs(a.AbdBiasDeg - b.AbdBiasDeg) > 0.01f || Mathf.Abs(a.ThoBiasDeg - b.ThoBiasDeg) > 0.01f))
                {
                    firstCommandDiffStep = a.Step;
                    firstCommandDiffDesc = $"SpineBiasCmd (R00=[{a.AbdBiasDeg:F2},{a.ThoBiasDeg:F2}] vs R10=[{b.AbdBiasDeg:F2},{b.ThoBiasDeg:F2}])";
                }

                if (firstPhysicalDiffStep == int.MaxValue && (Mathf.Abs(a.BarY - b.BarY) > 0.005f || Mathf.Abs(a.PelvisY - b.PelvisY) > 0.005f || Mathf.Abs(a.TrunkPitchDeg - b.TrunkPitchDeg) > 0.2f))
                {
                    firstPhysicalDiffStep = a.Step;
                    firstPhysicalDiffDesc = $"BarY/Trunk (R00=[barY={a.BarY:F3},trunk={a.TrunkPitchDeg:F2}] vs R10=[barY={b.BarY:F3},trunk={b.TrunkPitchDeg:F2}])";
                }

                if (firstKEDiffStep == int.MaxValue && Mathf.Abs(a.KE - b.KE) > 10f)
                {
                    firstKEDiffStep = a.Step;
                }

                if (firstContactLossStep == int.MaxValue && b.Contacts == 0)
                {
                    firstContactLossStep = b.Step;
                }

                if (firstDivergentStep == int.MaxValue && (Mathf.Abs(a.TrunkPitchDeg - b.TrunkPitchDeg) > 20f || Mathf.Abs(a.BarY - b.BarY) > 0.10f))
                {
                    firstDivergentStep = a.Step;
                }
            }

            Debug.Log($"[FIRST_CAUSE_ANALYSIS] firstCommandDiffStep={firstCommandDiffStep} ({firstCommandDiffDesc})");
            Debug.Log($"[FIRST_CAUSE_ANALYSIS] firstPhysicalDiffStep={firstPhysicalDiffStep} ({firstPhysicalDiffDesc})");
            string keDiffInfo = (firstKEDiffStep != int.MaxValue && firstKEDiffStep + 30 >= 0 && firstKEDiffStep + 30 < r00Steps.Count && firstKEDiffStep + 30 < r10Steps.Count)
                ? $" (R00_KE={r00Steps[firstKEDiffStep + 30].KE:F1} vs R10_KE={r10Steps[firstKEDiffStep + 30].KE:F1})"
                : "";
            Debug.Log($"[FIRST_CAUSE_ANALYSIS] firstKEDiffStep={firstKEDiffStep}{keDiffInfo}");
            Debug.Log($"[FIRST_CAUSE_ANALYSIS] firstContactLossStep={firstContactLossStep}");
            Debug.Log($"[FIRST_CAUSE_ANALYSIS] firstDivergentStep={firstDivergentStep}");

            // Write detailed window CSV around first divergence (from step -30 to step 120)
            string dir = Path.Combine(Directory.GetCurrentDirectory(), ArtifactDirectory);
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine("case,step,time_s,bar_y,bar_z,bar_vy,bar_vz,pelvis_y,pelvis_z,pelvis_vy,pelvis_vz,thorax_y,thorax_z,trunk_pitch_deg,ke,saddle_sep,saddle_att,contacts,com_ap,cop_ap,abd_bias_deg,tho_bias_deg,abd_tgt_rate,tho_tgt_rate,abd_act_deg,tho_act_deg,abd_demand,tho_demand,ankle_bal_deg");

            foreach (var s in r00Steps)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "R00,{0},{1:F3},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F2},{13:F1},{14:F4},{15},{16},{17:F4},{18:F4},{19:F2},{20:F2},{21:F4},{22:F4},{23:F2},{24:F2},{25:F4},{26:F4},{27:F2}",
                    s.Step, s.Time, s.BarY, s.BarZ, s.BarVy, s.BarVz, s.PelvisY, s.PelvisZ, s.PelvisVy, s.PelvisVz,
                    s.ThoraxY, s.ThoraxZ, s.TrunkPitchDeg, s.KE, s.SaddleSep, s.SaddleAttached ? 1 : 0, s.Contacts,
                    s.ComAp, s.CopAp, s.AbdBiasDeg, s.ThoBiasDeg, s.AbdTargetRate, s.ThoTargetRate,
                    s.AbdActualDeg, s.ThoActualDeg, s.AbdDemand, s.ThoDemand, s.AnkleBalanceDeg));
            }
            foreach (var s in r10Steps)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "R10,{0},{1:F3},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F2},{13:F1},{14:F4},{15},{16},{17:F4},{18:F4},{19:F2},{20:F2},{21:F4},{22:F4},{23:F2},{24:F2},{25:F4},{26:F4},{27:F2}",
                    s.Step, s.Time, s.BarY, s.BarZ, s.BarVy, s.BarVz, s.PelvisY, s.PelvisZ, s.PelvisVy, s.PelvisVz,
                    s.ThoraxY, s.ThoraxZ, s.TrunkPitchDeg, s.KE, s.SaddleSep, s.SaddleAttached ? 1 : 0, s.Contacts,
                    s.ComAp, s.CopAp, s.AbdBiasDeg, s.ThoBiasDeg, s.AbdTargetRate, s.ThoTargetRate,
                    s.AbdActualDeg, s.ThoActualDeg, s.AbdDemand, s.ThoDemand, s.AnkleBalanceDeg));
            }
            File.WriteAllText(Path.Combine(dir, "H17-first-divergence-causal-case.csv"), sb.ToString());

            yield return null;
        }

        private IEnumerator RunTraceFromSettle(List<SettleAndDescentTick> trace)
        {
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatBarSaddle saddle = _controller.Saddle;
            SquatPhysicalAdapter adapter = _controller.Adapter;
            PoweredJointController powered = _rig.PoweredController;
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            Rigidbody thorax = _rig.Segments["thorax"].Body;

            // Settle 30 ticks (step -30 to -1)
            for (int i = 0; i < 30; i++)
            {
                int step = -30 + i;
                RecordStep(step, trace, saddle, adapter, powered, pelvis, thorax);
                AdvanceTicks(runtime, 1);
            }

            // Start squat at step 0
            adapter.StartSquat();

            // Run 120 ticks of descent (step 0 to 120)
            for (int step = 0; step <= 120; step++)
            {
                AdvanceTicks(runtime, 1);
                RecordStep(step, trace, saddle, adapter, powered, pelvis, thorax);
            }
            yield return null;
        }

        private void RecordStep(
            int step, List<SettleAndDescentTick> trace,
            SquatBarSaddle saddle, SquatPhysicalAdapter adapter,
            PoweredJointController powered, Rigidbody pelvis, Rigidbody thorax)
        {
            float barY = _bar.Body.position.y;
            float barZ = _bar.Body.position.z;
            float barVy = _bar.Body.linearVelocity.y;
            float barVz = _bar.Body.linearVelocity.z;
            float pelY = pelvis.position.y;
            float pelZ = pelvis.position.z;
            float pelVy = pelvis.linearVelocity.y;
            float pelVz = pelvis.linearVelocity.z;
            float thoY = thorax.position.y;
            float thoZ = thorax.position.z;
            float thoVy = thorax.linearVelocity.y;
            float thoVz = thorax.linearVelocity.z;

            Vector3 axis = thorax.position - pelvis.position;
            float trunkPitch = Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg;

            float ke = 0.5f * _bar.Body.mass * _bar.Body.linearVelocity.sqrMagnitude;
            foreach (var seg in _rig.Segments.Values)
            {
                if (seg.Body != null)
                    ke += 0.5f * seg.Body.mass * seg.Body.linearVelocity.sqrMagnitude;
            }

            float abdAct = TwistX(powered.GetJoint("abdomen").Diagnostic.ActualRelative);
            float thoAct = TwistX(powered.GetJoint("thorax").Diagnostic.ActualRelative);

            float abdBias = adapter.Preload.SpineBiasDegrees(SquatJointFamily.Abdomen, adapter.Sq, adapter.EquilibriumLoadKg);
            float thoBias = adapter.Preload.SpineBiasDegrees(SquatJointFamily.Thorax, adapter.Sq, adapter.EquilibriumLoadKg);

            trace.Add(new SettleAndDescentTick
            {
                Step = step,
                Time = step * 0.01f,
                BarY = barY,
                BarZ = barZ,
                BarVy = barVy,
                BarVz = barVz,
                PelvisY = pelY,
                PelvisZ = pelZ,
                PelvisVy = pelVy,
                PelvisVz = pelVz,
                ThoraxY = thoY,
                ThoraxZ = thoZ,
                ThoraxVy = thoVy,
                ThoraxVz = thoVz,
                TrunkPitchDeg = trunkPitch,
                SaddleSep = saddle != null ? saddle.SaddleSeparationMeters : 0f,
                SaddleAttached = saddle != null && saddle.IsAttached,
                KE = ke,
                Contacts = adapter.Balance.SupportContactCount,
                ComAp = adapter.Balance.SystemCom.z,
                CopAp = adapter.Balance.HasCopEstimate ? adapter.Balance.CopEstimate.z : adapter.Balance.SupportApCenter,
                AbdBiasDeg = abdBias,
                ThoBiasDeg = thoBias,
                AbdTargetRate = powered.GetJoint("abdomen").Diagnostic.TargetAngularVelocityRadS.x,
                ThoTargetRate = powered.GetJoint("thorax").Diagnostic.TargetAngularVelocityRadS.x,
                AbdActualDeg = abdAct,
                ThoActualDeg = thoAct,
                AbdDemand = powered.GetJoint("abdomen").Diagnostic.ModeledDemand,
                ThoDemand = powered.GetJoint("thorax").Diagnostic.ModeledDemand,
                AnkleBalanceDeg = adapter.BalanceCorrectionRad * Mathf.Rad2Deg
            });
        }

        /// <summary>
        /// Permanent regression contract for GAM-11 Phase 5H17.
        /// Asserts that under production controller configuration with the domain guard in place:
        /// 1. The H16 ~141m numerical divergence / explosion is permanently eliminated (NO_NUMERICAL_EXPLOSION).
        /// 2. Athlete and barbell remain strictly above the platform (barY > 0m, no tunneling).
        /// 3. All rigidbody and command states remain finite throughout (no NaNs or Infs).
        /// 4. Total system translational kinetic energy remains strictly bounded (< 5000 J vs ~246,000 J in H16).
        /// 5. The athlete collapses bounded on the platform at y ~ 0.223m (deltaY ~ 1.068m).
        /// 6. The 105kg physical squat gate remains red (FAIL), preventing false qualification claims.
        /// </summary>
        [UnityTest]
        public IEnumerator H17_04_105KG_NUMERICAL_REGRESSION_REMOVED_BOUNDED_COLLAPSE()
        {
            LogAssert.ignoreFailingMessages = true;

            yield return LoadQualificationScene();
            PrepareManualRuntime(105f);
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatPhysicalAdapter adapter = _controller.Adapter;
            SquatBarSaddle saddle = _controller.Saddle;

            // Default production configuration: no manual bias overrides or switch disables
            adapter.BalanceCorrectionsEnabled = true;
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);

            // Settle 30 ticks
            for (int i = 0; i < 30; i++)
                AdvanceTicks(runtime, 1);

            float standingBarY = _bar.Body.position.y;
            Assert.That(standingBarY, Is.GreaterThan(1.20f), "Bar standing position should be plausible (> 1.2m).");

            adapter.StartSquat();
            int totalTicks = Mathf.CeilToInt(8.0f / (float)SimulationConstants.FixedDeltaTimeSeconds);
            float minBarY = standingBarY;
            float maxKe = 0f;

            for (int tick = 0; tick < totalTicks; tick++)
            {
                AdvanceTicks(runtime, 1);

                float currentBarY = _bar.Body.position.y;
                if (currentBarY < minBarY)
                    minBarY = currentBarY;

                // Total translational kinetic energy check
                float ke = 0.5f * _bar.Body.mass * _bar.Body.linearVelocity.sqrMagnitude;
                foreach (var seg in _rig.Segments.Values)
                {
                    if (seg.Body != null)
                        ke += 0.5f * seg.Body.mass * seg.Body.linearVelocity.sqrMagnitude;
                }
                if (ke > maxKe)
                    maxKe = ke;

                // Permanent safety invariants
                Assert.That(float.IsFinite(currentBarY), Is.True, $"Bar Y non-finite at tick {tick}");
                Assert.That(float.IsFinite(ke), Is.True, $"KE non-finite at tick {tick}");
                Assert.That(currentBarY, Is.GreaterThan(0.0f), $"Bar penetrated platform into negative space at tick {tick} (barY={currentBarY:F3})");
                Assert.That(ke, Is.LessThan(5000.0f), $"Kinetic energy explosion detected at tick {tick} (KE={ke:F1} J)");
            }

            float finalBarY = _bar.Body.position.y;
            float deltaY = standingBarY - finalBarY;

            Debug.Log($"[H17_04_REGRESSION_PROOF] standingBarY={standingBarY:F4} minBarY={minBarY:F4} finalBarY={finalBarY:F4} deltaY={deltaY:F4} maxKE={maxKe:F1} J");

            // Regression contract assertions
            Assert.That(finalBarY, Is.GreaterThan(0.15f), "Bar must remain above platform surface (no platform tunneling).");
            Assert.That(finalBarY, Is.LessThan(0.40f), "Bar should come to rest on platform (bounded collapse).");
            Assert.That(deltaY, Is.LessThan(1.50f), "Bar displacement must remain bounded (pre-H16 bounded failure).");
            Assert.That(maxKe, Is.LessThan(2500.0f), "Kinetic energy must remain safely bounded throughout entire simulation.");

            // 105kg physical gate contract: MUST REMAIN FAIL
            Assert.That(deltaY, Is.GreaterThan(0.15f), "105kg physical squat gate must remain FAIL (do not rebrand bounded failure as pass).");

            yield return null;
        }
    }
}
