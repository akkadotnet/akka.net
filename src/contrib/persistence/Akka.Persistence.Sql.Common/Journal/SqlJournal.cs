//-----------------------------------------------------------------------
// <copyright file="SqlJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Configuration;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.Persistence.Sql.Common.Queries;

namespace Akka.Persistence.Sql.Common.Journal
{
    public abstract class SqlJournal : AsyncWriteJournal, IWithUnboundedStash
    {
        private readonly Dictionary<string, ISet<IActorRef>> _persistenceIdSubscribers = new Dictionary<string, ISet<IActorRef>>();
        private readonly Dictionary<string, ISet<IActorRef>> _tagSubscribers = new Dictionary<string, ISet<IActorRef>>();
        private readonly HashSet<IActorRef> _allPersistenceIdSubscribers = new HashSet<IActorRef>();
        private readonly ReaderWriterLockSlim _allPersistenceIdsLock = new ReaderWriterLockSlim();
        private HashSet<string> _allPersistenceIds = new HashSet<string>();
        private IImmutableDictionary<string, long> _tagSequenceNr = ImmutableDictionary<string, long>.Empty;

        private readonly CancellationTokenSource _pendingRequestsCancellation;
        private JournalSettings _settings;

        private ILoggingAdapter _log;

        protected SqlJournal(Config journalConfig)
        {
            _settings = new JournalSettings(journalConfig);
            _pendingRequestsCancellation = new CancellationTokenSource();
        }

        public IStash Stash { get; set; }

        public IEnumerable<string> AllPersistenceIds => _allPersistenceIds;

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
        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        /// <summary>
        /// Initializes a database connection.
        /// </summary>
        protected abstract DbConnection CreateDbConnection(string connectionString);

        /// <summary>
        /// Used for generating SQL commands for journal-related database operations.
        /// </summary>
        public abstract IJournalQueryExecutor QueryExecutor { get; }

        protected override bool ReceivePluginInternal(object message)
        {
            return message.Match()
                .With<ReplayTaggedMessages>(replay =>
                {
                    ReplayTaggedMessagesAsync(replay)
                    .PipeTo(replay.ReplyTo, success: h => new RecoverySuccess(h), failure: e => new ReplayMessagesFailure(e));
                })
                .With<SubscribePersistenceId>(subscribe =>
                {
                    AddPersistenceIdSubscriber(Sender, subscribe.PersistenceId);
                    Context.Watch(Sender);
                })
                .With<SubscribeAllPersistenceIds>(subscribe =>
                {
                    AddAllPersistenceIdSubscriber(Sender);
                    Context.Watch(Sender);
                })
                .With<SubscribeTag>(subscribe =>
                {
                    AddTagSubscriber(Sender, subscribe.Tag);
                    Context.Watch(Sender);
                })
                .With<Terminated>(terminated => RemoveSubscriber(terminated.ActorRef))
                .With<Query>(query => HandleEventQuery(query))
                .WasHandled;
        }

        /// <summary>
        /// Asynchronously writes all persistent <paramref name="messages"/> inside SQL Server database.
        /// 
        /// Specific table used for message persistence may be defined through configuration within 
        /// 'akka.persistence.journal.sql-server' scope with 'schema-name' and 'table-name' keys.
        /// </summary>
        protected override async Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            var persistenceIds = new HashSet<string>();
            var allTags = new HashSet<string>();

            var writeTasks = messages.Select(async message =>
            {
                using (var connection = CreateDbConnection())
                {
                    await connection.OpenAsync();

                    var eventToTags = new Dictionary<IPersistentRepresentation, IImmutableSet<string>>();
                    var persistentMessages = ((IImmutableList<IPersistentRepresentation>)message.Payload).ToArray();
                    for (int i = 0; i < persistentMessages.Length; i++)
                    {
                        var p = persistentMessages[i];
                        if (p.Payload is Tagged)
                        {
                            var tagged = (Tagged)p.Payload;
                            persistentMessages[i] = p = p.WithPayload(tagged.Payload);
                            if (tagged.Tags.Count != 0)
                            {
                                allTags.UnionWith(tagged.Tags);
                                eventToTags.Add(p, tagged.Tags);
                            }
                            else eventToTags.Add(p, ImmutableHashSet<string>.Empty);
                        }
                        else eventToTags.Add(p, ImmutableHashSet<string>.Empty);

                        if (IsTagId(p.PersistenceId))
                            throw new InvalidOperationException($"Persistence Id {p.PersistenceId} must not start with {QueryExecutor.Configuration.TagsColumnName}");

                        NotifyNewPersistenceIdAdded(p.PersistenceId);
                    }

                    var batch = new WriteJournalBatch(eventToTags);
                    await QueryExecutor.InsertBatchAsync(connection, _pendingRequestsCancellation.Token, batch);
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
        /// Replays all events with given tag withing provided boundaries from current database.
        /// </summary>
        protected virtual async Task<long> ReplayTaggedMessagesAsync(ReplayTaggedMessages replay)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                return await QueryExecutor
                    .SelectByTagAsync(connection, _pendingRequestsCancellation.Token, replay.Tag, replay.FromOffset, replay.ToOffset, replay.Max, replayedTagged => {
                        foreach(var adapted in AdaptFromJournal(replayedTagged.Persistent))
                        { 
                            replay.ReplyTo.Tell(new ReplayedTaggedMessage(adapted, replayedTagged.Tag, replayedTagged.Offset), ActorRefs.NoSender);
                        }
                    });
            }
        }

        public override async Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            NotifyNewPersistenceIdAdded(persistenceId);
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                await QueryExecutor.SelectByPersistenceIdAsync(connection, _pendingRequestsCancellation.Token, persistenceId, fromSequenceNr, toSequenceNr, max, recoveryCallback);
            }
        }

