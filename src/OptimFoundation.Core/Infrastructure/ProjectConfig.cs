namespace OptimFoundation.Core
{
    /// <summary>
    /// 設定是否在 Console 顯示求解器 log，以及是否匯出模型與解答；由 OptProject / OptExperiment 的 LoadConfig 載入。
    /// 只控制輸出，不調整求解演算法，也不列入 ConfigSnapshot。比較求解設定時應固定這些選項。
    /// 專案名與保留期由 OptProject 建構子指定，檔案位置由 FolderDir 決定。
    /// 正式求解（Solve）使用屬性預設值；實驗（Experiment）預設用 <see cref="Quiet"/>。
    /// </summary>
    public sealed class ProjectConfig
    {
        /// <summary>建立淺層複本，複製目前所有設定值。</summary>
        public ProjectConfig Clone() => (ProjectConfig)MemberwiseClone();

        /// <summary>關閉求解器 log 的 Console 顯示與模型/解答匯出；框架 log 仍會記錄。實驗預設使用此設定。</summary>
        public static ProjectConfig Quiet() => new ProjectConfig { EnableSolverLog = false };

        /// <summary>是否即時把求解器 log 印到 Console；設為 false 時仍會寫入框架 log 檔。</summary>
        public bool EnableSolverLog { get; set; } = true;

        /// <summary>求解前把模型匯出成可讀的 .lp 檔，存入 FolderDir.Model，供對照 Model.md。</summary>
        public bool ExportLP { get; set; } = false;

        /// <summary>求解前把模型匯出成 .mps 標準交換格式，存入 FolderDir.Model。</summary>
        public bool ExportMPS { get; set; } = false;

        /// <summary>求解成功後把解匯出成 .sol 檔，存入 FolderDir.Solution。</summary>
        public bool ExportSol { get; set; } = false;

        /// <summary>預留的 IIS 匯出開關；目前框架沒有讀取此屬性，設定後不會影響輸出。</summary>
        public bool ExportIIS { get; set; } = false;
    }
}
