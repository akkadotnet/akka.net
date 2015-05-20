using System;
using System.Data.SqlClient;
using Npgsql;

namespace Akka.Persistence.PostgreSql
{
    internal static class InternalExtensions
    {
        public static string QuoteSchemaAndTable(this string sqlQuery, string schemaName, string tableName)
        {
            var cb = new NpgsqlCommandBuilder();
            return string.Format(sqlQuery, cb.QuoteIdentifier(schemaName), cb.QuoteIdentifier(tableName));
        }
    }
}