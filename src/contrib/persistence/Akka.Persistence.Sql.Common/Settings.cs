//-----------------------------------------------------------------------
// <copyright file="Settings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// Name of the table corresponding to event journal.
        /// </summary>
        public string JournalTableName { get; private set; }

        /// <summary>
        /// Name of the schema, where journal table resides.
        /// </summary>
        public string SchemaName { get; private set; }

        /// <summary>
        /// Name of the table corresponding to event journal persistenceId and sequenceNr metadata.
        /// </summary>
        public string MetaTableName { get; private set; }

        /// <summary>
        /// Fully qualified type name for <see cref="ITimestampProvider"/> used to generate journal timestamps.
        /// </summary>
        public string TimestampProvider { get; set; }

        /// <summary>
        /// Flag determining in in case of event journal or metadata table missing, they should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JournalSettings"/> class.
        /// </summary>
        /// <param name="config">The configuration used to configure the settings.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="config"/> is undefined.
        /// </exception>
        public JournalSettings(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<JournalSettings>();

            ConnectionString = config.GetString("connection-string");
            ConnectionStringName = config.GetString("connection-string-name");
            ConnectionTimeout = config.GetTimeSpan("connection-timeout");
            SchemaName = config.GetString("schema-name", null);
            JournalTableName = config.GetString("table-name");
            MetaTableName = config.GetString("metadata-table-name");
            TimestampProvider = config.GetString("timestamp-provider");
            AutoInitialize = config.GetBoolean("auto-initialize");
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

        /// <summary>
        /// Flag determining in in case of snapshot store table missing, they should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        /// <summary>
        /// The default serializer being used if no type match override is specified
        /// </summary>
        public string DefaultSerializer { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotStoreSettings"/> class.
        /// </summary>
        /// <param name="config">The configuration used to configure the settings.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="config"/> is undefined.
        /// </exception>
        public SnapshotStoreSettings(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<SnapshotStoreSettings>();

            ConnectionString = config.GetString("connection-string");
            ConnectionStringName = config.GetString("connection-string-name");
            ConnectionTimeout = config.GetTimeSpan("connection-timeout");
            SchemaName = config.GetString("schema-name", null);
            TableName = config.GetString("table-name");
            AutoInitialize = config.GetBoolean("auto-initialize");
            DefaultSerializer = config.GetString("serializer", null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public string FullTableName => string.IsNullOrEmpty(SchemaName) ? TableName : SchemaName + "." + TableName;
    }
}
