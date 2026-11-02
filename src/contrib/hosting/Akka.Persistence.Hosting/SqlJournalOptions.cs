// -----------------------------------------------------------------------
//  <copyright file="SqlJournalOptions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Data;
using System.Text;
using Akka.Hosting;

#nullable enable
namespace Akka.Persistence.Hosting
{
    /// <summary>
    /// Base class for all SQL-based journal options class. If you're writing an options class for no-SQL or other kind
    /// of plugins, use <see cref="JournalOptions"/> instead.
    /// </summary>
    public abstract class SqlJournalOptions: JournalOptions
    {
        protected SqlJournalOptions(bool isDefault): base(isDefault)
        {
        }

        /// <summary>
        ///     Connection string used for database access
        /// </summary>
        public string ConnectionString { get; set; } = "";

        /// <summary>
        ///     <para>
        ///         SQL commands timeout.
        ///     </para>
        ///     <b>Default</b>: 30 seconds
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        ///     <para>
        ///         The database schema name to table corresponding with persistent journal
        ///     </para>
        ///     <b>NOTE</b> When implementing this options, override this property with a valid default schema name for
        ///     the SQL database
        /// </summary>
        public abstract string SchemaName { get; set; }

        /// <summary>
        ///     <para>
        ///         The database journal table name corresponding with persistent journal
        ///     </para>
        ///     <b>NOTE</b> When implementing this options, override this property with a valid default table name for
        ///     the SQL database
        /// </summary>
        public abstract string TableName { get; set; }

        /// <summary>
        ///     <para>
        ///         The database metadata table name corresponding with persistent journal
        ///     </para>
        ///     <b>NOTE</b> When implementing this options, override this property with a valid default metadata table
        ///     name for the SQL database
        /// </summary>
        public abstract string MetadataTableName { get; set; }

        /// <summary>
        ///     <para>
        ///         Uses the CommandBehavior.SequentialAccess when creating DB commands, providing a performance
        ///         improvement for reading large BLOBS.
        ///     </para>
        ///     <b>Default</b>: false
        /// </summary>
        public abstract bool SequentialAccess { get; set; }

        /// <summary>
        ///     <para>
        ///         The <see cref="IsolationLevel"/> of all database read query.
        ///     </para>
        ///     <para>
        ///         <see cref="IsolationLevel"/> documentation can be read
        ///         <a href="https://learn.microsoft.com/en-us/dotnet/api/system.data.isolationlevel?#fields">here</a> 
        ///     </para>
        ///     <b>NOTE</b>: This is used primarily for backward compatibility,
        ///     you leave this empty for greenfield projects.
        /// </summary>
        public abstract IsolationLevel ReadIsolationLevel { get; set; }
        
        /// <summary>
        ///     <para>
        ///         The <see cref="IsolationLevel"/> of all database write query.
        ///     </para>
        ///     <para>
        ///         <see cref="IsolationLevel"/> documentation can be read
        ///         <a href="https://learn.microsoft.com/en-us/dotnet/api/system.data.isolationlevel?#fields">here</a> 
        ///     </para>
        ///     <b>NOTE</b>: This is used primarily for backward compatibility,
        ///     you leave this empty for greenfield projects.
        /// </summary>
        public abstract IsolationLevel WriteIsolationLevel { get; set; }
        
        protected override StringBuilder Build(StringBuilder sb)
        {
            sb.AppendLine($"connection-string = {ConnectionString.ToHocon()}");
            sb.AppendLine($"connection-timeout = {ConnectionTimeout.ToHocon()}");
            sb.AppendLine($"schema-name = {SchemaName.ToHocon()}");
            sb.AppendLine($"table-name = {TableName.ToHocon()}");
            sb.AppendLine($"metadata-table-name = {MetadataTableName.ToHocon()}");
            sb.AppendLine($"sequential-access = {SequentialAccess.ToHocon()}");
            
            sb.AppendLine($"read-isolation-level = {ReadIsolationLevel.ToHocon()}");
            sb.AppendLine($"write-isolation-level = {WriteIsolationLevel.ToHocon()}");

            return base.Build(sb);
        }
    }
}