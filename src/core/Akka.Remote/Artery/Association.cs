using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Remote.Artery.Compress;
using Akka.Remote.Artery.Utils;
using Akka.Remote.Artery.Utils.Concurrent;
using Akka.Streams;
using Akka.Streams.Implementation.Fusing;
using Akka.Util;

namespace Akka.Remote.Artery
{
    internal class Association : IOutboundContext
    {
        #region Static region

        public interface IQueueWrapper : SendQueue.IProducerApi<IOutboundEnvelope>
        {
            IQueue<IOutboundEnvelope> Queue { get; }
        }

        public sealed class QueueWrapperImpl : IQueueWrapper
        {
            public IQueue<IOutboundEnvelope> Queue { get; }

            public QueueWrapperImpl(IQueue<IOutboundEnvelope> queue)
            {
                Queue = queue;
            }

            public bool Offer(IOutboundEnvelope message)
                => Queue.Offer(message);

            public bool IsEnabled => true;
        }

        public sealed class DisabledQueueWrapper : IQueueWrapper
        {
            public static DisabledQueueWrapper Instance = new DisabledQueueWrapper();

            private DisabledQueueWrapper() {}

            public IQueue<IOutboundEnvelope> Queue => throw new InvalidOperationException("The Queue is disabled");

            public bool Offer(IOutboundEnvelope message)
                => throw new InvalidOperationException("The method Offer() is illegal on a disabled queue");

            public bool IsEnabled => false;
        }

        public sealed class RemovedQueueWrapper : IQueueWrapper
        {
            public static RemovedQueueWrapper Instance = new RemovedQueueWrapper();

            private RemovedQueueWrapper() {}

            public IQueue<IOutboundEnvelope> Queue => throw new InvalidOperationException("The Queue is removed");

            public bool Offer(IOutboundEnvelope message) => false;

            public bool IsEnabled => false;
        }

        public sealed class LazyQueueWrapper : IQueueWrapper
        {
            private readonly Action _materialize;
            private readonly AtomicBoolean _onlyOnce = new AtomicBoolean();

            public IQueue<IOutboundEnvelope> Queue { get; }

            public LazyQueueWrapper(IQueue<IOutboundEnvelope> queue, Action materialize)
            {
                Queue = queue;
                _materialize = materialize;
            }

            public void RunMaterialize()
            {
                if (_onlyOnce.CompareAndSet(false, true))
                    _materialize();
            }

            public bool Offer(IOutboundEnvelope message)
            {
                RunMaterialize();
                return Queue.Offer(message);
            }

            public bool IsEnabled => true;
        }

        public const int ControlQueueIndex = 0;
        public const int LargeQueueIndex = 1;
        public const int OrdinaryQueueIndex = 2;

        public interface IStopSignal { }
        public sealed class OutboundStreamStopIdleSignal : Exception, IStopSignal
        {
            public static OutboundStreamStopIdleSignal Instance = new OutboundStreamStopIdleSignal();
            private OutboundStreamStopIdleSignal() { }
            public override string StackTrace => "";
        }
        public sealed class OutboundStreamStopQuarantinedSignal : Exception, IStopSignal
        {
            public static OutboundStreamStopQuarantinedSignal Instance = new OutboundStreamStopQuarantinedSignal();
            private OutboundStreamStopQuarantinedSignal() { }
            public override string StackTrace => "";
        }

        public sealed class OutboundStreamMatValues
        {
            public IOptionVal<SharedKillSwitch> StreamKillSwitch { get; }
            public Task<Done> Completed { get; }
            public IOptionVal<IStopSignal> Stopping { get; }

            public OutboundStreamMatValues(
                IOptionVal<SharedKillSwitch> streamKillSwitch,
                Task<Done> completed,
                IOptionVal<IStopSignal> stopping)
            {
                StreamKillSwitch = streamKillSwitch;
                Completed = completed;
                Stopping = stopping;
            }
        }

        #endregion

        public readonly ArteryTransport Transport;
        public readonly IMaterializer Materializer;
        public readonly IMaterializer ControlMaterializer;
        public InboundControlJunction.IControlMessageSubject ControlSubject{ get; }
        public readonly WildcardIndex<NotUsed> LargeMessageDestinations;
        public readonly WildcardIndex<NotUsed> PriorityMessageDestinations;
        public readonly ObjectPool<ReusableOutboundEnvelope> OutboundEnvelopePool;

        private readonly ILoggingAdapter _log;
        private IRemotingFlightRecorder FlightRecorder => Transport.FlightRecorder;
        public ArterySettings Settings => Transport.Settings;
        public ArterySettings.AdvancedSettings AdvancedSettings => Transport.Settings.Advanced;

