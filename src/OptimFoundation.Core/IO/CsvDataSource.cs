using System.Collections.Generic;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// CSV 資料來源：包 CsvCtrl，檔案放 Data/ 資料夾。
    /// 慣例：參數檔 = {型別名}.csv（canonical schema，建議帶表頭按名對位）；set 檔 = Set_{name}.csv。
    /// </summary>
    public sealed class CsvDataSource : IDataSource
    {
        public List<TParamClass> ReadParameters<TParamClass>() where TParamClass : ModelElementBase, new()
            => CsvCtrl.BuildParameter<TParamClass>();

        public List<string> ReadSet(string name)
            => CsvCtrl.ReadStrSet(name.StartsWith("Set_") ? name : $"Set_{name}");
    }

    /// <summary>
    /// CSV 解輸出：包 CsvCtrl.SaveSolutionToCSV，寫到 Solution/{變數型別名}.csv。
    /// 輸出帶表頭，可直接被 CsvCtrl.BuildParameter 讀回（round-trip）。
    /// </summary>
    public sealed class CsvSolutionSink : ISolutionSink
    {
        public void WriteSolution<TVariableClass>(ISolverEngine engine, string dataId = null, string userId = null)
            => CsvCtrl.SaveSolutionToCSV<TVariableClass>(engine, dataId ?? "", userId ?? "");
    }
}
