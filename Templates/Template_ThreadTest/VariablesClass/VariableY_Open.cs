using OptimFoundation.Core;

namespace ThreadTest.VariableClass
{
    /// <summary>
    /// 主問題二元變數：貨源 s 是否開設（1=開, 0=不開）
    /// </summary>
    public class VariableY_Open : VariableBase
    {
        public string Source { get; set; }

        public VariableY_Open(params object[] Sets) => InitClassBySets(Sets);
        public VariableY_Open() { }
    }
}
