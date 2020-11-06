using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Remote.Artery.Compress;
using Akka.Remote.Artery.Utils;
using Akka.Remote.Transport;
using Akka.Streams;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API
    /// Inbound API that is used by the stream operators.
    /// Separate trait to facilitate testing without real transport.
    /// </summary>
    internal interface IInboundContext
    {
        /// <summary>
        /// The local inbound address.
        /// </summary>
        UniqueAddress LocalAddress { get; }

        ArterySettings Settings { get; }

        /// <summary>
        /// An inbound operator can send control message, e.g. a reply, to the origin
        /// address with this method. It will be sent over the control sub-channel.
        /// </summary>
        /// <param name="to"></param>
        /// <param name="message"></param>
        void SendControl(Address to, IControlMessage message);

        /// <summary>
        /// Lookup the outbound association for a given address.
        /// </summary>
        /// <param name="remoteAddress"></param>
        /// <returns></returns>
        IOutboundContext Association(Address remoteAddress);

        /// <summary>
        /// Lookup the outbound association for a given UID.
        /// </summary>
        /// <param name="uid"></param>
        /// <returns>
        /// <see cref="Option{OutboundContext}"/> if an association is found.
        /// <see cref="Option{OutboundContext}.None"/> if the UID is unknown, i.e. handshake not completed.
        /// </returns>
        IOptionVal<IOutboundContext> Association(long uid);

        Task<Done> CompleteHandshake(UniqueAddress peer);

        void PublishDropped(IInboundEnvelope inbound, string reason);
    }

    /// <summary>
    /// INTERNAL API
    /// Outbound association API that is used by the stream operators.
    /// Separate trait to facilitate testing without real transport.
    /// </summary>
    internal interface IOutboundContext
    {
        /// <summary>
        /// The local inbound address.
        /// </summary>
        UniqueAddress LocalAddress { get; }

        /// <summary>
        /// The outbound address for this association.
        /// </summary>
        Address RemoteAddress { get; }

        AssociationState AssociationState { get; }

        void Quarantine(string reason);

        /// <summary>
        /// An inbound operator can send control message, e.g. a HandshakeReq, to the remote
        /// address of this association. It will be sent over the control sub-channel.
        /// </summary>
        /// <param name="message"></param>
        void SendControl(IControlMessage message);

        /// <summary>
        /// 
        /// </summary>
        /// <returns>`true` if any of the streams are active (not stopped due to idle)</returns>
        bool IsOrdinaryMessageStreamActive();

        InboundControlJunction.IControlMessageSubject ControlSubject { get; }

        ArterySettings Settings { get; }
    }

    internal class AssociationState
    {
        public static AssociationState Create()
            => new AssociationState(
                incarnation: 1,
                lastUsedTimestamp: new AtomicLong(MonotonicClock.GetNanos()),
                controlIdleKillSwitch: Option<SharedKillSwitch>.None,
                quarantined: ImmutableDictionary<long, QuarantinedTimestamp>.Empty,
                uniqueRemoteAddress: new AtomicReference<UniqueRemoteAddressValue>(new UniqueRemoteAddressValue(Option<UniqueAddress>.None, null)));

        public sealed class QuarantinedTimestamp
        {
            public long NanoTime { get; }

            public QuarantinedTimestamp(long nanoTime)
            {
                NanoTime = nanoTime;
            }

            public override string ToString()
                => $"Quarantined {(MonotonicClock.GetNanos() - NanoTime) / 1000000000L} seconds ago";
        }

        internal sealed class UniqueRemoteAddressValue
        {
            public Option<UniqueAddress> UniqueRemoteAddress { get; }
            public ImmutableList<Action<UniqueAddress>> Listeners { get; }

            public UniqueRemoteAddressValue(Option<UniqueAddress> uniqueRemoteAddress, ImmutableList<Action<UniqueAddress>> listeners)
            {
                UniqueRemoteAddress = uniqueRemoteAddress;
                Listeners = listeners;
            }
        }

        public int Incarnation { get; }
        public AtomicLong LastUsedTimestamp { get;}
        public Option<SharedKillSwitch> ControlIdleKillSwitch { get; }
        public ImmutableDictionary<long, QuarantinedTimestamp> Quarantined { get; }

        private readonly AtomicReference<UniqueRemoteAddressValue> _uniqueRemoteAddress;
        /// <summary>
        /// Full outbound address with UID for this association.
        /// Completed by the handshake.
        /// </summary>
        public Option<UniqueAddress> UniqueRemoteAddress => _uniqueRemoteAddress.Value.UniqueRemoteAddress;

        public AssociationState(
            int incarnation,
            AtomicLong lastUsedTimestamp,
            Option<SharedKillSwitch> controlIdleKillSwitch,
            ImmutableDictionary<long, QuarantinedTimestamp> quarantined,
            AtomicReference<UniqueRemoteAddressValue> uniqueRemoteAddress)
        {
            Incarnation = incarnation;
            LastUsedTimestamp = lastUsedTimestamp;
            ControlIdleKillSwitch = controlIdleKillSwitch;
            Quarantined = quarantined;
            _uniqueRemoteAddress = uniqueRemoteAddress;
        }

        public void CompleteUniqueRemoteAddress(UniqueAddress peer)
        {
            var spinWait = new SpinWait();
            while (true)
            {
                var current = _uniqueRemoteAddress.Value;
                if (current.UniqueRemoteAddress.HasValue)
                    return;
                var newValue = new UniqueRemoteAddressValue(new Option<UniqueAddress>(peer), null);

                if (_uniqueRemoteAddress.CompareAndSet(current, newValue))
                {
                    foreach (var callback in current.Listeners)
                        callback(peer);
                    break;
                }
                spinWait.SpinOnce();
            }
        }

        public void AddUniqueRemoteAddressListener(Action<UniqueAddress> callback)
        {
            var spinWait = new SpinWait();
            while (true)
            {
                var current = _uniqueRemoteAddress.Value;
                if (current.UniqueRemoteAddress.HasValue)
                {
                    callback(current.UniqueRemoteAddress.Value);
                    break;
                }

                var newValue = new UniqueRemoteAddressValue(Option<UniqueAddress>.None, current.Listeners.Insert(0, callback));
                if (_uniqueRemoteAddress.CompareAndSet(current, newValue))
                    break;
                spinWait.SpinOnce();
            }
        }

        public void RemoveUniqueRemoteAddressListener(Action<UniqueAddress> callback)
        {
            var spinWait = new SpinWait();
            while (true)
            {
                var current = _uniqueRemoteAddress.Value;
                var newValue = new UniqueRemoteAddressValue(current.UniqueRemoteAddress, current.Listeners.Remove(callback));
                if (_uniqueRemoteAddress.CompareAndSet(current, newValue))
                    break;
                spinWait.SpinOnce();
            }
        }

        public AssociationState NewIncarnation(UniqueAddress remoteAddress)
            => new AssociationState(
                Incarnation + 1,
                new AtomicLong(MonotonicClock.GetNanos()), 
                ControlIdleKillSwitch,
                Quarantined,
                new AtomicReference<UniqueRemoteAddressValue>(new UniqueRemoteAddressValue(new Option<UniqueAddress>(remoteAddress), null)));

        public AssociationState NewQuarantined()
        {
            if (!UniqueRemoteAddress.HasValue)
                return this;

            var address = UniqueRemoteAddress.Value;
            var quarantined = 
                !Quarantined.ContainsKey(address.Uid) ? 
                    Quarantined.Add(address.Uid, new QuarantinedTimestamp(MonotonicClock.GetNanos())) : 
                    Quarantined.SetItem(address.Uid, new QuarantinedTimestamp(MonotonicClock.GetNanos()));

            return new AssociationState(
                Incarnation,
                new AtomicLong(MonotonicClock.GetNanos()), 
                ControlIdleKillSwitch,
                quarantined,
                _uniqueRemoteAddress);
        }

        public bool IsQuarantined()
            => UniqueRemoteAddress.HasValue && IsQuarantined(UniqueRemoteAddress.Value.Uid);

        public bool IsQuarantined(long uid)
            => Quarantined.ContainsKey(uid);

        public AssociationState WithControlIdleKillSwitch(Option<SharedKillSwitch> killSwitch)
            => new AssociationState(
                Incarnation,
                LastUsedTimestamp,
                killSwitch,
                Quarantined,
                _uniqueRemoteAddress);

        public override string ToString()
            => $"AssociationState({Incarnation}, {(!UniqueRemoteAddress.HasValue ? "unknown" : UniqueRemoteAddress.Value.ToString())})";
    }

    internal class FlushOnShutdown : UntypedActor
    {
        public static Props Props(
            Task<Done> done,
            TimeSpan timeout,
            IInboundContext inboundContext,
            ImmutableHashSet<Association> associations)
        {
            associations.Requiring(a => !a.IsEmpty, $"{nameof(associations)} must not be empty");
            return new Props(typeof(FlushOnShutdown), new object[] {done, timeout, inboundContext, associations});
        }

        public class TimeoutMessage
        { }

        public Task<Done> Done { get; }
        public TimeSpan Timeout { get; }
        public IInboundContext InboundContext { get; }
        public ImmutableHashSet<Association> Associations { get; }

        private ImmutableDictionary<UniqueAddress, int> _remaining = ImmutableDictionary<UniqueAddress, int>.Empty;

        private ICancelable _timeoutTask;

        public FlushOnShutdown(
            Task<Done> done,
            TimeSpan timeout,
            IInboundContext inboundContext,
            ImmutableHashSet<Association> associations)
        {
            Done = done;
            Timeout = timeout;
            InboundContext = inboundContext;
            Associations = associations;

            // ARTERY: Scala code can explicitly state which threading context a scheduled lambda is being run, WE DON'T HAVE THIS
            // val timeoutTask = context.system.scheduler.scheduleOnce(timeout, self, FlushOnShutdown.Timeout)(context.dispatcher)
            _timeoutTask = Context.System.Scheduler.ScheduleTellOnceCancelable(timeout, Self, new TimeoutMessage(), Self);
        }

        protected override void PreStart()
        {
            try
            {
                foreach (var a in Associations)
                {
                    var ackExpected = a.SendTerminationHint(Self);
                    Option<UniqueAddress> address = a.AssociationState.UniqueRemoteAddress;
                    if(address.HasValue)
                    {
                        _remaining = _remaining.ContainsKey(address.Value) ? 
                            _remaining.SetItem(address.Value, ackExpected) : 
                            _remaining.Add(address.Value, ackExpected);
                    }
                    else
                    {
                        // Ignore, handshake was not completed on this association
                    }
                }

                if (_remaining.Values.Sum() == 0)
                {
                    // ARTERY: set done task to success
                    // done.trySuccess(Done)
                    Context.Stop(Self);
                }
            }
            catch (Exception e)
            {
                if (!e.NonFatal()) throw;

                // ARTERY: set done task exception
                // done.tryFailure(e)
                throw;
            }
        }

        protected override void PostStop()
        {
            _timeoutTask.Cancel();
            // ARTERY: set done task to success
            // done.trySuccess(Done)
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ActorSystemTerminatingAck from:
                    // Just treat unexpected acks as systems from which zero acks are expected
                    var acksRemaining = _remaining.GetOrElse(from.From, 0);
                    _remaining = acksRemaining <= 1 ? 
                        _remaining.Remove(from.From) : 
                        _remaining.SetItem(from.From, acksRemaining - 1);

                    if(_remaining.IsEmpty)
                        Context.Stop(Self);
                    break;

                case TimeoutMessage _:
                    Context.Stop(Self);
                    break;
            }
        }
    }

    internal abstract class ArteryTransport : RemoteTransport, IInboundContext
    {
        public const string ProtocolName = "akka";

        // Note that the used version of the header format for outbound messages is defined in
        // `ArterySettings.Version` because that may depend on configuration settings.
        // This is the highest supported version on receiving (decoding) side.
        // ArterySettings.Version can be lower than this HighestVersion to support rolling upgrades.
        public const byte HighestVersion = 0;

        public const int ControlStreamId = 1;
        public const int OrdinaryStreamId = 2;
        public const int LargeStreamId = 3;

        public static string StreamName(int streamId)
        {
            switch (streamId)
            {
                case ControlStreamId: return "control";
                case LargeStreamId: return "large message";
                case OrdinaryStreamId: return "message";
                default:
                    throw new Exception($"Unknown stream id: {streamId}");
            }
        }

        private volatile UniqueAddress _localAddress;
        private volatile UniqueAddress _bindAddress;
        private volatile ImmutableHashSet<Address> _addresses;
        protected volatile ActorMaterializer _materializer;
        protected volatile ActorMaterializer _controlMaterializer;
        private volatile InboundControlJunction.IControlMessageSubject _controlSubject;
        private volatile MessageDispatcher _messageDispatcher;

        private readonly ILoggingAdapter _log;
        private readonly IRemotingFlightRecorder _flightRecorder;

        /*
         * Compression tables must be created once, such that inbound lane restarts don't cause dropping of the tables.
         * However are the InboundCompressions are owned by the Decoder operator, and any call into them must be looped through the Decoder!
         *
         * Use `inboundCompressionAccess` (provided by the materialized `Decoder`) to call into the compression infrastructure.
         */
        protected readonly IInboundCompressions InboundCompressions;
        private Option<InboundCompressionAccess> _inboundCompressionAccess = Option<InboundCompressionAccess>.None;
        // Only access compression tables via the CompressionAccess
        public Option<InboundCompressionAccess> InboundCompressionAccess => _inboundCompressionAccess;

        public UniqueAddress BindAddress => Volatile.Read(ref _bindAddress);

        public UniqueAddress LocalAddress => Volatile.Read(ref _localAddress);
        public override Address DefaultAddress => LocalAddress?.Address;
        public override ISet<Address> Addresses => Volatile.Read(ref _addresses);
        public override Address LocalAddressForRemote(Address remote)
            => DefaultAddress;

        protected SharedKillSwitch KillSwitch => KillSwitches.Shared("transportKillSwitch");

        protected AtomicReference<ImmutableDictionary<int, InboundStreamMatValues<Lifecycle>>> StreamMatValues;
        private AtomicBoolean _hasBeenShutdown = new AtomicBoolean(false);

        private SharedTestState _testState;
        protected int InboundLanes => Settings.Advanced.InboundLanes;

        private readonly bool _largeMessageChannelEnabled;
        private readonly WildcardIndex<NotUsed> _priorityMessageDestinations;

        private RestartCounter _restartCounter;

        protected readonly EnvelopeBufferPool LargeEnvelopeBufferPool;

        private readonly ObjectPool<ReusableInboundEnvelope> _inboundEnvelopePool;
        // The outboundEnvelopePool is shared among all outbound associations
        private readonly ObjectPool<ReusableOutboundEnvelope> _outboundEnvelopePool;

        private readonly AssociationRegistry _associationRegistry;

        private ImmutableHashSet<Address> _remoteAddresses;

        public ArterySettings Settings { get; }

        protected ArteryTransport(ExtendedActorSystem system, RemoteActorRefProvider provider) : base(system, provider)
        {
            // TODO: Scala logging is way more advanced than ours, this logger supposed to have marker adapter added to it
            _log = Logging.GetLogger(system, GetType());
            _flightRecorder = RemotingFlightRecorderExtension.Get(system);
            _log.Debug($"Using flight recorder {_flightRecorder}");

            Settings = provider.RemoteSettings.Artery;

            if(Settings.Advanced.Compression.Enabled)
                InboundCompressions = new InboundCompressionsImpl(system, this, Settings.Advanced.Compression, _flightRecorder);
            else
                InboundCompressions = new NoInboundCompressions();

            _largeMessageChannelEnabled =
                !Settings.LargeMessageDestinations.WildcardTree.IsEmpty ||
                !Settings.LargeMessageDestinations.DoubleWildcardTree.IsEmpty;

            _priorityMessageDestinations = new WildcardIndex<NotUsed>()
                // These destinations are not defined in configuration because it should not
                // be possible to abuse the control channel
                .Insert(new[] { "system", "remote-watcher" }, NotUsed.Instance)
                .Insert(new[] { "system", "cluster", "core", "daemon", "heartbeatSender" }, NotUsed.Instance)
                .Insert(new[] { "system", "cluster", "core", "daemon", "crossDcHeartbeatSender" }, NotUsed.Instance)
                .Insert(new[] { "system", "cluster", "heartbeatReceiver" }, NotUsed.Instance);

            _restartCounter = new RestartCounter(Settings.Advanced.InboundMaxRestarts, Settings.Advanced.InboundRestartTimeout);

            LargeEnvelopeBufferPool = _largeMessageChannelEnabled
                ? new EnvelopeBufferPool(Settings.Advanced.MaximumLargeFrameSize, Settings.Advanced.LargeBufferPoolSize)
                : new EnvelopeBufferPool(0, 2); // not used

            _inboundEnvelopePool = ReusableInboundEnvelope.CreateObjectPool(16);
            _outboundEnvelopePool = ReusableOutboundEnvelope.CreateObjectPool(
                Settings.Advanced.OutboundLargeMessageQueueSize * Settings.Advanced.OutboundLanes * 3);

            // ARTERY: AssociationRegistry is different than the one in TestTransport
            Func<Address, Association> createAssociation = remoteAddress => new Association(
                this,
                _materializer,
                _controlMaterializer,
                remoteAddress,
                _controlSubject,
                Settings.LargeMessageDestinations,
                _priorityMessageDestinations,
                _outboundEnvelopePool);
            _associationRegistry = new AssociationRegistry(createAssociation);

            _remoteAddresses = _associationRegistry.AllAssociations;


        }

        public override void Start()
        {
            if (System.Settings.ClrShutdownHooks)
                Runtime.GetRuntime().AddShutdownHook(ShutdownHook);

            StartTransport();
            _flightRecorder.TransportStarted();

            var systemMaterializer = new SystemMaterializer(System);
            _materializer =
                systemMaterializer.CreateAdditionalLegacySystemMaterializer(
                    "remote",
                    Settings.Advanced.MaterializerSettings);
            _controlMaterializer = systemMaterializer.CreateAdditionalLegacySystemMaterializer(
                "remoteControl",
                Settings.Advanced.ControlStreamMaterializerSettings);

            _messageDispatcher = new MessageDispatcher(System, Provider);
            _flightRecorder.TransportMaterializerStarted();

            (int port, int boundPort) = BindInboundStreams();

            _localAddress = new UniqueAddress(
                new Address(ArteryTransport.ProtocolName, System.Name, Settings.Canonical.Hostname, port),
                AddressUidExtension.Uid(System));

            _addresses = ImmutableHashSet<Address>.Empty.Add(_localAddress.Address);

            _bindAddress = new UniqueAddress(
                new Address(ArteryTransport.ProtocolName, System.Name, Settings.Bind.Hostname, boundPort), 
                AddressUidExtension.Uid(System));

            _flightRecorder.TransportUniqueAddressSet(_localAddress);

            RunInboundStreams(port, boundPort);

            _flightRecorder.TransportStartupFinished();

            StartRemoveQuarantinedAssociationTask();

            if(_localAddress.Address == _bindAddress.Address)
                _log.Info($"Remoting started with transport [Artery {Settings.Transport}]; listening on address [{BindAddress.Address}] with UID [{BindAddress.Uid}]");
            else
                _log.Info($"Remoting started with transport [Artery {Settings.Transport}]; listening on address [{_localAddress.Address}] and bound to [{_bindAddress.Address}] with UID [{_localAddress.Uid}]");
        }

        protected abstract void StartTransport();

        /// <summary>
        /// Bind to the ports for inbound streams. If '0' is specified, this will also select an
        /// arbitrary free local port. For UDP, we only select the port and leave the actual
        /// binding to Aeron when running the inbound stream.
        ///
        /// After calling this method the 'localAddress' and 'bindAddress' fields can be set.
        /// </summary>
        /// <returns></returns>
        protected abstract (int, int) BindInboundStreams();

        /// <summary>
        /// Run the inbound streams that have been previously bound.
        ///
        /// Before calling this method the 'localAddress' and 'bindAddress' should have been set.
        /// </summary>
        /// <param name="port"></param>
        /// <param name="bindPort"></param>
        protected abstract void RunInboundStreams(int port, int bindPort);

        private void StartRemoveQuarantinedAssociationTask()
        {
            var removeAfter = Settings.Advanced.RemoveQuarantinedAssociationAfter;
            var interval = new TimeSpan(removeAfter.Ticks / 2);

            // ARTERY: Scala code can explicitly state which threading context a scheduled lambda is being run, WE DON'T HAVE THIS
            /*
             * system.scheduler.scheduleWithFixedDelay(removeAfter, interval) { () =>
             *     if (!isShutdown)
             *         associationRegistry.removeUnusedQuarantined(removeAfter)
             * }(system.dispatchers.internalDispatcher)
             */
            System.Scheduler.ScheduleWithFixedDelay(removeAfter, interval, () =>
            {
                if (!IsShutdown)
                    _associationRegistry.RemoveUnusedQuarantined(removeAfter);
            });
        }

        /// <summary>
        /// Select inbound lane based on destination to preserve message order,
        /// Also include the uid of the sending system in the hash to spread
        /// "hot" destinations, e.g. ActorSelection anchor.
        /// </summary>
        // ARTERY: The original code is a bit weird, please recheck port
        protected int InboundLanePartitioner(IInboundEnvelope env) 
        {
            var r = env.Recipient;
            if (!r.HasValue) 
                return env.Lane;

            var a = r.Value.Path.Uid;
            var b = env.OriginUid;
            var hashA = 23 + a;
            var hash = (int)(23 * hashA + b.GetHashCode());
            return Math.Abs(hash % InboundLanes);
        }

        // ARTERY: Need to figure out how to do a coordinated shutdown hook
        internal void ShutdownHook()
        {
            var coord = new CoordinatedShutdown(System);
            // totalTimeout will be 0 when no tasks registered, so at least 3.seconds
            var totalTimeout = coord.TotalTimeout.Max(TimeSpan.FromSeconds(3));
        }

        protected void AttachControlMessageObserver(ControlMessageSubject ctrl)
        {
            _controlSubject = ctrl;
            // ARTERY: Need to figure out how to port this
            _controlSubject.Attach(new ControlMessageObserver(
                inboundEnvelope =>
                {
                    try
                    {
                        switch (inboundEnvelope)
                        {
                            case CompressionProtocol.ICompressionMessage m:
                                switch (m)
                                {
                                    case CompressionProtocol.ActorRefCompressionAdvertisement arca:
                                        var from = arca.From;
                                        var table = arca.Table;
                                        if (arca.Table.OriginUid == LocalAddress.Uid)
                                        {
                                            _log.Debug($"Incoming ActorRef compression advertisement from [{from}], table [{table}]");
                                            var a = new Association(from.Address);
                                            // make sure uid is same for active association
                                            if (a.AssociationState.UniqueRemoteAddress.Contains(from))
                                            {
                                                //a.ChangeActorRefCompression(table)
                                            }
                                        }
                                }
                        }
                    }
                }));
        }

        public void SendControl(Address to, IControlMessage message)
        {
            throw new NotImplementedException();
        }

        public IOutboundContext Association(Address remoteAddress)
        {
            throw new NotImplementedException();
        }

        public Option<IOutboundContext> Association(long uid)
        {
            throw new NotImplementedException();
        }

        public Task<Done> CompleteHandshake(UniqueAddress peer)
        {
            throw new NotImplementedException();
        }

        public void PublishDropped(IInboundEnvelope inbound, string reason)
        {
            throw new NotImplementedException();
        }
    }
}
