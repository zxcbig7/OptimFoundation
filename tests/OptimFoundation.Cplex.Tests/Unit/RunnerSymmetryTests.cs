using System.Reflection;
using System.Runtime.Serialization;
using OptimFoundation.Core;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
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

            Assert.NotNull(typeof(OptProject).GetMethod("Solve", BindingFlags.Public | BindingFlags.Instance));
            Assert.Null(typeof(OptExperiment).GetMethod("Solve", BindingFlags.Public | BindingFlags.Instance));
        }

        [Fact]
        public void ProjectIsTheOnlyEntryPoint_NoExtraRunnerTypes()
        {
            Assert.Empty(typeof(OptExperiment).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            Assert.Null(typeof(OptProject).Assembly.GetType("OptimFoundation.Cplex.OptSolve"));
            Assert.Null(typeof(OptProject).Assembly.GetType("OptimFoundation.Cplex.OptRun"));
            Assert.Null(typeof(ProjectConfig).Assembly.GetType("OptimFoundation.Core.OutputOptions"));
        }

        [Fact]
        public void ConfigConsumers_AreAllNamedLoadConfig()
        {
            Assert.NotNull(typeof(OptProject).GetMethod("LoadConfig", new[] { typeof(ProjectConfig) }));
            Assert.NotNull(typeof(OptExperiment).GetMethod("LoadConfig", new[] { typeof(ProjectConfig) }));
            Assert.NotNull(typeof(OptEngine).GetMethod("LoadConfig", new[] { typeof(ISolverConfig) }));

            foreach (var type in new[] { typeof(OptProject), typeof(OptExperiment), typeof(OptEngine) })
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
            Assert.Contains("模型建構階段不得修改資料", error.Message);

            // 載入後的保護只限制框架提供的修改方法；直接公開的欄位仍可賦值。
            data.PublicValue = 7;
            Assert.Equal(7, data.PublicValue);
        }

        [Fact]
        public void OptExperiment_Defaults_QuietOutputAndTrajectoryOn()
        {
            var experiment = new OptProject("defaults-" + Guid.NewGuid().ToString("N"), retentionDays: 0)
                .Experiment("exp", "test");
            var projectConfig = (ProjectConfig)typeof(OptExperiment)
                .GetField("_projectConfig", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(experiment)!;
            FieldInfo trajectory = typeof(OptExperiment)
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
