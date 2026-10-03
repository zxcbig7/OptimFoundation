using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 從 FolderDir.Input 讀取 CSV 檔案，並使用 CsvCtrl 解析內容。
    /// 檔名省略時使用型別名；Set 與 Parameter 都依 CSV 表頭對應資料列的 public property，缺欄即丟例外。
    /// </summary>
    public sealed class CsvDataSource : IDataSource
    {
        /// <summary>
        /// 建立 FolderDir.Input，供使用者放入 CSV；讀取缺檔會拋出含檔名的 FileNotFoundException。輸出端也會先建立資料夾。
        /// </summary>
        public CsvDataSource() => FolderDir.Input.CreateFolder();

        /// <summary>從 FolderDir.Input 載入 fileName 指定的 CSV，保留表頭並解析完整資料列；.csv 副檔名可省略。</summary>
        private IEnumerable<string[]> LoadRows(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(fileName)),
                    "CSV_SOURCE_INVALID", "CSV 資料來源不合法", nameof(LoadRows), fileName, "file_name_is_empty");

            using var reader = new StreamReader(FolderDir.Input.GetPathFile(EnsureCsv(fileName)), Encoding.UTF8);
            foreach (var row in CsvCtrl.ParseCsv(reader))
                yield return row;
        }

        /// <summary>把含表頭的 CSV 讀成 DataTable，不建立 Set 或 Parameter 物件。</summary>
        public DataTable LoadData(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(fileName)),
                    "CSV_SOURCE_INVALID", "CSV 資料來源不合法", nameof(LoadData), fileName, "file_name_is_empty");
            try
            {
                return TabularData.ToDataTable(LoadRows(fileName), fileName);
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "CSV_LOAD_FAILED", "公開 API 執行失敗", nameof(LoadData), fileName,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        private static string EnsureCsv(string fileName)
            => fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".csv";

    }

    /// <summary>
    /// 呼叫 CsvCtrl.WriteSolution，把解值寫到 FolderDir.Output 下的 {變數型別名}.csv。
    /// 輸出帶表頭；欄位相容時可由 IDataSource.Load&lt;T&gt; 讀回。
    /// </summary>
    public sealed class CsvSolutionSink : ISolutionSink
    {
        /// <summary>把某變數型別的解寫到 FolderDir.Output 下的 {型別名}.csv，覆寫同名檔；CSV 不輸出 dataId / userId。</summary>
        public void WriteSolution<TVariableClass>(ISolverEngine engine, string dataId = null, string userId = null)
            => CsvCtrl.WriteSolution<TVariableClass>(engine, dataId ?? "", userId ?? "");

        /// <summary>
        /// 取得批次輸出物件。每次 Write 都立即寫檔，Commit 不做任何事，也不提供交易回滾。
        /// 這個物件提供與資料庫輸出相同的呼叫方式，方便切換輸出目的地。
        /// </summary>
        public ISolutionBatch BeginBatch(string dataId = null, string userId = null)
            => new CsvSolutionBatch(this, dataId, userId);

        private sealed class CsvSolutionBatch : ISolutionBatch
        {
            private readonly CsvSolutionSink _sink;
            private readonly string _dataId;
            private readonly string _userId;

            /// <summary>記下 sink 與整批共用的 dataId / userId。</summary>
            public CsvSolutionBatch(CsvSolutionSink sink, string dataId, string userId)
            {
                _sink = sink;
                _dataId = dataId;
                _userId = userId;
            }

            /// <summary>立即寫出該變數型別的解（不等 Commit）。</summary>
            public void Write<TVariableClass>(ISolverEngine engine)
                => _sink.WriteSolution<TVariableClass>(engine, _dataId, _userId);

            /// <summary>不做任何事；CSV 已在 Write 時寫出。</summary>
            public void Commit()
            {
            }

            /// <summary>不做任何事；沒有需要釋放的資源。</summary>
            public void Dispose()
            {
            }
        }
    }
}
