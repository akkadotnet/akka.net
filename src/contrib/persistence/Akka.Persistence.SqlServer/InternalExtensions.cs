using System;
using System.Data.SqlClient;

namespace Akka.Persistence.SqlServer
{
    internal static class InternalExtensions
    {
        public static string QualifiedTypeName(this Type type)
        {
            return type.FullName + ", " + type.Assembly.GetName().Name;
        }

        public static string QuoteSchemaAndTable(this string sqlQuery, string schemaName, string tableName)
        {
            var cb = new SqlCommandBuilder();
            return string.Format(sqlQuery, cb.QuoteIdentifier(schemaName), cb.QuoteIdentifier(tableName));
        }
    }
}