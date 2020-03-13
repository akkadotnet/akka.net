//-----------------------------------------------------------------------
// <copyright file="RemoteWatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
        /// TBD
        /// </summary>
        /// <param name="failureDetector">TBD</param>
        /// <param name="heartbeatInterval">TBD</param>
        /// <param name="unreachableReaperInterval">TBD</param>
        /// <param name="heartbeatExpectedResponseAfter">TBD</param>
        /// <returns>TBD</returns>
        public static Props Props(
            IFailureDetectorRegistry<Address> failureDetector,
            TimeSpan heartbeatInterval,
            TimeSpan unreachableReaperInterval,
            TimeSpan heartbeatExpectedResponseAfter)
        {
            return Actor.Props.Create(() => new RemoteWatcher(failureDetector, heartbeatInterval, unreachableReaperInterval, heartbeatExpectedResponseAfter))
                .WithDeploy(Deploy.Local);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract class WatchCommand
        {
            readonly IInternalActorRef _watchee;
            readonly IInternalActorRef _watcher;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="watchee">TBD</param>
            /// <param name="watcher">TBD</param>
            protected WatchCommand(IInternalActorRef watchee, IInternalActorRef watcher)
            {
                _watchee = watchee;
                _watcher = watcher;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IInternalActorRef Watchee => _watchee;

            /// <summary>
            /// TBD
            /// </summary>
            public IInternalActorRef Watcher => _watcher;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class WatchRemote : WatchCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="watchee">TBD</param>
            /// <param name="watcher">TBD</param>
            public WatchRemote(IInternalActorRef watchee, IInternalActorRef watcher)
                : base(watchee, watcher)
            {
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class UnwatchRemote : WatchCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="watchee">TBD</param>
            /// <param name="watcher">TBD</param>
            public UnwatchRemote(IInternalActorRef watchee, IInternalActorRef watcher)
                : base(watchee, watcher)
            {
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Heartbeat : IPriorityMessage
        {
            private Heartbeat()
            {
            }

            private static readonly Heartbeat _instance = new Heartbeat();

            /// <summary>
            /// TBD
            /// </summary>
            public static Heartbeat Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public class HeartbeatRsp : IPriorityMessage
        {
            readonly int _addressUid;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="addressUid">TBD</param>
            public HeartbeatRsp(int addressUid)
            {
                _addressUid = addressUid;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public int AddressUid
            {
                get { return _addressUid; }
            }
        }

        // sent to self only
        /// <summary>
        /// TBD
        /// </summary>
        public class HeartbeatTick
        {
            private HeartbeatTick() { }
            private static readonly HeartbeatTick _instance = new HeartbeatTick();

            /// <summary>
            /// TBD
            /// </summary>
            public static HeartbeatTick Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public class ReapUnreachableTick
        {
            private ReapUnreachableTick() { }
            private static readonly ReapUnreachableTick _instance = new ReapUnreachableTick();

            /// <summary>
            /// TBD
            /// </summary>
            public static ReapUnreachableTick Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class ExpectedFirstHeartbeat
        {
            readonly Address _from;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="from">TBD</param>
            public ExpectedFirstHeartbeat(Address @from)
            {
                _from = @from;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Address From
            {
                get { return _from; }
            }
        }

        // test purpose
        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Stats
        {
            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as Stats;
                if (other == null) return false;
                return _watching == other._watching && _watchingNodes == other._watchingNodes;
            }

            /// <inheritdoc/>
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
            /// TBD
            /// </summary>
            public static Stats Empty = Counts(0, 0);

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="watching">TBD</param>
            /// <param name="watchingNodes">TBD</param>
            /// <returns>TBD</returns>
            public static Stats Counts(int watching, int watchingNodes)
            {
                return new Stats(watching, watchingNodes);
            }

            readonly int _watching;
            readonly int _watchingNodes;
            readonly ImmutableHashSet<(IActorRef, IActorRef)> _watchingRefs;
            readonly ImmutableHashSet<Address> _watchingAddresses;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="watching">TBD</param>
            /// <param name="watchingNodes">TBD</param>
            public Stats(int watching, int watchingNodes) : this(watching, watchingNodes, 
                ImmutableHashSet<(IActorRef, IActorRef)>.Empty, ImmutableHashSet<Address>.Empty) { }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="watching">TBD</param>
            /// <param name="watchingNodes">TBD</param>
            /// <param name="watchingRefs">TBD</param>
            /// <param name="watchingAddresses">TBD</param>
            public Stats(int watching, int watchingNodes, ImmutableHashSet<(IActorRef, IActorRef)> watchingRefs, ImmutableHashSet<Address> watchingAddresses)
            {
                _watching = watching;
                _watchingNodes = watchingNodes;
                _watchingRefs = watchingRefs;
                _watchingAddresses = watchingAddresses;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public int Watching => _watching;

            /// <summary>
            /// TBD
            /// </summary>
            public int WatchingNodes => _watchingNodes;

            /// <summary>
            /// TBD
            /// </summary>
            public ImmutableHashSet<(IActorRef, IActorRef)> WatchingRefs => _watchingRefs;

            /// <summary>
            /// TBD
            /// </summary>
            public ImmutableHashSet<Address> WatchingAddresses => _watchingAddresses;

            /// <inheritdoc/>
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
            /// TBD
            /// </summary>
            /// <param name="watching">TBD</param>
            /// <param name="watchingNodes">TBD</param>
            /// <param name="watchingRefs">TBD</param>
            /// <param name="watchingAddresses">TBD</param>
            /// <returns>TBD</returns>
            public Stats Copy(int watching, int watchingNodes, ImmutableHashSet<(IActorRef, IActorRef)> watchingRefs = null, ImmutableHashSet<Address> watchingAddresses = null)
            {
                return new Stats(watching, watchingNodes, watchingRefs ?? WatchingRefs, watchingAddresses ?? WatchingAddresses);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="failureDetector">TBD</param>
        /// <param name="heartbeatInterval">TBD</param>
        /// <param name="unreachableReaperInterval">TBD</param>
        /// <param name="heartbeatExpectedResponseAfter">TBD</param>
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
            var systemProvider = Context.System.AsInstanceOf<ExtendedActorSystem>().Provider as IRemoteActorRefProvider;
            if (systemProvider != null) _remoteProvider = systemProvider;
            else throw new ConfigurationException(
                $"ActorSystem {Context.System} needs to have a 'RemoteActorRefProvider' enabled in the configuration, current uses {Context.System.AsInstanceOf<ExtendedActorSystem>().Provider.GetType().FullName}");

            _heartbeatCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(heartbeatInterval, heartbeatInterval, Self, HeartbeatTick.Instance, Self);
            _failureDetectorReaperCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(unreachableReaperInterval, unreachableReaperInterval, Self, ReapUnreachableTick.Instance, Self);
        }

        private readonly IFailureDetectorRegistry<Address> _failureDetector;
        private readonly TimeSpan _heartbeatExpectedResponseAfter;
        private readonly IScheduler _scheduler = Context.System.Scheduler;
        private readonly IRemoteActorRefProvider _remoteProvider;
        private readonly HeartbeatRsp _selfHeartbeatRspMsg = new HeartbeatRsp(AddressUidExtension.Uid(Context.System));
       
        /// <summary>
        ///  Actors that this node is watching, map of watchee --> Set(watchers)
        /// </summary>
        protected readonly Dictionary<IInternalActorRef, HashSet<IInternalActorRef>>  Watching = new Dictionary<IInternalActorRef, HashSet<IInternalActorRef>>();

        /// <summary>
        /// Nodes that this node is watching, i.e. expecting heartbeats from these nodes. Map of address --> Set(watchee) on this address.
        /// </summary>
        protected readonly Dictionary<Address, HashSet<IInternalActorRef>> WatcheeByNodes = new Dictionary<Address, HashSet<IInternalActorRef>>();

        /// <summary>
        /// TBD
        /// </summary>
        protected ICollection<Address> WatchingNodes => WatcheeByNodes.Keys;
        /// <summary>
        /// TBD
        /// </summary>
        protected HashSet<Address> Unreachable { get; } = new HashSet<Address>();

        private readonly Dictionary<Address, int> _addressUids = new Dictionary<Address, int>();

        private readonly ICancelable _heartbeatCancelable;
        private readonly ICancelable _failureDetectorReaperCancelable;

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            base.PostStop();
            _heartbeatCancelable.Cancel();
            _failureDetectorReaperCancelable.Cancel();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected override void OnReceive(object message)
        {
            if (message is HeartbeatTick) SendHeartbeat();
            else if (message is Heartbeat) ReceiveHeartbeat();
            else if (message is HeartbeatRsp) ReceiveHeartbeatRsp(((HeartbeatRsp)message).AddressUid);
            else if (message is ReapUnreachableTick) ReapUnreachable();
            else if (message is ExpectedFirstHeartbeat) TriggerFirstHeartbeat(((ExpectedFirstHeartbeat)message).From);
            else if (message is WatchRemote)
            {
                var watchRemote = (WatchRemote)message;
                AddWatching(watchRemote.Watchee, watchRemote.Watcher);
            }
            else if (message is UnwatchRemote)
            {
                var unwatchRemote = (UnwatchRemote)message;
                RemoveWatch(unwatchRemote.Watchee, unwatchRemote.Watcher);
            }
            else if (message is Terminated)
            {
                var t = (Terminated)message;
                ProcessTerminated(t.ActorRef.AsInstanceOf<IInternalActorRef>(), t.ExistenceConfirmed, t.AddressTerminated);
            }
            // test purpose
            else if (message is Stats)
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
            }
            else
            {
                Unhandled(message);
            }
        }

        private void ReceiveHeartbeat()
        {
            Sender.Tell(_selfHeartbeatRspMsg);
        }

        private void ReceiveHeartbeatRsp(int uid)
        {
            var from = Sender.Path.Address;

            if (_failureDetector.IsMonitoring(from))
                Log.Debug("Received heartbeat rsp from [{0}]", from);
            else
                Log.Debug("Received first heartbeat rsp from [{0}]", from);

            if (WatcheeByNodes.ContainsKey(from) && !Unreachable.Contains(from))
            {
                if (_addressUids.TryGetValue(from, out int addressUid))
                {
                    if (addressUid != uid)
                        ReWatch(from);
                }
                else
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
                        _addressUids.TryGetValue(a, out int addressUid) ? new int?(addressUid) : null;

                    Quarantine(a, nullableAddressUid);
                    PublishAddressTerminated(a);
                    Unreachable.Add(a);
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="address">TBD</param>
        protected virtual void PublishAddressTerminated(Address address)
        {
            AddressTerminatedTopic.Get(Context.System).Publish(new AddressTerminated(address));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="address">TBD</param>
        /// <param name="addressUid">TBD</param>
        protected virtual void Quarantine(Address address, int? addressUid)
        {
            _remoteProvider.Quarantine(address, addressUid);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="watchee">TBD</param>
        /// <param name="watcher">TBD</param>
        /// <exception cref="InvalidOperationException">TBD</exception>
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
        /// TBD
        /// </summary>
        /// <param name="watchee">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="watchee">TBD</param>
        /// <param name="watcher">TBD</param>
        /// <exception cref="InvalidOperationException">TBD</exception>
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
        /// TBD
        /// </summary>
        /// <param name="watchee">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="watcheeAddress">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        protected readonly ILoggingAdapter Log = Context.GetLogger();
    }
}

