using System;

namespace OptimFoundation.Internal
{
    /// <summary>
    /// 依 Variable 類別名的 B/C/I 前綴判定 Binary、Continuous 或 Integer 型別。
    /// Generator 也編譯同一份檔案，讓產生程式碼時與執行時使用相同規則。
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
                // 舊版 X/Y 前綴先轉成 C/I，讓既有模型仍能使用。
                if (className.StartsWith("VariableX_", StringComparison.Ordinal)) className = className.Replace("VariableX_", "VariableC_");
                if (className.StartsWith("VariableY_", StringComparison.Ordinal)) className = className.Replace("VariableY_", "VariableI_");

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
