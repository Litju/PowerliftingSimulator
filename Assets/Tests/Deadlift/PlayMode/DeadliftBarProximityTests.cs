using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Deadlift.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 bar proximity: planned and realized horizontal bar-to-shin and
    /// bar-to-thigh surface gaps of the shipped attempt (DeadliftAttemptRuntime
    /// on fresh authoritative plants), measured separately through setup,
    /// liftoff, knee passage, upper-thigh ascent, lockout, lowering and return,
    /// against the rig's physical leg capsules and, as a check of what the owner
    /// sees, against the skinned visible mesh. Read-only: the measurement writes
    /// no body, joint target or force.
    /// </summary>
    public sealed class DeadliftBarProximityTests
    {
        private DeadliftPlantWorld _world;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_world != null)
                yield return _world.Teardown();
            _world = null;
            DeadliftAttemptRuntime.BarProximityPath = DeadliftAttemptRuntime.BarProximityPathDefault;
        }

        internal enum Window { Setup, BelowKnee, KneePassage, UpperThigh, Lockout, LoweringAbove, LoweringBelow, Returned }

        internal sealed class Stat
        {
            public int N;
            public double Sum;
            public float Min = float.PositiveInfinity, Max = float.NegativeInfinity;
            public void Add(float v)
            {
                if (float.IsNaN(v) || float.IsInfinity(v)) return;
                N++; Sum += v; Min = Mathf.Min(Min, v); Max = Mathf.Max(Max, v);
            }
            public float Mean => N == 0 ? float.NaN : (float)(Sum / N);
        }

        internal sealed class WindowStats
        {
            public int Ticks;
            public readonly Stat ShinGap = new Stat(), ThighGap = new Stat(), Shin3D = new Stat(), Thigh3D = new Stat();
            public readonly Stat PlanShin = new Stat(), PlanThigh = new Stat(), VisShin = new Stat(), VisThigh = new Stat();
            public readonly Stat BarZ = new Stat(), HipZ = new Stat(), ShoulderZ = new Stat(), AnchorZ = new Stat(), KneeZ = new Stat(), PlanBarZ = new Stat();
            public readonly Stat RollDemand = new Stat(), RollActual = new Stat(), BarLegN = new Stat(), Shoulder = new Stat(), Abdomen = new Stat(), Thorax = new Stat(), Hip = new Stat(), Knee = new Stat(), Ankle = new Stat();
            public int Saturated;
        }

        internal sealed class Record
        {
            public string Arm;
            public int Onset;
            public bool Good, Released, Down;
            public float Peak, MinFoot = float.MaxValue, FootTravel, MaxBarLegN, MinMargin = float.MaxValue;
            public ulong PullTick, Separation, LockoutTick, DownTick, ReturnedTick;
            public string FirstPull = string.Empty;
            public int Saturated, ZeroFootTicks;
            public string Summary = string.Empty, PathGeometry = string.Empty, PathReport = string.Empty;
            public float LockoutRise;
            internal readonly Dictionary<Window, WindowStats> Windows = new Dictionary<Window, WindowStats>();
            public readonly StringBuilder Csv = new StringBuilder(
                "tick,phase,s,barY,barZ,kneeY,kneeZ,hipZ,shoulderZ,anchorZ,shinGap,thighGap,shin3D,thigh3D,planShin,planThigh,planBarZ,visShin,visThigh,barLegN,footMin,shoulderDemand,abdomenDemand,thoraxDemand,hipDemand,kneeDemand,ankleDemand,rollDemand,rollReqL,rollActL,rollReqR,rollActR,regRollL,regRollR,acrossL,acrossTgtL,acrossR,acrossTgtR,footTravel,window,plateLN,plateRN,gripUpLN,gripUpRN,gripZLN,gripZRN,captureX,captureZ,captureMargin\n");

            public string Line() => string.Format(CultureInfo.InvariantCulture,
                "{0,-8} onset {1,4} | {2} | released {3} | separation {4} lockout {5} Down {6} returned {7} | bar peak rise {8:F1} mm (planned lockout rise {9:F1}) | min foot after break {10:F1} N, zero-point foot ticks {11}, feet travel {12:F4} m | min capture margin lifting {13:F1} mm | max bar-leg force {14:F1} N | ticks with a joint above its ceiling {15}",
                Arm, Onset, Summary, Released, Separation, LockoutTick, DownTick, ReturnedTick, Peak * 1000f, LockoutRise * 1000f,
                MinFoot, ZeroFootTicks, FootTravel, MinMargin * 1000f, MaxBarLegN, Saturated);
        }

        /// <summary>Leg vertices of the visible skinned mesh (dominant bone = thigh / shank), for the visual-vs-collider check.</summary>
        private sealed class VisibleLegs
        {
            private readonly PhysicalAthleteRig _rig;
            private readonly List<(SkinnedMeshRenderer smr, sbyte[] cls, Mesh baked)> _parts = new List<(SkinnedMeshRenderer, sbyte[], Mesh)>();
            private readonly MethodInfo _follow;
            private readonly List<Vector3> _v = new List<Vector3>();
            public bool Available => _parts.Count > 0;

            public VisibleLegs(PhysicalAthleteRig rig)
            {
                _rig = rig;
                _follow = typeof(PhysicalAthleteRig).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                Animator a = rig.VisibleAnimator;
                if (a == null) return;
                var cls = new Dictionary<Transform, sbyte>
                {
                    [a.GetBoneTransform(HumanBodyBones.LeftUpperLeg)] = 1, [a.GetBoneTransform(HumanBodyBones.RightUpperLeg)] = 1,
                    [a.GetBoneTransform(HumanBodyBones.LeftLowerLeg)] = 2, [a.GetBoneTransform(HumanBodyBones.RightLowerLeg)] = 2
                };
                foreach (SkinnedMeshRenderer smr in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Mesh m = smr.sharedMesh;
                    if (m == null) continue;
                    BoneWeight[] w = m.boneWeights;
                    if (w == null || w.Length != m.vertexCount) continue;
                    var c = new sbyte[w.Length];
                    bool any = false;
                    for (int i = 0; i < w.Length; i++)
                    {
                        int idx = w[i].boneIndex0;
                        if (idx >= 0 && idx < smr.bones.Length && smr.bones[idx] != null && cls.TryGetValue(smr.bones[idx], out sbyte k)) { c[i] = k; any = true; }
                    }
                    if (any) _parts.Add((smr, c, new Mesh()));
                }
            }

            /// <summary>Applies the rig's own visible follower (rotation-only bone copy) and measures the mesh gaps to the bar axis.</summary>
            public void Measure(Vector3 barCenter, float shaftRadius, float halfSpan, out float shin, out float thigh)
            {
                shin = thigh = float.NaN;
                if (!Available) return;
                _follow?.Invoke(_rig, null);
                float s = float.PositiveInfinity, t = float.PositiveInfinity;
                foreach ((SkinnedMeshRenderer smr, sbyte[] cls, Mesh baked) in _parts)
                {
                    smr.BakeMesh(baked, true);
                    baked.GetVertices(_v);
                    Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    for (int i = 0; i < _v.Count && i < cls.Length; i++)
                    {
                        if (cls[i] == 0) continue;
                        Vector3 w = m.MultiplyPoint3x4(_v[i]);
                        if (Mathf.Abs(w.x - barCenter.x) > halfSpan) continue;
                        float dy = w.y - barCenter.y, dz = w.z - barCenter.z;
                        float d = Mathf.Sqrt(dy * dy + dz * dz) - shaftRadius;
                        if (cls[i] == 1) t = Mathf.Min(t, d); else s = Mathf.Min(s, d);
                    }
                }
                shin = s; thigh = t;
            }
        }

        private static Vector3 Anchor(PhysicalAthleteRig rig, string child)
        {
            foreach (PhysicalAthleteRig.JointRuntime j in rig.Joints)
                if (j.Recipe.ChildId == child)
                    return j.Joint.transform.TransformPoint(j.Joint.anchor);
            throw new ArgumentException(child);
        }

        private static float Demand(PhysicalAthleteRig rig, params string[] ids)
        {
            float d = 0f;
            foreach (PoweredJointController.PoweredJointRuntime j in rig.PoweredController.Joints)
                if (j.HasPostPhysicsDiagnostic && Array.IndexOf(ids, j.Id) >= 0)
                    d = Mathf.Max(d, j.PostPhysicsDiagnostic.ModeledDemand);
            return d;
        }

        private static PoweredJointController.PoweredJointRuntime J(PhysicalAthleteRig rig, string id)
        {
            foreach (PoweredJointController.PoweredJointRuntime j in rig.PoweredController.Joints)
                if (j.Id == id) return j;
            throw new ArgumentException(id);
        }

        internal IEnumerator RunMeasured(int onsetTick, Record rec, bool proximity, bool measureVisual = true, bool preparation = false)
        {
            DeadliftAttemptRuntime.BarProximityPath = proximity;
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            var runtime = new DeadliftAttemptRuntime(_world.Foundation, _world.Rig, _world.Bar) { UpperChainPreparation = preparation };
            runtime.Initialize();
            DeadliftLiftPath path = runtime.Path;
            rec.Onset = onsetTick;
            rec.LockoutRise = path.LockoutRiseM;
            rec.PathReport = runtime.PathReport;
            rec.PathGeometry = path.GeometryReport();
            var planned = new DeadliftLiftPath.KeyGeometry[path.Keys.Count];
            for (int k = 0; k < planned.Length; k++) planned[k] = path.GeometryOf(path.Keys[k]);
            var visual = measureVisual ? new VisibleLegs(_world.Rig) : null;
            CapsuleCollider shaft = DeadliftBarLegGeometry.Shaft(runtime.Bar);
            float halfSpan = 0.5f * shaft.height;
            PhysicalAthleteRig rig = _world.Rig;
            Vector3 foot0L = rig.Segments["left_foot"].Body.position, foot0R = rig.Segments["right_foot"].Body.position;
            float rest = runtime.Bar.worldCenterOfMass.y;
            ulong releaseTick = 0;
            bool lowered = false;

            runtime.TickCompleted += rt =>
            {
                DeadliftPullPhase phase = rt.Pull.Phase;
                float s = rt.Pull.LiftProgress01;
                DeadliftBarLegGeometry.Gaps g = DeadliftBarLegGeometry.Realized(rig, rt.Bar, out DeadliftBarLegGeometry.Gaps g3);
                DeadliftBarLegGeometry.LiveCapsule(shaft, out Vector3 s0, out Vector3 s1, out float sr);
                Vector3 bar = 0.5f * (s0 + s1);
                Vector3 knee = 0.5f * (Anchor(rig, "left_shank") + Anchor(rig, "right_shank"));
                Vector3 hip = 0.5f * (Anchor(rig, "left_thigh") + Anchor(rig, "right_thigh"));
                Vector3 sh = 0.5f * (Anchor(rig, "left_upper_arm") + Anchor(rig, "right_upper_arm"));
                float anchorZ = 0.5f * (rig.Segments["left_hand"].Body.transform.TransformPoint(rt.LeftGrip.HandAnchorLocal).z
                                        + rig.Segments["right_hand"].Body.transform.TransformPoint(rt.RightGrip.HandAnchorLocal).z);
                path.KeySpan(s, out int i, out int j, out float t);
                float planShin = Mathf.Lerp(planned[i].Gaps.Shin, planned[j].Gaps.Shin, t);
                float planThigh = Mathf.Lerp(planned[i].Gaps.Thigh, planned[j].Gaps.Thigh, t);
                float planBarZ = Mathf.Lerp(planned[i].BarCenter.z, planned[j].BarCenter.z, t);
                float visShin = float.NaN, visThigh = float.NaN;
                if (visual != null && rt.Tick % 10 == 0)
                    visual.Measure(bar, sr, halfSpan, out visShin, out visThigh);
                float travel = Mathf.Max(Vector3.Distance(rig.Segments["left_foot"].Body.position, foot0L), Vector3.Distance(rig.Segments["right_foot"].Body.position, foot0R));
                rec.FootTravel = Mathf.Max(rec.FootTravel, travel);
                bool airborne = phase >= DeadliftPullPhase.Lifting && phase != DeadliftPullPhase.Returned || phase == DeadliftPullPhase.Separated;
                if (phase == DeadliftPullPhase.Lowering) lowered = true;
                Window w = phase == DeadliftPullPhase.Returned ? Window.Returned
                    : phase == DeadliftPullPhase.Lockout ? Window.Lockout
                    : phase == DeadliftPullPhase.Lowering ? (bar.y > knee.y + 0.03f ? Window.LoweringAbove : Window.LoweringBelow)
                    : phase == DeadliftPullPhase.Lifting || phase == DeadliftPullPhase.Separated
                        ? (bar.y < knee.y - 0.03f ? Window.BelowKnee : bar.y <= knee.y + 0.03f ? Window.KneePassage : Window.UpperThigh)
                    : Window.Setup;
                if (!rec.Windows.TryGetValue(w, out WindowStats ws)) rec.Windows[w] = ws = new WindowStats();
                ws.Ticks++;
                ws.ShinGap.Add(g.Shin); ws.ThighGap.Add(g.Thigh); ws.Shin3D.Add(g3.Shin); ws.Thigh3D.Add(g3.Thigh);
                ws.PlanShin.Add(planShin); ws.PlanThigh.Add(planThigh); ws.VisShin.Add(visShin); ws.VisThigh.Add(visThigh);
                ws.BarZ.Add(bar.z); ws.HipZ.Add(hip.z); ws.ShoulderZ.Add(sh.z); ws.AnchorZ.Add(anchorZ); ws.KneeZ.Add(knee.z); ws.PlanBarZ.Add(planBarZ);
                ws.BarLegN.Add(rt.BarOnLegN);
                float dSh = Demand(rig, "left_upper_arm", "right_upper_arm"), dAb = Demand(rig, "abdomen"), dTh = Demand(rig, "thorax");
                float dHip = Demand(rig, "left_thigh", "right_thigh"), dKn = Demand(rig, "left_shank", "right_shank"), dAn = Demand(rig, "left_foot", "right_foot");
                PoweredJointController.PoweredJointRuntime jl = J(rig, "left_foot"), jrr = J(rig, "right_foot");
                bool hasRoll = jl.HasPostPhysicsDiagnostic && jrr.HasPostPhysicsDiagnostic;
                float rollDemand = hasRoll ? Mathf.Max(jl.PostPhysicsDiagnostic.SecondaryDriveDemandFraction, jrr.PostPhysicsDiagnostic.SecondaryDriveDemandFraction) : float.NaN;
                float rollReqL = hasRoll ? jl.PostPhysicsDiagnostic.SecondaryRequestedAngleDeg : float.NaN, rollActL = hasRoll ? jl.PostPhysicsDiagnostic.SecondaryActualAngleDeg : float.NaN;
                float rollReqR = hasRoll ? jrr.PostPhysicsDiagnostic.SecondaryRequestedAngleDeg : float.NaN, rollActR = hasRoll ? jrr.PostPhysicsDiagnostic.SecondaryActualAngleDeg : float.NaN;
                ws.RollDemand.Add(rollDemand); ws.RollActual.Add(Mathf.Max(Mathf.Abs(rollActL), Mathf.Abs(rollActR)));
                ws.Shoulder.Add(dSh); ws.Abdomen.Add(dAb); ws.Thorax.Add(dTh); ws.Hip.Add(dHip); ws.Knee.Add(dKn); ws.Ankle.Add(dAn);
                bool saturated = false;
                foreach (PoweredJointController.PoweredJointRuntime jr in rig.PoweredController.Joints)
                    saturated |= jr.HasPostPhysicsDiagnostic && jr.PostPhysicsDiagnostic.ModeledDemand > 1f;
                if (airborne)
                {
                    if (saturated) { ws.Saturated++; rec.Saturated++; }
                    rec.MinFoot = Mathf.Min(rec.MinFoot, rt.FootLoadMinN);
                    rec.MaxBarLegN = Mathf.Max(rec.MaxBarLegN, rt.BarOnLegN);
                    if (rt.FootLoadMinN <= 0f) rec.ZeroFootTicks++;
                }
                if (phase == DeadliftPullPhase.Lifting)
                    rec.MinMargin = Mathf.Min(rec.MinMargin, rt.Monitor.MarginM);
                if (rec.Separation == 0 && rt.Pull.SeparationTick != 0) rec.Separation = rt.Pull.SeparationTick;
                if (phase == DeadliftPullPhase.Lockout && rec.LockoutTick == 0) rec.LockoutTick = rt.Tick;
                if (rt.Referee.DownGiven && rec.DownTick == 0) rec.DownTick = rt.Referee.DownTick;
                if (phase == DeadliftPullPhase.Returned && lowered && rec.ReturnedTick == 0) rec.ReturnedTick = rt.Tick;
                Vector3 gripL = rt.LeftGrip.ForceOnBarWorld, gripR = rt.RightGrip.ForceOnBarWorld;
                if (phase == DeadliftPullPhase.Pulling && rec.PullTick == 0)
                {
                    rec.PullTick = rt.Tick;
                    rec.FirstPull = FormattableString.Invariant(
                        $"pull {rt.Tick}, hands up {gripL.y:F1}/{gripR.y:F1} N, net Z {gripL.z + gripR.z:F1} N, plates {rt.PlateLeftN:F1}/{rt.PlateRightN:F1} N, shin gap {g.Shin * 1000f:F1} mm");
                }
                rec.Csv.AppendLine(string.Join(",", new[]
                {
                    rt.Tick.ToString(CultureInfo.InvariantCulture), phase.ToString(), Fm(s), Fm(bar.y), Fm(bar.z), Fm(knee.y), Fm(knee.z), Fm(hip.z), Fm(sh.z), Fm(anchorZ),
                    Fm(g.Shin), Fm(g.Thigh), Fm(g3.Shin), Fm(g3.Thigh), Fm(planShin), Fm(planThigh), Fm(planBarZ), Fm(visShin), Fm(visThigh),
                    Fm(rt.BarOnLegN), Fm(rt.FootLoadMinN), Fm(dSh), Fm(dAb), Fm(dTh), Fm(dHip), Fm(dKn), Fm(dAn), Fm(rollDemand), Fm(rollReqL), Fm(rollActL), Fm(rollReqR), Fm(rollActR),
                    Fm(rt.Regulator.RollOffsetDegFor("left_foot")), Fm(rt.Regulator.RollOffsetDegFor("right_foot")),
                    Fm(rt.Regulator.MeasuredAcrossFor("left_foot")), Fm(rt.Regulator.TargetAcrossFor("left_foot")),
                    Fm(rt.Regulator.MeasuredAcrossFor("right_foot")), Fm(rt.Regulator.TargetAcrossFor("right_foot")), Fm(travel), w.ToString(),
                    Fm(rt.PlateLeftN), Fm(rt.PlateRightN), Fm(gripL.y), Fm(gripR.y), Fm(gripL.z), Fm(gripR.z),
                    Fm(rt.Monitor.CapturePoint.x), Fm(rt.Monitor.CapturePoint.y), Fm(rt.Monitor.MarginM)
                }));
            };

            runtime.Pull.IntentOverride01 = 0f;
            for (int n = 1; n <= onsetTick + 1800; n++)
            {
                if (n == onsetTick)
                    runtime.Pull.IntentOverride01 = 1f;
                _world.Step(1);
                rec.Peak = Mathf.Max(rec.Peak, runtime.Bar.worldCenterOfMass.y - rest);
                if (runtime.GripsReleased && releaseTick == 0)
                    releaseTick = runtime.Tick;
                if (runtime.Referee.Completed && (releaseTick != 0 || !runtime.Referee.GoodLift) && runtime.Tick > releaseTick + 50)
                    break;
            }
            rec.Good = runtime.Referee.GoodLift;
            rec.Released = runtime.GripsReleased;
            rec.Down = runtime.Referee.DownGiven;
            rec.Summary = runtime.Referee.Summary();
            yield return _world.Teardown();
            _world = null;
            DeadliftAttemptRuntime.BarProximityPath = DeadliftAttemptRuntime.BarProximityPathDefault;
        }

        [UnityTest, Timeout(3600000), Explicit("GAM-70 matched complete attempts with upper-chain preparation")]
        public IEnumerator GAM70_UPPER_CHAIN_PREPARATION_AB()
        {
            var report = new StringBuilder();
            var results = new List<Record>();
            foreach (bool proximity in new[] { false, true })
            {
                foreach (bool preparation in new[] { false, true })
                {
                    var r = new Record { Arm = (proximity ? "proximity" : "original") + (preparation ? "-prepared" : "-control") };
                    yield return RunMeasured(301, r, proximity, measureVisual: false, preparation: preparation);
                    results.Add(r);
                    report.AppendLine(r.Line()).AppendLine(r.FirstPull).AppendLine(WindowTable(r));
                    System.IO.File.WriteAllText(System.IO.Path.Combine(F.EvidenceDirectory, "upper-chain-" + r.Arm + ".csv"), r.Csv.ToString());
                    System.IO.File.WriteAllText(System.IO.Path.Combine(F.EvidenceDirectory, "upper-chain-attempts.txt"), report.ToString());
                }
            }
            Debug.Log(report.ToString());
            Assert.That(results[0].Good && results[0].Released, Is.True, results[0].Line());
            Assert.That(results[2].Good, Is.False, results[2].Line());
            foreach (Record prepared in new[] { results[1], results[3] })
            {
                Assert.That(prepared.PullTick, Is.GreaterThan(301ul), prepared.Line());
                Assert.That(prepared.Separation, Is.GreaterThan(prepared.PullTick), prepared.Line());
            }
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 control matched to the prepared pull handoff")]
        public IEnumerator GAM70_UPPER_CHAIN_HANDOFF_TICK_CONTROL()
        {
            foreach (bool proximity in new[] { false, true })
            {
                var r = new Record { Arm = proximity ? "proximity-handoff-control" : "original-handoff-control" };
                // The prepared plants both first accepted Drive at tick 318.
                yield return RunMeasured(318, r, proximity, measureVisual: false);
                Debug.Log(r.Line() + Environment.NewLine + r.FirstPull + Environment.NewLine + WindowTable(r));
                System.IO.File.WriteAllText(System.IO.Path.Combine(F.EvidenceDirectory, "upper-chain-" + r.Arm + ".csv"), r.Csv.ToString());
                Assert.That(r.PullTick, Is.EqualTo(318ul));
                Assert.That(r.Separation, Is.GreaterThan(r.PullTick), r.Line());
            }
        }

        private static string Fm(float v) => float.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
        private static string Mm(Stat s) => s.N == 0 ? "     -      " : string.Format(CultureInfo.InvariantCulture, "{0,5:F0}/{1,5:F0}", s.Min * 1000f, s.Mean * 1000f);
        private static string Z(Stat s) => s.N == 0 ? "   -   " : s.Mean.ToString("+0.000;-0.000", CultureInfo.InvariantCulture);
        private static string D(Stat s) => s.N == 0 ? "  -  " : s.Max.ToString("F2", CultureInfo.InvariantCulture);

        internal static string WindowTable(Record r)
        {
            var b = new StringBuilder();
            b.AppendLine("[" + r.Arm + "] realized vs planned bar-leg surface gaps by phase (min/mean mm; sagittal capsule gap; 3D = shaft-capsule check; visual = skinned mesh, every 10th tick)");
            b.AppendLine("window          ticks | shin real  shin plan  shin 3D   shin vis  | thigh real thigh plan thigh 3D  thigh vis | mean Z (m): bar   plan   anchor  shldr   hip    knee  | max bar-leg N | max demand sh/abd/thx/hip/knee/ankle/roll | max |roll| deg | sat");
            foreach (Window w in Enum.GetValues(typeof(Window)))
            {
                if (!r.Windows.TryGetValue(w, out WindowStats s)) continue;
                b.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-14} {1,6} | {2} {3} {4} {5} | {6} {7} {8} {9} | {10} {11} {12} {13} {14} {15} | {16,8:F1} | {17}/{18}/{19}/{20}/{21}/{22}/{24} | {25,5:F1} | {23}",
                    w, s.Ticks, Mm(s.ShinGap), Mm(s.PlanShin), Mm(s.Shin3D), Mm(s.VisShin), Mm(s.ThighGap), Mm(s.PlanThigh), Mm(s.Thigh3D), Mm(s.VisThigh),
                    Z(s.BarZ), Z(s.PlanBarZ), Z(s.AnchorZ), Z(s.ShoulderZ), Z(s.HipZ), Z(s.KneeZ), s.BarLegN.Max,
                    D(s.Shoulder), D(s.Abdomen), D(s.Thorax), D(s.Hip), D(s.Knee), D(s.Ankle), s.Saturated, D(s.RollDemand), s.RollActual.Max));
            }
            return b.ToString();
        }

        /// <summary>
        /// Planner-only exploration of the bar-proximity path on the shipped plant's measurements:
        /// GAM70_PROX_SWEEP = "thighW,shinW,rollLimitDeg;..." (default: the shipped values).
        /// </summary>
        [UnityTest, Timeout(1800000), Explicit("GAM-70 bar-proximity path solve sweep; run on demand")]
        public IEnumerator GAM70_PROXIMITY_PATH_SOLVE()
        {
            DeadliftAttemptRuntime.BarProximityPath = false;
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            var runtime = new DeadliftAttemptRuntime(_world.Foundation, _world.Rig, _world.Bar);
            runtime.Initialize();
            var probe = new DeadliftLiftPath(_world.Rig, runtime.Measurements, runtime.Model, _world.Bar.Body.mass);
            string sweep = Environment.GetEnvironmentVariable("GAM70_PROX_SWEEP");
            if (string.IsNullOrEmpty(sweep))
                sweep = string.Join(",", probe.ThighProximityWeight.ToString(CultureInfo.InvariantCulture), probe.ShinProximityWeight.ToString(CultureInfo.InvariantCulture), probe.RollPlanLimitDeg.ToString(CultureInfo.InvariantCulture));
            var report = new StringBuilder("GAM-70 bar-proximity path solve sweep\n");
            report.AppendLine("=== control (Gate 6 path)").AppendLine(runtime.Path.GeometryReport()).AppendLine(runtime.Path.Describe());
            foreach (string c in sweep.Split(';'))
            {
                string[] v = c.Split(',');
                var path = new DeadliftLiftPath(_world.Rig, runtime.Measurements, runtime.Model, _world.Bar.Body.mass)
                {
                    BarProximity = true,
                    ThighProximityWeight = float.Parse(v[0], CultureInfo.InvariantCulture),
                    ShinProximityWeight = float.Parse(v[1], CultureInfo.InvariantCulture),
                    RollPlanLimitDeg = float.Parse(v[2], CultureInfo.InvariantCulture)
                };
                if (v.Length > 3) path.BarVerticalWeight = float.Parse(v[3], CultureInfo.InvariantCulture);
                float t0 = Time.realtimeSinceStartup;
                path.Solve();
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "=== thighW {0} shinW {1} roll limit {2} deg: solve {3:F1} s", v[0], v[1], v[2], Time.realtimeSinceStartup - t0));
                report.AppendLine(path.GeometryReport());
                report.AppendLine(path.Describe());
                F.WriteEvidence("prox-path-sweep.txt", report.ToString());
            }
            Assert.Pass(report.ToString());
        }

        /// <summary>
        /// Diagnosis and controlled A/B: the shipped attempt's planned and realized bar-shin / bar-thigh gaps,
        /// body landmarks (world Z), visible-mesh check, demands and support, on matched fresh plants with the
        /// same Drive onset. GAM70_PROXIMITY: "0" control only, "1" proximity only, otherwise both.
        /// </summary>
        [UnityTest, Timeout(3600000), Explicit("GAM-70 bar-leg geometry diagnosis and A/B; run on demand")]
        public IEnumerator GAM70_BAR_LEG_GEOMETRY_PLANNED_AND_REALIZED()
        {
            string arms = Environment.GetEnvironmentVariable("GAM70_PROXIMITY");
            int onset = int.TryParse(Environment.GetEnvironmentVariable("GAM70_PROX_ONSET"), out int o) ? o : 301;
            var report = new StringBuilder("GAM-70 bar-leg geometry, shipped attempt, fresh plants, Drive onset " + onset).AppendLine();
            var tables = new StringBuilder();
            foreach (bool proximity in new[] { false, true })
            {
                if (arms == "0" && proximity || arms == "1" && !proximity) continue;
                var r = new Record { Arm = proximity ? "proximity" : "control" };
                yield return RunMeasured(onset, r, proximity);
                report.AppendLine(r.Line());
                tables.AppendLine(WindowTable(r)).AppendLine(r.PathGeometry).AppendLine(r.PathReport);
                F.WriteEvidence("prox-trace-" + r.Arm + ".csv", r.Csv.ToString());
                F.WriteEvidence("prox-geometry-ab.txt", report.ToString() + Environment.NewLine + tables);
            }
            Assert.Pass(report.ToString());
        }

        /// <summary>
        /// The Gate 8 five pull-onset robustness cases (fresh plants, shipped lift rate and acceleration)
        /// on both ascent paths, with the realized lockout bar-thigh gap of each completed lockout.
        /// </summary>
        [UnityTest, Timeout(7200000), Explicit("GAM-70 bar-proximity onset robustness A/B; run on demand")]
        public IEnumerator GAM70_BAR_PROXIMITY_ONSET_ROBUSTNESS_AB()
        {
            var report = new StringBuilder("GAM-70 bar proximity: Drive-onset robustness A/B (fresh plants; 0.30 /s, 0.6 /s2 on the Gate 6 path, same joint speed on the proximity path)").AppendLine();
            int[] good = new int[2];
            foreach (int onset in new[] { 301, 302, 305, 320, 350 })
                foreach (bool proximity in new[] { false, true })
                {
                    var r = new Record { Arm = proximity ? "proximity" : "control" };
                    yield return RunMeasured(onset, r, proximity, measureVisual: false);
                    if (r.Good) good[proximity ? 1 : 0]++;
                    string lockGap = r.Windows.TryGetValue(Window.Lockout, out WindowStats ls)
                        ? string.Format(CultureInfo.InvariantCulture, " | lockout thigh gap min/mean {0:F0}/{1:F0} mm", ls.ThighGap.Min * 1000f, ls.ThighGap.Mean * 1000f) : " | no lockout";
                    report.AppendLine(r.Line() + lockGap);
                    F.WriteEvidence("prox-onset-robustness.txt", report.ToString());
                }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "good lifts: control {0}/5, proximity {1}/5", good[0], good[1]));
            F.WriteEvidence("prox-onset-robustness.txt", report.ToString());
            Assert.Pass(report.ToString());
        }
    }
}
