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

        // file 自由；表頭按名對位（大小寫不敏感、多餘欄忽略），缺 property 對應欄即 InvalidDataException
        public List<TParamClass> LoadParam<TParamClass>(string file = null) where TParamClass : ModelElementBase, new()
            => CsvCtrl.BuildParameter<TParamClass>(file);

        // set 名統一為檔名形式（見 SetNaming）：Set_{名}.csv；元素轉型交給 SetBase.ParseElement
        public List<string> LoadSet(string name)
            => CsvCtrl.ReadStrSet(SetNaming.File(name));

        // 整張表 raw DataTable（set/param 以外的通用用途）：第一列=欄名、全欄 string。
        // 與 DbDataSource.LoadTable 同名，第一引數是檔名（DB 那邊是 SQL）。非 IDataSource 契約。
        public DataTable LoadTable(string file)
            => CsvCtrl.ReadTable(file);
    }

    /// <summary>
    /// CSV 解輸出：包 CsvCtrl.WriteSolution，寫到 Solution/{變數型別名}.csv。
    /// 輸出帶表頭，可直接被 CsvCtrl.BuildParameter 讀回（round-trip）。
    /// </summary>
    public sealed class CsvSolutionSink : ISolutionSink
    {
        public void WriteSolution<TVariableClass>(ISolverEngine engine, string dataId = null, string userId = null)
            => CsvCtrl.WriteSolution<TVariableClass>(engine, dataId ?? "", userId ?? "");

        // CSV 逐檔寫本無 transaction 語意：no-op batch，Write 直接呼叫既有 WriteSolution，Commit 空實作，
        // 保持既有 CSV 輸出行為可用。
        public ISolutionBatch BeginBatch(string dataId = null, string userId = null)
            => new CsvSolutionBatch(this, dataId, userId);

        private sealed class CsvSolutionBatch : ISolutionBatch
        {
            private readonly CsvSolutionSink _sink;
            private readonly string _dataId;
            private readonly string _userId;

            public CsvSolutionBatch(CsvSolutionSink sink, string dataId, string userId)
            {
                _sink = sink;
                _dataId = dataId;
                _userId = userId;
            }

            public void Write<TVariableClass>(ISolverEngine engine)
                => _sink.WriteSolution<TVariableClass>(engine, _dataId, _userId);

            public void Commit()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
