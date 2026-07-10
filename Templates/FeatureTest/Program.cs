using OptimFoundation.Core;
using OptimFoundation.Cplex;

Logging.SetLogFileName("FeatureTest");

Sep("Test 1：Optimal MILP  →  BestObjValue / MIPGap / GetCVSolution / GetBVSolution");
Test1_Optimal();

Sep("Test 2：Infeasible  →  GetConflictConstraints");
Test2_Infeasible();

Sep("Test 3：Constraint Name Dedup");
Test3_Dedup();

Sep("All tests done.");

// ── helpers ──────────────────────────────────────────────────────────
static void Sep(string title)
{
    Console.WriteLine();
    Console.WriteLine("══════════════════════════════════════════════════════");
    Console.WriteLine($"  {title}");
    Console.WriteLine("══════════════════════════════════════════════════════");
}

// ─────────────────────────────────────────────────────────────────────
// Test 1：Product-mix MILP（3 products, continuous + binary）
//   Maximize  5xA + 8xB + 6xC
//   s.t.      xA + 2xB + 1.5xC <= 10   (resource)
//             xP <= 100 * yP            (activation linkage)
//   Expected: xA=10, obj=50  (A has best profit/resource = 5)
// ─────────────────────────────────────────────────────────────────────
static void Test1_Optimal()
{
    var products = new List<string> { "A", "B", "C" };
    var profit = new Dictionary<string, double> { ["A"] = 5, ["B"] = 8, ["C"] = 6 };
    var res    = new Dictionary<string, double> { ["A"] = 1, ["B"] = 2, ["C"] = 1.5 };
    const double maxRes = 10, bigM = 100;

    using var engine = new OptEngine(new CplexConfig { enableLog = false });
    engine.Build();

    engine.BuildCVs<VariableX_Amount>(products);
    engine.BuildBVs<VariableY_Active>(products);

    // Objective
    foreach (var p in products) engine.AddLHS(profit[p], new VariableX_Amount(p));
    engine.CreateMaximize();

    // Resource
    foreach (var p in products) engine.AddLHS(res[p], new VariableX_Amount(p));
    engine.CreateLessEqual(maxRes, "Resource");

    // Activation: Amount[p] <= BigM * Active[p]
    foreach (var p in products)
    {
        engine.AddLHS(1.0, new VariableX_Amount(p));
        engine.AddRHS(bigM, new VariableY_Active(p));
        engine.CreateLessEqual($"Activation@{p}");
    }

    engine.Solve();

    Console.WriteLine($"Status    : {engine.Status}");
    Console.WriteLine($"ObjValue  : {engine.GetObjectiveValue():F2}   (expected 50.00)");
    Console.WriteLine($"BestBound : {engine.BestObjValue:F2}");
    Console.WriteLine($"MIPGap    : {engine.MIPGap:P4}");

    Console.WriteLine("\n  GetCVSolution() — non-zero continuous:");
    foreach (var kv in engine.GetCVSolution().Where(kv => kv.Value > 1e-6))
        Console.WriteLine($"    {kv.Key,-30} = {kv.Value:F2}");

    Console.WriteLine("\n  GetBVSolution() — active binary:");
    foreach (var kv in engine.GetBVSolution().Where(kv => kv.Value > 0.5))
        Console.WriteLine($"    {kv.Key,-30} = {kv.Value:F0}");
}

