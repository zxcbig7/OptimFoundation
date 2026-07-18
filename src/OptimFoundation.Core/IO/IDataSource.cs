using System.Collections.Generic;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 名稱可定址的模型資料來源抽象：Dataload 只依賴本介面，換來源（CSV / 記憶體）不動模型 code——
    /// 與「換 solver 只換 OptEngine」同一哲學。實作：CsvDataSource / InMemoryDataSource。
    /// DB 是 query-only（第一引數是 SQL 而非名稱），故 DbDataSource 不實作本介面，但共用 LoadParam / LoadSet 命名。
    /// </summary>
    public interface IDataSource
    {
        /// <summary>
        /// 讀某參數型別的全部列。file = 資料檔名（自由，省略則 = 型別名）——檔名無限制，契約是「欄位對得上 class 的 property」，
        /// 對不上即丟例外。
        /// </summary>
        List<TParamClass> LoadParam<TParamClass>(string file = null) where TParamClass : ModelElementBase, new();

        /// <summary>讀一維 set。name = 邏輯名稱（檔名形式 Set_{X}），各實作自行解析位址。</summary>
        List<string> LoadSet(string name);
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
