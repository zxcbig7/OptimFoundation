using System;

namespace OptimFoundation.Internal
{
    /// <summary>
    /// 依 B/C/I 前綴回傳 VarType 成員名稱；與 Generator 共用同一份規則。
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
                // X/Y 為 C/I 的相容別名。
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
