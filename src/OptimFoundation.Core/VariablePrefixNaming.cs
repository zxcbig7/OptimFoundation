using System;

namespace OptimFoundation.Internal
{
    /// <summary>
    /// Variable 類別名前綴的唯一解析規則。
    /// 此檔也以 linked source 編入 Generator，避免編譯期與執行期規則漂移。
    /// 僅回傳既有 OptimFoundation.Core.VarType 的成員名稱，不另行定義變數型別。
    /// </summary>
    internal static class VariablePrefixNaming
    {
        internal const string ContinuousTypeName = "Continuous";
        internal const string IntegerTypeName = "Integer";
        internal const string BinaryTypeName = "Binary";

        internal const string NamingGuide =
            "VariableB_<語意>（Binary）/ VariableC_<語意>（Continuous）/ " +
            "VariableI_<語意>（Integer）";

        internal static bool TryResolve(string className, out string typeName)
        {
            if (className != null)
            {
                if (className.StartsWith("VariableB_", StringComparison.Ordinal))
                {
                    typeName = BinaryTypeName;
                    return true;
                }

                if (className.StartsWith("VariableC_", StringComparison.Ordinal))
                {
                    typeName = ContinuousTypeName;
                    return true;
                }

                if (className.StartsWith("VariableI_", StringComparison.Ordinal))
                {
                    typeName = IntegerTypeName;
                    return true;
                }
            }

            typeName = string.Empty;
            return false;
        }
    }
}
