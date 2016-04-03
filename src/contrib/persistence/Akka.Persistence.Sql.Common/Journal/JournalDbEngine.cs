//-----------------------------------------------------------------------
// <copyright file="JournalDbEngine.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Configuration;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.Persistence.Sql.Common.Queries;

namespace Akka.Persistence.Sql.Common.Journal
{
    /// <summary>
    /// Class used for storing intermediate result of the <see cref="IPersistentRepresentation"/>
    /// in form which is ready to be stored directly in the SQL table.
    /// </summary>
    public sealed class JournalEntry
    {
        public readonly string PersistenceId;
        public readonly long SequenceNr;
        public readonly bool IsDeleted;
        public readonly string Manifest;
        public readonly DateTime Timestamp;
        public readonly object Payload;

        public JournalEntry(string persistenceId, long sequenceNr, bool isDeleted, string manifest, DateTime timestamp, object payload)
        {
            PersistenceId = persistenceId;
            SequenceNr = sequenceNr;
            IsDeleted = isDeleted;
            Manifest = manifest;
            Payload = payload;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Class used to abstract SQL persistence capabilities for concrete implementation of actor journal.
    /// </summary>
    public abstract class JournalDbEngine : IDisposable
    {
        public const string TagPersistenceIdPrefix = "$$$";

        /// <summary>
        /// Settings applied to journal mapped from HOCON config file.
        /// </summary>
        public readonly JournalSettings Settings;
        
        /// <summary>
        /// Timestamp provider used for generation of timestamps for incoming persistent messages.
        /// </summary>
        protected readonly ITimestampProvider TimestampProvider;

        private readonly IDictionary<string, ISet<IActorRef>> _persistenceIdSubscribers = new Dictionary<string, ISet<IActorRef>>();
        private readonly IDictionary<string, ISet<IActorRef>> _tagSubscribers = new Dictionary<string, ISet<IActorRef>>();
        private readonly ISet<IActorRef> _allPersistenceIdSubscribers = new HashSet<IActorRef>();
        private readonly IDictionary<string, long> _tagSequenceNr = new Dictionary<string, long>();
        private readonly ConcurrentDictionary<string, int> _idMap = new ConcurrentDictionary<string, int>();

        private readonly ActorSystem _system;
        private readonly CancellationTokenSource _pendingRequestsCancellation;

        protected JournalDbEngine(ActorSystem system)
        {
            _system = system;

            Settings = new JournalSettings(system.Settings.Config.GetConfig(JournalConfigPath));
            QueryMapper = new DefaultJournalQueryMapper(_system.Serialization);
            TimestampProvider = CreateTimestampProvider();

            _pendingRequestsCancellation = new CancellationTokenSource();
        }

        public IEnumerable<string> AllPersistenceIds => _idMap.Keys;

        protected bool HasPersistenceIdSubscribers => _persistenceIdSubscribers.Count != 0;
        protected bool HasTagSubscribers => _tagSubscribers.Count != 0;
        protected bool HasAllPersistenceIdSubscribers => _allPersistenceIdSubscribers.Count != 0;

        /// <summary>
        /// Returns a HOCON config path to associated journal.
        /// </summary>
        protected abstract string JournalConfigPath { get; }

        /// <summary>
        /// System logger.
        /// </summary>
        protected ILoggingAdapter Log { get { return _system.Log; } }

        /// <summary>
        /// Initializes a database connection.
        /// </summary>
        protected abstract DbConnection CreateDbConnection(string connectionString);

        /// <summary>
        /// Copies values from entities to database command.
        /// </summary>
        /// <param name="sqlCommand"></param>
        /// <param name="entry"></param>
        protected abstract void CopyParamsToCommand(DbCommand sqlCommand, JournalEntry entry);

        /// <summary>
        /// Used for generating SQL commands for journal-related database operations.
        /// </summary>
        public IJournalQueryBuilder QueryBuilder { get; set; }

        /// <summary>
        /// Used for mapping results returned from database into <see cref="IPersistentRepresentation"/> objects.
        /// </summary>
        public IJournalQueryMapper QueryMapper { get; set; }

        public DbConnection CreateDbConnection()
        {
            var connectionString = GetConnectionString();
            return CreateDbConnection(connectionString);
        }

        /// <summary>
        /// Closes database connection if exists.
        /// </summary>
        public void Close()
        {
            _pendingRequestsCancellation.Cancel();
        }

        void IDisposable.Dispose()
        {
            Close();
        }

        /// <summary>
        /// Performs
        /// </summary>
        [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
        public async Task ReadEvents(object queryId, IEnumerable<IHint> hints, IActorRef sender, Action<IPersistentRepresentation> replayCallback)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();

                var sqlCommand = QueryBuilder.SelectEvents(hints);
                CompleteCommand(sqlCommand, connection);
                
                var reader = await sqlCommand.ExecuteReaderAsync(_pendingRequestsCancellation.Token);
                try
                {
                    while (reader.Read())
                    {
                        var persistent = QueryMapper.Map(reader, sender);
                        if (persistent != null)
                            replayCallback(persistent);
                    }
                }
                finally
                {
                    reader.Close();
                }
            }
        }

        /// <summary>
        /// Asynchronously replays all requested messages related to provided <paramref name="persistenceId"/>,
        /// using provided sequence ranges (inclusive) with <paramref name="max"/> number of messages replayed
        /// (counting from the beginning). Replay callback is invoked for each replayed message.
        /// </summary>
        /// <param name="persistenceId">Identifier of persistent messages stream to be replayed.</param>
        /// <param name="fromSequenceNr">Lower inclusive sequence number bound. Unbound by default.</param>
        /// <param name="toSequenceNr">Upper inclusive sequence number bound. Unbound by default.</param>
        /// <param name="max">Maximum number of messages to be replayed. Unbound by default.</param>
        /// <param name="recoveryCallback">Action invoked for each replayed message.</param>
        public async Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, IActorRef sender, Action<IPersistentRepresentation> recoveryCallback)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();

