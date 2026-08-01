using OptimFoundation.Modeling;
using Sudoku_SHC279.SetClass;

namespace Sudoku_SHC279.ParameterClass;

/// <summary>題盤已知數字的三維 key；純 key parameter，因此不生成 QTY。</summary>
[OptParam(HasValue = false)]
[OptDim<Set_Row>("Row")]
[OptDim<Set_Column>("Column")]
[OptDim<Set_Digit>("Digit")]
public partial class Parameter_Given { }