        protected override void PreStart()
        {
            base.PreStart();
            Initialize().PipeTo(Self);
            BecomeStacked(WaitingForInitialization);
        }

        protected override void PostStop()
        {
            base.PostStop();
            _pendingRequestsCancellation.Cancel();
        }

        protected bool WaitingForInitialization(object message)
        {
            return message.Match()
                .With<AllPersistenceIds>(all =>
                {
                    _allPersistenceIds = new HashSet<string>(all.Ids);
                    UnbecomeStacked();
                    Stash.UnstashAll();
                })
                .With<Failure>(fail =>
                {
                    Log.Error(fail.Exception, "Failure during {0} initialization.", Self);
                    Context.Stop(Self);
                })
                .Default(_ => Stash.Stash())
                .WasHandled;
        }

        private async Task<AllPersistenceIds> Initialize()
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();

                if (_settings.AutoInitialize)
                {
                    await QueryExecutor.CreateTablesAsync(connection, _pendingRequestsCancellation.Token);
                }

                var ids = await QueryExecutor.SelectAllPersistenceIdsAsync(connection, _pendingRequestsCancellation.Token);
                return new AllPersistenceIds(ids);
            }
        }

        public DbConnection CreateDbConnection()
        {
            var connectionString = GetConnectionString();
            return CreateDbConnection(connectionString);
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

        private async Task<long> NextTagSequenceNr(string tag)
        {
            long value;
            if (!_tagSequenceNr.TryGetValue(tag, out value))
            {
                value = await ReadHighestSequenceNrAsync(TagId(tag), 0L);
            }
            value++;
            _tagSequenceNr = _tagSequenceNr.SetItem(tag, value);
            return value;
        }

        private string TagId(string tag) => QueryExecutor.Configuration.TagsColumnName + tag;

        private void NotifyNewPersistenceIdAdded(string persistenceId)
        {
            var isNew = TryAddPersistenceId(persistenceId);
            if (isNew && HasAllPersistenceIdSubscribers && !IsTagId(persistenceId))
            {
                var added = new PersistenceIdAdded(persistenceId);
                foreach (var subscriber in _allPersistenceIdSubscribers)
                    subscriber.Tell(added);
            }
        }

        private bool TryAddPersistenceId(string persistenceId)
        {
            try
            {
                _allPersistenceIdsLock.EnterUpgradeableReadLock();

                if (_allPersistenceIds.Contains(persistenceId)) return false;
                else
                {
                    try
                    {
                        _allPersistenceIdsLock.EnterWriteLock();
                        _allPersistenceIds.Add(persistenceId);
                        return true;
                    }
                    finally
                    {
                        _allPersistenceIdsLock.ExitWriteLock();
                    }
                }
            }
            finally
            {
                _allPersistenceIdsLock.ExitUpgradeableReadLock();
            }
        }

        private bool IsTagId(string persistenceId)
        {
            return persistenceId.StartsWith(QueryExecutor.Configuration.TagsColumnName);
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

        /// <summary>
        /// Asynchronously deletes all persisted messages identified by provided <paramref name="persistenceId"/>
        /// up to provided message sequence number (inclusive).
        /// </summary>
        protected override async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            NotifyNewPersistenceIdAdded(persistenceId);
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                await QueryExecutor.DeleteBatchAsync(connection, _pendingRequestsCancellation.Token, persistenceId, toSequenceNr);
            }
        }

        /// <summary>
        /// Asynchronously reads a highest sequence number of the event stream related with provided <paramref name="persistenceId"/>.
        /// </summary>
        public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            NotifyNewPersistenceIdAdded(persistenceId);
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                return await QueryExecutor.SelectHighestSequenceNrAsync(connection, _pendingRequestsCancellation.Token, persistenceId);
            }
        }

        /// <summary>
        /// Returns connection string from either HOCON configuration or &lt;connectionStrings&gt; section of app.config.
        /// </summary>
        protected virtual string GetConnectionString()
        {
            var connectionString = _settings.ConnectionString;
            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = ConfigurationManager.ConnectionStrings[_settings.ConnectionStringName].ConnectionString;
            }

            return connectionString;
        }

        #region obsoleted
        
        protected ITimestampProvider GetTimestampProvider(string typeName)
        {
            var type = Type.GetType(typeName, true);
            try
            {
                return (ITimestampProvider)Activator.CreateInstance(type, Context.System);
            }
            catch (Exception)
            {
                return (ITimestampProvider)Activator.CreateInstance(type);
            }
        }

        [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
        private void HandleEventQuery(Query query)
        {
            var queryId = query.QueryId;
            var sender = Context.Sender;
            ReadEvents(queryId, query.Hints, Context.Sender, reply =>
            {
                foreach (var adapted in AdaptFromJournal(reply))
                {
                    sender.Tell(new QueryResponse(queryId, adapted));
                }
            })
            .ContinueWith(task =>
                task.IsFaulted || task.IsCanceled ? (IQueryReply)new QueryFailure(queryId, task.Exception) : new QuerySuccess(queryId),
                TaskContinuationOptions.ExecuteSynchronously)
            .PipeTo(Context.Sender);
        }

        /// <summary>
        /// Performs
        /// </summary>
        [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
        private async Task ReadEvents(object queryId, IEnumerable<IHint> hints, IActorRef sender, Action<IPersistentRepresentation> replayCallback)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                await QueryExecutor.SelectEventsAsync(connection, _pendingRequestsCancellation.Token, hints, replayCallback);
            }
        }

        #endregion
    }
}