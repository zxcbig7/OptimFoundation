using System;
using System.Linq;

namespace OptimFoundation.Core
{
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
