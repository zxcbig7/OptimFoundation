using System;
using System.Collections.Generic;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 一列 parameter 攤平後的型別抹除結構：index 值（依 indexSets 宣告順序 box 成 object[]）+
    /// 所有 double 型別欄位（含 QTY）。由 generator emit 的編譯期 lambda（indexOf/numbersOf）在
    /// RegisterParam 註冊當下產生——型別抹除只發生在這個邊界，之後驗證器只碰 object[]，零反射。
    /// </summary>
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

    /// <summary>
    /// 登記進 DataContext 的一筆 parameter metadata：名稱 + index-set 名 + 是否 FullGrid + 攤平後的列。
    /// public（非巢狀私有）：DataValidator 的純函式簽名與測試專案（不同組件）都需要看得到。
    /// </summary>
    public sealed class ParamRegistration
    {
        public string Name { get; }
        public string[] IndexSets { get; }
        public bool FullGrid { get; }
        public IReadOnlyList<ParamRow> Rows { get; }
        public int RowCount => Rows.Count;

        public ParamRegistration(string name, string[] indexSets, bool fullGrid, IReadOnlyList<ParamRow> rows)
        {
            Name = name;
            IndexSets = indexSets;
            FullGrid = fullGrid;
            Rows = rows;
        }
    }

    /// <summary>
    /// Dataload 的 base：持有 set / param 註冊表，供 DataValidator 消費（見框架資料防護規格）。
    /// 由 generator emit 的 partial override RegisterAll 呼叫 RegisterSet/RegisterParam 登記；
    /// OptData.Load 建構完成後呼叫 Initialize（= RegisterAll + ValidateData）。
    /// </summary>
    public abstract class DataContext
    {
        private readonly Dictionary<string, ISetBrick> _sets = new Dictionary<string, ISetBrick>();
        private readonly List<ParamRegistration> _params = new List<ParamRegistration>();

        /// <summary>登記一顆 set，供驗證 dangling / [FullGrid] 完整性用。</summary>
        protected void RegisterSet(string name, ISetBrick set)
        {
            _sets[name] = set;
        }

        /// <summary>
        /// 登記一批 parameter：indexOf/numbersOf 為 generator emit 的編譯期 lambda，
        /// 註冊當下就把每列攤平成 <see cref="ParamRow"/>（零 runtime reflection）。
        /// </summary>
        protected void RegisterParam<T>(
            IReadOnlyList<T> rows,
            string[] indexSets,
            Func<T, object[]> indexOf,
            Func<T, (string Name, double Value)[]> numbersOf,
            bool fullGrid)
            where T : ModelElementBase
        {
            var flatRows = new List<ParamRow>(rows.Count);
            foreach (var row in rows)
                flatRows.Add(new ParamRow(indexOf(row), numbersOf(row)));

            _params.Add(new ParamRegistration(typeof(T).Name, indexSets, fullGrid, flatRows));
        }

        /// <summary>
        /// 由 generator 為每個 DataContext 子類 emit 的 partial override 呼叫 RegisterSet/RegisterParam。
        /// 預設空 body：沒有 generator 註冊碼時安全 no-op。
        /// </summary>
        protected virtual void RegisterAll()
        {
        }

        /// <summary>blessed 建構路徑（OptData.Load）呼叫：先跑 generator 註冊碼，再驗證。</summary>
        internal void Initialize()
        {
            RegisterAll();
            ValidateData();
        }

        // 聚合三類檢查（referential integrity + type mismatch / duplicate key / numeric sanity）；
        // 任何違規 → 一次全丟 DataValidationException（見 DataValidator.Validate）；乾淨才印載入摘要。
        protected void ValidateData()
        {
            var issues = DataValidator.Validate(_sets, _params);
            if (issues.Count > 0)
                throw new DataValidationException(issues);

            Logging.Info("===== 資料載入摘要 =====");

            Logging.Info($"Sets（{_sets.Count}）：");
            foreach (var kv in _sets)
                Logging.Info($"  {kv.Key}: {kv.Value.Count} 個成員");

            Logging.Info($"Parameters（{_params.Count}）：");
            foreach (var p in _params)
            {
                Logging.Info($"  {p.Name}: index=[{string.Join(",", p.IndexSets)}], rows={p.RowCount}");
                if (p.RowCount > 0)
                {
                    var first = p.Rows[0];
                    string idxVals = string.Join(",", FormatIndexValues(first.Index));
                    Logging.Info($"    第一列 index=[{idxVals}]（型別化萃取器讀值，零反射）");
                }
            }
        }

        // Index 值格式化（僅供摘要顯示；DateTime 統一 yyyy-MM-dd，其餘用 ToString）
        private static IEnumerable<string> FormatIndexValues(object[] index)
        {
            foreach (var v in index)
                yield return v is DateTime dt ? dt.ToString("yyyy-MM-dd") : v?.ToString() ?? "null";
        }
    }
}
