using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OptimFoundation.Internal;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 變數的把關與索引，與 solver 無關、不建 engine 就能測：
    /// 把關 = 建立前決定變數型別、比對維度、略過重複名稱；索引 = 由 Set 組出 TypeName@維度值名稱（格式同 ModelElementBase.ToString()）、
    /// 依型別取名稱、由名稱取型別名。Engine 只拿這裡算好的名稱去建 solver 變數、登記與讀值。
    /// </summary>
    public static class VariableManager
    {
        #region 把關

        /// <summary>
        /// 泛型 Build*Vs / BuildVars 建立前的把關：決定變數型別，再依位置比對 sets 與 TVariable 的維度數量與型別。
        /// requestedType 為 null（BuildVars）時由類別名前綴判型，判不出來丟例外；
        /// 有值（BuildCVs / BuildIVs / BuildBVs）時前綴宣告的型別與它不同就丟例外，沒有正式前綴的類別照 requestedType 建立。
        /// </summary>
        /// <param name="requestedType">建立方法指定的型別；null 表示由前綴決定。</param>
        /// <param name="operation">呼叫端方法名，用於錯誤訊息與 log 的位置。</param>
        /// <param name="sets">各維度的集合。</param>
        /// <returns>要建立的變數型別。</returns>
        public static VarType ValidateVariableClass<TVariable>(VarType? requestedType, string operation, object[] sets)
        {
            string className = typeof(TVariable).Name;
            VarType declaredType = default;
            bool hasPrefix = VariablePrefixNaming.TryResolve(className, out var typeName);
            if (hasPrefix)
                declaredType = (VarType)Enum.Parse(typeof(VarType), typeName);

            if (requestedType == null && !hasPrefix)
                throw Logging.ErrorOnce(
                    new ArgumentException($"{operation}<{className}> 無法從類別名前綴判定變數型別；" +
                        $"命名天條：{VariablePrefixNaming.NamingGuide}，例：VariableC_Start；" +
                        "不依天條命名請改用 BuildCVs / BuildIVs / BuildBVs"),
                    "變數型別不合法", "無法從名稱前綴判定變數型別", operation, className,
                    "前綴不合法", $"變數類別={className}");

            if (requestedType != null && hasPrefix && declaredType != requestedType)
                throw Logging.ErrorOnce(
                    new ArgumentException($"{operation}<{className}> 要建立 {requestedType} 變數，但類別名前綴宣告為 {declaredType}；" +
                        $"命名規則：{VariablePrefixNaming.NamingGuide}"),
                    "變數型別不一致", "變數名稱前綴與建立方法指定的型別不一致", operation, className,
                    "宣告型別與指定型別不一致", $"變數類別={className} 宣告型別={declaredType} 指定型別={requestedType}");

            ValidateVariableDimensions<TVariable>(sets);
            return requestedType ?? declaredType;
        }

        /// <summary>
        /// 略過重複名稱：同一批內重複，或 <paramref name="exists"/> 回 true（已在變數池）的名稱不新建，記 <c>[變數重複]</c> 警告。
        /// 回傳要新建的名稱，順序不變。
        /// </summary>
        public static List<string> SkipDuplicates(string typeName, IReadOnlyList<string> names, Func<string, bool> exists)
        {
            var newNames = new List<string>(names.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new List<string>();
            foreach (var name in names)
            {
                if (exists(name) || !seen.Add(name))
                    duplicates.Add(name);
                else
                    newNames.Add(name);
            }

            if (duplicates.Count > 0)
                Logging.Warn($"[變數重複] 變數類別={typeName} 數量={duplicates.Count} 範例={string.Join(",", duplicates.Take(5))} 原因=名稱已存在 結果=保留原值");
            return newNames;
        }

        #endregion

        #region 索引

        /// <summary>某型別的變數名：名稱等於型別名（0 維變數），或以「型別名@」開頭；typeName 為 null 回空。順序同 allNames。</summary>
        public static IEnumerable<string> GetNamesOfType(string typeName, IEnumerable<string> allNames)
        {
            if (typeName == null) return Enumerable.Empty<string>();
            string prefix = typeName + ModelNaming.Separator;
            return allNames.Where(name => name == typeName || name.StartsWith(prefix, StringComparison.Ordinal));
        }

        /// <summary>變數名 → 型別名（第一個 @ 之前）；空白名稱回 <c>&lt;未命名&gt;</c>。建立統計與未引用變數分組用。</summary>
        public static string GetTypeName(string variableName)
        {
            if (string.IsNullOrWhiteSpace(variableName)) return "<未命名>";
            int separator = variableName.IndexOf(ModelNaming.Separator);
            return separator > 0 ? variableName.Substring(0, separator) : variableName;
        }

        #endregion

        /// <summary>組合各 Set 的資料列，回傳每組維度值的字串陣列；完整名稱由呼叫端組成。</summary>
        private static IEnumerable<string[]> CombineRows(List<string[]>[] setRows)
        {
            IEnumerable<string[]> result = new[] { Array.Empty<string>() };
            foreach (var rows in setRows)
                result = result.SelectMany(_ => rows, (prefix, row) =>
                {
                    var next = new string[prefix.Length + row.Length];
                    prefix.CopyTo(next, 0);
                    row.CopyTo(next, prefix.Length);
                    return next;
                });
            return result;
        }

        private static List<string[]>[] ConvertSetsToRows(object[] sets)
        {
            if (sets == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(sets), "sets 不得為 null"),
                    "變數維度集合不合法", null, nameof(ConvertSetsToRows), null,
                    "集合陣列為空");

            if (sets.Length > 0 && sets.All(x => x is string))
                sets = [sets.Cast<string>().ToList()];

            var result = new List<string[]>[sets.Length];
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i] == null)
                    throw Logging.ErrorOnce(
                        new ArgumentException($"集合 #{i + 1} 不得為 null", nameof(sets)),
                        "變數維度集合不合法", null, nameof(ConvertSetsToRows), null,
                        "集合為空", $"序號={i + 1}");

                if (sets[i] is System.Collections.IEnumerable rowSequence)
                {
                    var setRows = rowSequence.Cast<object>().ToList();
                    if (setRows.All(row => row is SetRowBase))
                    {
                        result[i] = setRows.Select(row => ModelElementBase.GetDimensions(row.GetType())
                            .Select(property => ModelNaming.Token(
                                $"集合資料列 #{i + 1}.{property.Name}", property.GetValue(row)))
                            .ToArray()).ToList();
                        continue;
                    }
                }

                if (GetValueTupleElementType(sets[i]) != null)
                {
                    if (sets[i] is not System.Collections.IEnumerable sequence)
                        throw Logging.ErrorOnce(
                            new ArgumentException($"集合 #{i + 1} 必須可列舉"),
                            "變數維度集合不合法", null, nameof(ConvertSetsToRows), sets[i],
                            "集合無法列舉", $"序號={i + 1}");

                    result[i] = sequence.Cast<object>().Select(item =>
                    {
                        if (item is not ITuple tuple)
                            throw Logging.ErrorOnce(
                                new ArgumentException($"集合 #{i + 1} 含有非元組的成員"),
                                "變數維度集合不合法", null, nameof(ConvertSetsToRows), item,
                                "集合成員不是元組", $"序號={i + 1}");
                        return Enumerable.Range(0, tuple.Length)
                            .Select(index => ModelNaming.Token($"集合 #{i + 1} 成員 #{index + 1}", tuple[index]))
                            .ToArray();
                    }).ToList();
                }
                else
                {
                    result[i] = ConvertSetsToTokens(sets[i]).Single()
                        .Select(value => new[] { value }).ToList();
                }
            }
            return result;
        }

        private static Type GetValueTupleElementType(object value)
        {
            foreach (var type in value.GetType().GetInterfaces())
            {
                if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(IEnumerable<>)) continue;
                var elementType = type.GetGenericArguments()[0];
                if (elementType.IsValueType && elementType.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true)
                    return elementType;
            }
            return null;
        }

        // 與 ConvertSetsToTokens 支援的元素型別相同（enum 另外判斷）
        private static readonly HashSet<Type> TokenTypes =
            [typeof(DateTime), typeof(int), typeof(long), typeof(double), typeof(decimal), typeof(string)];

        /// <summary>
        /// 依位置逐維比對各集合的維度型別與 TVariable 維度 property 的型別，數量或型別不同就丟例外；型別必須完全相同。
        /// 維度型別取自集合的宣告型別，空集合也能比對；判斷不出維度型別的集合（null、裸 string、不支援的型別）
        /// 不在這裡處理，留給組名時的既有檢查回報。
        /// </summary>
        /// <param name="sets">BuildVars 收到的各維度集合。</param>
        /// <typeparam name="TVariable">要建立的變數類別。</typeparam>
        private static void ValidateVariableDimensions<TVariable>(object[] sets)
        {
            if (sets == null) return;
            if (sets.Length > 0 && sets.All(x => x is string))
                sets = [sets.Cast<string>().ToList()];

            var supplied = new List<(int SetNumber, Type Type)>();
            for (int i = 0; i < sets.Length; i++)
            {
                var types = GetDimensionTypes(sets[i]);
                if (types == null) return;
                int setNumber = i + 1;
                supplied.AddRange(types.Select(type => (setNumber, type)));
            }

            string className = typeof(TVariable).Name;
            var properties = ModelElementBase.GetDimensions(typeof(TVariable));
            if (supplied.Count != properties.Length)
                throw Logging.ErrorOnce(
                    new ArgumentException($"BuildVars 維度數量不一致：{className} 傳入 {supplied.Count}，變數屬性 {properties.Length}"),
                    "變數維度數量不一致", null, nameof(ValidateVariableDimensions), className,
                    "變數屬性數量不一致", $"數量={supplied.Count}/{properties.Length}");

            for (int index = 0; index < properties.Length; index++)
            {
                var (setNumber, actualType) = supplied[index];
                var expectedType = properties[index].PropertyType;
                if (actualType == expectedType) continue;
                throw Logging.ErrorOnce(
                    new ArgumentException($"BuildVars 維度型別不一致：{className}.{properties[index].Name} 預期 {expectedType.Name}，集合 #{setNumber} 傳入 {actualType.Name}"),
                    "變數維度型別不一致", null, nameof(ValidateVariableDimensions), className,
                    "集合與變數屬性型別不一致",
                    $"屬性={properties[index].Name} 序號={setNumber} 預期型別={expectedType.Name} 實際型別={actualType.Name}");
            }
        }

        // 集合每列各維的型別：集合資料列取屬性型別、ValueTuple 取各欄型別、primitive 取元素型別；判斷不出來回 null。
        // 宣告型別看不出來（object、抽象型別）時改看第一筆資料的實際型別。
        private static Type[] GetDimensionTypes(object set)
        {
            if (set == null || set is string || set is not System.Collections.IEnumerable sequence) return null;

            var tupleType = GetValueTupleElementType(set);
            if (tupleType != null) return FlattenTupleTypes(tupleType);

            var elementType = set.GetType().GetInterfaces()
                .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];
            if (elementType == null || elementType == typeof(object) || elementType.IsAbstract || elementType.IsInterface)
                elementType = sequence.Cast<object>().FirstOrDefault()?.GetType();
            if (elementType == null) return null;

            if (typeof(SetRowBase).IsAssignableFrom(elementType))
                return ModelElementBase.GetDimensions(elementType)
                    .Select(property => property.PropertyType)
                    .ToArray();
            if (elementType.IsEnum || TokenTypes.Contains(elementType)) return [elementType];
            return null;
        }

        // ValueTuple 超過 7 欄時第 8 個型別參數是巢狀的 TRest；攤平後與 ITuple 的索引順序一致
        private static Type[] FlattenTupleTypes(Type tupleType)
        {
            var arguments = tupleType.GetGenericArguments();
            return arguments.Length == 8
                ? arguments.Take(7).Concat(FlattenTupleTypes(arguments[7])).ToArray()
                : arguments;
        }

        /// <summary>
        /// 將 DateTime、int、long、double、decimal、string 或 enum 的 IEnumerable&lt;T&gt; 轉成字串列表。
        /// 浮點值使用 InvariantCulture，enum 使用成員名稱。
        /// </summary>
        public static List<string>[] ConvertSetsToTokens(params object[] sets)
        {
            if (sets == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(sets), "sets 不得為 null"),
                    "變數維度集合不合法", null, nameof(ConvertSetsToTokens), null,
                    "集合陣列為空");

            // 單一 string[] 可能被展開成 params object[]；重新包裝以維持單一維度。
            if (sets.Length > 0 && sets.All(x => x is string))
                sets = [sets.Cast<string>().ToList()];

            var result = new List<string>[sets.Length];
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i] == null)
                    throw Logging.ErrorOnce(
                        new ArgumentException($"集合 #{i + 1} 不得為 null", nameof(sets)),
                        "變數維度集合不合法", null, nameof(ConvertSetsToTokens), null,
                        "集合為空", $"序號={i + 1}");

                result[i] = sets[i] switch
                {
                    IEnumerable<DateTime> seq => seq.Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    IEnumerable<int> seq => seq.Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    IEnumerable<long> seq => seq.Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    IEnumerable<double> seq => seq.Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    IEnumerable<decimal> seq => seq.Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    IEnumerable<string> seq => seq.Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    string s => throw Logging.ErrorOnce(
                        new ArgumentException($"集合不得為單一 string '{s}'：集合與裸 string 混傳，請確認每個參數都是一個集合（IEnumerable）"),
                        "變數維度集合不合法", null, nameof(ConvertSetsToTokens), s,
                        "字串不是集合", $"序號={i + 1}"),
                    // enum 是值型別，不能用 IEnumerable<Enum> 判斷任意 enum 集合，因此另外檢查集合的元素型別。
                    System.Collections.IEnumerable seq when GetEnumElementType(sets[i]) != null
                        => seq.Cast<object>().Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    _ => throw Logging.ErrorOnce(
                        new ArgumentException($"不支援的集合型別：{sets[i].GetType().Name}，目前僅支援 IEnumerable<DateTime/int/long/double/decimal/string/enum>"),
                        "變數維度集合不合法", null, nameof(ConvertSetsToTokens), sets[i].GetType().FullName,
                        "不支援的集合型別", $"序號={i + 1}")
                };
            }
            return result;
        }

        /// <summary>若 obj 為元素型別是 enum 的 IEnumerable&lt;T&gt;，回傳該 enum 型別，否則回傳 null。</summary>
        private static Type GetEnumElementType(object obj)
        {
            foreach (var type in obj.GetType().GetInterfaces())
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    var elementType = type.GetGenericArguments()[0];
                    if (elementType.IsEnum) return elementType;
                }
            return null;
        }

        /// <summary>
        /// 直接組成 typeName@維度值名稱，不建立實例或逐筆反射；不檢查維度數量與型別。
        /// 泛型 Build*Vs 先經 <see cref="ValidateVariableClass{TVariable}"/> 把關，再以類別名當 typeName 走到這裡。
        /// </summary>
        public static IEnumerable<string> ComposeNames(string typeName, object[] sets)
        {
            var setRows = ConvertSetsToRows(sets);
            foreach (var parts in CombineRows(setRows))
                yield return ModelNaming.Compose(typeName, parts);
        }
    }
}
