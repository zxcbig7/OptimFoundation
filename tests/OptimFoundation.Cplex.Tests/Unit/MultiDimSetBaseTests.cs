using System;
using System.Collections.Generic;
using System.Linq;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    public class MultiDimSetBaseTests
    {
        private sealed class Set_Arc : SetBase<(string NodeFrom, string NodeTo)> { }
        private sealed class Set_NumberedArc : SetBase<(string NodeFrom, int Sequence)> { }
        private sealed class Set_Single : SetBase<string> { }

        [Fact]
        public void Load_Rows_BuildsTupleMembersInSourceOrder()
        {
            var source = new RowsDataSource(new[]
            {
                new[] { "N1", "N2" },
                new[] { "N2", "N1" },
            });
            var arcs = new Set_Arc();

            arcs.Load(source, "ArcRows");

            Assert.Equal(new[] { ("N1", "N2"), ("N2", "N1") }, arcs.ToList());
            Assert.True(arcs.Contains(("N1", "N2")));
            Assert.True(arcs.Contains(("N2", "N1")));
        }

        [Fact]
        public void Load_Rows_ConvertsEachTupleComponent()
        {
            var source = new RowsDataSource(new[] { new[] { "N1", "42" } });
            var arcs = new Set_NumberedArc();

            arcs.Load(source, "ArcRows");

            Assert.Equal(("N1", 42), arcs.Single());
        }

        [Fact]
        public void Load_Rows_TrimsEveryTupleComponentAndSkipsBlankRows()
        {
            var source = new RowsDataSource(new[]
            {
                new[] { "  N1 ", " N2  " },
                new[] { "  ", "  " },
                new[] { "N2", "N3" },
            });
            var arcs = new Set_Arc();

            arcs.Load(source, "ArcRows");

            Assert.Equal(new[] { ("N1", "N2"), ("N2", "N3") }, arcs.ToList());
        }

        [Fact]
        public void Arity_ReportsTheNumberOfTupleComponents()
        {
            Assert.Equal(2, ((ISetBrick)new Set_Arc()).Arity);
            Assert.Equal(1, ((ISetBrick)new Set_Single()).Arity);
        }

        [Fact]
        public void Load_Rows_WithWrongColumnCount_ThrowsWithExpectedAndActualArity()
        {
            var source = new RowsDataSource(new[] { new[] { "N1", "N2", "unexpected" } });
            var arcs = new Set_Arc();

            var ex = Assert.Throws<FormatException>(() => arcs.Load(source, "ArcRows"));

            Assert.Contains("ArcRows", ex.Message);
            Assert.Contains("2", ex.Message);
            Assert.Contains("3", ex.Message);
        }

        [Fact]
        public void LoadFrom_TupleMembers_ConsidersOnlyTheWholeTupleForDuplicates()
        {
            var arcs = new Set_Arc();
            arcs.LoadFrom(new[] { ("N1", "N2"), ("N2", "N1") });

            Assert.Equal(2, arcs.Count);

            var duplicate = new Set_Arc();
            Assert.Throws<ArgumentException>(() =>
                duplicate.LoadFrom(new[] { ("N1", "N2"), ("N1", "N2") }));
        }

        [Fact]
        public void LoadFrom_Failure_DoesNotLeaveTheSetPartiallyLoaded()
        {
            var arcs = new Set_Arc();

            Assert.Throws<ArgumentException>(() =>
                arcs.LoadFrom(new[] { ("N1", "N2"), ("N1", "N2") }));

            arcs.LoadFrom(new[] { ("N2", "N3") });
            Assert.Equal(new[] { ("N2", "N3") }, arcs.ToList());
        }

        private sealed class RowsDataSource : IDataSource
        {
            private readonly IEnumerable<string[]> _rows;

            public RowsDataSource(IEnumerable<string[]> rows) => _rows = rows;

            // The multidimensional loading path must use LoadRows rather than legacy LoadSet.
            public IEnumerable<string[]> LoadRows(string name) => _rows;

            public List<string> LoadSet(string name) => throw new InvalidOperationException("Legacy LoadSet must not be used.");

            public List<TParamClass> LoadParam<TParamClass>(string file = null)
                where TParamClass : ModelElementBase, new()
                => throw new NotSupportedException();
        }
    }
}
