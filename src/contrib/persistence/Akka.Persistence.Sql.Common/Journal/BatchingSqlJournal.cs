using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
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

    public abstract class BatchingSqlJournalSetup
    {
        public string ConnectionString { get; }
        public int MaxConcurrentOperations { get; }
        public int MaxBatchSize { get; }
        public bool AutoInitialize { get; }
        public TimeSpan ConnectionTimeout { get; }
        public CircuitBreakerSettings CircuitBreakerSettings { get; }
        public QueryConfiguration NamingConventions { get; }

        protected BatchingSqlJournalSetup(string connectionString, int maxConcurrentOperations, int maxBatchSize, bool autoInitialize, TimeSpan connectionTimeout, CircuitBreakerSettings circuitBreakerSettings, QueryConfiguration namingConventions)
        {
            ConnectionString = connectionString;
            MaxConcurrentOperations = maxConcurrentOperations;
            MaxBatchSize = maxBatchSize;
            AutoInitialize = autoInitialize;
            ConnectionTimeout = connectionTimeout;
            CircuitBreakerSettings = circuitBreakerSettings;
            NamingConventions = namingConventions;
        }
    }

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

        protected const int PersistenceIdIndex = 0;
        protected const int SequenceNrIndex = 1;
        protected const int TimestampIndex = 2;
        protected const int IsDeletedIndex = 3;
        protected const int ManifestIndex = 4;
        protected const int PayloadIndex = 5;
        protected const int OrderingIndex = 6;

        protected virtual string DeleteBatchSql { get; }
        protected virtual string HighestSequenceNrSql { get; }
        protected virtual string InsertEventSql { get; }
        protected virtual string QueryEventsSql { get; }
        protected virtual string AllPersistenceIdsSql { get; }
        protected virtual string UpdateSequenceNrSql { get; }
        protected virtual string ByPersistenceIdSql { get; }
        protected virtual string ByTagSql { get; }
        protected abstract ImmutableDictionary<string, string> Initializers { get; }
        protected BatchingSqlJournalSetup Setup { get; }
        protected bool HasPersistenceIdSubscribers => _persistenceIdSubscribers.Count != 0;
        protected bool HasTagSubscribers => _tagSubscribers.Count != 0;
        protected bool HasAllIdsSubscribers => _allIdsSubscribers.Count != 0;
        protected int BufferSize => _buffer.Count;
        protected readonly bool CanPublish;
        protected readonly ILoggingAdapter Log;

        private readonly Dictionary<string, HashSet<IActorRef>> _persistenceIdSubscribers;
        private readonly Dictionary<string, HashSet<IActorRef>> _tagSubscribers;
        private readonly HashSet<IActorRef> _allIdsSubscribers;
        private readonly HashSet<string> _allPersistenceIds;

        private readonly Queue<IJournalRequest> _buffer;
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
            _buffer = new Queue<IJournalRequest>(Setup.MaxBatchSize);
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

            QueryEventsSql = $@"
                SELECT {allEventColumnNames}
                FROM {conventions.FullJournalTableName} e
                WHERE ";
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

        protected void BatchRequest(IJournalRequest message)
        {
            _buffer.Enqueue(message);
            TryProcess();
        }

        private void TryProcess()
        {
            if (_remainingOperations > 0 && _buffer.Count > 0)
            {
                _remainingOperations--;

                var chunk = DequeueChunk(_remainingOperations);
                var eventStream = Context.System.EventStream;
                _circuitBreaker.WithCircuitBreaker(() => ExecuteChunk(chunk, eventStream)).PipeTo(Self);
            }
        }

        private async Task<BatchComplete> ExecuteChunk(RequestChunk chunk, EventStream eventStream)
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
                                await HandleReplayMessages((ReplayMessages)req, command);
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
                                eventStream.Publish(chunk.Requests[i]);
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

        private async Task HandleReplayMessages(ReplayMessages req, TCommand command)
        {
            var persistentRef = req.PersistentActor;
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
                                persistentRef.Tell(new ReplayedMessage(adaptedRepresentation), ActorRefs.NoSender);
                            }
                        }
                    }
                }

                var response = new RecoverySuccess(highestSequenceNr);
                persistentRef.Tell(response, ActorRefs.NoSender);
            }
            catch (Exception cause)
            {
                var response = new ReplayMessagesFailure(cause);
                persistentRef.Tell(response, ActorRefs.NoSender);
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

        protected abstract TConnection CreateConnection(string connectionString);

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
            var operationsCount = Math.Min(_buffer.Count, Setup.MaxBatchSize);
            var array = new IJournalRequest[operationsCount];
            for (int i = 0; i < operationsCount; i++)
            {
                var req = _buffer.Dequeue();
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
}