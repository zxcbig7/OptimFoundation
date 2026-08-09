using System;
using System.Collections.Generic;
using System.Data;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 模型資料來源抽象：Dataload 只依賴本介面，換來源（CSV / 記憶體 / DB）不動模型 code。
    /// LoadSet 與 LoadParam 都遵循 schema；LoadData 則刻意不認得任何積木。
    /// </summary>
    public interface IDataSource
    {
        /// <summary>載入完整 Set 資料列，包含必填表頭。</summary>
        /// <summary>載入含 schema 的獨立 CSV／DB 表格，不考慮任何模型積木。</summary>
        DataTable LoadData(string file);

        /// <summary>依表頭與 public property 名稱對應，載入具型別的 Parameter。</summary>
        List<T> Load<T>(string file = null) where T : ModelElementBase, new()
        {
            var name = file ?? typeof(T).Name;
            return ModelRowMapper.MapTable<T>(LoadData(name), name);
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
