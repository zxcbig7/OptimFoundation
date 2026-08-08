using System.Globalization;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;

namespace Sudoku_SHC279
{
    /// <summary>Sudoku 資料唯一入口；求解時只讀標準 CSV，import 時才展開原始矩陣。</summary>
    public sealed partial class Dataload : DataContext
    {
        public const string PuzzleName = "Sudoku_SHC279";

        public Set_Row set_Row = new();
        public Set_Column set_Column = new();
        public Set_Digit set_Digit = new();
        public Set_Block set_Block = new();
        public List<Parameter_Given> parameter_Given = new();
        public List<Parameter_BlockCell> parameter_BlockCell = new();
        public List<Parameter_ExactlyOne> parameter_ExactlyOne = new();
        public List<Parameter_ObjCoef> parameter_ObjCoef = new();

        public Dataload() : this(new CsvDataSource()) { }

        /// <summary>讀取已就位的 canonical CSV；此建構子只做資料載入。</summary>
        public Dataload(IDataSource source)
        {
            set_Row.Load(source, "Set_Row");
            set_Column.Load(source, "Set_Column");
            set_Digit.Load(source, "Set_Digit");
            set_Block.Load(source, "Set_Block");
            parameter_Given = source.LoadParam<Parameter_Given>("Parameter_Given");
            parameter_BlockCell = source.LoadParam<Parameter_BlockCell>("Parameter_BlockCell");
            parameter_ExactlyOne = source.LoadParam<Parameter_ExactlyOne>("Parameter_ExactlyOne");
            parameter_ObjCoef = source.LoadParam<Parameter_ObjCoef>("Parameter_ObjCoef");
        }

        /// <summary>把方形原始題盤展開成 sets、givens、宮格對應與模型常數。</summary>
        public Dataload(string rawFile)
        {
            var rawRows = new CsvDataSource().LoadRows(rawFile).ToArray();
            if (rawRows.Length == 0)
                throw new InvalidDataException("Sudoku import requires at least one row.");
            int rowCount = rawRows.Length;
            int columnCount = rawRows[0].Length;
            if (rawRows.Any(row => row.Length != columnCount))
                throw new InvalidDataException("Sudoku import requires a rectangular CSV grid.");
            int blockSide = (int)Math.Sqrt(rowCount);

            if (rowCount != columnCount || blockSide * blockSide != rowCount)
                throw new InvalidDataException("Sudoku 題盤必須是邊長為完全平方數的正方形。");

            set_Row.LoadFrom(Enumerable.Range(1, rowCount));
            set_Column.LoadFrom(Enumerable.Range(1, columnCount));
            set_Digit.LoadFrom(Enumerable.Range(1, rowCount));
            set_Block.LoadFrom(Enumerable.Range(1, rowCount));

            parameter_ExactlyOne.Add(new Parameter_ExactlyOne { QTY = 1.0 });

            foreach (int digit in set_Digit)
                parameter_ObjCoef.Add(new Parameter_ObjCoef { Digit = digit, QTY = 0.0 });

            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
                for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    int row = rowIndex + 1;
                    int column = columnIndex + 1;
                    int block = rowIndex / blockSide * blockSide + columnIndex / blockSide + 1;

                    parameter_BlockCell.Add(new Parameter_BlockCell
                    {
                        Block = block,
                        Row = row,
                        Column = column,
                    });

                    int digit = int.Parse(rawRows[rowIndex][columnIndex].Trim(), CultureInfo.InvariantCulture);
                    if (digit > 0)
                        parameter_Given.Add(new Parameter_Given
                        {
                            Row = row,
                            Column = column,
                            Digit = digit,
                        });
                }
        }

        /// <summary>把 import ctor 產生的資料輸出成求解流程使用的 canonical CSV。</summary>
        public void Export()
        {
            CsvCtrl.WriteSet(set_Row, "Set_Row");
            CsvCtrl.WriteSet(set_Column, "Set_Column");
            CsvCtrl.WriteSet(set_Digit, "Set_Digit");
            CsvCtrl.WriteSet(set_Block, "Set_Block");
            CsvCtrl.WriteParam(parameter_Given, "Parameter_Given");
            CsvCtrl.WriteParam(parameter_BlockCell, "Parameter_BlockCell");
            CsvCtrl.WriteParam(parameter_ExactlyOne, "Parameter_ExactlyOne");
            CsvCtrl.WriteParam(parameter_ObjCoef, "Parameter_ObjCoef");
        }
    }
}
