using OptimFoundation.Core;

namespace ThreadTest.VariableClass
{
    public class VariableX_Supply : VariableBase
    {
        public string Source { get; set; }
        public string Dest { get; set; }

        public VariableX_Supply(params object[] Sets) => InitClassBySets(Sets);
        public VariableX_Supply() { }
    }
}
