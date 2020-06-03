using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Actors;
using Akka.Streams.Util;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Inbound API that is used by the stream operators.
    /// Separate trait to facilitate testing without real transport.
    /// </summary>
    internal interface IInboundContext
    {
        /// <summary>
        /// The local inbound address.
        /// </summary>
        UniqueAddress LocalAddress { get; }

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
        /// Will return `null` if the UID is unknown, i.e.
        /// handshake not completed.
        /// </summary>
        /// <param name="uid"></param>
        /// <returns></returns>
        IOutboundContext Association(long uid);

        Task<Done> CompleteHandshake(UniqueAddress peer);

        // ARTERY: ArterySettings is implemented in another PR
        // ArterySettings Settings { get; }

        void PublishDropped(IInboundEnvelope inbound, string reason);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class AssociationState
    {
        public class QuarantinedTimestamp
        {
            public long Ticks { get; }

            public QuarantinedTimestamp(long ticks)
            {
                Ticks = ticks;
            }

            public override string ToString()
            {
                return $"Quarantined {TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Ticks)} seconds ago";
            }
        }

        internal class UniqueRemoteAddressValue
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
        public AtomicCounterLong LastUsedTimestamp { get; }
        public Option<SharedKillSwitch> ControlIdleKillSwitch { get; }
        public ImmutableDictionary<long, QuarantinedTimestamp> Quarantined { get; }

        private readonly AtomicReference<UniqueRemoteAddressValue> _uniqueRemoteAddress;

        /// <summary>
        /// Full outbound address with UID for this association.
        /// Completed by the handshake.
        /// </summary>
        public Option<UniqueAddress> UniqueRemoteAddress => _uniqueRemoteAddress.Value.UniqueRemoteAddress;

        public AssociationState(
            int incarnation = 1, 
            AtomicCounterLong lastUsedTimestamp = null,
            Option<SharedKillSwitch>? controlIdleKillSwitch = null,
            ImmutableDictionary<long, QuarantinedTimestamp> quarantined = null,
            AtomicReference<UniqueRemoteAddressValue> uniqueRemoteAddress = null)
        {
            Incarnation = incarnation;
            LastUsedTimestamp = lastUsedTimestamp is null ? new AtomicCounterLong(DateTime.UtcNow.Ticks) : lastUsedTimestamp;
            ControlIdleKillSwitch = controlIdleKillSwitch is null ? Option<SharedKillSwitch>.None : controlIdleKillSwitch.Value;
            Quarantined = quarantined is null ? ImmutableDictionary<long, QuarantinedTimestamp>.Empty : quarantined;
            _uniqueRemoteAddress = uniqueRemoteAddress is null ? 
                new AtomicReference<UniqueRemoteAddressValue>(new UniqueRemoteAddressValue(Option<UniqueAddress>.None, null)) : 
                uniqueRemoteAddress;
        }

        public void CompleteUniqueRemoteAddress(UniqueAddress peer)
        {
            var current = _uniqueRemoteAddress.Value;
            if(!current.UniqueRemoteAddress.HasValue)
            {
                var newValue = new UniqueRemoteAddressValue(new Option<UniqueAddress>(peer), null);
                if (_uniqueRemoteAddress.CompareAndSet(current, newValue))
                {
                    foreach(var listener in current.Listeners)
                        listener.Invoke(peer);
                }
                else
                {
                    // case failed, retry
                    CompleteUniqueRemoteAddress(peer);
                }
            }
        }

        public void AddUniqueRemoteAddressListener(Action<UniqueAddress> callback)
        {
            var current = _uniqueRemoteAddress.Value;
            if(current.UniqueRemoteAddress.HasValue)
            {
                callback(current.UniqueRemoteAddress.Value);
            } 
            else
            {
                var newValue = new UniqueRemoteAddressValue(
                    Option<UniqueAddress>.None, current.Listeners.Insert(0, callback));
                if(!_uniqueRemoteAddress.CompareAndSet(current, newValue))
                {
                    // case failed, retry
                    AddUniqueRemoteAddressListener(callback);
                }
            }
        }

        public void RemoveUniqueRemoteAddressListener(Action<UniqueAddress> callback)
        {
            var current = _uniqueRemoteAddress.Value;
            var newValue = new UniqueRemoteAddressValue(current.UniqueRemoteAddress, current.Listeners.Remove(callback));
            if(!_uniqueRemoteAddress.CompareAndSet(current, newValue))
            {
                // case failed, retry
                RemoveUniqueRemoteAddressListener(callback);
            }
        }

        public AssociationState NewIncarnation(UniqueAddress remoteAddress)
            => new AssociationState(
                Incarnation + 1,
                new AtomicCounterLong(DateTime.UtcNow.Ticks),
                ControlIdleKillSwitch,
                Quarantined,
                new AtomicReference<UniqueRemoteAddressValue>(
                    new UniqueRemoteAddressValue(new Option<UniqueAddress>(remoteAddress), null)));

        public AssociationState NewQuarantined()
        {
            if (!UniqueRemoteAddress.HasValue) return this;

            return new AssociationState(
                Incarnation,
                new AtomicCounterLong(DateTime.UtcNow.Ticks),
                ControlIdleKillSwitch,
                Quarantined.SetItem(UniqueRemoteAddress.Value.Uid, new QuarantinedTimestamp(DateTime.UtcNow.Ticks)),
                _uniqueRemoteAddress);
        }

        public bool IsQuarantined()
        {
            if (!UniqueRemoteAddress.HasValue) return false;

            return IsQuarantined(UniqueRemoteAddress.Value.Uid);
        }

        public bool IsQuarantined(long uid) => Quarantined.ContainsKey(uid);

        public AssociationState WithControlIdleKillSwitch(Option<SharedKillSwitch> killSwitch)
            => new AssociationState(Incarnation, LastUsedTimestamp, killSwitch, Quarantined, _uniqueRemoteAddress);

        public override string ToString()
        {
            var a = UniqueRemoteAddress.HasValue ? UniqueRemoteAddress.Value.ToString() : "unknown";
            return $"AssociationState({Incarnation}, {a})";
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
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
        /// return true` if any of the streams are active (not stopped due to idle)
        /// </summary>
        /// <returns></returns>
        bool IsOrdinaryMessageStreamActive();

        // ARTERY: IControlMessageSubject is not implemented yet
        // IControlMessageSubject ControlSubject { get; }

        // ARTERY: ArterySettings is implemented in another PR
        // ArterySettings Settings { get; }
    }

    internal class FlushOnShutdown : ReceiveActor
    {
        public class TimeoutMessage
        {
            public static TimeoutMessage Instance = new TimeoutMessage();

            private TimeoutMessage() { }
        }

        public TaskCompletionSource<Done> DonePromise { get; }
        public TimeSpan Timeout { get; }
        [Obsolete]
        public IInboundContext InboundContext { get; }
        IImmutableSet<Association> Associations { get; }

        private ImmutableDictionary<UniqueAddress, int> _remaining = ImmutableDictionary<UniqueAddress, int>.Empty;
        private readonly Cancelable _timeoutTask;

        public FlushOnShutdown(
            TaskCompletionSource<Done> done, 
            TimeSpan timeout, 
            IInboundContext inboundContext, 
            IImmutableSet<Association> association)
        {
            DonePromise = done;
            Timeout = timeout;
            InboundContext = inboundContext;
            Associations = association;

            _timeoutTask = new Cancelable(Context.System.Scheduler);
            Context.System.Scheduler.ScheduleTellOnce(timeout, Self, TimeoutMessage.Instance, Self, _timeoutTask);

            Receive<ActorSystemTerminatingAck>(msg =>
            {
                // Just treat unexpected acks as systems from which zero acks are expected
                var acksRemaining = _remaining.GetOrElse(msg.From, 0);
                if(acksRemaining <= 1)
                {
                    _remaining = _remaining.Remove(msg.From);
                } 
                else
                {
                    _remaining = _remaining.SetItem(msg.From, acksRemaining - 1);
                }

                if (_remaining.Count == 0) Context.Stop(Self);
            });

            Receive<TimeoutMessage>(_ => { Context.Stop(Self); });
        }

        protected override void PreStart()
        {
            try
            {
                foreach (var association in Associations)
                {
                    var ackExpected = association.SendTerminationHint(Self);
                    var remoteAddress = association.AssociationState.UniqueRemoteAddress;
                    if (remoteAddress.HasValue)
                    {
                        _remaining = _remaining.Add(remoteAddress.Value, ackExpected);
                    }
                }
                var sum = _remaining.Aggregate(0, (accum, kvp) => accum + kvp.Value );
                if(sum == 0)
                {
                    DonePromise.NonBlockingTrySetResult(Done.Instance);
                    Context.Stop(Self);
                }
            }
            catch(Exception e)
            {
                DonePromise.NonBlockingTrySetException(e);
                throw e;
            }
        }

        protected override void PostStop()
        {
            _timeoutTask.Cancel();
            DonePromise.TrySetResult(Done.Instance);
        }
    }

    internal abstract class ArteryTransport : RemoteTransport, IInboundContext
    {
        // these vars are initialized once in the start method
        private volatile UniqueAddress _localAddress;
        private volatile UniqueAddress _bindAddress;
        private volatile ImmutableHashSet<Address> _addresses;
        private volatile IControlMessageSubject _controlSubject;

        protected volatile ActorMaterializer Materializer;
        protected volatile ActorMaterializer ControlMaterializer;

        // ARTERY: RemotingFlightRecorder isn't implemented yet
        // private readonly RemotingFlightRecorder _flightRecorder;

        // ARTERY: ArterySettings is implemented in another PR
        // private readonly ArterySettings _settings;

        // ARTERY: InboundCompressionImpl isn't implemented
        // private readonly InboundCompressionImpl _inboundCompression;

        // ARTERY: InboundCompressionAccess isn't implemented
        // private volatile Option<InboundCompressionAccess> _inboundCompressionAccess = Option<InboundCompressionAccess>.None;

        // Only access compression tables via the CompressionAccess
        // ARTERY: InboundCompressionAccess isn't implemented
        /*
        public Option<InboundCompressionAccess> InboundCompressionAccess {
            get => Volatile.Read(ref _inboundCompressionAccess);
            set => Volatile.Write(ref _inboundCompressionAccess, value);
        }
        */

        public UniqueAddress BindAddress => Volatile.Read(ref _bindAddress);
        public UniqueAddress LocalAddress => Volatile.Read(ref _localAddress);
        public override Address DefaultAddress => _localAddress?.Address;
        public override ISet<Address> Addresses => Volatile.Read(ref _addresses);
        public override Address LocalAddressForRemote(Address remote)
            => DefaultAddress;

        protected readonly SharedKillSwitch KillSwitch = KillSwitches.Shared("transportKillSwitch");

        // keyed by the streamId
        // ARTERY: InboundStreamMatValues isn't implemented
        /*
        protected readonly AtomicReference<ImmutableDictionary<int, InboundStreamMatValues<LifecycleState>>> =
            new AtomicReference<ImmutableDictionary<int, InboundStreamMatValues<LifecycleState>>>();
        */

        internal readonly AtomicBoolean HasBeenShutdown = new AtomicBoolean(false);

        // ARTERY: SharedTestState isn't implemented
        // internal readonly SharedTestState testState = new SharedTestState();

        public ArteryTransport(ExtendedActorSystem system, RemoteActorRefProvider provider) : base(system, provider)
        { 
            Log = Logging.GetLogger(system, GetType());

            // ARTERY: RemotingFlightRecorder isn't implemented yet
            // _flightRecorder = new RemotingFlightRecorder(system);
            // Log.Debug($"Using flight recorder {_flightRecorder}");

            // ARTERY: ArterySettings is implemented in another PR
            // _settings = Provider.RemoteSettings.Artery;

            /**
             * Compression tables must be created once, such that inbound lane restarts don't cause dropping of the tables.
             * However are the InboundCompressions are owned by the Decoder operator, and any call into them must be looped through the Decoder!
             * 
             * Use `inboundCompressionAccess` (provided by the materialized `Decoder`) to call into the compression infrastructure.
             */
            // ARTERY: ArterySettings is implemented in another PR
            // ARTERY: InboundCompressionImpl isn't implemented
            // ARTERY: RemotingFlightRecorder isn't implemented yet
            /*
            var _inboundCompressions = _settings.Advanced.Compression.Enabled ?
                new InboundCompressionImpl(system, this, _settings.Advanced.Compression, _flightRecorder) :
                new NoInboundCompression;
            */
        }

        public IOutboundContext Association(Address remoteAddress)
        {
            throw new NotImplementedException();
        }

        public IOutboundContext Association(long uid)
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

        public void SendControl(Address to, IControlMessage message)
        {
            throw new NotImplementedException();
        }
    }
}
