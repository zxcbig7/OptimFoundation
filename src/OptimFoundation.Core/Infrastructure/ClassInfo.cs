using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 把一個變數 / 參數類別的 property 反推出對應的 DB 表結構與 SQL——DB 端的表結構契約集中在這裡。
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
    /// <summary>
    /// 由一個變數 / 參數類別的 property 反推出對應的 DB 表結構與 SQL——DB 端的表結構契約集中在這裡。
    ///
    /// 慣例（建表與 INSERT 都照這套，兩邊必須一致）：
    /// - 參數表：DATA_ID + 各 property 欄 + USER_ID + TIME
    /// - 結果表：DATA_ID + VAR_TYPE + 各 property 欄 + QTY + USER_ID + TIME
    /// - 欄位順序 = property 宣告順序；表名與欄名一律大寫
    /// </summary>
    public class ClassInfo
    {
        /// <summary>被描述的類別型別。</summary>
        public Type Type { get; }

        /// <summary>類別名，同時是解 key 的前綴與 VAR_TYPE 欄的值來源。</summary>
        public string TypeName => Type.Name;

        /// <summary>各 property 名（依宣告順序），即 DB 的維度欄名。</summary>
        public string[] SetNames => ReflectionHelper.GetMemberNames(Type);

        /// <summary>各 property 的型別（順序同 <see cref="SetNames"/>），決定寫入時的資料轉型。</summary>
        public Type[] PropertyTypes => ReflectionHelper.GetMemberTypes(Type);

        /// <summary>維度欄名以逗號串接，供 INSERT 的欄位清單使用。</summary>
        public string ColNames => string.Join(", ", SetNames);

        /// <summary>對應 <see cref="ColNames"/> 的具名參數佔位符（:COL1, :COL2 …）。</summary>
        public string ParamPlaceholders => string.Join(", ", SetNames.Select(s => $":{s}"));

        /// <summary>維度欄的 DDL 片段（含前導逗號），供 CREATE TABLE 拼接。</summary>
        public string SQLColsDefinition => ReflectionHelper.GenerateSQLCols(Type);

        /// <summary>以指定型別建立描述器；本身不碰 DB，只做名稱 / 型別推導。</summary>
        public ClassInfo(Type type) { Type = type; }

        /// <summary>解結果表的 INSERT（欄位 DATA_ID, VAR_TYPE, 各維度, QTY, USER_ID），對得上 <see cref="VarTableCreateCmd"/>。</summary>
        public string VarInsertCmd(string tableName) =>
            $"INSERT INTO {tableName} (DATA_ID, VAR_TYPE, {ColNames}, QTY, USER_ID) VALUES (:DATA_ID, :VAR_TYPE, {ParamPlaceholders}, :QTY, :USER_ID)";

        /// <summary>參數表的 INSERT（欄位 DATA_ID + 各維度），對得上 <see cref="ParamTableCreateCmd"/>。</summary>
        public string ParamInsertCmd(string tableName) =>
            $"INSERT INTO {tableName} (DATA_ID, {ColNames}) VALUES (:DATA_ID, {ParamPlaceholders})";

        /// <summary>解結果表的 CREATE TABLE：DATA_ID / VAR_TYPE / 各維度 / QTY / USER_ID（預設 USER）/ TIME（預設 SYSTIMESTAMP）。</summary>
        public string VarTableCreateCmd(string tableName) =>
            $"CREATE TABLE {tableName} (DATA_ID VARCHAR2(255), VAR_TYPE VARCHAR2(255){SQLColsDefinition}, QTY NUMBER, USER_ID VARCHAR2(255) DEFAULT USER, TIME TIMESTAMP DEFAULT SYSTIMESTAMP)".ToUpper();

        /// <summary>參數表的 CREATE TABLE：DATA_ID / 各維度 / USER_ID（預設 USER）/ TIME（預設 SYSTIMESTAMP）。</summary>
        public string ParamTableCreateCmd(string tableName) =>
            $"CREATE TABLE {tableName} (DATA_ID VARCHAR2(255){SQLColsDefinition}, USER_ID VARCHAR2(255) DEFAULT USER, TIME TIMESTAMP DEFAULT SYSTIMESTAMP)".ToUpper();
    }
}
