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
            Extract(lhs);
            return name;
        }

        protected override string AddRangeConstraint(string name, object expr, double lb, double ub)
        {
            BuiltConstraints.Add(name);
            Extract(expr);
            return name;
        }

        protected override void SetObjective(object expr, double constant, ObjectiveSense sense)
        {
            ObjectiveSenseResult = sense;
            ObjectiveConstantResult = constant;
            Extract(expr);
        }

        // 模擬 CPLEX 的收錄規則：變數要被限制式或目標式引用才算進模型（Ncols 不含只宣告沒用到的變數）
        private readonly HashSet<string> _extractedVars = new();

        private void Extract(object expr)
        {
            if (expr is IEnumerable<(double coef, string var)> terms)
                foreach (var term in terms) _extractedVars.Add(term.var);
        }

        // 模擬「solver 端有框架不知道的內容」：測模型統計對帳的落差情境用
        public int ExtraSolverVariables { get; set; }
        public int ExtraSolverConstraints { get; set; }
        public int SolverSpecialElements { get; set; }
        public ObjectiveSense? SolverObjectiveOverride { get; set; }

        protected override ModelCounts ReadSolverModelCounts()
        {
            int binary = _extractedVars.Count(v => _varTypes[v] == VarType.Binary);
            int integer = _extractedVars.Count(v => _varTypes[v] == VarType.Integer);
            return new ModelCounts
            {
                Variables = _extractedVars.Count + ExtraSolverVariables,
                Binary = binary,
                Integer = integer,
                Continuous = _extractedVars.Count - binary - integer + ExtraSolverVariables,
                Constraints = BuiltConstraints.Count + ExtraSolverConstraints,
                SpecialElements = SolverSpecialElements,
                SpecialDetail = $"SOS={SolverSpecialElements}",
                Objective = SolverObjectiveOverride ?? ObjectiveSenseResult,
            };
        }

        // 繞過 pool 直接呼叫 primitive：進了索引與 solver，卻沒有建模記帳
        public void AddUnledgeredConstraint(string name, string varName)
            => AddConstraint(name, LinearExpr(new[] { (1.0, varName) }), ConstraintSense.LessEqual, 1);

        public double? ObjectiveConstantResult { get; private set; }

        public readonly List<(string Name, List<(string Var, double Value)> Entries)> MipStarts = new();

        protected override void AddMIPStartCore(IReadOnlyList<(string var, double value)> entries, string name)
            => MipStarts.Add((name, entries.ToList()));

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
        public string AddUnregisteredVar(string name) => AddVariable(name, 0, OptBounds.Infinity, VarType.Continuous);

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
