//-----------------------------------------------------------------------
// <copyright file="SqlJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Journal;

namespace Akka.Persistence.Sql.Common.Journal
{
    /// <summary>
    /// TBD
    /// </summary>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="journalConfig">TBD</param>
        protected SqlJournal(Config journalConfig)
        {
            _settings = new JournalSettings(journalConfig);
            _pendingRequestsCancellation = new CancellationTokenSource();
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IStash Stash { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public IEnumerable<string> AllPersistenceIds => _allPersistenceIds;

        /// <summary>
        /// TBD
        /// </summary>
        protected bool HasPersistenceIdSubscribers => _persistenceIdSubscribers.Count != 0;
        /// <summary>
        /// TBD
        /// </summary>
        protected bool HasTagSubscribers => _tagSubscribers.Count != 0;
        /// <summary>
        /// TBD
        /// </summary>
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
        /// <param name="connectionString">TBD</param>
        /// <returns>TBD</returns>
        protected abstract DbConnection CreateDbConnection(string connectionString);

        /// <summary>
        /// Used for generating SQL commands for journal-related database operations.
        /// </summary>
        public abstract IJournalQueryExecutor QueryExecutor { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
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
                .WasHandled;
        }

        /// <summary>
        /// Asynchronously writes all persistent <paramref name="messages"/> inside SQL Server database.
        /// 
        /// Specific table used for message persistence may be defined through configuration within 
        /// 'akka.persistence.journal.sql-server' scope with 'schema-name' and 'table-name' keys.
        /// </summary>
        /// <param name="messages">TBD</param>
        /// <exception cref="InvalidOperationException">TBD</exception>
        /// <returns>TBD</returns>
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
            }).ToArray();

            var result = await Task<IImmutableList<Exception>>
                .Factory
                .ContinueWhenAll(writeTasks,
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
        /// <param name="replay">TBD</param>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="fromSequenceNr">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="recoveryCallback">TBD</param>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            base.PreStart();
            Initialize().PipeTo(Self);
            BecomeStacked(WaitingForInitialization);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            base.PostStop();
            _pendingRequestsCancellation.Cancel();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
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

        private async Task<object> Initialize()
        {
            try
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
            catch (Exception e)
            {
                return new Failure {Exception = e};
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public DbConnection CreateDbConnection()
        {
            var connectionString = GetConnectionString();
            return CreateDbConnection(connectionString);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        /// <param name="tag">TBD</param>
        public void AddTagSubscriber(IActorRef subscriber, string tag)
        {
            if (!_tagSubscribers.TryGetValue(tag, out var subscriptions))
            {
                subscriptions = new HashSet<IActorRef>();
                _tagSubscribers.Add(tag, subscriptions);
            }

            subscriptions.Add(subscriber);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        public void AddAllPersistenceIdSubscriber(IActorRef subscriber)
        {
            _allPersistenceIdSubscribers.Add(subscriber);
            subscriber.Tell(new CurrentPersistenceIds(AllPersistenceIds));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        /// <param name="persistenceId">TBD</param>
        public void AddPersistenceIdSubscriber(IActorRef subscriber, string persistenceId)
        {
            if (!_persistenceIdSubscribers.TryGetValue(persistenceId, out var subscriptions))
            {
                subscriptions = new HashSet<IActorRef>();
                _persistenceIdSubscribers.Add(persistenceId, subscriptions);
            }

            subscriptions.Add(subscriber);
        }

        private async Task<long> NextTagSequenceNr(string tag)
        {
            if (!_tagSequenceNr.TryGetValue(tag, out long value))
                value = await ReadHighestSequenceNrAsync(TagId(tag), 0L);

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
            if (_persistenceIdSubscribers.TryGetValue(persistenceId, out var subscribers))
            {
                var changed = new EventAppended(persistenceId);
                foreach (var subscriber in subscribers)
                    subscriber.Tell(changed);
            }
        }

        private void NotifyTagChange(string tag)
        {
            if (_tagSubscribers.TryGetValue(tag, out var subscribers))
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
        /// <param name="persistenceId">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <returns>TBD</returns>
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
        /// <param name="persistenceId">TBD</param>
        /// <param name="fromSequenceNr">TBD</param>
        /// <returns>TBD</returns>
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
        /// <returns>TBD</returns>
        protected virtual string GetConnectionString()
        {
            var connectionString = _settings.ConnectionString;

#if CONFIGURATION
            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = System.Configuration.ConfigurationManager.ConnectionStrings[_settings.ConnectionStringName].ConnectionString;
            }
#endif

            return connectionString;
        }

        #region obsoleted

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="typeName">TBD</param>
        /// <returns>TBD</returns>
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
        #endregion
    }
}