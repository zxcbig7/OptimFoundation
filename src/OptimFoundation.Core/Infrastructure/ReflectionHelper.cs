using System;
using System.Collections.Generic; // for Dictionary
using System.Linq;
using System.Reflection;

namespace OptimFoundation.Core
{
    /// <summary>
    ///
    /// </summary>
    public static class ReflectionHelper
    {
        /// <summary>
        /// C# → Oracle 欄位型別對應（數學模型資料用）。
        /// 整數型用 NUMBER(p) 自帶 scale 0 → 保整數性；浮點型一律用無精度 NUMBER → 保數值保真。
        /// </summary>
        private static readonly Dictionary<Type, string> OracleTypeMap = new Dictionary<Type, string>
        {
            // 文字 — Set 成員、標籤
            [typeof(string)] = "VARCHAR2(255)",
            [typeof(char)] = "CHAR(1)",

            // 布林 — 二元變數 / 旗標（Oracle 資料表欄位無原生 BOOLEAN）
            [typeof(bool)] = "NUMBER(1)",

            // 整數 — index、計數、整數變數
            [typeof(byte)] = "NUMBER(3)",
            [typeof(short)] = "NUMBER(5)",
            [typeof(int)] = "NUMBER(10)",
            [typeof(long)] = "NUMBER(19)",

            // 浮點 / 連續量 — 係數、QTY、目標值（用 NUMBER 保值，避免 BINARY_DOUBLE 浮點誤差）
            [typeof(float)] = "NUMBER",
            [typeof(double)] = "NUMBER",
            [typeof(decimal)] = "NUMBER",

            // 時間
            [typeof(DateTime)] = "DATE",
        };
        /// <summary>
        /// 對class 反射取得 Type 的 public field/property 名稱與型別（不含 method）。用於 SQL 欄位定義、CSV 標頭等。
        /// </summary>
        /// <returns>成員名稱，順序為反射回傳順序（實務上等同宣告順序，全框架的欄位對位都靠它）。</returns>
        public static string[] GetMemberNames(Type type)
        {
            return type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                       .Where(m => m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property)
                       .Select(m => m.Name)
                       .ToArray();
        }

        /// <summary>取 public field / property 的型別，順序與 <see cref="GetMemberNames"/> 一一對應。</summary>
        public static Type[] GetMemberTypes(Type type)
        {
            return type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                       .Where(m => m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property)
                       .Select(m => m.MemberType == MemberTypes.Field
                           ? ((FieldInfo)m).FieldType
                           : ((PropertyInfo)m).PropertyType)
                       .ToArray();
        }

        /// <summary>
        /// 產生 CREATE TABLE 的欄位定義片段（每欄前置逗號、全大寫），供 <see cref="ClassInfo"/> 拼接建表語句。
        /// Nullable&lt;T&gt; 取底層型別、enum 以底層整數型存；對應不到 Oracle 型別的成員直接跳過（不會產生欄位）。
        /// </summary>
        public static string GenerateSQLCols(Type type)
        {
            string[] names = GetMemberNames(type);
            Type[] types = GetMemberTypes(type);
            string cols = "";
            for (int i = 0; i < types.Length; i++)
            {
                // 解開 Nullable<T>（int? / double? ...）取底層型別，否則對應會落空
                Type t = Nullable.GetUnderlyingType(types[i]) ?? types[i];

                // enum 以底層整數型存
                if (t.IsEnum) t = Enum.GetUnderlyingType(t);

                if (OracleTypeMap.TryGetValue(t, out string sqlType))
                    cols += $", {names[i]} {sqlType}";
                // 未對應型別 → 跳過（維持原行為，不產生意外欄位）
            }
            return cols.ToUpper();
        }
    }
}
