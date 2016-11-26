using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Pattern;
using Akka.Persistence.Journal;
using Akka.Persistence.Sql.Common.Journal;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Persistence.Sqlite.Journal
{
    public class BatchingSqliteJournal : WriteJournalBase
    {
        #region internal classes

        private sealed class BatchComplete
        {
            public readonly Exception Cause;

            public static readonly BatchComplete Successful = new BatchComplete(null);
            public BatchComplete(Exception cause)
            {
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
            public readonly bool IsReadOnly;
            public readonly IJournalRequest[] Requests;

            public RequestChunk(bool isReadOnly, IJournalRequest[] requests)
            {
                IsReadOnly = isReadOnly;
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

        protected readonly string DeleteBatchSql;
        protected readonly string HighestSequenceNrSql;
        protected readonly string InsertEventSql;
        protected readonly string QueryEventsSql;
        protected readonly string AllPersistenceIdsSql;
        protected readonly string UpdateSequenceNrSql;
        protected readonly string ByPersistenceIdSql;
        protected readonly string ByTagSql;
        protected readonly string CreateJournalSql;
        protected readonly string CreateMetadataSql;
        protected readonly ILoggingAdapter Log;

        protected readonly bool CanPublish;

        private readonly Dictionary<string, HashSet<IActorRef>> _persistenceIdSubscribers;
        private readonly Dictionary<string, HashSet<IActorRef>> _tagSubscribers;
        private readonly HashSet<IActorRef> _allIdsSubscribers;
        private readonly HashSet<string> _allPersistenceIds;

        private readonly Queue<IJournalRequest> _buffer;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Func<Type, Serializer> _getSerializer;
        private int _remainingOperations;
        private readonly CircuitBreaker _circuitBreaker;

        /// <summary>
        /// Special SQLite connection for the journal lifetime in order to keep in-memory SQLite database alive.
        /// </summary>
        private readonly SQLiteConnection _anchor;

        protected BatchingSqliteJournalSettings BatchingJournalSettings { get; }
        protected bool HasPersistenceIdSubscribers => _persistenceIdSubscribers.Count != 0;
        protected bool HasTagSubscribers => _tagSubscribers.Count != 0;
        protected bool HasAllIdsSubscribers => _allIdsSubscribers.Count != 0;

        public BatchingSqliteJournal(Config config)
        {
            BatchingJournalSettings = BatchingSqliteJournalSettings.Create(config);
            CanPublish = Persistence.Instance.Apply(Context.System).Settings.Internal.PublishPluginCommands;

            _persistenceIdSubscribers = new Dictionary<string, HashSet<IActorRef>>();
            _tagSubscribers = new Dictionary<string, HashSet<IActorRef>>();
            _allIdsSubscribers = new HashSet<IActorRef>();
            _allPersistenceIds = new HashSet<string>();

            _remainingOperations = BatchingJournalSettings.MaxConcurrentOperations;
            _buffer = new Queue<IJournalRequest>(BatchingJournalSettings.MaxBatchSize);
            _cancellationTokenSource = new CancellationTokenSource(BatchingJournalSettings.ConnectionTimeout);
            _getSerializer = Context.System.Serialization.FindSerializerFor;
            Log = Context.GetLogger();
            _circuitBreaker = CircuitBreaker.Create(
                maxFailures: BatchingJournalSettings.CircuitBreakerSettings.MaxFailures,
                callTimeout: BatchingJournalSettings.CircuitBreakerSettings.CallTimeout,
                resetTimeout: BatchingJournalSettings.CircuitBreakerSettings.ResetTimeout);
            _anchor = CreateConnection();
            _anchor.Open();

            var configuration = new QueryConfiguration(
                schemaName: null,
                journalEventsTableName: config.GetString("table-name"),
                metaTableName: config.GetString("metadata-table-name"),
                persistenceIdColumnName: "persistence_id",
                sequenceNrColumnName: "sequence_nr",
                payloadColumnName: "payload",
                manifestColumnName: "manifest",
                timestampColumnName: "timestamp",
                isDeletedColumnName: "is_deleted",
                tagsColumnName: "tags",
                orderingColumnName: "ordering",
                timeout: config.GetTimeSpan("connection-timeout"));

            var allEventColumnNames = $@"
                e.{configuration.PersistenceIdColumnName} as PersistenceId, 
                e.{configuration.SequenceNrColumnName} as SequenceNr, 
                e.{configuration.TimestampColumnName} as Timestamp, 
                e.{configuration.IsDeletedColumnName} as IsDeleted, 
                e.{configuration.ManifestColumnName} as Manifest, 
                e.{configuration.PayloadColumnName} as Payload";

            AllPersistenceIdsSql = $@"
                SELECT DISTINCT e.{configuration.PersistenceIdColumnName} as PersistenceId 
                FROM {configuration.FullJournalTableName} e;";

            HighestSequenceNrSql = $@"
                SELECT MAX(u.SeqNr) as SequenceNr 
                FROM (
                    SELECT e.{configuration.SequenceNrColumnName} as SeqNr FROM {configuration.FullJournalTableName} e WHERE e.{configuration.PersistenceIdColumnName} = @PersistenceId
                    UNION
                    SELECT m.{configuration.SequenceNrColumnName} as SeqNr FROM {configuration.FullMetaTableName} m WHERE m.{configuration.PersistenceIdColumnName} = @PersistenceId) as u";

            DeleteBatchSql = $@"
                DELETE FROM {configuration.FullJournalTableName} 
                WHERE {configuration.PersistenceIdColumnName} = @PersistenceId AND {configuration.SequenceNrColumnName} <= @ToSequenceNr;";

            UpdateSequenceNrSql = $@"
                INSERT INTO {configuration.FullMetaTableName} ({configuration.PersistenceIdColumnName}, {configuration.SequenceNrColumnName}) 
                VALUES (@PersistenceId, @SequenceNr);";

            ByPersistenceIdSql =
                $@"
                SELECT {allEventColumnNames}
                FROM {configuration.FullJournalTableName} e
                WHERE e.{configuration.PersistenceIdColumnName} = @PersistenceId
                AND e.{configuration.SequenceNrColumnName} BETWEEN @FromSequenceNr AND @ToSequenceNr;";

            ByTagSql =
                $@"
                SELECT {allEventColumnNames}, e.{configuration.OrderingColumnName} as Ordering
                FROM {configuration.FullJournalTableName} e
                WHERE e.{configuration.OrderingColumnName} > @Ordering AND e.{configuration.TagsColumnName} LIKE @Tag
                ORDER BY {configuration.OrderingColumnName} ASC";

            InsertEventSql = $@"
                INSERT INTO {configuration.FullJournalTableName} (
                    {configuration.PersistenceIdColumnName},
                    {configuration.SequenceNrColumnName},
                    {configuration.TimestampColumnName},
                    {configuration.IsDeletedColumnName},
                    {configuration.ManifestColumnName},
                    {configuration.PayloadColumnName},
                    {configuration.TagsColumnName}
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
                FROM {configuration.FullJournalTableName} e
                WHERE ";

            CreateJournalSql = $@"
                CREATE TABLE IF NOT EXISTS {configuration.FullJournalTableName} (
                    {configuration.OrderingColumnName} INTEGER PRIMARY KEY NOT NULL,
                    {configuration.PersistenceIdColumnName} VARCHAR(255) NOT NULL,
                    {configuration.SequenceNrColumnName} INTEGER(8) NOT NULL,
                    {configuration.IsDeletedColumnName} INTEGER(1) NOT NULL,
                    {configuration.ManifestColumnName} VARCHAR(255) NULL,
                    {configuration.TimestampColumnName} INTEGER NOT NULL,
                    {configuration.PayloadColumnName} BLOB NOT NULL,
                    {configuration.TagsColumnName} VARCHAR(2000) NULL,
                    UNIQUE ({configuration.PersistenceIdColumnName}, {configuration.SequenceNrColumnName})
                );";

            CreateMetadataSql = $@"
                CREATE TABLE IF NOT EXISTS {configuration.FullMetaTableName} (
                    {configuration.PersistenceIdColumnName} VARCHAR(255) NOT NULL,
                    {configuration.SequenceNrColumnName} INTEGER(8) NOT NULL,
                    PRIMARY KEY ({configuration.PersistenceIdColumnName}, {configuration.SequenceNrColumnName})
                );";
        }

        protected override void PreStart()
        {
            if (BatchingJournalSettings.AutoInitialize)
            {
                using (var connection = CreateConnection())
                using (var command = new SQLiteCommand(connection))
                {
                    connection.Open();

                    command.CommandText = CreateJournalSql;
                    command.ExecuteNonQuery();
                    command.CommandText = CreateMetadataSql;
                    command.ExecuteNonQuery();
                }
            }

            base.PreStart();
        }

        protected override void PostStop()
        {
            _anchor.Dispose();
            _cancellationTokenSource.Cancel(false);
            base.PostStop();
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
            else if (message is Terminated) RemoveSubscriber(((Terminated) message).ActorRef);
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
            GetAllPersistenceIdsAsync(_cancellationTokenSource.Token)
                .ContinueWith(task =>
                {
                    if (task.IsCanceled || task.IsFaulted)
                    {
                        var cause = (Exception) task.Exception ?? new OperationCanceledException("Cancellation occurred while trying to retrieve current persistence ids");
                        Log.Error(cause, "Couldn't retrieve current persistence ids");
                    }
                    else
                    {
                        self.Tell(new CurrentPersistenceIds(task.Result));
                    }
                });
        }

        private async Task<IEnumerable<string>>  GetAllPersistenceIdsAsync(CancellationToken token)
        {
            var result = new List<string>(256);
            using (var connection = CreateConnection())
            {
                await connection.OpenAsync(token);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = AllPersistenceIdsSql;

                    var reader = await command.ExecuteReaderAsync(token);
                    while (await reader.ReadAsync(token))
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

        private void BatchRequest(IJournalRequest message)
        {
            _buffer.Enqueue(message);
            TryProcess();
        }

        private void TryProcess()
        {
            if (_remainingOperations > 0 && _buffer.Count > 0)
            {
                _remainingOperations--;

                var chunk = DequeueChunk();
                _circuitBreaker.WithCircuitBreaker(() => ExecuteChunk(chunk))
                    .PipeTo(Self, failure: cause => new BatchComplete(cause ?? new OperationCanceledException($"Timeout occured while trying to execute {chunk.Requests.Length} operations.")));
            }
        }

        private async Task<BatchComplete> ExecuteChunk(RequestChunk chunk)
        {
            using (var connection = CreateConnection())
            {
                await connection.OpenAsync(_cancellationTokenSource.Token);

                using (var transaction = connection.BeginTransaction())
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    try
                    {
                        for (int i = 0; i < chunk.Requests.Length; i++)
                        {
                            var req = chunk.Requests[i];

                            if (req is WriteMessages)
                                await HandleWriteMessages((WriteMessages)req, command, _cancellationTokenSource.Token);
                            else if (req is ReplayMessages)
                                await HandleReplayMessages((ReplayMessages)req, command, _cancellationTokenSource.Token);
                            else if (req is ReadHighestSequenceNr)
                                await HandleReadHighestSequenceNr((ReadHighestSequenceNr)req, command, _cancellationTokenSource.Token);
                            else if (req is DeleteMessagesTo)
                                await HandleDeleteMessagesTo((DeleteMessagesTo)req, command, _cancellationTokenSource.Token);
                            else if (req is ReplayTaggedMessages)
                                await HandleReplayTaggedMessages((ReplayTaggedMessages)req, command, _cancellationTokenSource.Token);
                            else Unhandled(req);
                        }
                        
                        transaction.Commit();

                        if (CanPublish)
                        {
                            var eventStream = Context.System.EventStream;
                            for (int i = 0; i < chunk.Requests.Length; i++)
                            {
                                eventStream.Publish(chunk.Requests[i]);
                            }
                        }
                    }
                    catch (Exception cause)
                    {
                        transaction.Rollback();
                        return new BatchComplete(cause);
                    }
                }
            }

            return BatchComplete.Successful;
        }

        private async Task HandleDeleteMessagesTo(DeleteMessagesTo req, SQLiteCommand command, CancellationToken token)
        {
            var toSequenceNr = req.ToSequenceNr;
            var persistenceId = req.PersistenceId;

            NotifyNewPersistenceIdAdded(persistenceId);

            try
            {
                var highestSequenceNr = await ReadHighestSequenceNr(persistenceId, command, token);

                command.CommandText = DeleteBatchSql;
                command.Parameters.Clear();
                AddParameter(command, "PersistenceId", DbType.String, persistenceId);
                AddParameter(command, "ToSequenceNr", DbType.Int64, toSequenceNr);

                await command.ExecuteNonQueryAsync(token);

                if (highestSequenceNr <= toSequenceNr)
                {
                    command.CommandText = UpdateSequenceNrSql;
                    command.Parameters.Clear();

                    AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
                    AddParameter(command, "@SequenceNr", DbType.Int64, highestSequenceNr);

                    await command.ExecuteNonQueryAsync(token);
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

        private async Task HandleReadHighestSequenceNr(ReadHighestSequenceNr req, SQLiteCommand command, CancellationToken token)
        {
            var replyTo = req.PersistentActor;
            var persistenceId = req.PersistenceId;

            NotifyNewPersistenceIdAdded(persistenceId);

            try
            {
                var highestSequenceNr = await ReadHighestSequenceNr(persistenceId, command, token);

                var response = new ReadHighestSequenceNrSuccess(highestSequenceNr);
                replyTo.Tell(response, ActorRefs.NoSender);
            }
            catch (Exception cause)
            {
                var response = new ReadHighestSequenceNrFailure(cause);
                replyTo.Tell(response, ActorRefs.NoSender);
            }
        }

        private async Task<long> ReadHighestSequenceNr(string persistenceId, SQLiteCommand command, CancellationToken token)
        {
            command.CommandText = HighestSequenceNrSql;
            AddParameter(command, "PersistenceId", DbType.String, persistenceId);

            var result = await command.ExecuteScalarAsync(token);
            var highestSequenceNr = result is long ? Convert.ToInt64(result) : 0L;
            return highestSequenceNr;
        }

        private async Task HandleReplayTaggedMessages(ReplayTaggedMessages req, SQLiteCommand command, CancellationToken token)
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

                using (var reader = await command.ExecuteReaderAsync(token))
                {
                    while (await reader.ReadAsync(token))
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

        private async Task HandleReplayMessages(ReplayMessages req, SQLiteCommand command, CancellationToken token)
        {
            var persistentRef = req.PersistentActor;
            var persistenceId = req.PersistenceId;

            NotifyNewPersistenceIdAdded(persistenceId);

            try
            {
                var highestSequenceNr = await ReadHighestSequenceNr(persistenceId, command, token);
                var toSequenceNr = Math.Min(req.ToSequenceNr, highestSequenceNr);

                command.CommandText = ByPersistenceIdSql;
                command.Parameters.Clear();

                AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
                AddParameter(command, "@FromSequenceNr", DbType.Int64, req.FromSequenceNr);
                AddParameter(command, "@ToSequenceNr", DbType.Int64, toSequenceNr);

                using (var reader = await command.ExecuteReaderAsync(token))
                {
                    var i = 0L;
                    while ((i++) < req.Max && await reader.ReadAsync(token))
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

        private async Task HandleWriteMessages(WriteMessages req, SQLiteCommand command, CancellationToken token)
        {
            IJournalResponse summary;
            var responses = new List<IJournalResponse>();
            var tags = new HashSet<string>();
            var persistenceIds = new HashSet<string>();
            var actorInstanceId = req.ActorInstanceId;

            try
            {
                var resequenceCounter = 0;

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

                                resequenceCounter++;
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
                                AddParameter(command, "@Tag", DbType.String, tagBuilder.ToString());

                                await command.ExecuteNonQueryAsync(token);

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

        private Persistent ReadEvent(DbDataReader reader)
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

        private SQLiteConnection CreateConnection()
        {
            var connectionString = BatchingJournalSettings.ConnectionString;
            return new SQLiteConnection(connectionString);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddParameter(SQLiteCommand command, string paramName, DbType dbType, object value)
        {
            var param = command.CreateParameter();
            param.Value = value;
            param.ParameterName = paramName;
            param.DbType = dbType;
            command.Parameters.Add(param);
        }

        private RequestChunk DequeueChunk()
        {
            var operationsCount = Math.Min(_buffer.Count, BatchingJournalSettings.MaxBatchSize);
            var isReadOnly = false;
            var array = new IJournalRequest[operationsCount];
            for (int i = 0; i < operationsCount; i++)
            {
                var req = _buffer.Dequeue();
                if (req is ReplayMessages || req is ReadHighestSequenceNr) isReadOnly = true;
                array[i] = req;
            }

            return new RequestChunk(isReadOnly, array);
        }

        private void CompleteBatch(BatchComplete msg)
        {
            _remainingOperations++;
            if (msg.Cause != null)
            {
                Log.Error(msg.Cause, "An error occurred during event batch processing");    
            }
            else Log.Debug("Completed batch with success");

            TryProcess();
        }
    }
}