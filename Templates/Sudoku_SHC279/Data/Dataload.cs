using System.Globalization;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;

namespace Sudoku_SHC279
{
    /// <summary>Sudoku 資料唯一入口；求解時只讀標準 CSV，import 時才展開原始矩陣。</summary>
    public sealed partial class Dataload : DataContext
    {
        public const string PuzzleName = "Sudoku_SHC279";

        public List<Set_Row> set_Row = new();
        public List<Set_Column> set_Column = new();
        public List<Set_Digit> set_Digit = new();
        public List<Set_Block> set_Block = new();
        public List<Set_Given> set_Given = new();
        public List<Set_BlockCell> set_BlockCell = new();
        public List<Parameter_ExactlyOne> parameter_ExactlyOne = new();
        public List<Parameter_ObjCoef> parameter_ObjCoef = new();

        public Dataload() : this(new CsvDataSource()) { }

        /// <summary>讀取已就位的 canonical CSV；此建構子只做資料載入。</summary>
        public Dataload(IDataSource source)
        {
            set_Row = source.Load<Set_Row>("Set_Row");
            set_Column = source.Load<Set_Column>("Set_Column");
            set_Digit = source.Load<Set_Digit>("Set_Digit");
            set_Block = source.Load<Set_Block>("Set_Block");
            set_Given = source.Load<Set_Given>("Set_Given");
            set_BlockCell = source.Load<Set_BlockCell>("Set_BlockCell");
            parameter_ExactlyOne = source.Load<Parameter_ExactlyOne>("Parameter_ExactlyOne");
            parameter_ObjCoef = source.Load<Parameter_ObjCoef>("Parameter_ObjCoef");
        }

        /// <summary>把方形原始題盤展開成 sets、givens、宮格對應與模型常數。</summary>
        public Dataload(string rawFile)
        {
            var rawData = new CsvDataSource().LoadData(rawFile);
            if (rawData.Rows.Count == 0)
                throw new InvalidDataException("Sudoku import requires at least one row.");
            int rowCount = rawData.Rows.Count;
            int columnCount = rawData.Columns.Count;
            int blockSide = (int)Math.Sqrt(rowCount);

            if (rowCount != columnCount || blockSide * blockSide != rowCount)
                throw new InvalidDataException("Sudoku 題盤必須是邊長為完全平方數的正方形。");

            set_Row = Enumerable.Range(1, rowCount).Select(Row => new Set_Row { Row = Row }).ToList();
            set_Column = Enumerable.Range(1, columnCount).Select(Column => new Set_Column { Column = Column }).ToList();
            set_Digit = Enumerable.Range(1, rowCount).Select(Digit => new Set_Digit { Digit = Digit }).ToList();
            set_Block = Enumerable.Range(1, rowCount).Select(Block => new Set_Block { Block = Block }).ToList();

            parameter_ExactlyOne.Add(new Parameter_ExactlyOne { QTY = 1.0 });

            foreach (int digit in set_Digit)
                parameter_ObjCoef.Add(new Parameter_ObjCoef { Digit = digit, QTY = 0.0 });

            var blockCells = new List<(int Block, int Row, int Column)>();
            var givens = new List<(int Row, int Column, int Digit)>();
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
                for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    int row = rowIndex + 1;
                    int column = columnIndex + 1;
                    int block = rowIndex / blockSide * blockSide + columnIndex / blockSide + 1;

                    blockCells.Add((block, row, column));

                    int digit = int.Parse(rawData.Rows[rowIndex][columnIndex]?.ToString()?.Trim() ?? string.Empty, CultureInfo.InvariantCulture);
                    if (digit > 0)
                        givens.Add((row, column, digit));
                }

            set_BlockCell = blockCells.Select(x => new Set_BlockCell { Block = x.Block, Row = x.Row, Column = x.Column }).ToList();
            set_Given = givens.Select(x => new Set_Given { Row = x.Row, Column = x.Column, Digit = x.Digit }).ToList();
        }

        /// <summary>把 import ctor 產生的資料輸出成求解流程使用的 canonical CSV。</summary>
        public void Export()
        {
            CsvCtrl.WriteRows(set_Row, "Set_Row");
            CsvCtrl.WriteRows(set_Column, "Set_Column");
            CsvCtrl.WriteRows(set_Digit, "Set_Digit");
            CsvCtrl.WriteRows(set_Block, "Set_Block");
            CsvCtrl.WriteRows(set_Given, "Set_Given");
            CsvCtrl.WriteRows(set_BlockCell, "Set_BlockCell");
            CsvCtrl.WriteRows(parameter_ExactlyOne, "Parameter_ExactlyOne");
            CsvCtrl.WriteRows(parameter_ObjCoef, "Parameter_ObjCoef");
        }
    }
}
