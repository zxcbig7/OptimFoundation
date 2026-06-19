namespace OptimFoundation.Core
{

    public enum ExpWriterType
    {
        CSV,
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
