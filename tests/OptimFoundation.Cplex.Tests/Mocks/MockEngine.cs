using OptimFoundation.Core;

namespace OptimFoundation.Cplex.Tests.Mocks
{
    /// <summary>
    /// 不依賴任何 solver 的最小 EngineBase 實作。
    /// 變數用字串表示自身，LinearExpr 不做任何事，constraint 只記錄名稱供驗證。
    /// </summary>
    internal class MockEngine : EngineBase<object, string, object, string>
    {
        public readonly List<string> BuiltConstraints = new();
        public ObjectiveSense? ObjectiveSenseResult { get; private set; }

        private static readonly MockConfig _cfg = new();
        public MockEngine() : base(_cfg) { }

        public override void Configuration(ISolverConfig config) { }

        // 變數用自身名稱作為 TVar，寫入 Variables dict
        protected override string AddVariable(string name, double lb, double ub, VarType type)
        {
            Variables[name] = name;
            return name;
        }

        protected override object LinearExpr(IEnumerable<(double coef, string var)> terms) => terms.ToList();

        protected override string AddConstraint(string name, object lhs, ConstraintSense sense, double rhs)
        {
            BuiltConstraints.Add(name);
            return name;
        }

        protected override string AddRangeConstraint(string name, object expr, double lb, double ub)
        {
            BuiltConstraints.Add(name);
            return name;
        }

        protected override void SetObjective(object expr, ObjectiveSense sense)
            => ObjectiveSenseResult = sense;

        protected override void SetVariableBounds(string variable, double? lb, double? ub) { }

        public override void Build() => Configuration(Config);
        public override bool Solve() => true;
        public override double GetObjectiveValue() => 0;
        public override double GetVariableValue(string name) => 0;
        public override void Dispose() { }
    }

    internal class MockConfig : ISolverConfig
    {
        public double? TimeLimit { get; set; }
        public double? MipGap { get; set; }
        public int? Threads { get; set; }
        public bool LogToConsole { get; set; }
        public string LogFilePath { get; set; } = "";
    }
}