                var sqlCommand = QueryBuilder.SelectMessages(persistenceId, fromSequenceNr, toSequenceNr, max);
                CompleteCommand(sqlCommand, connection);
                
                var reader = await sqlCommand.ExecuteReaderAsync(_pendingRequestsCancellation.Token);

                try
                {
                    while (reader.Read())
                    {
                        var persistent = QueryMapper.Map(reader, sender);
                        if (persistent != null)
                            recoveryCallback(persistent);
                    }
                }
                finally
                {
                    reader.Close();
                }
            }
        }

        /// <summary>
        /// Asynchronously reads a highest sequence number of the event stream related with provided <paramref name="persistenceId"/>.
        /// </summary>
        public async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();

                var sqlCommand = QueryBuilder.SelectHighestSequenceNr(persistenceId);
                CompleteCommand(sqlCommand, connection);

                var seqNr = await sqlCommand.ExecuteScalarAsync(_pendingRequestsCancellation.Token);
                return seqNr is long ? Convert.ToInt64(seqNr) : 0L;
            }
        }

        /// <summary>
        /// Asynchronously writes all persistent <paramref name="messages"/> inside SQL Server database.
        /// 
        /// Specific table used for message persistence may be defined through configuration within 
        /// 'akka.persistence.journal.sql-server' scope with 'schema-name' and 'table-name' keys.
        /// </summary>
        public async Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            var persistenceIds = new HashSet<string>();
            var allTags = new HashSet<string>();

            var writeTasks = messages.Select(async message =>
            {
                using (var connection = CreateDbConnection())
                {
                    await connection.OpenAsync();

                    var persistentMessages = ((IImmutableList<IPersistentRepresentation>) message.Payload).ToArray();
                    for (int i = 0; i < persistentMessages.Length; i++)
                    {
                        var p = persistentMessages[i];
                        if (p.Payload is Tagged)
                        {
                            var tagged = (Tagged) p.Payload;
                            persistentMessages[i] = p.WithPayload(tagged.Payload);
                            if (tagged.Tags.Count != 0 && HasTagSubscribers)
                            {
                                allTags.UnionWith(tagged.Tags);
                            }
                        }

                        if (HasPersistenceIdSubscribers)
                        {
                            persistenceIds.Add(p.PersistenceId);
                        }
                    }
                    var sqlCommand = QueryBuilder.InsertBatchMessages(persistentMessages);
                    CompleteCommand(sqlCommand, connection);

                    var journalEntries = persistentMessages.Select(ToJournalEntry).ToList();
                    await InsertInTransactionAsync(sqlCommand, journalEntries);
                }
            });

            var result = await Task<IImmutableList<Exception>>
                .Factory
                .ContinueWhenAll(writeTasks.ToArray(),
                    tasks => tasks.Select(t => t.IsFaulted ? TryUnwrapException(t.Exception) : null).ToImmutableList());

            if (HasPersistenceIdSubscribers)
            {
                foreach (var persistenceId in persistenceIds)
                {
                    NotifyPersistenceIdChange(persistenceId);
                }
            }

            if (HasTagSubscribers && allTags.Count != 0)
            {
                foreach (var tag in allTags)
                {
                    NotifyTagChange(tag);
                }
            }

            return result;
        }

        /// <summary>
        /// Asynchronously deletes all persisted messages identified by provided <paramref name="persistenceId"/>
        /// up to provided message sequence number (inclusive).
        /// </summary>
        public async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();

                var sqlCommand = QueryBuilder.DeleteBatchMessages(persistenceId, toSequenceNr);
                CompleteCommand(sqlCommand, connection);

                await sqlCommand.ExecuteNonQueryAsync();
            }
        }

        private Exception TryUnwrapException(Exception e)
        {
            var aggregateException = e as AggregateException;
            if (aggregateException != null)
            {
                aggregateException = aggregateException.Flatten();
                if (aggregateException.InnerExceptions.Count == 1)
                    return aggregateException.InnerExceptions[0];
            }
            return e;
        }

        /// <summary>
        /// Returns connection string from either HOCON configuration or &lt;connectionStrings&gt; section of app.config.
        /// </summary>
        protected virtual string GetConnectionString()
        {
            var connectionString = Settings.ConnectionString;
            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = ConfigurationManager.ConnectionStrings[Settings.ConnectionStringName].ConnectionString;
            }

            return connectionString;
        }

        private void CompleteCommand(DbCommand sqlCommand, DbConnection connection)
        {
            sqlCommand.Connection = connection;
            sqlCommand.CommandTimeout = (int)Settings.ConnectionTimeout.TotalMilliseconds;
        }

        private JournalEntry ToJournalEntry(IPersistentRepresentation message)
        {
            var payloadType = message.Payload.GetType();
            var serializer = _system.Serialization.FindSerializerForType(payloadType);
            var manifest = string.IsNullOrEmpty(message.Manifest)
                ? payloadType.QualifiedTypeName()
                : message.Manifest;
            var timestamp = TimestampProvider.GenerateTimestamp(message);
            var payload = serializer.ToBinary(message.Payload);

            return new JournalEntry(message.PersistenceId, message.SequenceNr, message.IsDeleted, manifest,
                timestamp, payload);
        }

        private ITimestampProvider CreateTimestampProvider()
        {
            var type = Type.GetType(Settings.TimestampProvider, true);
            var instance = Activator.CreateInstance(type);
            return (ITimestampProvider) instance;
        }

        private async Task InsertInTransactionAsync(DbCommand sqlCommand, IEnumerable<JournalEntry> journalEntries)
        {
            using (var tx = sqlCommand.Connection.BeginTransaction())
            {
                sqlCommand.Transaction = tx;
                try
                {
                    foreach (var entry in journalEntries)
                    {
                        CopyParamsToCommand(sqlCommand, entry);

                        var result = await sqlCommand.ExecuteNonQueryAsync();
                        if (result != 1)
                        {
                            Log.Error("Persisted event operation was expected to return 1, but returned [{0}]", result);
                        }
                    }

                    tx.Commit();
                }
                catch (Exception)
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        private void NotifyPersistenceIdChange(string persistenceId)
        {
            ISet<IActorRef> subscribers;
            if (_persistenceIdSubscribers.TryGetValue(persistenceId, out subscribers))
            {
                var changed = new EventAppended(persistenceId);
                foreach (var subscriber in subscribers)
                    subscriber.Tell(changed);
            }
        }

        private void NotifyTagChange(string tag)
        {
            ISet<IActorRef> subscribers;
            if (_tagSubscribers.TryGetValue(tag, out subscribers))
            {
                var changed = new TaggedEventAppended(tag);
                foreach (var subscriber in subscribers)
                    subscriber.Tell(changed);
            }
        }

        public void RemoveSubscriber(IActorRef subscriber)
        {
            var pidSubscriptions = _persistenceIdSubscribers.Values.Where(x => x.Contains(subscriber));
            foreach (var subscription in pidSubscriptions)
                subscription.Remove(subscriber);

            var tagSubscriptions = _tagSubscribers.Values.Where(x => x.Contains(subscriber));
            foreach (var subscription in tagSubscriptions)
                subscription.Remove(subscriber);

            _allPersistenceIdSubscribers.Remove(subscriber);
        }

        public void AddTagSubscriber(IActorRef subscriber, string tag)
        {
            ISet<IActorRef> subscriptions;
            if (!_tagSubscribers.TryGetValue(tag, out subscriptions))
            {
                subscriptions = new HashSet<IActorRef>();
                _tagSubscribers.Add(tag, subscriptions);
            }

            subscriptions.Add(subscriber);
        }

        public void AddAllPersistenceIdSubscriber(IActorRef subscriber)
        {
            _allPersistenceIdSubscribers.Add(subscriber);
            subscriber.Tell(new CurrentPersistenceIds(AllPersistenceIds));
        }

        public void AddPersistenceIdSubscriber(IActorRef subscriber, string persistenceId)
        {
            ISet<IActorRef> subscriptions;
            if (!_persistenceIdSubscribers.TryGetValue(persistenceId, out subscriptions))
            {
                subscriptions = new HashSet<IActorRef>();
                _persistenceIdSubscribers.Add(persistenceId, subscriptions);
            }

            subscriptions.Add(subscriber);
        }

        public string TagAsPersistenceId(string tag) => TagPersistenceIdPrefix + tag;

        public Task ReplayTaggedMessagesAsync(string tag, long fromSequenceNr, long toSeqNr, long max, Action<ReplayedTaggedMessage> callback)
        {
            var tagNumericId = TagNumericId(tag);
            throw new NotImplementedException();
        }

        private string TagNumericId(string tag)
        {
            return NumericId(TagAsPersistenceId(tag));
        }

        private string NumericId(string tag)
        {
            throw new NotImplementedException();
        }
    }
}