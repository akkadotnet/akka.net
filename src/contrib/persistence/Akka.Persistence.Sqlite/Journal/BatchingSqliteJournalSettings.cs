using System;
using System.Configuration;
using Akka.Configuration;

namespace Akka.Persistence.Sqlite.Journal
{
    public sealed class CircuitBreakerSettings
    {
        public static CircuitBreakerSettings Create(Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            return new CircuitBreakerSettings(
                maxFailures: config.GetInt("max-failures", 5),
                callTimeout: config.GetTimeSpan("call-timeout", TimeSpan.FromSeconds(20)),
                resetTimeout: config.GetTimeSpan("reset-timeout", TimeSpan.FromSeconds(60)));
        }

        public int MaxFailures { get; }
        public TimeSpan CallTimeout { get; }
        public TimeSpan ResetTimeout { get; }

        public CircuitBreakerSettings(int maxFailures, TimeSpan callTimeout, TimeSpan resetTimeout)
        {
            MaxFailures = maxFailures;
            CallTimeout = callTimeout;
            ResetTimeout = resetTimeout;
        }
    }

    public sealed class BatchingSqliteJournalSettings
    {
        /// <summary>
        /// Connection string used to access a persistent SQL Server instance.
        /// </summary>
        public string ConnectionString { get; private set; }

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
        /// Flag determining in in case of event journal or metadata table missing, they should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        /// <summary>
        /// Maximum number of concurrent database read/write operations.
        /// </summary>
        public int MaxConcurrentOperations { get; private set; }

        /// <summary>
        /// Maximum number of messages executed in a single read/write batch.
        /// </summary>
        public int MaxBatchSize { get; private set; }

        public CircuitBreakerSettings CircuitBreakerSettings { get; }

        public BatchingSqliteJournalSettings(string connectionString, TimeSpan connectionTimeout, string journalTableName, string schemaName, string metaTableName, bool autoInitialize, int maxConcurrentOperations, int maxBatchSize, CircuitBreakerSettings circuitBreakerSettings)
        {
            ConnectionString = connectionString;
            ConnectionTimeout = connectionTimeout;
            JournalTableName = journalTableName;
            SchemaName = schemaName;
            MetaTableName = metaTableName;
            AutoInitialize = autoInitialize;
            MaxConcurrentOperations = maxConcurrentOperations;
            MaxBatchSize = maxBatchSize;
            CircuitBreakerSettings = circuitBreakerSettings;
        }

        public static BatchingSqliteJournalSettings Create(Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config), "Sql journal settings cannot be initialized, because required HOCON section couldn't been found");

            var connectionString = config.GetString("connection-string");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = ConfigurationManager
                    .ConnectionStrings[config.GetString("connection-string-name", "DefaultConnection")]
                    .ConnectionString;
            }

            if(string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("No connection string for Sql Event Journal was specified");

            return new BatchingSqliteJournalSettings(
                connectionString: connectionString,
                connectionTimeout: config.GetTimeSpan("connection-timeout", TimeSpan.FromSeconds(30)),
                schemaName: config.GetString("schema-name"),
                journalTableName: config.GetString("table-name"),
                metaTableName: config.GetString("metadata-table-name"),
                autoInitialize: config.GetBoolean("auto-initialize", false),
                maxConcurrentOperations: config.GetInt("max-concurrent-operations", 64),
                maxBatchSize: config.GetInt("max-batch-size", 100),
                circuitBreakerSettings: CircuitBreakerSettings.Create(config.GetConfig("circuit-breaker")));
        }
    }
}