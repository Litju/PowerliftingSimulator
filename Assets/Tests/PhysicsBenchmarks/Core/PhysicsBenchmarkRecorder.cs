using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Squat.Unity;

namespace PowerliftingSimulator.PhysicsBenchmarks
{
    /// <summary>
    /// Earliest-causal-layer taxonomy from GAM-50 section 7. A failing row is
    /// always attributed to the lowest layer the evidence implicates.
    /// </summary>
    public enum CausalLayer
    {
        UnitsFramesEquations = 1,
        MassComInertia = 2,
        JointTopologyTargetConvention = 3,
        DriveSemantics = 4,
        ConstraintConvergence = 5,
        ContactFriction = 6,
        BarSaddleLoadPath = 7,
        NumericalConvergence = 8,
        AthleteEquilibrium = 9,
        Controller = 10,
        GameQualification = 11
    }

    public enum ToleranceKind
    {
        Absolute,
        Relative,
        /// <summary>Observed must be at most the tolerance (expected is a bound).</summary>
        UpperBound,
        /// <summary>Observed must be at least the tolerance.</summary>
        LowerBound,
        /// <summary>Observed is recorded but never gates (characterization).</summary>
        Informational
    }

    public sealed class BenchmarkMetric
    {
        public string Case;
        public string Metric;
        public string Configuration;
        public double Expected;
        public double Observed;
        public double Tolerance;
        public ToleranceKind Kind;
        public string ToleranceSource;
        public string FailureMeaning;
        public CausalLayer Layer;

        public double AbsoluteError => Math.Abs(Observed - Expected);

        public double RelativeError =>
            Math.Abs(Expected) > 1e-12 ? Math.Abs(Observed - Expected) / Math.Abs(Expected) : double.NaN;

        public bool Pass
        {
            get
            {
                if (double.IsNaN(Observed) || double.IsInfinity(Observed))
                    return Kind == ToleranceKind.Informational;
                switch (Kind)
                {
                    case ToleranceKind.Absolute: return AbsoluteError <= Tolerance;
                    case ToleranceKind.Relative:
                        return Math.Abs(Expected) > 1e-12
                            ? RelativeError <= Tolerance
                            : AbsoluteError <= Tolerance;
                    case ToleranceKind.UpperBound: return Observed <= Tolerance;
                    case ToleranceKind.LowerBound: return Observed >= Tolerance;
                    default: return true;
                }
            }
        }
    }

    /// <summary>
    /// Collects benchmark metrics for one benchmark case and writes them as a
    /// raw JSON file the offline comparator aggregates. A case never decides
    /// its own verdict in prose: every row carries expected, observed, the
    /// tolerance, where the tolerance came from, and what a failure means.
    /// </summary>
    public sealed class PhysicsBenchmarkRecorder
    {
        public const string RawDirectoryVariable = PhysicsBenchmarkEvidence.RawDirectoryVariable;
        public const string SchemaVersion = "PHYSICS_BENCHMARK_V1";

        private readonly string _caseId;
        private readonly List<BenchmarkMetric> _metrics = new List<BenchmarkMetric>();
        private readonly List<KeyValuePair<string, string>> _notes = new List<KeyValuePair<string, string>>();
        private readonly Dictionary<string, StringBuilder> _series = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);

        public PhysicsBenchmarkRecorder(string caseId)
        {
            _caseId = caseId ?? throw new ArgumentNullException(nameof(caseId));
        }

        public IReadOnlyList<BenchmarkMetric> Metrics => _metrics;

        public BenchmarkMetric Record(
            string metric,
            string configuration,
            double expected,
            double observed,
            double tolerance,
            ToleranceKind kind,
            string toleranceSource,
            string failureMeaning,
            CausalLayer layer)
        {
            var row = new BenchmarkMetric
            {
                Case = _caseId,
                Metric = metric,
                Configuration = configuration ?? string.Empty,
                Expected = expected,
                Observed = observed,
                Tolerance = tolerance,
                Kind = kind,
                ToleranceSource = toleranceSource,
                FailureMeaning = failureMeaning,
                Layer = layer
            };
            _metrics.Add(row);
            return row;
        }

        public void Info(string metric, string configuration, double observed, string meaning, CausalLayer layer) =>
            Record(metric, configuration, double.NaN, observed, double.NaN, ToleranceKind.Informational,
                "characterization only", meaning, layer);

        public void Note(string key, string value) => _notes.Add(new KeyValuePair<string, string>(key, value));

