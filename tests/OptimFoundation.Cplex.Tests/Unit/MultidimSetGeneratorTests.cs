using System;
using System.Linq;
using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using OptimFoundation.Modeling;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    [OptSet<string, string>("NodeFrom", "NodeTo")]
    public partial class Set_Arc
    {
    }

    public class MultidimSetGeneratorTests
    {
        [Fact]
        public void OptSet_ArityTwo_GeneratesNamedTupleSetBase()
        {
            Assert.Equal(
                typeof(SetBase<(string NodeFrom, string NodeTo)>),
                typeof(Set_Arc).BaseType);
        }

        [Fact]
        public void GetVarNames_TupleSet_PreservesSparseRows()
        {
            var arcs = new[] { ("N1", "N2"), ("N2", "N4") };

            var names = VariableBuilder.GetVarNames<VariableX_ArcFlow>([arcs]).ToArray();

            Assert.Equal(new[]
            {
                "VariableX_ArcFlow@N1@N2",
                "VariableX_ArcFlow@N2@N4"
            }, names);
        }

        [Fact]
        public void GetVarNames_TupleSetWithDate_CreatesCrossProductByRows()
        {
            var arcs = new[] { ("N1", "N2"), ("N2", "N4") };
            var dates = new[] { new DateTime(2026, 1, 1), new DateTime(2026, 1, 2) };

            var names = VariableBuilder.GetVarNames<VariableX_ArcFlowByDate>([arcs, dates]).ToArray();

            Assert.Equal(4, names.Length);
            Assert.Equal("VariableX_ArcFlowByDate@N1@N2@2026-01-01", names[0]);
            Assert.Equal("VariableX_ArcFlowByDate@N2@N4@2026-01-02", names[3]);
        }

        [Fact]
        public void GetVarNames_TupleArityMismatch_FailsBeforeVariableCreation()
        {
            var arcs = new[] { ("N1", "N2") };

            var ex = Assert.Throws<ArgumentException>(() =>
                VariableBuilder.GetVarNames<VariableX_ArcFlowWrongArity>([arcs]).ToArray());

            Assert.Contains("arity", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
