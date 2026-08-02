using OptimFoundation.Cplex;

namespace Sudoku_SHC279
{
    /// <summary>逐項建立 Sudoku 的零權重可行性目標；對應 Model.md 的 OBJ。</summary>
    public sealed class ObjectiveFunction
    {
        private readonly Set_Row _rows;
        private readonly Set_Column _columns;
        private readonly Set_Digit _digits;
        private readonly List<Parameter_ObjCoef> _objCoefByDigit;

        public ObjectiveFunction(
            Set_Row rows,
            Set_Column columns,
            Set_Digit digits,
            List<Parameter_ObjCoef> objCoefByDigit)
        {
            _rows = rows;
            _columns = columns;
            _digits = digits;
            _objCoefByDigit = objCoefByDigit;
        }

        public void Build(OptEngine engine)
        {
            foreach (int row in _rows)
                foreach (int column in _columns)
                    foreach (int digit in _digits)
                    {
                        double coefficient = _objCoefByDigit.Single(
                            parameter => parameter.Digit == digit).QTY;
                        engine.AddLHS(
                            coefficient,
                            new VariableB_CellDigit { Row = row, Column = column, Digit = digit });
                    }

            engine.CreateMinimize();
        }
    }
}
