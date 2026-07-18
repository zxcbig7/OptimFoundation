using System;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// set 名慣例的單一真相：名稱可定址來源（CSV / InMemory）的使用端一律用「檔名形式」Set_{X}
    /// （與磁碟上 Set_{X}.csv、Set 積木類名一致），各來源在邊界轉成自己的位址——不再各行其是。
    ///   CSV      → File(name)    讀 Data/{Set_X}.csv
    ///   InMemory → Logical(name) 當註冊 key
    /// 兩形式（"Product" / "Set_Product"）經此一律等價。（DB 是 query-only，set 直接明寫 SQL，不經本類。）
    /// </summary>
    internal static class SetNaming
    {
        private const string Prefix = "Set_";

        /// <summary>去 Set_ 前綴的邏輯名（"Set_Product" → "Product"，"Product" → "Product"）。</summary>
        public static string Logical(string name)
            => name != null && name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(Prefix.Length) : name;

        /// <summary>補 Set_ 前綴的檔名形式（"Product" → "Set_Product"，"Set_Product" → "Set_Product"）。</summary>
        public static string File(string name)
            => name != null && name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                ? name : Prefix + name;
    }
}
