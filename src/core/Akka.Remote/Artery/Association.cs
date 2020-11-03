using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Artery.Compress;
using Akka.Remote.Artery.Settings;
using Akka.Remote.Artery.Utils;
using Akka.Streams;
using Akka.Streams.Implementation.Fusing;
using Akka.Util;

namespace Akka.Remote.Artery
{
    internal class Association : AbstractAssociation, IOutboundContext
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

            public bool Offer(IOutboundEnvelope message)
            {
                if (_onlyOnce.CompareAndSet(false, true))
                    _materialize();
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
            public OptionVal<SharedKillSwitch> StreamKillSwitch { get; }
            public Task<Done> Completed { get; }
            public OptionVal<IStopSignal> Stopping { get; }

            public OutboundStreamMatValues(
                OptionVal<SharedKillSwitch> streamKillSwitch,
                Task<Done> completed,
                OptionVal<IStopSignal> stopping)
            {
                StreamKillSwitch = streamKillSwitch;
                Completed = completed;
                Stopping = stopping;
            }
        }
        #endregion

        private readonly ILoggingAdapter _log;
        private readonly IRemotingFlightRecorder _flightRecorder;
        private readonly ArterySettings _settings;
        private readonly ArterySettings.AdvancedSettings _advancedSettings;
        private readonly bool _deathWatchNotificationFlushEnabled;
        private readonly RestartCounter _restartCounter;
        private readonly int _outboundLanes;
        private readonly int _controlQueueSize;
        private readonly int _queueSize;
        private readonly int _largeQueueSize;
        private readonly SendQueue.IProducerApi<IOutboundEnvelope>[] _queues;
        private readonly SendQueue.IProducerApi<IOutboundEnvelope> _controlQueue;

        private volatile bool _queuesVisibility = false;
        private volatile OptionVal<OutboundControlJunction.IOutboundControlIngress> _outboundControlIngress = OptionVal<OutboundControlJunction.IOutboundControlIngress>.None;
        private volatile CountDownLatch _materializing = new CountDownLatch(1);
        private volatile List<OutboundCompressionAccess> _outboundCompressionAccess = new List<OutboundCompressionAccess>();

        // keyed by stream queue index
        private readonly AtomicReference<Dictionary<int, OutboundStreamMatValues>> _streamMatValues = new AtomicReference<Dictionary<int, OutboundStreamMatValues>>();

        private readonly AtomicReference<OptionVal<Cancelable>> _idleTimer = new AtomicReference<OptionVal<Cancelable>>(OptionVal<Cancelable>.None);
        private readonly AtomicReference<OptionVal<Cancelable>> _stopQuarantinedTimer = new AtomicReference<OptionVal<Cancelable>>(OptionVal<Cancelable>.None);

        public ArteryTransport Transport { get; }
        public Materializer Materializer{ get; }
        public Materializer ControlMaterializer{ get; }
        public Address RemoteAddress{ get; }
        public ControlMessageSubject ControlSubject{ get; }
        public WildcardIndex<NotUsed> LargeMessageDestinations{ get; }
        public WildcardIndex<NotUsed> PriorityMessageDestinations{ get; }
        public ObjectPool<ReusableOutboundEnvelope> OutboundEnvelopePool{ get; }

        public Association(
            ArteryTransport transport, 
            Materializer materializer, 
            Materializer controlMaterializer, 
            Address remoteAddress, 
            ControlMessageSubject controlSubject, 
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

            _log = Logging.WithMarker(transport.System, this.GetType());
            _flightRecorder = transport.FlightRecorder;
            _settings = transport.Settings;
            _advancedSettings = transport.Settings.Advanced;
            _deathWatchNotificationFlushEnabled =
                _advancedSettings.DeathWatchNotificationFlushTimeout > TimeSpan.Zero &&
                transport.Provider.Settings.HasCluster;

            _restartCounter = new RestartCounter(_advancedSettings.OutboundMaxRestarts, _advancedSettings.OutboundRestartTimeout);

            _outboundLanes = _advancedSettings.OutboundLanes;
            _controlQueueSize = _advancedSettings.OutboundControlQueueSize;
            _queueSize = _advancedSettings.OutboundMessageQueueSize;
            _largeQueueSize = _advancedSettings.OutboundLargeMessageQueueSize;

            _queues = new SendQueue.IProducerApi<IOutboundEnvelope>[2 + _outboundLanes];
            _queues[ControlQueueIndex] = new QueueWrapperImpl<IQueue<IOutboundEnvelope>, IOutboundEnvelope>(CreateQueue(_controlQueueSize, ControlQueueIndex));
            _queues[LargeQueueIndex] = transport.LargeMessageChannelEnabled
                ? new QueueWrapperImpl(CreateQueue(_largeQueueSize, LargeQueueIndex))
                : DisabledQueueWrapper.Instance;

            for (var i = 0; i < _outboundLanes; ++i)
            {
                _queues[OrdinaryQueueIndex + i] = new QueueWrapperImpl(CreateQueue(_queueSize, OrdinaryQueueIndex + 1));
            }

            _controlQueue = _queues[ControlQueueIndex];
        }

        // We start with the raw wrapped queue and then it is replaced with the materialized value of
        // the `SendQueue` after materialization. Using same underlying queue. This makes it possible to
        // start sending (enqueuing) to the Association immediate after construction.
        private IQueue<IOutboundEnvelope> CreateQueue(int capacity, int queueIndex)
        {
            var linked = queueIndex == ControlQueueIndex || queueIndex == LargeQueueIndex;
            if (linked)
                return new LinkedBlockingQueue<IOutboundEnvelope>(capacity);
            else
                return new ManyToOneConcurrentArrayQueue<IOutboundEnvelope>(capacity);
        }

        public Task<Done> ChangeActorRefCompression(CompressionTable<IActorRef> table)
            => UpdateOutboundCompression(c => c.ChangeActorRefCompression(false));

        private Task<Done> UpdateOutboundCompression(Func<OutboundCompressionAccess, Task<Done>> action)
        {
            var ec = Transport.System.Dispatchers.InternalDispatcher;
            var c = Volatile.Read(ref _outboundCompressionAccess);
            switch (c.Count)
            {
                case 0:
                    return Task.FromResult(Done.Instance);
                case 1:
                    return action(c[0]);
                default:
                    var tasks = new List<Task<Done>>();
                    foreach (var item in c)
                    {
                        tasks.Add(action(item));
                    }
                    // TODO: RECHECK CODE: I'm not sure this is a proper 1 to 1 conversion of the original scala code
                    return Task.WhenAll(tasks).ContinueWith(t => Task.FromResult(Done.Instance)).Unwrap();
            }
        }
    }
}
