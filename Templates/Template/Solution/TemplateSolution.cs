using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace Template;

public sealed class TemplateSolution
{
    private const double Tolerance = 1e-6;

    public static void ValidateData(Dataload data)
    {
        if (data.DataIssues.Count > 0)
            throw new InvalidOperationException($"資料不合法：框架資料檢查發現 {data.DataIssues.Count} 個問題");

        Require(data.set_StringKey.Count >= 2, "Set_StringKey 至少要兩個 Key，Constraint_Range 的 [1, |Key| - 1] 才有解");

        foreach (var key in data.set_StringKey)
        {
            Require(
                data.parameter_OneDim.Any(row => row.Key == key.Key),
                $"找不到 Parameter_OneDim：{key.Key}");

            foreach (var date in data.set_DateKey)
            {
                Require(
                    data.parameter_TwoDim.Any(row => row.Key == key.Key && row.Date == date.Date),
                    $"找不到 Parameter_TwoDim：{key.Key} / {date.Date:yyyy-MM-dd}");
            }
        }

        foreach (var pair in data.set_SparsePair)
        {
            Require(
                data.set_StringKey.Any(key => key.Key == pair.Key) && data.set_DateKey.Any(date => date.Date == pair.Date),
                $"Set_SparsePair 的 {pair.Key} / {pair.Date:yyyy-MM-dd} 不在 Set_StringKey × Set_DateKey 內");
        }

        Require(data.parameter_OneDim.All(row => row.QTY >= 0.0), "Parameter_OneDim 不得為負數");
        Require(data.parameter_TwoDim.All(row => row.QTY >= 0.0), "Parameter_TwoDim 不得為負數");
        Require(data.parameter_Scalar.Single().QTY > 0.0, "Parameter_Scalar 必須大於零");
    }

    public static IReadOnlyDictionary<string, double> CreateStartValues(Dataload data)
    {
        string cheapest = data.parameter_OneDim.OrderBy(row => row.QTY).First().Key;

        return data.set_StringKey.ToDictionary(
            key => new VariableB_Binary { Key = key.Key }.ToString(),
            key => key.Key == cheapest ? 1.0 : 0.0);
    }

    public static void ReadAndValidate(OptEngine engine, Dataload data, ISolutionSink sink)
    {
        var binary = engine.GetSetVarValues<VariableB_Binary>();
        var integer = engine.GetSetVarValues<VariableI_Integer>();
        var continuous = engine.GetSetVarValues<VariableC_Continuous>();
        var zeroDim = engine.GetSetVarValues<VariableC_ZeroDim>();

        ValidateRules(binary, integer, continuous, zeroDim, data);

        using var batch = sink.BeginBatch("Template", "SYSTEM");
        batch.Write<VariableB_Binary>(engine);
        batch.Write<VariableI_Integer>(engine);
        batch.Write<VariableC_Continuous>(engine);
        batch.Write<VariableC_ZeroDim>(engine);
        batch.Commit();
    }

    private static void ValidateRules(
        Dictionary<string, double> binary,
        Dictionary<string, double> integer,
        Dictionary<string, double> continuous,
        Dictionary<string, double> zeroDim,
        Dataload data)
    {
        double selected = 0.0;

        foreach (var key in data.set_StringKey)
        {
            double binaryValue = ValueOf(binary, new VariableB_Binary { Key = key.Key }.ToString());
            Require(
                Math.Abs(binaryValue - Math.Round(binaryValue)) <= Tolerance &&
                binaryValue >= -Tolerance && binaryValue <= 1.0 + Tolerance,
                $"{key.Key} 的 VariableB_Binary 不是二元值");
            selected += binaryValue;

            foreach (var date in data.set_DateKey)
            {
                bool allowed = data.set_SparsePair.Any(pair => pair.Key == key.Key && pair.Date == date.Date);
                double integerValue = allowed
                    ? ValueOf(integer, new VariableI_Integer { Key = key.Key, Date = date.Date }.ToString())
                    : 0.0;
                double continuousValue = ValueOf(continuous, new VariableC_Continuous { Key = key.Key, Date = date.Date }.ToString());
                double requirement = data.parameter_TwoDim
                    .Single(row => row.Key == key.Key && row.Date == date.Date).QTY;
                string label = $"{key.Key} / {date.Date:yyyy-MM-dd}";

                Require(
                    Math.Abs(integerValue + continuousValue - requirement) <= Tolerance,
                    $"{label} 違反 Constraint_Equal");
                Require(continuousValue >= -Tolerance, $"{label} 的 VariableC_Continuous 為負數");

                if (!allowed) continue;

                Require(
                    integerValue <= requirement * binaryValue + Tolerance,
                    $"{label} 違反 Constraint_LessEqual");
                Require(
                    Math.Abs(integerValue - Math.Round(integerValue)) <= Tolerance &&
                    integerValue >= -Tolerance,
                    $"{label} 的 VariableI_Integer 不是非負整數");
            }
        }

        double zeroDimValue = ValueOf(zeroDim, new VariableC_ZeroDim().ToString());
        foreach (var date in data.set_DateKey)
        {
            double load = data.set_SparsePair
                .Where(pair => pair.Date == date.Date)
                .Sum(pair => ValueOf(integer, new VariableI_Integer { Key = pair.Key, Date = pair.Date }.ToString()));
            Require(zeroDimValue + Tolerance >= load, $"{date.Date:yyyy-MM-dd} 違反 Constraint_GreaterEqual");
        }

        Require(
            selected >= 1.0 - Tolerance && selected <= data.set_StringKey.Count - 1 + Tolerance,
            "違反 Constraint_Range");
    }

    private static double ValueOf(Dictionary<string, double> values, string key) =>
        values.TryGetValue(key, out double value) ? value : 0.0;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"[解驗證失敗] {message}");
    }
}
