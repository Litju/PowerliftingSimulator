using NUnit.Framework;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM13SupportGeometryTests
    {
        [Test]
        public void ConvexHullSeparatesConcaveContactSetFromAabbProxy()
        {
            Vector3[] contacts =
            {
                new Vector3(-1f, 0f, -1f),
                new Vector3(1f, 0f, -1f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 0f, 1f),
                new Vector3(-1f, 0f, 1f)
            };

            SquatSupportGeometry.Measurement measurement =
                SquatSupportGeometry.Measure(contacts, new Vector2(0.75f, 0.75f));

            Assert.That(measurement.HullPointCount, Is.EqualTo(5));
            Assert.That(measurement.HullAreaM2, Is.EqualTo(3.5f).Within(1e-5f));
            Assert.That(measurement.AabbAreaM2, Is.EqualTo(4f).Within(1e-5f));
            Assert.That(measurement.AabbContainsQuery, Is.True);
            Assert.That(measurement.HullContainsQuery, Is.False);
        }
    }
}
