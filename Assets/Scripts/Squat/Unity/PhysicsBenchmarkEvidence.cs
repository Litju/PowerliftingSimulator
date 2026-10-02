using System;
using System.IO;
using System.Text;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// Owns path creation for benchmark evidence writes, including nested outputs.
    /// </summary>
    public static class PhysicsBenchmarkEvidence
    {
        public const string RawDirectoryVariable = "PHYSICS_BENCHMARK_RAW_DIR";
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

        public static string RawDirectory()
        {
            string configured = Environment.GetEnvironmentVariable(RawDirectoryVariable);
            if (string.IsNullOrWhiteSpace(configured))
                configured = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts", "Benchmarks", "Physics", "_local", "raw");
            return Directory.CreateDirectory(configured).FullName;
        }

        public static string RawPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new ArgumentException("Benchmark evidence paths must be non-empty and relative.", nameof(relativePath));
            return Path.Combine(RawDirectory(), relativePath);
        }

        public static void WriteRawText(string relativePath, string contents) =>
            WriteText(RawPath(relativePath), contents);

        public static void WriteText(string path, string contents)
        {
            string fullPath = Path.GetFullPath(path);
            string parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(parent))
                throw new ArgumentException("Benchmark evidence files require a parent directory.", nameof(path));
            Directory.CreateDirectory(parent);
            File.WriteAllText(fullPath, contents, Utf8WithoutBom);
        }
    }
}
