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
    /// <summary>
    /// Phase 5H14, sections 4 to 10.
    ///
    /// H13 left both numeric squat gates passing and the rendered movement
    /// unacceptable: the trunk folds past the reference and the bar migrates
    /// forward. The H13 receipt also recorded the support interval shrinking
    /// from about 290 mm at s_q 0.55 to about 38 mm at 0.80, which is the
    /// whole plantar length down to an eighth of it.
    ///
    /// Nothing in that number says the heel left the platform. The support
    /// interval is built from engine contact points alone, and a contact
    /// widens it whether or not it carries any impulse, so a collapse means
    /// the solver stopped generating contacts back there, not merely that the
    /// heel stopped being loaded. Whether the geometry actually separated is a
    /// different measurement, and this takes it directly: witness points
    /// derived from the real foot collider, their clearance above the real
    /// platform, and the impulse each half of each foot carries.
    ///
    /// One trace per load carries everything both reports need, so the plantar
    /// ordering and the trunk decomposition are read off the same run rather
    /// than two runs that might not match.
    /// </summary>
    public sealed class PlantarSupportAndTrunkFoldTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";
        private const string PlatformName = "PhysicalPlatform_GAM6";
        private const float ApproachPhaseRate = 0.30f;

        /// <summary>
        /// Reference world trunk pitch on the accepted GAM-10 pose, sampled in
        /// the reference scene. Provenance:
        /// Artifacts/Measurements/GAM-11/GAM11-5h12-r1-reference-com-trajectory.csv,
        /// column reference_world_trunk_pitch_deg. Held here because the
        /// physical scene cannot evaluate it and the comparison has to be
        /// against the same definition the physical side uses, which is the
        /// pelvis-to-thorax axis.
        /// </summary>
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
        private SquatPhysicalPrototypeController _controller;
        private float _platformTopY;
        private float _contactOffsetM;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_rig, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            _bootstrap.enabled = false;

            GameObject platform = GameObject.Find(PlatformName);
            Assert.That(platform, Is.Not.Null, "The support platform was not found.");
            Collider platformCollider = platform.GetComponent<Collider>();
            Assert.That(platformCollider, Is.Not.Null);
            _platformTopY = platformCollider.bounds.max.y;

            // The tolerance every geometric clearance verdict is stated
            // against. Beyond a collider's contact offset the solver cannot
            // generate a contact at all, so this is the engine's own
            // separation scale rather than a chosen number.
            Collider footCollider = _rig.Segments["left_foot"].Collider;
            _contactOffsetM = footCollider.contactOffset;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = true;
            if (_controller != null)
                _controller.enabled = false;
            if (_rig != null)
                _rig.enabled = false;
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
            _controller = null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------
        // Foot witness geometry, section 4.
        // ------------------------------------------------------------------

        private struct FootWitness
        {
            public Vector3 HeelWorld;
            public Vector3 ToeWorld;
            public float HeelLocalAp;
            public float ToeLocalAp;
            public float CenterLocalAp;
            public float PlantarLocalY;
        }

        /// <summary>
        /// Posterior-most and anterior-most plantar witness points, taken from
        /// the actual runtime box collider rather than placed by eye: the
        /// plantar plane is the collider's lower face and the two witnesses are
        /// its posterior and anterior ends on the foot centreline.
        /// </summary>
        private FootWitness Witness(string footId)
        {
            var box = (BoxCollider)_rig.Segments[footId].Collider;
            Vector3 c = box.center;
            Vector3 h = 0.5f * box.size;
            Transform t = box.transform;

            var w = new FootWitness
            {
                HeelLocalAp = c.z - h.z,
                ToeLocalAp = c.z + h.z,
                CenterLocalAp = c.z,
                PlantarLocalY = c.y - h.y
            };
            w.HeelWorld = t.TransformPoint(new Vector3(c.x, w.PlantarLocalY, w.HeelLocalAp));
            w.ToeWorld = t.TransformPoint(new Vector3(c.x, w.PlantarLocalY, w.ToeLocalAp));
            return w;
        }

        // ------------------------------------------------------------------
        // The recorded trace.
        // ------------------------------------------------------------------

        private struct Row
        {
            public int Tick;
            public float Sq;

            public float LeftHeelClearance;
            public float LeftToeClearance;
            public float RightHeelClearance;
            public float RightToeClearance;
            public float LeftFootPitch;
            public float RightFootPitch;

            public float PosteriorImpulse;
            public float AnteriorImpulse;
            public float TotalImpulse;
            public float PosteriorContactAp;
            public float AnteriorContactAp;
            public int ContactCount;
            public int MaxPerFootContacts;

            public float SupportMin;
            public float SupportMax;
            public float SupportLength;
            public float SupportCenter;
            public float CopAp;
            public bool HasCop;
            public float CopFraction;

            public float AnkleNominalDeg;
            public float AnkleGravityDeg;
            public float AnkleBalanceDeg;
            public float AnkleFinalDeg;
            public float AnkleActualDeg;
            public float RawAnkleDemandDeg;
            public float AppliedAnkleDeg;

            public float ComAp;
            public float ComVelAp;
            public float ComRefAp;

            public float WorldTrunkPitch;
            public float ReferenceWorldTrunkPitch;
            public float PelvisWorldPitch;
            public float ThighWorldPitch;
            public float ShankWorldPitch;

            public float HipNominalDeg;
            public float HipActualDeg;
            public float AbdomenNominalDeg;
            public float AbdomenActualDeg;
            public float ThoraxNominalDeg;
            public float ThoraxActualDeg;

            public float BarAp;
            public float BarRelSupportCenter;
            public float BarRelCop;
            public float SaddleSeparation;
            public bool SaddleAttached;

            public float SlipLeft;
            public float SlipRight;
        }

        private IEnumerator RecordDescent(float loadKg, List<Row> rows)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController bc = adapter.BalanceController;
            PhysicalBarbell barbell = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            Rigidbody thorax = _rig.Segments["thorax"].Body;
            Rigidbody thigh = _rig.Segments["left_thigh"].Body;
            Rigidbody shank = _rig.Segments["left_shank"].Body;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.BalanceCorrectionsEnabled = true;
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            float sq = 0f;
            int tick = 0;
            while (tick < 420)
            {
                sq = Mathf.Min(1f, sq + ApproachPhaseRate * dt);
                SquatState state = sq >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
                adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
                tick++;

                FootWitness left = Witness("left_foot");
                FootWitness right = Witness("right_foot");

                var row = new Row
                {
                    Tick = tick,
                    Sq = sq,
                    LeftHeelClearance = left.HeelWorld.y - _platformTopY,
                    LeftToeClearance = left.ToeWorld.y - _platformTopY,
                    RightHeelClearance = right.HeelWorld.y - _platformTopY,
                    RightToeClearance = right.ToeWorld.y - _platformTopY,
                    LeftFootPitch = WitnessPitch(left),
                    RightFootPitch = WitnessPitch(right),
                    SupportMin = balance.SupportApMin,
                    SupportMax = balance.SupportApMax,
                    SupportLength = balance.SupportApLength,
                    SupportCenter = balance.SupportApCenter,
                    HasCop = balance.HasCopEstimate,
                    CopAp = balance.HasCopEstimate ? balance.CopEstimate.z : balance.SupportApCenter,
                    ContactCount = balance.SupportContactCount,
                    ComAp = balance.SystemCom.z,
                    ComVelAp = balance.SystemComVelocity.z,
                    ComRefAp = bc.ComRefAp,
                    RawAnkleDemandDeg = bc.RawAnkleSagittalOffsetRad * Mathf.Rad2Deg,
                    AppliedAnkleDeg = bc.AnkleSagittalOffsetRad * Mathf.Rad2Deg,
                    WorldTrunkPitch = WorldPitch(pelvis.position, thorax.position),
                    ReferenceWorldTrunkPitch = SampleReferenceTrunkPitch(sq),
                    PelvisWorldPitch = SegmentWorldPitch(pelvis),
                    ThighWorldPitch = SegmentWorldPitch(thigh),
                    ShankWorldPitch = SegmentWorldPitch(shank),
                    SlipLeft = _controller.LeftFootContact != null ? _controller.LeftFootContact.SlipSpeed : 0f,
                    SlipRight = _controller.RightFootContact != null ? _controller.RightFootContact.SlipSpeed : 0f
                };

                AccumulatePlantar(_controller.LeftFootContact, "left_foot", ref row);
                AccumulatePlantar(_controller.RightFootContact, "right_foot", ref row);

                row.CopFraction = row.SupportLength > 1e-5f
                    ? (row.CopAp - row.SupportMin) / row.SupportLength
                    : float.NaN;

                ReadJoint(adapter, powered, "left_foot", out row.AnkleNominalDeg, out row.AnkleGravityDeg,
                    out row.AnkleBalanceDeg, out row.AnkleFinalDeg, out row.AnkleActualDeg);
                ReadJoint(adapter, powered, "left_thigh", out _, out _, out _, out _, out row.HipActualDeg);
                row.HipNominalDeg = NominalOf(adapter, "left_thigh");
                ReadJoint(adapter, powered, "abdomen", out _, out _, out _, out _, out row.AbdomenActualDeg);
                row.AbdomenNominalDeg = NominalOf(adapter, "abdomen");
                ReadJoint(adapter, powered, "thorax", out _, out _, out _, out _, out row.ThoraxActualDeg);
                row.ThoraxNominalDeg = NominalOf(adapter, "thorax");

                if (barbell != null && barbell.Body != null)
                {
                    row.BarAp = barbell.Body.worldCenterOfMass.z;
                    row.BarRelSupportCenter = row.BarAp - row.SupportCenter;
                    row.BarRelCop = row.BarAp - row.CopAp;
                }
                SquatBarSaddle saddle = _controller.Saddle;
                if (saddle != null)
                {
                    row.SaddleAttached = saddle.IsAttached;
                    row.SaddleSeparation = saddle.SaddleSeparationMeters;
                }

                rows.Add(row);
            }
            yield return null;
        }

        /// <summary>
        /// Foot pitch taken from the two plantar witnesses themselves, so it
        /// is the same geometry the clearances are measured on. Positive is
        /// toe above heel.
        /// </summary>
        private static float WitnessPitch(FootWitness w)
        {
            float dy = w.ToeWorld.y - w.HeelWorld.y;
            float dz = w.ToeWorld.z - w.HeelWorld.z;
            return Mathf.Atan2(dy, Mathf.Abs(dz)) * Mathf.Rad2Deg;
        }

        private static float WorldPitch(Vector3 lower, Vector3 upper)
        {
            Vector3 axis = upper - lower;
            return axis.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg;
        }

        private static float SegmentWorldPitch(Rigidbody body)
        {
            Vector3 up = body.transform.TransformDirection(Vector3.up);
            return Mathf.Atan2(up.z, up.y) * Mathf.Rad2Deg;
        }

        private void AccumulatePlantar(PhysicalFootContactDetector detector, string footId, ref Row row)
        {
            if (detector == null)
                return;
            var box = (BoxCollider)_rig.Segments[footId].Collider;
            Transform t = box.transform;
            float centerAp = box.center.z;

            int count = detector.CompletedContactCount;
            row.MaxPerFootContacts = Mathf.Max(row.MaxPerFootContacts, count);
            for (int index = 0; index < count; index++)
            {
                Vector3 world = detector.CompletedContactPoint(index);
                float impulse = detector.CompletedNormalImpulse(index);
                float localAp = t.InverseTransformPoint(world).z;

                if (localAp < centerAp)
                    row.PosteriorImpulse += impulse;
                else
                    row.AnteriorImpulse += impulse;
                row.TotalImpulse += impulse;

                row.PosteriorContactAp = row.PosteriorContactAp == 0f
                    ? localAp
                    : Mathf.Min(row.PosteriorContactAp, localAp);
                row.AnteriorContactAp = row.AnteriorContactAp == 0f
                    ? localAp
                    : Mathf.Max(row.AnteriorContactAp, localAp);
            }
        }

        private static void ReadJoint(
            SquatPhysicalAdapter adapter, PoweredJointController powered, string jointId,
            out float nominal, out float gravity, out float balance, out float final, out float actual)
        {
            nominal = gravity = balance = final = actual = 0f;
            if (!adapter.TryGetTargetComposition(jointId, out var c))
                return;
            nominal = TwistX(c.Nominal);
            gravity = TwistX(c.GravityBias);
            balance = TwistX(c.BalanceOffset);
            final = TwistX(c.Final);
            PoweredJointController.PoweredJointRuntime joint = powered.GetJoint(jointId);
            if (joint != null)
                actual = TwistX(joint.Diagnostic.ActualRelative);
        }

        private static float NominalOf(SquatPhysicalAdapter adapter, string jointId) =>
            adapter.TryGetTargetComposition(jointId, out var c) ? TwistX(c.Nominal) : 0f;

        private static float SampleReferenceTrunkPitch(float sq)
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

        // ------------------------------------------------------------------
        // T1. Plantar support and causal ordering, sections 4 to 7.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator T1_PLANTAR_SUPPORT_AND_CAUSAL_ORDERING()
        {
            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H14 T1 PLANTAR SUPPORT THROUGH THE DESCENT");
            report.AppendLine();
            report.AppendLine(GeometryPreamble());

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,tick,sq,left_heel_clearance,left_toe_clearance,right_heel_clearance," +
                           "right_toe_clearance,left_foot_pitch,right_foot_pitch,posterior_impulse," +
                           "anterior_impulse,total_impulse,posterior_fraction,posterior_contact_ap," +
                           "anterior_contact_ap,contacts,max_per_foot_contacts,support_min,support_max," +
                           "support_length,cop_ap,cop_fraction,has_cop,raw_ankle_deg,applied_ankle_deg," +
                           "ankle_nominal_deg,ankle_balance_deg,ankle_final_deg,ankle_actual_deg," +
                           "com_ap,com_ref_ap,world_trunk_pitch,ref_world_trunk_pitch,slip_left,slip_right");

            foreach (float load in new[] { 0f, 25f })
            {
                var rows = new List<Row>();
                yield return RecordDescent(load, rows);
                EmitCsv(load, rows, csv);
                report.AppendLine(PlantarSection(load, rows));
            }

            WriteMeasurement("GAM11-5h14-t1-plantar-support.txt", report.ToString());
            WriteMeasurement("GAM11-5h14-t1-plantar-support.csv", csv.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        private string GeometryPreamble()
        {
            FootWitness left = Witness("left_foot");
            FootWitness right = Witness("right_foot");
            var box = (BoxCollider)_rig.Segments["left_foot"].Collider;
            var text = new StringBuilder();
            text.AppendLine("Foot witness points derived from the runtime box collider, not placed by eye.");
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  collider size {0}  center {1}  contactOffset {2:F4} m",
                box.size, box.center, _contactOffsetM));
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  LEFT_HEEL_WITNESS  local AP {0:F4}   RIGHT_HEEL_WITNESS  local AP {1:F4}",
                left.HeelLocalAp, right.HeelLocalAp));
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  LEFT_TOE_WITNESS   local AP {0:F4}   RIGHT_TOE_WITNESS   local AP {1:F4}",
                left.ToeLocalAp, right.ToeLocalAp));
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  plantar plane local Y {0:F4}   plantar AP length {1:F4} m   platform top Y {2:F4}",
                left.PlantarLocalY, left.ToeLocalAp - left.HeelLocalAp, _platformTopY));
            text.AppendLine();
            text.AppendLine("Clearance verdicts are stated against the collider's own contactOffset:");
            text.AppendLine("beyond that separation the solver cannot generate a contact there at all.");
            return text.ToString();
        }

        private string PlantarSection(float loadKg, List<Row> rows)
        {
            var text = new StringBuilder();
            text.AppendLine("--- load " + loadKg.ToString("F0", CultureInfo.InvariantCulture) + " kg ---");
            text.AppendLine();
            text.AppendLine("  s_q   supportLen  heelClrL  heelClrR  footPitchL  postImp  antImp  postFrac  contacts  copFrac  trunkErr");
            foreach (float phase in new[] { 0.00f, 0.20f, 0.40f, 0.55f, 0.65f, 0.70f, 0.75f, 0.80f, 0.90f, 1.00f })
            {
                Row r = Nearest(rows, phase);
                float postFrac = r.TotalImpulse > 1e-6f ? r.PosteriorImpulse / r.TotalImpulse : float.NaN;
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,4:F2}  {1,10:F4}  {2,8:F4}  {3,8:F4}  {4,10:F2}  {5,7:F3}  {6,6:F3}  {7,8:F3}  {8,8}  {9,7:F3}  {10,8:F2}",
                    r.Sq, r.SupportLength, r.LeftHeelClearance, r.RightHeelClearance, r.LeftFootPitch,
                    r.PosteriorImpulse, r.AnteriorImpulse, postFrac, r.ContactCount, r.CopFraction,
                    r.WorldTrunkPitch - r.ReferenceWorldTrunkPitch));
            }

            // Events. Each is stated with the rule that produced it.
            int rearfootUnload = FirstTick(rows, r => r.TotalImpulse > 1e-6f && r.PosteriorImpulse <= 1e-6f);
            int heelLift = FirstTick(rows, r =>
                r.LeftHeelClearance > _contactOffsetM && r.RightHeelClearance > _contactOffsetM);
            float standingSpan = rows.Count > 0 ? Nearest(rows, 0.02f).SupportLength : 0f;
            int spanCollapse = FirstTick(rows, r => r.SupportLength < 0.5f * standingSpan);
            int trunkDiverge = FirstTick(rows, r => r.WorldTrunkPitch - r.ReferenceWorldTrunkPitch > 15f);
            int barMigration = FirstTick(rows, r => r.BarRelSupportCenter > 0f);
            int copEdge = FirstTick(rows, r => r.HasCop && r.SupportMax - r.CopAp <
                SquatPredictiveBalanceController.SupportInteriorMarginM);

            text.AppendLine();
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  standing support span {0:F4} m", standingSpan));
            text.AppendLine("  FIRST_REARFOOT_UNLOAD_TICK        = " + Describe(rows, rearfootUnload) +
                            "   (posterior half carries no normal impulse while the foot still does)");
            text.AppendLine("  FIRST_GEOMETRIC_HEEL_LIFT_TICK    = " + Describe(rows, heelLift) +
                            "   (both heel witnesses clear the platform by more than contactOffset)");
            text.AppendLine("  FIRST_SUPPORT_SPAN_COLLAPSE_TICK  = " + Describe(rows, spanCollapse) +
                            "   (span below half the measured standing span; diagnostic bisection)");
            text.AppendLine("  FIRST_TRUNK_DIVERGENCE_TICK       = " + Describe(rows, trunkDiverge) +
                            "   (world trunk pitch exceeds reference by 15 deg, the H12 criterion)");
            text.AppendLine("  FIRST_BAR_FORWARD_MIGRATION_TICK  = " + Describe(rows, barMigration) +
                            "   (bar centre of mass anterior of the support centre)");
            text.AppendLine("  FIRST_COP_FOREFOOT_EDGE_TICK      = " + Describe(rows, copEdge) +
                            "   (COP within the controller's own 30 mm interior margin of the front edge)");

            text.AppendLine();
            text.AppendLine("  " + Classify(rows, rearfootUnload, heelLift, spanCollapse));
            text.AppendLine();
            return text.ToString();
        }

        /// <summary>
        /// Section 7. The discriminating question is whether the plantar
        /// geometry actually separated at the moment the engine's support
        /// interval collapsed. If the span collapses while both heel witnesses
        /// are still within contact range of the platform, the interval is
        /// reporting something the geometry does not support.
        /// </summary>
        private string Classify(List<Row> rows, int rearfootUnload, int heelLift, int spanCollapse)
        {
            if (spanCollapse < 0)
                return "PLANTAR_SUPPORT_CLASS = FULL_FOOT_SUPPORT (the span never halved)";

            Row atCollapse = rows[spanCollapse];
            bool heelStillDown = atCollapse.LeftHeelClearance <= _contactOffsetM ||
                                 atCollapse.RightHeelClearance <= _contactOffsetM;

            string detail = string.Format(CultureInfo.InvariantCulture,
                "at the collapse tick heel clearance is L {0:F4} R {1:F4} m against a contactOffset of {2:F4}, " +
                "foot pitch L {3:F2} deg, posterior impulse {4:F3} of {5:F3} total",
                atCollapse.LeftHeelClearance, atCollapse.RightHeelClearance, _contactOffsetM,
                atCollapse.LeftFootPitch, atCollapse.PosteriorImpulse, atCollapse.TotalImpulse);

            if (heelStillDown && (heelLift < 0 || heelLift > spanCollapse))
                return "PLANTAR_SUPPORT_CLASS = CONTACT_REPORTING_COLLAPSE — " + detail;
            if (heelLift >= 0 && heelLift <= spanCollapse)
                return "PLANTAR_SUPPORT_CLASS = REAL_PHYSICAL_HEEL_LIFT — " + detail;
            if (rearfootUnload >= 0 && rearfootUnload < spanCollapse)
                return "PLANTAR_SUPPORT_CLASS = REARFOOT_UNLOADED — " + detail;
            return "PLANTAR_SUPPORT_CLASS = OTHER — " + detail;
        }

        // ------------------------------------------------------------------
        // T2. Trunk decomposition and bar path, sections 8 to 10.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator T2_TRUNK_AND_BAR_PATH_DECOMPOSITION()
        {
            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H14 T2 TRUNK DECOMPOSITION AND BAR PATH");
            report.AppendLine("Joint angles are the calibrated flexion axis in joint space. World pitches");
            report.AppendLine("are measured on the physical bodies. The reference world trunk pitch is the");
            report.AppendLine("accepted GAM-10 pose sampled in the reference scene, same pelvis-to-thorax");
            report.AppendLine("definition. A forward lean matching the reference is not a defect; only the");
            report.AppendLine("excess over it is.");
            report.AppendLine();

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,tick,sq,world_trunk_pitch,ref_world_trunk_pitch,trunk_excess," +
                           "pelvis_world_pitch,thigh_world_pitch,shank_world_pitch," +
                           "hip_nominal,hip_actual,hip_excess,abdomen_nominal,abdomen_actual,abdomen_excess," +
                           "thorax_nominal,thorax_actual,thorax_excess," +
                           "bar_ap,support_center,cop_ap,bar_rel_support,bar_rel_cop," +
                           "saddle_separation,saddle_attached,com_ap,support_min,support_max");

            foreach (float load in new[] { 0f, 25f })
            {
                var rows = new List<Row>();
                yield return RecordDescent(load, rows);

                foreach (Row r in rows)
                {
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F0},{1},{2:F4},{3:F3},{4:F3},{5:F3},{6:F3},{7:F3},{8:F3}," +
                        "{9:F3},{10:F3},{11:F3},{12:F3},{13:F3},{14:F3},{15:F3},{16:F3},{17:F3}," +
                        "{18:F5},{19:F5},{20:F5},{21:F5},{22:F5},{23:F5},{24},{25:F5},{26:F5},{27:F5}",
                        load, r.Tick, r.Sq,
                        r.WorldTrunkPitch, r.ReferenceWorldTrunkPitch,
                        r.WorldTrunkPitch - r.ReferenceWorldTrunkPitch,
                        r.PelvisWorldPitch, r.ThighWorldPitch, r.ShankWorldPitch,
                        r.HipNominalDeg, r.HipActualDeg, r.HipActualDeg - r.HipNominalDeg,
                        r.AbdomenNominalDeg, r.AbdomenActualDeg, r.AbdomenActualDeg - r.AbdomenNominalDeg,
                        r.ThoraxNominalDeg, r.ThoraxActualDeg, r.ThoraxActualDeg - r.ThoraxNominalDeg,
                        r.BarAp, r.SupportCenter, r.CopAp, r.BarRelSupportCenter, r.BarRelCop,
                        r.SaddleSeparation, r.SaddleAttached ? 1 : 0,
                        r.ComAp, r.SupportMin, r.SupportMax));
                }

                report.AppendLine("--- load " + load.ToString("F0", CultureInfo.InvariantCulture) + " kg ---");
                report.AppendLine();
                report.AppendLine("  s_q   trunkAct  trunkRef  excess |  hipNom  hipAct  hipExc | abdNom  abdAct  abdExc |" +
                                  " thoNom  thoAct  thoExc |  barRelSup  barRelCop  saddleSep");
                foreach (float phase in new[] { 0.00f, 0.40f, 0.55f, 0.70f, 0.80f, 0.90f, 1.00f })
                {
                    Row r = Nearest(rows, phase);
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,4:F2}  {1,8:F2}  {2,8:F2}  {3,6:F2} | {4,6:F2}  {5,6:F2}  {6,6:F2} | {7,6:F2}  {8,6:F2}  {9,6:F2} |" +
                        " {10,6:F2}  {11,6:F2}  {12,6:F2} |  {13,9:F4}  {14,9:F4}  {15,9:F4}",
                        r.Sq, r.WorldTrunkPitch, r.ReferenceWorldTrunkPitch,
                        r.WorldTrunkPitch - r.ReferenceWorldTrunkPitch,
                        r.HipNominalDeg, r.HipActualDeg, r.HipActualDeg - r.HipNominalDeg,
                        r.AbdomenNominalDeg, r.AbdomenActualDeg, r.AbdomenActualDeg - r.AbdomenNominalDeg,
                        r.ThoraxNominalDeg, r.ThoraxActualDeg, r.ThoraxActualDeg - r.ThoraxNominalDeg,
                        r.BarRelSupportCenter, r.BarRelCop, r.SaddleSeparation));
                }

                report.AppendLine();
                report.AppendLine(TrunkAttribution(rows));
                report.AppendLine();
            }

            WriteMeasurement("GAM11-5h14-t2-trunk-and-bar.txt", report.ToString());
            WriteMeasurement("GAM11-5h14-t2-trunk-and-bar.csv", csv.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        /// <summary>
        /// Section 8. Attributes the excess world trunk pitch across the chain
        /// by comparing each joint's actual flexion against the flexion the
        /// accepted reference commands at the same phase. Hip flexion is
        /// negative in logical joint space, so its excess is negated to read as
        /// "more flexed than asked".
        /// </summary>
        private static string TrunkAttribution(List<Row> rows)
        {
            Row bottom = Nearest(rows, 1.00f);
            float hipExcess = -(bottom.HipActualDeg - bottom.HipNominalDeg);
            float abdExcess = bottom.AbdomenActualDeg - bottom.AbdomenNominalDeg;
            float thoExcess = bottom.ThoraxActualDeg - bottom.ThoraxNominalDeg;
            float spinal = abdExcess + thoExcess;
            float trunkExcess = bottom.WorldTrunkPitch - bottom.ReferenceWorldTrunkPitch;

            string source;
            float dominant = Mathf.Max(Mathf.Abs(hipExcess), Mathf.Abs(spinal));
            if (dominant < 1f)
                source = "NONE_MATERIAL";
            else if (Mathf.Abs(hipExcess) > 2f * Mathf.Abs(spinal))
                source = "HIP_EXCESS";
            else if (Mathf.Abs(spinal) > 2f * Mathf.Abs(hipExcess))
                source = "SPINAL_TRUNK_FLEXION";
            else
                source = "COMBINED_CHAIN";

            return string.Format(CultureInfo.InvariantCulture,
                "  At the bottom the world trunk pitch exceeds the reference by {0:F2} deg.\n" +
                "  Hip flexes {1:F2} deg beyond its reference, abdomen {2:F2}, thorax {3:F2}, spine total {4:F2}.\n" +
                "  TRUNK_EXCESS_SOURCE = {5}",
                trunkExcess, hipExcess, abdExcess, thoExcess, spinal, source);
        }

        // ------------------------------------------------------------------
        // T3. Why the spine does not hold, section 8 follow-up.
        // ------------------------------------------------------------------

        /// <summary>
        /// T2 attributes the excess lean to the abdomen and thorax rather than
        /// the hip. That leaves one question the redesign turns on: are those
        /// drives asking for more than their authored ceiling can deliver, or
        /// are they inside it and simply not stiff enough to hold the segment?
        ///
        /// ModeledDemand is the conceptual spring-plus-damper torque over the
        /// finite maximumForce the capacity scales, so demand at or above one
        /// means the ceiling is the binding constraint. Nothing is changed;
        /// this only reads the diagnostic the powered controller already
        /// publishes.
        /// </summary>
        [UnityTest]
        public IEnumerator T3_SPINAL_DRIVE_AUTHORITY()
        {
            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H14 T3 SPINAL DRIVE AUTHORITY THROUGH THE DESCENT");
            report.AppendLine("demand is PoweredJointDiagnostic.ModeledDemand: conceptual drive torque over");
            report.AppendLine("the authored maximumForce ceiling. At or above 1.0 the ceiling is binding.");
            report.AppendLine("errX is the joint-space flexion tracking error in degrees.");
            report.AppendLine();

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,tick,sq,joint,demand,max_force_nm,err_x_deg,capacity_scale," +
                           "solver_torque_x_nm,nominal_deg,final_deg,actual_deg");

            string[] watched = { "abdomen", "thorax", "left_thigh", "left_foot" };

            foreach (float load in new[] { 0f, 25f })
            {
                var samples = new List<(float sq, string joint, float demand, float err, float maxForce, float capacity)>();
                yield return RecordDriveDemand(load, watched, samples, csv);

                report.AppendLine("--- load " + load.ToString("F0", CultureInfo.InvariantCulture) + " kg ---");
                report.AppendLine();
                report.AppendLine("  s_q   joint         demand   maxForceNm  capacity  errXdeg");
                foreach (var s in samples)
                {
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,4:F2}  {1,-12}  {2,7:F3}  {3,10:F1}  {4,8:F3}  {5,7:F2}",
                        s.sq, s.joint, s.demand, s.maxForce, s.capacity, s.err));
                }

                float worstAbdomen = 0f;
                float worstThorax = 0f;
                foreach (var s in samples)
                {
                    if (s.joint == "abdomen")
                        worstAbdomen = Mathf.Max(worstAbdomen, s.demand);
                    if (s.joint == "thorax")
                        worstThorax = Mathf.Max(worstThorax, s.demand);
                }
                report.AppendLine();
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  peak abdomen demand {0:F3}   peak thorax demand {1:F3}   ceiling binding = {2}",
                    worstAbdomen, worstThorax,
                    worstAbdomen >= 1f || worstThorax >= 1f ? "YES" : "NO"));
                report.AppendLine();
            }

            WriteMeasurement("GAM11-5h14-t3-spinal-drive-authority.txt", report.ToString());
            WriteMeasurement("GAM11-5h14-t3-spinal-drive-authority.csv", csv.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        private IEnumerator RecordDriveDemand(
            float loadKg,
            string[] watched,
            List<(float sq, string joint, float demand, float err, float maxForce, float capacity)> samples,
            StringBuilder csv)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.BalanceCorrectionsEnabled = true;
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            var wanted = new List<float> { 0.00f, 0.40f, 0.55f, 0.70f, 0.80f, 0.90f, 1.00f };
            int nextWanted = 0;
            float sq = 0f;
            int tick = 0;
            while (tick < 420)
            {
                sq = Mathf.Min(1f, sq + ApproachPhaseRate * dt);
                SquatState state = sq >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
                adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
                tick++;

                foreach (string jointId in watched)
                {
                    PoweredJointController.PoweredJointRuntime joint = powered.GetJoint(jointId);
                    if (joint == null)
                        continue;
                    adapter.TryGetTargetComposition(jointId, out var composition);
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F0},{1},{2:F4},{3},{4:F4},{5:F2},{6:F3},{7:F3},{8:F3},{9:F3},{10:F3},{11:F3}",
                        loadKg, tick, sq, jointId,
                        joint.Diagnostic.ModeledDemand, joint.Diagnostic.MaximumForceNm,
                        joint.Diagnostic.ErrorRad.x * Mathf.Rad2Deg, joint.Diagnostic.CapacityScale,
                        joint.Diagnostic.SolverTorqueJointSpaceNm.x,
                        TwistX(composition.Nominal), TwistX(composition.Final),
                        TwistX(joint.Diagnostic.ActualRelative)));
                }

                if (nextWanted < wanted.Count && sq >= wanted[nextWanted] - 1e-4f)
                {
                    foreach (string jointId in watched)
                    {
                        PoweredJointController.PoweredJointRuntime joint = powered.GetJoint(jointId);
                        if (joint == null)
                            continue;
                        samples.Add((sq, jointId, joint.Diagnostic.ModeledDemand,
                            joint.Diagnostic.ErrorRad.x * Mathf.Rad2Deg,
                            joint.Diagnostic.MaximumForceNm, joint.Diagnostic.CapacityScale));
                    }
                    nextWanted++;
                }
            }
            yield return null;
        }

        // ------------------------------------------------------------------
        // T4. Visual gate, section 17.
        // ------------------------------------------------------------------

        /// <summary>
        /// Captures the same descent this phase measured, at the phases the
        /// numbers are quoted at, and logs the measured heel clearance and
        /// trunk excess beside each frame so the image and the trace can be
        /// checked against one another rather than argued about separately.
        ///
        /// It also runs the production auto-cycle path the existing visual
        /// qualification uses, because the earlier screenshot came from that
        /// path while this phase drives the phase directly. If the two
        /// disagree, the conclusion belongs to whichever one the owner sees.
        /// </summary>
        [UnityTest]
        public IEnumerator T4_PLANTAR_VISUAL_GATE()
        {
            var log = new StringBuilder();
            log.AppendLine("GAM-11 PHASE 5H14 T4 VISUAL GATE");
            log.AppendLine("Frames captured on the driven descent this phase measured, and on the");
            log.AppendLine("production auto-cycle the existing visual qualification uses.");
            log.AppendLine();

            yield return CaptureDrivenDescent(25f, log);
            yield return CaptureAutoCycle(25f, log);

            WriteMeasurement("GAM11-5h14-t4-visual-gate.txt", log.ToString());
            Debug.Log(log.ToString());
            yield return null;
        }

        private IEnumerator CaptureDrivenDescent(float loadKg, StringBuilder log)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatBalanceObserver balance = adapter.Balance;
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            Rigidbody thorax = _rig.Segments["thorax"].Body;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.BalanceCorrectionsEnabled = true;
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
            Assert.That(camera, Is.Not.Null);

            log.AppendLine("--- driven descent, 25 kg ---");
            log.AppendLine("  tag                      s_q   heelClrL  heelClrR  supportLen  trunkAct  trunkRef  excess");

            var stops = new List<float> { 0.00f, 0.55f, 0.70f, 0.80f, 1.00f };
            int next = 0;
            float sq = 0f;
            int tick = 0;
            while (tick < 420 && next < stops.Count)
            {
                if (sq >= stops[next] - 1e-4f)
                {
                    yield return null;
                    string tag = string.Format(CultureInfo.InvariantCulture,
                        "h14_driven_25kg_sq{0:F2}", stops[next]);
                    Capture(camera, tag + "_side.png", new Vector3(2.6f, 1.1f, 0f), new Vector3(0f, 0.9f, 0f));
                    Capture(camera, tag + "_oblique.png", new Vector3(2.0f, 1.35f, 1.8f), new Vector3(0f, 0.9f, 0f));

                    FootWitness left = Witness("left_foot");
                    FootWitness right = Witness("right_foot");
                    float actual = WorldPitch(pelvis.position, thorax.position);
                    float reference = SampleReferenceTrunkPitch(sq);
                    log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,-22}  {1,4:F2}  {2,8:F4}  {3,8:F4}  {4,10:F4}  {5,8:F2}  {6,8:F2}  {7,6:F2}",
                        tag, sq,
                        left.HeelWorld.y - _platformTopY, right.HeelWorld.y - _platformTopY,
                        balance.SupportApLength, actual, reference, actual - reference));
                    next++;
                    continue;
                }

                sq = Mathf.Min(1f, sq + ApproachPhaseRate * dt);
                SquatState state = sq >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
                adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
                tick++;
            }
            log.AppendLine();
            yield return null;
        }

        private IEnumerator CaptureAutoCycle(float loadKg, StringBuilder log)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatBalanceObserver balance = adapter.Balance;
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            Rigidbody thorax = _rig.Segments["thorax"].Body;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            for (int i = 0; i < 30; i++)
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);

            Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
            adapter.StartSquat();

            log.AppendLine("--- production auto-cycle, 25 kg, the path the existing visual capture uses ---");
            log.AppendLine("  tag                      s_q   heelClrL  heelClrR  supportLen  trunkAct  trunkRef  excess");

            int[] captureTicks = { 175, 250, 325, 350 };
            int index = 0;
            int total = Mathf.CeilToInt(8.0f / dt);
            for (int tick = 0; tick < total; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
                if (index >= captureTicks.Length || tick != captureTicks[index])
                    continue;

                yield return null;
                string tag = string.Format(CultureInfo.InvariantCulture,
                    "h14_auto_25kg_t{0:D3}_sq{1:F2}", tick, adapter.Sq);
                Capture(camera, tag + "_side.png", new Vector3(2.6f, 1.1f, 0f), new Vector3(0f, 0.9f, 0f));
                Capture(camera, tag + "_oblique.png", new Vector3(2.0f, 1.35f, 1.8f), new Vector3(0f, 0.9f, 0f));

                FootWitness left = Witness("left_foot");
                FootWitness right = Witness("right_foot");
                float actual = WorldPitch(pelvis.position, thorax.position);
                float reference = SampleReferenceTrunkPitch(adapter.Sq);
                log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-22}  {1,4:F2}  {2,8:F4}  {3,8:F4}  {4,10:F4}  {5,8:F2}  {6,8:F2}  {7,6:F2}",
                    tag, adapter.Sq,
                    left.HeelWorld.y - _platformTopY, right.HeelWorld.y - _platformTopY,
                    balance.SupportApLength, actual, reference, actual - reference));
                index++;
            }
            log.AppendLine();
            yield return null;
        }

        private static void Capture(Camera camera, string filename, Vector3 position, Vector3 lookAt)
        {
            camera.transform.position = position;
            camera.transform.LookAt(lookAt);
            var texture = new RenderTexture(1280, 720, 24);
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;

            string directory = Path.Combine(
                Directory.GetCurrentDirectory(), "Artifacts/Evidence/GAM-11/h14-plantar");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, filename), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(texture);
        }

        // ------------------------------------------------------------------

        private static Row Nearest(List<Row> rows, float phase)
        {
            Row best = default;
            float bestDelta = float.MaxValue;
            foreach (Row r in rows)
            {
                float delta = Mathf.Abs(r.Sq - phase);
                if (delta >= bestDelta)
                    continue;
                bestDelta = delta;
                best = r;
            }
            return best;
        }

        private static int FirstTick(List<Row> rows, Func<Row, bool> predicate)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (predicate(rows[i]))
                    return i;
            }
            return -1;
        }

        private static string Describe(List<Row> rows, int index) =>
            index < 0
                ? "never"
                : string.Format(CultureInfo.InvariantCulture, "tick {0} (s_q {1:F3})",
                    rows[index].Tick, rows[index].Sq);

        private static void EmitCsv(float load, List<Row> rows, StringBuilder csv)
        {
            foreach (Row r in rows)
            {
                float postFrac = r.TotalImpulse > 1e-6f ? r.PosteriorImpulse / r.TotalImpulse : -1f;
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F0},{1},{2:F4},{3:F5},{4:F5},{5:F5},{6:F5},{7:F3},{8:F3},{9:F4},{10:F4},{11:F4}," +
                    "{12:F4},{13:F5},{14:F5},{15},{16},{17:F5},{18:F5},{19:F5},{20:F5},{21:F4},{22}," +
                    "{23:F3},{24:F3},{25:F3},{26:F3},{27:F3},{28:F3},{29:F5},{30:F5},{31:F3},{32:F3},{33:F5},{34:F5}",
                    load, r.Tick, r.Sq,
                    r.LeftHeelClearance, r.LeftToeClearance, r.RightHeelClearance, r.RightToeClearance,
                    r.LeftFootPitch, r.RightFootPitch,
                    r.PosteriorImpulse, r.AnteriorImpulse, r.TotalImpulse, postFrac,
                    r.PosteriorContactAp, r.AnteriorContactAp, r.ContactCount, r.MaxPerFootContacts,
                    r.SupportMin, r.SupportMax, r.SupportLength, r.CopAp, r.CopFraction, r.HasCop ? 1 : 0,
                    r.RawAnkleDemandDeg, r.AppliedAnkleDeg,
                    r.AnkleNominalDeg, r.AnkleBalanceDeg, r.AnkleFinalDeg, r.AnkleActualDeg,
                    r.ComAp, r.ComRefAp, r.WorldTrunkPitch, r.ReferenceWorldTrunkPitch,
                    r.SlipLeft, r.SlipRight));
            }
        }

        private static float TwistX(Quaternion rotation)
        {
            Quaternion q = PoweredJointController.NormalizeCanonical(rotation);
            var vector = new Vector3(q.x, q.y, q.z);
            Vector3 projection = Vector3.Project(vector, Vector3.right);
            var twist = new Quaternion(projection.x, projection.y, projection.z, q.w);
            float magnitude = Mathf.Sqrt(
                twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (magnitude < 1e-6f)
                return 0f;
            twist = new Quaternion(
                twist.x / magnitude, twist.y / magnitude, twist.z / magnitude, twist.w / magnitude);
            twist.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
                angle -= 360f;
            return angle * Mathf.Sign(Vector3.Dot(axis, Vector3.right));
        }

        private void TickFeet(float dt)
        {
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private static void WriteMeasurement(string filename, string content)
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), content);
        }
    }
}
