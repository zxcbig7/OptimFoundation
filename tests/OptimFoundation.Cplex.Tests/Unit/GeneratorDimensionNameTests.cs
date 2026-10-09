using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OptimFoundation.Core;
using OptimFoundation.Generators;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    /// <summary>
    /// 維度名稱不合法時，generator 回報 OPTF009 並指向使用者寫的 [OptDim]；產生的檔案不再出現 CS0102 / CS0100。
    /// </summary>
    public class GeneratorDimensionNameTests
    {
        private const string UserFile = "User.cs";

        private static (ImmutableArray<Diagnostic> Generator, Diagnostic[] CompileErrors) Run(string body)
        {
            string source = "using OptimFoundation.Modeling;\nnamespace GenTest\n{\n" + body + "\n}\n";
            var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Append(typeof(SetRowBase).Assembly.Location)
                .Distinct()
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "GenTest",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions, UserFile) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new AutoSetsGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

            return (generatorDiagnostics,
                output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray());
        }

        [Theory]
        [InlineData("[OptSet][OptDim<string>(\"Node\")][OptDim<string>(\"Node\")] public partial class Set_Arc { }", "Node", "維度名稱重複")]
        [InlineData("[OptParam][OptDim<string>(\"QTY\")] public partial class Parameter_Demand { }", "QTY", "QTY 由 generator 自動產生")]
        [InlineData("[OptVar][OptDim<string>(\"1st\")] public partial class VariableB_Pick { }", "1st", "不是合法的 C# 識別字")]
        [InlineData("[OptVar][OptDim<string>(\"class\")] public partial class VariableB_Pick { }", "class", "不是合法的 C# 識別字")]
        [InlineData("[OptSet][OptDim<string>(\"\")] public partial class Set_Item { }", "", "維度名稱為空")]
        [InlineData("[OptSet][OptDim<string>(\"Set_Item\")] public partial class Set_Item { }", "Set_Item", "不能與類別名相同")]
        [InlineData("[OptSet][OptDim<string>(\"Code\")] public partial class Set_Item { public string Code => \"x\"; }", "Code", "手寫的成員同名")]
        public void InvalidDimensionName_ReportsOptf009AtUserAttribute(string body, string name, string reason)
        {
            var (generator, compileErrors) = Run(body);

            var diagnostic = Assert.Single(generator, d => d.Id == "OPTF009");
            Assert.Equal(UserFile, diagnostic.Location.SourceTree?.FilePath);
            Assert.Contains($"'{name}'", diagnostic.GetMessage());
            Assert.Contains(reason, diagnostic.GetMessage());
            Assert.Empty(compileErrors);
        }

        [Fact]
        public void ValidDimensionNames_NoDiagnostics()
        {
            var (generator, compileErrors) = Run(
                "[OptSet][OptDim<string>(\"From\")][OptDim<string>(\"To\")] public partial class Set_Arc { }\n" +
                "[OptParam][OptDim<string>(\"From\")][OptDim<string>(\"To\")] public partial class Parameter_ArcCost { }");

            Assert.Empty(generator);
            Assert.Empty(compileErrors);
        }
    }
}
