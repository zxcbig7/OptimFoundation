using System;
using System.Collections.Generic;
using System.Data;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// Dataload 透過此介面讀取資料，讓 CSV、記憶體與資料庫使用相同的載入方式。
    /// Load&lt;T&gt; 依欄名建立 Set 或 Parameter 資料列；LoadData 直接回傳含欄名的 DataTable。
    /// </summary>
    public interface IDataSource
    {
        /// <summary>載入含欄名的 DataTable，保留表格形式，不建立 Set 或 Parameter 物件。</summary>
        /// <param name="sourceName">
        /// 來源名稱：<see cref="CsvDataSource"/> 讀取 FolderDir.Input 下以 sourceName 指定的 CSV 檔；
        /// <see cref="DbDataSource"/> 使用完整 SQL；<see cref="InMemoryDataSource"/> 使用已註冊的表格名稱。
        /// </param>
        DataTable LoadData(string sourceName);

        /// <summary>依欄名與 public property 名稱對應，載入具型別的 Set 或 Parameter model row。</summary>
        /// <typeparam name="T">要建立的 Set 或 Parameter row 型別。</typeparam>
        /// <param name="sourceName">
        /// 使用 <see cref="CsvDataSource"/> 時，sourceName 是 FolderDir.Input 下的檔名，
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
    /// 求解結果的輸出介面，用來把某變數型別的解值寫成 CSV 或存入資料庫。
    /// 實作：CsvSolutionSink / OracleSolutionSink。
    /// </summary>
    public interface ISolutionSink
    {
        /// <summary>輸出某變數型別的全部解值。dataId 用來識別資料批次，userId 表示寫入者；實作可忽略這兩欄。</summary>
        void WriteSolution<TVariableClass>(ISolverEngine engine, string dataId = null, string userId = null);

        /// <summary>開始輸出多個變數型別。Oracle 等 Commit 才在同一交易寫入；CSV 每次 Write 都立即寫檔。</summary>
        ISolutionBatch BeginBatch(string dataId = null, string userId = null);
    }

    /// <summary>批次輸出介面。Oracle 在 Commit 時執行交易，未 Commit 就 Dispose 會捨棄待寫操作；CSV 已寫出的檔案不會回復。</summary>
    public interface ISolutionBatch : IDisposable
    {
        /// <summary>將某變數型別的解加入本批次；何時寫出由實作決定。</summary>
        void Write<TVariableClass>(ISolverEngine engine);

        /// <summary>完成本批輸出。Oracle 會在同一交易中寫入，失敗時全部回滾；CSV 已於 Write 寫檔，此方法不做任何事。</summary>
        void Commit();
    }
}
