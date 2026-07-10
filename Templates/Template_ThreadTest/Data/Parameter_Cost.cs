using OptimFoundation.Core;

namespace ThreadTest.Data
{
    public class Parameter_Cost : ParameterBase
    {
        public string Source { get; set; }
        public string Dest { get; set; }
        public double QTY { get; set; }

        public Parameter_Cost(params object[] Sets) => InitClassBySets(Sets);
    }
}
