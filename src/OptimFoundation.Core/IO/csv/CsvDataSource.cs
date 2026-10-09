using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 從 FolderDir.Input 載入 CSV；預設檔名為型別名，依表頭對應 property，缺欄拋例外。
    /// </summary>
    public sealed class CsvDataSource : IDataSource
    {
        /// <summary>
        /// 建立 FolderDir.Input；讀取缺檔時拋 FileNotFoundException。
        /// </summary>
        public CsvDataSource() => FolderDir.Input.CreateFolder();

        /// <summary>從 FolderDir.Input 載入 fileName 指定的 CSV，保留表頭並解析完整資料列；.csv 副檔名可省略。</summary>
        private IEnumerable<string[]> LoadRows(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(fileName), "fileName 不得為空"),
                    "CSV 資料來源不合法", null, nameof(LoadRows), fileName, "檔名為空");

            // 遇到非 UTF-8 位元組直接丟例外，不讓中文 key 悄悄變成 U+FFFD；有 BOM 時依 BOM 判斷。
            using var reader = new StreamReader(FolderDir.Input.GetPathFile(EnsureCsv(fileName)), StrictUtf8);
            foreach (var row in CsvCtrl.ParseCsv(reader))
                yield return row;
        }

        /// <summary>把含表頭的 CSV 讀成 DataTable，不建立 Set 或 Parameter 物件。</summary>
        public DataTable LoadData(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(fileName), "fileName 不得為空"),
                    "CSV 資料來源不合法", null, nameof(LoadData), fileName, "檔名為空");
            try
            {
                return TabularData.ToDataTable(LoadRows(fileName), fileName);
            }
            catch (DecoderFallbackException ex)
            {
                throw Logging.ErrorOnce(
                    new InvalidDataException($"CSV 不是 UTF-8 編碼：{fileName}；請另存成 UTF-8（Excel 選「CSV UTF-8」）", ex),
                    "CSV 編碼不合法", null, nameof(LoadData), fileName, "不是UTF-8編碼");
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "CSV 載入失敗", null, nameof(LoadData), fileName,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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
        /// 每次 Write 立即寫檔；Commit 無動作，不支援回滾。
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
