using System;
using System.Collections.Generic;
using System.Data;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// CSV 資料來源：包 CsvCtrl，檔案放 Data/ 資料夾。
    /// 參數檔名自由（省略則 = 型別名），契約是欄位對得上 class（BuildParameter 表頭缺欄即丟例外）；set 檔 = Set_{name}.csv。
    /// </summary>
    public sealed class CsvDataSource : IDataSource
    {
        /// <summary>
        /// 建構即備好輸入資料夾 Data/（即使空的）：引用 CSV 來源就把資料夾建好，使用者一眼知道往哪放檔；
        /// 缺檔的錯誤也從 DirectoryNotFound 降為明確的 FileNotFound（少了哪個檔一目了然）。
        /// 與輸出端對稱——Solution/ 等輸出資料夾寫入時本就自動建立（CsvCtrl.WriteSolution → TryCreateFile）。
        /// </summary>
        public CsvDataSource() => FolderDir.Data.CreateFolder();

        /// <summary>
        /// 從 Data/ 讀一份參數 CSV 成物件清單。表頭按名對位（大小寫不敏感、多餘欄忽略）。
        /// </summary>
        /// <param name="file">檔名；省略則用型別名。</param>
        /// <exception cref="System.IO.InvalidDataException">CSV 缺少某個 property 對應的欄位。</exception>
        public List<TParamClass> LoadParam<TParamClass>(string file = null) where TParamClass : ModelElementBase, new()
            => CsvCtrl.BuildParameter<TParamClass>(file);

        /// <summary>從 Data/Set_{name}.csv 讀一個 set 的成員（每列一個）。元素轉型交給 SetBase.ParseElement。</summary>
        public List<string> LoadSet(string name)
            => CsvCtrl.ReadStrSet(SetNaming.File(name));

        /// <summary>
        /// 讀整張 CSV 成 raw DataTable（第一列 = 欄名、全欄 string），供 set / param 以外的通用用途。
        /// 非 IDataSource 契約；與 DbDataSource.LoadTable 同名但第一引數是檔名（DB 那邊是 SQL）。
        /// </summary>
        public DataTable LoadTable(string file)
            => CsvCtrl.ReadTable(file);
    }

    /// <summary>
    /// CSV 解輸出：包 CsvCtrl.WriteSolution，寫到 Solution/{變數型別名}.csv。
    /// 輸出帶表頭，可直接被 CsvCtrl.BuildParameter 讀回（round-trip）。
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
