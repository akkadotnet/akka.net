using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Artery.Utils;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.IO;
using Akka.Util;

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
        /// <see cref="Some{OutboundContext}"/> if an association is found.
        /// <see cref="None{OutboundContext}"/> if the UID is unknown, i.e. handshake not completed.
        /// </returns>
        IOptionVal<IOutboundContext> Association(long uid);

        Task<Done> CompleteHandshake(UniqueAddress peer);

        void PublishDropped(IInboundEnvelope inbound, string reason);
    }

    internal class AssociationState
    {
        public static AssociationState Create()
            => new AssociationState(
                incarnation: 1,
                lastUsedTimestamp: new AtomicLong(MonotonicClock.GetNanos()),
                controlIdleKillSwitch: OptionVal.None<SharedKillSwitch>(),
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
        public IOptionVal<SharedKillSwitch> ControlIdleKillSwitch { get; }
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
            IOptionVal<SharedKillSwitch> controlIdleKillSwitch,
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

        public AssociationState WithControlIdleKillSwitch(IOptionVal<SharedKillSwitch> killSwitch)
            => new AssociationState(
                Incarnation,
                LastUsedTimestamp,
                killSwitch,
                Quarantined,
                _uniqueRemoteAddress);

        public override string ToString()
            => $"AssociationState({Incarnation}, {(!UniqueRemoteAddress.HasValue ? "unknown" : UniqueRemoteAddress.Value.ToString())})";
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

    // ARTERY: The scala code "cheats" by using the type keyword to declare TLifeCycle, making it a
    //         locally scoped generic parameter. We don't have this, so this generic parameter will bleed
    //         over to other sibling classes. Need to find a way to fix this. -- Gregorius
    internal abstract class ArteryTransport : RemoteTransport, IInboundContext
    {
        #region Static region

        public const string ProtocolName = "akka";

        // Note that the used version of the header format for outbound messages is defined in
        // `ArterySettings.Version` because that may depend on configuration settings.
        // This is the highest supported version on receiving (decoding) side.
        // ArterySettings.Version can be lower than this HighestVersion to support rolling upgrades.
        public const byte HighestVersion = 0;

        // ARTERY: Placeholder stub class to help port, will need to remove this later.
        public sealed class AeronTerminated : Exception
        {
            public AeronTerminated(Exception e) : base("", e)
            { }
        }

        public sealed class ShutdownSignal : Exception
        {
            public static readonly ShutdownSignal Instance = new ShutdownSignal();
            private ShutdownSignal() { }
            public override string StackTrace => "";
        }

        public sealed class ShuttingDown : Exception
        {
            public static readonly ShuttingDown Instance = new ShuttingDown();
            private ShuttingDown() { }
            public override string StackTrace => "";
        }

        public sealed class InboundStreamMatValues<TLifeCycle>
        {
            public TLifeCycle LifeCycle { get; }
            public Task<Done> Completed { get; }

            public InboundStreamMatValues(TLifeCycle lifeCycle, Task<Done> completed)
            {
                LifeCycle = lifeCycle;
                Completed = completed;
            }
        }

        public const int ControlStreamId = 1;
        public const int OrdinaryStreamId = 2;
        public const int LargeStreamId = 3;

        public static string StreamName(int streamId)
        {
            return streamId switch
            {
                ControlStreamId => "control",
                LargeStreamId => "large message",
                OrdinaryStreamId => "message",
                _ => throw new Exception($"Unknown stream id: {streamId}")
            };
        }

        #endregion

        private volatile UniqueAddress _localAddress;
        private volatile UniqueAddress _bindAddress;
        private volatile ImmutableHashSet<Address> _addresses;
        protected volatile IMaterializer _materializer;
        protected volatile IMaterializer _controlMaterializer;
        private volatile InboundControlJunction.IControlMessageSubject _controlSubject;
        private volatile MessageDispatcher _messageDispatcher;

        private readonly ILoggingAdapter _log;
        public readonly IRemotingFlightRecorder FlightRecorder;

        /*
         * Compression tables must be created once, such that inbound lane restarts don't cause dropping of the tables.
         * However are the InboundCompressions are owned by the Decoder operator, and any call into them must be looped through the Decoder!
         *
         * Use `inboundCompressionAccess` (provided by the materialized `Decoder`) to call into the compression infrastructure.
         */
        protected readonly IInboundCompressions InboundCompressions;

        private volatile IOptionVal<Decoder.IInboundCompressionAccess> _inboundCompressionAccess = OptionVal.None<Decoder.IInboundCompressionAccess>();

        // Only access compression tables via the CompressionAccess
#pragma warning disable 420
        public IOptionVal<Decoder.IInboundCompressionAccess> InboundCompressionAccess => Volatile.Read(ref _inboundCompressionAccess);
        protected void SetInboundCompressionAccess(Decoder.IInboundCompressionAccess a)
            => Volatile.Write(ref _inboundCompressionAccess, OptionVal.Apply(a));

        public UniqueAddress BindAddress => Volatile.Read(ref _bindAddress);
        public UniqueAddress LocalAddress => Volatile.Read(ref _localAddress);
        public override Address DefaultAddress => LocalAddress?.Address;
        public override ISet<Address> Addresses => Volatile.Read(ref _addresses);
        public override Address LocalAddressForRemote(Address remote) => DefaultAddress;
#pragma warning restore 420

        protected readonly SharedKillSwitch KillSwitch;

        protected readonly AtomicReference<ImmutableDictionary<int, InboundStreamMatValues<TLifeCycle>>> StreamMatValues;
        private readonly AtomicBoolean _hasBeenShutdown;

        private readonly SharedTestState _testState;
        protected readonly int InboundLanes;

        public readonly bool LargeMessageChannelEnabled;
        private readonly WildcardIndex<NotUsed> _priorityMessageDestinations;

        private readonly RestartCounter _restartCounter;

        protected readonly EnvelopeBufferPool LargeEnvelopeBufferPool;

        private readonly ObjectPool<ReusableInboundEnvelope> _inboundEnvelopePool;
        // The outboundEnvelopePool is shared among all outbound associations
        private readonly ObjectPool<ReusableOutboundEnvelope> _outboundEnvelopePool;

        private readonly AssociationRegistry _associationRegistry;

        public ImmutableHashSet<Address> RemoteAddresses
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public ArterySettings Settings => Provider.RemoteSettings.Artery;

        /// <summary>
        /// Exposed for orderly shutdown purposes, can not be trusted except for during shutdown as streams may restart.
        /// Will complete successfully even if one of the stream completion futures failed
        /// </summary>
        private Task<Done> StreamsCompleted {
            get
            {
                throw new NotImplementedException();
            }
        }

        public bool IsShutdown => _hasBeenShutdown.Value;

        // ARTERY: Need to figure out how to do a coordinated shutdown hook
        private Action ShutdownHook
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public readonly Sink<IInboundEnvelope, Task> MessageDispatcherSink;

        public readonly Flow<IInboundEnvelope, IInboundEnvelope, NotUsed> FlushReplier;

        protected ArteryTransport(ExtendedActorSystem system, RemoteActorRefProvider provider) : base(system, provider)
        {
            // TODO: Scala logging is way more advanced than ours, this logger supposed to have marker adapter added to it
            //_log = Logging.WithMarker(system, GetType());
            _log = Logging.GetLogger(system, GetType());
            FlightRecorder = RemotingFlightRecorderExtension.Get(system);
            _log.Debug($"Using flight recorder {FlightRecorder}");

            InboundCompressions = Settings.Advanced.Compression.Enabled
                ? new InboundCompressionsImpl(System, this, Settings.Advanced.Compression, FlightRecorder)
                : (IInboundCompressions) NoInboundCompressions.Instance;

            KillSwitch = KillSwitches.Shared("transportKillSwitch");

            // keyed by the streamId
            StreamMatValues =
                new AtomicReference<ImmutableDictionary<int, InboundStreamMatValues<TLifeCycle>>>(
                    ImmutableDictionary<int, InboundStreamMatValues<TLifeCycle>>.Empty);
            _hasBeenShutdown = new AtomicBoolean(false);

            _testState = new SharedTestState();

            InboundLanes = Settings.Advanced.InboundLanes;

            LargeMessageChannelEnabled =
                !Settings.LargeMessageDestinations.WildcardTree.IsEmpty ||
                !Settings.LargeMessageDestinations.DoubleWildcardTree.IsEmpty;

            _priorityMessageDestinations = new WildcardIndex<NotUsed>()
                // These destinations are not defined in configuration because it should not
                // be possible to abuse the control channel
                .Insert(new[] { "system", "remote-watcher" }, NotUsed.Instance)
                // these belongs to cluster and should come from there
                .Insert(new[] { "system", "cluster", "core", "daemon", "heartbeatSender" }, NotUsed.Instance)
                .Insert(new[] { "system", "cluster", "core", "daemon", "crossDcHeartbeatSender" }, NotUsed.Instance)
                .Insert(new[] { "system", "cluster", "heartbeatReceiver" }, NotUsed.Instance);

            _restartCounter = new RestartCounter(Settings.Advanced.InboundMaxRestarts, Settings.Advanced.InboundRestartTimeout);

            LargeEnvelopeBufferPool = LargeMessageChannelEnabled
                ? new EnvelopeBufferPool(Settings.Advanced.MaximumLargeFrameSize, Settings.Advanced.LargeBufferPoolSize)
                : new EnvelopeBufferPool(0, 2); // not used

            _inboundEnvelopePool = ReusableInboundEnvelope.CreateObjectPool(16);
            _outboundEnvelopePool = ReusableOutboundEnvelope.CreateObjectPool(
                Settings.Advanced.OutboundLargeMessageQueueSize * Settings.Advanced.OutboundLanes * 3);

            // ARTERY: AssociationRegistry is different than the one in TestTransport
            _associationRegistry = new AssociationRegistry(remoteAddress => new Association(
                this,
                _materializer,
                _controlMaterializer,
                remoteAddress,
                _controlSubject,
                Settings.LargeMessageDestinations,
                _priorityMessageDestinations,
                _outboundEnvelopePool));

            MessageDispatcherSink = Sink.ForEach<IInboundEnvelope>(m =>
            {
                _messageDispatcher.Dispatch(m);
                if (m is ReusableInboundEnvelope r)
                {
                    _inboundEnvelopePool.Release(r);
                }
            });

            // Checks for termination hint messages and sends an ACK for those (not processing them further)
            // Purpose of this stage is flushing, the sender can wait for the ACKs up to try flushing
            // pending messages.
            FlushReplier = Flow.Create<IInboundEnvelope>().Filter(envelope =>
            {
                if (!(envelope.Message is OutputStreamSourceStage.Flush))
                    return true;

                if (envelope.Sender is Some<IActorRef> snd)
                {
                    snd.Get.Tell(FlushAck.Instance, ActorRefs.NoSender);
                }
                else
                {
                    _log.Error("Expected sender for Flush message from [{0}]", envelope.Association);
                }

                return false;
            });
        }

        public override void Start()
        {
            // ARTERY: This is scala code to hook a callback to JVM shutdown event.
            //         Need to convert this to dotnet somehow.
            /*
            if (System.Settings.ClrShutdownHooks)
                Runtime.GetRuntime().AddShutdownHook(ShutdownHook);
            */

            StartTransport();
            FlightRecorder.TransportStarted();

            var systemMaterializer = SystemMaterializer.Get(System);
            _materializer =
                systemMaterializer.CreateAdditionalLegacySystemMaterializer(
                    "remote",
                    Settings.Advanced.MaterializerSettings);
            _controlMaterializer = systemMaterializer.CreateAdditionalLegacySystemMaterializer(
                "remoteControl",
                Settings.Advanced.ControlStreamMaterializerSettings);

            _messageDispatcher = new MessageDispatcher(System, Provider);
            FlightRecorder.TransportMaterializerStarted();

            var (port, boundPort) = BindInboundStreams();

            _localAddress = new UniqueAddress(
                new Address(ProtocolName, System.Name, Settings.Canonical.Hostname, port),
                AddressUidExtension.Uid(System));

            _addresses = ImmutableHashSet<Address>.Empty.Add(_localAddress.Address);

            _bindAddress = new UniqueAddress(
                new Address(ProtocolName, System.Name, Settings.Bind.Hostname, boundPort), 
                AddressUidExtension.Uid(System));

            FlightRecorder.TransportUniqueAddressSet(_localAddress);

            RunInboundStreams(port, boundPort);

            FlightRecorder.TransportStartupFinished();

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
            throw new NotImplementedException();
        }

        /// <summary>
        /// Select inbound lane based on destination to preserve message order,
        /// Also include the uid of the sending system in the hash to spread
        /// "hot" destinations, e.g. ActorSelection anchor.
        /// </summary>
        protected int InboundLanePartitioner(IInboundEnvelope env) 
        {
            switch (env.Recipient)
            {
                case Some<IInternalActorRef> r:
                    var a = r.Get.Path.Uid;
                    var b = env.OriginUid;
                    var hashA = 23 + a;
                    var hash = (int)(23 * hashA + b.GetHashCode());
                    return Math.Abs(hash % InboundLanes);
                default:
                    return env.Lane;
            }
        }

        private class ControlMessageObserver : InboundControlJunction.IControlMessageObserver
        {
            private ArteryTransport _parent;

            public ControlMessageObserver(ArteryTransport parent)
            {
                _parent = parent;
            }

            public void Notify(IInboundEnvelope inboundEnvelope)
            {
                try
                {
                    throw new NotImplementedException();
                }
                catch (ShuttingDown)
                {
                    // silence it
                }
            }

            public void ControlSubjectCompleted(Try<Done> signal) { }
        }

        protected void AttachControlMessageObserver(InboundControlJunction.IControlMessageSubject ctrl)
        {
            _controlSubject = ctrl;
            _controlSubject.Attach(new ControlMessageObserver(this));
        }

        protected void AttachInboundStreamRestart(string streamName, Task<Done> streamCompleted, Action restart)
        {
            throw new NotImplementedException();
        }

        public override Task Shutdown()
        {
            throw new NotImplementedException();
        }

        private Task<Done> InternalShutdown()
        {
            throw new NotImplementedException();
        }

        protected abstract Task<Done> ShutdownTransport();

        public override Task<bool> ManagementCommand(object cmd)
        {
            throw new NotImplementedException();
        }

        // IInboundContext
        public void SendControl(Address to, IControlMessage message)
        {
            throw new NotImplementedException();
        }

        public override void Send(object message, IActorRef sender, RemoteActorRef recipient)
        {
            throw new NotImplementedException();
        }

        public IOutboundContext Association(Address remoteAddress)
        {
            throw new NotImplementedException();
        }

        public IOptionVal<IOutboundContext> Association(long uid)
        {
            throw new NotImplementedException();
        }

        public Task<Done> CompleteHandshake(UniqueAddress peer)
        {
            throw new NotImplementedException();
        }

        public void Quarantine(Address remoteAddress, Option<long> uid, string reason)
        {
            throw new NotImplementedException();
        }

        public void Quarantine(Address remoteAddress, Option<long> uid, string reason, bool harmless)
        {
            throw new NotImplementedException();
        }

        public Sink<IOutboundEnvelope, Task<Done>> OutboundLarge(IOutboundContext outboundContext)
        {
            throw new NotImplementedException();
        }

        public Sink<IOutboundEnvelope, (Encoder.IOutboundCompressionAccess, Task<Done>)> Outbound(IOutboundContext outboundContext)
        {
            throw new NotImplementedException();
        }

        private Sink<IOutboundEnvelope, (Encoder.IOutboundCompressionAccess, Task<Done>)>
            CreateOutboundSink(int streamId, IOutboundContext outboundContext, EnvelopeBufferPool bufferPool)
        {
            throw new NotImplementedException();
        }

        public Sink<EnvelopeBuffer, Task<Done>> OutboundTransportSink(IOutboundContext outboundContext)
        {
            throw new NotImplementedException();
        }

        protected abstract Sink<EnvelopeBuffer, Task<Done>> OutboundTransportSink(
            IOutboundContext outboundContext,
            int streamId,
            EnvelopeBufferPool bufferPool);

        public Flow<IOutboundEnvelope, EnvelopeBuffer, Encoder.IOutboundCompressionAccess>
            OutboundLane(IOutboundContext outboundContext)
        {
            throw new NotImplementedException();
        }

        private Flow<IOutboundEnvelope, EnvelopeBuffer, Encoder.IOutboundCompressionAccess>
            OutboundLane(IOutboundContext outboundContext, EnvelopeBufferPool bufferPool, int streamId)
        {
            throw new NotImplementedException();
        }

        public Sink<IOutboundEnvelope, (OutboundControlJunction.IOutboundControlIngress, Task<Done>)>
            OutboundControl(IOutboundContext outboundContext)
        {
            throw new NotImplementedException();
        }

        public Flow<IOutboundEnvelope, EnvelopeBuffer, Encoder.IOutboundCompressionAccess> 
            CreateEncoder(EnvelopeBufferPool pool, int streamId)
        {
            throw new NotImplementedException();
        }

        public Flow<EnvelopeBuffer, IInboundEnvelope, Decoder.IInboundCompressionAccess>
            CreateDecoder(ArterySettings settings, IInboundCompressions compressions)
        {
            throw new NotImplementedException();
        }

        public Flow<EnvelopeBuffer, IInboundEnvelope, Decoder.IInboundCompressionAccess>
            CreateDeserializer(EnvelopeBufferPool bufferPool)
        {
            throw new NotImplementedException();
        }

        // Checks for termination hint messages and sends an ACK for those (not processing them further)
        // Purpose of this stage is flushing, the sender can wait for the ACKs up to try flushing
        // pending messages.
        public Flow<IInboundEnvelope, IInboundEnvelope, NotUsed> TerminationHintReplier(bool inControlStream)
        {
            throw new NotImplementedException();
        }

        public Sink<IInboundEnvelope, Task<Done>> InboundSink(EnvelopeBufferPool bufferPool)
        {
            throw new NotImplementedException();
        }

        public Flow<EnvelopeBuffer, IInboundEnvelope, Decoder.IInboundCompressionAccess>
            InboundFlow(ArterySettings settings, IInboundCompressions compressions)
        {
            throw new NotImplementedException();
        }

        public Flow<EnvelopeBuffer, IInboundEnvelope, object> InboundLargeFlow(ArterySettings settings)
        {
            throw new NotImplementedException();
        }

        public Sink<IInboundEnvelope, (InboundControlJunction.IControlMessageSubject, Task<Done>)>
            InboundControlSink
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public Flow<IOutboundEnvelope, IOutboundEnvelope, NotUsed> OutboundTestFlow(IOutboundContext outboundContext)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// INTERNAL API: for testing only.
        /// </summary>
        /// <param name="actorRef"></param>
        /// <param name="manifest"></param>
        private void TriggerCompressionAdvertisement(bool actorRef, bool manifest)
        {
            throw new NotImplementedException();
        }

        public void PublishDropped(IInboundEnvelope inbound, string reason)
        {
            throw new NotImplementedException();
        }
    }
}
