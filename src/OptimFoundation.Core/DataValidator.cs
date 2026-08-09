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

    /// <summary>驗證已載入的 Set 與 Parameter 資料列。</summary>
    public static class DataValidator
    {
        public const double MaxMagnitude = 1e15;

        /// <summary>驗證 Set／Parameter key 重複與 Parameter 數值合理性。</summary>
        public static IReadOnlyList<DataIssue> Validate(
            IReadOnlyList<SetRegistration> sets,
            IReadOnlyList<ParamRegistration> parameters)
        {
            var issues = new List<DataIssue>();
            foreach (var set in sets)
                CheckDuplicateSetKeys(set, issues);
            foreach (var parameter in parameters)
            {
                CheckDuplicateParameterKeys(parameter, issues);
                CheckNumericValues(parameter, issues);
            }
            return issues;
        }

        /// <summary>保留只驗證 Parameter 的呼叫方式。</summary>
        public static IReadOnlyList<DataIssue> Validate(IReadOnlyList<ParamRegistration> parameters)
            => Validate(Array.Empty<SetRegistration>(), parameters);

        private static void CheckDuplicateSetKeys(SetRegistration set, List<DataIssue> issues)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var row = 0; row < set.Rows.Count; row++)
            {
                var key = ComposeKey(set.Name, row, set.Rows[row]);
                if (!seen.Add(key))
                    issues.Add(new DataIssue(
                        DataIssueKind.DuplicateKey,
                        set.Name,
                        $"duplicate Set key at row {row + 1}: {key}"));
            }
        }

        private static void CheckDuplicateParameterKeys(ParamRegistration parameter, List<DataIssue> issues)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var row = 0; row < parameter.Rows.Count; row++)
            {
                var key = ComposeKey(parameter.Name, row, parameter.Rows[row].Index);
                if (!seen.Add(key))
                    issues.Add(new DataIssue(
                        DataIssueKind.DuplicateKey,
                        parameter.Name,
                        $"duplicate Parameter key at row {row + 1}: {key}"));
            }
        }

        private static string ComposeKey(string source, int row, IReadOnlyList<object> values)
            => string.Join("\u001f", values.Select((value, index) =>
                ModelNaming.Token($"{source} row #{row + 1} index #{index + 1}", value)));

        private static void CheckNumericValues(ParamRegistration parameter, List<DataIssue> issues)
        {
            for (var row = 0; row < parameter.Rows.Count; row++)
                foreach (var (name, value) in parameter.Rows[row].Numbers)
                    if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > MaxMagnitude)
                        issues.Add(new DataIssue(DataIssueKind.Numeric, parameter.Name, $"row {row + 1}, {name}={value.ToString(CultureInfo.InvariantCulture)}"));
        }
    }
}
