using System;
using System.Linq;

namespace OptimFoundation.Core
{
    public class ClassInfo
    {
        public Type Type { get; }
        public string TypeName => Type.Name;
        public string[] SetNames => ReflectionHelper.GetMemberNames(Type);
        public Type[] PropertyTypes => ReflectionHelper.GetMemberTypes(Type);
        public string ColNames => string.Join(", ", SetNames);
        public string ParamPlaceholders => string.Join(", ", SetNames.Select(s => $":{s}"));
        public string SQLColsDefinition => ReflectionHelper.GenerateSQLCols(Type);

        public ClassInfo(Type type) { Type = type; }

        public string VarInsertCmd(string tableName) =>
            $"INSERT INTO {tableName} (DATA_ID, VAR_TYPE, {ColNames}, QTY, USER_ID) VALUES (:DATA_ID, :VAR_TYPE, {ParamPlaceholders}, :QTY, :USER_ID)";

        public string ParamInsertCmd(string tableName) =>
            $"INSERT INTO {tableName} (DATA_ID, {ColNames}) VALUES (:DATA_ID, {ParamPlaceholders})";

        public string VarTableCreateCmd(string tableName) =>
            $"CREATE TABLE {tableName} (DATA_ID VARCHAR2(255), VAR_TYPE VARCHAR2(255){SQLColsDefinition}, QTY NUMBER, USER_ID VARCHAR2(255) DEFAULT USER, TIME TIMESTAMP DEFAULT SYSTIMESTAMP)".ToUpper();

        public string ParamTableCreateCmd(string tableName) =>
            $"CREATE TABLE {tableName} (DATA_ID VARCHAR2(255){SQLColsDefinition}, USER_ID VARCHAR2(255) DEFAULT USER, TIME TIMESTAMP DEFAULT SYSTIMESTAMP)".ToUpper();
    }
}
