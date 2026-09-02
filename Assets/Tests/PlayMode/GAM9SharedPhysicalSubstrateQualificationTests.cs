using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Profiling;
using Unity.Profiling;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM9SharedPhysicalSubstrateQualificationTests
    {
        private const string QualificationScene = "PhysicalAthletePhysics";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-9";
        private const string MeasurementDirectory = "Artifacts/Measurements";

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private PhysicalBarbell _bar;

        [UnityTest]
        public IEnumerator G1_PHYSICAL_SUBSTRATE_QUALIFICATION_EVIDENCE()
        {
            yield return LoadQualificationScene();
            AssertSceneTopology();
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null), "G1 visual evidence requires a graphics device.");

            FoundationRuntime runtime = _bootstrap.Runtime;
            Assert.That(runtime.AuthoritativeScene.GetPhysicsScene(), Is.Not.EqualTo(Physics.defaultPhysicsScene));
            Assert.That(_rig.TotalMassKg, Is.EqualTo(PhysicalAthleteDefinition.PrototypeBodyMassKg).Within(0.0001f));
            Assert.That(_rig.MaxInitialNonAdjacentPenetrationMeters, Is.LessThanOrEqualTo(0.015f));

            _rig.InspectNeutral();
            AdvanceTicks(runtime, 1);
            Assert.That(runtime.CurrentObservation.BodyCount, Is.EqualTo(17));
            Assert.That(runtime.CurrentObservation.PrimaryBody.BodyId, Is.EqualTo("pelvis"));
            Assert.That(runtime.CurrentObservation.BodyAt(16).BodyId, Is.EqualTo("barbell"));
            yield return null;
            CaptureEvidence("GAM-9-neutral-substrate.png");

            _rig.ResetPassive();
            float passiveStart = _rig.CalculateWholeBodyCom().y;
            AdvanceTicks(runtime, 75);
            float passiveDrop = passiveStart - _rig.CalculateWholeBodyCom().y;
            Assert.That(passiveDrop, Is.GreaterThan(0.04f), "The unpowered 16-body athlete did not fall under gravity.");
            AssertFiniteAthlete();
            yield return null;
            CaptureEvidence("GAM-9-passive-no-support.png");

            _rig.StartPoweredNeutral();
            float poweredStart = _rig.CalculateWholeBodyCom().y;
            AdvanceTicks(runtime, 75);
            float poweredDrop = poweredStart - _rig.CalculateWholeBodyCom().y;
            Assert.That(poweredDrop, Is.LessThan(passiveDrop * 0.85f),
                $"Finite powered-neutral authority did not reduce COM drop (passive={passiveDrop:F3} m, powered={poweredDrop:F3} m).");
            AssertFiniteAthlete();
            yield return null;
            CaptureEvidence("GAM-9-powered-neutral.png");

            _rig.StartZeroActivation();
            foreach (PoweredJointController.PoweredJointRuntime joint in _rig.PoweredController.Joints)
            {
                if (joint.Profile.HasValue)
                    Assert.That(ActiveDrive(joint).maximumForce, Is.Zero, joint.Id);
            }

            _rig.StartSelectedJointPulse(true);
            AdvanceTicks(runtime, 45);
            float positivePulseDegrees = SignedXDegrees(_rig.PoweredController.GetJoint("left_shank").Diagnostic.ActualRelative);
            Assert.That(positivePulseDegrees, Is.GreaterThan(1f));

            _rig.StartPoweredNeutral();
            AdvanceTicks(runtime, 60);
            Collider platform = FindNamedComponent<Collider>(runtime.AuthoritativeScene, "PhysicalPlatform_GAM6");
            Assert.That(platform, Is.Not.Null);
            float leftFootGap = Mathf.Abs(_rig.Segments["left_foot"].Collider.bounds.min.y - platform.bounds.max.y);
            float rightFootGap = Mathf.Abs(_rig.Segments["right_foot"].Collider.bounds.min.y - platform.bounds.max.y);
            float bestFootGap = Mathf.Min(leftFootGap, rightFootGap);
            Assert.That(bestFootGap, Is.LessThan(0.08f), $"Neither physical foot reached the platform (gaps {leftFootGap:F3}/{rightFootGap:F3} m).");
            yield return null;
            CaptureEvidence("GAM-9-feet-platform-contact.png");

            _bar.ConfigureLoad(105f);
            _rig.StartPoweredNeutral();
            AdvanceTicks(runtime, 2);
            Assert.That(_bar.LoadedMassKg, Is.EqualTo(105f).Within(0.0001f));
            Assert.That(_bar.Body.GetComponentsInChildren<Rigidbody>(true), Has.Length.EqualTo(1));
            yield return null;
            CaptureEvidence("GAM-9-athlete-and-bar.png");

            _bar.ClearTrace();
            _rig.StartPoweredNeutral();
            runtime.BeginAttemptTrace();
            AdvanceTicks(runtime, 20);
            runtime.EndAttemptTrace();
            Assert.That(runtime.AttemptTrace.Count, Is.EqualTo(20));
            AssertTraceShape(runtime.AttemptTrace, 17);
            int traceSampleCount = runtime.AttemptTrace.Count;
            _bar.ToggleRecordedTrail();
            Assert.That(_bar.IsRecordedTrailVisible, Is.True);
            Assert.That(_bar.RecordedTrailPointCount, Is.EqualTo(runtime.AttemptTrace.Count));
            GameObject trail = GameObject.Find("RecordedBarTrail_GAM8_PresentationOnly");
            Assert.That(trail, Is.Not.Null);
            Assert.That(trail.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            _bar.ClearTrace();

            _bar.ConfigureLoad(105f);
            _rig.StartPoweredNeutral();
            float barStart = _bar.Body.position.y;
            AdvanceTicks(runtime, 75);
            float observedBarDrop = barStart - _bar.Body.position.y;
            Assert.That(observedBarDrop, Is.GreaterThan(0.05f));
            Assert.That(Finite(_bar.Body.position), Is.True);
            Assert.That(Finite(_bar.Body.linearVelocity), Is.True);

            _bar.ConfigureLoad(105f);
            _rig.StartPoweredNeutral();
            AdvanceTicks(runtime, 1);
            yield return null;
            CaptureEvidence("GAM-9-reset-repeatability.png");

            WriteMeasurement("GAM-9-g1-qualification.json", new G1QualificationArtifact
            {
                schema = "GAM9_G1_QUALIFICATION_V1",
                status = "PASS_PENDING_PERFORMANCE_BUILD_RECEIPTS",
                unityVersion = Application.unityVersion,
                scene = QualificationScene,
                authoritativeScene = runtime.AuthoritativeScene.name,
                bodyCount = _rig.Segments.Count,
                jointCount = _rig.Joints.Count,
                poweredJointCount = _rig.PoweredController.PoweredJointCount,
                passiveJointCount = _rig.PoweredController.PassiveJointCount,
                totalMassKg = _rig.TotalMassKg,
                passiveComDropM = passiveDrop,
                poweredComDropM = poweredDrop,
                positivePulseDegrees = positivePulseDegrees,
                barDropM = observedBarDrop,
                barMassKg = _bar.LoadedMassKg,
                observationBodyCount = runtime.CurrentObservation.BodyCount,
                traceSamples = traceSampleCount,
                evidence = new[]
                {
                    EvidenceDirectory + "/GAM-9-neutral-substrate.png",
                    EvidenceDirectory + "/GAM-9-passive-no-support.png",
                    EvidenceDirectory + "/GAM-9-powered-neutral.png",
                    EvidenceDirectory + "/GAM-9-feet-platform-contact.png",
                    EvidenceDirectory + "/GAM-9-athlete-and-bar.png",
                    EvidenceDirectory + "/GAM-9-reset-repeatability.png"
                }
            });
        }

        [UnityTest]
        public IEnumerator G1_FIXED_TICK_RESET_REPEATABILITY()
        {
            const int repeatCount = 20;
            const int ticksPerRepeat = 20;
            const int tracedRepeatCount = 10;

            yield return LoadQualificationScene();
            AssertSceneTopology();
            FoundationRuntime runtime = _bootstrap.Runtime;

            G1Snapshot[] firstTrial = null;
            float maxPositionDelta = 0f;
            float maxRotationDelta = 0f;
            float maxLinearVelocityDelta = 0f;
            float maxAngularVelocityDelta = 0f;
            float maxTracePositionDelta = 0f;
            float maxTraceRotationDelta = 0f;
            float maxTraceLinearVelocityDelta = 0f;
            float maxTraceAngularVelocityDelta = 0f;
            float maxBarInertiaDelta = 0f;

            for (int repeatIndex = 0; repeatIndex < repeatCount; repeatIndex++)
            {
                _bar.ConfigureLoad(105f);
                _rig.StartPoweredNeutral();
                AssertPoweredControllerResetState(_rig.PoweredController);

                bool traced = repeatIndex >= repeatCount - tracedRepeatCount;
                if (traced)
                    runtime.BeginAttemptTrace();

                G1Snapshot[] trial = RunManualSnapshots(runtime, _bar, ticksPerRepeat);

                if (traced)
                {
                    runtime.EndAttemptTrace();
                    Assert.That(runtime.AttemptTrace.Count, Is.EqualTo(ticksPerRepeat));
                    AssertTraceShape(runtime.AttemptTrace, 17);
                }

                if (firstTrial == null)
                {
                    firstTrial = trial;
                    continue;
                }

                Assert.That(trial.Length, Is.EqualTo(firstTrial.Length));
                for (int tickIndex = 0; tickIndex < firstTrial.Length; tickIndex++)
                {
                    G1Snapshot baseline = firstTrial[tickIndex];
                    G1Snapshot candidate = trial[tickIndex];
                    maxBarInertiaDelta = Mathf.Max(maxBarInertiaDelta, CompareSnapshotMetadata(baseline, candidate));
                    float positionDelta = CompareSnapshots(
                        baseline,
                        candidate,
                        0.0001f,
                        0.01f,
                        out float linearVelocityDelta,
                        out float angularVelocityDelta);
                    maxPositionDelta = Mathf.Max(maxPositionDelta, positionDelta);
                    maxRotationDelta = Mathf.Max(maxRotationDelta, CompareRotationSnapshots(baseline, candidate));
                    maxLinearVelocityDelta = Mathf.Max(maxLinearVelocityDelta, linearVelocityDelta);
                    maxAngularVelocityDelta = Mathf.Max(maxAngularVelocityDelta, angularVelocityDelta);

                    if (traced)
                    {
                        maxTracePositionDelta = Mathf.Max(maxTracePositionDelta, positionDelta);
                        maxTraceRotationDelta = Mathf.Max(maxTraceRotationDelta, CompareRotationSnapshots(baseline, candidate));
                        maxTraceLinearVelocityDelta = Mathf.Max(maxTraceLinearVelocityDelta, linearVelocityDelta);
                        maxTraceAngularVelocityDelta = Mathf.Max(maxTraceAngularVelocityDelta, angularVelocityDelta);
                    }
                }
            }

            Assert.That(firstTrial, Is.Not.Null);
            Assert.That(maxPositionDelta, Is.LessThan(0.0001f));
            Assert.That(maxRotationDelta, Is.LessThan(0.01f));
            Assert.That(maxLinearVelocityDelta, Is.LessThan(0.0001f));
            Assert.That(maxAngularVelocityDelta, Is.LessThan(0.0001f));
            Assert.That(maxTracePositionDelta, Is.LessThan(0.0001f));
            Assert.That(maxTraceRotationDelta, Is.LessThan(0.01f));
            Assert.That(maxTraceLinearVelocityDelta, Is.LessThan(0.0001f));
            Assert.That(maxTraceAngularVelocityDelta, Is.LessThan(0.0001f));
            Assert.That(runtime.AttemptTrace.Count, Is.EqualTo(ticksPerRepeat));
            AssertTraceShape(runtime.AttemptTrace, 17);

            _rig.StartPoweredNeutral();
            AssertPoweredControllerResetState(_rig.PoweredController);
            Assert.That(runtime.CurrentObservation.HasPrimaryBody, Is.False);
            Assert.That(runtime.AttemptTrace.Count, Is.Zero);
            Assert.That(runtime.InputBuffer.PendingSampleCount, Is.Zero);
            Assert.That(_rig.PoweredController.Mode, Is.EqualTo(PoweredAthleteMode.PoweredNeutral));
            Assert.That(_rig.PoweredController.GlobalActivation, Is.EqualTo(1f));
            foreach (PhysicalAthleteRig.SegmentRuntime segment in _rig.Segments.Values)
            {
                Assert.That(segment.Body.linearVelocity, Is.EqualTo(Vector3.zero), segment.Recipe.Id);
                Assert.That(segment.Body.angularVelocity, Is.EqualTo(Vector3.zero), segment.Recipe.Id);
                Assert.That(segment.Body.isKinematic, Is.False, segment.Recipe.Id);
                Assert.That(segment.Body.useGravity, Is.True, segment.Recipe.Id);
            }
            Assert.That(_bar.Body.linearVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(_bar.Body.angularVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(_bar.Body.isKinematic, Is.False);
            Assert.That(_bar.Body.useGravity, Is.True);

            WriteMeasurement("GAM-9-repeatability.json", new RepeatabilityArtifact
            {
                schema = "GAM9_RESET_REPEATABILITY_V1",
                status = "PASS",
                repeatCount = repeatCount,
                ticksPerRepeat = ticksPerRepeat,
                tracedRepeatCount = tracedRepeatCount,
                maxPositionDeltaM = maxPositionDelta,
                maxRotationDeltaDeg = maxRotationDelta,
                maxLinearVelocityDeltaMps = maxLinearVelocityDelta,
                maxAngularVelocityDeltaRadS = maxAngularVelocityDelta,
                maxTracePositionDeltaM = maxTracePositionDelta,
                maxTraceRotationDeltaDeg = maxTraceRotationDelta,
                maxTraceLinearVelocityDeltaMps = maxTraceLinearVelocityDelta,
                maxTraceAngularVelocityDeltaRadS = maxTraceAngularVelocityDelta,
                barMassKg = firstTrial[0].BarMassKg,
                maxBarInertiaDeltaKgM2 = maxBarInertiaDelta,
                firstTick = firstTrial[0].Tick,
                firstSimulationTimeS = firstTrial[0].TimeSeconds,
                resetObservationBodyCount = runtime.CurrentObservation.BodyCount,
                resetPendingInputSamples = runtime.InputBuffer.PendingSampleCount
            });
            yield return null;
        }

        [UnityTest]
        public IEnumerator G1_MUTATION_GATES_REJECT_FORBIDDEN_AUTHORITY()
        {
            yield return LoadQualificationScene();
            AssertSceneTopology();
            FoundationRuntime runtime = _bootstrap.Runtime;
            var results = new List<MutationResult>();

            SourceAudit sourceAudit = AuditProductionSource();
            results.Add(new MutationResult
            {
                id = "M01",
                mutation = "second PhysicsScene.Simulate or global Physics.Simulate call",
                gate = "production source audit",
                rejected = sourceAudit.localSimulateCalls == 1 && sourceAudit.globalSimulateCalls == 0 && sourceAudit.fixedUpdateCalls == 0 && sourceAudit.scriptedMotionCalls == 0,
                observed = $"localSimulateCalls={sourceAudit.localSimulateCalls}; globalSimulateCalls={sourceAudit.globalSimulateCalls}; fixedUpdateCalls={sourceAudit.fixedUpdateCalls}; scriptedMotionCalls={sourceAudit.scriptedMotionCalls}"
            });

            Exception duplicateWriterError = null;
            try
            {
                runtime.RegisterPrePhysicsStep((_, __) => { });
            }
            catch (InvalidOperationException exception)
            {
                duplicateWriterError = exception;
            }
            results.Add(Rejected("M02", "second powered-joint pre-physics writer", "FoundationRuntime.RegisterPrePhysicsStep", duplicateWriterError));

            _rig.ReleasePassive();
            Assert.That(InvokeRuntimeValidation(_rig), Is.Null, "The unmutated passive runtime must pass its validation gate.");

            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            bool oldKinematic = pelvis.isKinematic;
            pelvis.isKinematic = true;
            Exception kinematicError = InvokeRuntimeValidation(_rig);
            pelvis.isKinematic = oldKinematic;
            results.Add(Rejected("M03", "pelvis isKinematic=true", "PhysicalAthleteRig.ValidateRuntime", kinematicError));

            PhysicalAthleteRig.JointRuntime kneeRuntime = FindJoint("left_shank");
            ConfigurableJoint knee = kneeRuntime.Joint;
            JointDrive oldKneeDrive = knee.angularXDrive;
            JointDrive infiniteDrive = oldKneeDrive;
            infiniteDrive.maximumForce = float.PositiveInfinity;
            knee.angularXDrive = infiniteDrive;
            Exception infiniteError = InvokeRuntimeValidation(_rig);
            knee.angularXDrive = oldKneeDrive;
            results.Add(Rejected("M04", "powered joint maximumForce=+Infinity", "PhysicalAthleteRig.ValidateRuntime", infiniteError));

            JointDrive accelerationDrive = oldKneeDrive;
            accelerationDrive.useAcceleration = true;
            knee.angularXDrive = accelerationDrive;
            Exception accelerationError = InvokeRuntimeValidation(_rig);
            knee.angularXDrive = oldKneeDrive;
            results.Add(Rejected(
                "M05",
                "powered joint useAcceleration=true",
                "PoweredJointController.ValidatePoweredDrive via PhysicalAthleteRig.ValidateRuntime",
                accelerationError));

            JointProjectionMode oldProjection = knee.projectionMode;
            knee.projectionMode = JointProjectionMode.PositionAndRotation;
            Exception projectionError = InvokeRuntimeValidation(_rig);
            knee.projectionMode = oldProjection;
            results.Add(Rejected("M06", "joint projection enabled", "PhysicalAthleteRig.ValidateRuntime", projectionError));

            GameObject extraBodyObject = new GameObject("GAM9_FORBIDDEN_EXTRA_BAR_RIGIDBODY");
            SceneManager.MoveGameObjectToScene(extraBodyObject, runtime.AuthoritativeScene);
            extraBodyObject.transform.SetParent(_bar.Body.transform, false);
            extraBodyObject.AddComponent<BoxCollider>();
            extraBodyObject.AddComponent<Rigidbody>();
            int barRigidbodiesAfterMutation = _bar.Body.GetComponentsInChildren<Rigidbody>(true).Length;
            UnityEngine.Object.DestroyImmediate(extraBodyObject);
            results.Add(new MutationResult
            {
                id = "M07",
                mutation = "child Rigidbody added below the physical bar",
                gate = "one-dynamic-bar invariant",
                rejected = barRigidbodiesAfterMutation > 1,
                observed = $"barRigidbodiesAfterMutation={barRigidbodiesAfterMutation}"
            });

            float[] symmetricLeft = CopyMasses(_bar.LoadLayout.Left);
            float[] symmetricRight = CopyMasses(_bar.LoadLayout.Right);
            symmetricLeft[0] += 1.25f;
            bool asymmetricRejected = !SameMassSequence(symmetricLeft, symmetricRight);
            results.Add(new MutationResult
            {
                id = "M08",
                mutation = "one-sided plate mass mutation",
                gate = "symmetric-load qualification detector",
                rejected = asymmetricRejected,
                observed = "mutated left/right per-side mass sequences differ"
            });

            float maximumFiniteInventoryLoadKg = BarbellPrototypeConfiguration.BaseBarbellMassKg;
            for (int index = 0; index < BarbellPrototypeConfiguration.Inventory.Count; index++)
            {
                BarbellInventoryEntry entry = BarbellPrototypeConfiguration.Inventory[index];
                maximumFiniteInventoryLoadKg += entry.MassKilograms * entry.MaximumPairsPerSide * 2f;
            }
            Exception sleeveOverflowError = null;
            try
            {
                BarbellLoadingSolver.Solve(maximumFiniteInventoryLoadKg);
            }
            catch (InvalidOperationException exception)
            {
                sleeveOverflowError = exception;
            }
            results.Add(Rejected(
                "M09",
                $"sleeve-overflow load ({maximumFiniteInventoryLoadKg:0.###} kg)",
                "BarbellLoadingSolver.CalculateSideLayout",
                sleeveOverflowError));

            bool duplicateBodyRejected = false;
            string duplicateBodyMessage = string.Empty;
            try
            {
                runtime.RegisterBody(_rig.Segments["abdomen"].Body, "pelvis");
            }
            catch (InvalidOperationException exception)
            {
                duplicateBodyRejected = true;
                duplicateBodyMessage = exception.Message;
            }
            results.Add(new MutationResult
            {
                id = "M10",
                mutation = "duplicate registered body/identifier",
                gate = "AuthoritativePhysicsScene.RegisterBody",
                rejected = duplicateBodyRejected,
                observed = duplicateBodyMessage
            });

            _rig.StartPoweredNeutral();
            runtime.BeginAttemptTrace();
            AdvanceTicks(runtime, 3);
            runtime.EndAttemptTrace();
            _bar.ToggleRecordedTrail();
            GameObject recordedTrail = GameObject.Find("RecordedBarTrail_GAM8_PresentationOnly");
            bool trailHasNoRigidbodies = recordedTrail != null && recordedTrail.GetComponentsInChildren<Rigidbody>(true).Length == 0;
            _bar.ClearTrace();
            results.Add(new MutationResult
            {
                id = "M11",
                mutation = "recorded-state trail given physics authority",
                gate = "presentation-only trail invariant",
                rejected = trailHasNoRigidbodies,
                observed = trailHasNoRigidbodies ? "no Rigidbody below recorded trail" : "Rigidbody found below recorded trail"
            });

            _rig.StartSelectedJointPulse(false);
            AdvanceTicks(runtime, 45);
            float wrongSignDegrees = SignedXDegrees(_rig.PoweredController.GetJoint("left_shank").Diagnostic.ActualRelative);
            results.Add(new MutationResult
            {
                id = "M12",
                mutation = "negative command sent to the positive knee convention",
                gate = "known-sign pulse detector",
                rejected = wrongSignDegrees < -1f,
                observed = $"signedKneeResponseDeg={wrongSignDegrees:0.000}; positive convention requires > 0"
            });

            bool allPassed = true;
            for (int index = 0; index < results.Count; index++)
            {
                if (!results[index].rejected)
                    allPassed = false;
            }
            WriteMeasurement("GAM-9-mutation-gates.json", new MutationArtifact
            {
                schema = "GAM9_MUTATION_GATE_MATRIX_V1",
                status = allPassed ? "PASS" : "FAIL",
                productionPhysicsSimulateCall = sourceAudit.localSimulateCalls,
                productionGlobalPhysicsSimulateCalls = sourceAudit.globalSimulateCalls,
                productionFixedUpdateCalls = sourceAudit.fixedUpdateCalls,
                productionScriptedMotionCalls = sourceAudit.scriptedMotionCalls,
                results = results.ToArray()
            });
            Assert.That(allPassed, Is.True, "At least one forbidden-authority mutation was not rejected by its declared gate.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator G1_PERFORMANCE_BOUNDED_SOAK_AND_CATCH_UP()
        {
            yield return LoadQualificationScene();
            AssertSceneTopology();
            FoundationRuntime runtime = _bootstrap.Runtime;
            _bootstrap.enabled = false;
            _rig.enabled = false;
            _bar.enabled = false;

            AllocationCapture allocationOffCapture = new AllocationCapture(120);
            _bar.ConfigureLoad(105f);
            _rig.StartPoweredNeutral();
            yield return CaptureProfilerGcSamples(runtime, false, allocationOffCapture);

            AllocationCapture allocationTraceCapture = new AllocationCapture(120);
            _bar.ConfigureLoad(105f);
            _rig.StartPoweredNeutral();
            yield return CaptureProfilerGcSamples(runtime, true, allocationTraceCapture);

            Assert.That(allocationOffCapture.Available, Is.True, allocationOffCapture.UnavailableReason);
            Assert.That(allocationTraceCapture.Available, Is.True, allocationTraceCapture.UnavailableReason);

            _bar.ConfigureLoad(105f);
            _rig.StartPoweredNeutral();
            for (int index = 0; index < 50; index++)
                runtime.StepOne();

            var physicsTickMs = new double[300];
            for (int index = 0; index < physicsTickMs.Length; index++)
            {
                Stopwatch timer = Stopwatch.StartNew();
                runtime.StepOne();
                timer.Stop();
                physicsTickMs[index] = timer.Elapsed.TotalMilliseconds;
            }
            AssertFiniteAthlete();

            _bar.ConfigureLoad(105f);
            _rig.StartPoweredNeutral();
            for (int index = 0; index < 50; index++)
                runtime.AdvanceRenderFrame(0.01d);
            var foundationFrameStepMs = new double[300];
            for (int index = 0; index < foundationFrameStepMs.Length; index++)
            {
                Stopwatch timer = Stopwatch.StartNew();
                Assert.That(runtime.AdvanceRenderFrame(0.01d), Is.EqualTo(1));
                timer.Stop();
                foundationFrameStepMs[index] = timer.Elapsed.TotalMilliseconds;
            }

            _bar.ConfigureLoad(105f);
            _rig.StartPoweredNeutral();
            for (int index = 0; index < 50; index++)
                Assert.That(runtime.AdvanceRenderFrame(0.04d), Is.EqualTo(4));
            var catchUpMs = new double[100];
            for (int index = 0; index < catchUpMs.Length; index++)
            {
                Stopwatch timer = Stopwatch.StartNew();
                Assert.That(runtime.AdvanceRenderFrame(0.04d), Is.EqualTo(4));
                timer.Stop();
                catchUpMs[index] = timer.Elapsed.TotalMilliseconds;
                Assert.That(runtime.LastCatchUpTicks, Is.EqualTo(4));
            }

            _bar.ConfigureLoad(105f);
            _rig.StartPoweredNeutral();
            for (int index = 0; index < 50; index++)
                _rig.PoweredController.Step(runtime.CurrentTime, PlayerIntentFrame.Empty);
            var controllerMs = new double[300];
            for (int index = 0; index < controllerMs.Length; index++)
            {
                Stopwatch timer = Stopwatch.StartNew();
                _rig.PoweredController.Step(runtime.CurrentTime, PlayerIntentFrame.Empty);
                timer.Stop();
                controllerMs[index] = timer.Elapsed.TotalMilliseconds;
            }

            _bar.ConfigureLoad(105f);
            _rig.StartPoweredNeutral();
            runtime.BeginAttemptTrace();
            for (int index = 0; index < 300; index++)
                runtime.StepOne();
            runtime.EndAttemptTrace();
            Assert.That(runtime.AttemptTrace.Count, Is.EqualTo(300));
            AssertTraceShape(runtime.AttemptTrace, 17);

            int baselineRigidbodies = CountComponents<Rigidbody>(runtime.AuthoritativeScene);
            int baselineJoints = CountComponents<ConfigurableJoint>(runtime.AuthoritativeScene);
            long baselineMemory = Profiler.GetTotalAllocatedMemoryLong();
            long peakMemory = baselineMemory;
            int soakCycles = 100;
            for (int cycle = 0; cycle < soakCycles; cycle++)
            {
                _bar.ConfigureLoad(105f);
                _rig.StartPoweredNeutral();
                for (int tick = 0; tick < 4; tick++)
                    runtime.StepOne();

                Assert.That(CountComponents<Rigidbody>(runtime.AuthoritativeScene), Is.EqualTo(baselineRigidbodies));
                Assert.That(CountComponents<ConfigurableJoint>(runtime.AuthoritativeScene), Is.EqualTo(baselineJoints));
                AssertFiniteAthlete();
                long currentMemory = Profiler.GetTotalAllocatedMemoryLong();
                if (currentMemory > peakMemory)
                    peakMemory = currentMemory;
            }
            long finalMemory = Profiler.GetTotalAllocatedMemoryLong();

            MetricSummary physicsSummary = Summarize(physicsTickMs);
            MetricSummary foundationFrameStepSummary = Summarize(foundationFrameStepMs);
            MetricSummary catchUpSummary = Summarize(catchUpMs);
            MetricSummary controllerSummary = Summarize(controllerMs);
            AllocationMetricArtifact allocationOff = allocationOffCapture.ToArtifact();
            AllocationMetricArtifact allocationTrace = allocationTraceCapture.ToArtifact();
            HotPathAudit hotPathAudit = AuditObservationTraceHotPath();
            long workingSetBytes = Process.GetCurrentProcess().WorkingSet64;

            var hardBudgetFailures = new List<string>();
            bool editorCatchUpWithinBudget = catchUpSummary.p95Ms <= 8d;
            bool editorPhysicsTickWithinBudget = physicsSummary.p95Ms <= 2d;
            bool editorFoundationFrameStepWithinBudget = foundationFrameStepSummary.p95Ms <= 10d;
            bool editorControllerWithinBudget = controllerSummary.p95Ms <= 0.25d;
            bool editorAllocationClean = allocationOffCapture.Available && allocationTraceCapture.Available &&
                allocationOff.maxBytes == 0L && allocationTrace.maxBytes == 0L;
            long runtimeAllocatedMemoryBytes = Profiler.GetTotalAllocatedMemoryLong();
            if (runtimeAllocatedMemoryBytes > 2L * 1024L * 1024L * 1024L)
                hardBudgetFailures.Add("runtimeAllocatedMemory>2GB");
            if (runtime.AttemptTrace.ReservedStorageEstimateBytes > 25L * 1024L * 1024L)
                hardBudgetFailures.Add("traceReservedStorage>25MB");
            if (runtime.AttemptTrace.LogicalPayloadStorageBytes > 25L * 1024L * 1024L)
                hardBudgetFailures.Add("traceLogicalPayload>25MB");
            if (!hotPathAudit.Pass)
                hardBudgetFailures.Add("staticHotPathAuditFailed");
            if (baselineRigidbodies != CountComponents<Rigidbody>(runtime.AuthoritativeScene))
                hardBudgetFailures.Add("rigidbodyCountChanged");
            if (baselineJoints != CountComponents<ConfigurableJoint>(runtime.AuthoritativeScene))
                hardBudgetFailures.Add("jointCountChanged");
            bool hardBudgetsPass = hardBudgetFailures.Count == 0;

            WriteMeasurement("GAM-9-performance.json", new PerformanceArtifact
            {
                schema = "GAM9_PERFORMANCE_BOUNDED_SOAK_V1",
                status = hardBudgetsPass ? "PASS_PENDING_STANDALONE_PERF" : "FAIL_HARD_BUDGET",
                measurementMethod = "Unity PlayMode local-authority harness; ProfilerRecorder Internal/GC.Alloc summed on the current thread for editor diagnostic frames; Stopwatch only for named foundation stage diagnostics; 300 timing samples and 120 allocation frames. Release allocation qualification is the standalone Development player artifact.",
                physicsTick = physicsSummary,
                steadyStateGcOff = allocationOff,
                steadyStateGcTrace = allocationTrace,
                allocationMetric = allocationOff.channel,
                editorAllocationClean = editorAllocationClean,
                editorAllocationGate = editorAllocationClean
                    ? "PASS; editor current-thread samples were zero."
                    : "NOT_A_RELEASE_GATE; Unity Editor/PlayMode test-runner frame samples include harness allocations; standalone GC.Alloc evidence is required.",
                releaseAllocationGate = "GAM9StandaloneSmoke: Development Windows player GC.Alloc (current thread), 120 warmed OFF and 120 warmed TRACE frames.",
                foundationFrameStep = foundationFrameStepSummary,
                fourTickCatchUp = catchUpSummary,
                controllerStep = controllerSummary,
                normalTraceSamples = 300,
                normalTraceAppendAllocationP95Bytes = allocationTrace.p95Bytes,
                normalTraceAppendAllocationMaxBytes = allocationTrace.maxBytes,
                normalTraceBudgetBytes = 25L * 1024L * 1024L,
                traceCapacitySamples = runtime.AttemptTrace.Capacity,
                traceRegisteredBodyCount = runtime.AttemptTrace.RegisteredBodyCount,
                traceReservedBodyRecordCount = runtime.AttemptTrace.ReservedBodyRecordCount,
                traceReservedBodyRecordStorageBytes = runtime.AttemptTrace.ReservedBodyRecordStorageBytes,
                traceReservedStorageEstimateBytes = runtime.AttemptTrace.ReservedStorageEstimateBytes,
                traceLogicalPayloadStorageBytes = runtime.AttemptTrace.LogicalPayloadStorageBytes,
                traceCurrentLogicalPayloadStorageBytes = runtime.AttemptTrace.CurrentLogicalPayloadStorageBytes,
                boundedSoakCycles = soakCycles,
                baselineRigidbodies = baselineRigidbodies,
                baselineJoints = baselineJoints,
                baselineMemoryBytes = baselineMemory,
                peakMemoryBytes = peakMemory,
                finalMemoryBytes = finalMemory,
                workingSetBytes = workingSetBytes,
                runtimeAllocatedMemoryBytes = runtimeAllocatedMemoryBytes,
                memoryBudgetBasis = "Profiler.GetTotalAllocatedMemoryLong; Process.WorkingSet64 is unavailable in this Unity editor harness when reported as zero.",
                gpuP95Ms = -1d,
                gpuMeasurement = "GPU=NOT_AVAILABLE_IN_CURRENT_HARNESS; standalone graphics smoke is a separate gate.",
                staticHotPathAuditPass = hotPathAudit.Pass,
                staticHotPathAudit = hotPathAudit.Description,
                hardBudgetsPass = hardBudgetsPass,
                hardBudgetFailures = hardBudgetFailures.ToArray(),
                editorCatchUpWithinBudget = editorCatchUpWithinBudget,
                editorPhysicsTickWithinBudget = editorPhysicsTickWithinBudget,
                editorFoundationFrameStepWithinBudget = editorFoundationFrameStepWithinBudget,
                editorControllerWithinBudget = editorControllerWithinBudget,
                releaseCatchUpGate = "GAM9 standalone Windows smoke runner"
            });
            UnityEngine.Debug.Log($"GAM9_PERFORMANCE hardBudgetsPass={hardBudgetsPass} failures={string.Join(",", hardBudgetFailures)} editorAllocationClean={editorAllocationClean} editorWithinBudget=physics:{editorPhysicsTickWithinBudget},foundationStep:{editorFoundationFrameStepWithinBudget},catchUp:{editorCatchUpWithinBudget},controller:{editorControllerWithinBudget} physicsP95={physicsSummary.p95Ms:0.000}ms foundationStepP95={foundationFrameStepSummary.p95Ms:0.000}ms catchUpP95={catchUpSummary.p95Ms:0.000}ms controllerP95={controllerSummary.p95Ms:0.000}ms gcOffMax={allocationOff.maxBytes}B gcTraceMax={allocationTrace.maxBytes}B runtimeMemory={runtimeAllocatedMemoryBytes} traceReserved={runtime.AttemptTrace.ReservedStorageEstimateBytes}");
            Assert.That(hardBudgetsPass, Is.True, "A hard G1 memory, trace, or bounded-soak structural budget failed.");

            runtime.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator G1_OBSERVATION_TRACE_STORAGE_IMMUTABILITY_AND_STATIC_AUDIT()
        {
            yield return LoadQualificationScene();
            AssertSceneTopology();
            FoundationRuntime runtime = _bootstrap.Runtime;
            _bootstrap.enabled = false;
            _rig.enabled = false;
            _bar.enabled = false;

            _rig.StartPoweredNeutral();
            runtime.StepOne();
            PhysicalObservation first = runtime.CurrentObservation;
            Assert.That(first.BodyCount, Is.EqualTo(17));
            Assert.That(first.BodyAt(0).BodyId, Is.EqualTo("pelvis"));
            Assert.That(first.BodyAt(16).BodyId, Is.EqualTo("barbell"));

            PhysicalBodyObservation callerCopy = first.BodyAt(0);
            callerCopy = new PhysicalBodyObservation(
                callerCopy.BodyId,
                999f,
                new Vector3Value(999f, 999f, 999f),
                callerCopy.RotationWorldFromBody,
                callerCopy.LinearVelocityMetersPerSecond,
                callerCopy.AngularVelocityRadiansPerSecond);
            Assert.That(first.BodyAt(0).MassKilograms, Is.Not.EqualTo(callerCopy.MassKilograms));
            Assert.That(first.BodyAt(0).PositionMeters.X, Is.Not.EqualTo(callerCopy.PositionMeters.X));

            runtime.StepOne();
            Assert.That(runtime.PreviousObservation.SimulationTick, Is.EqualTo(first.SimulationTick));
            Assert.That(runtime.CurrentObservation.SimulationTick, Is.EqualTo(first.SimulationTick + 1ul));
            Assert.That(runtime.PreviousObservation.BodyAt(16).BodyId, Is.EqualTo("barbell"));
            Assert.That(runtime.CurrentObservation.BodyAt(16).BodyId, Is.EqualTo("barbell"));

            _rig.StartPoweredNeutral();
            runtime.BeginAttemptTrace();
            runtime.StepOne();
            AttemptTraceSample historicalSample = runtime.AttemptTrace.GetSample(0);
            G1BodyState[] historicalBodies = CaptureBodyStates(historicalSample.Observation);
            for (int index = 0; index < 300; index++)
                runtime.StepOne();
            runtime.EndAttemptTrace();

            Assert.That(runtime.AttemptTrace.Count, Is.EqualTo(301));
            Assert.That(runtime.AttemptTrace.RegisteredBodyCount, Is.EqualTo(17));
            Assert.That(runtime.AttemptTrace.ReservedBodyRecordCount, Is.EqualTo(
                runtime.AttemptTrace.Capacity * runtime.AttemptTrace.BodyCapacity));
            Assert.That(runtime.AttemptTrace.ReservedStorageEstimateBytes, Is.GreaterThan(0L));
            Assert.That(runtime.AttemptTrace.LogicalPayloadStorageBytes, Is.LessThan(25L * 1024L * 1024L));
            AssertHistoricalBodiesUnchanged(historicalSample.Observation, historicalBodies);

            HotPathAudit hotPathAudit = AuditObservationTraceHotPath();
            Assert.That(hotPathAudit.Pass, Is.True, hotPathAudit.Description);
            yield return null;
        }

        private static IEnumerator CaptureProfilerGcSamples(
            FoundationRuntime runtime,
            bool recording,
            AllocationCapture capture)
        {
            ProfilerRecorder recorder;
            string unavailableReason;
            if (!TryStartGcFrameRecorder(out recorder, out unavailableReason))
            {
                capture.Available = false;
                capture.UnavailableReason = unavailableReason;
                yield break;
            }

            bool recordingStarted = false;
            try
            {
                if (recording)
                {
                    runtime.BeginAttemptTrace();
                    recordingStarted = true;
                }

                for (int index = 0; index < 20; index++)
                {
                    runtime.StepOne();
                    yield return null;
                }

                for (int index = 0; index < capture.values.Length; index++)
                {
                    runtime.StepOne();
                    yield return null;
                    if (!recorder.Valid || recorder.Count == 0)
                    {
                        capture.Available = false;
                        capture.UnavailableReason = "ProfilerRecorder was valid but returned no frame samples.";
                        yield break;
                    }

                    capture.values[index] = recorder.LastValue;
                }

                capture.Available = true;
            }
            finally
            {
                if (recordingStarted && runtime.AttemptTrace.IsRecording)
                    runtime.EndAttemptTrace();
                recorder.Dispose();
            }
        }

        private static bool TryStartGcFrameRecorder(out ProfilerRecorder recorder, out string reason)
        {
            recorder = default(ProfilerRecorder);
            reason = string.Empty;
            try
            {
                recorder = ProfilerRecorder.StartNew(
                    ProfilerCategory.Internal,
                    "GC.Alloc",
                    128,
                    ProfilerRecorderOptions.SumAllSamplesInFrame |
                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            }
            catch (Exception exception)
            {
                reason = "ProfilerRecorder Internal/GC.Alloc unavailable: " + exception.Message;
                return false;
            }

            if (recorder.Valid)
                return true;

            recorder.Dispose();
            reason = "ProfilerRecorder Internal/GC.Alloc returned an invalid recorder.";
            return false;
        }

        private static HotPathAudit AuditObservationTraceHotPath()
        {
            string scriptsDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Assets", "Scripts");
            string[] files =
            {
                Path.Combine(scriptsDirectory, "Foundation", "Unity", "AuthoritativePhysicsScene.cs"),
                Path.Combine(scriptsDirectory, "Foundation", "Unity", "PhysicsTickDriver.cs"),
                Path.Combine(scriptsDirectory, "Foundation", "AttemptTrace.cs")
            };
            string[] methodNames = { "CaptureObservation", "StepOne", "Append" };
            bool pass = true;
            var findings = new List<string>();

            for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
            {
                string source = File.ReadAllText(files[fileIndex]);
                string methodName = methodNames[fileIndex];
                string method = ExtractMethodBody(source, methodName);
                if (method == null)
                {
                    pass = false;
                    findings.Add(Path.GetFileName(files[fileIndex]) + "." + methodName + ": method not found");
                    continue;
                }

                if (Regex.IsMatch(method, @"\bnew\s+[^;\r\n]*\["))
                {
                    pass = false;
                    findings.Add(Path.GetFileName(files[fileIndex]) + "." + methodName + ": managed array allocation");
                }
                if (Regex.IsMatch(method, @"\b(Enumerable|Select|Where|ToArray|ToList)\b"))
                {
                    pass = false;
                    findings.Add(Path.GetFileName(files[fileIndex]) + "." + methodName + ": LINQ/materializing query");
                }
                if (Regex.IsMatch(method, @"\b(String\.Format|string\.Format)\s*\(|\.ToString\s*\("))
                {
                    pass = false;
                    findings.Add(Path.GetFileName(files[fileIndex]) + "." + methodName + ": string formatting");
                }
                if (Regex.IsMatch(method, @"\bnew\s+object\s*\[|\bbox\s*\("))
                {
                    pass = false;
                    findings.Add(Path.GetFileName(files[fileIndex]) + "." + methodName + ": boxing/object allocation");
                }
            }

            return new HotPathAudit
            {
                Pass = pass,
                Description = pass
                    ? "PASS; CaptureObservation, PhysicsTickDriver.StepOne, and AttemptTrace.Append contain no managed arrays, LINQ/materialization, boxing, or string formatting."
                    : "FAIL; " + string.Join("; ", findings)
            };
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            Match signature = Regex.Match(
                source,
                @"\b(public|internal|private|protected)\s+(?:static\s+)?[^\r\n{;]+\b" + Regex.Escape(methodName) + @"\s*\(");
            if (!signature.Success)
                return null;

            int openingBrace = source.IndexOf('{', signature.Index);
            if (openingBrace < 0)
                return null;

            int depth = 0;
            for (int index = openingBrace; index < source.Length; index++)
            {
                if (source[index] == '{')
                    depth++;
                else if (source[index] == '}' && --depth == 0)
                    return source.Substring(openingBrace, index - openingBrace + 1);
            }
            return null;
        }

        private static G1BodyState[] CaptureBodyStates(PhysicalObservation observation)
        {
            var bodies = new G1BodyState[observation.BodyCount];
            for (int index = 0; index < bodies.Length; index++)
            {
                PhysicalBodyObservation body = observation.BodyAt(index);
                bodies[index] = new G1BodyState
                {
                    id = body.BodyId,
                    position = ToUnityVector(body.PositionMeters),
                    rotation = ToUnityQuaternion(body.RotationWorldFromBody),
                    linearVelocity = ToUnityVector(body.LinearVelocityMetersPerSecond),
                    angularVelocity = ToUnityVector(body.AngularVelocityRadiansPerSecond)
                };
            }
            return bodies;
        }

        private static void AssertHistoricalBodiesUnchanged(PhysicalObservation observation, G1BodyState[] expected)
        {
            Assert.That(observation.BodyCount, Is.EqualTo(expected.Length));
            for (int index = 0; index < expected.Length; index++)
            {
                PhysicalBodyObservation body = observation.BodyAt(index);
                Assert.That(body.BodyId, Is.EqualTo(expected[index].id));
                Assert.That(ToUnityVector(body.PositionMeters), Is.EqualTo(expected[index].position));
                Assert.That(ToUnityQuaternion(body.RotationWorldFromBody), Is.EqualTo(expected[index].rotation));
                Assert.That(ToUnityVector(body.LinearVelocityMetersPerSecond), Is.EqualTo(expected[index].linearVelocity));
                Assert.That(ToUnityVector(body.AngularVelocityRadiansPerSecond), Is.EqualTo(expected[index].angularVelocity));
            }
        }

        [UnityTearDown]
        public IEnumerator ShutdownQualificationScene()
        {
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
            yield return null;
        }

        private IEnumerator LoadQualificationScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The G1 qualification scene is missing from the project.");
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _bar = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_bootstrap.Runtime, Is.Not.Null);
            Assert.That(_bootstrap.Runtime.IsInitialized, Is.True);
            Assert.That(_rig, Is.Not.Null);
            Assert.That(_bar, Is.Not.Null);
            Assert.That(_bar.IsBuilt, Is.True);
        }

        private void AssertSceneTopology()
        {
            Assert.That(_rig.Segments.Count, Is.EqualTo(16));
            Assert.That(_rig.Joints.Count, Is.EqualTo(15));
            Assert.That(_rig.PoweredController.PoweredJointCount, Is.EqualTo(14));
            Assert.That(_rig.PoweredController.PassiveJointCount, Is.EqualTo(1));
            Assert.That(_bar.Body.GetComponentsInChildren<Rigidbody>(true), Has.Length.EqualTo(1));
            Assert.That(_bar.Body.isKinematic, Is.False);
            Assert.That(_bar.Body.useGravity, Is.True);
        }

        private static void AdvanceTicks(FoundationRuntime runtime, int count)
        {
            for (int index = 0; index < count; index++)
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
        }

        private static G1Snapshot[] RunManualSnapshots(FoundationRuntime runtime, PhysicalBarbell bar, int ticks)
        {
            var snapshots = new G1Snapshot[ticks];
            for (int index = 0; index < ticks; index++)
            {
                runtime.StepOne();
                snapshots[index] = CaptureSnapshot(runtime, bar);
            }
            return snapshots;
        }

        private static G1Snapshot CaptureSnapshot(FoundationRuntime runtime, PhysicalBarbell bar)
        {
            PhysicalObservation observation = runtime.CurrentObservation;
            Assert.That(observation.BodyCount, Is.EqualTo(17));
            var bodies = new G1BodyState[observation.BodyCount];
            for (int index = 0; index < bodies.Length; index++)
            {
                PhysicalBodyObservation body = observation.BodyAt(index);
                bodies[index] = new G1BodyState
                {
                    id = body.BodyId,
                    position = ToUnityVector(body.PositionMeters),
                    rotation = ToUnityQuaternion(body.RotationWorldFromBody),
                    linearVelocity = ToUnityVector(body.LinearVelocityMetersPerSecond),
                    angularVelocity = ToUnityVector(body.AngularVelocityRadiansPerSecond)
                };
            }
            return new G1Snapshot
            {
                Tick = observation.SimulationTick,
                TimeSeconds = observation.SimulationTimeSeconds,
                Bodies = bodies,
                BarMassKg = bar.LoadedMassKg,
                BarInertiaTensor = bar.Body.inertiaTensor
            };
        }

        private static void AssertPoweredControllerResetState(PoweredJointController controller)
        {
            foreach (PoweredJointController.PoweredJointRuntime joint in controller.Joints)
            {
                Assert.That(Quaternion.Angle(joint.AppliedTarget, Quaternion.identity), Is.LessThan(0.0001f), joint.Id);
                Assert.That(Quaternion.Angle(joint.RequestedCommand.TargetRelativeRotation, Quaternion.identity), Is.LessThan(0.0001f), joint.Id);
            }
        }

        private static float CompareSnapshotMetadata(G1Snapshot first, G1Snapshot second)
        {
            Assert.That(second.Tick, Is.EqualTo(first.Tick));
            Assert.That(second.TimeSeconds, Is.EqualTo(first.TimeSeconds).Within(0.000000001d));
            Assert.That(second.BarMassKg, Is.EqualTo(first.BarMassKg).Within(0.0001f));
            float barInertiaDelta = Vector3.Distance(first.BarInertiaTensor, second.BarInertiaTensor);
            Assert.That(barInertiaDelta, Is.LessThan(0.0001f));
            return barInertiaDelta;
        }

        private static float CompareSnapshots(
            G1Snapshot first,
            G1Snapshot second,
            float positionTolerance,
            float rotationTolerance,
            out float maximumLinearVelocityDelta,
            out float maximumAngularVelocityDelta)
        {
            Assert.That(second.Bodies.Length, Is.EqualTo(first.Bodies.Length));
            float maximumPositionDelta = 0f;
            maximumLinearVelocityDelta = 0f;
            maximumAngularVelocityDelta = 0f;
            for (int index = 0; index < first.Bodies.Length; index++)
            {
                Assert.That(second.Bodies[index].id, Is.EqualTo(first.Bodies[index].id));
                maximumPositionDelta = Mathf.Max(maximumPositionDelta, Vector3.Distance(first.Bodies[index].position, second.Bodies[index].position));
                float linearVelocityDelta = Vector3.Distance(first.Bodies[index].linearVelocity, second.Bodies[index].linearVelocity);
                float angularVelocityDelta = Vector3.Distance(first.Bodies[index].angularVelocity, second.Bodies[index].angularVelocity);
                maximumLinearVelocityDelta = Mathf.Max(maximumLinearVelocityDelta, linearVelocityDelta);
                maximumAngularVelocityDelta = Mathf.Max(maximumAngularVelocityDelta, angularVelocityDelta);
                Assert.That(linearVelocityDelta, Is.LessThan(positionTolerance));
                Assert.That(angularVelocityDelta, Is.LessThan(positionTolerance));
                Assert.That(Quaternion.Angle(first.Bodies[index].rotation, second.Bodies[index].rotation), Is.LessThan(rotationTolerance));
            }
            return maximumPositionDelta;
        }

        private static float CompareRotationSnapshots(G1Snapshot first, G1Snapshot second)
        {
            float maximumRotationDelta = 0f;
            for (int index = 0; index < first.Bodies.Length; index++)
                maximumRotationDelta = Mathf.Max(maximumRotationDelta, Quaternion.Angle(first.Bodies[index].rotation, second.Bodies[index].rotation));
            return maximumRotationDelta;
        }

        private static void AssertTraceShape(AttemptTrace trace, int bodyCount)
        {
            ulong previousTick = 0ul;
            for (int index = 0; index < trace.Count; index++)
            {
                AttemptTraceSample sample = trace.GetSample(index);
                Assert.That(sample.Observation.BodyCount, Is.EqualTo(bodyCount));
                Assert.That(sample.Intent.Tick, Is.EqualTo(sample.Tick));
                Assert.That(sample.Observation.TryGetBody("barbell", out PhysicalBodyObservation _), Is.True);
                if (index > 0)
                    Assert.That(sample.Tick, Is.GreaterThan(previousTick));
                previousTick = sample.Tick;
            }
        }

        private void AssertFiniteAthlete()
        {
            foreach (PhysicalAthleteRig.SegmentRuntime segment in _rig.Segments.Values)
            {
                Assert.That(Finite(segment.Body.position), Is.True, segment.Recipe.Id);
                Assert.That(Finite(segment.Body.rotation), Is.True, segment.Recipe.Id);
                Assert.That(Finite(segment.Body.linearVelocity), Is.True, segment.Recipe.Id);
                Assert.That(Finite(segment.Body.angularVelocity), Is.True, segment.Recipe.Id);
                Assert.That(segment.Body.linearVelocity.magnitude, Is.LessThan(100f), segment.Recipe.Id);
                Assert.That(segment.Body.angularVelocity.magnitude, Is.LessThan(100f), segment.Recipe.Id);
            }
        }

        private static JointDrive ActiveDrive(PoweredJointController.PoweredJointRuntime joint) =>
            joint.Recipe.Kind == PhysicalJointKind.Hinge ? joint.Joint.angularXDrive : joint.Joint.slerpDrive;

        private PhysicalAthleteRig.JointRuntime FindJoint(string childId)
        {
            for (int index = 0; index < _rig.Joints.Count; index++)
            {
                if (string.Equals(_rig.Joints[index].Recipe.ChildId, childId, StringComparison.Ordinal))
                    return _rig.Joints[index];
            }
            throw new AssertionException($"Joint '{childId}' was not found.");
        }

        private static Exception InvokeRuntimeValidation(PhysicalAthleteRig rig)
        {
            MethodInfo validator = typeof(PhysicalAthleteRig).GetMethod("ValidateRuntime", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(validator, Is.Not.Null);
            try
            {
                validator.Invoke(rig, null);
                return null;
            }
            catch (TargetInvocationException exception)
            {
                return exception.InnerException ?? exception;
            }
        }

        private static MutationResult Rejected(string id, string mutation, string gate, Exception error)
        {
            return new MutationResult
            {
                id = id,
                mutation = mutation,
                gate = gate,
                rejected = error != null,
                observed = error == null ? "accepted" : error.GetType().Name + ": " + error.Message
            };
        }

        private static SourceAudit AuditProductionSource()
        {
            string scriptsDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Assets", "Scripts");
            string[] files = Directory.GetFiles(scriptsDirectory, "*.cs", SearchOption.AllDirectories);
            var audit = new SourceAudit();
            for (int index = 0; index < files.Length; index++)
            {
                string source = File.ReadAllText(files[index]);
                audit.localSimulateCalls += Regex.Matches(source, @"\.\s*Simulate\s*\(").Count;
                audit.globalSimulateCalls += Regex.Matches(source, @"\bPhysics\s*\.\s*Simulate\s*\(").Count;
                audit.fixedUpdateCalls += Regex.Matches(source, @"\bFixedUpdate\s*\(").Count;
                audit.scriptedMotionCalls += Regex.Matches(source, @"\b(AddTorque|MovePosition|MoveRotation)\s*\(").Count;
            }
            return audit;
        }

        private static float[] CopyMasses(BarbellSideLayout side)
        {
            var masses = new float[side.PlatePlacements.Count];
            for (int index = 0; index < masses.Length; index++)
                masses[index] = side.PlatePlacements[index].MassKilograms;
            return masses;
        }

        private static bool SameMassSequence(float[] first, float[] second)
        {
            if (first.Length != second.Length)
                return false;
            for (int index = 0; index < first.Length; index++)
            {
                if (Mathf.Abs(first[index] - second[index]) > 0.0001f)
                    return false;
            }
            return true;
        }

        private static MetricSummary Summarize(double[] samples)
        {
            var sorted = new double[samples.Length];
            Array.Copy(samples, sorted, samples.Length);
            Array.Sort(sorted);
            return new MetricSummary
            {
                sampleCount = sorted.Length,
                p50Ms = Percentile(sorted, 0.50d),
                p95Ms = Percentile(sorted, 0.95d),
                p99Ms = Percentile(sorted, 0.99d),
                maxMs = sorted[sorted.Length - 1]
            };
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            if (sorted.Length == 0)
                return 0d;
            int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
            index = Math.Max(0, Math.Min(index, sorted.Length - 1));
            return sorted[index];
        }

        private static long Percentile(long[] sorted, double percentile)
        {
            if (sorted.Length == 0)
                return 0L;
            int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
            index = Math.Max(0, Math.Min(index, sorted.Length - 1));
            return sorted[index];
        }

        private static int CountComponents<T>(Scene scene) where T : Component
        {
            int count = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
                count += roots[index].GetComponentsInChildren<T>(true).Length;
            return count;
        }

        private static T FindNamedComponent<T>(Scene scene, string objectName) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
                {
                    if (string.Equals(transforms[transformIndex].name, objectName, StringComparison.Ordinal))
                        return transforms[transformIndex].GetComponent<T>();
                }
            }
            return null;
        }

        private static void CaptureEvidence(string filename)
        {
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            string directory = Path.GetFullPath(EvidenceDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, filename);
            RenderTexture texture = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(texture);
                UnityEngine.Object.DestroyImmediate(image);
            }
            Assert.That(File.Exists(path), Is.True, path);
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(1024L), path);
        }

        private static void WriteMeasurement<T>(string filename, T artifact)
        {
            string directory = Path.GetFullPath(MeasurementDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), JsonUtility.ToJson(artifact, true));
        }

        private static Vector3 ToUnityVector(Vector3Value value) => new Vector3(value.X, value.Y, value.Z);

        private static Quaternion ToUnityQuaternion(QuaternionValue value) => new Quaternion(value.X, value.Y, value.Z, value.W);

        private static float SignedXDegrees(Quaternion rotation)
        {
            Vector3 vector = new Vector3(rotation.x, rotation.y, rotation.z);
            Vector3 projection = Vector3.Project(vector, Vector3.right);
            Quaternion twist = PoweredJointController.NormalizeCanonical(new Quaternion(projection.x, projection.y, projection.z, rotation.w));
            twist.ToAngleAxis(out float angle, out Vector3 axis);
            return angle * Mathf.Sign(Vector3.Dot(axis, Vector3.right));
        }

        private static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static bool Finite(Quaternion value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);

        private sealed class G1Snapshot
        {
            public ulong Tick;
            public double TimeSeconds;
            public G1BodyState[] Bodies;
            public float BarMassKg;
            public Vector3 BarInertiaTensor;
        }

        private struct G1BodyState
        {
            public string id;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 linearVelocity;
            public Vector3 angularVelocity;
        }

        [Serializable]
        private sealed class G1QualificationArtifact
        {
            public string schema;
            public string status;
            public string unityVersion;
            public string scene;
            public string authoritativeScene;
            public int bodyCount;
            public int jointCount;
            public int poweredJointCount;
            public int passiveJointCount;
            public float totalMassKg;
            public float passiveComDropM;
            public float poweredComDropM;
            public float positivePulseDegrees;
            public float barDropM;
            public float barMassKg;
            public int observationBodyCount;
            public int traceSamples;
            public string[] evidence;
        }

        [Serializable]
        private sealed class RepeatabilityArtifact
        {
            public string schema;
            public string status;
            public int repeatCount;
            public int ticksPerRepeat;
            public int tracedRepeatCount;
            public float maxPositionDeltaM;
            public float maxRotationDeltaDeg;
            public float maxLinearVelocityDeltaMps;
            public float maxAngularVelocityDeltaRadS;
            public float maxTracePositionDeltaM;
            public float maxTraceRotationDeltaDeg;
            public float maxTraceLinearVelocityDeltaMps;
            public float maxTraceAngularVelocityDeltaRadS;
            public float barMassKg;
            public float maxBarInertiaDeltaKgM2;
            public ulong firstTick;
            public double firstSimulationTimeS;
            public int resetObservationBodyCount;
            public int resetPendingInputSamples;
        }

        [Serializable]
        private sealed class MutationArtifact
        {
            public string schema;
            public string status;
            public int productionPhysicsSimulateCall;
            public int productionGlobalPhysicsSimulateCalls;
            public int productionFixedUpdateCalls;
            public int productionScriptedMotionCalls;
            public MutationResult[] results;
        }

        [Serializable]
        private sealed class MutationResult
        {
            public string id;
            public string mutation;
            public string gate;
            public bool rejected;
            public string observed;
        }

        [Serializable]
        private sealed class PerformanceArtifact
        {
            public string schema;
            public string status;
            public string measurementMethod;
            public MetricSummary physicsTick;
            public AllocationMetricArtifact steadyStateGcOff;
            public AllocationMetricArtifact steadyStateGcTrace;
            public string allocationMetric;
            public bool editorAllocationClean;
            public string editorAllocationGate;
            public string releaseAllocationGate;
            public MetricSummary foundationFrameStep;
            public MetricSummary fourTickCatchUp;
            public MetricSummary controllerStep;
            public int normalTraceSamples;
            public long normalTraceAppendAllocationP95Bytes;
            public long normalTraceAppendAllocationMaxBytes;
            public long normalTraceBudgetBytes;
            public int traceCapacitySamples;
            public int traceRegisteredBodyCount;
            public int traceReservedBodyRecordCount;
            public long traceReservedBodyRecordStorageBytes;
            public long traceReservedStorageEstimateBytes;
            public long traceLogicalPayloadStorageBytes;
            public long traceCurrentLogicalPayloadStorageBytes;
            public int boundedSoakCycles;
            public int baselineRigidbodies;
            public int baselineJoints;
            public long baselineMemoryBytes;
            public long peakMemoryBytes;
            public long finalMemoryBytes;
            public long workingSetBytes;
            public long runtimeAllocatedMemoryBytes;
            public string memoryBudgetBasis;
            public double gpuP95Ms;
            public string gpuMeasurement;
            public bool staticHotPathAuditPass;
            public string staticHotPathAudit;
            public bool hardBudgetsPass;
            public string[] hardBudgetFailures;
            public bool editorCatchUpWithinBudget;
            public bool editorPhysicsTickWithinBudget;
            public bool editorFoundationFrameStepWithinBudget;
            public bool editorControllerWithinBudget;
            public string releaseCatchUpGate;
        }

        [Serializable]
        private sealed class AllocationMetricArtifact
        {
            public bool available;
            public string channel;
            public int sampleCount;
            public long p50Bytes;
            public long p95Bytes;
            public long p99Bytes;
            public long maxBytes;
            public string scope;
            public string unavailableReason;
        }

        [Serializable]
        private struct MetricSummary
        {
            public int sampleCount;
            public double p50Ms;
            public double p95Ms;
            public double p99Ms;
            public double maxMs;
        }

        private sealed class AllocationCapture
        {
            public AllocationCapture(int sampleCount)
            {
                values = new long[sampleCount];
                Channel = "GC.Alloc (current thread)";
                Scope = "Editor PlayMode frame; includes Unity Editor/Test Framework work and is not the release gate.";
            }

            public readonly long[] values;
            public string Channel { get; set; }
            public string Scope { get; set; }
            public bool Available { get; set; }
            public string UnavailableReason { get; set; }

            public AllocationMetricArtifact ToArtifact()
            {
                if (!Available)
                {
                    return new AllocationMetricArtifact
                    {
                        available = false,
                        channel = Channel,
                        sampleCount = 0,
                        p50Bytes = -1L,
                        p95Bytes = -1L,
                        p99Bytes = -1L,
                        maxBytes = -1L,
                        scope = Scope,
                        unavailableReason = UnavailableReason ?? "ProfilerRecorder channel unavailable."
                    };
                }

                long[] sorted = new long[values.Length];
                Array.Copy(values, sorted, values.Length);
                Array.Sort(sorted);
                return new AllocationMetricArtifact
                {
                    available = true,
                    channel = Channel,
                    sampleCount = sorted.Length,
                    p50Bytes = Percentile(sorted, 0.50d),
                    p95Bytes = Percentile(sorted, 0.95d),
                    p99Bytes = Percentile(sorted, 0.99d),
                    maxBytes = sorted[sorted.Length - 1],
                    scope = Scope,
                    unavailableReason = string.Empty
                };
            }
        }

        private sealed class HotPathAudit
        {
            public bool Pass;
            public string Description;
        }

        private sealed class SourceAudit
        {
            public int localSimulateCalls;
            public int globalSimulateCalls;
            public int fixedUpdateCalls;
            public int scriptedMotionCalls;
        }
    }
}