        private readonly bool _deathWatchNotificationFlushEnabled;
        private readonly RestartCounter _restartCounter;

        private readonly int _outboundLanes;
        private readonly int _controlQueueSize;
        private readonly int _queueSize;
        private readonly int _largeQueueSize;

        private readonly SendQueue.IProducerApi<IOutboundEnvelope>[] _queues;

        private volatile bool _queuesVisibility = false;

        private SendQueue.IProducerApi<IOutboundEnvelope> ControlQueue => _queues[ControlQueueIndex];

        private volatile IOptionVal<OutboundControlJunction.IOutboundControlIngress> _outboundControlIngress = OptionVal.None<OutboundControlJunction.IOutboundControlIngress>();
        private volatile CountDownLatch _materializing = new CountDownLatch(1);
        private volatile List<Encoder.IOutboundCompressionAccess> _outboundCompressionAccess = new List<Encoder.IOutboundCompressionAccess>();

        // keyed by stream queue index
        private readonly AtomicReference<Dictionary<int, OutboundStreamMatValues>> _streamMatValues = new AtomicReference<Dictionary<int, OutboundStreamMatValues>>();

        private readonly AtomicReference<IOptionVal<Cancelable>> _idleTimer = new AtomicReference<IOptionVal<Cancelable>>(OptionVal.None<Cancelable>());
        private readonly AtomicReference<IOptionVal<Cancelable>> _stopQuarantinedTimer = new AtomicReference<IOptionVal<Cancelable>>(OptionVal.None<Cancelable>());

        private IActorRef DeadLetters => Transport.System.DeadLetters;

        public OutboundControlJunction.IOutboundControlIngress OutboundControlIngress
        {
            get
            {
                switch (_outboundControlIngress)
                {
                    case Some<OutboundControlJunction.IOutboundControlIngress> o:
                        return o.Get;
                    default:
                        if (ControlQueue is LazyQueueWrapper w)
                            w.RunMaterialize();

                        // the outboundControlIngress may be accessed before the stream is materialized
                        // using CountDownLatch to make sure that materialization is completed
                        _materializing.Await().Wait(TimeSpan.FromSeconds(10));
                        switch (_outboundControlIngress)
                        {
                            case Some<OutboundControlJunction.IOutboundControlIngress> o:
                                return o.Get;
                            default:
                                if(Transport.IsShutdown || IsRemovedAfterQuarantined())
                                    throw ArteryTransport.ShuttingDown.Instance;
                                throw new IllegalStateException(
                                    $"outboundControlIngress for [{RemoteAddress}] not initialized yet");
                        }
                }
            }
        }

        public UniqueAddress LocalAddress => Transport.LocalAddress;

        /// <summary>
        /// Holds reference to shared state of Association - *access only via helper methods*
        /// </summary>
        private readonly AtomicReference<AssociationState> _sharedStateDoNotCallMeDirectly = AssociationState.Create();

        /// <summary>
        /// Helper method for access to underlying state via Unsafe
        /// </summary>
        /// <param name="oldState">Previous state</param>
        /// <param name="newState">Next state on transition</param>
        /// <returns>Whether the previous state matched correctly</returns>
        public bool SwapState(AssociationState oldState, AssociationState newState)
        {
            return _sharedStateDoNotCallMeDirectly.CompareAndSet(oldState, newState);
        }

        public AssociationState AssociationState => _sharedStateDoNotCallMeDirectly.Value;

        public void SetControlIdleKillSwitch(IOptionVal<SharedKillSwitch> killSwitch)
        {
            var current = AssociationState;
            SwapState(current, current.WithControlIdleKillSwitch(killSwitch));
        }

        public Address RemoteAddress{ get; }

