namespace OptimFoundation.Core
{

    /// <summary>實驗輸出格式：CSV 扁平給人看，JSON 巢狀含軌跡、是累積時的權威來源。</summary>
    public enum ExpWriterType
    {
        /// <summary>逗號分隔，一列一個 Trial 摘要。</summary>
        CSV,

        /// <summary>巢狀 JSON，含完整設定快照與收斂軌跡。</summary>
        JSON
    }

    /// <summary>
    /// 實驗持久化策略。實作：CSV（扁平、給人）、JSON（巢狀含軌跡、給 LLM）。
    /// 同名實驗為 append（Load → 合併 → 覆寫整檔），不覆寫歷史。
    /// </summary>
    public interface IExperimentWriter
    {
        /// <summary>輸出副檔名（不含點），如 "csv" / "json"。</summary>
        ExpWriterType Extension { get; }

        /// <summary>將整個 Experiment 寫到 experiments/&lt;name&gt;.&lt;ext&gt;。</summary>
        void Write(Experiment experiment, string path);

        /// <summary>讀回既有實驗（累積用）；檔案不存在回 null。</summary>
        Experiment Read(string path);
    }
}
