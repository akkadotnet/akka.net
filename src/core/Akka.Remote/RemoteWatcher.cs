//-----------------------------------------------------------------------
// <copyright file="RemoteWatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#pragma warning disable AK1004
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util.Internal;
using Akka.Configuration;

namespace Akka.Remote
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Remote nodes with actors that are watched are monitored by this actor to be able
    /// to detect network failures and process crashes. <see cref="RemoteActorRefProvider"/>
    /// intercepts Watch and Unwatch system messages and sends corresponding
    /// <see cref="RemoteWatcher.WatchRemote"/> and <see cref="RemoteWatcher.UnwatchRemote"/> to this actor.
    ///
    /// For a new node to be watched this actor periodically sends <see cref="RemoteWatcher.Heartbeat"/>
    /// to the peer actor on the other node, which replies with <see cref="RemoteWatcher.HeartbeatRsp"/>
    /// message back. The failure detector on the watching side monitors these heartbeat messages.
    /// If arrival of heartbeat messages stops it will be detected and this actor will publish
    /// <see cref="AddressTerminated"/> to the <see cref="AddressTerminatedTopic"/>.
    ///
    /// When all actors on a node have been unwatched it will stop sending heartbeat messages.
    ///
    /// For bi-directional watch between two nodes the same thing will be established in
    /// both directions, but independent of each other.
    /// </summary>
    public class RemoteWatcher : UntypedActor, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        /// <summary>
        /// Creates actor props for a watcher that monitors remote addresses using heartbeats and a failure detector.
        /// </summary>
        /// <param name="failureDetector">Registry used to track heartbeat health for remote addresses.</param>
        /// <param name="heartbeatInterval">Interval between heartbeat messages sent to watched nodes.</param>
        /// <param name="unreachableReaperInterval">Interval between checks for unreachable watched nodes.</param>
        /// <param name="heartbeatExpectedResponseAfter">Delay before recording an expected first heartbeat for a newly watched node.</param>
        /// <returns>Props that create a locally deployed remote watcher on the internal dispatcher.</returns>
        public static Props Props(
            IFailureDetectorRegistry<Address> failureDetector,
            TimeSpan heartbeatInterval,
            TimeSpan unreachableReaperInterval,
            TimeSpan heartbeatExpectedResponseAfter)
        {
            return Actor.Props.Create(() => new RemoteWatcher(failureDetector, heartbeatInterval, unreachableReaperInterval, heartbeatExpectedResponseAfter))
                .WithDispatcher(Dispatchers.InternalDispatcherId)
                .WithDeploy(Deploy.Local);
        }

        /// <summary>
        /// Base message carrying a watch or unwatch operation for a remote actor.
        /// </summary>
        public abstract class WatchCommand
        {
            readonly IInternalActorRef _watchee;
            readonly IInternalActorRef _watcher;

            /// <summary>
            /// Creates a watch command for a watchee and its watcher.
            /// </summary>
            /// <param name="watchee">The actor whose termination is being watched.</param>
            /// <param name="watcher">The actor that requested the watch.</param>
            protected WatchCommand(IInternalActorRef watchee, IInternalActorRef watcher)
            {
                _watchee = watchee;
                _watcher = watcher;
            }

            /// <summary>
            /// Gets the actor whose termination is being watched.
            /// </summary>
            public IInternalActorRef Watchee => _watchee;

            /// <summary>
            /// Gets the actor that requested the watch.
            /// </summary>
            public IInternalActorRef Watcher => _watcher;
        }

        /// <summary>
        /// Requests that the remote watcher monitor an actor on another node.
        /// </summary>
        public sealed class WatchRemote : WatchCommand
        {
            /// <summary>
            /// Creates a remote watch request.
            /// </summary>
            /// <param name="watchee">The remote actor to watch.</param>
            /// <param name="watcher">The actor that requested the watch.</param>
            public WatchRemote(IInternalActorRef watchee, IInternalActorRef watcher)
                : base(watchee, watcher)
            {
            }
        }

        /// <summary>
        /// Requests that the remote watcher stop monitoring an actor on another node.
        /// </summary>
        public sealed class UnwatchRemote : WatchCommand
        {
            /// <summary>
            /// Creates a remote unwatch request.
            /// </summary>
            /// <param name="watchee">The remote actor to stop watching.</param>
            /// <param name="watcher">The actor that requested the unwatch.</param>
            public UnwatchRemote(IInternalActorRef watchee, IInternalActorRef watcher)
                : base(watchee, watcher)
            {
            }
        }

        /// <summary>
        /// Heartbeat request sent to a remote watcher to verify that its actor system is responsive.
        /// </summary>
        public sealed class Heartbeat : IPriorityMessage
        {
            private Heartbeat()
            {
            }

            /// <summary>
            /// Gets the singleton heartbeat request instance.
            /// </summary>
            public static Heartbeat Instance { get; } = new();
        }

        /// <summary>
        /// Heartbeat reply containing the sender actor system's UID.
        /// </summary>
        public class HeartbeatRsp : IPriorityMessage
        {
            readonly long _addressUid;

            /// <summary>
            /// Creates a heartbeat reply for an actor system UID.
            /// </summary>
            /// <param name="addressUid">The UID of the actor system sending the reply.</param>
            public HeartbeatRsp(long addressUid)
            {
                _addressUid = addressUid;
            }

            /// <summary>
            /// Gets the UID announced by the actor system that sent the reply.
            /// </summary>
            public long AddressUid
            {
                get { return _addressUid; }
            }
        }

        // sent to self only
        /// <summary>
        /// Timer message used by the remote watcher to send scheduled heartbeats to watched nodes.
        /// </summary>
        public class HeartbeatTick
        {
            private HeartbeatTick() { }

            /// <summary>
            /// Gets the singleton heartbeat timer message.
            /// </summary>
            public static HeartbeatTick Instance { get; } = new();
        }

        /// <summary>
        /// Timer message used to check watched nodes for failure-detector timeouts.
        /// </summary>
        public class ReapUnreachableTick
        {
            private ReapUnreachableTick() { }

            /// <summary>
            /// Gets the singleton unreachable-node reaper timer message.
            /// </summary>
            public static ReapUnreachableTick Instance { get; } = new();
        }

        /// <summary>
        /// Timer message that records the first expected heartbeat for a watched node.
        /// </summary>
        public sealed class ExpectedFirstHeartbeat
        {
            readonly Address _from;

            /// <summary>
            /// Creates a message identifying the node whose first heartbeat is expected.
            /// </summary>
            /// <param name="from">The address of the watched node.</param>
            public ExpectedFirstHeartbeat(Address @from)
            {
                _from = @from;
            }

            /// <summary>
            /// Gets the address of the node whose first heartbeat is expected.
            /// </summary>
            public Address From
            {
                get { return _from; }
            }
        }

        // test purpose
        /// <summary>
        /// Test snapshot of the remote watcher's watched actors and nodes.
        /// </summary>
        public sealed class Stats
        {
            
            public override bool Equals(object obj)
            {
                var other = obj as Stats;
                if (other == null) return false;
                return _watching == other._watching && _watchingNodes == other._watchingNodes;
            }

            
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + _watching.GetHashCode();
                    hash = hash * 23 + _watchingNodes.GetHashCode();

                    return hash;
                }
            }

            /// <summary>
            /// Gets an empty watcher statistics snapshot.
            /// </summary>
            public static Stats Empty = Counts(0, 0);

            /// <summary>
            /// Creates statistics containing only the number of watched actor pairs and remote nodes.
            /// </summary>
            /// <param name="watching">The number of watched actor-watcher pairs.</param>
            /// <param name="watchingNodes">The number of distinct remote node addresses being watched.</param>
            /// <returns>A statistics snapshot with empty reference and address sets.</returns>
            public static Stats Counts(int watching, int watchingNodes)
            {
                return new Stats(watching, watchingNodes);
            }

            readonly int _watching;
            readonly int _watchingNodes;
            readonly ImmutableHashSet<(IActorRef, IActorRef)> _watchingRefs;
            readonly ImmutableHashSet<Address> _watchingAddresses;

            /// <summary>
            /// Creates statistics containing watched actor and node counts with empty detail sets.
            /// </summary>
            /// <param name="watching">The number of watched actor-watcher pairs.</param>
            /// <param name="watchingNodes">The number of distinct remote node addresses being watched.</param>
            public Stats(int watching, int watchingNodes) : this(watching, watchingNodes, 
                ImmutableHashSet<(IActorRef, IActorRef)>.Empty, ImmutableHashSet<Address>.Empty) { }

            /// <summary>
            /// Creates a complete snapshot of watched actor pairs and remote node addresses.
            /// </summary>
            /// <param name="watching">The number of watched actor-watcher pairs.</param>
            /// <param name="watchingNodes">The number of distinct remote node addresses being watched.</param>
            /// <param name="watchingRefs">Pairs of watched actors and their watchers.</param>
            /// <param name="watchingAddresses">Addresses of the watched remote nodes.</param>
            public Stats(int watching, int watchingNodes, ImmutableHashSet<(IActorRef, IActorRef)> watchingRefs, ImmutableHashSet<Address> watchingAddresses)
            {
                _watching = watching;
                _watchingNodes = watchingNodes;
                _watchingRefs = watchingRefs;
                _watchingAddresses = watchingAddresses;
            }

            /// <summary>
            /// Gets the number of watched actor-watcher pairs.
            /// </summary>
            public int Watching => _watching;

            /// <summary>
            /// Gets the number of distinct remote node addresses being watched.
            /// </summary>
            public int WatchingNodes => _watchingNodes;

            /// <summary>
            /// Gets pairs of watched actors and their watchers.
            /// </summary>
            public ImmutableHashSet<(IActorRef, IActorRef)> WatchingRefs => _watchingRefs;

            /// <summary>
            /// Gets the addresses of remote nodes being watched.
            /// </summary>
            public ImmutableHashSet<Address> WatchingAddresses => _watchingAddresses;

            
            public override string ToString()
            {
                string FormatWatchingRefs()
                {
                    if (!_watchingRefs.Any()) return "";
                    return $"{string.Join(", ", _watchingRefs.Select(r => r.Item2.Path.Name + "-> " + r.Item1.Path.Name))}";
                }

                string FormatWatchingAddresses()
                {
                    if (!_watchingAddresses.Any()) return "";
                    return string.Join(",", WatchingAddresses);
                }

                return $"Stats(watching={_watching}, watchingNodes={_watchingNodes}, watchingRefs=[{FormatWatchingRefs()}], watchingAddresses=[{FormatWatchingAddresses()}])";
            }

            /// <summary>
            /// Creates a statistics snapshot with updated counts and optional detail sets.
            /// </summary>
            /// <param name="watching">The updated number of watched actor-watcher pairs.</param>
            /// <param name="watchingNodes">The updated number of distinct watched node addresses.</param>
            /// <param name="watchingRefs">Replacement watched actor pairs, or <c>null</c> to retain the current set.</param>
            /// <param name="watchingAddresses">Replacement watched node addresses, or <c>null</c> to retain the current set.</param>
            /// <returns>A new statistics snapshot.</returns>
            public Stats Copy(int watching, int watchingNodes, ImmutableHashSet<(IActorRef, IActorRef)> watchingRefs = null, ImmutableHashSet<Address> watchingAddresses = null)
            {
                return new Stats(watching, watchingNodes, watchingRefs ?? WatchingRefs, watchingAddresses ?? WatchingAddresses);
            }
        }

        /// <summary>
        /// Creates a remote watcher that tracks remote watches and detects unreachable nodes with heartbeats.
        /// </summary>
        /// <param name="failureDetector">Registry used to track heartbeat health for remote addresses.</param>
        /// <param name="heartbeatInterval">Interval between heartbeat messages sent to watched nodes.</param>
        /// <param name="unreachableReaperInterval">Interval between checks for unreachable watched nodes.</param>
        /// <param name="heartbeatExpectedResponseAfter">Delay before recording an expected first heartbeat for a newly watched node.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when the actor system does not have a <see cref="RemoteActorRefProvider"/> enabled in the configuration.
        /// </exception>
        public RemoteWatcher(
            IFailureDetectorRegistry<Address> failureDetector,
            TimeSpan heartbeatInterval,
            TimeSpan unreachableReaperInterval,
            TimeSpan heartbeatExpectedResponseAfter
            )
        {
            _failureDetector = failureDetector;
            _heartbeatExpectedResponseAfter = heartbeatExpectedResponseAfter;
            if (Context.System.AsInstanceOf<ExtendedActorSystem>().Provider is IRemoteActorRefProvider systemProvider) _remoteProvider = systemProvider;
            else throw new ConfigurationException(
                $"ActorSystem {Context.System} needs to have a 'RemoteActorRefProvider' enabled in the configuration, current uses {Context.System.AsInstanceOf<ExtendedActorSystem>().Provider.GetType().FullName}");

            _heartbeatCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(heartbeatInterval, heartbeatInterval, Self, HeartbeatTick.Instance, Self);
            _failureDetectorReaperCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(unreachableReaperInterval, unreachableReaperInterval, Self, ReapUnreachableTick.Instance, Self);
        }

        private readonly IFailureDetectorRegistry<Address> _failureDetector;
        private readonly TimeSpan _heartbeatExpectedResponseAfter;
        private readonly IScheduler _scheduler = Context.System.Scheduler;
        private readonly IRemoteActorRefProvider _remoteProvider;
        private readonly HeartbeatRsp _selfHeartbeatRspMsg = new(AddressUidExtension.Uid(Context.System));
       
        /// <summary>
        ///  Actors that this node is watching, map of watchee --> Set(watchers)
        /// </summary>
        protected readonly Dictionary<IInternalActorRef, HashSet<IInternalActorRef>>  Watching = new();

        /// <summary>
        /// Nodes that this node is watching, i.e. expecting heartbeats from these nodes. Map of address --> Set(watchee) on this address.
        /// </summary>
        protected readonly Dictionary<Address, HashSet<IInternalActorRef>> WatcheeByNodes = new();

        /// <summary>
        /// Gets the addresses for which the watcher currently tracks remote actors.
        /// </summary>
        protected ICollection<Address> WatchingNodes => WatcheeByNodes.Keys;
        /// <summary>
        /// Gets addresses that the failure detector has marked unreachable.
        /// </summary>
        protected HashSet<Address> Unreachable { get; } = new();

        private readonly Dictionary<Address, long> _addressUids = new();

        private readonly ICancelable _heartbeatCancelable;
        private readonly ICancelable _failureDetectorReaperCancelable;

        /// <summary>
        /// Cancels the heartbeat and unreachable-node timers when the watcher stops.
        /// </summary>
        protected override void PostStop()
        {
            base.PostStop();
            _heartbeatCancelable.Cancel();
            _failureDetectorReaperCancelable.Cancel();
        }

        /// <summary>
        /// Processes heartbeat, remote watch, termination, and test statistics messages.
        /// </summary>
        /// <param name="message">The message received by the remote watcher.</param>
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case HeartbeatTick _:
                    SendHeartbeat();
                    break;
                case Heartbeat _:
                    ReceiveHeartbeat();
                    break;
                case HeartbeatRsp rsp:
                    ReceiveHeartbeatRsp(rsp.AddressUid);
                    break;
                case ReapUnreachableTick _:
                    ReapUnreachable();
                    break;
                case ExpectedFirstHeartbeat heartbeat:
                    TriggerFirstHeartbeat(heartbeat.From);
                    break;
                case WatchRemote watchRemote:
                {
                    AddWatching(watchRemote.Watchee, watchRemote.Watcher);
                    break;
                }
                case UnwatchRemote unwatchRemote:
                {
                    RemoveWatch(unwatchRemote.Watchee, unwatchRemote.Watcher);
                    break;
                }
                // test purpose
                case Terminated t:
                {
                    ProcessTerminated(t.ActorRef.AsInstanceOf<IInternalActorRef>(), t.ExistenceConfirmed, t.AddressTerminated);
                    break;
                }
                case Stats _:
                {
                    var watchSet = ImmutableHashSet.Create(Watching.SelectMany(pair =>
                    {
                        var list = new List<(IActorRef, IActorRef)>(pair.Value.Count);
                        var wee = pair.Key;
                        list.AddRange(pair.Value.Select(wer => ((IActorRef)wee, (IActorRef)wer)));
                        return list;
                    }).ToArray());
                    Sender.Tell(new Stats(watchSet.Count(), WatchingNodes.Count, watchSet,
                        ImmutableHashSet.Create(WatchingNodes.ToArray())));
                    break;
                }
                default:
                    Unhandled(message);
                    break;
            }
        }

        private void ReceiveHeartbeat()
        {
            Sender.Tell(_selfHeartbeatRspMsg);
        }

        private void ReceiveHeartbeatRsp(long uid)
        {
            var from = Sender.Path.Address;

            if (_failureDetector.IsMonitoring(from))
                Log.Debug("Received heartbeat rsp from [{0}]", from);
            else
                Log.Debug("Received first heartbeat rsp from [{0}]", from);

            if (WatcheeByNodes.ContainsKey(from) && !Unreachable.Contains(from))
            {
                if (!_addressUids.TryGetValue(from, out long addressUid) || addressUid != uid)
                    ReWatch(from);

                _addressUids[from] = uid;
                _failureDetector.Heartbeat(from);
            }
        }

        private void ReapUnreachable()
        {
            foreach (var a in WatchingNodes)
            {
                if (!Unreachable.Contains(a) && !_failureDetector.IsAvailable(a))
                {
                    Log.Warning("Detected unreachable: [{0}]", a);
                    var nullableAddressUid =
                        _addressUids.TryGetValue(a, out long addressUid) ? new long?(addressUid) : null;

                    Quarantine(a, nullableAddressUid);
                    PublishAddressTerminated(a);
                    Unreachable.Add(a);
                }
            }
        }

        /// <summary>
        /// Publishes an <see cref="AddressTerminated"/> signal while handling a failure-detector decision that the remote address is unreachable.
        /// </summary>
        /// <param name="address">The remote address reported as unreachable by the failure detector.</param>
        protected virtual void PublishAddressTerminated(Address address)
        {
            AddressTerminatedTopic.Get(Context.System).Publish(new AddressTerminated(address));
        }

        /// <summary>
        /// Quarantines the remote address and UID through the remote actor reference provider.
        /// </summary>
        /// <param name="address">The remote system address to quarantine.</param>
        /// <param name="addressUid">The remote system UID, if it has been confirmed.</param>
        protected virtual void Quarantine(Address address, long? addressUid)
        {
            _remoteProvider.Quarantine(address, addressUid);
        }

        /// <summary>
        /// Adds a watcher for a remote actor and subscribes to that actor's termination.
        /// </summary>
        /// <param name="watchee">The actor whose termination is being watched.</param>
        /// <param name="watcher">The actor that requested the watch.</param>
        /// <exception cref="InvalidOperationException">The remote watcher cannot itself be registered as the watcher.</exception>
        protected void AddWatching(IInternalActorRef watchee, IInternalActorRef watcher)
        {
            // TODO: replace with Code Contracts assertion
            if(watcher.Equals(Self)) throw new InvalidOperationException("Watcher cannot be the RemoteWatcher!");
            Log.Debug("Watching: [{0} -> {1}]", watcher.Path, watchee.Path);

            if (Watching.TryGetValue(watchee, out var watching))
                watching.Add(watcher);
            else
                Watching.Add(watchee, new HashSet<IInternalActorRef> { watcher });
            WatchNode(watchee);

            // add watch from self, this will actually send a Watch to the target when necessary
            Context.Watch(watchee);
        }

        /// <summary>
        /// Adds a remote actor to the set of actors watched at its address, resetting prior unreachable state when needed.
        /// </summary>
        /// <param name="watchee">The actor whose remote address should be monitored.</param>
        protected virtual void WatchNode(IInternalActorRef watchee)
        {
            var watcheeAddress = watchee.Path.Address;
            if (!WatcheeByNodes.ContainsKey(watcheeAddress) && Unreachable.Contains(watcheeAddress))
            {
                // first watch to a node after a previous unreachable
                Unreachable.Remove(watcheeAddress);
                _failureDetector.Remove(watcheeAddress);
            }

            if (WatcheeByNodes.TryGetValue(watcheeAddress, out var watchees))
                watchees.Add(watchee);
            else
                WatcheeByNodes.Add(watcheeAddress, new HashSet<IInternalActorRef> { watchee });
        }


        /// <summary>
        /// Removes one watcher and ends the self-watch when no watchers remain for the actor.
        /// </summary>
        /// <param name="watchee">The actor whose watch is being removed.</param>
        /// <param name="watcher">The actor that requested the unwatch.</param>
        /// <exception cref="InvalidOperationException">The remote watcher cannot itself be registered as the watcher.</exception>
        protected void RemoveWatch(IInternalActorRef watchee, IInternalActorRef watcher)
        {
            if (watcher.Equals(Self)) throw new InvalidOperationException("Watcher cannot be the RemoteWatcher!");
            Log.Debug($"Unwatching: [{watcher.Path} -> {watchee.Path}]");
            if (Watching.TryGetValue(watchee, out var watchers))
            {
                watchers.Remove(watcher);
                if (!watchers.Any())
                {
                    // clean up self watch when no more watchers of this watchee
                    Log.Debug("Cleanup self watch of [{0}]", watchee.Path);
                    Context.Unwatch(watchee);
                    RemoveWatchee(watchee);
                }
            }
        }

        /// <summary>
        /// Removes a terminated actor and its watchers from the remote watch state.
        /// </summary>
        /// <param name="watchee">The terminated actor to remove.</param>
        protected void RemoveWatchee(IInternalActorRef watchee)
        {
            var watcheeAddress = watchee.Path.Address;
            Watching.Remove(watchee);
            if (WatcheeByNodes.TryGetValue(watcheeAddress, out var watchees))
            {
                watchees.Remove(watchee);
                if (!watchees.Any())
                {
                    // unwatched last watchee on that node
                    Log.Debug("Unwatched last watchee of node: [{0}]", watcheeAddress);
                    UnwatchNode(watcheeAddress);
                }
            }
        }

        /// <summary>
        /// Stops monitoring an address after its last watched actor is removed.
        /// </summary>
        /// <param name="watcheeAddress">The remote address no longer being watched.</param>
        protected void UnwatchNode(Address watcheeAddress)
        {
            WatcheeByNodes.Remove(watcheeAddress);
            _addressUids.Remove(watcheeAddress);
            _failureDetector.Remove(watcheeAddress);
        }

      
        private void ProcessTerminated(IInternalActorRef watchee, bool existenceConfirmed, bool addressTerminated)
        {
            Log.Debug("Watchee terminated: [{0}]", watchee.Path);

            // When watchee is stopped it sends DeathWatchNotification to this RemoteWatcher,
            // which will propagate it to all watchers of this watchee.
            // addressTerminated case is already handled by the watcher itself in DeathWatch trait

            if (!addressTerminated)
            {
                if (Watching.TryGetValue(watchee, out var watchers))
                {
                    foreach (var watcher in watchers)
                    {
                        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                        watcher.SendSystemMessage(new DeathWatchNotification(watchee, existenceConfirmed, addressTerminated));
                    }
                }
            }

            RemoveWatchee(watchee);
        }

        private void SendHeartbeat()
        {
            foreach (var a in WatchingNodes)
            {
                if (!Unreachable.Contains(a))
                {
                    if (_failureDetector.IsMonitoring(a))
                    {
                        Log.Debug("Sending Heartbeat to [{0}]", a);
                    }
                    else
                    {
                        Log.Debug("Sending first Heartbeat to [{0}]", a);
                        // schedule the expected first heartbeat for later, which will give the
                        // other side a chance to reply, and also trigger some resends if needed
                        _scheduler.ScheduleTellOnce(_heartbeatExpectedResponseAfter, Self, new ExpectedFirstHeartbeat(a), Self);
                    }
                    Context.ActorSelection(new RootActorPath(a) / Self.Path.Elements).Tell(Heartbeat.Instance);
                }
            }
        }

        private void TriggerFirstHeartbeat(Address address)
        {
            if (WatcheeByNodes.ContainsKey(address) && !_failureDetector.IsMonitoring(address))
            {
                Log.Debug("Trigger extra expected heartbeat from [{0}]", address);
                _failureDetector.Heartbeat(address);
            }
        }

        /// <summary>
        /// To ensure that we receive heartbeat messages from the right actor system
        /// incarnation we send Watch again for the first HeartbeatRsp (containing
        /// the system UID) and if HeartbeatRsp contains a new system UID.
        /// Terminated will be triggered if the watchee (including correct Actor UID)
        /// does not exist.
        /// </summary>
        /// <param name="address"></param>
        private void ReWatch(Address address)
        {
            var watcher = Self.AsInstanceOf<IInternalActorRef>();
            foreach (var watchee in WatcheeByNodes[address])
            {
                Log.Debug("Re-watch [{0} -> {1}]", watcher.Path, watchee.Path);
                watchee.SendSystemMessage(new Watch(watchee, watcher)); // ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS ⬅⬅⬅
            }
        }

        protected readonly ILoggingAdapter Log = Context.GetLogger();
    }
}

