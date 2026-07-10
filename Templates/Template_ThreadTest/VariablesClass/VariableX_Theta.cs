using OptimFoundation.Core;

namespace ThreadTest.VariableClass
{
    /// <summary>
    /// 主問題連續變數：子問題成本的近似值（Benders theta）
    /// </summary>
    public class VariableX_Theta : VariableBase
    {
        public string Index { get; set; }

        public VariableX_Theta(params object[] Sets) => InitClassBySets(Sets);
        public VariableX_Theta() { }
    }
}
