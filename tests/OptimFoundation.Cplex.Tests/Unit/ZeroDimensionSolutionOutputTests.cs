using System.IO;
using System.Linq;
using OptimFoundation.Cplex.Tests.Mocks;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Db.Oracle;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // 零維變數沒有 set 欄：CSV 表頭與 INSERT 欄位清單都不能在固定欄之間留下空欄。
    public class ZeroDimensionSolutionOutputTests
    {
        [Fact]
        public void CsvWriteSolution_ZeroDimVariable_HasNoEmptyColumn()
        {
            var engine = new MockEngine();
            engine.BuildVars<VariableC_ZeroDim>();

            CsvCtrl.WriteSolution<VariableC_ZeroDim>(engine, "", "");

            var lines = File.ReadAllLines(FolderDir.Output.GetPathFile("VariableC_ZeroDim.csv"));
            Assert.Equal("VAR_TYPE,QTY", lines[0]);
            Assert.Equal("VariableC_ZeroDim,0", lines[1]);
        }

        [Fact]
        public void CsvWriteSolution_TwoDimVariable_KeepsSetColumns()
        {
            var engine = new MockEngine();
            engine.BuildBVs<VarDG>(new[] { new System.DateTime(2026, 1, 1) }, new[] { "G1" });

            CsvCtrl.WriteSolution<VarDG>(engine, "", "");

            var lines = File.ReadAllLines(FolderDir.Output.GetPathFile("VarDG.csv"));
            Assert.Equal("VAR_TYPE,D,G,QTY", lines[0]);
            Assert.Equal("VarDG,2026_01_01,G1,0", lines[1]);
        }

        [Fact]
        public void OracleSolutionSink_ZeroDimVariable_InsertHasNoEmptyColumn()
        {
            var fake = new FakeDbCtrl();
            var sink = new OracleSolutionSink(fake, "T_SOLUTION");
            var engine = new MockEngine();
            engine.BuildVars<VariableC_ZeroDim>();

            using (var batch = sink.BeginBatch("D1", "U1"))
            {
                batch.Write<VariableC_ZeroDim>(engine);
                batch.Commit();
            }

            var (sql, rows) = fake.ExecutedBatches.Single();
            Assert.Equal(
                "INSERT INTO T_SOLUTION (DATA_ID, VAR_TYPE, QTY, USER_ID) VALUES (:DATA_ID, :VAR_TYPE, :QTY, :USER_ID)",
                sql);
            Assert.Equal(new[] { ":DATA_ID", ":VAR_TYPE", ":QTY", ":USER_ID" }, rows.Single().Select(p => p.name));
        }

        [Fact]
        public void ClassInfo_InsertCommands_OneDimKeepsColumnOrder()
        {
            var classInfo = new ClassInfo(typeof(VarS));

            Assert.Equal(
                "INSERT INTO T (DATA_ID, VAR_TYPE, S, QTY, USER_ID) VALUES (:DATA_ID, :VAR_TYPE, :S, :QTY, :USER_ID)",
                classInfo.VarInsertCmd("T"));
            Assert.Equal(
                "INSERT INTO T (DATA_ID, S) VALUES (:DATA_ID, :S)",
                classInfo.ParamInsertCmd("T"));
        }
    }
}
