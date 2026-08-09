using System;
using System.Collections.Generic;
using System.Data;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 模型資料來源抽象：Dataload 只依賴本介面，換來源（CSV / 記憶體 / DB）不動模型 code。
    /// Set 與 Parameter 都透過 Load&lt;T&gt; 依 schema 映射；LoadData 則只回傳中立表格。
    /// </summary>
    public interface IDataSource
    {
        /// <summary>載入含 schema 的獨立表格，不映射至模型資料列。</summary>
        /// <param name="sourceName">
        /// 資料來源識別：<see cref="CsvDataSource"/> 使用 <c>Data/{sourceName}</c> 下的 CSV 檔案；
        /// <see cref="DbDataSource"/> 使用完整 SQL；<see cref="InMemoryDataSource"/> 使用已註冊的表格名稱。
        /// </param>
        DataTable LoadData(string sourceName);

        /// <summary>依欄名與 public property 名稱對應，載入具型別的 Set 或 Parameter model row。</summary>
        /// <typeparam name="T">要建立的 Set 或 Parameter row 型別。</typeparam>
        /// <param name="sourceName">
        /// 資料來源識別。使用 <see cref="CsvDataSource"/> 時，檔案位於 <c>Data/{sourceName}</c>，
        /// 可傳入有或沒有 <c>.csv</c> 的檔名，而且檔名不必等於 <typeparamref name="T"/> 的類別名稱；
        /// 使用 <see cref="DbDataSource"/> 時是完整 SQL；使用 <see cref="InMemoryDataSource"/> 時是已註冊的表格名稱。
        /// 省略時使用 <c>typeof(T).Name</c> 作為來源名稱。
        /// </param>
        /// <returns>依來源資料列順序建立的 model row 清單。</returns>
        List<T> Load<T>(string sourceName = null) where T : ModelElementBase, new()
        {
            var resolvedSourceName = sourceName ?? typeof(T).Name;
            return ModelRowMapper.MapTable<T>(LoadData(resolvedSourceName), resolvedSourceName);
        }
    }

    /// <summary>
    /// 解結果輸出抽象：求解成功後把某變數型別的解寫到目的地（CSV / DB）。
    /// 實作：CsvSolutionSink / OracleSolutionSink。
    /// </summary>
    public interface ISolutionSink
    {
        /// <summary>輸出某變數型別的全部解值。dataId / userId 供多情境與稽核欄位（實作可忽略）。</summary>
        void WriteSolution<TVariableClass>(ISolverEngine engine, string dataId = null, string userId = null);

        /// <summary>開一個批次：多變數型別原子寫入。CSV 為 no-op batch，Oracle 為真 transaction。</summary>
        ISolutionBatch BeginBatch(string dataId = null, string userId = null);
    }

    /// <summary>單一輸出 transaction 的批次寫入：未 Commit 即 Dispose 視為 rollback。</summary>
    public interface ISolutionBatch : IDisposable
    {
        /// <summary>把某變數型別的解排進本批次（實際落地時機由實作決定）。</summary>
        void Write<TVariableClass>(ISolverEngine engine);

        /// <summary>送出整批；全部成功才算數，任一失敗整批不留。</summary>
        void Commit();
    }
}