// ─────────────────────────────────────────────────────────────────────
// Test 2：Infeasible model（contradicting sum constraints）
//   Minimize  x1 + x2
//   s.t.      x1 + x2 >= 10   ← LB_Sum
//             x1 + x2 <= 5    ← UB_Sum  (conflicts)
//   Expected: Infeasible, conflict = {LB_Sum, UB_Sum}
// ─────────────────────────────────────────────────────────────────────
static void Test2_Infeasible()
{
    var items = new List<string> { "x1", "x2" };

    using var engine = new OptEngine(new CplexConfig { enableLog = false, exportLP = true });
    engine.Build();

    engine.BuildCVs<VariableX_Simple>(items);

    // Objective
    foreach (var i in items) engine.AddLHS(1.0, new VariableX_Simple(i));
    engine.CreateMinimize();

    // LB_Sum: x1 + x2 >= 10
    foreach (var i in items) engine.AddLHS(1.0, new VariableX_Simple(i));
    engine.CreateGreatEqual(10, "LB_Sum");

    // UB_Sum: x1 + x2 <= 5  ← conflicts with LB_Sum
    foreach (var i in items) engine.AddLHS(1.0, new VariableX_Simple(i));
    engine.CreateLessEqual(5, "UB_Sum");

    engine.Solve();

    Console.WriteLine($"Status  : {engine.Status}   (expected Infeasible)");

    var conflicts = engine.GetConflictConstraints();
    Console.WriteLine($"Conflict constraints ({conflicts.Count}): [{string.Join(", ", conflicts)}]");
    Console.WriteLine($"IIS check : {(conflicts.Count == 2 && conflicts.Contains("LB_Sum") && conflicts.Contains("UB_Sum") ? "PASS" : "FAIL")}");
}

// ─────────────────────────────────────────────────────────────────────
// Test 3：Constraint name dedup
//   Same model as Test 1, but "Resource" is added TWICE with different RHS:
//     1st: Resource <= 10  → gets added
//     2nd: Resource <= 3   → BLOCKED by dedup (same name)
//   Expected: obj=50 (RHS=10 active)   vs   obj=15 if dedup fails (RHS=3 active)
// ─────────────────────────────────────────────────────────────────────
static void Test3_Dedup()
{
    var products = new List<string> { "A", "B", "C" };
    var profit = new Dictionary<string, double> { ["A"] = 5, ["B"] = 8, ["C"] = 6 };
    var res    = new Dictionary<string, double> { ["A"] = 1, ["B"] = 2, ["C"] = 1.5 };
    const double bigM = 100;

    using var engine = new OptEngine(new CplexConfig { enableLog = false });
    engine.Build();

    engine.BuildCVs<VariableX_Amount>(products);
    engine.BuildBVs<VariableY_Active>(products);

    foreach (var p in products) engine.AddLHS(profit[p], new VariableX_Amount(p));
    engine.CreateMaximize();

    // 1st Resource (RHS=10) — should be added
    foreach (var p in products) engine.AddLHS(res[p], new VariableX_Amount(p));
    engine.CreateLessEqual(10, "Resource");

    // 2nd Resource (RHS=3, stricter) — should be BLOCKED
    foreach (var p in products) engine.AddLHS(res[p], new VariableX_Amount(p));
    engine.CreateLessEqual(3, "Resource");

    foreach (var p in products)
    {
        engine.AddLHS(1.0, new VariableX_Amount(p));
        engine.AddRHS(bigM, new VariableY_Active(p));
        engine.CreateLessEqual($"Activation@{p}");
    }

    engine.Solve();

    double obj = engine.GetObjectiveValue();
    Console.WriteLine($"Status   : {engine.Status}");
    Console.WriteLine($"ObjValue : {obj:F2}");
    Console.WriteLine($"Dedup    : {(Math.Abs(obj - 50.0) < 0.1 ? "PASS  (RHS=10 constraint active, as expected)" : "FAIL  (RHS=3 also applied, dedup did not work)")}");
}

// ── Variable / Parameter Classes ─────────────────────────────────────

public class VariableX_Amount : VariableBase
{
    public string PRODUCT { get; set; }
    public VariableX_Amount(params object[] sets) => InitClassBySets(sets);
    public VariableX_Amount() { }
}

public class VariableY_Active : VariableBase
{
    public string PRODUCT { get; set; }
    public VariableY_Active(params object[] sets) => InitClassBySets(sets);
    public VariableY_Active() { }
}

public class VariableX_Simple : VariableBase
{
    public string IDX { get; set; }
    public VariableX_Simple(params object[] sets) => InitClassBySets(sets);
    public VariableX_Simple() { }
}
