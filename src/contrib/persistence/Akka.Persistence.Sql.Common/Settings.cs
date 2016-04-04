//-----------------------------------------------------------------------
// <copyright file="Settings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Persistence.Sql.Common.Journal;

namespace Akka.Persistence.Sql.Common
{
    /// <summary>
    /// Configuration settings representation targeting Sql Server journal actor.
    /// </summary>
    public class JournalSettings
    {
        /// <summary>
        /// Connection string used to access a persistent SQL Server instance.
        /// </summary>
        public string ConnectionString { get; private set; }

        /// <summary>
        /// Name of the connection string stored in &lt;connectionStrings&gt; section of *.config file.
        /// </summary>
        public string ConnectionStringName { get; private set; }

        /// <summary>
        /// Connection timeout for SQL Server related operations.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; private set; }

        /// <summary>
        /// Schema name, where table corresponding to event journal is placed.
        /// </summary>
        public string SchemaName { get; private set; }

        /// <summary>
        /// Name of the table corresponding to event journal.
        /// </summary>
        public string TableName { get; private set; }

        /// <summary>
        /// Fully qualified type name for <see cref="ITimestampProvider"/> used to generate journal timestamps.
        /// </summary>
        public string TimestampProvider { get; set; }

        public JournalSettings(Config config)
        {
            if (config == null) throw new ArgumentNullException("config", "SqlServer journal settings cannot be initialized, because required HOCON section couldn't been found");

            ConnectionString = config.GetString("connection-string");
            ConnectionStringName = config.GetString("connection-string-name");
            ConnectionTimeout = config.GetTimeSpan("connection-timeout");
            SchemaName = config.GetString("schema-name");
            TableName = config.GetString("table-name");
            TimestampProvider = config.GetString("timestamp-provider");
        }
    }

    /// <summary>
    /// Configuration settings representation targeting Sql Server snapshot store actor.
    /// </summary>
    public class SnapshotStoreSettings
    {
        /// <summary>
        /// Connection string used to access a persistent SQL Server instance.
        /// </summary>
        public string ConnectionString { get; private set; }

        /// <summary>
        /// Name of the connection string stored in &lt;connectionStrings&gt; section of *.config file.
        /// </summary>
        public string ConnectionStringName { get; private set; }

        /// <summary>
        /// Connection timeout for SQL Server related operations.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; private set; }

        /// <summary>
        /// Schema name, where table corresponding to snapshot store is placed.
        /// </summary>
        public string SchemaName { get; private set; }

        /// <summary>
        /// Name of the table corresponding to snapshot store.
        /// </summary>
        public string TableName { get; private set; }

        public SnapshotStoreSettings(Config config)
        {
            if (config == null) throw new ArgumentNullException("config", "SqlServer snapshot store settings cannot be initialized, because required HOCON section couldn't been found");

            ConnectionString = config.GetString("connection-string");
            ConnectionStringName = config.GetString("connection-string-name");
            ConnectionTimeout = config.GetTimeSpan("connection-timeout");
            SchemaName = config.GetString("schema-name");
            TableName = config.GetString("table-name");
        }
    }
}