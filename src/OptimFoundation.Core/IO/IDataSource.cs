using System.Collections.Generic;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 模型資料來源抽象：Dataload 只依賴本介面，換來源（記憶體 / CSV / DB）不動模型 code——
    /// 與「換 solver 只換 OptEngine」同一哲學。實作：InMemoryDataSource / CsvDataSource / DbDataSource。
    /// </summary>
    public interface IDataSource
    {
        /// <summary>讀取某參數型別的全部列。Set 建議由 Parameter 衍生（distinct），與資料永不失同步。</summary>
        List<TParamClass> ReadParameters<TParamClass>() where TParamClass : ModelElementBase, new();

        /// <summary>讀取無法由 Parameter 衍生的一維 set（如獨立的名單檔/表）。name 為邏輯名稱，各實作自行解析。</summary>
        List<string> ReadSet(string name);
    }

    /// <summary>
    /// 解結果輸出抽象：求解成功後把某變數型別的解寫到目的地（CSV / DB）。
    /// 實作：CsvSolutionSink / OracleSolutionSink。
    /// </summary>
    public interface ISolutionSink
    {
        /// <summary>輸出某變數型別的全部解值。dataId / userId 供多情境與稽核欄位（實作可忽略）。</summary>
        void WriteSolution<TVariableClass>(ISolverEngine engine, string dataId = null, string userId = null);
    }
}
