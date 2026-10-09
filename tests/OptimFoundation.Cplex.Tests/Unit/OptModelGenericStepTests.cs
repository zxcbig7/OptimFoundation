using System.Reflection;
using System.Runtime.Serialization;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    /// <summary>檢查 AddObjective&lt;T&gt; / AddConstraints&lt;T&gt;：組裝時建立物件、套用時呼叫 Build，引數不符當場丟例外。</summary>
    [Collection("Logging")]
    public class OptModelGenericStepTests
    {
        public sealed class RecordingStep
        {
            private readonly List<string> log;
            private readonly string label;

            public RecordingStep(List<string> log, string label)
            {
                this.log = log;
                this.label = label;
                log.Add("new " + label);
            }

            public void Build(OptEngine engine) => log.Add("build " + label);
        }

        public sealed class WidenedStep
        {
            public WidenedStep(List<string> log, double value) => log.Add("value " + value);

            public void Build(OptEngine engine) { }
        }

        public sealed class NoBuildStep
        {
            public NoBuildStep() { }
        }

        public sealed class ThrowingStep
        {
            public ThrowingStep(string reason) => throw new InvalidOperationException(reason);

            public void Build(OptEngine engine) { }
        }

        private static void ApplyTo(OptModel model)
        {
#pragma warning disable SYSLIB0050
            var engine = (OptEngine)FormatterServices.GetUninitializedObject(typeof(OptEngine));
#pragma warning restore SYSLIB0050
            typeof(OptModel).GetMethod("ApplyTo", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, new object[] { engine });
        }

        [Fact(DisplayName = "組裝時建立物件，套用時依 objective → constraints 呼叫 Build；套用兩次共用同一物件")]
        public void GenericSteps_CreateOnceAtDefinition_BuildOnEachApply()
        {
            var log = new List<string>();
            var model = new OptModel("generic")
                .AddConstraints<RecordingStep>(log, "c1")
                .AddObjective<RecordingStep>(log, "obj");

            Assert.Equal(new[] { "new c1", "new obj" }, log);

            ApplyTo(model);
            ApplyTo(model);

            Assert.Equal(new[] { "new c1", "new obj", "build obj", "build c1", "build obj", "build c1" }, log);
        }

        [Fact(DisplayName = "數值引數照建構子參數型別放寬（int → double）")]
        public void GenericSteps_WidenPrimitiveArguments()
        {
            var log = new List<string>();
            new OptModel("widen").AddConstraints<WidenedStep>(log, 3);

            Assert.Equal(new[] { "value 3" }, log);
        }

        [Fact(DisplayName = "建構子引數不符：組裝那一行就丟 ArgumentException，訊息列出傳入型別與可用建構子")]
        public void GenericSteps_ArgumentMismatch_ThrowsAtDefinition()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                new OptModel("mismatch").AddConstraints<RecordingStep>("c1", new List<string>()));

            Assert.Contains("RecordingStep", ex.Message);
            Assert.Contains("傳入=(String, List<String>)", ex.Message);
            Assert.Contains("(List<String> log, String label)", ex.Message);
        }

        [Fact(DisplayName = "沒有 public void Build(OptEngine)：組裝那一行就丟 MissingMethodException")]
        public void GenericSteps_MissingBuild_Throws()
        {
            Assert.Throws<MissingMethodException>(() => new OptModel("nobuild").AddObjective<NoBuildStep>());
        }

        [Fact(DisplayName = "建構子本身丟例外：原樣拋出，不包成 TargetInvocationException")]
        public void GenericSteps_ConstructorException_IsUnwrapped()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                new OptModel("throwing").AddConstraints<ThrowingStep>("boom"));

            Assert.Equal("boom", ex.Message);
        }
    }
}
