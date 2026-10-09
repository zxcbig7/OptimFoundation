using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex.Tests.Mocks;
using OptimFoundation.Modeling;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // partial class 手寫的輔助成員不影響框架：維度只認 OptDim；Parameter 的可寫 property 仍是資料欄。

    [OptSet]
    [OptDim<string>("From")]
    [OptDim<string>("To")]
    public partial class Set_HelperArc
    {
        public string Label => $"{From}_{To}";
        public string Note { get; set; } = "";
        public bool IsLoop() => From == To;
    }

    [OptVar]
    [OptDim<string>("From")]
    [OptDim<string>("To")]
    public partial class VariableB_HelperUseArc
    {
        public string Label => $"{From}_{To}";
        public string Note { get; set; } = "";
    }

    [OptParam]
    [OptDim<string>("From")]
    [OptDim<string>("To")]
    public partial class Parameter_HelperArcCost
    {
        public double Doubled => QTY * 2;
        public double Toll { get; set; }
    }

    public class OptDimOnlyTests
    {
        private static MockEngine NewEngine()
        {
            var e = new MockEngine();
            e.Build();
            return e;
        }

        private static List<Set_HelperArc> Arcs() => new() { new Set_HelperArc { From = "A", To = "B", Note = "x" } };

        [Fact]
        public void Set_LoadsFromDimensionColumnsOnly()
        {
            IDataSource source = new InMemoryDataSource()
                .AddRows("Set_HelperArc", new[] { new[] { "From", "To" }, new[] { "A", "B" } });

            var row = Assert.Single(source.Load<Set_HelperArc>());

            Assert.Equal("A", row.From);
            Assert.Equal("B", row.To);
            Assert.Equal("A_B", row.Label);
        }

        [Fact]
        public void Set_ToString_UsesDimensionsOnly()
        {
            Assert.Equal("A@B", Arcs()[0].ToString());
        }

        [Fact]
        public void BuildVars_TypedAndString_NamesMatchVariableKey()
        {
            var typed = NewEngine();
            typed.BuildVars<VariableB_HelperUseArc>(Arcs());

            var str = NewEngine();
            str.BuildBVs("VariableB_HelperUseArc", Arcs());

            var key = new VariableB_HelperUseArc { From = "A", To = "B", Note = "x" };
            Assert.Equal("VariableB_HelperUseArc@A@B", key.ToString());
            Assert.Equal(new[] { key.ToString() }, typed.GetAllVarNames());
            Assert.Equal(new[] { key.ToString() }, str.GetAllVarNames());
            Assert.True(str.AddLHS(1.0, key));
        }

        [Fact]
        public void Parameter_LoadsWritableExtraColumn_IgnoresReadOnlyHelper()
        {
            IDataSource source = new InMemoryDataSource()
                .AddRows("Parameter_HelperArcCost", new[] { new[] { "From", "To", "Toll", "QTY" }, new[] { "A", "B", "3", "5" } });

            var row = Assert.Single(source.Load<Parameter_HelperArcCost>());

            Assert.Equal(3.0, row.Toll);
            Assert.Equal(5.0, row.QTY);
            Assert.Equal(10.0, row.Doubled);
            Assert.Equal("Parameter_HelperArcCost@A@B", row.ToString());
        }

        [Fact]
        public void ClassInfo_UsesDataColumns()
        {
            Assert.Equal(new[] { "From", "To" }, new ClassInfo(typeof(VariableB_HelperUseArc)).SetNames);
            Assert.Equal(new[] { "From", "To" }, new ClassInfo(typeof(Set_HelperArc)).SetNames);

            string[] parameterColumns = new ClassInfo(typeof(Parameter_HelperArcCost)).SetNames;
            Assert.Equal(new[] { "From", "To" }, parameterColumns.Take(2));
            Assert.Equal(new[] { "QTY", "Toll" }, parameterColumns.Skip(2).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void CsvWriteRows_Set_WritesDimensionsOnlyAndLoadsBack()
        {
            string fileName = $"helper-arc-{Guid.NewGuid():N}.csv";
            string path = FolderDir.Input.GetPathFile(fileName);
            try
            {
                CsvCtrl.WriteRows(Arcs(), fileName);

                Assert.Equal("FROM,TO", File.ReadLines(path).First());
                IDataSource source = new CsvDataSource();
                var row = Assert.Single(source.Load<Set_HelperArc>(fileName));
                Assert.Equal("A@B", row.ToString());
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
