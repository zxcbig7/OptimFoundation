using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>驗證問題種類：見框架資料防護規格 Acceptance Criteria。</summary>
    public enum DataIssueKind
    {
        MissingSet,
        TypeMismatch,
        Dangling,
        DuplicateKey,
        MissingCell,
        Numeric
    }

    /// <summary>單一驗證問題：所屬 parameter + 種類 + 細節描述。</summary>
    public sealed class DataIssue
    {
        public DataIssueKind Kind { get; }
        public string Parameter { get; }
        public string Detail { get; }

        public DataIssue(DataIssueKind kind, string parameter, string detail)
        {
            Kind = kind;
            Parameter = parameter;
            Detail = detail;
        }
    }

    /// <summary>DataContext.ValidateData 聚合失敗時丟出，Issues 一次列出全部違規。</summary>
    public sealed class DataValidationException : Exception
    {
        public IReadOnlyList<DataIssue> Issues { get; }

        public DataValidationException(IReadOnlyList<DataIssue> issues)
            : base(BuildMessage(issues))
        {
            Issues = issues;
        }

        // 依 Kind 分組附數量做標題，逐筆問題另起一行——一次全報，人讀得懂。
        private static string BuildMessage(IReadOnlyList<DataIssue> issues)
        {
            if (issues == null || issues.Count == 0) return "資料驗證失敗";

            string header = string.Join("、", issues
                .GroupBy(i => i.Kind)
                .Select(g => $"{g.Key}×{g.Count()}"));

            var sb = new StringBuilder();
            sb.Append($"資料驗證失敗，共 {issues.Count} 筆問題（{header}）：");
            foreach (var issue in issues)
                sb.Append($"\n[{issue.Kind}] {issue.Parameter}: {issue.Detail}");
            return sb.ToString();
        }
    }

    /// <summary>標在 Parameter_* 上，啟用 [FullGrid] 完整性檢查（缺格即報）；未標則不檢查完整性。</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class FullGridAttribute : Attribute
    {
    }

    /// <summary>
    /// 三類檢查（參照完整性含型別不符辨別 / index key 唯一性 / 數值 sanity）+ 聚合報告（見框架資料防護規格）。
    /// 純函式：只讀 sets/parameters、回傳全部問題，NEVER 提前 return、NEVER 自己 throw——好測、好組合。
    /// </summary>
    public static class DataValidator
    {
        /// <summary>
        /// 原始資料值的數值量級門檻：超過此值 double 連整數精度都保不住，任何 solver 都會數值失穩。
        /// 注意這與 Numeric.SafeRatio 的 1e9 是不同用途（那是衍生 BigM 的較嚴門檻），兩者刻意不同，勿統一。
        /// </summary>
        public const double MaxMagnitude = 1e15;

        /// <summary>[FullGrid] 笛卡兒積規模保護門檻：期望格數超過此值就不逐一枚舉，改回報格數不符。</summary>
        public const long FullGridEnumerationCeiling = 1_000_000;

        /// <summary>[FullGrid] 缺格訊息最多列出的組合數，超過就補「還有 N 組未列出」。</summary>
        private const int FullGridMaxListed = 20;

        public static IReadOnlyList<DataIssue> Validate(
            IReadOnlyDictionary<string, ISetBrick> sets,
            IReadOnlyList<ParamRegistration> parameters)
        {
            var issues = new List<DataIssue>();

            foreach (var p in parameters)
            {
                CheckDeclaredSets(p, sets, issues);
                CheckReferentialIntegrity(p, sets, issues);
                CheckDuplicateKeys(p, issues);
                CheckNumericSanity(p, issues);
                CheckFullGrid(p, sets, issues);
            }

            return issues;
        }

        // 檢查零：宣告的 index-set 名必須都已註冊——每個 parameter 只驗一次，NEVER 掛在列迴圈上。
        // Why: 掛列迴圈會讓「零列 parameter + set 名打錯」完全靜默（迴圈不執行就沒人報），有列時又噴 N 筆重複噪音。
        private static void CheckDeclaredSets(
            ParamRegistration p, IReadOnlyDictionary<string, ISetBrick> sets, List<DataIssue> issues)
        {
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (string setName in p.IndexSets)
            {
                if (sets.ContainsKey(setName) || !reported.Add(setName)) continue;
                issues.Add(new DataIssue(DataIssueKind.MissingSet, p.Name,
                    $"index 欄位 '{setName}' 找不到對應的 Set（Set 未註冊或名稱打錯）；本 parameter 的參照完整性與 [FullGrid] 檢查已略過。"));
            }
        }

        // 檢查一：參照完整性——每列每個 index 欄位值必須在對應 Set 內；型別不符與純粹缺值分開辨別。
        private static void CheckReferentialIntegrity(
            ParamRegistration p, IReadOnlyDictionary<string, ISetBrick> sets, List<DataIssue> issues)
        {
            for (int row = 0; row < p.Rows.Count; row++)
            {
                var index = p.Rows[row].Index;
                for (int i = 0; i < p.IndexSets.Length; i++)
                {
                    string setName = p.IndexSets[i];
                    object value = index[i];

                    // set 不存在已由檢查零回報過（每 parameter 一次），此處略過避免 N 列重複噪音
                    if (!sets.TryGetValue(setName, out var set)) continue;

                    if (set.ContainsObject(value)) continue;

                    Type valueType = value?.GetType();
                    if (valueType != set.ElementType)
                    {
                        issues.Add(new DataIssue(DataIssueKind.TypeMismatch, p.Name,
                            $"第 {row + 1} 列 index 欄位 '{setName}' 值 '{FormatValue(value)}' 型別為 " +
                            $"{valueType?.Name ?? "null"}，與 Set '{setName}' 元素型別 {set.ElementType.Name} 不符" +
                            "（常見原因：字串式 OptParam(\"Name:Type\") 宣告把型別打錯）。"));
                    }
                    else
                    {
                        issues.Add(new DataIssue(DataIssueKind.Dangling, p.Name,
                            $"第 {row + 1} 列 index 欄位 '{setName}' 值 '{FormatValue(value)}' 不在 Set '{setName}' 內。"));
                    }
                }
            }
        }

        // 檢查二：index key 唯一性——用該列 Index 全部值的結構性相等比較組 key，重複即報。
        private static void CheckDuplicateKeys(ParamRegistration p, List<DataIssue> issues)
        {
            var seenAtRow = new Dictionary<object[], int>(IndexKeyComparer.Instance);

            for (int row = 0; row < p.Rows.Count; row++)
            {
                var index = p.Rows[row].Index;
                if (seenAtRow.TryGetValue(index, out int firstRow))
                {
                    issues.Add(new DataIssue(DataIssueKind.DuplicateKey, p.Name,
                        $"index key ({FormatKey(index)}) 重複：第 {firstRow + 1} 列與第 {row + 1} 列。"));
                }
                else
                {
                    seenAtRow[index] = row;
                }
            }
        }

        // 檢查三：數值 sanity——NaN / Infinity / 超過量級門檻。
        private static void CheckNumericSanity(ParamRegistration p, List<DataIssue> issues)
        {
            for (int row = 0; row < p.Rows.Count; row++)
            {
                foreach (var (name, value) in p.Rows[row].Numbers)
                {
                    if (double.IsNaN(value))
                    {
                        issues.Add(new DataIssue(DataIssueKind.Numeric, p.Name,
                            $"第 {row + 1} 列欄位 '{name}' 為 NaN。"));
                    }
                    else if (double.IsInfinity(value))
                    {
                        issues.Add(new DataIssue(DataIssueKind.Numeric, p.Name,
                            $"第 {row + 1} 列欄位 '{name}' 為 Infinity。"));
                    }
                    else if (Math.Abs(value) > MaxMagnitude)
                    {
                        issues.Add(new DataIssue(DataIssueKind.Numeric, p.Name,
                            $"第 {row + 1} 列欄位 '{name}' 值 {value.ToString(CultureInfo.InvariantCulture)} 超過量級門檻 " +
                            $"{MaxMagnitude.ToString("E0", CultureInfo.InvariantCulture)}。"));
                    }
                }
            }
        }

        // 檢查四（opt-in）：[FullGrid] 完整性——只對 p.FullGrid 為 true 的 parameter 執行，缺格報 MissingCell。
        private static void CheckFullGrid(
            ParamRegistration p, IReadOnlyDictionary<string, ISetBrick> sets, List<DataIssue> issues)
        {
            if (!p.FullGrid) return;

            var setBricks = new ISetBrick[p.IndexSets.Length];
            for (int i = 0; i < p.IndexSets.Length; i++)
            {
                // 對應 set 找不到已由檢查一報 MissingSet（若有列踩到），這裡無法判斷完整性，略過避免噪音重複。
                if (!sets.TryGetValue(p.IndexSets[i], out var set)) return;
                setBricks[i] = set;
            }

            long expectedCells = 1;
            foreach (var set in setBricks)
                expectedCells *= set.Count;

            var seenKeys = new HashSet<object[]>(IndexKeyComparer.Instance);
            foreach (var row in p.Rows)
                seenKeys.Add(row.Index);

            // 規模保護：期望格數過大就不枚舉笛卡兒積（避免記憶體/時間爆掉），只比對格數是否相符。
            if (expectedCells > FullGridEnumerationCeiling)
            {
                if (seenKeys.Count != expectedCells)
                {
                    issues.Add(new DataIssue(DataIssueKind.MissingCell, p.Name,
                        $"[FullGrid] 期望 {expectedCells} 格，實際 {seenKeys.Count} 個不重複組合，數量不符——" +
                        $"因組合數過大（{expectedCells} 格）未逐一列出缺漏。"));
                }
                return;
            }

            var missing = new List<object[]>();
            foreach (var combo in CartesianProduct(setBricks))
            {
                if (!seenKeys.Contains(combo))
                    missing.Add(combo);
            }

            if (missing.Count == 0) return;

            int shown = Math.Min(missing.Count, FullGridMaxListed);
            var sb = new StringBuilder();
            sb.Append($"[FullGrid] 缺 {missing.Count} 格（共 {expectedCells} 格）：");
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append("；");
                sb.Append('(').Append(FormatKey(missing[i])).Append(')');
            }
            if (missing.Count > shown)
                sb.Append($"……還有 {missing.Count - shown} 組未列出");

            issues.Add(new DataIssue(DataIssueKind.MissingCell, p.Name, sb.ToString()));
        }

        // 各 index set 成員的笛卡兒積，逐維計數器遞增產生（非遞迴，避免深維度爆 stack）；呼叫前已保證總數 <= 門檻。
        private static IEnumerable<object[]> CartesianProduct(ISetBrick[] setBricks)
        {
            if (setBricks.Length == 0)
            {
                yield return Array.Empty<object>();
                yield break;
            }

            var members = new object[setBricks.Length][];
            for (int i = 0; i < setBricks.Length; i++)
                members[i] = setBricks[i].MembersAsObjects().ToArray();

            var counters = new int[setBricks.Length];
            while (true)
            {
                var combo = new object[setBricks.Length];
                for (int i = 0; i < setBricks.Length; i++)
                    combo[i] = members[i][counters[i]];
                yield return combo;

                int dim = setBricks.Length - 1;
                while (dim >= 0)
                {
                    counters[dim]++;
                    if (counters[dim] < members[dim].Length) break;
                    counters[dim] = 0;
                    dim--;
                }
                if (dim < 0) yield break;
            }
        }

        private static string FormatKey(object[] index) => string.Join(",", index.Select(FormatValue));

        private static string FormatValue(object value)
        {
            if (value is DateTime dt) return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (value is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
            return value?.ToString() ?? "null";
        }

        // object[] 的結構性相等比較（逐元素 Equals），供 duplicate key 偵測用——非字串拼接比對。
        private sealed class IndexKeyComparer : IEqualityComparer<object[]>
        {
            public static readonly IndexKeyComparer Instance = new IndexKeyComparer();

            public bool Equals(object[] x, object[] y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null || x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++)
                    if (!Equals(x[i], y[i])) return false;
                return true;
            }

            public int GetHashCode(object[] obj)
            {
                int hash = 17;
                foreach (var v in obj)
                    hash = hash * 31 + (v?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
