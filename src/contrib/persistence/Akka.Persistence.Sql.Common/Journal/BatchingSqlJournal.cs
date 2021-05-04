//-----------------------------------------------------------------------
// <copyright file="BatchingSqlJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Pattern;
using Akka.Persistence.Journal;
using Akka.Serialization;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Persistence.Sql.Common.Journal
{
    /// <summary>
    /// Settings used for managing filter rules during event replay.
    /// </summary>
    public sealed class ReplayFilterSettings
    {
        /// <summary>
        /// Mode used when detecting invalid events.
        /// </summary>
        public readonly ReplayFilterMode Mode;

        /// <summary>
        /// Size (in number of events) of the look ahead buffer used for analyzing the events.
        /// </summary>
        public readonly int WindowSize;

        /// <summary>
        /// Maximum number of writerUuid to remember.
        /// </summary>
        public readonly int MaxOldWriters;

        /// <summary>
        /// Determine if the debug logging is enabled for each replayed event.
        /// </summary>
        public readonly bool IsDebug;

        /// <summary>
        /// Determine if the replay filter feature is enabled
        /// </summary>
        public bool IsEnabled => Mode != ReplayFilterMode.Disabled;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReplayFilterSettings" /> class.
        /// </summary>
        /// <param name="config">The configuration used to configure the replay filter.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when an invalid <c>replay-filter.mode</c> is read from the specified <paramref name="config"/>.
        /// Acceptable <c>replay-filter.mode</c> values include: off | repair-by-discard-old | fail | warn
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="config"/> is undefined.
        /// </exception>
        public ReplayFilterSettings(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ReplayFilterSettings>();

            ReplayFilterMode mode;
            var replayModeString = config.GetString("mode", "off");
            switch (replayModeString)
            {
                case "off": mode = ReplayFilterMode.Disabled; break;
                case "repair-by-discard-old": mode = ReplayFilterMode.RepairByDiscardOld; break;
                case "fail": mode = ReplayFilterMode.Fail; break;
                case "warn": mode = ReplayFilterMode.Warn; break;
                default: throw new ConfigurationException($"Invalid replay-filter.mode [{replayModeString}], supported values [off, repair-by-discard-old, fail, warn]");
            }

            Mode = mode;
            WindowSize = config.GetInt("window-size", 100);
            MaxOldWriters = config.GetInt("max-old-writers", 10);
            IsDebug = config.GetBoolean("debug", false);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReplayFilterSettings" /> class.
        /// </summary>
        /// <param name="mode">The mode used when detecting invalid events.</param>
        /// <param name="windowSize">The size of the replay filter's buffer.</param>
        /// <param name="maxOldWriters">The maximum number of writerUuid to remember.</param>
        /// <param name="isDebug">If set to <c>true</c>, debug logging is enabled for each replayed event.</param>
        public ReplayFilterSettings(ReplayFilterMode mode, int windowSize, int maxOldWriters, bool isDebug)
        {
            Mode = mode;
            WindowSize = windowSize;
            MaxOldWriters = maxOldWriters;
            IsDebug = isDebug;
        }
    }

    /// <summary>
    /// Settings used by <see cref="CircuitBreaker"/> used internally by
    /// the batching journal when executing event batches.
    /// </summary>
    public sealed class CircuitBreakerSettings
    {
        /// <summary>
        /// Maximum number of failures that can happen before the circuit opens.
        /// </summary>
        public int MaxFailures { get; }

        /// <summary>
        /// Maximum time available for operation to execute before 
        /// <see cref="CircuitBreaker"/> considers it a failure.
        /// </summary>
        public TimeSpan CallTimeout { get; }

        /// <summary>
        /// Timeout that has to pass before <see cref="CircuitBreaker"/>
        /// moves into half-closed state, trying to eventually close 
        /// after sampling an operation.
        /// </summary>
        public TimeSpan ResetTimeout { get; }

        /// <summary>
        /// Creates a new instance of the <see cref="CircuitBreakerSettings"/> from provided HOCON <paramref name="config"/>.
        /// </summary>
        /// <param name="config">The configuration used to configure the circuit breaker.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="config"/> is undefined.
        /// </exception>
        public CircuitBreakerSettings(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<CircuitBreakerSettings>();

            MaxFailures = config.GetInt("max-failures", 5);
            CallTimeout = config.GetTimeSpan("call-timeout", TimeSpan.FromSeconds(20));
            ResetTimeout = config.GetTimeSpan("reset-timeout", TimeSpan.FromSeconds(60));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CircuitBreakerSettings" /> class.
        /// </summary>
        /// <param name="maxFailures">The maximum number of failures that can happen before the circuit opens.</param>
        /// <param name="callTimeout">
        /// The maximum time available for operation to execute before <see cref="CircuitBreaker"/> considers it a failure.
        /// </param>
        /// <param name="resetTimeout">
        /// The amount of time before <see cref="CircuitBreaker"/> moves into the half-closed state.
        /// </param>
        public CircuitBreakerSettings(int maxFailures, TimeSpan callTimeout, TimeSpan resetTimeout)
        {
            MaxFailures = maxFailures;
            CallTimeout = callTimeout;
            ResetTimeout = resetTimeout;
        }
    }

    /// <summary>
    /// All settings that can be used by implementations of 
    /// <see cref="BatchingSqlJournal{TConnection,TCommand}"/>.
    /// </summary>
    public abstract class BatchingSqlJournalSetup
    {
        /// <summary>
        /// Connection string to a SQL database.
        /// </summary>
        public string ConnectionString { get; }

        /// <summary>
        /// Maximum number of batch operations allowed to be executed at the same time.
        /// Each batch operation must acquire a <see cref="DbConnection"/>, so this setting
        /// can be effectively used to limit the usage of ADO.NET connection pool by current journal.
        /// </summary>
        public int MaxConcurrentOperations { get; }

        /// <summary>
        /// Maximum size of single batch of operations to be executed over a single <see cref="DbConnection"/>.
        /// </summary>
        public int MaxBatchSize { get; }

        /// <summary>
        /// Maximum size of requests stored in journal buffer. Once buffer will be surpassed, it will start
        /// to apply <see cref="BatchingSqlJournal{TConnection,TCommand}.OnBufferOverflow"/> method to incoming requests.
        /// </summary>
        public int MaxBufferSize { get; }

        /// <summary>
        /// If true, once created, journal will run all SQL scripts stored under 
        /// <see cref="BatchingSqlJournal{TConnection,TCommand}.Initializers"/> collection
        /// prior to starting executing any requests. In most implementation this is used 
        /// to initialize necessary tables.
        /// </summary>
        public bool AutoInitialize { get; }

        /// <summary>
        /// Maximum time given for executed <see cref="DbCommand"/> to complete.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; }

        /// <summary>
        /// Isolation level of transactions used during query execution.
        /// </summary>
        public IsolationLevel IsolationLevel { get; }

        /// <summary>
        /// Settings specific to <see cref="CircuitBreaker"/>, which is used internally 
        /// for executing request batches.
        /// </summary>
        public CircuitBreakerSettings CircuitBreakerSettings { get; }

        /// <summary>
        /// Settings specific to replay filter rules used when replaying events from database
        /// back to the persistent actors.
        /// </summary>
        public ReplayFilterSettings ReplayFilterSettings { get; }

        /// <summary>
        /// Database specific naming conventions (table and column names) used to construct valid SQL statements.
        /// </summary>
        public QueryConfiguration NamingConventions { get; }

        /// <summary>
        /// The default serializer used when not type override matching is found
        /// </summary>
        public string DefaultSerializer { get; } 

        /// <summary>
        /// Initializes a new instance of the <see cref="BatchingSqlJournalSetup" /> class.
        /// </summary>
        /// <param name="config">The configuration used to configure the journal.</param>
        /// <param name="namingConventions">The naming conventions used by the database to construct valid SQL statements.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown for a couple of reasons.
        /// <ul>
        /// <li>A connection string for the SQL event journal was not specified.</li>
        /// <li>
        /// An unknown <c>isolation-level</c> value was specified. Acceptable <c>isolation-level</c> values include:
        /// chaos | read-committed | read-uncommitted | repeatable-read | serializable | snapshot | unspecified
        /// </li>
        /// </ul>
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="config"/> is undefined.
        /// </exception>
        protected BatchingSqlJournalSetup(Config config, QueryConfiguration namingConventions)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<BatchingSqlJournalSetup>();

            var connectionString = config.GetString("connection-string", null);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = System.Configuration.ConfigurationManager
                    .ConnectionStrings[config.GetString("connection-string-name", "DefaultConnection")]?
                    .ConnectionString;
            }

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ConfigurationException("No connection string for Sql Event Journal was specified");

            IsolationLevel level;
            switch (config.GetString("isolation-level", "unspecified"))
            {
                case "chaos": level = IsolationLevel.Chaos; break;
                case "read-committed": level = IsolationLevel.ReadCommitted; break;
                case "read-uncommitted": level = IsolationLevel.ReadUncommitted; break;
                case "repeatable-read": level = IsolationLevel.RepeatableRead; break;
                case "serializable": level = IsolationLevel.Serializable; break;
                case "snapshot": level = IsolationLevel.Snapshot; break;
                case "unspecified": level = IsolationLevel.Unspecified; break;
                default: throw new ConfigurationException("Unknown isolation-level value. Should be one of: chaos | read-committed | read-uncommitted | repeatable-read | serializable | snapshot | unspecified");
            }

            ConnectionString = connectionString;
            MaxConcurrentOperations = config.GetInt("max-concurrent-operations", 64);
            MaxBatchSize = config.GetInt("max-batch-size", 100);
            MaxBufferSize = config.GetInt("max-buffer-size", 500000);
            AutoInitialize = config.GetBoolean("auto-initialize", false);
            ConnectionTimeout = config.GetTimeSpan("connection-timeout", TimeSpan.FromSeconds(30));
            IsolationLevel = level;
            CircuitBreakerSettings = new CircuitBreakerSettings(config.GetConfig("circuit-breaker"));
            ReplayFilterSettings = new ReplayFilterSettings(config.GetConfig("replay-filter"));
            NamingConventions = namingConventions;
            DefaultSerializer = config.GetString("serializer", null);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BatchingSqlJournalSetup" /> class.
        /// </summary>
        /// <param name="connectionString">The connection string used to connect to the database.</param>
        /// <param name="maxConcurrentOperations">The maximum number of batch operations allowed to be executed at the same time.</param>
        /// <param name="maxBatchSize">The maximum size of single batch of operations to be executed over a single <see cref="DbConnection"/>.</param>
        /// <param name="maxBufferSize">The maximum size of requests stored in journal buffer.</param>
        /// <param name="autoInitialize">
        /// If set to <c>true</c>, the journal executes all SQL scripts stored under the
        /// <see cref="BatchingSqlJournal{TConnection,TCommand}.Initializers"/> collection prior
        /// to starting executing any requests.
        /// </param>
        /// <param name="connectionTimeout">The maximum time given for executed <see cref="DbCommand"/> to complete.</param>
        /// <param name="isolationLevel">The isolation level of transactions used during query execution.</param>
        /// <param name="circuitBreakerSettings">
        /// The settings used by the <see cref="CircuitBreaker"/> when for executing request batches.
        /// </param>
        /// <param name="replayFilterSettings">The settings used when replaying events from database back to the persistent actors.</param>
        /// <param name="namingConventions">The naming conventions used by the database to construct valid SQL statements.</param>
        /// <param name="defaultSerializer">The serializer used when no specific type matching can be found.</param>
        protected BatchingSqlJournalSetup(string connectionString, int maxConcurrentOperations, int maxBatchSize, int maxBufferSize, bool autoInitialize, TimeSpan connectionTimeout, IsolationLevel isolationLevel, CircuitBreakerSettings circuitBreakerSettings, ReplayFilterSettings replayFilterSettings, QueryConfiguration namingConventions, string defaultSerializer)
        {
            ConnectionString = connectionString;
            MaxConcurrentOperations = maxConcurrentOperations;
            MaxBatchSize = maxBatchSize;
            MaxBufferSize = maxBufferSize;
            AutoInitialize = autoInitialize;
            ConnectionTimeout = connectionTimeout;
            IsolationLevel = isolationLevel;
            CircuitBreakerSettings = circuitBreakerSettings;
            ReplayFilterSettings = replayFilterSettings;
            NamingConventions = namingConventions;
            DefaultSerializer = defaultSerializer;
        }
    }

    /// <summary>
    /// An abstract journal used by <see cref="PersistentActor"/>s to read/write events to a database.
    /// 
    /// This implementation uses horizontal batching to recycle usage of the <see cref="DbConnection"/> 
    /// and to optimize writes made to a database. Batching journal is not going to acquire a new DB
    /// connection on every request. Instead it will batch incoming requests and execute them only when
    /// a previous operation batch has been completed. This means that requests coming from many 
    /// actors at the same time will be executed in one batch.
    /// 
    /// Maximum number of batches executed at the same time is defined by 
    /// <see cref="BatchingSqlJournalSetup.MaxConcurrentOperations"/> setting, while max allowed batch
    /// size is defined by <see cref="BatchingSqlJournalSetup.MaxBatchSize"/> setting.
    /// 
    /// Batching journal also defines <see cref="BatchingSqlJournalSetup.MaxBufferSize"/>, which defines
    /// a maximum number of all requests stored at once in memory. Once that value is surpassed, journal
    /// will start to apply <see cref="OnBufferOverflow"/> logic on each incoming requests, until a
    /// buffer gets freed again. This may be used for overflow strategies, request denials or backpressure.
    /// </summary>
    /// <typeparam name="TConnection">A concrete implementation of <see cref="DbConnection"/> for targeted database provider.</typeparam>
    /// <typeparam name="TCommand">A concrete implementation of <see cref="DbCommand"/> for targeted database provider.</typeparam>
    public abstract class BatchingSqlJournal<TConnection, TCommand> : WriteJournalBase
        where TConnection : DbConnection
        where TCommand : DbCommand
    {
        #region internal classes

        private sealed class ChunkExecutionFailure : IDeadLetterSuppression
        {
            public Exception Cause { get; }
            public IJournalRequest[] Requests { get; }
            public int ChunkId { get; }

            public ChunkExecutionFailure(Exception cause, IJournalRequest[] requests, int chunkId)
            {
                Cause = cause;
                Requests = requests;
                ChunkId = chunkId;
            }
        }

        private sealed class BatchComplete
        {
            public readonly int ChunkId;
            public readonly int OperationCount;
            public readonly TimeSpan TimeSpent;

            public BatchComplete(int chunkId, int operationCount, TimeSpan timeSpent)
            {
                ChunkId = chunkId;
                TimeSpent = timeSpent;
                OperationCount = operationCount;
            }
        }

        private struct RequestChunk
        {
            public readonly int ChunkId;
            public readonly IJournalRequest[] Requests;

            public RequestChunk(int chunkId, IJournalRequest[] requests)
            {
                ChunkId = chunkId;
                Requests = requests;
            }
        }

        #endregion

        /// <summary>
        /// Default index of <see cref="IPersistentRepresentation.PersistenceId"/> 
        /// column get from <see cref="ByPersistenceIdSql"/> query.
        /// </summary>
        protected const int PersistenceIdIndex = 0;

        /// <summary>
        /// Default index of <see cref="IPersistentRepresentation.SequenceNr"/> 
        /// column get from <see cref="ByPersistenceIdSql"/> query.
        /// </summary>
        protected const int SequenceNrIndex = 1;

        /// <summary>
        /// Default index of <see cref="IPersistentRepresentation.Timestamp"/> 
        /// column get from <see cref="ByPersistenceIdSql"/> query.
        /// </summary>
        protected const int TimestampIndex = 2;

        /// <summary>
        /// Default index of <see cref="IPersistentRepresentation.IsDeleted"/> 
        /// column get from <see cref="ByPersistenceIdSql"/> query.
        /// </summary>
        protected const int IsDeletedIndex = 3;

        /// <summary>
        /// Default index of <see cref="IPersistentRepresentation.Manifest"/> 
        /// column get from <see cref="ByPersistenceIdSql"/> query.
        /// </summary>
        protected const int ManifestIndex = 4;

        /// <summary>
        /// Default index of <see cref="IPersistentRepresentation.Payload"/> 
        /// column get from <see cref="ByPersistenceIdSql"/> query.
        /// </summary>
        protected const int PayloadIndex = 5;

        /// <summary>
        /// Default index of <see cref="Serializer.Identifier"/>
        /// </summary>
        protected const int SerializerIdIndex = 6;

        /// <summary>
        /// Default index of tags column get from <see cref="ByTagSql"/> query.
        /// </summary>
        protected const int OrderingIndex = 7;

        /// <summary>
        /// SQL query executed as result of <see cref="DeleteMessagesTo"/> request to journal.
        /// </summary>
        protected virtual string DeleteBatchSql { get; }

        /// <summary>
        /// SQL query executed as result of <see cref="ReadHighestSequenceNr"/> request to journal.
        /// Also used under some conditions, when storing metadata upon <see cref="DeleteMessagesTo"/> request.
        /// </summary>
        protected virtual string HighestSequenceNrSql { get; }

        /// <summary>
        /// SQL statement executed as result of <see cref="WriteMessages"/> request to journal.
        /// </summary>
        protected virtual string InsertEventSql { get; }

        /// <summary>
        /// SQL query executed as result of <see cref="SelectCurrentPersistenceIds"/> request to journal.
        /// It's a part of persistence query protocol.
        /// </summary>
        protected virtual string AllPersistenceIdsSql { get; }

        /// <summary>
        /// SQL statement executed as result of writing metadata, which is 
        /// a possible effect of <see cref="DeleteMessagesTo"/> request.
        /// </summary>
        protected virtual string UpdateSequenceNrSql { get; }

        /// <summary>
        /// SQL query executed as result of <see cref="ReplayMessages"/> request to journal.
        /// It's also part of persistence query protocol.
        /// </summary>
        protected virtual string ByPersistenceIdSql { get; }

        /// <summary>
        /// SQL query executed as result of <see cref="ReplayTaggedMessages"/> request to journal.
        /// It's a part of persistence query protocol.
        /// </summary>
        protected virtual string ByTagSql { get; }

        /// <summary>
        /// SQL query executed as result of <see cref="ReplayAllEvents"/> request to journal.
        /// It's a part of persistence query protocol.
        /// </summary>
        protected virtual string AllEventsSql { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string HighestOrderingSql { get; }

        /// <summary>
        /// A named collection of SQL statements to be executed once journal actor gets initialized
        /// and the <see cref="BatchingSqlJournalSetup.AutoInitialize"/> flag is set.
        /// </summary>
        protected abstract ImmutableDictionary<string, string> Initializers { get; }

        /// <summary>
        /// All configurable settings defined for a current batching journal.
        /// </summary>
        protected BatchingSqlJournalSetup Setup { get; }

        /// <summary>
        /// Flag determining if current journal has any subscribers for <see cref="EventAppended"/> events.
        /// </summary>
        protected bool HasPersistenceIdSubscribers => _persistenceIdSubscribers.Count != 0;

        /// <summary>
        /// Flag determining if current journal has any subscribers for <see cref="TaggedEventAppended"/> events.
        /// </summary>
        protected bool HasTagSubscribers => _tagSubscribers.Count != 0;

        /// <summary>
        /// Flag determining if current journal has any subscribers for <see cref="NewEventAppended"/> and 
        /// </summary>
        protected bool HasNewEventsSubscribers => _newEventSubscriber.Count != 0;

        /// <summary>
        /// Flag determining if incoming journal requests should be published in current actor system event stream.
        /// Useful mostly for tests.
        /// </summary>
        protected readonly bool CanPublish;

        /// <summary>
        /// Logging adapter for current journal actor .
        /// </summary>
        protected readonly ILoggingAdapter Log;

        private readonly Queue<(IJournalRequest request, long id)>[] _buffers;

        /// <summary>
        /// Buffer for write requests that are waiting to be served when next DB connection will be released.
        /// </summary>
        private Queue<(IJournalRequest request, long id)> WriteBuffer => _buffers[0];

        /// <summary>
        /// Buffer for read requests that are waiting to be served when next DB connection will be released.
        /// </summary>
        private Queue<(IJournalRequest request, long id)> ReadBuffer => _buffers[1];

        private readonly AtomicCounterLong _bufferIdCounter;

        private readonly Dictionary<string, HashSet<IActorRef>> _persistenceIdSubscribers;
        private readonly Dictionary<string, HashSet<IActorRef>> _tagSubscribers;
        private readonly HashSet<IActorRef> _newEventSubscriber;

        private readonly Akka.Serialization.Serialization _serialization;
        private readonly CircuitBreaker _circuitBreaker;
        private int _remainingOperations;

        /// <summary>
        /// Initializes a new instance of the <see cref="BatchingSqlJournal{TConnection, TCommand}" /> class.
        /// </summary>
        /// <param name="setup">The settings used to configure the journal.</param>
        protected BatchingSqlJournal(BatchingSqlJournalSetup setup)
        {
            Setup = setup;
            CanPublish = Persistence.Instance.Apply(Context.System).Settings.Internal.PublishPluginCommands;

            _persistenceIdSubscribers = new Dictionary<string, HashSet<IActorRef>>();
            _tagSubscribers = new Dictionary<string, HashSet<IActorRef>>();
            _newEventSubscriber = new HashSet<IActorRef>();

            _remainingOperations = Setup.MaxConcurrentOperations;
            _buffers = new[]
            {
                new Queue<(IJournalRequest, long)>(Setup.MaxBatchSize),
                new Queue<(IJournalRequest, long)>(Setup.MaxBatchSize)
            };
            _bufferIdCounter = new AtomicCounterLong(0);

            _serialization = Context.System.Serialization;
            Log = Context.GetLogger();
            _circuitBreaker = CircuitBreaker.Create(
                Context.System.Scheduler,
                maxFailures: Setup.CircuitBreakerSettings.MaxFailures,
                callTimeout: Setup.CircuitBreakerSettings.CallTimeout,
                resetTimeout: Setup.CircuitBreakerSettings.ResetTimeout);

            var conventions = Setup.NamingConventions;

            var allEventColumnNames = $@"
                e.{conventions.PersistenceIdColumnName} as PersistenceId, 
                e.{conventions.SequenceNrColumnName} as SequenceNr, 
                e.{conventions.TimestampColumnName} as Timestamp, 
                e.{conventions.IsDeletedColumnName} as IsDeleted, 
                e.{conventions.ManifestColumnName} as Manifest, 
                e.{conventions.PayloadColumnName} as Payload,
                e.{conventions.SerializerIdColumnName} as SerializerId";

            AllPersistenceIdsSql = $@"
                SELECT DISTINCT u.Id as PersistenceId 
                FROM (
                    SELECT DISTINCT e.{conventions.PersistenceIdColumnName} as Id 
                    FROM {conventions.FullJournalTableName} e
                    WHERE e.{conventions.OrderingColumnName} > @Ordering
                    UNION
                    SELECT DISTINCT e.{conventions.PersistenceIdColumnName} as Id 
                    FROM {conventions.FullMetaTableName} e
                ) as u";

            HighestSequenceNrSql = $@"
                SELECT MAX(u.SeqNr) as SequenceNr 
                FROM (
                    SELECT e.{conventions.SequenceNrColumnName} as SeqNr FROM {conventions.FullJournalTableName} e WHERE e.{conventions.PersistenceIdColumnName} = @PersistenceId
                    UNION
                    SELECT m.{conventions.SequenceNrColumnName} as SeqNr FROM {conventions.FullMetaTableName} m WHERE m.{conventions.PersistenceIdColumnName} = @PersistenceId) as u";

            DeleteBatchSql = $@"
                DELETE FROM {conventions.FullJournalTableName}
                WHERE {conventions.PersistenceIdColumnName} = @PersistenceId AND {conventions.SequenceNrColumnName} <= @ToSequenceNr;
                DELETE FROM {conventions.FullMetaTableName}
                WHERE {conventions.PersistenceIdColumnName} = @PersistenceId AND {conventions.SequenceNrColumnName} <= @ToSequenceNr;";

            UpdateSequenceNrSql = $@"
                INSERT INTO {conventions.FullMetaTableName} ({conventions.PersistenceIdColumnName}, {conventions.SequenceNrColumnName}) 
                VALUES (@PersistenceId, @SequenceNr);";

            ByPersistenceIdSql =
                $@"
                SELECT {allEventColumnNames}
                FROM {conventions.FullJournalTableName} e
                WHERE e.{conventions.PersistenceIdColumnName} = @PersistenceId
                AND e.{conventions.SequenceNrColumnName} BETWEEN @FromSequenceNr AND @ToSequenceNr
                ORDER BY e.{conventions.SequenceNrColumnName} ASC;";

            ByTagSql =
                $@"
                SELECT {allEventColumnNames}, e.{conventions.OrderingColumnName} as Ordering
                FROM {conventions.FullJournalTableName} e
                WHERE e.{conventions.OrderingColumnName} > @Ordering AND e.{conventions.TagsColumnName} LIKE @Tag
                ORDER BY {conventions.OrderingColumnName} ASC";

            AllEventsSql =
                $@"
                SELECT {allEventColumnNames}, e.{conventions.OrderingColumnName} as Ordering
                FROM {conventions.FullJournalTableName} e
                WHERE e.{conventions.OrderingColumnName} > @Ordering
                ORDER BY {conventions.OrderingColumnName} ASC";

            HighestOrderingSql =
                $@"
                SELECT MAX(e.{conventions.OrderingColumnName}) as Ordering
                FROM {conventions.FullJournalTableName} e";

            InsertEventSql = $@"
                INSERT INTO {conventions.FullJournalTableName} (
                    {conventions.PersistenceIdColumnName},
                    {conventions.SequenceNrColumnName},
                    {conventions.TimestampColumnName},
                    {conventions.IsDeletedColumnName},
                    {conventions.ManifestColumnName},
                    {conventions.PayloadColumnName},
                    {conventions.TagsColumnName},
                    {conventions.SerializerIdColumnName}
                ) VALUES (
                    @PersistenceId, 
                    @SequenceNr,
                    @Timestamp,
                    @IsDeleted,
                    @Manifest,
                    @Payload,
                    @Tag,
                    @SerializerId
                )";
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            if (Setup.AutoInitialize)
            {
                using (var connection = CreateConnection(Setup.ConnectionString))
                using (var command = connection.CreateCommand())
                {
                    connection.Open();

                    foreach (var entry in Initializers)
                    {
                        Log.Debug("Executing initialization script: {0}", entry.Key);
                        command.CommandText = entry.Value;
                        command.ExecuteNonQuery();
                    }
                }
            }

            base.PreStart();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected sealed override bool Receive(object message)
        {
            switch (message)
            {
                case WriteMessages msg:
                    BatchRequest(msg);
                    return true;
                case ReplayMessages msg:
                    BatchRequest(msg);
                    return true;
                case DeleteMessagesTo msg:
                    BatchRequest(msg);
                    return true;
                case ReplayTaggedMessages msg:
                    BatchRequest(msg);
                    return true;
                case ReplayAllEvents msg:
                    BatchRequest(msg);
                    return true;
                case SelectCurrentPersistenceIds msg:
                    BatchRequest(msg);
                    return true;
                case BatchComplete msg:
                    CompleteBatch(msg);
                    return true;
                case SubscribePersistenceId msg:
                    AddPersistenceIdSubscriber(msg);
                    return true;
                case SubscribeTag msg:
                    AddTagSubscriber(msg);
                    return true;
                case SubscribeNewEvents msg:
                    AddNewEventsSubscriber(msg);
                    return true;
                case Terminated msg:
                    RemoveSubscriber(msg.ActorRef);
                    return true;
                case ChunkExecutionFailure msg:
                    FailChunkExecution(msg);
                    return true;
                default:
                    return false;
            }
        }

        private void FailChunkExecution(ChunkExecutionFailure message)
        {
            _remainingOperations++;
            var cause = message.Cause;
            Log.Error(cause, "An error occurred during event batch processing. ChunkId: [{0}], batched requests: [{1}]", message.ChunkId, message.Requests.Length);
            
            foreach (var request in message.Requests)
            {
                switch (request)
                {
                    case WriteMessages req:
                    {
                        var atomicWriteCount = req.Messages.OfType<AtomicWrite>().Count();
                        var actorInstanceId = req.ActorInstanceId;
                        var aRef = req.PersistentActor;

                        aRef.Tell(new WriteMessagesFailed(cause, atomicWriteCount));
                        foreach (var envelope in req.Messages)
                        {
                            if (!(envelope is AtomicWrite write)) 
                                continue;

                            var writes = (IImmutableList<IPersistentRepresentation>)write.Payload;
                            foreach (var unadapted in writes)
                            {
                                if (cause is DbException)
                                {
                                    // database-related exceptions should result in failure                                
                                    aRef.Tell(new WriteMessageFailure(unadapted, cause, actorInstanceId), unadapted.Sender);
                                }
                                else
                                {
                                    aRef.Tell(new WriteMessageRejected(unadapted, cause, actorInstanceId), unadapted.Sender);
                                }
                            }
                        }
                        break;
                    }

                    case DeleteMessagesTo delete:
                        delete.PersistentActor.Tell(new DeleteMessagesFailure(cause, delete.ToSequenceNr), ActorRefs.NoSender);
                        break;

                    case ReplayMessages replay:
                        replay.PersistentActor.Tell(new ReplayMessagesFailure(cause));
                        break;

                    case ReplayTaggedMessages replayTagged:
                        replayTagged.ReplyTo.Tell(new ReplayMessagesFailure(cause));
                        break;

                    case ReplayAllEvents replayAll:
                        replayAll.ReplyTo.Tell(new EventReplayFailure(cause));
                        break;

                    default:
                        throw new Exception($"Unknown persistence journal request type [{request.GetType()}]");
                }
            }

            TryProcess();
        }

        #region subscriptions
        private void RemoveSubscriber(IActorRef subscriberRef)
        {
            _persistenceIdSubscribers.RemoveItem(subscriberRef);
            _tagSubscribers.RemoveItem(subscriberRef);
            _newEventSubscriber.Remove(subscriberRef);
        }

        private void AddNewEventsSubscriber(SubscribeNewEvents message)
        {
            var subscriber = Sender;
            _newEventSubscriber.Add(subscriber);
            Context.Watch(subscriber);
        }

        private void AddTagSubscriber(SubscribeTag message)
        {
            var subscriber = Sender;
            _tagSubscribers.AddItem(message.Tag, subscriber);
            Context.Watch(subscriber);
        }

        private void AddPersistenceIdSubscriber(SubscribePersistenceId message)
        {
            var subscriber = Sender;
            _persistenceIdSubscribers.AddItem(message.PersistenceId, subscriber);
            Context.Watch(subscriber);
        }

        private void NotifyNewEventAppended()
        {
            if (HasNewEventsSubscribers)
            {
                foreach (var subscriber in _newEventSubscriber)
                {
                    subscriber.Tell(NewEventAppended.Instance);
                }
            }
        }

        private void NotifyTagChanged(string tag)
        {
            if (_tagSubscribers.TryGetValue(tag, out var bucket))
            {
                var changed = new TaggedEventAppended(tag);
                foreach (var subscriber in bucket)
                    subscriber.Tell(changed);
            }
        }

        private void NotifyPersistenceIdChanged(string persistenceId)
        {
            if (_persistenceIdSubscribers.TryGetValue(persistenceId, out var bucket))
            {
                var changed = new EventAppended(persistenceId);
                foreach (var subscriber in bucket)
                    subscriber.Tell(changed);
            }
        }

        #endregion

        /// <summary>
        /// Tries to add incoming <paramref name="message"/> to <see cref="Buffer"/>.
        /// Also checks if any DB connection has been released and next batch can be processed.
        /// </summary>
        /// <param name="message">TBD</param>
        protected void BatchRequest(IJournalRequest message)
        {
            if (WriteBuffer.Count + ReadBuffer.Count > Setup.MaxBufferSize)
            {
                OnBufferOverflow(message);
            }
            else
            {
                var id = _bufferIdCounter.GetAndIncrement();
                // Enqueue writes and delete operation requests into the write queue,
                // else if they are query operations, enqueue them into the read queue
                if (message is WriteMessages || message is DeleteMessagesTo)
                    WriteBuffer.Enqueue((message, id));
                else
                    ReadBuffer.Enqueue((message, id));
            }

            TryProcess();
        }

        /// <summary>
        /// Method called, once given <paramref name="request"/> couldn't be added to <see cref="Buffer"/>
        /// due to buffer overflow. Overflow is controlled by max buffer size and can be set using 
        /// <see cref="BatchingSqlJournalSetup.MaxBufferSize"/> setting.
        /// </summary>
        /// <param name="request">TBD</param>
        protected virtual void OnBufferOverflow(IJournalMessage request)
        {
            Log.Warning("Batching journal buffer limit has been reached. Denying a request [{0}].", request);

            switch (request)
            {
                case WriteMessages msg:
                    var atomicWriteCount = msg.Messages.OfType<AtomicWrite>().Count();
                    msg.PersistentActor.Tell(new WriteMessagesFailed(JournalBufferOverflowException.Instance, atomicWriteCount), ActorRefs.NoSender);
                    break;
                case ReplayMessages msg:
                    msg.PersistentActor.Tell(new ReplayMessagesFailure(JournalBufferOverflowException.Instance), ActorRefs.NoSender);
                    break;
                case DeleteMessagesTo msg:
                    msg.PersistentActor.Tell(new DeleteMessagesFailure(JournalBufferOverflowException.Instance, msg.ToSequenceNr), ActorRefs.NoSender);
                    break;
                case ReplayTaggedMessages msg:
                    msg.ReplyTo.Tell(new ReplayMessagesFailure(JournalBufferOverflowException.Instance), ActorRefs.NoSender);
                    break;
                case ReplayAllEvents msg:
                    msg.ReplyTo.Tell(new EventReplayFailure(JournalBufferOverflowException.Instance), ActorRefs.NoSender);
                    break;
            }
        }

        private void TryProcess()
        {
            if (_remainingOperations > 0 && (WriteBuffer.Count > 0 || ReadBuffer.Count > 0))
            {
                _remainingOperations--;

                var chunk = DequeueChunk(_remainingOperations);
                var context = Context;
                _circuitBreaker.WithCircuitBreaker(() => ExecuteChunk(chunk, context))
                    .PipeTo(Self, failure: ex => new ChunkExecutionFailure(ex, chunk.Requests, chunk.ChunkId));
            }
        }

        private async Task<BatchComplete> ExecuteChunk(RequestChunk chunk, IActorContext context)
        {
            var writeResults = new Queue<WriteMessagesResult>();
            var isWriteOperation = false;
            var stopwatch = new Stopwatch();
            using (var connection = CreateConnection(Setup.ConnectionString))
            {
                await connection.OpenAsync();

                // In the grand scheme of thing, using a transaction in an all read batch operation
                // should not hurt performance by much, because it is done only once at the start.
                using (var tx = connection.BeginTransaction(Setup.IsolationLevel))
                using (var command = (TCommand)connection.CreateCommand())
                {
                    command.CommandTimeout = (int)Setup.ConnectionTimeout.TotalMilliseconds;
                    command.Transaction = tx;
                    try
                    {
                        stopwatch.Start();
                        // This looks dangerous at a glance, but we have separated read and write operations
                        // in the DequeueChunk method. 
                        foreach (var req in chunk.Requests)
                        {
                            switch (req)
                            {
                                case WriteMessages msg:
                                    isWriteOperation = true;
                                    writeResults.Enqueue(await HandleWriteMessages(msg, command)); 
                                    break;
                                case DeleteMessagesTo msg:
                                    isWriteOperation = true;
                                    await HandleDeleteMessagesTo(msg, command);
                                    break;
                                case ReplayMessages msg:
                                    await HandleReplayMessages(msg, command, context);
                                    break;
                                case ReplayTaggedMessages msg:
                                    await HandleReplayTaggedMessages(msg, command);
                                    break;
                                case ReplayAllEvents msg:
                                    await HandleReplayAllMessages(msg, command);
                                    break;
                                case SelectCurrentPersistenceIds msg:
                                    await HandleSelectCurrentPersistenceIds(msg, command);
                                    break;
                                default:
                                    Unhandled(req);
                                    break;
                            }
                        }
                        tx.Commit();
                    }
                    catch (Exception e1)
                    {
                        try
                        {
                            tx.Rollback();
                        }
                        catch (Exception e2)
                        {
                            throw new AggregateException(e2, e1);
                        }
                        throw;
                    }
                    finally
                    {
                        stopwatch.Stop();
                    }
                }
            }

            if (CanPublish)
            {
                foreach (var request in chunk.Requests)
                {
                    context.System.EventStream.Publish(request);
                }
            }

            if (isWriteOperation)
            {
                foreach (var request in chunk.Requests)
                {
                    switch (request)
                    {
                        case WriteMessages _:
                            writeResults.Dequeue().FinalizeSuccess(this);
                            break;
                        case DeleteMessagesTo req:
                            req.PersistentActor.Tell(new DeleteMessagesSuccess(req.ToSequenceNr));
                            break;
                        default:
                            throw new Exception($"Unknown database write operation {request.GetType()}");
                    }
                }
            }

            return new BatchComplete(chunk.ChunkId, chunk.Requests.Length, stopwatch.Elapsed);
        }

        protected virtual async Task HandleDeleteMessagesTo(DeleteMessagesTo req, TCommand command)
        {
            var toSequenceNr = req.ToSequenceNr;
            var persistenceId = req.PersistenceId;

            var highestSequenceNr = await ReadHighestSequenceNr(persistenceId, command);

            command.CommandText = DeleteBatchSql;
            command.Parameters.Clear();
            AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
            AddParameter(command, "@ToSequenceNr", DbType.Int64, toSequenceNr);

            await command.ExecuteNonQueryAsync();

            if (highestSequenceNr <= toSequenceNr)
            {
                command.CommandText = UpdateSequenceNrSql;
                command.Parameters.Clear();

                AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
                AddParameter(command, "@SequenceNr", DbType.Int64, highestSequenceNr);

                await command.ExecuteNonQueryAsync();
            }
        }

        protected virtual async Task<long> ReadHighestSequenceNr(string persistenceId, TCommand command)
        {
            command.CommandText = HighestSequenceNrSql;

            command.Parameters.Clear();
            AddParameter(command, "@PersistenceId", DbType.String, persistenceId);

            var result = await command.ExecuteScalarAsync();
            var highestSequenceNr = result is long ? Convert.ToInt64(result) : 0L;
            return highestSequenceNr;
        }

        protected virtual async Task<long> ReadHighestSequenceNr(TCommand command)
        {
            command.CommandText = HighestOrderingSql;
            command.Parameters.Clear();

            var result = await command.ExecuteScalarAsync();
            var highestSequenceNr = result is long ? Convert.ToInt64(result) : 0L;
            return highestSequenceNr;
        }

        protected virtual async Task HandleSelectCurrentPersistenceIds(SelectCurrentPersistenceIds message, TCommand command)
        {
            long highestOrderingNumber = await ReadHighestSequenceNr(command);

            var result = new List<string>(256);
            command.CommandText = AllPersistenceIdsSql;
            command.Parameters.Clear();
            AddParameter(command, "@Ordering", DbType.Int64, message.Offset);

            var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(reader.GetString(0));
            }

            message.ReplyTo.Tell(new CurrentPersistenceIds(result, highestOrderingNumber));
        }

        protected virtual async Task HandleReplayTaggedMessages(ReplayTaggedMessages req, TCommand command)
        {
            var replyTo = req.ReplyTo;

            try
            {
                var maxSequenceNr = 0L;
                var tag = req.Tag;
                var toOffset = req.ToOffset;
                var fromOffset = req.FromOffset;
                var take = Math.Min(toOffset - fromOffset, req.Max);

                command.CommandText = ByTagSql;
                command.Parameters.Clear();

                AddParameter(command, "@Tag", DbType.String, "%;" + tag + ";%");
                AddParameter(command, "@Ordering", DbType.Int64, fromOffset);
                AddParameter(command, "@Take", DbType.Int64, take);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var persistent = ReadEvent(reader);
                        var ordering = reader.GetInt64(OrderingIndex);
                        maxSequenceNr = Math.Max(maxSequenceNr, persistent.SequenceNr);

                        foreach (var adapted in AdaptFromJournal(persistent))
                        {
                            replyTo.Tell(new ReplayedTaggedMessage(adapted, tag, ordering), ActorRefs.NoSender);
                        }
                    }
                }

                replyTo.Tell(new RecoverySuccess(maxSequenceNr));
            }
            catch (Exception cause)
            {
                replyTo.Tell(new ReplayMessagesFailure(cause));
            }
        }

        protected virtual async Task HandleReplayAllMessages(ReplayAllEvents req, TCommand command)
        {
            var replyTo = req.ReplyTo;

            try
            {
                var toOffset = req.ToOffset;
                var fromOffset = req.FromOffset;
                var max = req.Max;

                var take = Math.Min(toOffset - fromOffset, max);

                command.CommandText = HighestOrderingSql;
                command.Parameters.Clear();

                var maxOrdering = (await command.ExecuteScalarAsync()) as long? ?? 0L;

                command.CommandText = AllEventsSql;
                command.Parameters.Clear();

                AddParameter(command, "@Ordering", DbType.Int64, fromOffset);
                AddParameter(command, "@Take", DbType.Int64, take);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var persistent = ReadEvent(reader);
                        var ordering = reader.GetInt64(OrderingIndex);

                        foreach (var adapted in AdaptFromJournal(persistent))
                        {
                            replyTo.Tell(new ReplayedEvent(adapted, ordering), ActorRefs.NoSender);
                        }
                    }
                }

                replyTo.Tell(new EventReplaySuccess(maxOrdering));
            }
            catch (Exception cause)
            {
                replyTo.Tell(new EventReplayFailure(cause));
            }
        }

        protected virtual async Task HandleReplayMessages(ReplayMessages req, TCommand command, IActorContext context)
        {
            var replaySettings = Setup.ReplayFilterSettings;
            var replyTo = replaySettings.IsEnabled
                ? context.ActorOf(ReplayFilter.Props(
                    persistentActor: req.PersistentActor,
                    mode: replaySettings.Mode,
                    windowSize: replaySettings.WindowSize,
                    maxOldWriters: replaySettings.MaxOldWriters,
                    debugEnabled: replaySettings.IsDebug))
                : req.PersistentActor;
            var persistenceId = req.PersistenceId;

            try
            {
                var highestSequenceNr = await ReadHighestSequenceNr(persistenceId, command);
                var toSequenceNr = Math.Min(req.ToSequenceNr, highestSequenceNr);

                command.CommandText = ByPersistenceIdSql;
                command.Parameters.Clear();

                AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
                AddParameter(command, "@FromSequenceNr", DbType.Int64, req.FromSequenceNr);
                AddParameter(command, "@ToSequenceNr", DbType.Int64, toSequenceNr);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    var i = 0L;
                    while ((i++) < req.Max && await reader.ReadAsync())
                    {
                        var persistent = ReadEvent(reader);

                        if (!persistent.IsDeleted) // old records from pre 1.5 may still have the IsDeleted flag
                        {
                            foreach (var adaptedRepresentation in AdaptFromJournal(persistent))
                            {
                                replyTo.Tell(new ReplayedMessage(adaptedRepresentation), ActorRefs.NoSender);
                            }
                        }
                    }
                }

                var response = new RecoverySuccess(highestSequenceNr);
                replyTo.Tell(response, ActorRefs.NoSender);
            }
            catch (Exception cause)
            {
                var response = new ReplayMessagesFailure(cause);
                replyTo.Tell(response, ActorRefs.NoSender);
            }
        }

        private async Task<WriteMessagesResult> HandleWriteMessages(WriteMessages req, TCommand command)
        {
            var tags = new HashSet<string>();
            var persistenceIds = new HashSet<string>();

            command.CommandText = InsertEventSql;

            var tagBuilder = new StringBuilder(16); // magic number                

            foreach (var envelope in req.Messages.OfType<AtomicWrite>())
            {
                var writes = (IImmutableList<IPersistentRepresentation>)envelope.Payload;
                foreach (var unadapted in writes)
                {
                    command.Parameters.Clear();
                    tagBuilder.Clear();

                    var persistent = AdaptToJournal(unadapted);
                    if (persistent.Payload is Tagged tagged)
                    {
                        if (tagged.Tags.Count != 0)
                        {
                            tagBuilder.Append(';');
                            foreach (var tag in tagged.Tags)
                            {
                                tags.Add(tag);
                                tagBuilder.Append(tag).Append(';');
                            }
                        }
                        persistent = persistent.WithPayload(tagged.Payload);
                    }

                    WriteEvent(command, persistent.WithTimestamp(DateTime.UtcNow.Ticks), tagBuilder.ToString());

                    await command.ExecuteNonQueryAsync();

                    persistenceIds.Add(persistent.PersistenceId);
                }
            }

            return new WriteMessagesResult(req, tags, persistenceIds);
        }

        /// <summary>
        /// Perform write of persistent event with specified <paramref name="tags"/> 
        /// into database using given <paramref name="command"/>.
        /// </summary>
        /// <param name="command">Database command object used to store data.</param>
        /// <param name="persistent">Persistent event representation.</param>
        /// <param name="tags">Optional tags extracted from persistent event payload.</param>
        protected virtual void WriteEvent(TCommand command, IPersistentRepresentation persistent, string tags = "")
        {
            var payloadType = persistent.Payload.GetType();
            var serializer = _serialization.FindSerializerForType(payloadType, Setup.DefaultSerializer);

            // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
            Akka.Serialization.Serialization.WithTransport(_serialization.System, () =>
            {
                string manifest = "";
                if (serializer is SerializerWithStringManifest stringManifest)
                {
                    manifest = stringManifest.Manifest(persistent.Payload);
                }
                else
                {
                    if (serializer.IncludeManifest)
                    {
                        manifest = persistent.Payload.GetType().TypeQualifiedName();
                    }
                }

                var binary = serializer.ToBinary(persistent.Payload);

                AddParameter(command, "@PersistenceId", DbType.String, persistent.PersistenceId);
                AddParameter(command, "@SequenceNr", DbType.Int64, persistent.SequenceNr);
                AddParameter(command, "@Timestamp", DbType.Int64, persistent.Timestamp);
                AddParameter(command, "@IsDeleted", DbType.Boolean, false);
                AddParameter(command, "@Manifest", DbType.String, manifest);
                AddParameter(command, "@Payload", DbType.Binary, binary);
                AddParameter(command, "@Tag", DbType.String, tags);
                AddParameter(command, "@SerializerId", DbType.Int32, serializer.Identifier);

                return manifest;
            });
        }

        /// <summary>
        /// Returns a persistent representation of an event read from a current row in the database.
        /// </summary>
        /// <param name="reader">TBD</param>
        /// <returns>TBD</returns>
        protected virtual IPersistentRepresentation ReadEvent(DbDataReader reader)
        {
            var persistenceId = reader.GetString(PersistenceIdIndex);
            var sequenceNr = reader.GetInt64(SequenceNrIndex);
            var timestamp = reader.GetInt64(TimestampIndex);
            var isDeleted = reader.GetBoolean(IsDeletedIndex);
            var manifest = reader.GetString(ManifestIndex);
            var payload = reader[PayloadIndex];

            object deserialized;
            if (reader.IsDBNull(SerializerIdIndex))
            {
                // Support old writes that did not set the serializer id
                var type = Type.GetType(manifest, true);
                var deserializer = _serialization.FindSerializerForType(type, Setup.DefaultSerializer);
                deserialized = deserializer.FromBinary((byte[])payload, type);
            }
            else
            {
                var serializerId = reader.GetInt32(SerializerIdIndex);
                deserialized = _serialization.Deserialize((byte[])payload, serializerId, manifest);
            }

            return new Persistent(deserialized, sequenceNr, persistenceId, manifest, isDeleted, ActorRefs.NoSender, null, timestamp);
        }

        /// <summary>
        /// Creates a new database connection from a given <paramref name="connectionString"/>.
        /// </summary>
        /// <param name="connectionString">TBD</param>
        /// <returns>TBD</returns>
        protected abstract TConnection CreateConnection(string connectionString);

        /// <summary>
        /// Helper method used to add a parameter to existing database <paramref name="command"/>.
        /// </summary>
        /// <param name="command"><see cref="DbCommand"/> used to define a parameter in.</param>
        /// <param name="paramName">Query or procedure parameter name.</param>
        /// <param name="dbType">Database type of a query or procedure parameter.</param>
        /// <param name="value">Value of a query or procedure parameter.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void AddParameter(TCommand command, string paramName, DbType dbType, object value)
        {
            var param = command.CreateParameter();
            param.Value = value;
            param.ParameterName = paramName;
            param.DbType = dbType;

            PreAddParameterToCommand(command, param);
            
            command.Parameters.Add(param);
        }

        /// <summary>
        /// Override this to customize <see cref="DbParameter"/> creation used for building database queries
        /// </summary>
        /// <param name="command"><see cref="DbCommand"/> used to define a parameter in.</param>
        /// <param name="param">Parameter to customize</param>
        protected virtual void PreAddParameterToCommand(TCommand command, DbParameter param) { }
        
        /// <summary>
        /// Select the buffer that has the smallest id on its first item, retrieve a maximum Setup.MaxBatchSize
        /// items from it, and return it as a chunk that needs to be batched
        /// </summary>
        private RequestChunk DequeueChunk(int chunkId)
        {
            var currentBuffer = _buffers
                .Where(q => q.Count > 0)
                .OrderBy(q => q.Peek().id).First();

            var operations = new List<IJournalRequest>();
            if (ReferenceEquals(currentBuffer, WriteBuffer))
            {
                // Stop dequeuing when we encounter another type of write operation request
                // We don't batch delete and writes in the same batch, reason being a database
                // can be deadlocked if write and delete happens in the same transaction
                var writeType = currentBuffer.Peek().request.GetType();
                while(currentBuffer.Count > 0 && currentBuffer.Peek().request.GetType() == writeType)
                {
                    operations.Add(currentBuffer.Dequeue().request);
                    if (operations.Count == Setup.MaxBatchSize)
                        break;
                }
            }
            else
            {
                while(currentBuffer.Count > 0)
                {
                    operations.Add(currentBuffer.Dequeue().request);
                    if (operations.Count == Setup.MaxBatchSize)
                        break;
                }
            }
            
            return new RequestChunk(chunkId, operations.ToArray());
        }

        private void CompleteBatch(BatchComplete msg)
        {
            _remainingOperations++;
            Log.Debug("Completed batch (chunkId: {0}) of {1} operations in {2} milliseconds", msg.ChunkId, msg.OperationCount, msg.TimeSpent.TotalMilliseconds);

            TryProcess();
        }

        private class WriteMessagesResult
        {
            private readonly WriteMessages _request;

            private readonly ImmutableHashSet<string> _tags;
            private readonly ImmutableHashSet<string> _persistenceIds;

            public WriteMessagesResult(
                WriteMessages request,
                IEnumerable<string> tags, 
                IEnumerable<string> persistenceIds)
            {
                _request = request;
                _tags = tags.ToImmutableHashSet();
                _persistenceIds = persistenceIds.ToImmutableHashSet();
            }

            public void FinalizeSuccess(BatchingSqlJournal<TConnection, TCommand> journal)
            {
                var actorInstanceId = _request.ActorInstanceId;
                var aRef = _request.PersistentActor;
                aRef.Tell(WriteMessagesSuccessful.Instance);

                foreach (var envelope in _request.Messages)
                {
                    if (!(envelope is AtomicWrite write))
                    {
                        aRef.Tell(new LoopMessageSuccess(envelope.Payload, actorInstanceId), envelope.Sender);
                        continue;
                    }

                    var writes = (IImmutableList<IPersistentRepresentation>)write.Payload;
                    foreach (var unadapted in writes)
                    {
                        aRef.Tell(new WriteMessageSuccess(unadapted, actorInstanceId), unadapted.Sender);
                    }
                }

                if (journal.HasTagSubscribers && _tags.Count != 0)
                {
                    foreach (var tag in _tags)
                    {
                        journal.NotifyTagChanged(tag);
                    }
                }

                if (journal.HasPersistenceIdSubscribers)
                {
                    foreach (var persistenceId in _persistenceIds)
                    {
                        journal.NotifyPersistenceIdChanged(persistenceId);
                    }
                }

                if (journal.HasNewEventsSubscribers)
                {
                    journal.NotifyNewEventAppended();
                }

            }
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class JournalBufferOverflowException : AkkaException
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly JournalBufferOverflowException Instance = new JournalBufferOverflowException();

        /// <summary>
        /// Initializes a new instance of the <see cref="JournalBufferOverflowException" /> class.
        /// </summary>
        public JournalBufferOverflowException() : base(
            "Batching journal buffer has been overflowed. This may happen as an effect of burst of persistent actors "
            + "requests incoming faster than the underlying database is able to fulfill them. You may modify "
            + "`max-buffer-size`, `max-batch-size` and `max-concurrent-operations` HOCON settings in order to "
            + " change it.")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JournalBufferOverflowException" /> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected JournalBufferOverflowException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
