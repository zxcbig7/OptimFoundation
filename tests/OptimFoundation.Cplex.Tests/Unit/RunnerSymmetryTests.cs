using System.Reflection;
using System.Runtime.Serialization;
using OptimFoundation.Core;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // 會建立 OptProject（切換全域 log 檔），與其他讀 log 的測試同一個 collection，避免平行執行時互相蓋掉 log。
    [Collection("Logging")]
    public class RunnerSymmetryTests
    {
        [Fact]
        public void OptModel_ApplyTo_AlwaysUsesVariablesObjectiveConstraintsOrder()
        {
            var observed = new List<string>();
            var model = new OptModel("ordered")
                .AddConstraints(_ => observed.Add("constraints-1"))
                .AddVariables(_ => observed.Add("variables-1"))
                .AddObjective(_ => observed.Add("objective-1"))
                .AddVariables(_ => observed.Add("variables-2"))
                .AddConstraints(_ => observed.Add("constraints-2"));

#pragma warning disable SYSLIB0050
            var engine = (OptEngine)FormatterServices.GetUninitializedObject(typeof(OptEngine));
#pragma warning restore SYSLIB0050
            MethodInfo applyTo = typeof(OptModel).GetMethod("ApplyTo", BindingFlags.Instance | BindingFlags.NonPublic)!;
            applyTo.Invoke(model, new object[] { engine });

            Assert.Equal(
                new[] { "variables-1", "variables-2", "objective-1", "constraints-1", "constraints-2" },
                observed);
        }

        [Fact]
        public void OptModel_IsDefinitionOnly_AndOnlyProjectSolves()
        {
            string[] forbidden = { "Execute", "Solve", "LoadConfig", "Dispose" };
            foreach (string method in forbidden)
                Assert.Null(typeof(OptModel).GetMethod(method, BindingFlags.Public | BindingFlags.Instance));

            // 正式環境與實驗各自一個型別，共用 OptExecution 的動詞；舊入口 Solve 已改名 Production
            Assert.Null(typeof(OptProject).GetMethod("Solve", BindingFlags.Public | BindingFlags.Instance));
            Assert.Equal(typeof(OptProduction), typeof(OptProject).GetMethod("Production", BindingFlags.Public | BindingFlags.Instance)!.ReturnType);
            Assert.Equal(typeof(OptExperiment), typeof(OptProject).GetMethod("Experiment", BindingFlags.Public | BindingFlags.Instance)!.ReturnType);
            Assert.Equal(typeof(OptExecution), typeof(OptProduction).BaseType);
            Assert.Equal(typeof(OptExecution), typeof(OptExperiment).BaseType);
            Assert.Null(typeof(OptExecution).GetMethod("Solve", BindingFlags.Public | BindingFlags.Instance));
        }

        [Fact]
        public void Production_And_Experiment_ShareTheSameVerbs()
        {
            // 單組與多組寫法一致：公開動詞全部定義在 OptExecution，子類別不另加
            foreach (var type in new[] { typeof(OptProduction), typeof(OptExperiment) })
                Assert.Empty(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
            foreach (string verb in new[] { "AddProjectConfig", "AddModel", "AddSolverConfig", "AddTrial", "BeforeSolve", "OnSolved", "CaptureTrajectory", "Run" })
                Assert.NotNull(typeof(OptExecution).GetMethod(verb, BindingFlags.Public | BindingFlags.Instance));
        }

        [Fact]
        public void ProjectIsTheOnlyEntryPoint_NoExtraRunnerTypes()
        {
            foreach (var type in new[] { typeof(OptExecution), typeof(OptProduction), typeof(OptExperiment) })
                Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            // 執行層不認得專案：OptExecution 沒有任何 OptProject 型別的欄位
            Assert.DoesNotContain(typeof(OptExecution).GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
                f => f.FieldType == typeof(OptProject));
            Assert.Null(typeof(OptProject).Assembly.GetType("OptimFoundation.Cplex.OptSolve"));
            Assert.Null(typeof(OptProject).Assembly.GetType("OptimFoundation.Cplex.OptRun"));
            Assert.Null(typeof(ProjectConfig).Assembly.GetType("OptimFoundation.Core.OutputOptions"));
        }

        [Fact]
        public void ConfigConsumers_NameWhichConfigTheyTake()
        {
            // builder 用 AddProjectConfig / AddSolverConfig 寫明是哪一種設定；OptProject 不再有第二個入口
            Assert.Null(typeof(OptProject).GetMethod("LoadConfig", new[] { typeof(ProjectConfig) }));
            Assert.NotNull(typeof(OptExecution).GetMethod("AddProjectConfig", new[] { typeof(ProjectConfig) }));
            Assert.NotNull(typeof(OptExecution).GetMethod("AddSolverConfig", new[] { typeof(string), typeof(CplexConfig) }));
            Assert.Null(typeof(OptExecution).GetMethod("LoadConfig"));
            Assert.Null(typeof(OptExecution).GetMethod("AddConfig"));
            // engine 層吃 solver 設定仍是 LoadConfig
            Assert.NotNull(typeof(OptEngine).GetMethod("LoadConfig", new[] { typeof(ISolverConfig) }));

            foreach (var type in new[] { typeof(OptProject), typeof(OptExecution), typeof(OptEngine) })
            {
                Assert.Null(type.GetMethod("UseOutput"));
                Assert.Null(type.GetMethod("UseConfig"));
                Assert.Null(type.GetMethod("Configuration"));
            }
        }

        [Fact]
        public void ConfigClones_CopyEveryPublicFieldAndProperty()
        {
            var cplex = new CplexConfig
            {
                TimeLimit = 12.5,
                MipGap = 0.025,
                Threads = 3,
                Seed = 41,
                HeuristicEffort = 0.7,
            };
            var projectConfig = new ProjectConfig
            {
                EnableSolverLog = false,
                ExportLP = true,
                ExportMPS = true,
                ExportSol = true,
                ExportIIS = true,
            };

            AssertAllPublicMembersEqual(cplex, cplex.Clone());
            AssertAllPublicMembersEqual(projectConfig, projectConfig.Clone());
        }

        [Fact]
        public void OptDataLoad_FreezesFrameworkControlledMutationApi()
        {
            var data = OptData.Load(() => new GuardedData());

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => data.FrameworkMutation("Items"));
            Assert.Contains("Items", error.Message);
            Assert.Contains("建立模型階段不得修改資料", error.Message);

            // 載入後的保護只限制框架提供的修改方法；直接公開的欄位仍可賦值。
            data.PublicValue = 7;
            Assert.Equal(7, data.PublicValue);
        }

        [Fact]
        public void OptExperiment_Defaults_QuietOutputAndTrajectoryOn()
        {
            var experiment = new OptProject("defaults-" + Guid.NewGuid().ToString("N"), retentionDays: 0)
                .Experiment("exp", "test");
            var projectConfig = (ProjectConfig)typeof(OptExecution)
                .GetField("_projectConfig", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(experiment)!;
            FieldInfo trajectory = typeof(OptExecution)
                .GetField("_captureTrajectory", BindingFlags.Instance | BindingFlags.NonPublic)!;

            Assert.False(projectConfig.EnableSolverLog);
            Assert.False(projectConfig.ExportLP);
            Assert.False(projectConfig.ExportMPS);
            Assert.False(projectConfig.ExportSol);
            Assert.False(projectConfig.ExportIIS);
            Assert.True((bool)trajectory.GetValue(experiment)!);

            experiment.CaptureTrajectory(false);
            Assert.False((bool)trajectory.GetValue(experiment)!);
        }

        [Fact]
        public void OptProduction_Defaults_SolverLogOnAndTrajectoryOff()
        {
            var production = new OptProject("defaults-" + Guid.NewGuid().ToString("N"), retentionDays: 0)
                .Production();
            var projectConfig = (ProjectConfig)typeof(OptExecution)
                .GetField("_projectConfig", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(production)!;
            FieldInfo trajectory = typeof(OptExecution)
                .GetField("_captureTrajectory", BindingFlags.Instance | BindingFlags.NonPublic)!;

            Assert.True(projectConfig.EnableSolverLog);
            Assert.False(projectConfig.ExportLP || projectConfig.ExportMPS || projectConfig.ExportSol || projectConfig.ExportIIS);
            Assert.False((bool)trajectory.GetValue(production)!);
            Assert.Equal(OptProject.ProductionExperimentName, production.Name);
        }

        [Fact]
        public void ProjectConfig_DefaultIsSolveBehavior_AndQuietSilencesEverything()
        {
            var solve = new ProjectConfig();
            Assert.True(solve.EnableSolverLog);
            Assert.False(solve.ExportLP || solve.ExportMPS || solve.ExportSol || solve.ExportIIS);

            var quiet = ProjectConfig.Quiet();
            Assert.False(quiet.EnableSolverLog);
            Assert.False(quiet.ExportLP || quiet.ExportMPS || quiet.ExportSol || quiet.ExportIIS);
        }

        private static void AssertAllPublicMembersEqual<T>(T original, T clone) where T : class
        {
            Assert.NotSame(original, clone);
            Type type = typeof(T);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                Assert.Equal(field.GetValue(original), field.GetValue(clone));
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                Assert.Equal(property.GetValue(original), property.GetValue(clone));
            }
        }

        private sealed class GuardedData : DataContext
        {
            public int PublicValue;
            public void FrameworkMutation(string member) => GuardMutation(member);
        }
    }
}
