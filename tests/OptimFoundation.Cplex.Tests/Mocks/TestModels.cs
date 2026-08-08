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

    // 用於 BuildVars 前綴解析測試（命名天條：VariableB_/X_/I_ → Binary/Continuous/Integer）
    internal class VariableB_Pick : VariableBase
    {
        public string S { get; set; } = "";
    }

    internal class VariableX_Amt : VariableBase
    {
        public string S { get; set; } = "";
    }

    internal class VariableI_Cnt : VariableBase
    {
        public string S { get; set; } = "";
    }

    internal class VariableX_ArcFlow : VariableBase
    {
        public string NodeFrom { get; set; } = "";
        public string NodeTo { get; set; } = "";
    }

    internal class VariableX_ArcFlowByDate : VariableBase
    {
        public string NodeFrom { get; set; } = "";
        public string NodeTo { get; set; } = "";
        public DateTime Date { get; set; }
    }

    internal class VariableX_ArcFlowWrongArity : VariableBase
    {
        public string NodeFrom { get; set; } = "";
    }

    // 用於 Parameter 測試
    internal class ParamX : ParameterBase
    {
        public string K { get; set; } = "";
        public double QTY { get; set; }
    }
}
