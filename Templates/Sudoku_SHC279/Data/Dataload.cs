using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using Sudoku_SHC279.ParameterClass;
using Sudoku_SHC279.SetClass;

namespace Sudoku_SHC279.Data;

/// <summary>Sudoku 資料唯一入口：Set 與 Given 都從 Data/*.csv 載入，再由 DataContext 驗證。</summary>
public partial class Dataload : DataContext
{
    public const string PuzzleName = "Sudoku_SHC279";

    public Set_Row ROW = new();
    public Set_Column COLUMN = new();
    public Set_Digit DIGIT = new();
    public List<Parameter_Given> parameter_Given = new();

    public Dataload() : this(new CsvDataSource()) { }

    /// <summary>
    /// 第一階段：吃不規則來源——9x9 題盤矩陣（0 = 空格），格式與框架要的三欄長格式不同，這裡把它攤平。
    /// rawFile 相對於 Data/（ReadMatrixCsv 的位址慣例），例：raw/Puzzle_SHC279。
    /// </summary>
    public Dataload(string rawFile)
    {
        var grid = CsvCtrl.ReadMatrixCsv(rawFile);
        int rows = grid.GetLength(0);
        int cols = grid.GetLength(1);

        ROW.LoadFrom(Enumerable.Range(1, rows));
        COLUMN.LoadFrom(Enumerable.Range(1, cols));
        DIGIT.LoadFrom(Enumerable.Range(1, rows));

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                if (grid[r, c] > 0)
                    parameter_Given.Add(new Parameter_Given(r + 1, c + 1, (int)grid[r, c]));
    }

    /// <summary>第二階段：標準接口。</summary>
    public Dataload(IDataSource source)
    {
        ROW.Load(source, "Set_Row");
        COLUMN.Load(source, "Set_Column");
        DIGIT.Load(source, "Set_Digit");
        parameter_Given = source.LoadParam<Parameter_Given>("Parameter_Given");
    }

    /// <summary>
    /// 中間那個箭頭：把積木寫回 Data/，成為第二階段的輸入。
    /// 檔名 MUST 與上面 IDataSource ctor 讀取時用的名稱一致，否則下次讀不到。
    /// </summary>
    public void Export()
    {
        CsvCtrl.WriteSet(ROW, "Set_Row");
        CsvCtrl.WriteSet(COLUMN, "Set_Column");
        CsvCtrl.WriteSet(DIGIT, "Set_Digit");
        CsvCtrl.WriteParam(parameter_Given, "Parameter_Given");
    }
}
