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
            // Outside the hull edge (1,0)-(0,1) by (0.75+0.75-1)/sqrt(2).
            Assert.That(measurement.HullSignedMarginM, Is.EqualTo(-0.5f / Mathf.Sqrt(2f)).Within(1e-5f));
            Assert.That(measurement.AabbSignedMarginM, Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(measurement.AabbApSignedMarginM, Is.EqualTo(0.25f).Within(1e-5f));
        }

        [Test]
        public void SignedMarginsAreExactEuclideanDistances()
        {
            Vector3[] square =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0.2f, 0f, 0f),
                new Vector3(0.2f, 0f, 0.3f),
                new Vector3(0f, 0f, 0.3f)
            };

            SquatSupportGeometry.Measurement inside =
                SquatSupportGeometry.Measure(square, new Vector2(0.05f, 0.1f));
            Assert.That(inside.HullContainsQuery, Is.True);
            Assert.That(inside.HullSignedMarginM, Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(inside.AabbSignedMarginM, Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(inside.AabbApSignedMarginM, Is.EqualTo(0.1f).Within(1e-6f));

            // Diagonally outside the (0.2, 0.3) corner by (0.03, 0.04): 0.05 m.
            SquatSupportGeometry.Measurement corner =
                SquatSupportGeometry.Measure(square, new Vector2(0.23f, 0.34f));
            Assert.That(corner.HullContainsQuery, Is.False);
            Assert.That(corner.HullSignedMarginM, Is.EqualTo(-0.05f).Within(1e-6f));
            Assert.That(corner.AabbSignedMarginM, Is.EqualTo(-0.05f).Within(1e-6f));
            Assert.That(corner.AabbApSignedMarginM, Is.EqualTo(-0.04f).Within(1e-6f));
        }

        [Test]
        public void ApOnlyProxyIgnoresMediolateralExit()
        {
            Vector3[] square =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0.2f, 0f, 0f),
                new Vector3(0.2f, 0f, 0.3f),
                new Vector3(0f, 0f, 0.3f)
            };

            SquatSupportGeometry.Measurement lateral =
                SquatSupportGeometry.Measure(square, new Vector2(0.26f, 0.15f));

            Assert.That(lateral.AabbApSignedMarginM, Is.EqualTo(0.15f).Within(1e-6f));
            Assert.That(lateral.AabbSignedMarginM, Is.EqualTo(-0.06f).Within(1e-6f));
            Assert.That(lateral.HullSignedMarginM, Is.EqualTo(-0.06f).Within(1e-6f));
        }

        [Test]
        public void CollinearContactsHaveNoInterior()
        {
            Vector3[] line =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 0.1f),
                new Vector3(0f, 0f, 0.2f)
            };

            SquatSupportGeometry.Measurement measurement =
                SquatSupportGeometry.Measure(line, new Vector2(0.03f, 0.1f));

            Assert.That(measurement.HullPointCount, Is.EqualTo(2));
            Assert.That(measurement.HullAreaM2, Is.EqualTo(0f));
            Assert.That(measurement.HullContainsQuery, Is.False);
            Assert.That(measurement.HullSignedMarginM, Is.EqualTo(-0.03f).Within(1e-6f));
        }

        [Test]
        public void NoContactsReportsNotObservable()
        {
            SquatSupportGeometry.Measurement measurement =
                SquatSupportGeometry.Measure(new Vector3[0], Vector2.zero);

            Assert.That(measurement.ContactCount, Is.EqualTo(0));
            Assert.That(float.IsNaN(measurement.HullSignedMarginM), Is.True);
            Assert.That(float.IsNaN(measurement.AabbSignedMarginM), Is.True);
        }
    }
}
