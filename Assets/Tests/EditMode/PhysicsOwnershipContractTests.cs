using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    public sealed class PhysicsOwnershipContractTests
    {
        [Test]
        public void PhysicsTickDriverContainsTheOnlyPhysicsSceneSimulationCall()
        {
            string sourcePath = Path.Combine(
                Application.dataPath,
                "Scripts",
                "Foundation",
                "Unity",
                "PhysicsTickDriver.cs");
            string source = File.ReadAllText(sourcePath);

            Assert.That(Regex.Matches(source, @"\.Simulate\s*\(").Count, Is.EqualTo(1));
            Assert.That(source, Does.Contain("PhysicsSceneHandle.Simulate"));
        }
    }
}
