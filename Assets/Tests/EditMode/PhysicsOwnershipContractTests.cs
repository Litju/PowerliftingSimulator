using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    public sealed class PhysicsOwnershipContractTests
    {
        private static readonly Regex SimulateCall = new Regex(@"\.\s*Simulate\s*\(", RegexOptions.CultureInvariant);
        private static readonly Regex StepOneDeclaration = new Regex(
            @"\bpublic\s+void\s+StepOne\s*\(\s*\)",
            RegexOptions.CultureInvariant);

        [Test]
        public void SOLE_PRODUCTION_SIMULATE_CALL()
        {
            List<SimulationCall> calls = FindProductionSimulationCalls();

            Assert.That(calls, Has.Count.EqualTo(1), "Production physics ownership must have exactly one PhysicsScene.Simulate call.");

            SimulationCall acceptedCall = calls[0];
            string expectedPath = NormalizePath(Path.Combine(
                Application.dataPath,
                "Scripts",
                "Foundation",
                "Unity",
                "PhysicsTickDriver.cs"));
            Assert.That(acceptedCall.Path, Is.EqualTo(expectedPath));

            Match stepOne = StepOneDeclaration.Match(acceptedCall.Source);
            Assert.That(stepOne.Success, Is.True, "The accepted Simulate call must be in PhysicsTickDriver.StepOne.");
            Assert.That(acceptedCall.MatchIndex, Is.GreaterThan(stepOne.Index));
            Assert.That(acceptedCall.MatchIndex, Is.LessThan(FindNextMethodIndex(acceptedCall.Source, stepOne.Index)));
            Assert.That(acceptedCall.Source, Does.Contain("_authoritativeScene.PhysicsSceneHandle.Simulate"));
        }

        private static List<SimulationCall> FindProductionSimulationCalls()
        {
            var calls = new List<SimulationCall>();
            foreach (string path in Directory.EnumerateFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsProductionSource(path))
                    continue;

                string source = File.ReadAllText(path);
                foreach (Match match in SimulateCall.Matches(source))
                    calls.Add(new SimulationCall(NormalizePath(path), source, match.Index));
            }

            return calls;
        }

        private static bool IsProductionSource(string path)
        {
            string normalized = NormalizePath(path);
            string fileName = Path.GetFileName(normalized);
            if (fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
                return false;

            string[] segments = normalized.Split('/');
            foreach (string segment in segments)
            {
                if (segment.Equals("Tests", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("Generated", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("Library", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("Packages", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("Build", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("Builds", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("Temp", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("Obj", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("bin", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private static int FindNextMethodIndex(string source, int stepOneIndex)
        {
            int nextMethodIndex = source.IndexOf("public ", stepOneIndex + 1, StringComparison.Ordinal);
            return nextMethodIndex < 0 ? source.Length : nextMethodIndex;
        }

        private static string NormalizePath(string path) => path.Replace('\\', '/');

        private readonly struct SimulationCall
        {
            public SimulationCall(string path, string source, int matchIndex)
            {
                Path = path;
                Source = source;
                MatchIndex = matchIndex;
            }

            public string Path { get; }
            public string Source { get; }
            public int MatchIndex { get; }
        }
    }
}
