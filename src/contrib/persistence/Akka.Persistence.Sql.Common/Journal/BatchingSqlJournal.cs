﻿//-----------------------------------------------------------------------
// <copyright file="BatchingSqlJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Pattern;
using Akka.Persistence.Journal;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Persistence.Sql.Common.Journal
{
    /// <summary>
    /// Settings used for managing filter rules during event replay.
    /// </summary>
    public sealed class ReplayFilterSettings
    {
        /// <summary>
        /// What the filter should do when detecting invalid events.
        /// </summary>
        public readonly ReplayFilterMode Mode;
        
        /// <summary>
        /// It uses a look ahead buffer for analyzing the events.
        /// This defines the size (in number of events) of the buffer.
        /// </summary>
        public readonly int WindowSize;

        /// <summary>
        /// How many old writerUuid to remember.
        /// </summary>
        public readonly int MaxOldWriters;

        /// <summary>
        /// Should the debug logging be enabled for each replayed event?
        /// </summary>
        public readonly bool IsDebug;

        /// <summary>
        /// Is replay filter feature enabled?
        /// </summary>
        public bool IsEnabled => Mode != ReplayFilterMode.Disabled;

        /// <summary>
        /// Creates a new instance of the <see cref="ReplayFilterSettings"/> from provided HOCON <paramref name="config"/>.
        /// </summary>
        public ReplayFilterSettings(Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config), "No HOCON config was provided for replay filter settings");

            ReplayFilterMode mode;
            var replayModeString = config.GetString("mode", "off");
            switch (replayModeString)
            {
                case "off": mode = ReplayFilterMode.Disabled; break;
                case "repair-by-discard-old": mode = ReplayFilterMode.RepairByDiscardOld; break;
                case "fail": mode = ReplayFilterMode.Fail; break;
                case "warn": mode = ReplayFilterMode.Warn; break;
                default: throw new ArgumentException($"Invalid replay-filter.mode [{replayModeString}], supported values [off, repair-by-discard-old, fail, warn]", nameof(config));
            }
            
            Mode = mode;
            WindowSize = config.GetInt("window-size", 100);
            MaxOldWriters = config.GetInt("max-old-writers", 10);
            IsDebug = config.GetBoolean("debug", false);
        }

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
        /// Max number of failures that can happen before circuit will open.
        /// </summary>
        public int MaxFailures { get; }
        
        /// <summary>
        /// Max time available for operation to execute before 
        /// <see cref="CircuitBreaker"/> will consider it a failure.
        /// </summary>
        public TimeSpan CallTimeout { get; }

        /// <summary>
        /// Timeot that has to pass before <see cref="CircuitBreaker"/>
        /// will move into half-closed state, trying to eventually close 
        /// after sampling an operation.
        /// </summary>
        public TimeSpan ResetTimeout { get; }

        /// <summary>
        /// Creates a new instance of the <see cref="CircuitBreakerSettings"/> from provided HOCON <paramref name="config"/>.
        /// </summary>
        public CircuitBreakerSettings(Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            MaxFailures = config.GetInt("max-failures", 5);
            CallTimeout = config.GetTimeSpan("call-timeout", TimeSpan.FromSeconds(20));
            ResetTimeout = config.GetTimeSpan("reset-timeout", TimeSpan.FromSeconds(60));
        }

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
        /// can be effectivelly used to limit the usage of ADO.NET connection pool by current journal.
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

        protected BatchingSqlJournalSetup(Config config, QueryConfiguration namingConventions)
        {
            if (config == null) throw new ArgumentNullException(nameof(config), "Sql journal settings cannot be initialized, because required HOCON section couldn't been found");

            var connectionString = config.GetString("connection-string");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = ConfigurationManager
                    .ConnectionStrings[config.GetString("connection-string-name", "DefaultConnection")]
                    .ConnectionString;
            }

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("No connection string for Sql Event Journal was specified");
            
            ConnectionString = connectionString;
            MaxConcurrentOperations = config.GetInt("max-concurrent-operations", 64);
            MaxBatchSize = config.GetInt("max-batch-size", 100);
            MaxBufferSize = config.GetInt("max-buffer-size", 500000);
            AutoInitialize = config.GetBoolean("auto-initialize", false);
            ConnectionTimeout = config.GetTimeSpan("connection-timeout", TimeSpan.FromSeconds(30));
            CircuitBreakerSettings = new CircuitBreakerSettings(config.GetConfig("circuit-breaker"));
            ReplayFilterSettings = new ReplayFilterSettings(config.GetConfig("replay-filter"));
            NamingConventions = namingConventions;
        }

        protected BatchingSqlJournalSetup(string connectionString, int maxConcurrentOperations, int maxBatchSize, int maxBufferSize, bool autoInitialize, TimeSpan connectionTimeout, CircuitBreakerSettings circuitBreakerSettings, ReplayFilterSettings replayFilterSettings, QueryConfiguration namingConventions)
        {
            ConnectionString = connectionString;
            MaxConcurrentOperations = maxConcurrentOperations;
            MaxBatchSize = maxBatchSize;
            MaxBufferSize = maxBufferSize;
            AutoInitialize = autoInitialize;
            ConnectionTimeout = connectionTimeout;
            CircuitBreakerSettings = circuitBreakerSettings;
            ReplayFilterSettings = replayFilterSettings;
            NamingConventions = namingConventions;
        }
    }

    /// <summary>
    /// An abstract journal used by <see cref="PersistentActor"/>s to read/write events to a database.
    /// 
    /// This implementation uses horizontal batching to recycle usage of the <see cref="DbConnection"/> 
    /// and to optimize writes made to a database. Batching journal is not going to acquire a new DB
    /// connection on every request. Instead it will batch incoming requests and execute them only when
    /// a previous operation batch has been completed. This means that requests comming from many 
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

        private sealed class BatchComplete
        {
            public readonly int ChunkId;
            public readonly int OperationCount;
            public readonly TimeSpan TimeSpent;
            public readonly Exception Cause;

            public BatchComplete(int chunkId, int operationCount, TimeSpan timeSpent, Exception cause = null)
            {
                ChunkId = chunkId;
                TimeSpent = timeSpent;
                OperationCount = operationCount;
                Cause = cause;
            }
        }

        // this little guy will be called only once, only by the current journal
        private sealed class GetCurrentPersistenceIds
        {
            public static readonly GetCurrentPersistenceIds Instance = new GetCurrentPersistenceIds();
            private GetCurrentPersistenceIds() { }
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

        //protected const int TimestampIndex = 2;

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
        /// Default index of tags column get from <see cref="ByTagSql"/> query.
        /// </summary>
        protected const int OrderingIndex = 6;

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
        /// SQL query executed as result of <see cref="GetCurrentPersistenceIds"/> request to journal.
        /// It's a part of persitence query protocol.
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
        /// Flag determining if current journal has any subscribers for <see cref="GetCurrentPersistenceIds"/> and 
        /// <see cref="PersistenceIdAdded"/> messages.
        /// </summary>
        protected bool HasAllIdsSubscribers => _allIdsSubscribers.Count != 0;
        
        /// <summary>
        /// Flag determining if incoming journal requests should be published in current actor system event stream.
        /// Usefull mostly for tests.
        /// </summary>
        protected readonly bool CanPublish;

        /// <summary>
        /// Logging adapter for current journal actor .
        /// </summary>
        protected readonly ILoggingAdapter Log;

        /// <summary>
        /// Buffer for requests that are waiting to be served when next DB connection will be released.
        /// This object access is NOT thread safe.
        /// </summary>
        protected readonly Queue<IJournalRequest> Buffer;

        private readonly Dictionary<string, HashSet<IActorRef>> _persistenceIdSubscribers;
        private readonly Dictionary<string, HashSet<IActorRef>> _tagSubscribers;
        private readonly HashSet<IActorRef> _allIdsSubscribers;
        private readonly HashSet<string> _allPersistenceIds;

        private readonly Func<Type, Serializer> _getSerializer;
        private readonly CircuitBreaker _circuitBreaker;
        private int _remainingOperations;

        protected BatchingSqlJournal(BatchingSqlJournalSetup setup)
        {
            Setup = setup;
            CanPublish = Persistence.Instance.Apply(Context.System).Settings.Internal.PublishPluginCommands;

            _persistenceIdSubscribers = new Dictionary<string, HashSet<IActorRef>>();
            _tagSubscribers = new Dictionary<string, HashSet<IActorRef>>();
            _allIdsSubscribers = new HashSet<IActorRef>();
            _allPersistenceIds = new HashSet<string>();
            
            _remainingOperations = Setup.MaxConcurrentOperations;
            Buffer = new Queue<IJournalRequest>(Setup.MaxBatchSize);
            _getSerializer = Context.System.Serialization.FindSerializerFor;
            Log = Context.GetLogger();
            _circuitBreaker = CircuitBreaker.Create(
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
                e.{conventions.PayloadColumnName} as Payload";

            AllPersistenceIdsSql = $@"
                SELECT DISTINCT e.{conventions.PersistenceIdColumnName} as PersistenceId 
                FROM {conventions.FullJournalTableName} e;";

            HighestSequenceNrSql = $@"
                SELECT MAX(u.SeqNr) as SequenceNr 
                FROM (
                    SELECT e.{conventions.SequenceNrColumnName} as SeqNr FROM {conventions.FullJournalTableName} e WHERE e.{conventions.PersistenceIdColumnName} = @PersistenceId
                    UNION
                    SELECT m.{conventions.SequenceNrColumnName} as SeqNr FROM {conventions.FullMetaTableName} m WHERE m.{conventions.PersistenceIdColumnName} = @PersistenceId) as u";

            DeleteBatchSql = $@"
                DELETE FROM {conventions.FullJournalTableName} 
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

            InsertEventSql = $@"
                INSERT INTO {conventions.FullJournalTableName} (
                    {conventions.PersistenceIdColumnName},
                    {conventions.SequenceNrColumnName},
                    {conventions.TimestampColumnName},
                    {conventions.IsDeletedColumnName},
                    {conventions.ManifestColumnName},
                    {conventions.PayloadColumnName},
                    {conventions.TagsColumnName}
                ) VALUES (
                    @PersistenceId, 
                    @SequenceNr,
                    @Timestamp,
                    @IsDeleted,
                    @Manifest,
                    @Payload,
                    @Tag
                )";
        }

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
        
        protected sealed override bool Receive(object message)
        {
            if (message is WriteMessages) BatchRequest((IJournalRequest)message);
            else if (message is ReplayMessages) BatchRequest((IJournalRequest)message);
            else if (message is BatchComplete) CompleteBatch((BatchComplete)message);
            else if (message is ReadHighestSequenceNr) BatchRequest((IJournalRequest)message);
            else if (message is DeleteMessagesTo) BatchRequest((IJournalRequest)message);
            else if (message is ReplayTaggedMessages) BatchRequest((IJournalRequest)message);
            else if (message is SubscribePersistenceId) AddPersistenceIdSubscriber((SubscribePersistenceId)message);
            else if (message is SubscribeAllPersistenceIds) AddAllSubscriber((SubscribeAllPersistenceIds)message);
            else if (message is SubscribeTag) AddTagSubscriber((SubscribeTag)message);
            else if (message is Terminated) RemoveSubscriber(((Terminated)message).ActorRef);
            else if (message is GetCurrentPersistenceIds) InitializePersistenceIds();
            else if (message is CurrentPersistenceIds) SendCurrentPersistenceIds((CurrentPersistenceIds)message);
            else return false;
            return true;
        }

        private void SendCurrentPersistenceIds(CurrentPersistenceIds message)
        {
            foreach (var persistenceId in message.AllPersistenceIds)
            {
                _allPersistenceIds.Add(persistenceId);
            }

            foreach (var subscriber in _allIdsSubscribers)
            {
                subscriber.Tell(message);
            }
        }

        #region subscriptions

        private void InitializePersistenceIds()
        {
            var self = Self;
            GetAllPersistenceIdsAsync()
                .ContinueWith(task =>
                {
                    if (task.IsCanceled || task.IsFaulted)
                    {
                        var cause = (Exception)task.Exception ?? new OperationCanceledException("Cancellation occurred while trying to retrieve current persistence ids");
                        Log.Error(cause, "Couldn't retrieve current persistence ids");
                    }
                    else
                    {
                        self.Tell(new CurrentPersistenceIds(task.Result));
                    }
                });
        }

        private async Task<IEnumerable<string>> GetAllPersistenceIdsAsync()
        {
            var result = new List<string>(256);
            using (var connection = CreateConnection(Setup.ConnectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = AllPersistenceIdsSql;

                    var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        result.Add(reader.GetString(0));
                    }
                }
            }
            return result;
        }

        private void RemoveSubscriber(IActorRef subscriberRef)
        {
            _allIdsSubscribers.Remove(subscriberRef);
            _persistenceIdSubscribers.RemoveItem(subscriberRef);
            _tagSubscribers.RemoveItem(subscriberRef);
        }

        private void AddTagSubscriber(SubscribeTag message)
        {
            var subscriber = Sender;
            _tagSubscribers.AddItem(message.Tag, subscriber);
            Context.Watch(subscriber);
        }

        private void AddAllSubscriber(SubscribeAllPersistenceIds message)
        {
            if (!HasAllIdsSubscribers)
            {
                Self.Tell(GetCurrentPersistenceIds.Instance);
            }

            var subscriber = Sender;
            _allIdsSubscribers.Add(subscriber);
            Context.Watch(subscriber);
        }

        private void AddPersistenceIdSubscriber(SubscribePersistenceId message)
        {
            var subscriber = Sender;
            _persistenceIdSubscribers.AddItem(message.PersistenceId, subscriber);
            Context.Watch(subscriber);
        }

        private void NotifyTagChanged(string tag)
        {
            HashSet<IActorRef> bucket;
            if (_tagSubscribers.TryGetValue(tag, out bucket))
            {
                var changed = new TaggedEventAppended(tag);
                foreach (var subscriber in bucket)
                {
                    subscriber.Tell(changed);
                }
            }
        }

        private void NotifyPersistenceIdChanged(string persitenceId)
        {
            HashSet<IActorRef> bucket;
            if (_persistenceIdSubscribers.TryGetValue(persitenceId, out bucket))
            {
                var changed = new EventAppended(persitenceId);
                foreach (var subscriber in bucket)
                {
                    subscriber.Tell(changed);
                }
            }
        }

        private void NotifyNewPersistenceIdAdded(string persistenceId)
        {
            if (_allPersistenceIds.Add(persistenceId) && HasAllIdsSubscribers)
            {
                var added = new PersistenceIdAdded(persistenceId);
                foreach (var subscriber in _allIdsSubscribers)
                {
                    subscriber.Tell(added, ActorRefs.NoSender);
                }
            }
        }

        #endregion

        /// <summary>
        /// Tries to add incoming <paramref name="message"/> to <see cref="Buffer"/>.
        /// Also checks if any DB connection has been released and next batch can be processed.
        /// </summary>
        protected void BatchRequest(IJournalRequest message)
        {
            if (Buffer.Count > Setup.MaxBufferSize)
                OnBufferOverflow(message);
            else
                Buffer.Enqueue(message);

            TryProcess();
        }

        /// <summary>
        /// Method called, once given <paramref name="request"/> couldn't be added to <see cref="Buffer"/>
        /// due to buffer overflow. Overflow is controlled by max buffer size and can be set using 
        /// <see cref="BatchingSqlJournalSetup.MaxBufferSize"/> setting.
        /// </summary>
        protected virtual void OnBufferOverflow(IJournalMessage request)
        {
            Log.Warning("Batching journal buffer limit has been reached. Denying a request [{0}].", request);

            if (request is WriteMessages)
            {
                var r = (WriteMessages)request;
                r.PersistentActor.Tell(new WriteMessagesFailed(JournalBufferOverflowException.Instance), ActorRefs.NoSender);
            }
            else if (request is ReplayMessages)
            {
                var r = (ReplayMessages)request;
                r.PersistentActor.Tell(new ReplayMessagesFailure(JournalBufferOverflowException.Instance), ActorRefs.NoSender);

            }
            else if (request is ReadHighestSequenceNr)
            {
                var r = (ReadHighestSequenceNr)request;
                r.PersistentActor.Tell(new ReadHighestSequenceNrFailure(JournalBufferOverflowException.Instance), ActorRefs.NoSender);

            }
            else if (request is DeleteMessagesTo)
            {
                var r = (DeleteMessagesTo)request;
                r.PersistentActor.Tell(new DeleteMessagesFailure(JournalBufferOverflowException.Instance, r.ToSequenceNr), ActorRefs.NoSender);

            }
            else if (request is ReplayTaggedMessages)
            {
                var r = (ReplayTaggedMessages) request;
                r.ReplyTo.Tell(new ReplayMessagesFailure(JournalBufferOverflowException.Instance), ActorRefs.NoSender);
            }
        }

        private void TryProcess()
        {
            if (_remainingOperations > 0 && Buffer.Count > 0)
            {
                _remainingOperations--;

                var chunk = DequeueChunk(_remainingOperations);
                var context = Context;
                _circuitBreaker.WithCircuitBreaker(() => ExecuteChunk(chunk, context)).PipeTo(Self);
            }
        }

        private async Task<BatchComplete> ExecuteChunk(RequestChunk chunk, IActorContext context)
        {
            Exception cause = null;
            var stopwatch = new Stopwatch();
            using (var connection = CreateConnection(Setup.ConnectionString))
            {
                await connection.OpenAsync();
                
                using (var tx = connection.BeginTransaction())
                using (var command = (TCommand)connection.CreateCommand())
                {
                    command.CommandTimeout = (int) Setup.ConnectionTimeout.TotalMilliseconds;
                    command.Transaction = tx;
                    try
                    {
                        stopwatch.Start();
                        for (int i = 0; i < chunk.Requests.Length; i++)
                        {
                            var req = chunk.Requests[i];

                            if (req is WriteMessages)
                                await HandleWriteMessages((WriteMessages)req, command);
                            else if (req is ReplayMessages)
                                await HandleReplayMessages((ReplayMessages)req, command, context);
                            else if (req is ReadHighestSequenceNr)
                                await HandleReadHighestSequenceNr((ReadHighestSequenceNr)req, command);
                            else if (req is DeleteMessagesTo)
                                await HandleDeleteMessagesTo((DeleteMessagesTo)req, command);
                            else if (req is ReplayTaggedMessages)
                                await HandleReplayTaggedMessages((ReplayTaggedMessages)req, command);
                            else Unhandled(req);
                        }

                        tx.Commit();

                        if (CanPublish)
                        {
                            for (int i = 0; i < chunk.Requests.Length; i++)
                            {
                                context.System.EventStream.Publish(chunk.Requests[i]);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        cause = e;
                        tx.Rollback();
                    }
                    finally
                    {
                        stopwatch.Stop();
                    }
                }
            }

            return new BatchComplete(chunk.ChunkId, chunk.Requests.Length, stopwatch.Elapsed, cause);
        }

        private async Task HandleDeleteMessagesTo(DeleteMessagesTo req, TCommand command)
        {
            var toSequenceNr = req.ToSequenceNr;
            var persistenceId = req.PersistenceId;

            NotifyNewPersistenceIdAdded(persistenceId);

            try
            {
                var highestSequenceNr = await ReadHighestSequenceNr(persistenceId, command);

                command.CommandText = DeleteBatchSql;
                command.Parameters.Clear();
                AddParameter(command, "PersistenceId", DbType.String, persistenceId);
                AddParameter(command, "ToSequenceNr", DbType.Int64, toSequenceNr);

                await command.ExecuteNonQueryAsync();

                if (highestSequenceNr <= toSequenceNr)
                {
                    command.CommandText = UpdateSequenceNrSql;
                    command.Parameters.Clear();

                    AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
                    AddParameter(command, "@SequenceNr", DbType.Int64, highestSequenceNr);

                    await command.ExecuteNonQueryAsync();
                }

                var response = new DeleteMessagesSuccess(toSequenceNr);
                req.PersistentActor.Tell(response);
            }
            catch (Exception cause)
            {
                var response = new DeleteMessagesFailure(cause, toSequenceNr);
                req.PersistentActor.Tell(response, ActorRefs.NoSender);
            }
        }

        private async Task HandleReadHighestSequenceNr(ReadHighestSequenceNr req, TCommand command)
        {
            var replyTo = req.PersistentActor;
            var persistenceId = req.PersistenceId;

            NotifyNewPersistenceIdAdded(persistenceId);

            try
            {
                var highestSequenceNr = await ReadHighestSequenceNr(persistenceId, command);

                var response = new ReadHighestSequenceNrSuccess(highestSequenceNr);
                replyTo.Tell(response, ActorRefs.NoSender);
            }
            catch (Exception cause)
            {
                var response = new ReadHighestSequenceNrFailure(cause);
                replyTo.Tell(response, ActorRefs.NoSender);
            }
        }

        private async Task<long> ReadHighestSequenceNr(string persistenceId, TCommand command)
        {
            command.CommandText = HighestSequenceNrSql;

            command.Parameters.Clear();
            AddParameter(command, "PersistenceId", DbType.String, persistenceId);

            var result = await command.ExecuteScalarAsync();
            var highestSequenceNr = result is long ? Convert.ToInt64(result) : 0L;
            return highestSequenceNr;
        }

        private async Task HandleReplayTaggedMessages(ReplayTaggedMessages req, TCommand command)
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

        private async Task HandleReplayMessages(ReplayMessages req, TCommand command, IActorContext context)
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

            NotifyNewPersistenceIdAdded(persistenceId);

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

        private async Task HandleWriteMessages(WriteMessages req, TCommand command)
        {
            IJournalResponse summary;
            var responses = new List<IJournalResponse>();
            var tags = new HashSet<string>();
            var persistenceIds = new HashSet<string>();
            var actorInstanceId = req.ActorInstanceId;

            try
            {
                command.CommandText = InsertEventSql;
                
                var tagBuilder = new StringBuilder(16); // magic number

                foreach (var envelope in req.Messages)
                {
                    var write = envelope as AtomicWrite;
                    if (write != null)
                    {
                        var writes = (IImmutableList<IPersistentRepresentation>)write.Payload;
                        foreach (var unadapted in writes)
                        {
                            var e = AdaptToJournal(unadapted);

                            try
                            {
                                command.Parameters.Clear();
                                tagBuilder.Clear();
                                
                                var persistent = e;
                                if (persistent.Payload is Tagged)
                                {
                                    var tagged = (Tagged)persistent.Payload;
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
                                
                                WriteEvent(command, persistent, tagBuilder.ToString());

                                await command.ExecuteNonQueryAsync();

                                var response = new WriteMessageSuccess(persistent, actorInstanceId);
                                responses.Add(response);
                                persistenceIds.Add(persistent.PersistenceId);

                                NotifyNewPersistenceIdAdded(persistent.PersistenceId);
                            }
                            catch (Exception cause)
                            {
                                //TODO: this scope wraps atomic write. Atomic writes have all-or-nothing commits.
                                // so we should revert transaction here. But we need to check how this affect performance.

                                var response = new WriteMessageRejected(e, cause, actorInstanceId);
                                responses.Add(response);
                            }
                        }
                    }
                    else
                    {
                        //TODO: other cases?
                        var response = new LoopMessageSuccess(envelope.Payload, actorInstanceId);
                        responses.Add(response);
                    }
                }

                if (HasTagSubscribers && tags.Count != 0)
                {
                    foreach (var tag in tags)
                    {
                        NotifyTagChanged(tag);
                    }
                }

                if (HasPersistenceIdSubscribers)
                {
                    foreach (var persistenceId in persistenceIds)
                    {
                        NotifyPersistenceIdChanged(persistenceId);
                    }
                }

                summary = WriteMessagesSuccessful.Instance;
            }
            catch (Exception cause)
            {
                summary = new WriteMessagesFailed(cause);
            }

            var aref = req.PersistentActor;

            aref.Tell(summary);
            foreach (var response in responses)
            {
                aref.Tell(response);
            }
        }
        
        /// <summary>
        /// Perform write of persistent event with specified <paramref name="tags"/> 
        /// into database using given <paramref name="command"/>.
        /// </summary>
        /// <param name="command">Database command object used to store data.</param>
        /// <param name="persistent">Persistent event representation.</param>
        /// <param name="tags">Optional tags extracted from peristent event payload.</param>
        protected virtual void WriteEvent(TCommand command, IPersistentRepresentation persistent, string tags = "")
        {
            var payloadType = persistent.Payload.GetType();
            var manifest = string.IsNullOrEmpty(persistent.Manifest)
                ? payloadType.TypeQualifiedName()
                : persistent.Manifest;
            var serializer = _getSerializer(payloadType);
            var binary = serializer.ToBinary(persistent.Payload);

            AddParameter(command, "@PersistenceId", DbType.String, persistent.PersistenceId);
            AddParameter(command, "@SequenceNr", DbType.Int64, persistent.SequenceNr);
            AddParameter(command, "@Timestamp", DbType.Int64, 0L);
            AddParameter(command, "@IsDeleted", DbType.Boolean, false);
            AddParameter(command, "@Manifest", DbType.String, manifest);
            AddParameter(command, "@Payload", DbType.Binary, binary);
            AddParameter(command, "@Tag", DbType.String, tags);
        }

        /// <summary>
        /// Returns a persitent representation of an event read from a current row in the database.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        protected virtual IPersistentRepresentation ReadEvent(DbDataReader reader)
        {
            var persistenceId = reader.GetString(PersistenceIdIndex);
            var sequenceNr = reader.GetInt64(SequenceNrIndex);
            var isDeleted = reader.GetBoolean(IsDeletedIndex);
            var manifest = reader.GetString(ManifestIndex);
            var payload = reader[PayloadIndex];

            var type = Type.GetType(manifest, true);
            var deserializer = _getSerializer(type);
            var deserialized = deserializer.FromBinary((byte[])payload, type);

            var persistent = new Persistent(deserialized, sequenceNr, persistenceId, manifest, isDeleted, ActorRefs.NoSender, null);
            return persistent;
        }

        /// <summary>
        /// Creates a new database connection from a given <paramref name="connectionString"/>.
        /// </summary>
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
            command.Parameters.Add(param);
        }

        private RequestChunk DequeueChunk(int chunkId)
        {
            var operationsCount = Math.Min(Buffer.Count, Setup.MaxBatchSize);
            var array = new IJournalRequest[operationsCount];
            for (int i = 0; i < operationsCount; i++)
            {
                var req = Buffer.Dequeue();
                array[i] = req;
            }

            return new RequestChunk(chunkId, array);
        }

        private void CompleteBatch(BatchComplete msg)
        {
            _remainingOperations++;
            if (msg.Cause != null)
            {
                Log.Error(msg.Cause, "An error occurred during event batch processing (chunkId: {0})", msg.ChunkId);
            }
            else Log.Debug("Completed batch (chunkId: {0}) of {1} operations in {2} milliseconds", msg.ChunkId, msg.OperationCount, msg.TimeSpent.TotalMilliseconds);

            TryProcess();
        }
    }

    public class JournalBufferOverflowException : AkkaException
    {
        public static readonly JournalBufferOverflowException Instance = new JournalBufferOverflowException();

        public JournalBufferOverflowException() : base(
            "Batching journal buffer has been overflowed. This may happen as an effect of burst of persistent actors "
            + "requests incoming faster than the underlying database is able to fullfil them. You may modify "
            + "`max-buffer-size`, `max-batch-size` and `max-concurrent-operations` HOCON settings in order to "
            + " change it.")
        {
        }
    }
}