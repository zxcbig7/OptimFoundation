using OptimFoundation.Core;

namespace OptimFoundation.Cplex.Tests.Mocks
{
    /// <summary>
    /// 不依賴任何 solver 的最小 EngineBase 實作。
    /// 以字串表示變數，以清單保存算式項目；記錄變數、限制式與目標式資訊供測試檢查，不實際求解。
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

        public override void LoadConfig(ISolverConfig config) { }

        // 以名稱表示變數，並記錄界限與型別，供測試檢查 BuildVars 的前綴判定。
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

        protected override void SetObjective(object expr, double constant, ObjectiveSense sense)
        {
            ObjectiveSenseResult = sense;
            ObjectiveConstantResult = constant;
        }

        public double? ObjectiveConstantResult { get; private set; }

        public readonly List<(string Name, List<(string Var, double Value)> Entries)> MipStarts = new();

        protected override void AddMIPStartCore(IReadOnlyList<(string var, double value)> entries, string name)
            => MipStarts.Add((name, entries.ToList()));

        protected override void SetVariableBounds(string variable, double? lb, double? ub) { }

        // 依 AddVariable 記錄的型別計算各類變數數量，模擬向 solver 查詢模型包含哪些變數。
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

        // 公開呼叫 protected ReadVar 的方法，讓測試比較以變數物件或名稱查詢時是否得到相同結果。
        public string ReadVarByInstance(object searchData) => ReadVar(searchData);
        public string ReadVarByName(string varName) => ReadVar(varName);

        // 模擬 import：繞過 Build*Vs 直接建變數，只進 Variables、不記建立統計
        public string AddUnregisteredVar(string name) => AddVariable(name, 0, OptBounds.Infinity, VarType.Continuous);

        protected override void BuildCore() => LoadConfig(SolverConfig);
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