        public Association(
            ArteryTransport transport, 
            IMaterializer materializer, 
            IMaterializer controlMaterializer, 
            Address remoteAddress, 
            InboundControlJunction.IControlMessageSubject controlSubject, 
            WildcardIndex<NotUsed> largeMessageDestinations, 
            WildcardIndex<NotUsed> priorityMessageDestinations, 
            ObjectPool<ReusableOutboundEnvelope> outboundEnvelopePool)
        {
            remoteAddress.Port.Requiring(i => i.HasValue, $"{nameof(remoteAddress.Port)} must have a value");

            Transport = transport;
            Materializer = materializer;
            ControlMaterializer = controlMaterializer;
            RemoteAddress = remoteAddress;
            ControlSubject = controlSubject;
            LargeMessageDestinations = largeMessageDestinations;
            PriorityMessageDestinations = priorityMessageDestinations;
            OutboundEnvelopePool = outboundEnvelopePool;

            _log = Logging.WithMarker(transport.System, GetType());
            _deathWatchNotificationFlushEnabled =
                AdvancedSettings.DeathWatchNotificationFlushTimeout > TimeSpan.Zero &&
                transport.Provider.Settings.HasCluster;

            _restartCounter = new RestartCounter(AdvancedSettings.OutboundMaxRestarts, AdvancedSettings.OutboundRestartTimeout);

            _outboundLanes = AdvancedSettings.OutboundLanes;
            _controlQueueSize = AdvancedSettings.OutboundControlQueueSize;
            _queueSize = AdvancedSettings.OutboundMessageQueueSize;
            _largeQueueSize = AdvancedSettings.OutboundLargeMessageQueueSize;

            _queues = new SendQueue.IProducerApi<IOutboundEnvelope>[2 + _outboundLanes];
            _queues[ControlQueueIndex] = new QueueWrapperImpl(CreateQueue(_controlQueueSize, ControlQueueIndex));
            _queues[LargeQueueIndex] = transport.LargeMessageChannelEnabled
                ? new QueueWrapperImpl(CreateQueue(_largeQueueSize, LargeQueueIndex))
                : (IQueueWrapper)DisabledQueueWrapper.Instance;

            for (var i = 0; i < _outboundLanes; ++i)
            {
                _queues[OrdinaryQueueIndex + i] = new QueueWrapperImpl(
                    CreateQueue(_queueSize, OrdinaryQueueIndex + 1));
            }
        }

        // We start with the raw wrapped queue and then it is replaced with the materialized value of
        // the `SendQueue` after materialization. Using same underlying queue. This makes it possible to
        // start sending (enqueuing) to the Association immediate after construction.
        private IQueue<IOutboundEnvelope> CreateQueue(int capacity, int queueIndex)
        {
            var linked = queueIndex == ControlQueueIndex || queueIndex == LargeQueueIndex;
            return linked 
                ? new LinkedBlockingQueue<IOutboundEnvelope>(capacity) 
                : (IQueue<IOutboundEnvelope>) new ManyToOneConcurrentArrayQueue<IOutboundEnvelope>(capacity);
        }

        public Task<Done> ChangeActorRefCompression(CompressionTable<IActorRef> table)
            => UpdateOutboundCompression(c => c.ChangeActorRefCompression(table));

        public Task<Done> ChangeClassManifestCompression(CompressionTable<string> table)
            => UpdateOutboundCompression(c => c.ChangeClassManifestCompression(table));

        public Task<Done> ClearOutboundCompression()
            => UpdateOutboundCompression(c => c.ClearCompression());

        private Task<Done> UpdateOutboundCompression(Func<Encoder.IOutboundCompressionAccess, Task<Done>> action)
        {
            //var ec = Transport.System.Dispatchers.InternalDispatcher;
            var c = Volatile.Read(ref _outboundCompressionAccess);
            var tasks = c.Select(action).ToList();

            return tasks.Count switch
            {
                0 => Task.FromResult(Done.Instance),
                1 => action(c[0]),
                // ARTERY TODO: RECHECK CODE: I'm not sure this is a proper 1 to 1 conversion of the original scala code
                _ => Task.WhenAll(tasks).ContinueWith(t => Task.FromResult(Done.Instance)).Unwrap()
            };
        }

        public Task<Done> CompleteHandshake(UniqueAddress peer)
        {
            throw new NotImplementedException();
        }

        // IOutboundContext
        public void SendControl(IControlMessage message)
        {
            throw new NotImplementedException();
        }

        public void Send(object message, IOptionVal<IActorRef> sender, IOptionVal<RemoteActorRef> recipient)
        {
            throw new NotImplementedException();
        }

        public int SelectQueue(IOptionVal<RemoteActorRef> recipient)
        {
            throw new NotImplementedException();
        }

        public bool IsOrdinaryMessageStreamActive()
        {
            throw new NotImplementedException();
        }

        public bool IsStreamActive(int queueIndex)
        {
            throw new NotImplementedException();
        }

        public int SendTerminationHint(IActorRef replyTo)
        {
            throw new NotImplementedException();
        }

        public int SendFlush(IActorRef replyTo, bool excludeControlQueue)
        {
            throw new NotImplementedException();
        }

        public int SendToAllQueues(IControlMessage msg, IActorRef replyTo, bool excludeControlQueue)
        {
            throw new NotImplementedException();
        }

        // IOutboundContext
        public void Quarantine(string reason)
        {
            throw new NotImplementedException();
        }

