//-----------------------------------------------------------------------
// <copyright file="SqlJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.Common;
using System.Linq;
using System.Reflection;
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
        private ImmutableDictionary<string, IImmutableSet<IActorRef>> _persistenceIdSubscribers = ImmutableDictionary.Create<string, IImmutableSet<IActorRef>>();
        private ImmutableDictionary<string, IImmutableSet<IActorRef>> _tagSubscribers = ImmutableDictionary.Create<string, IImmutableSet<IActorRef>>();
        private readonly HashSet<IActorRef> _newEventsSubscriber = new HashSet<IActorRef>();
        private IImmutableDictionary<string, long> _tagSequenceNr = ImmutableDictionary<string, long>.Empty;

        private readonly CancellationTokenSource _pendingRequestsCancellation;
        private readonly JournalSettings _settings;

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

        public IStash Stash { get; set; }

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
        protected bool HasNewEventSubscribers => _newEventsSubscriber.Count != 0;

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
            switch (message)
            {
                case ReplayTaggedMessages replay:
                    ReplayTaggedMessagesAsync(replay)
                        .PipeTo(replay.ReplyTo, success: h => new RecoverySuccess(h), failure: e => new ReplayMessagesFailure(e));
                    return true;
                case ReplayAllEvents replay:
                    ReplayAllEventsAsync(replay)
                        .PipeTo(replay.ReplyTo, success: h => new EventReplaySuccess(h),
                            failure: e => new EventReplayFailure(e));
                    return true;
                case SubscribePersistenceId subscribe:
                    AddPersistenceIdSubscriber(Sender, subscribe.PersistenceId);
                    Context.Watch(Sender);
                    return true;
                case SelectCurrentPersistenceIds request:
                    SelectAllPersistenceIdsAsync(request.Offset)
                        .PipeTo(request.ReplyTo, success: result => new CurrentPersistenceIds(result.Ids, request.Offset));
                    return true;
                case SubscribeTag subscribe:
                    AddTagSubscriber(Sender, subscribe.Tag);
                    Context.Watch(Sender);
                    return true;
                case SubscribeNewEvents _:
                    AddNewEventsSubscriber(Sender);
                    Context.Watch(Sender);
                    return true;
                case Terminated terminated:
                    RemoveSubscriber(terminated.ActorRef);
                    return true;
                default:
                    return false;
            }
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
                    }

                    var batch = new WriteJournalBatch(eventToTags);
                    using(var cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
                        await QueryExecutor.InsertBatchAsync(connection, cancellationToken.Token, batch);
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

            if (HasNewEventSubscribers)
                NotifyNewEventAppended();

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
                using(var cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
                {
                    return await QueryExecutor
                        .SelectByTagAsync(connection, cancellationToken.Token, replay.Tag, replay.FromOffset, replay.ToOffset, replay.Max, replayedTagged => {
                            foreach(var adapted in AdaptFromJournal(replayedTagged.Persistent))
                            { 
                                replay.ReplyTo.Tell(new ReplayedTaggedMessage(adapted, replayedTagged.Tag, replayedTagged.Offset), ActorRefs.NoSender);
                            }
                        });
                }
            }
        }

        protected virtual async Task<long> ReplayAllEventsAsync(ReplayAllEvents replay)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                using (var cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
                {
                    return await QueryExecutor
                        .SelectAllEventsAsync(
                            connection,
                            cancellationToken.Token, 
                            replay.FromOffset, 
                            replay.ToOffset,
                            replay.Max, 
                            replayedEvent => {
                                foreach (var adapted in AdaptFromJournal(replayedEvent.Persistent))
                                {
                                    replay.ReplyTo.Tell(new ReplayedEvent(adapted, replayedEvent.Offset), ActorRefs.NoSender);
                                }
                            });
                }
            }
        }

        protected virtual async Task<(IEnumerable<string> Ids, long LastOrdering)> SelectAllPersistenceIdsAsync(long offset)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                using (var cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
                {
                    var lastOrdering = await QueryExecutor.SelectHighestSequenceNrAsync(connection, cancellationToken.Token);
                    var ids = await QueryExecutor.SelectAllPersistenceIdsAsync(connection, cancellationToken.Token, offset);
                    return (ids, lastOrdering);
                }
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
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                using (var cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
                {
                    await QueryExecutor.SelectByPersistenceIdAsync(connection, cancellationToken.Token, persistenceId, fromSequenceNr, toSequenceNr, max, recoveryCallback);
                }
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
            switch (message)
            {
                case Status.Success _:
                    UnbecomeStacked();
                    Stash.UnstashAll();
                    return true;
                case Status.Failure fail:
                    Log.Error(fail.Cause, "Failure during {0} initialization.", Self);
                    Context.Stop(Self);
                    return true;
                default:
                    Stash.Stash();
                    return true;
            }
        }

        private async Task<object> Initialize()
        {
            if (!_settings.AutoInitialize) 
                return new Status.Success(NotUsed.Instance);

            try
            {
                using (var connection = CreateDbConnection())
                {
                    await connection.OpenAsync();
                    using (var cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
                    {
                        await QueryExecutor.CreateTablesAsync(connection, cancellationToken.Token);
                    }
                }
            }
            catch (Exception e)
            {
                return new Status.Failure(e);
            }
            return new Status.Success(NotUsed.Instance);
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
            _persistenceIdSubscribers = _persistenceIdSubscribers.SetItems(_persistenceIdSubscribers
                .Where(kv => kv.Value.Contains(subscriber))
                .Select(kv => new KeyValuePair<string, IImmutableSet<IActorRef>>(kv.Key, kv.Value.Remove(subscriber))));

            _tagSubscribers = _tagSubscribers.SetItems(_tagSubscribers
                .Where(kv => kv.Value.Contains(subscriber))
                .Select(kv => new KeyValuePair<string, IImmutableSet<IActorRef>>(kv.Key, kv.Value.Remove(subscriber))));

            _newEventsSubscriber.Remove(subscriber);
        }

        public void AddNewEventsSubscriber(IActorRef subscriber)
        {
            _newEventsSubscriber.Add(subscriber);
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
                _tagSubscribers = _tagSubscribers.Add(tag, ImmutableHashSet.Create(subscriber));
            }
            else
            {
                _tagSubscribers = _tagSubscribers.SetItem(tag, subscriptions.Add(subscriber));
            }
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
                _persistenceIdSubscribers = _persistenceIdSubscribers.Add(persistenceId, ImmutableHashSet.Create(subscriber));
            }
            else
            {
                _persistenceIdSubscribers = _persistenceIdSubscribers.SetItem(persistenceId, subscriptions.Add(subscriber));
            }
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

        private void NotifyNewEventAppended()
        {
            if (HasNewEventSubscribers)
            {
                foreach (var subscriber in _newEventsSubscriber)
                {
                    subscriber.Tell(NewEventAppended.Instance);
                }
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
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                using (var cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
                {
                    await QueryExecutor.DeleteBatchAsync(connection, cancellationToken.Token, persistenceId, toSequenceNr);
                }
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
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                using (var cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
                {
                    return await QueryExecutor.SelectHighestSequenceNrAsync(connection, cancellationToken.Token, persistenceId);
                }
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
            var withSystem = type.GetConstructor(new[] { Context.System.GetType() }) != null;
            return withSystem ?
                (ITimestampProvider)Activator.CreateInstance(type, Context.System) :
                (ITimestampProvider)Activator.CreateInstance(type);
        }
        #endregion
    }
}