        /// <summary>Appends one CSV row to a named raw time series.</summary>
        public void Series(string name, string header, string row)
        {
            if (!_series.TryGetValue(name, out StringBuilder builder))
            {
                builder = new StringBuilder();
                builder.AppendLine(header);
                _series.Add(name, builder);
            }
            builder.AppendLine(row);
        }

        public static string RawDirectory()
        {
            return PhysicsBenchmarkEvidence.RawDirectory();
        }

        /// <summary>
        /// Writes the raw case file and fails the NUnit test when any gated
        /// metric fails. The raw evidence is written first, so a red case
        /// still leaves its full record behind for the comparator.
        /// </summary>
        public void WriteAndAssert()
        {
            string path = PhysicsBenchmarkEvidence.RawPath(_caseId + ".json");
            PhysicsBenchmarkEvidence.WriteText(path, ToJson());
            foreach (KeyValuePair<string, StringBuilder> series in _series)
            {
                PhysicsBenchmarkEvidence.WriteRawText(_caseId + "." + series.Key + ".csv", series.Value.ToString());
            }

            var failures = new StringBuilder();
            foreach (BenchmarkMetric metric in _metrics)
            {
                if (metric.Kind == ToleranceKind.Informational || metric.Pass)
                    continue;
                failures.AppendFormat(CultureInfo.InvariantCulture,
                    "{0}[{1}] expected={2:R} observed={3:R} tol={4:R} ({5}) -> {6}\n",
                    metric.Metric, metric.Configuration, metric.Expected, metric.Observed,
                    metric.Tolerance, metric.Kind, metric.FailureMeaning);
            }
            UnityEngine.Debug.Log($"PHYSICS_BENCHMARK case={_caseId} metrics={_metrics.Count} raw={path}");
            Assert.That(failures.Length, Is.EqualTo(0), $"Benchmark {_caseId} failed:\n{failures}");
        }

        private string ToJson()
        {
            var json = new StringBuilder();
            json.Append("{\n  \"schema\": ").Append(Quote(SchemaVersion));
            json.Append(",\n  \"case\": ").Append(Quote(_caseId));
            json.Append(",\n  \"unity_version\": ").Append(Quote(UnityEngine.Application.unityVersion));
            json.Append(",\n  \"platform\": ").Append(Quote(UnityEngine.Application.platform.ToString()));
            json.Append(",\n  \"notes\": {");
            for (int i = 0; i < _notes.Count; i++)
            {
                json.Append(i == 0 ? "\n    " : ",\n    ")
                    .Append(Quote(_notes[i].Key)).Append(": ").Append(Quote(_notes[i].Value));
            }
            json.Append(_notes.Count > 0 ? "\n  }" : "}");
            json.Append(",\n  \"metrics\": [");
            for (int i = 0; i < _metrics.Count; i++)
            {
                BenchmarkMetric m = _metrics[i];
                json.Append(i == 0 ? "\n    {" : ",\n    {");
                json.Append("\"case\": ").Append(Quote(m.Case));
                json.Append(", \"metric\": ").Append(Quote(m.Metric));
                json.Append(", \"configuration\": ").Append(Quote(m.Configuration));
                json.Append(", \"expected\": ").Append(Number(m.Expected));
                json.Append(", \"observed\": ").Append(Number(m.Observed));
                json.Append(", \"abs_error\": ").Append(Number(m.AbsoluteError));
                json.Append(", \"rel_error\": ").Append(Number(m.RelativeError));
                json.Append(", \"tolerance\": ").Append(Number(m.Tolerance));
                json.Append(", \"tolerance_kind\": ").Append(Quote(m.Kind.ToString()));
                json.Append(", \"tolerance_source\": ").Append(Quote(m.ToleranceSource));
                json.Append(", \"pass\": ").Append(m.Pass ? "true" : "false");
                json.Append(", \"gated\": ").Append(m.Kind == ToleranceKind.Informational ? "false" : "true");
                json.Append(", \"failure_meaning\": ").Append(Quote(m.FailureMeaning));
                json.Append(", \"layer\": ").Append(((int)m.Layer).ToString(CultureInfo.InvariantCulture));
                json.Append(", \"layer_name\": ").Append(Quote(m.Layer.ToString()));
                json.Append('}');
            }
            json.Append(_metrics.Count > 0 ? "\n  ]\n}\n" : "]\n}\n");
            return json.ToString();
        }

        public static string Number(double value) =>
            double.IsNaN(value) || double.IsInfinity(value)
                ? "null"
                : value.ToString("R", CultureInfo.InvariantCulture);

        public static string Quote(string value)
        {
            if (value == null)
                return "null";
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            builder.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                        else
                            builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        public static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
