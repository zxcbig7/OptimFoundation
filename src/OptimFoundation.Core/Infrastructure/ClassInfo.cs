using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 讀取變數或參數類別的公開欄位與屬性，轉成 Oracle 欄位名稱與型別，供建表 SQL 使用。
    /// </summary>
    public static class ReflectionHelper
    {
        /// <summary>
        /// C# → Oracle 欄位型別對應（數學模型資料用）。
        /// 整數型對應 NUMBER(p)，小數位數為 0；浮點型對應未指定精度的 NUMBER。
        /// </summary>
        private static readonly Dictionary<Type, string> OracleTypeMap = new Dictionary<Type, string>
        {
            [typeof(string)] = "VARCHAR2(255)",
            [typeof(char)] = "CHAR(1)",

            [typeof(bool)] = "NUMBER(1)",

            [typeof(byte)] = "NUMBER(3)",
            [typeof(short)] = "NUMBER(5)",
            [typeof(int)] = "NUMBER(10)",
            [typeof(long)] = "NUMBER(19)",

            [typeof(float)] = "NUMBER",
            [typeof(double)] = "NUMBER",
            [typeof(decimal)] = "NUMBER",

            [typeof(DateTime)] = "DATE",
        };
        /// <summary>
        /// 取得型別的 public field/property 名稱，包含 instance 與 static 成員，不包含方法。
        /// </summary>
        /// <returns>成員名稱按 reflection 回傳順序排列，與 GetMemberTypes 回傳的型別逐項對應。</returns>
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
                Type t = Nullable.GetUnderlyingType(types[i]) ?? types[i];

                if (t.IsEnum) t = Enum.GetUnderlyingType(t);

                if (OracleTypeMap.TryGetValue(t, out string sqlType))
                    cols += $", {names[i]} {sqlType}";
            }
            return cols.ToUpper();
        }
    }
    /// <summary>
    /// 依公開欄位與屬性產生 Oracle 建表及 INSERT SQL，兩者沿用 reflection 順序。
    /// 參數表含 DATA_ID、成員欄、USER_ID、TIME；結果表另含 VAR_TYPE 與 QTY。
    /// </summary>
    public class ClassInfo
    {
        /// <summary>被描述的類別型別。</summary>
        public Type Type { get; }

        /// <summary>類別名，同時是解 key 的前綴與 VAR_TYPE 欄的值來源。</summary>
        public string TypeName => Type.Name;

        /// <summary>各 public field/property 的名稱（依 reflection 順序），用作資料庫欄名。</summary>
        public string[] SetNames => ReflectionHelper.GetMemberNames(Type);

        /// <summary>各 public field/property 的型別，順序與 <see cref="SetNames"/> 相同，用來轉換寫入值。</summary>
        public Type[] PropertyTypes => ReflectionHelper.GetMemberTypes(Type);

        /// <summary>維度欄名以逗號串接，供 INSERT 的欄位清單使用。</summary>
        public string ColNames => string.Join(", ", SetNames);

        /// <summary>對應 <see cref="ColNames"/> 的具名參數佔位符（:COL1, :COL2 …）。</summary>
        public string ParamPlaceholders => string.Join(", ", SetNames.Select(s => $":{s}"));

        /// <summary>維度欄的 DDL 片段（含前導逗號），供 CREATE TABLE 拼接。</summary>
        public string SQLColsDefinition => ReflectionHelper.GenerateSQLCols(Type);

        /// <summary>記住要處理的類別型別，供後續產生欄位名稱與 SQL；此時不連線資料庫。</summary>
        public ClassInfo(Type type) { Type = type; }

        /// <summary>解結果表的 INSERT（欄位 DATA_ID, VAR_TYPE, 各維度, QTY, USER_ID），對得上 <see cref="VarTableCreateCmd"/>。</summary>
        public string VarInsertCmd(string tableName) =>
            InsertCmd(tableName, new[] { "DATA_ID", "VAR_TYPE" }.Concat(SetNames).Concat(new[] { "QTY", "USER_ID" }));

        /// <summary>參數表的 INSERT（欄位 DATA_ID + 各維度），對得上 <see cref="ParamTableCreateCmd"/>。</summary>
        public string ParamInsertCmd(string tableName) =>
            InsertCmd(tableName, new[] { "DATA_ID" }.Concat(SetNames));

        // 逐欄 Join：零維型別沒有維度欄，不能留下「, ,」這種空欄位
        private static string InsertCmd(string tableName, IEnumerable<string> columns)
        {
            var names = columns.ToArray();
            return $"INSERT INTO {tableName} ({string.Join(", ", names)}) VALUES ({string.Join(", ", names.Select(name => $":{name}"))})";
        }

        /// <summary>解結果表的 CREATE TABLE：DATA_ID / VAR_TYPE / 各維度 / QTY / USER_ID（預設 USER）/ TIME（預設 SYSTIMESTAMP）。</summary>
        public string VarTableCreateCmd(string tableName) =>
            $"CREATE TABLE {tableName} (DATA_ID VARCHAR2(255), VAR_TYPE VARCHAR2(255){SQLColsDefinition}, QTY NUMBER, USER_ID VARCHAR2(255) DEFAULT USER, TIME TIMESTAMP DEFAULT SYSTIMESTAMP)".ToUpper();

        /// <summary>參數表的 CREATE TABLE：DATA_ID / 各維度 / USER_ID（預設 USER）/ TIME（預設 SYSTIMESTAMP）。</summary>
        public string ParamTableCreateCmd(string tableName) =>
            $"CREATE TABLE {tableName} (DATA_ID VARCHAR2(255){SQLColsDefinition}, USER_ID VARCHAR2(255) DEFAULT USER, TIME TIMESTAMP DEFAULT SYSTIMESTAMP)".ToUpper();
    }
}
