using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace OptimFoundation.Generators
{
    /// <summary>
    /// AutoSets source generator。編譯期把宣告式標記補成完整 class。兩條並存路徑（後者為 2026-07 新增，前者原封保留為逃生口）：
    ///
    /// A. 字串式（逃生口 / 遷移用）：
    ///     [OptVar("Date:DateTime", "Employee")]     → VariableBase + 依序 set 屬性
    ///     [OptParam("Date:DateTime", "Group")]      → ParameterBase + set 屬性 + QTY + ctor
    ///
    /// B. Set 積木 + 泛型引用（paved path）：
    ///     [OptSet&lt;DateTime&gt;] partial class Set_Date {}   → SetBase&lt;DateTime&gt;
    ///     [OptSet&lt;string&gt;]   partial class Set_Emp {}    → SetBase&lt;string&gt;（元素型別一律顯式寫出）
    ///     [OptSet]               partial class Set_Emp {}    → SetBase&lt;string&gt;（無參數版仍受支援，但非預設寫法）
    ///     [OptVar&lt;Set_Date, Set_Emp&gt;]                     → VariableBase，property 名/型別從積木自動抓
    ///     [OptParam&lt;Set_Date, Set_Grp&gt;]                   → ParameterBase + QTY + ctor
    ///   泛型 attribute 以 where T : ISetBrick 約束，引用非積木 = CS0311 原生 compile error。
    ///
    /// 兩路共同：變數型別由類別名前綴決定（VariableB_=Binary / VariableX_=Continuous / VariableI_=Integer，OPTF001）；
    /// 參數一律含 QTY 值欄位且為最後一個資料屬性；Set 積木類名須 Set_ 前綴（OPTF003）。
    /// attribute / marker 由本 generator 於編譯期注入 OptimFoundation.Modeling namespace，使用端只需 using 該 namespace。
    /// </summary>
    [Generator]
    public sealed class AutoSetsGenerator : IIncrementalGenerator
    {
        private const string VarAttr = "OptimFoundation.Modeling.OptVarAttribute";
        private const string ParamAttr = "OptimFoundation.Modeling.OptParamAttribute";
        private const string SetAttr = "OptimFoundation.Modeling.OptSetAttribute";

        private const string VariableBaseFqn = "global::OptimFoundation.Core.VariableBase";
        private const string ParameterBaseFqn = "global::OptimFoundation.Core.ParameterBase";
        private const string SetBaseFqn = "global::OptimFoundation.Core.SetBase";

        private const int MaxArity = 6;

        // 命名天條：型別由類別名前綴決定，前綴不合法直接 compile error，訊息教正確取名
        private static readonly DiagnosticDescriptor VarNamingRule = new DiagnosticDescriptor(
            id: "OPTF001",
            title: "OptVar 類別命名不符命名天條",
            messageFormat: "類別 '{0}' 掛 [OptVar]，但名稱無法判定變數型別。命名天條：VariableB_<語意>（Binary）/ VariableX_<語意>（Continuous）/ VariableI_<語意>（Integer），例：VariableX_Start",
            category: "OptimFoundation.Naming",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ParamNamingRule = new DiagnosticDescriptor(
            id: "OPTF002",
            title: "OptParam 類別命名不符命名天條",
            messageFormat: "類別 '{0}' 掛 [OptParam]，但名稱不是 Parameter_ 前綴。命名天條：Parameter_<語意>，例：Parameter_ProcessTime",
            category: "OptimFoundation.Naming",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor SetNamingRule = new DiagnosticDescriptor(
            id: "OPTF003",
            title: "OptSet 類別命名不符命名天條",
            messageFormat: "類別 '{0}' 掛 [OptSet]，但名稱不是 Set_ 前綴。命名天條：Set_<語意>，例：Set_Date",
            category: "OptimFoundation.Naming",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor SetElementTypeRule = new DiagnosticDescriptor(
            id: "OPTF004",
            title: "OptSet 元素型別不在合法域",
            messageFormat: "[OptSet<{1}>]（類別 '{0}'）的元素型別不受支援。合法：string / System.DateTime / int / long / double / decimal。",
            category: "OptimFoundation.Naming",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor NotASetBrickRule = new DiagnosticDescriptor(
            id: "OPTF005",
            title: "泛型引用的型別不是 Set 積木",
            messageFormat: "類別 '{0}' 的維度引用了 '{1}'，但它沒有掛 [OptSet]／[OptSet<T>]，不是合法 Set 積木。",
            category: "OptimFoundation.Naming",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // DataContext 欄位漏掛 attribute → 靜默不註冊（永遠不被驗證）。只在 DataContext 子類欄位掃描路徑觸發（見框架資料防護規格）。
        private static readonly DiagnosticDescriptor UnregisteredDataMemberRule = new DiagnosticDescriptor(
            id: "OPTF006",
            title: "DataContext 欄位引用的型別漏掛 attribute，將被靜默排除於資料驗證外",
            messageFormat: "{0}",
            category: "OptimFoundation.DataGuard",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // "VariableB_Assign" → "Binary"；非法前綴回 null（呼叫端報 OPTF001）
        private static string? VarTypeFromPrefix(string className)
        {
            if (className.StartsWith("VariableB_", System.StringComparison.Ordinal)) return "Binary";
            if (className.StartsWith("VariableX_", System.StringComparison.Ordinal)) return "Continuous";
            if (className.StartsWith("VariableI_", System.StringComparison.Ordinal)) return "Integer";
            return null;
        }

        // 注入到使用端編譯的 attribute + marker（使用端不需任何額外組件）。
        // 字串式 OptVar/OptParam 原封保留（逃生口）；新增 OptSet 與泛型 OptVar/OptParam（arity 1..6）。
        private static readonly string AttributeSource = BuildAttributeSource();

        private static string BuildAttributeSource()
        {
            var sb = new StringBuilder();
            sb.Append(@"// <auto-generated/>
#nullable enable
using System;

namespace OptimFoundation.Modeling
{
    // ── 字串式（逃生口 / 遷移用），原封保留 ──

    /// <summary>
    /// 以 set 名稱字串標記變數類別，generator 據此生成各維度 property。
    /// 逃生口用法——set 有對應積木類別時 ALWAYS 改用泛型版 OptVar&lt;TSet1, …&gt;，才享有編譯期檢查。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class OptVarAttribute : Attribute
    {
        /// <summary>各維度的 set 名；順序即生成 property 的順序，也是變數 key 的組成順序。</summary>
        public string[] Sets { get; }

        /// <summary>依序列出各維度的 set 名。</summary>
        public OptVarAttribute(params string[] sets) { Sets = sets; }
    }

    /// <summary>
    /// 以 set 名稱字串標記參數類別（字串式逃生口，同 OptVarAttribute 的取捨）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class OptParamAttribute : Attribute
    {
        /// <summary>各維度的 set 名；順序即生成 property 的順序。</summary>
        public string[] Sets { get; }

        /// <summary>true（預設）會生成 QTY 值欄位；純 key 參數設 false。</summary>
        public bool HasValue { get; set; } = true;

        /// <summary>依序列出各維度的 set 名。</summary>
        public OptParamAttribute(params string[] sets) { Sets = sets; }
    }

    // ── Set 積木：元素型別由泛型參數帶（無參數 = 預設 string） ──

    /// <summary>標記這個類別是一顆 set 積木，成員型別為 string。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class OptSetAttribute : Attribute { }

    /// <summary>標記這個類別是一顆 set 積木，成員型別為 T（支援 string / DateTime / int / long / double / decimal）。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class OptSetAttribute<T> : Attribute { }

    // ── 具名維度：同 set 多維度 / 自訂 index 名。泛型 = 來源 set（直接綁，不 alias），字串 = 維度名 ──

    /// <summary>
    /// 為某個維度取自訂名稱，來源 set 由泛型參數 TSet 指定。
    /// 用於同一顆 set 當多個維度的情形（例：來源站 / 目的站都是 Set_Station），可重複標記多次。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class OptDimAttribute<TSet> : Attribute where TSet : global::OptimFoundation.Core.ISetBrick
    {
        /// <summary>這個維度的名稱，會成為生成的 property 名與 CSV / DB 欄名。</summary>
        public string Name { get; }

        /// <summary>指定維度名稱。</summary>
        public OptDimAttribute(string name) { Name = name; }
    }

");
            // 泛型 OptVar / OptParam：arity 1..MaxArity，where Tn : ISetBrick → 引用非積木 = CS0311
            for (int n = 1; n <= MaxArity; n++)
            {
                string tparams = string.Join(", ", Enumerable.Range(1, n).Select(i => "T" + i));
                string constraints = string.Join(" ", Enumerable.Range(1, n)
                    .Select(i => $"where T{i} : global::OptimFoundation.Core.ISetBrick"));

                sb.Append($@"    /// <summary>
    /// 標記變數類別的維度（paved path）：泛型參數依序為各維度的 set 積木型別，generator 據此生成 property。
    /// 型別不是積木會直接 compile error（CS0311），比字串式早一步抓到錯。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class OptVarAttribute<{tparams}> : Attribute {constraints} {{ }}

    /// <summary>
    /// 標記參數類別的維度（paved path）：泛型參數依序為各維度的 set 積木型別。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class OptParamAttribute<{tparams}> : Attribute {constraints}
    {{
        /// <summary>true（預設）會生成 QTY 值欄位；純 key 參數設 false。</summary>
        public bool HasValue {{ get; set; }} = true;
    }}

");
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// generator 進入點：先把 attribute 定義注入使用端編譯（PostInitialization，故使用端不需引用額外組件），
        /// 再依各 attribute 分別註冊產碼管線——字串式 OptVar / OptParam（逃生口）、OptSet 積木、
        /// 泛型 OptVar / OptParam（arity 1..6）與 OptDim 具名維度。
        /// </summary>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(ctx =>
                ctx.AddSource("OptModelingAttributes.g.cs", SourceText.From(AttributeSource, Encoding.UTF8)));

            // A. 字串式（原封保留）
            Register(context, VarAttr, ExtractVar);
            Register(context, ParamAttr, ExtractParam);

            // B. Set 積木（非泛型預設 string + 泛型 arity 1）
            Register(context, SetAttr, ExtractSet);
            Register(context, SetAttr + "`1", ExtractSet);

            // B. 泛型 Var / Param（arity 1..MaxArity）
            for (int n = 1; n <= MaxArity; n++)
            {
                Register(context, VarAttr + "`" + n, ExtractVarGeneric);
                Register(context, ParamAttr + "`" + n, ExtractParamGeneric);
            }

            // C. DataContext 註冊碼：非屬性標記，靠繼承關係掃（見框架資料防護規格）
            var dataContexts = context.SyntaxProvider.CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax cds
                        && cds.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword),
                    transform: static (ctx, _) => ExtractDataContext(ctx))
                .Where(m => m is not null);
            context.RegisterSourceOutput(dataContexts, static (spc, m) => EmitDataContextRegister(spc, m!));
        }

        private static void Register(
            IncrementalGeneratorInitializationContext context,
            string metadataName,
            System.Func<GeneratorAttributeSyntaxContext, EmitModel?> extract)
        {
            var provider = context.SyntaxProvider.ForAttributeWithMetadataName(
                    metadataName,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: (ctx, _) => extract(ctx))
                .Where(m => m is not null);
            context.RegisterSourceOutput(provider, static (spc, m) => Emit(spc, m!));
        }

        // ── A. 字串式提取（原封保留） ──

        private static EmitModel? ExtractVar(GeneratorAttributeSyntaxContext ctx)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol symbol || ctx.Attributes.Length == 0) return null;

            string? varType = VarTypeFromPrefix(symbol.Name);

            // 具名維度 [OptDim<TSet>("name")] 優先（同 set 多維度 / 自訂 index 名）
            var (dimProps, dimNotBrick, hasDims) = ResolveDims(symbol);
            if (hasDims)
                return new EmitModel(NamespaceOf(symbol), symbol.Name, VariableBaseFqn, string.Empty,
                    AddQty: false, AddCtors: false,
                    Meta: varType == null ? string.Empty : $"VarType={varType}（由類別名前綴決定）",
                    NamingViolation: varType == null ? VarNamingRule : dimNotBrick,
                    DiagLocation: symbol.Locations.FirstOrDefault(),
                    Props: dimProps, DiagArg: symbol.Name);

            // 字串式（原封保留）
            var args = ctx.Attributes[0].ConstructorArguments;
            if (args.Length < 1) return null;

            string setsCsv = JoinSets(args[0]);

            return new EmitModel(NamespaceOf(symbol), symbol.Name, VariableBaseFqn, setsCsv,
                AddQty: false, AddCtors: false,
                Meta: varType == null ? string.Empty : $"VarType={varType}（由類別名前綴決定）",
                NamingViolation: varType == null ? VarNamingRule : null,
                DiagLocation: symbol.Locations.FirstOrDefault());
        }

        private static EmitModel? ExtractParam(GeneratorAttributeSyntaxContext ctx)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol symbol || ctx.Attributes.Length == 0) return null;

            var attr = ctx.Attributes[0];
            bool hasValue = ReadHasValue(attr);
            bool badPrefix = !symbol.Name.StartsWith("Parameter_", System.StringComparison.Ordinal);

            // 具名維度 [OptDim<TSet>("name")] 優先
            var (dimProps, dimNotBrick, hasDims) = ResolveDims(symbol);
            if (hasDims)
                return new EmitModel(NamespaceOf(symbol), symbol.Name, ParameterBaseFqn, string.Empty,
                    AddQty: hasValue, AddCtors: true, Meta: string.Empty,
                    NamingViolation: badPrefix ? ParamNamingRule : dimNotBrick,
                    DiagLocation: symbol.Locations.FirstOrDefault(),
                    Props: dimProps, DiagArg: symbol.Name);

            // 字串式（原封保留）
            if (attr.ConstructorArguments.Length < 1) return null;

            string setsCsv = JoinSets(attr.ConstructorArguments[0]);

            return new EmitModel(NamespaceOf(symbol), symbol.Name, ParameterBaseFqn, setsCsv,
                AddQty: hasValue, AddCtors: true, Meta: string.Empty,
                NamingViolation: badPrefix ? ParamNamingRule : null,
                DiagLocation: symbol.Locations.FirstOrDefault());
        }

        // ── B. Set 積木 + 泛型引用提取（新增） ──

        private static EmitModel? ExtractSet(GeneratorAttributeSyntaxContext ctx)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol symbol || ctx.Attributes.Length == 0) return null;

            var loc = symbol.Locations.FirstOrDefault();
            bool badPrefix = !symbol.Name.StartsWith("Set_", System.StringComparison.Ordinal);

            // 元素型別：泛型 [OptSet<T>] 取型別參數；非泛型 [OptSet] 預設 string
            var attrClass = ctx.Attributes[0].AttributeClass;
            string elemFq = "string";
            string elemDisplay = "string";
            bool legal = true;
            if (attrClass != null && attrClass.IsGenericType && attrClass.TypeArguments.Length == 1)
            {
                var (fq, _, ok) = MapElem(attrClass.TypeArguments[0]);
                elemFq = fq;
                elemDisplay = attrClass.TypeArguments[0].ToDisplayString();
                legal = ok;
            }

            var diag = badPrefix ? SetNamingRule : (legal ? null : SetElementTypeRule);

            return new EmitModel(NamespaceOf(symbol), symbol.Name, $"{SetBaseFqn}<{elemFq}>", string.Empty,
                AddQty: false, AddCtors: false, Meta: string.Empty,
                NamingViolation: diag, DiagLocation: loc,
                Props: System.Array.Empty<PropSpec>(), DiagArg: elemDisplay);
        }

        private static EmitModel? ExtractVarGeneric(GeneratorAttributeSyntaxContext ctx)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol symbol || ctx.Attributes.Length == 0) return null;

            string? varType = VarTypeFromPrefix(symbol.Name);
            var (props, notBrick) = ResolveBricks(ctx.Attributes[0].AttributeClass);

            return new EmitModel(NamespaceOf(symbol), symbol.Name, VariableBaseFqn, string.Empty,
                AddQty: false, AddCtors: false,
                Meta: varType == null ? string.Empty : $"VarType={varType}（由類別名前綴決定）",
                NamingViolation: varType == null ? VarNamingRule : notBrick,
                DiagLocation: symbol.Locations.FirstOrDefault(),
                Props: props, DiagArg: symbol.Name);
        }

        private static EmitModel? ExtractParamGeneric(GeneratorAttributeSyntaxContext ctx)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol symbol || ctx.Attributes.Length == 0) return null;

            bool hasValue = ReadHasValue(ctx.Attributes[0]);
            bool badPrefix = !symbol.Name.StartsWith("Parameter_", System.StringComparison.Ordinal);
            var (props, notBrick) = ResolveBricks(ctx.Attributes[0].AttributeClass);

            return new EmitModel(NamespaceOf(symbol), symbol.Name, ParameterBaseFqn, string.Empty,
                AddQty: hasValue, AddCtors: true, Meta: string.Empty,
                NamingViolation: badPrefix ? ParamNamingRule : notBrick,
                DiagLocation: symbol.Locations.FirstOrDefault(),
                Props: props, DiagArg: symbol.Name);
        }

        // ── C. DataContext 註冊碼提取（新增，見框架資料防護規格）──
        // 靠繼承關係找 DataContext 子類（非 attribute 標記），為它 emit RegisterAll override。

        private static DataContextEmitModel? ExtractDataContext(GeneratorSyntaxContext ctx)
        {
            var cds = (ClassDeclarationSyntax)ctx.Node;
            if (ctx.SemanticModel.GetDeclaredSymbol(cds) is not INamedTypeSymbol symbol) return null;
            if (!DerivesFrom(symbol, "OptimFoundation.Core.DataContext")) return null;

            // 多個 partial 宣告時只處理其一（member 已由 symbol.GetMembers() 彙整），避免重複 emit RegisterAll
            var syntaxRefs = symbol.DeclaringSyntaxReferences;
            if (syntaxRefs.Length > 1)
            {
                var first = syntaxRefs
                    .OrderBy(r => r.SyntaxTree.FilePath, System.StringComparer.Ordinal)
                    .ThenBy(r => r.Span.Start)
                    .First();
                if (first.SyntaxTree != cds.SyntaxTree || first.Span != cds.Span) return null;
            }

            var sets = new System.Collections.Generic.List<SetReg>();
            var parms = new System.Collections.Generic.List<ParamReg>();
            var diagnostics = new System.Collections.Generic.List<Diagnostic>();
            var aliases = new System.Collections.Generic.List<(string CustomName, string SetRegisterName)>();

            foreach (var member in symbol.GetMembers())
            {
                ITypeSymbol? memberType = member switch
                {
                    IFieldSymbol f when !f.IsStatic && f.DeclaredAccessibility == Accessibility.Public => f.Type,
                    IPropertySymbol p when !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public => p.Type,
                    _ => null
                };
                if (memberType is not INamedTypeSymbol namedType) continue;

                if (IsSetBrick(namedType))
                {
                    string regName = namedType.Name.StartsWith("Set_", System.StringComparison.Ordinal)
                        ? namedType.Name.Substring(4) : namedType.Name;
                    sets.Add(new SetReg(regName, member.Name));
                    continue;
                }

                if (TryGetParamElementType(namedType, out var paramType))
                {
                    string[] indexProps = ResolveIndexSetNames(paramType);
                    string[] numberProps = ResolveNumberPropNames(paramType);
                    bool fullGrid = paramType.GetAttributes().Any(a =>
                        a.AttributeClass?.ToDisplayString() == "OptimFoundation.Core.FullGridAttribute");
                    parms.Add(new ParamReg(member.Name, indexProps, numberProps, fullGrid));
                    aliases.AddRange(ResolveDimAliases(paramType));
                    continue;
                }

                // 靜默跳過防呆：欄位型別看起來是 Set_*/Parameter_* 但漏掛對應 attribute → 不會被 IsSetBrick/
                // TryGetParamElementType 承認，會被無聲排除於 RegisterAll 之外——升級成 compile error（OPTF006）。
                if (namedType.Name.StartsWith("Set_", System.StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic.Create(UnregisteredDataMemberRule, member.Locations.FirstOrDefault(),
                        $"'{namedType.Name}' 被 '{symbol.Name}.{member.Name}' 引用但未掛 [OptSet]/[OptSet<T>]，將無法納入資料驗證；請補上 attribute"));
                    continue;
                }

                if (IsListLike(namedType, out var elem) && elem.Name.StartsWith("Parameter_", System.StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic.Create(UnregisteredDataMemberRule, member.Locations.FirstOrDefault(),
                        $"'{elem.Name}' 被 '{symbol.Name}.{member.Name}' 引用但未掛 [OptParam]/[OptParam<...>]，將無法納入資料驗證；請補上 attribute"));
                }
            }

            return new DataContextEmitModel(NamespaceOf(symbol), symbol.Name, sets.ToArray(), parms.ToArray(), diagnostics.ToArray(), aliases.ToArray());
        }

        // [OptDim<TSet>("自訂名")] 且自訂名與 TSet 型別推導出的預設 register 名不同時，回傳 (自訂名, 型別推導名)。
        // 供 EmitDataContextRegister 額外註冊別名：同一顆 Set 被多個自訂維度名引用時（如 PreGroup/Group 皆指向
        // Set_Group），generator 只會用型別名註冊一次，若不補別名，用自訂名查詢會恆報 MissingSet（見框架資料防護規格）。
        // 非合法 Set 積木（漏掛 [OptSet]）已由 ResolveDims 報 NotASetBrickRule，這裡略過不重複處理。
        private static (string CustomName, string SetRegisterName)[] ResolveDimAliases(INamedTypeSymbol paramType)
        {
            var dims = paramType.GetAttributes()
                .Where(a => a.AttributeClass != null && a.AttributeClass.Name == "OptDimAttribute" && a.AttributeClass.IsGenericType)
                .ToList();
            if (dims.Count == 0) return System.Array.Empty<(string, string)>();

            var list = new System.Collections.Generic.List<(string, string)>();
            foreach (var d in dims)
            {
                string customName = d.ConstructorArguments.Length > 0
                    ? d.ConstructorArguments[0].Value?.ToString() ?? string.Empty : string.Empty;
                var setSym = d.AttributeClass!.TypeArguments.Length == 1 ? d.AttributeClass.TypeArguments[0] : null;
                if (setSym == null || customName.Length == 0) continue;

                bool isBrick = setSym.GetAttributes().Any(a => a.AttributeClass?.Name == "OptSetAttribute");
                if (!isBrick) continue;

                string registerName = setSym.Name.StartsWith("Set_", System.StringComparison.Ordinal)
                    ? setSym.Name.Substring(4) : setSym.Name;

                if (customName != registerName)
                    list.Add((customName, registerName));
            }
            return list.ToArray();
        }

        // set 積木判定：類別本身掛 [OptSet]/[OptSet&lt;T&gt;]（原始語法即見，不吃同一 pass 內其它 emit 的 base type）
        private static bool IsSetBrick(INamedTypeSymbol t)
            => t.GetAttributes().Any(a => a.AttributeClass != null && a.AttributeClass.Name == "OptSetAttribute");

        // List&lt;T&gt; / IReadOnlyList&lt;T&gt; → T（不判斷 T 是否為合法 param，純結構判斷，供兩處共用）
        private static bool IsListLike(INamedTypeSymbol listType, out INamedTypeSymbol elementType)
        {
            elementType = null!;
            if (!listType.IsGenericType || listType.TypeArguments.Length != 1) return false;

            string originalName = listType.OriginalDefinition.ToDisplayString();
            bool isListLike = originalName == "System.Collections.Generic.List<T>"
                || originalName == "System.Collections.Generic.IReadOnlyList<T>";
            if (!isListLike) return false;

            if (listType.TypeArguments[0] is not INamedTypeSymbol elem) return false;
            elementType = elem;
            return true;
        }

        // List&lt;Parameter_X&gt; / IReadOnlyList&lt;Parameter_X&gt; → Parameter_X（Parameter_X 本身掛 [OptParam]/[OptParam&lt;...&gt;]）
        private static bool TryGetParamElementType(INamedTypeSymbol listType, out INamedTypeSymbol elementType)
        {
            elementType = null!;
            if (!IsListLike(listType, out var elem)) return false;
            bool isParam = HasOptParamAttribute(elem);
            if (!isParam) return false;

            elementType = elem;
            return true;
        }

        private static bool HasOptParamAttribute(INamedTypeSymbol t)
            => t.GetAttributes().Any(a => a.AttributeClass != null && a.AttributeClass.Name == "OptParamAttribute");

        private static bool DerivesFrom(INamedTypeSymbol type, string fullyQualifiedBaseName)
        {
            for (var b = type.BaseType; b != null; b = b.BaseType)
                if (b.ToDisplayString() == fullyQualifiedBaseName) return true;
            return false;
        }

        // index-set 屬性規格沿用 Parameter 類別自己產碼時已解析出的來源（ResolveDims/ResolveBricks/字串式），
        // NEVER 命名猜測——供 ResolveIndexSetNames（註冊 metadata）與 ResolveNumberPropNames（numbersOf 萃取器）共用。
        private static PropSpec[] ResolveParamIndexProps(INamedTypeSymbol paramType)
        {
            var (dimProps, _, hasDims) = ResolveDims(paramType);
            if (hasDims) return dimProps;

            var optParamAttr = paramType.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass != null && a.AttributeClass.Name == "OptParamAttribute");
            if (optParamAttr == null) return System.Array.Empty<PropSpec>();

            if (optParamAttr.AttributeClass!.IsGenericType)
            {
                var (props, _) = ResolveBricks(optParamAttr.AttributeClass);
                return props;
            }

            if (optParamAttr.ConstructorArguments.Length > 0)
            {
                string setsCsv = JoinSets(optParamAttr.ConstructorArguments[0]);
                if (setsCsv.Length == 0) return System.Array.Empty<PropSpec>();
                return setsCsv.Split('|').Select(raw =>
                {
                    var (name, type, isString) = ParseSet(raw);
                    return new PropSpec(name, type, isString);
                }).ToArray();
            }

            return System.Array.Empty<PropSpec>();
        }

        private static string[] ResolveIndexSetNames(INamedTypeSymbol paramType)
            => ResolveParamIndexProps(paramType).Select(p => p.Name).ToArray();

        // numbersOf 萃取器涵蓋的欄位名：該 Parameter 型別上「所有 double 型別屬性」，來源三路徑（去重、保持穩定順序）：
        //   1) index props 裡型別為 double 的（generator 自己 emit，來源＝attribute 宣告反推，symbol 這時看不到）
        //   2) QTY（generator 自己 emit，當 HasValue=true，同理不能靠 symbol 看）
        //   3) 使用者在 partial 另一半手寫的 double 屬性（宣告期即存在於原始碼，symbol 可見，用 GetMembers() 掃）
        // 這是修掉「數值 sanity 漏掉非 QTY 值欄位」的實質漏洞（見框架資料防護規格追補）：真實專案值欄位多半
        // 走 [OptParam(HasValue=false)] + 手寫 double 值欄位（如 Profit/Required/Stock），先前完全不受涵蓋。
        // 非 double 的值欄位（int/decimal 等）本次不納入，維持現狀。
        private static string[] ResolveNumberPropNames(INamedTypeSymbol paramType)
        {
            var names = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

            foreach (var p in ResolveParamIndexProps(paramType).Where(p => p.Type == "double"))
                if (seen.Add(p.Name)) names.Add(p.Name);

            if (ParamHasValue(paramType) && seen.Add("QTY"))
                names.Add("QTY");

            foreach (var member in paramType.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.IsStatic || prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (prop.Type.SpecialType != SpecialType.System_Double) continue;
                if (seen.Add(prop.Name)) names.Add(prop.Name);
            }

            return names.ToArray();
        }

        private static bool ParamHasValue(INamedTypeSymbol paramType)
        {
            var optParamAttr = paramType.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass != null && a.AttributeClass.Name == "OptParamAttribute");
            return optParamAttr == null || ReadHasValue(optParamAttr);
        }

        private static void EmitDataContextRegister(SourceProductionContext spc, DataContextEmitModel m)
        {
            foreach (var d in m.Diagnostics)
                spc.ReportDiagnostic(d);

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();

            bool hasNs = m.Namespace.Length > 0;
            if (hasNs)
            {
                sb.Append("namespace ").Append(m.Namespace).AppendLine();
                sb.AppendLine("{");
            }

            sb.Append("    public partial class ").Append(m.ClassName).AppendLine();
            sb.AppendLine("    {");
            sb.AppendLine("        // 由 AutoSetsGenerator 依繼承關係掃出的 DataContext 子類 emit（見框架資料防護規格）");
            sb.AppendLine("        protected override void RegisterAll()");
            sb.AppendLine("        {");

            var registeredSetNames = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (var s in m.Sets)
            {
                sb.Append("            RegisterSet(\"").Append(s.RegisterName).Append("\", ").Append(s.FieldName).AppendLine(");");
                registeredSetNames.Add(s.RegisterName);
            }

            // 同一顆 Set 被多個自訂維度名引用（[OptDim<TSet>("自訂名")]）→ 各自訂名都額外註冊一次別名，
            // 指向同一顆 Set 欄位（同名只註冊一次）。找不到對應 Set 欄位（使用者沒宣告那顆積木）→ 略過，
            // 維持現狀讓它報 MissingSet（那是真缺，不該掩蓋）。
            foreach (var alias in m.Aliases)
            {
                if (!registeredSetNames.Add(alias.CustomName)) continue;
                var targetSet = m.Sets.FirstOrDefault(s => s.RegisterName == alias.SetRegisterName);
                if (targetSet == null) continue;
                sb.Append("            RegisterSet(\"").Append(alias.CustomName).Append("\", ").Append(targetSet.FieldName).AppendLine(");");
            }

            foreach (var p in m.Params)
            {
                string idxArr = p.IndexSets.Length == 0
                    ? "global::System.Array.Empty<string>()"
                    : "new[] { " + string.Join(", ", p.IndexSets.Select(n => "\"" + n + "\"")) + " }";
                string indexOf = p.IndexSets.Length == 0
                    ? "r => global::System.Array.Empty<object>()"
                    : "r => new object[] { " + string.Join(", ", p.IndexSets.Select(n => "r." + n)) + " }";
                string numbersOf = p.NumberProps.Length == 0
                    ? "r => global::System.Array.Empty<(string, double)>()"
                    : "r => new (string, double)[] { " + string.Join(", ", p.NumberProps.Select(n => "(\"" + n + "\", r." + n + ")")) + " }";

                sb.Append("            RegisterParam(").Append(p.FieldName).Append(", ").Append(idxArr)
                  .Append(", ").Append(indexOf).Append(", ").Append(numbersOf)
                  .Append(", fullGrid: ").Append(p.FullGrid ? "true" : "false").AppendLine(");");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            if (hasNs) sb.AppendLine("}");

            spc.AddSource($"{m.ClassName}.DataContextRegister.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        // 把泛型 attribute 的型別參數（Set 積木）解析成 property 規格。回傳 (props, 若有非積木則帶 diagnostic)
        private static (PropSpec[] props, DiagnosticDescriptor? notBrick) ResolveBricks(INamedTypeSymbol? attrClass)
        {
            if (attrClass == null || attrClass.TypeArguments.Length == 0)
                return (System.Array.Empty<PropSpec>(), null);

            var list = new System.Collections.Generic.List<PropSpec>();
            DiagnosticDescriptor? notBrick = null;

            foreach (var arg in attrClass.TypeArguments)
            {
                string propName = arg.Name.StartsWith("Set_", System.StringComparison.Ordinal)
                    ? arg.Name.Substring(4) : arg.Name;

                var optSet = arg.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass != null && a.AttributeClass.Name == "OptSetAttribute");

                if (optSet == null)
                {
                    notBrick = NotASetBrickRule;
                    list.Add(new PropSpec(propName, "string", true));
                    continue;
                }

                if (optSet.AttributeClass!.IsGenericType && optSet.AttributeClass.TypeArguments.Length == 1)
                {
                    var (fq, isStr, _) = MapElem(optSet.AttributeClass.TypeArguments[0]);
                    list.Add(new PropSpec(propName, fq, isStr));
                }
                else
                {
                    list.Add(new PropSpec(propName, "string", true));
                }
            }
            return (list.ToArray(), notBrick);
        }

        // 讀 class 上所有 [OptDim<TSet>("name")]（宣告序）→ property 規格。名字=arg、型別=TSet 的 [OptSet<T>]
        private static (PropSpec[] props, DiagnosticDescriptor? notBrick, bool has) ResolveDims(INamedTypeSymbol symbol)
        {
            var dims = symbol.GetAttributes()
                .Where(a => a.AttributeClass != null && a.AttributeClass.Name == "OptDimAttribute" && a.AttributeClass.IsGenericType)
                .ToList();
            if (dims.Count == 0) return (System.Array.Empty<PropSpec>(), null, false);

            var list = new System.Collections.Generic.List<PropSpec>();
            DiagnosticDescriptor? notBrick = null;
            foreach (var d in dims)
            {
                string name = d.ConstructorArguments.Length > 0 ? d.ConstructorArguments[0].Value?.ToString() ?? string.Empty : string.Empty;
                var setSym = d.AttributeClass!.TypeArguments.Length == 1 ? d.AttributeClass.TypeArguments[0] : null;
                var optSet = setSym?.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "OptSetAttribute");
                if (optSet == null)
                {
                    notBrick = NotASetBrickRule;
                    list.Add(new PropSpec(name, "string", true));
                    continue;
                }
                var (fq, isStr) = ElemFromOptSetAttr(optSet.AttributeClass!);
                list.Add(new PropSpec(name, fq, isStr));
            }
            return (list.ToArray(), notBrick, true);
        }

        // OptSetAttribute（generic 或非 generic）→ 元素型別
        private static (string fq, bool isString) ElemFromOptSetAttr(INamedTypeSymbol optSetAttrClass)
        {
            if (optSetAttrClass.IsGenericType && optSetAttrClass.TypeArguments.Length == 1)
            {
                var (fq, isStr, _) = MapElem(optSetAttrClass.TypeArguments[0]);
                return (fq, isStr);
            }
            return ("string", true);
        }

        // CLR 元素型別 → (生成用 fq 型別, 是否 string, 是否合法域)
        private static (string fq, bool isString, bool legal) MapElem(ITypeSymbol t)
        {
            switch (t.SpecialType)
            {
                case SpecialType.System_String: return ("string", true, true);
                case SpecialType.System_Int32: return ("int", false, true);
                case SpecialType.System_Int64: return ("long", false, true);
                case SpecialType.System_Double: return ("double", false, true);
                case SpecialType.System_Decimal: return ("decimal", false, true);
                case SpecialType.System_DateTime: return ("global::System.DateTime", false, true);
            }
            if (t.ToDisplayString() == "System.DateTime") return ("global::System.DateTime", false, true);
            return ("string", true, false);
        }

        private static bool ReadHasValue(AttributeData attr)
        {
            foreach (var na in attr.NamedArguments)
                if (na.Key == "HasValue" && na.Value.Value is bool b) return b;
            return true;
        }

        private static string JoinSets(TypedConstant arg)
        {
            if (arg.Kind != TypedConstantKind.Array) return string.Empty;
            var sets = arg.Values.Select(v => v.Value?.ToString() ?? string.Empty).Where(s => s.Length > 0);
            return string.Join("|", sets);
        }

        private static string NamespaceOf(INamedTypeSymbol s)
            => s.ContainingNamespace.IsGlobalNamespace ? string.Empty : s.ContainingNamespace.ToDisplayString();

        private static void Emit(SourceProductionContext spc, EmitModel m)
        {
            if (m.NamingViolation != null)
            {
                string arg = m.DiagArg ?? m.ClassName;
                spc.ReportDiagnostic(m.NamingViolation == SetElementTypeRule || m.NamingViolation == NotASetBrickRule
                    ? Diagnostic.Create(m.NamingViolation, m.DiagLocation, m.ClassName, arg)
                    : Diagnostic.Create(m.NamingViolation, m.DiagLocation, m.ClassName));
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();

            bool hasNs = m.Namespace.Length > 0;
            if (hasNs)
            {
                sb.Append("namespace ").Append(m.Namespace).AppendLine();
                sb.AppendLine("{");
            }

            if (m.Meta.Length > 0)
                sb.Append("    // [").Append(m.Meta).AppendLine("] —— 由 AutoSetsGenerator 生成");
            sb.Append("    public partial class ").Append(m.ClassName).Append(" : ").AppendLine(m.BaseFqn);
            sb.AppendLine("    {");

            // 屬性：泛型路徑用預解析的 Props；字串路徑用 SetsCsv + ParseSet（原邏輯）
            if (m.Props != null)
            {
                foreach (var p in m.Props)
                {
                    if (p.IsString)
                        sb.Append("        public string ").Append(p.Name).AppendLine(" { get; set; } = string.Empty;");
                    else
                        sb.Append("        public ").Append(p.Type).Append(' ').Append(p.Name).AppendLine(" { get; set; }");
                }
            }
            else
            {
                string[] sets = m.SetsCsv.Length == 0 ? System.Array.Empty<string>() : m.SetsCsv.Split('|');
                foreach (var raw in sets)
                {
                    var (name, type, isString) = ParseSet(raw);
                    if (isString)
                        sb.Append("        public string ").Append(name).AppendLine(" { get; set; } = string.Empty;");
                    else
                        sb.Append("        public ").Append(type).Append(' ').Append(name).AppendLine(" { get; set; }");
                }
            }

            if (m.AddQty)
                sb.AppendLine("        public double QTY { get; set; }");

            if (m.AddCtors)
            {
                sb.AppendLine();
                sb.Append("        public ").Append(m.ClassName).AppendLine("(params object[] sets) => InitClassBySets(sets);");
                sb.Append("        public ").Append(m.ClassName).AppendLine("() { }");
            }

            sb.AppendLine("    }");
            if (hasNs) sb.AppendLine("}");

            spc.AddSource($"{m.ClassName}.AutoSets.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        // "Name" → string；"Name:DateTime|date|int|double" → 對應型別（字串路徑用）
        private static (string name, string type, bool isString) ParseSet(string raw)
        {
            var parts = raw.Split(':');
            string name = parts[0].Trim();
            string t = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "string";

            switch (t)
            {
                case "datetime":
                case "date": return (name, "global::System.DateTime", false);
                case "int": return (name, "int", false);
                case "double": return (name, "double", false);
                default: return (name, "string", true);
            }
        }

        private sealed record PropSpec(string Name, string Type, bool IsString);

        private sealed record EmitModel(
            string Namespace, string ClassName, string BaseFqn, string SetsCsv,
            bool AddQty, bool AddCtors, string Meta,
            DiagnosticDescriptor? NamingViolation = null, Location? DiagLocation = null,
            PropSpec[]? Props = null, string? DiagArg = null);

        private sealed record SetReg(string RegisterName, string FieldName);
        private sealed record ParamReg(string FieldName, string[] IndexSets, string[] NumberProps, bool FullGrid);
        private sealed record DataContextEmitModel(
            string Namespace, string ClassName, SetReg[] Sets, ParamReg[] Params, Diagnostic[] Diagnostics,
            (string CustomName, string SetRegisterName)[] Aliases);
    }
}
