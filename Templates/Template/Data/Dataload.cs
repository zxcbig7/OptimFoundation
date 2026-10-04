using System.Globalization;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;

namespace Template;

public sealed partial class Dataload : DataContext
{
    public List<Set_StringKey> set_StringKey = new();
    public List<Set_DateKey> set_DateKey = new();
    public List<Set_SparsePair> set_SparsePair = new();
    public List<Parameter_Scalar> parameter_Scalar = new();
    public List<Parameter_OneDim> parameter_OneDim = new();
    public List<Parameter_TwoDim> parameter_TwoDim = new();

    public Dataload() : this(new CsvDataSource()) { }

    public Dataload(IDataSource source)
    {
        set_StringKey = source.Load<Set_StringKey>("Set_StringKey");
        set_DateKey = source.Load<Set_DateKey>("Set_DateKey");
        set_SparsePair = source.Load<Set_SparsePair>("Set_SparsePair");
        parameter_Scalar = source.Load<Parameter_Scalar>("Parameter_Scalar");
        parameter_OneDim = source.Load<Parameter_OneDim>("Parameter_OneDim");
        parameter_TwoDim = source.Load<Parameter_TwoDim>("Parameter_TwoDim");
    }

    public Dataload(string rawFile)
    {
        var raw = new CsvDataSource().LoadData(rawFile);
        if (raw.Rows.Count == 0)
            throw new InvalidDataException("Template 原始檔案至少需要一筆資料列");

        static double Number(object value, string column) =>
            double.TryParse(
                value?.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double result)
                ? result
                : throw new InvalidDataException($"原始資料的欄 {column} 不是有效數字：'{value}'");

        static DateTime Date(object value) =>
            DateTime.TryParseExact(
                value?.ToString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime result)
                ? result
                : throw new InvalidDataException($"原始資料的欄 Date 不是 yyyy-MM-dd：'{value}'");

        double penalty = Number(raw.Rows[0]["Penalty"], "Penalty");

        foreach (System.Data.DataRow row in raw.Rows)
        {
            string key = row["Key"]?.ToString()?.Trim() ?? string.Empty;
            if (key.Length == 0)
                throw new InvalidDataException("原始資料的欄 Key 不得為空白");

            DateTime date = Date(row["Date"]);
            double cost = Number(row["Cost"], "Cost");
            if (Number(row["Penalty"], "Penalty") != penalty)
                throw new InvalidDataException("原始資料每筆資料列的欄 Penalty 必須一致");

            if (!set_StringKey.Any(item => item.Key == key))
            {
                set_StringKey.Add(new Set_StringKey { Key = key });
                parameter_OneDim.Add(new Parameter_OneDim { Key = key, QTY = cost });
            }
            else if (parameter_OneDim.Single(item => item.Key == key).QTY != cost)
            {
                throw new InvalidDataException($"原始資料 Key {key} 每筆資料列的欄 Cost 必須一致");
            }

            if (!set_DateKey.Any(item => item.Date == date))
                set_DateKey.Add(new Set_DateKey { Date = date });

            if (Number(row["Allowed"], "Allowed") == 1.0)
                set_SparsePair.Add(new Set_SparsePair { Key = key, Date = date });

            parameter_TwoDim.Add(new Parameter_TwoDim
            {
                Key = key,
                Date = date,
                QTY = Number(row["Requirement"], "Requirement"),
            });
        }

        parameter_Scalar.Add(new Parameter_Scalar { QTY = penalty });
    }

    public void Export()
    {
        CsvCtrl.WriteRows(set_StringKey, "Set_StringKey");
        CsvCtrl.WriteRows(set_DateKey, "Set_DateKey");
        CsvCtrl.WriteRows(set_SparsePair, "Set_SparsePair");
        CsvCtrl.WriteRows(parameter_Scalar, "Parameter_Scalar");
        CsvCtrl.WriteRows(parameter_OneDim, "Parameter_OneDim");
        CsvCtrl.WriteRows(parameter_TwoDim, "Parameter_TwoDim");
    }

    public InMemoryDataSource CreateScaledSource(double factor) =>
        new InMemoryDataSource()
            .AddRows(set_StringKey)
            .AddRows(set_DateKey)
            .AddRows(set_SparsePair)
            .AddRows(parameter_Scalar)
            .AddRows(parameter_OneDim)
            .AddRows(parameter_TwoDim.Select(row => new Parameter_TwoDim
            {
                Key = row.Key,
                Date = row.Date,
                QTY = row.QTY * factor,
            }));
}
