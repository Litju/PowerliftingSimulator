using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Squat;

namespace PowerliftingSimulator.Tests
{
    public sealed class SquatObservationTraceContractTests
    {
        [Test]
        public void SnapshotCopiesSourceValuesIntoImmutableValueState()
        {
            SquatBarObservation sourceBar = SquatBarObservation.Available(
                new Vector3Value(1f, 2f, 3f),
                new Vector3Value(4f, 5f, 6f),
                QuaternionValue.Identity,
                new Vector3Value(0.1f, 0.2f, 0.3f),
                25f,
                SquatTelemetryAvailability.AVAILABLE,
                true,
                false,
                0.01f);
            PlayerIntentFrame sourceIntent = Intent(1ul, 0.01d, 0.25f);
            SquatObservationSnapshot snapshot = Snapshot(1ul, 0.01d, sourceBar, sourceIntent);

            sourceBar = SquatBarObservation.Unavailable();
            sourceIntent = Intent(1ul, 0.01d, 0.95f);

            Assert.That(snapshot.Bar.Availability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
            Assert.That(snapshot.Bar.PositionWorldMeters.X, Is.EqualTo(1f));
            Assert.That(snapshot.Intent.Drive01, Is.EqualTo(0.25f));
        }

        [Test]
        public void TraceRejectsDuplicateOrBackwardTickAndTime()
        {
            SquatTrace trace = new SquatTrace(4);
            trace.BeginRecording();
            trace.Append(Snapshot(1ul, 0.01d));

            Assert.Throws<InvalidOperationException>(() => trace.Append(Snapshot(1ul, 0.02d)));
            Assert.Throws<InvalidOperationException>(() => trace.Append(Snapshot(2ul, 0.005d)));
        }

        [Test]
        public void TraceStoresCapacityExactlyAndRejectsOverflow()
        {
            SquatTrace trace = new SquatTrace(2);
            trace.BeginRecording();
            trace.Append(Snapshot(1ul, 0.01d));
            trace.Append(Snapshot(2ul, 0.02d));

            Assert.That(trace.Count, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => trace.Append(Snapshot(3ul, 0.03d)));
        }

        [Test]
        public void TraceFreezesAfterFinalize()
        {
            SquatTrace trace = new SquatTrace(2);
            trace.BeginRecording();
            trace.Append(Snapshot(1ul, 0.01d));
            trace.EndRecording();

            Assert.That(trace.IsFrozen, Is.True);
            Assert.Throws<InvalidOperationException>(() => trace.Append(Snapshot(2ul, 0.02d)));
        }

        [Test]
        public void MissingBarIsExplicitlyUnavailableAndNeverAValidZero()
        {
            SquatObservationSnapshot snapshot = Snapshot(1ul, 0.01d);

            Assert.That(snapshot.Bar.Availability, Is.EqualTo(SquatTelemetryAvailability.NOT_AVAILABLE));
            Assert.That(float.IsNaN(snapshot.Bar.LoadKilograms), Is.True);
            Assert.That(float.IsNaN(snapshot.Bar.PositionWorldMeters.X), Is.True);
            Assert.That(float.IsNaN(snapshot.Bar.LinearVelocityWorldMetersPerSecond.X), Is.True);
            Assert.That(float.IsNaN(snapshot.Bar.AngularVelocityBarRadiansPerSecond.X), Is.True);
        }

        [Test]
        public void ChannelCatalogIsCompleteAndVersioned()
        {
            Assert.DoesNotThrow(() => SquatTelemetrySchema.ValidateComplete());
            Assert.That(SquatTelemetrySchema.ChannelCount, Is.EqualTo((int)SquatTelemetryChannelId.Count));

            for (int index = 0; index < SquatTelemetrySchema.ChannelCount; index++)
            {
                SquatTelemetryChannelDescriptor descriptor = SquatTelemetrySchema.ChannelAt(index);
                Assert.That(descriptor.CanonicalName, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.Unit, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.FrameId, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.ProvenanceVersion, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.ClaimNote, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        public void RawBilateralLandmarksReproduceDepthMarginsDeterministically()
        {
            SquatDepthLandmarks landmarks = new SquatDepthLandmarks(
                0.90f,
                0.91f,
                0.95f,
                0.94f,
                0.005f);

            Assert.That(landmarks.LeftDepthM, Is.EqualTo(-0.05f).Within(0.000001f));
            Assert.That(landmarks.RightDepthM, Is.EqualTo(-0.03f).Within(0.000001f));
            Assert.That(landmarks.WorstSideDepthM, Is.EqualTo(-0.03f).Within(0.000001f));
            Assert.That(landmarks.DepthMarginM, Is.EqualTo(0.005f));
        }

        [Test]
        public void SourceClassificationKeepsModelledDemandOutOfDirectPhysics()
        {
            SquatTelemetryChannelDescriptor demand = SquatTelemetrySchema.GetDescriptor(
                SquatTelemetryChannelId.MaximumModeledDemand);
            SquatTelemetryChannelDescriptor velocity = SquatTelemetrySchema.GetDescriptor(
                SquatTelemetryChannelId.BarLinearVelocityWorld);
            SquatTelemetryChannelDescriptor unavailable = SquatTelemetrySchema.GetDescriptor(
                SquatTelemetryChannelId.UnmeasuredGroundReactionForce);

            Assert.That(demand.SourceClass, Is.EqualTo(SquatTelemetrySourceClass.GAME_MODEL));
            Assert.That(velocity.SourceClass, Is.EqualTo(SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION));
            Assert.That(unavailable.SourceClass, Is.EqualTo(SquatTelemetrySourceClass.NOT_OBSERVABLE));
            Assert.That(SquatTelemetrySchema.CanReceiveRuntimeValue(
                SquatTelemetryChannelId.UnmeasuredGroundReactionForce), Is.False);
        }

        [Test]
        public void NewPureObservationFilesDoNotReferenceUnityEngine()
        {
            string[] relativePaths =
            {
                "Assets/Scripts/Squat/SquatObservationSnapshot.cs",
                "Assets/Scripts/Squat/SquatTrace.cs",
                "Assets/Scripts/Squat/SquatTelemetrySchema.cs"
            };

            foreach (string relativePath in relativePaths)
            {
                string path = Path.GetFullPath(relativePath);
                Assert.That(File.Exists(path), Is.True, path);
                Assert.That(File.ReadAllText(path), Does.Not.Contain("UnityEngine"), path);
            }
        }

        [Test]
        public void IdenticalInputsProduceIdenticalStoredTraces()
        {
            SquatObservationSnapshot[] snapshots =
            {
                Snapshot(1ul, 0.01d),
                Snapshot(2ul, 0.02d),
                Snapshot(3ul, 0.03d)
            };
            SquatTrace first = new SquatTrace(3);
            SquatTrace second = new SquatTrace(3);
            first.BeginRecording();
            second.BeginRecording();
            for (int index = 0; index < snapshots.Length; index++)
            {
                first.Append(snapshots[index]);
                second.Append(snapshots[index]);
            }

            for (int index = 0; index < snapshots.Length; index++)
            {
                SquatObservationSnapshot left = first.GetSnapshot(index);
                SquatObservationSnapshot right = second.GetSnapshot(index);
                Assert.That(left.SimulationTick, Is.EqualTo(right.SimulationTick));
                Assert.That(left.SimulationTimeSeconds, Is.EqualTo(right.SimulationTimeSeconds));
                Assert.That(left.Depth.WorstSideDepthM, Is.EqualTo(right.Depth.WorstSideDepthM));
                Assert.That(left.Intent.Edges, Is.EqualTo(right.Intent.Edges));
            }
        }

        [Test]
        public void CanonicalObservationHasNoCameraOrScreenTruth()
        {
            foreach (FieldInfo field in typeof(SquatObservationSnapshot).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.That(field.Name, Does.Not.Contain("Camera"));
                Assert.That(field.Name, Does.Not.Contain("Screen"));
            }

            for (int index = 0; index < SquatTelemetrySchema.ChannelCount; index++)
            {
                SquatTelemetryChannelDescriptor descriptor = SquatTelemetrySchema.ChannelAt(index);
                Assert.That(descriptor.CanonicalName, Does.Not.Contain("camera"));
                Assert.That(descriptor.CanonicalName, Does.Not.Contain("screen"));
            }
        }

        private static SquatObservationSnapshot Snapshot(
            ulong tick,
            double time,
            SquatBarObservation bar = default,
            PlayerIntentFrame intent = default)
        {
            if (intent.Tick == 0ul && tick != 0ul)
                intent = Intent(tick, time, 0.25f);

            return new SquatObservationSnapshot(
                tick,
                time,
                SimulationConstants.FixedDeltaTimeSeconds,
                true,
                (tick - 1ul) * SimulationConstants.FixedDeltaTimeSeconds,
                SquatState.DESCENT,
                SquatPhaseDirection.Descent,
                0.25f,
                SquatIntentSnapshot.From(intent),
                bar.Availability == 0 ? SquatBarObservation.Unavailable() : bar,
                new SquatDepthLandmarks(0.90f, 0.91f, 0.95f, 0.94f, 0.005f),
                SquatSupportObservation.Unavailable(),
                SquatFootObservation.Unavailable(),
                SquatFootObservation.Unavailable(),
                new SquatJointObservationSet(
                    SquatJointObservation.Unavailable(),
                    SquatJointObservation.Unavailable(),
                    SquatJointObservation.Unavailable(),
                    SquatJointObservation.Unavailable(),
                    SquatJointObservation.Unavailable(),
                    SquatJointObservation.Unavailable(),
                    SquatJointObservation.Unavailable(),
                    SquatJointObservation.Unavailable()),
                SquatTelemetryValue.UnavailableVector3,
                SquatTelemetryValue.UnavailableVector3,
                SquatTelemetryValue.UnavailableQuaternion,
                float.NaN,
                SquatTelemetryAvailability.NOT_AVAILABLE,
                false,
                float.NaN,
                SquatTelemetryQualityFlags.POST_PHYSICS | SquatTelemetryQualityFlags.RAW);
        }

        private static PlayerIntentFrame Intent(ulong tick, double time, float drive)
        {
            return new PlayerIntentFrame(
                tick,
                time,
                IntentEdgeFlags.None,
                0.5f,
                0.75f,
                drive,
                -0.1f,
                1f,
                true,
                true,
                drive > 0f,
                false,
                true,
                false,
                false);
        }
    }
}
