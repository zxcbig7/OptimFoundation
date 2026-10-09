using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;



namespace OptimFoundation.Core
{

    #region Data 驗證

    /// <summary>資料驗證發現的問題種類。</summary>
    public enum DataIssueKind
    {
        /// <summary>Set 或 Parameter 的 key 重複。</summary>
        DuplicateKey,

        /// <summary>Parameter 數值是 NaN、無限大，或絕對值超過 <see cref="DataValidator.MaxMagnitude"/>。</summary>
        Numeric,

        /// <summary>key 值不符合模型命名規則。</summary>
        InvalidKey
    }

    /// <summary>一筆資料驗證問題。</summary>
    public sealed class DataIssue
    {
        /// <summary>問題種類。</summary>
        public DataIssueKind Kind { get; }

        /// <summary>出問題的 Set 或 Parameter 類別名稱。</summary>
        public string Parameter { get; }

        /// <summary>問題細節：第幾列、哪個欄位、什麼值。</summary>
        public string Detail { get; }

        /// <summary>建立一筆資料驗證問題。</summary>
        public DataIssue(DataIssueKind kind, string parameter, string detail)
            => (Kind, Parameter, Detail) = (kind, parameter, detail);
    }

    /// <summary>驗證已載入的 Set 與 Parameter 資料列。</summary>
    public static class DataValidator
    {
        /// <summary>Parameter 數值的絕對值上限；超過就記 <see cref="DataIssueKind.Numeric"/>。</summary>
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
                        $"第 {row + 1} 資料列的集合鍵重複：{key}"));
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
                        $"第 {row + 1} 資料列的參數鍵重複：{key}"));
            }
        }

        // 不合法的 key 會記錄問題，並略過重複檢查。
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
                var field = index < indexFields.Length ? indexFields[index] : $"索引 #{index + 1}";
                issues.Add(new DataIssue(
                    DataIssueKind.InvalidKey,
                    source,
                    $"第 {row + 1} 資料列 {field}='{ModelNaming.DisplayValue(token)}' 原因={reason}"));
            }
            return valid ? string.Join("\u001f", tokens) : null;
        }

        private static void CheckNumericValues(ParamRegistration parameter, List<DataIssue> issues)
        {
            for (var row = 0; row < parameter.Rows.Count; row++)
                foreach (var (name, value) in parameter.Rows[row].Numbers)
                    if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > MaxMagnitude)
                        issues.Add(new DataIssue(DataIssueKind.Numeric, parameter.Name, $"第 {row + 1} 資料列 {name}={value.ToString(CultureInfo.InvariantCulture)}"));
        }
    }

    #endregion

    #region Data 內容
    /// <summary>透過 factory 載入資料；DataContext 會先驗證再凍結。</summary>
    public static class OptData
    {
        /// <summary>載入資料；DataContext 會先驗證再凍結。</summary>
        public static T Load<T>(System.Func<T> factory)
        {
            if (factory == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(factory), "factory 不得為 null"),
                    "資料載入不合法", null, nameof(Load), null, "建立函式為空");

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
                    ex, "資料載入失敗", null, nameof(Load), typeof(T).FullName,
                    ex.GetBaseException().Message);
                throw;
            }
        }
    }

    /// <summary>Parameter 的維度值與數值欄位。</summary>
    public sealed class ParamRow
    {
        /// <summary>依維度順序排列的 key 值。</summary>
        public object[] Index { get; }

        /// <summary>數值欄位的名稱與值。</summary>
        public (string Name, double Value)[] Numbers { get; }

        /// <summary>建立一筆 Parameter 資料列。</summary>
        public ParamRow(object[] index, (string Name, double Value)[] numbers)
        {
            Index = index;
            Numbers = numbers;
        }
    }

    /// <summary>供資料驗證使用的 Set 註冊資料。</summary>
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

    /// <summary>供資料驗證使用的 Parameter 註冊資料。</summary>
    public sealed class ParamRegistration
    {
        /// <summary>Parameter row 類別名稱。</summary>
        public string Name { get; }
        /// <summary>依宣告順序排列的維度欄名。</summary>
        public string[] IndexFields { get; }
        /// <summary>每一列的維度值與數值欄位。</summary>
        public IReadOnlyList<ParamRow> Rows { get; }
        /// <summary>資料列數。</summary>
        public int RowCount => Rows.Count;

        /// <summary>建立供 validator 使用的 Parameter 註冊資料。</summary>
        public ParamRegistration(string name, string[] indexFields, IReadOnlyList<ParamRow> rows)
        {
            Name = name;
            IndexFields = indexFields;
            Rows = rows;
        }
    }

    /// <summary>Dataload 的基底類別，負責登記與驗證資料。</summary>
    public abstract class DataContext
    {
        private bool _isFrozen;
        private readonly List<SetRegistration> _sets = new();
        private readonly List<ParamRegistration> _params = new();

        /// <summary>資料驗證發現的問題；只記 Warning，不中斷建模。</summary>
        public IReadOnlyList<DataIssue> DataIssues { get; private set; } = Array.Empty<DataIssue>();

        /// <summary>登記 Set 資料，供重複 key 檢查與摘要使用。</summary>
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

        /// <summary>登記 Parameter 資料，供資料檢查與摘要使用。</summary>
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

        /// <summary>Generator 在載入時覆寫此方法，逐一登記 Set 和 Parameter。</summary>
        protected virtual void RegisterAll() { }

        internal void Initialize()
        {
            RegisterAll();
            ValidateData();
        }

        internal void Freeze() => _isFrozen = true;

        /// <summary>資料已凍結時拋例外，防止建模階段修改資料。</summary>
        protected void GuardMutation(string member)
        {
            if (_isFrozen)
                throw Logging.ErrorOnce(
                    new InvalidOperationException($"DataContext 成員 '{member}' 已凍結，建立模型階段不得修改資料"),
                    "資料內容已凍結", "建立模型階段不可修改", nameof(GuardMutation), member, "資料內容已凍結");
        }

        /// <summary>驗證已登記的資料，記錄問題並輸出摘要。</summary>
        protected void ValidateData()
        {
            DataIssues = DataValidator.Validate(_sets, _params);
            foreach (var issue in DataIssues)
                Logging.Warn(
                    $"[資料不合法] 名稱={issue.Parameter} " +
                    $"原因={issue.Kind switch { DataIssueKind.DuplicateKey => "鍵重複", DataIssueKind.InvalidKey => "鍵不合法", _ => "數值不合法" }} " +
                    $"細節={issue.Detail} 結果=繼續");

            Logging.Info($"[資料載入摘要] 集合數量={_sets.Count} 參數數量={_params.Count} 問題數量={DataIssues.Count}");
            foreach (var set in _sets)
                Logging.Info($"[集合載入完成] 名稱={set.Name} 索引={FormatIndex(set.IndexFields)} 資料列數量={set.RowCount}");
            foreach (var parameter in _params)
                Logging.Info($"[參數載入完成] 名稱={parameter.Name} 索引={FormatIndex(parameter.IndexFields)} 資料列數量={parameter.RowCount}");
        }

        // Scalar 參數沒有維度欄名。
        private static string FormatIndex(IEnumerable<string> fields)
        {
            string joined = string.Join("|", fields);
            return joined.Length == 0 ? "<空白>" : joined;
        }
    }

    /// <summary>依呼叫端提供的條件查找 Parameter；找不到時只記錄警告，不會自行推測與 Set 的對應關係。</summary>
    public static class ParameterLookupExtensions
    {
        /// <summary>
        /// 回傳第一筆符合條件的 Parameter；找不到時記錄 <c>[參數找不到]</c> 並回傳 null。
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
                    new ArgumentNullException(nameof(rows), "rows 不得為 null"),
                    "參數查找不合法", null,
                    typeof(T).Name, null, "資料列集合為空");
            if (predicate == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(predicate), "predicate 不得為 null"),
                    "參數查找不合法", null,
                    typeof(T).Name, null, "查找條件為空");

            string key = FormatKey(keyValues);
            try
            {
                var row = rows.FirstOrDefault(predicate);
                if (row == null)
                    Logging.Warn(
                        $"[參數找不到] 型別={typeof(T).Name} " +
                        $"鍵={key} 原因=沒有符合的資料列 結果=回傳空值");
                return row;
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(
                    ex, "參數查找失敗", null,
                    typeof(T).Name, key, ex.GetBaseException().Message);
                throw;
            }
        }

        private static string FormatKey(IReadOnlyList<object> values)
        {
            if (values == null || values.Count == 0) return "<未指定>";
            return string.Join("@", values.Select(value => value switch
            {
                null => "<空值>",
                // Set 資料列只列維度值，跟限制式名稱展開 Set 的方式相同
                SetRowBase row => string.Join("@", row.KeyParts()),
                DateTime date => date.ToString(
                    date.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            }));
        }
    }
    #endregion

}