        public void Quarantine(string reason, IOptionVal<long> uid, bool harmless)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// After calling this no messages can be sent with this Association instance
        /// </summary>
        public void RemovedAfterQuarantined()
        {
            throw new NotImplementedException();
        }

        public bool IsRemovedAfterQuarantined()
            => _queues[ControlQueueIndex].Equals(RemovedQueueWrapper.Instance);

        private void CancelStopQuarantinedTimer()
        {
            throw new NotImplementedException();
        }

        private void SetupStopQuarantinedTimer()
        {
            throw new NotImplementedException();
        }

        private void AbortQuarantined()
        {
            throw new NotImplementedException();
        }

        private void CancelIdleTimer()
        {
            throw new NotImplementedException();
        }

        private void SetupIdleTimer()
        {
            throw new NotImplementedException();
        }

        private void CancelAllTimers()
        {
            CancelIdleTimer();
            CancelStopQuarantinedTimer();
        }

        /// <summary>
        /// Called once after construction when the `Association` instance
        /// wins the CAS in the `AssociationRegistry`. It will materialize
        /// the streams. It is possible to sending (enqueuing) to the association
        /// before this method is called.
        /// </summary>
        /// <exception cref="ArteryTransport.ShuttingDown">if called while the transport is shutting down</exception>
        public void Associate()
        {
            throw new NotImplementedException();
        }

        private void RunOutboundStreams()
        {
            throw new NotImplementedException();
        }

        private void RunOutboundControlStream()
        {
            throw new NotImplementedException();
        }

        private IQueueWrapper GetOrCreateQueueWrapper(int queueIndex, int capacity)
        {
            throw new NotImplementedException();
        }

        private void RunOutboundOrdinaryMessageStream()
        {
            throw new NotImplementedException();
        }

        private void RunOutboundLargeMessageStream()
        {
            throw new NotImplementedException();
        }

        private void AttachOutboundStreamRestart(
            string streamName, 
            int queueIndex, 
            int queueCapacity,
            Task<Done> streamCompleted, 
            Action restart)
        {
            throw new NotImplementedException();
        }

        private void UpdateStreamMatValues(int streamId, SharedKillSwitch streamKillSwitch, Task<Done> completed)
        {
            throw new NotImplementedException();
        }

        private void UpdateStreamMatValues(int streamId, OutboundStreamMatValues values)
        {
            throw new NotImplementedException();
        }

        private void SetStopReason(int streamId, IStopSignal stopSignal)
        {
            throw new NotImplementedException();
        }

        private IOptionVal<IStopSignal> GetStopReason(int streamId)
        {
            throw new NotImplementedException();
        }

        // after it has been used we remove the kill switch to cleanup some memory,
        // not a "leak" but a KillSwitch is rather heavy
        private void ClearStreamKillSwitch(int streamIs, SharedKillSwitch old)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Exposed for orderly shutdown purposes, can not be trusted except for during shutdown as streams may restart.
        /// Will complete successfully even if one of the stream completion futures failed
        /// </summary>
        public Task<Done> StreamsCompleted
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override string ToString()
            => $"Association({LocalAddress} -> {RemoteAddress} with {AssociationState})";
    }

    internal class AssociationRegistry
    {
        private readonly Func<Address, Association> _createAssociation;
        private readonly AtomicReference<Dictionary<Address, Association>> _associationByAddress;
        private readonly AtomicReference<ImmutableDictionary<long, Association>> _associationByUid;

        public ImmutableHashSet<Association> AllAssociations =>
            _associationByAddress.Value.Values.ToImmutableHashSet();

        public AssociationRegistry(Func<Address, Association> createAssociation)
        {
            _createAssociation = createAssociation;
            _associationByAddress =
                new AtomicReference<Dictionary<Address, Association>>(
                    new Dictionary<Address, Association>());
            _associationByUid =
                new AtomicReference<ImmutableDictionary<long, Association>>(
                    ImmutableDictionary<long, Association>.Empty);
        }

        public Association Association(Address remoteAddress)
        {
            throw new NotImplementedException();
        }

        public IOptionVal<Association> Association(long uid)
        {
            throw new NotImplementedException();
        }

        public Association SetUid(UniqueAddress peer)
        {
            throw new NotImplementedException();
        }

        public void RemoveUnusedQuarantined(TimeSpan after)
        {
            throw new NotImplementedException();
        }

        private void RemoveUnusedQuarantinedByAddress(TimeSpan after)
        {
            throw new NotImplementedException();
        }

        private void RemoveUnusedQuarantinedByUid(TimeSpan after)
        {
            throw new NotImplementedException();
        }
    }
}
