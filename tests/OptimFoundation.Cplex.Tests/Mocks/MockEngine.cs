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
        public readonly List<(string Name, double Lb, double Ub, VarType Type)> BuiltVars = new();
        public ObjectiveSense? ObjectiveSenseResult { get; private set; }
        private readonly Dictionary<string, VarType> _varTypes = new();
        public override int ConstraintCount => BuiltConstraints.Count;

        private static readonly MockConfig _cfg = new();
        public MockEngine() : base(_cfg) { }
        public MockEngine(ISolverConfig config) : base(config) { }

        public override void Configuration(ISolverConfig config) { }

        // 變數用自身名稱作為 TVar，寫入 Variables dict；bounds/type 一併記錄供 BuildVars 前綴解析測試斷言
        protected override string AddVariable(string name, double lb, double ub, VarType type)
        {
            BuiltVars.Add((name, lb, ub, type));
            _varTypes[name] = type;
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

        // mock 的「模型」就是這份型別表——AddVariable 是唯一入口，等同 solver 端的模型狀態
        protected override (int Continuous, int Integer, int Binary, bool HasDiscreteStructure) ReadModelComposition()
        {
            int continuous = 0, integer = 0, binary = 0;
            foreach (var type in _varTypes.Values)
            {
                if (type == VarType.Integer) integer++;
                else if (type == VarType.Binary) binary++;
                else continuous++;
            }
            return (continuous, integer, binary, integer + binary > 0);
        }

        // protected 成員的測試通道：ReadVar / SetVar* 的 typed 與 string 兩條路徑要能直接對照
        public string ReadVarByInstance(object searchData) => ReadVar(searchData);
        public string ReadVarByName(string varName) => ReadVar(varName);

        // 模擬 import：繞過 Build*Vs 直接建變數，只進 Variables 不進 VariableSets
        public string AddUnregisteredVar(string name) => AddVariable(name, 0, 1E100, VarType.Continuous);

        protected override void BuildCore() => Configuration(Config);
        protected override bool SolveCore() => true;
        public override double GetObjectiveValue() => 0;
        public override double GetVariableValue(string name) => 0;
        public override void Dispose() { }
    }

    internal class MockConfig : ISolverConfig
    {
        public double? TimeLimit { get; set; }
        public double? MipGap { get; set; }
        public int? Threads { get; set; }
        public int? Seed { get; set; }
        public int? Emphasis { get; set; }
        public double? FeasibilityTol { get; set; }
        public double? OptimalityTol { get; set; }
        public int? RootAlgorithm { get; set; }
        public int? Presolve { get; set; }
        public double? HeuristicEffort { get; set; }
        public double? MemoryLimitMb { get; set; }
        public bool LogToConsole { get; set; }
        public string LogFilePath { get; set; } = "";
    }
}
