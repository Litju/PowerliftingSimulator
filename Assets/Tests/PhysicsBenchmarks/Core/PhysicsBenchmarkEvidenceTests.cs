using System;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Squat.Unity;

namespace PowerliftingSimulator.PhysicsBenchmarks
{
    [Category("GAM50EvidenceWriter")]
    public sealed class GAM50EvidenceWriterTests
    {
        [Test]
        public void Writer_creates_absent_root_and_nested_parents()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "PowerliftingSimulator-GAM50-" + Guid.NewGuid().ToString("N"));
            string rawDirectory = Path.Combine(tempDirectory, "raw");
            string previousRawDirectory = Environment.GetEnvironmentVariable(PhysicsBenchmarkRecorder.RawDirectoryVariable);

            Assert.That(Directory.Exists(tempDirectory), Is.False);
            Assert.That(Directory.Exists(rawDirectory), Is.False);
            try
            {
                Environment.SetEnvironmentVariable(PhysicsBenchmarkRecorder.RawDirectoryVariable, rawDirectory);
                var recorder = new PhysicsBenchmarkRecorder("evidence-regression");
                recorder.Series("trace", "tick,value", "0,1");
                recorder.WriteAndAssert();

                string casePath = Path.Combine(rawDirectory, "evidence-regression.json");
                string seriesPath = Path.Combine(rawDirectory, "evidence-regression.trace.csv");
                Assert.That(File.Exists(casePath), Is.True);
                Assert.That(File.ReadAllText(casePath), Does.Contain("\"case\": \"evidence-regression\""));
                Assert.That(File.Exists(seriesPath), Is.True);
                Assert.That(File.ReadAllText(seriesPath), Does.Contain("tick,value"));
                Assert.That(File.ReadAllText(seriesPath), Does.Contain("0,1"));

                string oraclePath = Path.Combine(rawDirectory, "oracle", "nested", "oracle.json");
                Assert.That(Directory.Exists(Path.GetDirectoryName(oraclePath)), Is.False);
                PhysicsBenchmarkEvidence.WriteRawText(Path.Combine("oracle", "nested", "oracle.json"), "{\"stage\":\"nested\"}");
                Assert.That(File.Exists(oraclePath), Is.True);

                string receiptPath = Path.Combine(rawDirectory, "runtime-receipts", "nested", "receipt.json");
                PhysicsBenchmarkEvidence.WriteText(receiptPath, "{\"receipt\":true}");
                Assert.That(File.Exists(receiptPath), Is.True);
            }
            finally
            {
                Environment.SetEnvironmentVariable(PhysicsBenchmarkRecorder.RawDirectoryVariable, previousRawDirectory);
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, true);
            }
        }
    }
}
