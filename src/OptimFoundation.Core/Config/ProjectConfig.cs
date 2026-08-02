namespace OptimFoundation.Core
{
    /// <summary>
    /// 專案層設定：這個專案叫什麼、要留哪些檔。
    /// 與「solver 怎麼解」無關——跑 tuning 掃描時本物件不該跟著變，
    /// 也因此它不會進 ConfigSnapshot，不污染實驗分析資料。
    /// 每個屬性的預設值 = 今天不設定時的行為。
    /// 檔案「放哪」不在本型別職責內：路徑仍由 FolderDir 固定配置（見規格 Out of Scope）。
    /// </summary>
    public sealed class ProjectConfig
    {
        /// <summary>Creates a shallow copy containing every current public setting.</summary>
        public ProjectConfig Clone() => (ProjectConfig)MemberwiseClone();
        /// <summary>專案名（log 檔與 LP/MPS/Sol/IIS 檔名前綴）。null = 沿用 OptModel ctor 的 projectName。</summary>
        public string ProjectName { get; set; }

        /// <summary>輸出檔保留天數，超過就在 Execute() 開頭清掉。null = 30；&lt;= 0 關閉清理。</summary>
        public int? RetentionDays { get; set; }

        /// <summary>solver 求解過程的 log 是否即時印到 Console。false 仍完整寫進框架 log 檔，只是不洗畫面。</summary>
        public bool EnableSolverLog { get; set; } = true;

        /// <summary>求解前把模型匯出成 .lp（人可讀，對照 Model.md 驗證用）→ Models/。</summary>
        public bool ExportLP { get; set; } = false;

        /// <summary>求解前把模型匯出成 .mps（標準交換格式）→ Models/。</summary>
        public bool ExportMPS { get; set; } = false;

        /// <summary>求解成功後把解匯出成 .sol → Sols/。</summary>
        public bool ExportSol { get; set; } = false;

        /// <summary>寫解時的 DATA_ID 欄位預設值。null = 呼叫端自己傳。</summary>
        public string DataId { get; set; }

        /// <summary>寫解時的 USER 欄位預設值。null = 呼叫端自己傳。</summary>
        public string UserId { get; set; }
    }
}
