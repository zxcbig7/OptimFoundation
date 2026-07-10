using OptimFoundation.Core;

namespace OptimFoundation.Cplex.Tests.Mocks
{
    // 用於 VariableBuilder / EngineBase 測試的最小 Variable 類別
    internal class VarS : VariableBase
    {
        public string S { get; set; } = "";
    }

    internal class VarDG : VariableBase
    {
        public DateTime D { get; set; }
        public string G { get; set; } = "";
    }

    internal class VarInt : VariableBase
    {
        public int N { get; set; }
    }

    // 用於 Parameter 測試
    internal class ParamX : ParameterBase
    {
        public string K { get; set; } = "";
        public double QTY { get; set; }
    }
}
