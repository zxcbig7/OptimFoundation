# OptimFoundation

A .NET framework for building mixed-integer programming (MIP) optimization models with a clean, solver-agnostic API.

## Packages

| Package | Description |
|---------|-------------|
| `OptimFoundation.Core` | Abstract base classes, engine contract, variable builder |
| `OptimFoundation.Cplex` | IBM CPLEX solver implementation |
| `OptimFoundation.Gurobi` | Gurobi solver implementation |
| `OptimFoundation.Db` | Database utilities |
| `OptimFoundation.Db.Oracle` | Oracle connector |

## Quick Example

```csharp
// 1. Define a variable class (properties only, no constructor)
public class VariableB_Assign : VariableBase
{
    public DateTime Date     { get; set; }
    public string   Employee { get; set; }
    public string   Shift    { get; set; }
}

// 2. Build variables
optEngine.BuildBVs<VariableB_Assign>(dates, employees, shifts);

// 3. Write a constraint
employees.ForEach(e =>
{
    shifts.ForEach(s =>
        engine.AddLHS(1, new VariableB_Assign { Date = d, Employee = e, Shift = s }));
    engine.AddRHS(1);
    engine.CreateEqual($"OneShift@{d:yyyy_MM_dd}@{e}");
});

// 4. Solve
bool solved = optEngine.Solve();
```

## Getting Started

See [specs/developer-guide.md](specs/developer-guide.md) for the full development guide.

## Requirements

- .NET 8
- IBM CPLEX or Gurobi license (depending on solver used)
