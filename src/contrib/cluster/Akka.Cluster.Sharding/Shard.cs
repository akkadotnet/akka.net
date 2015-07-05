//-----------------------------------------------------------------------
// <copyright file="Shard.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;
    using EntryId = String;

    internal struct BufferedMessage
    {
        public readonly object Message;
        public readonly IActorRef ActorRef;

        public BufferedMessage(object message, IActorRef actorRef)
        {
            Message = message;
            ActorRef = actorRef;
        }
    }

    public class Shard : PersistentActor
    {
        #region Messages

        public interface IShardCommand { }

        /**
         * When a `StateChange` fails to write to the journal, we will retry it after a back off.
         */
        public class RetryPersistence : IShardCommand
        {
            public readonly StateChange Payload;

            public RetryPersistence(StateChange payload)
            {
                Payload = payload;
            }
        }

        /**
         * The Snapshot tick for the shards
         */

        public sealed class SnapshotTick : IShardCommand
        {
            public static readonly SnapshotTick Instance = new SnapshotTick();
            private SnapshotTick() { }
        }

        /**
         * When an remembering entries and the entry stops without issuing a `Passivate`, we
         * restart it after a back off using this message.
         */
        public sealed class RestartEntry : IShardCommand
        {
            public readonly EntryId EntryId;

            public RestartEntry(string entryId)
            {
                EntryId = entryId;
            }
        }

        public abstract class StateChange
        {
            public readonly EntryId EntryId;
            protected StateChange(EntryId entryId)
            {
                EntryId = entryId;
            }
        }

        /**
         * `State` change for starting an entry in this `Shard`
         */
        [Serializable]
        public sealed class EntryStarted : StateChange
        {
            public EntryStarted(string entryId) : base(entryId) { }
        }

        /**
         * `State` change for an entry which has terminated.
         */
        [Serializable]
        public sealed class EntryStopped : StateChange
        {
            public EntryStopped(string entryId) : base(entryId) { }
        }

        #endregion

        /**
         * Persistent state of the Shard.
         */
        [Serializable]
        public class State
        {
            public static readonly State Empty = new State();
            public readonly ISet<EntryId> Entries;

            public State() : this(new HashSet<EntryId>()) { }

            public State(ISet<EntryId> entries)
            {
                Entries = entries;
            }
        }

        private readonly string _typeName;
        private readonly string _shardId;

        private readonly Props _entryProps;
        private readonly ClusterShardingSettings _settings;
        private readonly IdExtractor _idExtractor;
        private readonly ShardResolver _shardResolver;
        private readonly object _handOffStopMessage;

        private readonly ICancelable _snapshotCancel;
        private readonly ILoggingAdapter _log;

        private State _state = State.Empty;
        private IDictionary<IActorRef, EntryId> _idByRef = new Dictionary<IActorRef, string>();
        private IDictionary<EntryId, IActorRef> _refById = new Dictionary<string, IActorRef>();
        private ISet<IActorRef> _passivating = new HashSet<IActorRef>();
        private ConcurrentDictionary<EntryId, ICollection<BufferedMessage>> _messageBuffers = new ConcurrentDictionary<string, ICollection<BufferedMessage>>();

        private readonly Cluster _cluster;
        private readonly string _persistenceId;

        private int _persistCount = 0;
        private IActorRef _handOffStopper = null;

        public Shard(string typeName, string shardId, Props entryProps, ClusterShardingSettings settings, IdExtractor idExtractor, ShardResolver shardResolver, object handOffStopMessage)
        {
            _typeName = typeName;
            _shardId = shardId;
            _entryProps = entryProps;
            _settings = settings;
            _idExtractor = idExtractor;
            _shardResolver = shardResolver;
            _handOffStopMessage = handOffStopMessage;

            _cluster = Cluster.Get(Context.System);
            _persistenceId = "/sharding/" + _typeName + "Shard/" + _shardId;

            JournalPluginId = _settings.JournalPluginId;
            SnapshotPluginId = _settings.SnapshotPluginId;

            _log = Context.GetLogger();
        }

        public override ShardId PersistenceId { get { return _persistenceId; } }

        public int TotalBufferSize
        {
            get { return _messageBuffers.Aggregate(0, (acc, entry) => acc + entry.Value.Count); }
        }

        private void ProcessChange<T>(T @event, Action<T> handler)
        {
            if (_settings.RememberEntities)
            {
                SaveSnapshotIfNeeded();
                Persist(@event, handler);
            }
            else handler(@event);
        }

        private void SaveSnapshotIfNeeded()
        {
            _persistCount++;
            if (_persistCount%_settings.TunningParameters.SnapshotAfter == 0)
            {
                _log.Debug("Saving snapshot, sequence number [{0}]", SnapshotSequenceNr);
                SaveSnapshot(_state);
            }
        }

        protected override void PostStop()
        {
            base.PostStop();
            _snapshotCancel.Cancel();
        }
        
        protected override bool ReceiveRecover(object message)
        {
            if (message is EntryStarted && _settings.RememberEntities)
            {
                var started = (EntryStarted)message;
                _state.Entries.Add(started.EntryId);
            }
            else if (message is EntryStopped && _settings.RememberEntities)
            {
                var stopped = (EntryStopped)message;
                _state.Entries.Remove(stopped.EntryId);
            }
            else if (message is SnapshotOffer)
            {
                var offer = (SnapshotOffer)message;
                if (offer.Snapshot is State) _state = (State)offer.Snapshot;
                else return false;
            }
            else if (message is RecoveryCompleted)
            {
                foreach (var entry in _state.Entries)
                {
                    GetEntry(entry);
                }
            }
            else return false;
            return true;
        }

        private IActorRef GetEntry(EntryId id)
        {
            var name = Uri.EscapeDataString(id);
            var child = Context.Child(name);
            if (child == null)
            {
                _log.Debug("Starting entry [{0}] in shard [{1}]", id, _shardId);

                child = Context.Watch(Context.ActorOf(_entryProps, name));
                _idByRef.Add(child, id);
                _refById.Add(id, child);
                _state.Entries.Add(id);
            }

            return child;
        }

        protected override bool ReceiveCommand(Object message)
        {
            if (message is Terminated)
            {
                ReceiveTerminated(message as Terminated);
            }
            else if (message is ICoordinatorMessage)
            {
                ReceiveCoordinatorMessage(message as ICoordinatorMessage);
            }
            else if (message is IShardCommand)
            {
                ReceiveShardCommand(message as IShardCommand);
            }
            else if (message is IShardRegionCommand)
            {
                ReceiveShardRegionCommand(message as IShardRegionCommand);
            }
            else if (_idExtractor(message) != null)
            {
                DeliverMessage(message, Sender);
            }
            else return false;
            return true;
        }

        private void ReceiveShardRegionCommand(IShardRegionCommand command)
        {
            if (command is Passivate) Passivate(Sender, (command as Passivate).StopMessage);
            else Unhandled(command);
        }

        private void ReceiveShardCommand(IShardCommand message)
        {
            if (message is RestartEntry) GetEntry(((RestartEntry)message).EntryId);
        }

        private void ReceiveCoordinatorMessage(ICoordinatorMessage coordinatorMessage)
        {
            var handOff = coordinatorMessage as HandOff;
            if (handOff != null)
            {
                if (handOff.Shard == _shardId) HandOff(Sender);
                else _log.Warning("Shard [{0}] can not hand off for another Shard [{1}]", _shardId, handOff.Shard);
            }
            else Unhandled(coordinatorMessage);
        }

        private void ReceiveTerminated(Terminated terminated)
        {
            EntryId id;
            if (_handOffStopper != null && _handOffStopper.Equals(terminated.ActorRef))
            {
                Context.Stop(Self);
            }
            else if (_idByRef.TryGetValue(terminated.ActorRef, out id) && _handOffStopper == null)
            {
                ICollection<BufferedMessage> messages;
                if (!_messageBuffers.TryGetValue(id, out messages) || messages.Count == 0)
                {
                    //NOTE: because we're not persisting the EntryStopped, we don't need to persist the EntryStarted either.
                    _log.Debug("Starting entry [{0}] again, there are buffered messages for it", id);
                    SendMessageBuffer(new EntryStarted(id));
                }
                else
                {
                    if (_settings.RememberEntities && !_passivating.Contains(terminated.ActorRef))
                    {
                        _log.Debug("Entry [{0}] stopped without passivating, will restart after backoff", id);
                        Context.System.Scheduler.ScheduleTellOnce(_settings.TunningParameters.EntityRestartBackoff, Self, new RestartEntry(id), Self);
                    }
                    else
                    {
                        ProcessChange(new EntryStopped(id), PassivateCompleted);
                    }
                }

                _passivating.Remove(terminated.ActorRef);
            }
        }

        private void HandleRetryPersistence(StateChange stateChange)
        {
            _log.Debug("Retrying persistence of [{0}]", stateChange);
            Persist(stateChange, e =>
            {
                if (e is EntryStarted) SendMessageBuffer(e as EntryStarted);
                if (e is EntryStopped) PassivateCompleted(e as EntryStopped);
            });
        }

        private void HandOff(IActorRef replyTo)
        {
            if (_handOffStopper != null)
            {
                _log.Warning("HandOff shard [{0}] received during existing hand off", _shardId);
            }
            else
            {
                _log.Debug("HandOff shard [{0}]", _shardId);
                if (_state.Entries.Count != 0)
                {
                    _handOffStopper = Context.Watch(Context.ActorOf(Props.Create(() => new HandOffStopper(_shardId, replyTo, _idByRef.Keys.ToArray()))));

                    //During hand off we only care about watching for termination of the hand off stopper
                    Context.Become(message =>
                    {
                        if (message is Terminated) ReceiveTerminated(message as Terminated);
                        else return false;
                        return true;
                    });
                }
                else
                {
                    replyTo.Tell(new ShardStopped(_shardId));
                    Context.Stop(Self);
                }
            }
        }

        private void Passivate(IActorRef entry, object stopMessage)
        {
            EntryId id;
            if (_idByRef.TryGetValue(entry, out id) && !_messageBuffers.ContainsKey(id))
            {
                _log.Debug("Passivating started on entry " + id);
                _passivating.Add(entry);
                _messageBuffers.TryAdd(id, new LinkedList<BufferedMessage>());
                entry.Tell(stopMessage);
            }
        }

        private void PassivateCompleted(EntryStopped e)
        {
            _log.Debug("Entry stopped [{0}]", e.EntryId);

            var actorRef = _refById[e.EntryId];
            _idByRef.Remove(actorRef);
            _refById.Remove(e.EntryId);
            _state.Entries.Remove(e.EntryId);

            ICollection<BufferedMessage> x;
            _messageBuffers.TryRemove(e.EntryId, out x);
        }

        private void SendMessageBuffer(EntryStarted e)
        {
            // Get the buffered messages and remove the buffer
            ICollection<BufferedMessage> messages;
            if (!_messageBuffers.TryRemove(e.EntryId, out messages)) { messages = new LinkedList<BufferedMessage>(); }

            if (messages.Count > 0)
            {
                _log.Debug("Sending message buffer for entry [{0}] ({1} messages)", e.EntryId, messages.Count);
                GetEntry(e.EntryId);

                // Now there is no deliveryBuffer we can try to redeliver and as the child exists, the message will be directly forwarded
                foreach (var message in messages)
                {
                    DeliverMessage(message.Message, message.ActorRef);
                }
            }
        }

        private void DeliverMessage(object message, IActorRef sender)
        {
            var tup = _idExtractor(message);
            var id = tup.Item1;
            var payload = tup.Item2;
            if (string.IsNullOrEmpty(id))
            {
                _log.Warning("Id must not be empty, dropping the message [{0}]", message.GetType());
                Context.System.DeadLetters.Tell(message);
            }
            else
            {
                ICollection<BufferedMessage> messages;
                if (_messageBuffers.TryGetValue(id, out messages))
                {
                    if (TotalBufferSize >= _settings.TunningParameters.BufferSize)
                    {
                        _log.Debug("Buffer is full, dropping message for entry [{0}]", id);
                        Context.System.DeadLetters.Tell(message);
                    }
                    else
                    {
                        _log.Debug("Message for entry [{0}] buffered", id);
                        messages.Add(new BufferedMessage(message, sender));
                    }
                }
                else
                {
                    DeliverTo(id, message, payload, sender);
                }
            }
        }

        private void DeliverTo(EntryId id, object message, object payload, IActorRef sender)
        {
            var name = Uri.EscapeDataString(id);
            var child = Context.Child(name);
            if (child != null)
            {
                child.Tell(payload, sender);
            }
            else if (_settings.RememberEntities)
            {
                //Note; we only do this if remembering, otherwise the buffer is an overhead
                var vec = new LinkedList<BufferedMessage>();
                vec.AddLast(new BufferedMessage(message, sender));
                if (_messageBuffers.TryAdd(id, vec))
                {
                    SaveSnapshotIfNeeded();
                    Persist(new EntryStarted(id), SendMessageBuffer);
                }
            }
            else
            {
                GetEntry(id).Tell(payload, sender);
            }
        }
    }

    internal class HandOffStopper : ActorBase
    {
        private readonly string _shardId;
        private readonly IActorRef _replyTo;
        private List<IActorRef> _remaining;

        public HandOffStopper(ShardId shardId, IActorRef replyTo, IActorRef[] entries)
        {
            _shardId = shardId;
            _replyTo = replyTo;
            foreach (var aref in entries)
            {
                Context.Watch(aref);
                aref.Tell(PoisonPill.Instance);
            }

            _remaining = entries.ToList();
        }

        protected override bool Receive(object message)
        {
            if (message is Terminated)
            {
                var terminated = (Terminated) message;
                _remaining.Remove(terminated.ActorRef);
                if (_remaining.Count == 0)
                {
                    _replyTo.Tell(new ShardStopped(_shardId));
                    Context.Stop(Self);
                }

                return true;
            }

            return false;
        }
    }
}