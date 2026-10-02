using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM44InteractionContractTests
    {
        [Test]
        public void CORRECTED_300KG_CONTROL_CANNOT_PASS_BY_SADDLE_ATTACHMENT()
        {
            List<Dictionary<string, string>> rows = Read("Artifacts/Measurements/GAM-44/control/F1-S0-control.csv");

            Assert.That(Row(rows, 25f)["pass"], Is.EqualTo("true"));
            Assert.That(Row(rows, 170f)["pass"], Is.EqualTo("true"));
            Assert.That(Row(rows, 230f)["pass"], Is.EqualTo("false"));
            Assert.That(Row(rows, 270f)["pass"], Is.EqualTo("false"));
            Assert.That(Row(rows, 300f)["loaded_setup_valid"], Is.EqualTo("true"));
            Assert.That(Row(rows, 300f)["standing_gates_pass"], Is.EqualTo("false"));
            Assert.That(Row(rows, 300f)["pass"], Is.EqualTo("false"));
        }

        [Test]
        public void F2_SADDLE_MATRIX_HAS_ONLY_S1_AS_THE_HEAVY_STANDING_SURVIVOR()
        {
            var expected = new Dictionary<string, string[]>
            {
                ["F2-S0.csv"] = new[] { "true", "false", "false" },
                ["F2-S1.csv"] = new[] { "true", "true", "true" },
                ["F2-S2.csv"] = new[] { "false", "false", "false" }
            };

            foreach (KeyValuePair<string, string[]> arm in expected)
            {
                List<Dictionary<string, string>> rows = Read("Artifacts/Measurements/GAM-44/matrix/" + arm.Key);
                Assert.That(rows[0].ContainsKey("max_saddle_linear_limit_occupancy"), Is.True);
                Assert.That(rows[0].ContainsKey("max_saddle_angular_limit_occupancy"), Is.True);
                Assert.That(Row(rows, 230f)["pass"], Is.EqualTo(arm.Value[0]), arm.Key);
                Assert.That(Row(rows, 270f)["pass"], Is.EqualTo(arm.Value[1]), arm.Key);
                Assert.That(Row(rows, 300f)["pass"], Is.EqualTo(arm.Value[2]), arm.Key);
            }
        }

        [Test]
        public void F2_S1_HOLDOUTS_AND_FRESH_REPEATS_ALL_PASS()
        {
            List<Dictionary<string, string>> full = Read("Artifacts/Measurements/GAM-44/qualification/F2-S1-full.csv");
            List<Dictionary<string, string>> repeats = Read("Artifacts/Measurements/GAM-44/qualification/F2-S1-repeats3.csv");

            Assert.That(full.Count, Is.EqualTo(9));
            Assert.That(repeats.Count, Is.EqualTo(15));
            Assert.That(full.Exists(row => row["pass"] != "true"), Is.False);
            Assert.That(repeats.Exists(row => row["pass"] != "true"), Is.False);
            foreach (float loadKg in new[] { 25f, 60f, 140f, 170f, 300f })
                Assert.That(repeats.FindAll(row => float.Parse(row["load_kg"], CultureInfo.InvariantCulture) == loadKg).Count, Is.EqualTo(3), loadKg.ToString(CultureInfo.InvariantCulture));
        }

        private static Dictionary<string, string> Row(List<Dictionary<string, string>> rows, float loadKg) =>
            rows.Find(row => Math.Abs(float.Parse(row["load_kg"], CultureInfo.InvariantCulture) - loadKg) < 0.001f);

        private static List<Dictionary<string, string>> Read(string relativePath)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            Assert.That(File.Exists(path), Is.True, "Missing GAM-44 evidence: " + relativePath);
            string[] lines = File.ReadAllLines(path);
            string[] header = lines[0].Split(',');
            var rows = new List<Dictionary<string, string>>(lines.Length - 1);
            for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                if (string.IsNullOrWhiteSpace(lines[lineIndex]))
                    continue;
                string[] values = lines[lineIndex].Split(',');
                Assert.That(values.Length, Is.EqualTo(header.Length), relativePath + " row " + lineIndex);
                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int column = 0; column < header.Length; column++)
                    row[header[column]] = values[column].Trim('"');
                rows.Add(row);
            }
            return rows;
        }
    }
}
