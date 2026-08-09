using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// CSV 資料來源：包 CsvCtrl，檔案放 Data/ 資料夾。
    /// 參數檔名自由（省略則 = 型別名），契約是欄位對得上 class（LoadParam 表頭缺欄即丟例外）；set 檔 = Set_{name}.csv。
    /// </summary>
    public sealed class CsvDataSource : IDataSource
    {
        /// <summary>
        /// 建構即備好輸入資料夾 Data/（即使空的）：引用 CSV 來源就把資料夾建好，使用者一眼知道往哪放檔；
        /// 缺檔的錯誤也從 DirectoryNotFound 降為明確的 FileNotFound（少了哪個檔一目了然）。
        /// 與輸出端對稱——Solution/ 等輸出資料夾寫入時本就自動建立（CsvCtrl.WriteSolution → TryCreateFile）。
        /// </summary>
        public CsvDataSource() => FolderDir.Data.CreateFolder();

        /// <summary>從 <c>Data/{name}.csv</c> 載入完整 RFC4180 Set 資料列，包含必填表頭。</summary>
        private IEnumerable<string[]> LoadRows(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

            using var reader = new StreamReader(FolderDir.Data.GetFilePath(EnsureCsv(name)), Encoding.UTF8);
            foreach (var row in CsvCtrl.ParseCsv(reader))
                yield return row;
        }

        /// <summary>將含 schema 的 CSV 載入為中立 DataTable，不映射至 Set 或 Parameter。</summary>
        public DataTable LoadData(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            return TabularData.ToDataTable(LoadRows(name), name);
        }

        /// <summary>依 CSV 表頭與 Parameter 的 public property 對應，載入具型別 Parameter。</summary>
        private static string EnsureCsv(string fileName)
            => fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".csv";

    }

    /// <summary>
    /// CSV 解輸出：包 CsvCtrl.WriteSolution，寫到 Solution/{變數型別名}.csv。
    /// 輸出帶表頭，可直接由 CsvDataSource.LoadParam 讀回（round-trip）。
    /// </summary>
    public sealed class CsvSolutionSink : ISolutionSink
    {
        /// <summary>把某變數型別的解寫成 Solution/{型別名}.csv（覆寫既有檔）。dataId / userId 省略時寫空字串。</summary>
        public void WriteSolution<TVariableClass>(ISolverEngine engine, string dataId = null, string userId = null)
            => CsvCtrl.WriteSolution<TVariableClass>(engine, dataId ?? "", userId ?? "");

        /// <summary>
        /// 取得批次輸出物件。CSV 沒有 transaction 語意，這是 no-op batch——每次 Write 就直接落檔，Commit 不做事。
        /// 存在的理由是讓消費端寫法與 DB sink 一致（換來源不必改 code）。
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

            /// <summary>no-op：CSV 在 Write 當下就已落檔。</summary>
            public void Commit()
            {
            }

            /// <summary>no-op：沒有需要釋放的資源。</summary>
            public void Dispose()
            {
            }
        }
    }
}
