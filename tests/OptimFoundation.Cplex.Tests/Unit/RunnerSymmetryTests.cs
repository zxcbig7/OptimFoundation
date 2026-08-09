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
        public void OptModel_IsDefinitionOnly_AndOnSolvedExistsOnlyOnOptProject()
        {
            string[] forbidden = { "Execute", "UseConfig", "OnSolved", "Dispose" };
            foreach (string method in forbidden)
                Assert.Null(typeof(OptModel).GetMethod(method, BindingFlags.Public | BindingFlags.Instance));

            Assert.NotNull(typeof(OptProject).GetMethod("OnSolved", BindingFlags.Public | BindingFlags.Instance));
            Assert.Null(typeof(OptExperiment).GetMethod("OnSolved", BindingFlags.Public | BindingFlags.Instance));
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
            var project = new ProjectConfig
            {
                ProjectName = new string("clone-project".ToCharArray()),
                RetentionDays = 11,
                EnableSolverLog = false,
                ExportLP = true,
                ExportMPS = true,
                ExportSol = true,
                DataId = new string("data".ToCharArray()),
                UserId = new string("user".ToCharArray()),
            };

            AssertAllPublicMembersEqual(cplex, cplex.Clone());
            ProjectConfig projectClone = project.Clone();
            AssertAllPublicMembersEqual(project, projectClone);
            Assert.Same(project.ProjectName, projectClone.ProjectName);
            Assert.Same(project.DataId, projectClone.DataId);
            Assert.Same(project.UserId, projectClone.UserId);
        }

        [Fact]
        public void OptDataLoad_FreezesFrameworkControlledMutationApi()
        {
            var data = OptData.Load(() => new GuardedData());

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => data.FrameworkMutation("Items"));
            Assert.Contains("Items", error.Message);
            Assert.Contains("模型建構階段不得修改資料", error.Message);

            // Approved Packet 1b scope: direct public fields remain mutable.
            data.PublicValue = 7;
            Assert.Equal(7, data.PublicValue);
        }

        [Fact]
        public void OptExperiment_DefaultProjectConfig_DisablesOutputAndHousekeeping()
        {
            var experiment = new OptExperiment("defaults", "test");
            FieldInfo field = typeof(OptExperiment).GetField(
                "_projectConfigFactory",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var factory = (Func<ProjectConfig>)field.GetValue(experiment)!;
            ProjectConfig config = factory();

            Assert.False(config.EnableSolverLog);
            Assert.False(config.ExportLP);
            Assert.False(config.ExportMPS);
            Assert.False(config.ExportSol);
            Assert.Equal(0, config.RetentionDays);
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
