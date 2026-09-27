using System.Collections.Generic;
using System.Linq;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 一份模型規模統計。同一組欄位有兩個來源：框架的建模記帳（建了什麼）與 solver 模型實際持有的內容，
    /// 由 <see cref="ModelStatsReport"/> 逐項對帳。
    /// </summary>
    public sealed class ModelCounts
    {
        /// <summary>變數總數。solver 端只算已收進模型的變數（CPLEX 不收沒被任何限制式或目標式引用的變數）。</summary>
        public int Variables { get; set; }

        /// <summary>Binary 變數數。</summary>
        public int Binary { get; set; }

        /// <summary>Integer 變數數。</summary>
        public int Integer { get; set; }

        /// <summary>連續變數數。</summary>
        public int Continuous { get; set; }

        /// <summary>線性限制式條數（含範圍限制式與軟性限制式）。</summary>
        public int Constraints { get; set; }

        /// <summary>框架不建立也不索引的模型元素總數（SOS、二次限制式、indicator、semi-continuous、lazy / user cut）。框架端恆為 0。</summary>
        public int SpecialElements { get; set; }

        /// <summary>特殊元素的分項明細；只有 solver 端有值。</summary>
        public string SpecialDetail { get; set; }

        /// <summary>目標式方向；null = 沒有目標式。</summary>
        public ObjectiveSense? Objective { get; set; }
    }

    /// <summary>對帳不一致的一個項目。值以文字保存，目標式方向與數量共用同一種格式。</summary>
    public sealed class ModelStatsMismatch
    {
        /// <summary>項目名：Variables / Binary / Integer / Continuous / Constraints / SpecialElements / Objective。</summary>
        public string Item { get; set; }

        /// <summary>框架建模記帳的值。</summary>
        public string Framework { get; set; }

        /// <summary>框架索引的值（Variables / Constraints 才有）；其他項目為 null。</summary>
        public string Index { get; set; }

        /// <summary>solver 模型實際的值。</summary>
        public string Solver { get; set; }

        /// <summary>落差的可能原因，給人判讀用。</summary>
        public string Reason { get; set; }
    }

    /// <summary>
    /// 框架建模統計與 solver 模型實際統計的對帳結果。
    /// 三個來源：建模記帳（Build*Vs / Create* / 軟性限制式 / 目標式 / 匯入 re-index 親手建了什麼）、
    /// 框架索引（Variables 與限制式清單）、solver 模型（CPLEX Ncols / Nrows / NbinVars …）。三者一致才算對得上。
    /// </summary>
    public sealed class ModelStatsReport
    {
        /// <summary>模型來源：Authored（框架建模）/ Imported（匯入模型檔）/ Imported+Authored（匯入後再追加）。</summary>
        public string Source { get; set; }

        /// <summary>框架建模記帳。</summary>
        public ModelCounts Framework { get; set; } = new ModelCounts();

        /// <summary>框架索引持有的變數數。</summary>
        public int IndexedVariables { get; set; }

        /// <summary>框架索引持有的限制式條數。</summary>
        public int IndexedConstraints { get; set; }

        /// <summary>solver 模型實際持有的內容。</summary>
        public ModelCounts Solver { get; set; } = new ModelCounts();

        /// <summary>不一致的項目；空 = 完全一致。</summary>
        public List<ModelStatsMismatch> Mismatches { get; set; } = new List<ModelStatsMismatch>();

        /// <summary>三個來源是否逐項一致。</summary>
        public bool IsMatch => Mismatches == null || Mismatches.Count == 0;

        /// <summary>一行摘要，給 log 與實驗說明檔用。</summary>
        public string Summary => IsMatch
            ? "MATCH"
            : "MISMATCH:" + string.Join(",", Mismatches.Select(m => m.Item));
    }
}
