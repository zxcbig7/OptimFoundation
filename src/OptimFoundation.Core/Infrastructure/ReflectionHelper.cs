using System;
using System.Linq;
using System.Reflection;

namespace OptimFoundation.Core
{ 
    /// <summary>
    /// 處理 Class 的資訊
    /// </summary>
    public static class ReflectionHelper
    {
        public static string[] GetMemberNames(Type type)
        {
            return type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                       .Where(m => m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property)
                       .Select(m => m.Name)
                       .ToArray();
        }

        public static Type[] GetMemberTypes(Type type)
        {
            return type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                       .Where(m => m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property)
                       .Select(m => m.MemberType == MemberTypes.Field
                           ? ((FieldInfo)m).FieldType
                           : ((PropertyInfo)m).PropertyType)
                       .ToArray();
        }

        public static string GenerateSQLCols(Type type)
        {
            string[] names = GetMemberNames(type);
            Type[]   types = GetMemberTypes(type);
            string cols = "";
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] == typeof(string))        cols += $", {names[i]} VARCHAR2(255)";
                else if (types[i] == typeof(double))   cols += $", {names[i]} NUMBER";
                else if (types[i] == typeof(int))      cols += $", {names[i]} NUMBER";
                else if (types[i] == typeof(DateTime)) cols += $", {names[i]} DATE";
            }
            return cols.ToUpper();
        }
    }
}
