using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Remote
{
    /// <summary>
    /// Remote nodes with actors that are watched are monitored by this actor to be able
    /// to detect network failures and JVM crashes. [[akka.remote.RemoteActorRefProvider]]
    /// intercepts Watch and Unwatch system messages and sends corresponding
    /// [[RemoteWatcher.WatchRemote]] and [[RemoteWatcher.UnwatchRemote]] to this actor.
    ///
    /// For a new node to be watched this actor periodically sends [[RemoteWatcher.Heartbeat]]
    /// to the peer actor on the other node, which replies with [[RemoteWatcher.HeartbeatRsp]]
    /// message back. The failure detector on the watching side monitors these heartbeat messages.
    /// If arrival of hearbeat messages stops it will be detected and this actor will publish
    /// [[akka.actor.AddressTerminated]] to the [[akka.event.AddressTerminatedTopic]].
    ///
    /// When all actors on a node have been unwatched it will stop sending heartbeat messages.
    ///
    /// For bi-directional watch between two nodes the same thing will be established in
    /// both directions, but independent of each other.
    /// </summary>
    public class RemoteWatcher : UntypedActor, IActorLogging
    {
        public static Props Props(
            DefaultFailureDetectorRegistry<Address> failureDetector,
            TimeSpan heartbeatInterval,
            TimeSpan unreachableReaperInterval,
            TimeSpan heartbeatExpectedResponseAfter)
        {
            return new Props(
                Deploy.Local, 
                typeof(RemoteWatcher), 
                new Object[]{failureDetector, heartbeatInterval, unreachableReaperInterval, heartbeatExpectedResponseAfter});
        }

        public abstract class WatchCommand
        {
            readonly ActorRef _watchee;
            readonly ActorRef _watcher;

            protected WatchCommand(ActorRef watchee, ActorRef watcher)
            {
                _watchee = watchee;
                _watcher = watcher;
            }

            public ActorRef Watchee
            {
                get { return _watchee; }
            }

            public ActorRef Watcher
            {
                get { return _watcher; }
            }
        }
        public sealed class WatchRemote : WatchCommand
        {
            public WatchRemote(ActorRef watchee, ActorRef watcher) 
                : base(watchee, watcher)
            {
            }
        }
        public sealed class UnwatchRemote : WatchCommand
        {
            public UnwatchRemote(ActorRef watchee, ActorRef watcher) 
                : base(watchee, watcher)
            {
            }
        }
        public sealed class RewatchRemote : WatchCommand
        {
            public RewatchRemote(ActorRef watchee, ActorRef watcher)
                : base(watchee, watcher)
            {
            }
        }
        public class Rewatch : Watch
        {
            public Rewatch(InternalActorRef watchee, InternalActorRef watcher) : base(watchee, watcher)
            {
            }
        }

        public sealed class Heartbeat //TODO: : IPriorityMessage
        {
            private Heartbeat()
            {
            }

            private static readonly Heartbeat _instance = new Heartbeat();

            public static Heartbeat Instance
            {
                get
                {
                    return _instance;
                }
            } 
        }

        public class HeartbeatRsp//TODO: : IPriorityMessage
        {
            readonly int _addressUid;

            public HeartbeatRsp(int addressUid)
            {
                _addressUid = addressUid;
            }

            public int AddressUid
            {
                get { return _addressUid; }
            }
        }

        // sent to self only
        public class HeartbeatTick
        {
            private HeartbeatTick() { }
            private static readonly HeartbeatTick _instance = new HeartbeatTick();

            public static HeartbeatTick Instance
            {
                get
                {
                    return _instance;
                }
            }            
        }

        public class ReapUnreachableTick
        {
            private ReapUnreachableTick() { }
            private static readonly ReapUnreachableTick _instance = new ReapUnreachableTick();

            public static ReapUnreachableTick Instance
            {
                get
                {
                    return _instance;
                }
            }             
        }

        public sealed class ExpectedFirstHeartbeat
        {
            readonly Address _from;

            public ExpectedFirstHeartbeat(Address @from)
            {
                _from = @from;
            }

            public Address From
            {
                get { return _from; }
            }
        }

        // test purpose
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
                    hash = hash * 23 +_watching.GetHashCode();
                    hash = hash * 23 + _watchingNodes.GetHashCode();

                    return hash;
                }
            }

            public static Stats Empty = Counts(0, 0);

            public static Stats Counts(int watching, int watchingNodes)
            {
                return new Stats(watching, watchingNodes, new HashSet<Tuple<ActorRef, ActorRef>>());
            }

            readonly int _watching;
            readonly int _watchingNodes;
            //TODO: This should either be a deep copy or immutable
            readonly HashSet<Tuple<ActorRef, ActorRef>> _watchingRefs;

            public Stats(int watching, int watchingNodes, HashSet<Tuple<ActorRef, ActorRef>> watchingRefs)
            {
                _watching = watching;
                _watchingNodes = watchingNodes;
                _watchingRefs = watchingRefs;
            }

            public int Watching
            {
                get { return _watching; }
            }

            public int WatchingNodes
            {
                get { return _watchingNodes; }
            }

            public override string ToString()
            {
                Func<string> formatWatchingRefs = () =>
                {
                    if (!_watchingRefs.Any()) return "";
                    return
                        String.Format(", watchingRefs=[{0}]",
                            _watchingRefs.Select(r => r.Item2.Path.Name + "-> " + r.Item1.Path.Name)
                                .Aggregate((a, b) => a + ", " + b));
                };

                return string.Format("Stats(watching={0}, watchingNodes={1}{2}", _watching, _watchingNodes,
                    formatWatchingRefs());
            }
        }

        public RemoteWatcher(
            IFailureDetectorRegistry<Address> failureDetector,
            TimeSpan heartbeatInterval,
            TimeSpan unreachableReaperInterval,
            TimeSpan heartbeatExpectedResponseAfter
            )
        {
            _failureDetector = failureDetector;
            _heartbeatExpectedResponseAfter = heartbeatExpectedResponseAfter;
            var systemProvider = Context.System.AsInstanceOf<ExtendedActorSystem>().Provider as RemoteActorRefProvider;
            if (systemProvider != null) _remoteProvider = systemProvider;
            else throw new ConfigurationException(String.Format("ActorSystem {0} needs to have a 'RemoteActorRefProvider' enabled in the configuration, current uses {1}", Context.System, Context.System.AsInstanceOf<ExtendedActorSystem>().Provider.GetType().FullName));

            _heartbeatCancellable = new CancellationTokenSource();
            _heartbeatTask = Context.System.Scheduler.Schedule(heartbeatInterval, heartbeatInterval, Self, HeartbeatTick.Instance, _heartbeatCancellable.Token);
            _failureDetectorReaperCancellable = new CancellationTokenSource();
            _failureDetectorReaperTask = Context.System.Scheduler.Schedule(unreachableReaperInterval,
                unreachableReaperInterval, Self, ReapUnreachableTick.Instance, _failureDetectorReaperCancellable.Token);
        }

        readonly IFailureDetectorRegistry<Address> _failureDetector;
        readonly TimeSpan _heartbeatExpectedResponseAfter;
        readonly Scheduler _scheduler = Context.System.Scheduler;
        readonly RemoteActorRefProvider _remoteProvider;
        readonly HeartbeatRsp _selfHeartbeatRspMsg = new HeartbeatRsp(AddressUidExtension.Uid(Context.System));
        readonly HashSet<Tuple<ActorRef, ActorRef>> _watching = new HashSet<Tuple<ActorRef, ActorRef>>();
        protected HashSet<Tuple<ActorRef, ActorRef>> Watching { get { return _watching; } } //TODO: this needs to be immutable
        readonly HashSet<Address> _watchingNodes = new HashSet<Address>();
        readonly HashSet<Address> _unreachable = new HashSet<Address>();
        protected HashSet<Address> Unreachable { get { return _unreachable; } }
        readonly Dictionary<Address, int> _addressUids = new Dictionary<Address, int>();

        readonly CancellationTokenSource _heartbeatCancellable;
        readonly Task _heartbeatTask;
        readonly CancellationTokenSource _failureDetectorReaperCancellable;
        readonly Task _failureDetectorReaperTask;

        protected override void PostStop()
        {
            base.PostStop();
            _heartbeatCancellable.Cancel();
            _failureDetectorReaperCancellable.Cancel();
        }

        protected override void OnReceive(object message)
        {
            if (message is HeartbeatTick) SendHeartbeat();
            else if (message is Heartbeat) ReceiveHeartbeat();
            else if (message is HeartbeatRsp) ReceiveHeartbeatRsp(((HeartbeatRsp) message).AddressUid);
            else if (message is ReapUnreachableTick) ReapUnreachable();
            else if (message is ExpectedFirstHeartbeat) TriggerFirstHeartbeat(((ExpectedFirstHeartbeat) message).From);
            else if (message is WatchRemote)
            {
                var watchRemote = (WatchRemote) message;
                ProcessWatchRemote(watchRemote.Watchee, watchRemote.Watcher);
            }
            else if (message is UnwatchRemote)
            {
                var unwatchRemote = (UnwatchRemote)message;
                ProcessUnwatchRemote(unwatchRemote.Watchee, unwatchRemote.Watcher);
            }
            else if (message is Terminated)
            {
                var t = (Terminated) message;
                ProcessTerminated(t.ActorRef, t.ExistenceConfirmed, t.AddressTerminated);
            }
            else if (message is RewatchRemote)
            {
                var rewatchRemote = (RewatchRemote)message;
                ProcessRewatchRemote(rewatchRemote.Watchee, rewatchRemote.Watcher);
            }

            // test purpose
            else if (message is Stats) Sender.Tell(new Stats(_watching.Count(), _watchingNodes.Count, _watching));
        }

        private void ReceiveHeartbeat()
        {
            Sender.Tell(_selfHeartbeatRspMsg);
        }

        private void ReceiveHeartbeatRsp(int uid)
        {
            var from = Sender.Path.Address;

            if (_failureDetector.IsMonitoring(from))
            {
                Log.Debug("Received heartbeat rsp from [{0}]", from);
            }
            else
            {
                Log.Debug("Received first heartbeat rsp from [{0}]", from);
            }

            if (_watchingNodes.Contains(from) && !_unreachable.Contains(from))
            {
                if (!_addressUids.ContainsKey(from) || _addressUids.ContainsKey(from))
                    ReWatch(from);
                _addressUids[from] = uid;
                _failureDetector.Heartbeat(from);
            }
        }

        private void ReapUnreachable()
        {
            foreach (var a in _watchingNodes)
            {
                if (!_unreachable.Contains(a) && !_failureDetector.IsAvailable(a))
                {
                    Log.Warning("Detected unreachable: [{0}]", a);
                    int addressUid;
                    var nullableAddressUid =
                        _addressUids.TryGetValue(a, out addressUid) ? new int?(addressUid) : null;

                    Quarantine(a, nullableAddressUid);
                    PublishAddressTerminated(a);
                    _unreachable.Add(a);
                }
            }
        }

        protected virtual void PublishAddressTerminated(Address address)
        {
            //TODO: What are consequence of not passing system through.
            //TODO: Is AddressTerminatedTopic plumbed in?
            new AddressTerminatedTopic().Publish(new AddressTerminated(address));
        }

        protected virtual void Quarantine(Address address, int? addressUid)
        {
            _remoteProvider.Quarantine(address, addressUid);
        }

        private void ProcessRewatchRemote(ActorRef watchee, ActorRef watcher)
        {
            if (_watching.Contains(Tuple.Create(watchee, watcher)))
                ProcessWatchRemote(watchee, watcher);
            else
                //has been unwatched inbetween, skip re-watch
                Log.Debug("Ignoring re-watch after being unwatched in the meantime: [{0} -> {1}]", watcher.Path,
                    watchee.Path);
        }

        private void ProcessWatchRemote(ActorRef watchee, ActorRef watcher)
        {
            //TODO: What to do about this. Remote actors seem to get 0 uid
            /*if (watchee.Path.Uid == ActorCell.UndefinedUid)
            {
                LogActorForDeprecationWarning(watchee);
            }
            else*/ if (watcher != Self)
            {
                Log.Debug("Watching: [{0} -> {1}]", watcher.Path, watchee.Path);
                AddWatching(watchee, watcher);

                // also watch from self, to be able to cleanup on termination of the watchee
                Context.Watch(watchee);
                _watching.Add(Tuple.Create(watchee, Self));
            }
        }

        private void AddWatching(ActorRef watchee, ActorRef watcher)
        {
            _watching.Add(Tuple.Create(watchee, watcher));
            var watcheeAddress = watchee.Path.Address;
            if (!_watchingNodes.Contains(watcheeAddress) && _unreachable.Contains(watcheeAddress))
            {
                // first watch to that node after previous unreachable
                _unreachable.Remove(watcheeAddress);
                _failureDetector.Remove(watcheeAddress);
            }
            _watchingNodes.Add(watcheeAddress);
        }

        protected void ProcessUnwatchRemote(ActorRef watchee, ActorRef watcher)
        {
            //TODO: What to do about this. Remote actors seem to get 0 uid
            // as ActorPathSurrogate doesn't contain the uid
            /*if (watchee.Path.Uid == ActorCell.UndefinedUid)
                LogActorForDeprecationWarning(watchee);
            else*/
                if (watcher != Self)
            {
                Log.Debug("Unwatching: [{0} -> {1}]", watcher.Path, watchee.Path);
                _watching.Remove(Tuple.Create(watchee, watcher));

                // clean up self watch when no more watchers of this watchee
                if (_watching.All(t => t.Item1 != watchee || t.Item2 == Self))
                {
                    Log.Debug("Cleanup self watch of [{0}]", watchee.Path);
                    Context.Unwatch(watchee);
                    _watching.Remove(Tuple.Create(watchee, Self));
                }
                CheckLastUnwatchOfNode(watchee.Path.Address);
            }
        }

        private void LogActorForDeprecationWarning(ActorRef watchee)
        {
            Log.Debug(
                "actorFor is deprecated, and watching a remote ActorRef acquired with actorFor is not reliable: [{0}]",
                watchee.Path);
        }

        private void ProcessTerminated(ActorRef watchee, bool existenceConfirmed, bool addressTerminated)
        {
            Log.Debug("Watchee terminated: [{0}]", watchee.Path);

            // When watchee is stopped it sends DeathWatchNotification to the watcher and to this RemoteWatcher,
            // which is also watching. Send extra DeathWatchNotification to the watcher in case the
            // DeathWatchNotification message is only delivered to RemoteWatcher. Otherwise there is a risk that
            // the monitoring is removed, subsequent node failure is not detected and the original watcher is
            // never notified. This may occur for normal system shutdown of the watchee system when not all remote
            // messages are flushed at shutdown.
            var toProcess = _watching.Where(t => t.Item1.Equals(watchee)).ToList();
            foreach (var t in toProcess)
            {
                if(!addressTerminated && t.Item2 != Self)
                    t.Item2.Tell(new DeathWatchNotification(watchee, existenceConfirmed, false));
            }

            foreach (var t in toProcess) _watching.Remove(t);

            CheckLastUnwatchOfNode(watchee.Path.Address);
        }

        private void CheckLastUnwatchOfNode(Address watcheeAddress)
        {
            if (_watchingNodes.Contains(watcheeAddress) && _watching.All(t => t.Item1.Path.Address != watcheeAddress))
            {
                // unwatched last watchee on that node
                Log.Debug("Unwatched last watchee of node: [{0}]", watcheeAddress);
                _watchingNodes.Remove(watcheeAddress);
                _addressUids.Remove(watcheeAddress);
                _failureDetector.Remove(watcheeAddress);
            }
        }

        private void SendHeartbeat()
        {
            foreach (var a in _watchingNodes)
            {
                if (!_unreachable.Contains(a))
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
                        _scheduler.ScheduleOnce(_heartbeatExpectedResponseAfter, Self, new ExpectedFirstHeartbeat(a));
                    }
                    Context.ActorSelection(new RootActorPath(a) / Self.Path.Elements).Tell(Heartbeat.Instance);
                }
            }
        }

        private void TriggerFirstHeartbeat(Address address)
        {
            if (_watchingNodes.Contains(address) && !_failureDetector.IsMonitoring(address))
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
            foreach (var t in _watching)
            {
                var wee = t.Item1 as InternalActorRef;
                var wer = t.Item2 as InternalActorRef;
                if (wee != null && wer != null)
                {
                    if (wee.Path.Address == address)
                    {
                        // this re-watch will result in a RewatchRemote message to this actor
                        // must be a special message to be able to detect if an UnwatchRemote comes in
                        // before the extra RewatchRemote, then the re-watch should be ignored
                        Log.Debug("Re-watch [{0} -> {1}]", wer, wee);
                        wee.Tell(new Rewatch(wee, wer)); // ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS
                    }
                }
            }
        }

        private LoggingAdapter _log = Logging.GetLogger(Context);
        public LoggingAdapter Log { get { return _log; } }
    }


}
