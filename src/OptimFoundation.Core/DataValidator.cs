using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OptimFoundation.Core
{
    public enum DataIssueKind { DuplicateKey, Numeric }

    public sealed class DataIssue
    {
        public DataIssueKind Kind { get; }
        public string Parameter { get; }
        public string Detail { get; }
        public DataIssue(DataIssueKind kind, string parameter, string detail)
            => (Kind, Parameter, Detail) = (kind, parameter, detail);
    }

    public sealed class DataValidationException : Exception
    {
        public IReadOnlyList<DataIssue> Issues { get; }
        public DataValidationException(IReadOnlyList<DataIssue> issues) : base(BuildMessage(issues)) => Issues = issues;
        private static string BuildMessage(IReadOnlyList<DataIssue> issues)
            => string.Join(Environment.NewLine, issues.Select(i => $"[{i.Kind}] {i.Parameter}: {i.Detail}"));
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class FullGridAttribute : Attribute { }

    /// <summary>Validates each loaded Parameter table without consulting Set rows.</summary>
    public static class DataValidator
    {
        public const double MaxMagnitude = 1e15;

        public static IReadOnlyList<DataIssue> Validate(IReadOnlyList<ParamRegistration> parameters)
        {
            var issues = new List<DataIssue>();
            foreach (var parameter in parameters)
            {
                CheckDuplicateKeys(parameter, issues);
                CheckNumericValues(parameter, issues);
            }
            return issues;
        }

        private static void CheckDuplicateKeys(ParamRegistration parameter, List<DataIssue> issues)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var row = 0; row < parameter.Rows.Count; row++)
            {
                var key = string.Join("\u001f", parameter.Rows[row].Index.Select(Format));
                if (!seen.Add(key))
                    issues.Add(new DataIssue(DataIssueKind.DuplicateKey, parameter.Name, $"duplicate index row {row + 1}: {key}"));
            }
        }

        private static void CheckNumericValues(ParamRegistration parameter, List<DataIssue> issues)
        {
            for (var row = 0; row < parameter.Rows.Count; row++)
                foreach (var (name, value) in parameter.Rows[row].Numbers)
                    if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > MaxMagnitude)
                        issues.Add(new DataIssue(DataIssueKind.Numeric, parameter.Name, $"row {row + 1}, {name}={value.ToString(CultureInfo.InvariantCulture)}"));
        }

        private static string Format(object? value) => value switch
        {
            DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            null => "",
            _ => value.ToString() ?? "",
        };
    }
}
