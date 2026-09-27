using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;



namespace OptimFoundation.Core
{

    #region Data 驗證

    public enum DataIssueKind { DuplicateKey, Numeric, InvalidKey }

    public sealed class DataIssue
    {
        public DataIssueKind Kind { get; }
        public string Parameter { get; }
        public string Detail { get; }
        public DataIssue(DataIssueKind kind, string parameter, string detail)
            => (Kind, Parameter, Detail) = (kind, parameter, detail);
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
                var key = ComposeKey(set.Name, set.IndexFields, row, set.Rows[row], issues);
                if (key != null && !seen.Add(key))
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
                var key = ComposeKey(parameter.Name, parameter.IndexFields, row, parameter.Rows[row].Index, issues);
                if (key != null && !seen.Add(key))
                    issues.Add(new DataIssue(
                        DataIssueKind.DuplicateKey,
                        parameter.Name,
                        $"duplicate Parameter key at row {row + 1}: {key}"));
            }
        }

        // key 值不合法（與模型命名同一套規則）→ 記 InvalidKey 並回 null，該列不參與重複比對
        private static string ComposeKey(
            string source, string[] indexFields, int row, IReadOnlyList<object> values, List<DataIssue> issues)
        {
            var tokens = new string[values.Count];
            var valid = true;
            for (var index = 0; index < values.Count; index++)
            {
                if (ModelNaming.TryToken(values[index], out var token, out var reason))
                {
                    tokens[index] = token;
                    continue;
                }

                valid = false;
                var field = index < indexFields.Length ? indexFields[index] : $"index #{index + 1}";
                issues.Add(new DataIssue(
                    DataIssueKind.InvalidKey,
                    source,
                    $"row {row + 1}, {field}='{ModelNaming.DisplayValue(token)}' reason={reason}"));
            }
            return valid ? string.Join("\u001f", tokens) : null;
        }

        private static void CheckNumericValues(ParamRegistration parameter, List<DataIssue> issues)
        {
            for (var row = 0; row < parameter.Rows.Count; row++)
                foreach (var (name, value) in parameter.Rows[row].Numbers)
                    if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > MaxMagnitude)
                        issues.Add(new DataIssue(DataIssueKind.Numeric, parameter.Name, $"row {row + 1}, {name}={value.ToString(CultureInfo.InvariantCulture)}"));
        }
    }

    #endregion

    #region Data 內容
    /// <summary>Creates a data context through the project's chosen constructor.</summary>
    public static class OptData
    {
        public static T Load<T>(System.Func<T> factory)
        {
            if (factory == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(factory)),
                    "DATA_LOAD_INVALID", "資料載入失敗", nameof(Load), null, "factory_is_null");

            try
            {
                var value = factory();
                if (value is DataContext data)
                {
                    data.Initialize();
                    data.Freeze();
                }
                return value;
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(
                    ex, "DATA_LOAD_FAILED", "公開 API 執行失敗", nameof(Load), typeof(T).FullName,
                    ex.GetBaseException().Message);
                throw;
            }
        }
    }

    /// <summary>Flattened generated Parameter row used by validation and diagnostics.</summary>
    public sealed class ParamRow
    {
        public object[] Index { get; }
        public (string Name, double Value)[] Numbers { get; }
        public ParamRow(object[] index, (string Name, double Value)[] numbers)
        {
            Index = index;
            Numbers = numbers;
        }
    }

    /// <summary>供資料驗證使用的 Set schema 與 key 列。</summary>
    public sealed class SetRegistration
    {
        /// <summary>Set row 類別名稱。</summary>
        public string Name { get; }
        /// <summary>依宣告順序排列的維度欄名。</summary>
        public string[] IndexFields { get; }
        /// <summary>每一列依維度順序攤平後的 key 值。</summary>
        public IReadOnlyList<object[]> Rows { get; }
        /// <summary>資料列數。</summary>
        public int RowCount => Rows.Count;

        /// <summary>建立供 validator 使用的 Set 註冊資料。</summary>
        public SetRegistration(string name, string[] indexFields, IReadOnlyList<object[]> rows)
        {
            Name = name;
            IndexFields = indexFields;
            Rows = rows;
        }
    }

    public sealed class ParamRegistration
    {
        public string Name { get; }
        public string[] IndexFields { get; }
        public IReadOnlyList<ParamRow> Rows { get; }
        public int RowCount => Rows.Count;
        public ParamRegistration(string name, string[] indexFields, IReadOnlyList<ParamRow> rows)
        {
            Name = name;
            IndexFields = indexFields;
            Rows = rows;
        }
    }

    /// <summary>Base class for Dataload. Set and Parameter rows are owned by the project as List&lt;T&gt;.</summary>
    public abstract class DataContext
    {
        private bool _isFrozen;
        private readonly List<SetRegistration> _sets = new();
        private readonly List<ParamRegistration> _params = new();

        /// <summary>載入時資料驗證發現的問題；每筆都已寫成 Warning，不阻擋後續建模。</summary>
        public IReadOnlyList<DataIssue> DataIssues { get; private set; } = Array.Empty<DataIssue>();

        /// <summary>註冊一份 Set 資料及其 key schema，供重複 key 驗證與摘要輸出。</summary>
        protected void RegisterSet<T>(
            IReadOnlyList<T> rows,
            string[] indexFields,
            Func<T, object[]> indexOf)
            where T : SetRowBase
        {
            GuardMutation(typeof(T).Name);
            _sets.Add(new SetRegistration(
                typeof(T).Name,
                indexFields,
                rows.Select(indexOf).ToArray()));
        }

        protected void RegisterParam<T>(
            IReadOnlyList<T> rows,
            string[] indexFields,
            Func<T, object[]> indexOf,
            Func<T, (string Name, double Value)[]> numbersOf)
            where T : ModelElementBase
        {
            GuardMutation(typeof(T).Name);
            _params.Add(new ParamRegistration(
                typeof(T).Name,
                indexFields,
                rows.Select(row => new ParamRow(indexOf(row), numbersOf(row))).ToArray()));
        }

        protected virtual void RegisterAll() { }

        internal void Initialize()
        {
            RegisterAll();
            ValidateData();
        }

        internal void Freeze() => _isFrozen = true;

        protected void GuardMutation(string member)
        {
            if (_isFrozen)
                throw Logging.ErrorOnce(
                    new InvalidOperationException($"DataContext member '{member}' is frozen; 模型建構階段不得修改資料。"),
                    "DATA_CONTEXT_FROZEN", "資料內容不可修改", nameof(GuardMutation), member, "context_is_frozen");
        }

        protected void ValidateData()
        {
            DataIssues = DataValidator.Validate(_sets, _params);
            foreach (var issue in DataIssues)
                Logging.Warn(
                    $"[DATA_VALIDATION_WARNING] 資料驗證發現問題 | context={issue.Parameter} " +
                    $"reason={issue.Kind} detail={issue.Detail} result=continued");

            Logging.Info("===== Data load summary =====");
            Logging.Info($"Sets ({_sets.Count}):");
            foreach (var set in _sets)
                Logging.Info($"  {set.Name}: index=[{string.Join(",", set.IndexFields)}], rows={set.RowCount}");
            Logging.Info($"Parameters ({_params.Count}):");
            foreach (var parameter in _params)
                Logging.Info($"  {parameter.Name}: index=[{string.Join(",", parameter.IndexFields)}], rows={parameter.RowCount}");
            Logging.Info($"Issues ({DataIssues.Count})");
        }
    }

    /// <summary>由開發者明確指定條件的 Parameter 查找；缺值只記錄 Warning，不推導 Set 關聯。</summary>
    public static class ParameterLookupExtensions
    {
        /// <summary>
        /// 回傳第一筆符合條件的 Parameter；找不到時記錄 <c>PARAMETER_NOT_FOUND</c> 並回傳 null。
        /// keyValues 只用於 Log，缺值後要採 0、略過或其他預設值由呼叫端決定。
        /// </summary>
        public static T FindParameterOrLog<T>(
            this IEnumerable<T> rows,
            Func<T, bool> predicate,
            params object[] keyValues)
            where T : ParameterBase
        {
            if (rows == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(rows)),
                    "PARAMETER_LOOKUP_INVALID", "Parameter 查找條件不合法",
                    typeof(T).Name, null, "rows_are_null");
            if (predicate == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(predicate)),
                    "PARAMETER_LOOKUP_INVALID", "Parameter 查找條件不合法",
                    typeof(T).Name, null, "predicate_is_null");

            string key = FormatKey(keyValues);
            try
            {
                var row = rows.FirstOrDefault(predicate);
                if (row == null)
                    Logging.Warn(
                        $"[PARAMETER_NOT_FOUND] Parameter key 查無資料 | context={typeof(T).Name} " +
                        $"key={key} reason=no_matching_row result=missing");
                return row;
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(
                    ex, "PARAMETER_LOOKUP_FAILED", "Parameter 查找失敗",
                    typeof(T).Name, key, ex.GetBaseException().Message);
                throw;
            }
        }

        private static string FormatKey(IReadOnlyList<object> values)
        {
            if (values == null || values.Count == 0) return "<unspecified>";
            return string.Join("@", values.Select(value => value switch
            {
                null => "<null>",
                DateTime date => date.ToString(
                    date.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            }));
        }
    }
    #endregion

}
