//-----------------------------------------------------------------------
// <copyright file="StreamRefs.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.Configuration;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Util.Internal;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    
    /// <summary> 
    /// API MAY CHANGE: The functionality of stream refs is working, however it is expected that the materialized value
    /// will eventually be able to remove the Task wrapping the stream references. For this reason the API is now marked
    /// as API may change. See ticket https://github.com/akka/akka/issues/24372 for more details.
    /// 
    /// Factories for creating stream refs.
    /// </summary>
    [ApiMayChange]
    public static class StreamRefs
    {
        /// <summary>
        /// A local <see cref="Sink{TIn,TMat}"/> which materializes a <see cref="SourceRef{T}"/> which can be used by other streams (including remote ones),
        /// to consume data from this local stream, as if they were attached in the spot of the local Sink directly.
        /// 
        /// Adheres to <see cref="StreamRefAttributes"/>.
        /// </summary>
        /// <seealso cref="SourceRef{T}"/>
        [ApiMayChange]
        public static Sink<T, Task<ISourceRef<T>>> SourceRef<T>() => 
            Sink.FromGraph<T, Task<ISourceRef<T>>>(new SinkRefStageImpl<T>(null));

        /// <summary>
        /// A local <see cref="Sink{TIn,TMat}"/> which materializes a <see cref="SinkRef{T}"/> which can be used by other streams (including remote ones),
        /// to consume data from this local stream, as if they were attached in the spot of the local Sink directly.
        /// 
        /// Adheres to <see cref="StreamRefAttributes"/>.
        /// 
        /// See more detailed documentation on [[SinkRef]].
        /// </summary>
        /// <seealso cref="SinkRef{T}"/>
        [ApiMayChange]
        public static Source<T, Task<ISinkRef<T>>> SinkRef<T>() => 
            Source.FromGraph<T, Task<ISinkRef<T>>>(new SourceRefStageImpl<T>(null));
    }

    #region StreamRef messages

    internal interface IStreamRefsProtocol { }

    /// <summary>
    /// Sequenced <see cref="ISubscriber{T}.OnNext"/> equivalent.
    /// The receiving end of these messages MUST fail the stream if it observes gaps in the sequence,
    /// as these messages will not be re-delivered.
    /// 
    /// Sequence numbers start from `0`.
    /// </summary>
    internal sealed class SequencedOnNext : IStreamRefsProtocol, IDeadLetterSuppression
    {
        public long SeqNr { get; }
        public object Payload { get; }

        public SequencedOnNext(long seqNr, object payload)
        {
            SeqNr = seqNr;
            Payload = payload ?? throw ReactiveStreamsCompliance.ElementMustNotBeNullException;
        }
    }

    /// <summary>
    /// Initial message sent to remote side to establish partnership between origin and remote stream refs.
    /// </summary>
    internal sealed class OnSubscribeHandshake : IStreamRefsProtocol, IDeadLetterSuppression
    {
        public OnSubscribeHandshake(IActorRef targetRef)
        {
            TargetRef = targetRef;
        }

        public IActorRef TargetRef { get; }
    }

    /// <summary>
    /// Sent to a the receiver side of a stream ref, once the sending side of the SinkRef gets signalled a Failure.
    /// </summary>
    internal sealed class RemoteStreamFailure : IStreamRefsProtocol
    {
        public RemoteStreamFailure(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }

    /// <summary>
    /// Sent to a the receiver side of a stream ref, once the sending side of the SinkRef gets signalled a completion.
    /// </summary>
    internal sealed class RemoteStreamCompleted : IStreamRefsProtocol
    {
        public RemoteStreamCompleted(long seqNr)
        {
            SeqNr = seqNr;
        }

        public long SeqNr { get; }
    }

    /// <summary>
    /// INTERNAL API: Cumulative demand, equivalent to sequence numbering all events in a stream.
    /// 
    /// This message may be re-delivered.
    /// </summary>
    internal sealed class CumulativeDemand : IStreamRefsProtocol, IDeadLetterSuppression
    {
        public CumulativeDemand(long seqNr)
        {
            if (seqNr < 0) throw ReactiveStreamsCompliance.NumberOfElementsInRequestMustBePositiveException;
            SeqNr = seqNr;
        }

        public long SeqNr { get; }
    }

    #endregion

    #region extension

    internal sealed class StreamRefsMaster : IExtension
    {
        public static StreamRefsMaster Get(ActorSystem system) =>
            system.WithExtension<StreamRefsMaster, StreamRefsMasterProvider>();

        private readonly EnumerableActorName sourceRefStageNames = new EnumerableActorNameImpl("SourceRef", new AtomicCounterLong(0L));
        private readonly EnumerableActorName sinkRefStageNames = new EnumerableActorNameImpl("SinkRef", new AtomicCounterLong(0L)); 
        
        public StreamRefsMaster(ExtendedActorSystem system)
        {
            
        }

        public string NextSourceRefName() => sourceRefStageNames.Next();
        public string NextSinkRefName() => sinkRefStageNames.Next();
    }
    
    internal sealed class StreamRefsMasterProvider : ExtensionIdProvider<StreamRefsMaster>
    {
        public override StreamRefsMaster CreateExtension(ExtendedActorSystem system) => 
            new StreamRefsMaster(system);
    }

    #endregion

    public sealed class StreamRefSettings
    {
        public static StreamRefSettings Create(Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config), "`akka.stream.materializer.stream-ref` was not present");
            
            return new StreamRefSettings(
                bufferCapacity: config.GetInt("buffer-capacity"),
                demandRedeliveryInterval: config.GetTimeSpan("demand-redelivery-interval"),
                subscriptionTimeout: config.GetTimeSpan("subscription-timeout"));
        }
        
        public int BufferCapacity { get; }
        public TimeSpan DemandRedeliveryInterval { get; }
        public TimeSpan SubscriptionTimeout { get; }

        public StreamRefSettings(int bufferCapacity, TimeSpan demandRedeliveryInterval, TimeSpan subscriptionTimeout)
        {
            BufferCapacity = bufferCapacity;
            DemandRedeliveryInterval = demandRedeliveryInterval;
            SubscriptionTimeout = subscriptionTimeout;
        }

        public string ProductPrefix => nameof(StreamRefSettings);

        public StreamRefSettings WithBufferCapacity(int value) => Copy(bufferCapacity: value);
        public StreamRefSettings WithDemandRedeliveryInterval(TimeSpan value) => Copy(demandRedeliveryInterval: value);
        public StreamRefSettings WithSubscriptionTimeout(TimeSpan value) => Copy(subscriptionTimeout: value);
        
        public StreamRefSettings Copy(int? bufferCapacity = null, 
            TimeSpan? demandRedeliveryInterval = null, 
            TimeSpan? subscriptionTimeout = null) => new StreamRefSettings(
            bufferCapacity: bufferCapacity ?? this.BufferCapacity,
            demandRedeliveryInterval: demandRedeliveryInterval ?? this.DemandRedeliveryInterval,
            subscriptionTimeout: subscriptionTimeout ?? this.SubscriptionTimeout);
    }

    /// <summary>
    /// Abstract class defined serialization purposes of <see cref="SourceRefImpl{T}"/>.
    /// </summary>
    internal abstract class SourceRefImpl 
    {
        public static SourceRefImpl Create(Type eventType, IActorRef initialPartnerRef)
        {
            var destType = typeof(SourceRefImpl<>).MakeGenericType(eventType);
            return (SourceRefImpl)Activator.CreateInstance(destType, initialPartnerRef);
        }
        
        protected SourceRefImpl(IActorRef initialPartnerRef)
        {
            InitialPartnerRef = initialPartnerRef;
        }

        public IActorRef InitialPartnerRef { get; }
        public abstract Type EventType { get; }
    }
    internal sealed class SourceRefImpl<T> : SourceRefImpl, ISourceRef<T>
    {
        public SourceRefImpl(IActorRef initialPartnerRef) : base(initialPartnerRef) { }
        public override Type EventType => typeof(T);
        public Source<T, NotUsed> Source => 
            Dsl.Source.FromGraph(new SourceRefStageImpl<T>(InitialPartnerRef)).MapMaterializedValue(_ => NotUsed.Instance);
    }

    /// <summary>
    /// Abstract class defined serialization purposes of <see cref="SinkRefImpl{T}"/>.
    /// </summary>
    internal abstract class SinkRefImpl
    {
        public static SinkRefImpl Create(Type eventType, IActorRef initialPartnerRef)
        {
            var destType = typeof(SinkRefImpl<>).MakeGenericType(eventType);
            return (SinkRefImpl)Activator.CreateInstance(destType, initialPartnerRef);
        }
        
        protected SinkRefImpl(IActorRef initialPartnerRef)
        {
            InitialPartnerRef = initialPartnerRef;
        }

        public IActorRef InitialPartnerRef { get; }
        public abstract Type EventType { get; }
    }
    
    internal sealed class SinkRefImpl<T> : SinkRefImpl, ISinkRef<T>
    {
        public SinkRefImpl(IActorRef initialPartnerRef) : base(initialPartnerRef) { }
        public override Type EventType => typeof(T);
        public Sink<T, NotUsed> Sink => Dsl.Sink.FromGraph(new SinkRefStageImpl<T>(InitialPartnerRef)).MapMaterializedValue(_ => NotUsed.Instance);
    }
    
    /// <summary>
    /// INTERNAL API: Actual stage implementation backing <see cref="ISinkRef{TIn}"/>s.
    ///
    /// If initialPartnerRef is set, then the remote side is already set up. If it is none, then we are the side creating
    /// the ref.
    /// </summary>
    /// <typeparam name="TIn"></typeparam>
    internal sealed class SinkRefStageImpl<TIn> : GraphStageWithMaterializedValue<SinkShape<TIn>, Task<ISourceRef<TIn>>>
    {
        #region logic

        private sealed class Logic : TimerGraphStageLogic
        {
            private const string SubscriptionTimeoutKey = "SubscriptionTimeoutKey";
            
            private readonly SinkRefStageImpl<TIn> _stage;
            private readonly TaskCompletionSource<ISourceRef<TIn>> _promise;
            
            private readonly StreamRefsMaster _streamRefsMaster;
            private readonly StreamRefSettings _settings;
            private readonly StreamRefAttributes.SubscriptionTimeout _subscriptionTimeout;
            private readonly string _stageActorName;
            
            private StageActorRef _self;

            private IActorRef _partnerRef = null;

            #region demand management
            private long _remoteCumulativeDemandReceived = 0L;
            private long _remoteCumulativeDemandConsumed = 0L;
            #endregion

            private Status _completedBeforeRemoteConnected = null;
            
            public IActorRef PartnerRef
            {
                get
                {
                    if (_partnerRef == null) throw new TargetRefNotInitializedYetException();
                    return _partnerRef;
                }
            }
            
            public Logic(SinkRefStageImpl<TIn> stage, TaskCompletionSource<ISourceRef<TIn>> promise) : base(stage.Shape)
            {
                _stage = stage;
                _promise = promise;

                var materializer = ActorMaterializerHelper.Downcast(Materializer);
                _streamRefsMaster = StreamRefsMaster.Get(materializer.System);
                _settings = materializer.Settings.StreamRefSettings;
                _subscriptionTimeout = _stage.InitialAttributes.GetAttribute<StreamRefAttributes.SubscriptionTimeout>();
                _stageActorName = _streamRefsMaster.NextSinkRefName();
                
                this.SetHandler(_stage.Inlet, 
                    onPush: OnPush,
                    onUpstreamFinish: OnUpstreamFinish,
                    onUpstreamFailure: OnUpstreamFailure);
            }

            public override void PreStart()
            {
                _self = GetStageActorRef(InitialReceive);
                var initialPartnerRef = _stage._initialPartnerRef;
                if (initialPartnerRef != null)
                    ObserveAndValidateSender(initialPartnerRef, "Illegal initialPartnerRef! This would be a bug in the SinkRef usage or impl.");
                
                Log.Debug("Created SinkRef, pointing to remote Sink receiver: {0}, local worker: {1}", initialPartnerRef, _self);
                
                _promise.SetResult(new SourceRefImpl<TIn>(_self));

                if (_partnerRef != null)
                {
                    _partnerRef.Tell(new OnSubscribeHandshake(_self));
                    TryPull();
                }
                
                ScheduleOnce(SubscriptionTimeoutKey, _subscriptionTimeout.Timeout);
            }

            private void InitialReceive(Tuple<IActorRef, object> args)
            {
                var sender = args.Item1;
                var message = args.Item2;

                switch (message)
                {
                    case Terminated terminated:
                        if (terminated.ActorRef == PartnerRef)
                            FailStage(new RemoteStreamRefActorTerminatedException("Remote target receiver of data $partnerRef terminated. " + 
                                                                                  "Local stream terminating, message loss (on remote side) may have happened."));
                        break;
                    case CumulativeDemand demand:
                        // the other side may attempt to "double subscribe", which we want to fail eagerly since we're 1:1 pairings
                        ObserveAndValidateSender(sender, "Illegal sender for CumulativeDemand");
                        if (_remoteCumulativeDemandReceived < demand.SeqNr)
                        {
                            _remoteCumulativeDemandReceived = demand.SeqNr;
                            Log.Debug("Received cumulative demand [{0}], consumable demand: [{1}]", demand.SeqNr, _remoteCumulativeDemandReceived - _remoteCumulativeDemandConsumed);
                        }
                        TryPull();
                        break;        
                }
            }

            private void OnPush()
            {
                var element = GrabSequenced(_stage.Inlet);
                PartnerRef.Tell(element);
                Log.Debug("Sending sequenced: {0} to {1}", element, PartnerRef);
                TryPull();
            }

            private void TryPull()
            {
                if (_remoteCumulativeDemandConsumed < _remoteCumulativeDemandReceived && !HasBeenPulled(_stage.Inlet))
                {
                    Pull(_stage.Inlet);
                }
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (timerKey == SubscriptionTimeoutKey)
                {
                    // we know the future has been competed by now, since it is in preStart
                    var ex = new StreamRefSubscriptionTimeoutException($"[{_stageActorName}] Remote side did not subscribe (materialize) handed out Sink reference [${_promise.Task.Result}], " +
                                                                       "within subscription timeout: ${PrettyDuration.format(subscriptionTimeout.timeout)}!");

                    throw ex; // this will also log the exception, unlike failStage; this should fail rarely, but would be good to have it "loud"
                }
            }

            private SequencedOnNext GrabSequenced(Inlet<TIn> inlet)
            {
                var onNext = new SequencedOnNext(_remoteCumulativeDemandConsumed, Grab(inlet));
                _remoteCumulativeDemandConsumed++;
                return onNext;
            }

            private void OnUpstreamFailure(Exception cause)
            {
                if (_partnerRef != null)
                {
                    _partnerRef.Tell(new RemoteStreamFailure(cause.ToString()));
                    _self.Unwatch(_partnerRef);
                    FailStage(cause);
                }
                else
                {
                    _completedBeforeRemoteConnected = new Status.Failure(cause);
                    // not terminating on purpose, since other side may subscribe still and then we want to fail it
                    // the stage will be terminated either by timeout, or by the handling in `observeAndValidateSender`
                    SetKeepGoing(true);
                }
            }

            private void OnUpstreamFinish()
            {
                if (_partnerRef != null)
                {
                    _partnerRef.Tell(new RemoteStreamCompleted(_remoteCumulativeDemandConsumed));
                    _self.Unwatch(_partnerRef);
                    CompleteStage();
                }
                else
                {
                    _completedBeforeRemoteConnected = new Status.Success(Done.Instance);
                    // not terminating on purpose, since other side may subscribe still and then we want to complete it
                    SetKeepGoing(true);
                }
            }

            private void ObserveAndValidateSender(IActorRef partner, string failureMessage)
            {
                if (_partnerRef == null)
                {
                    _partnerRef = partner;
                    _self.Watch(_partnerRef);

                    switch (_completedBeforeRemoteConnected)
                    {
                        case Status.Failure failure:
                            Log.Warning("Stream already terminated with exception before remote side materialized, failing now.");
                            partner.Tell(new RemoteStreamFailure(failure.Cause.ToString()));
                            FailStage(failure.Cause);
                            break;
                        case Status.Success _:
                            Log.Warning("Stream already completed before remote side materialized, failing now.");
                            partner.Tell(new RemoteStreamCompleted(_remoteCumulativeDemandConsumed));
                            CompleteStage();
                            break;
                        case null:
                            if (!Equals(partner, PartnerRef))
                            {
                                var ex = new InvalidPartnerActorException(partner, PartnerRef, failureMessage);
                                partner.Tell(new RemoteStreamFailure(ex.ToString()));
                                throw ex;
                            }
                            break;
                    }
                }
            }
        }

        #endregion

        private readonly IActorRef _initialPartnerRef;
        
        public SinkRefStageImpl(IActorRef initialPartnerRef)
        {
            _initialPartnerRef = initialPartnerRef;
        }

        public Inlet<TIn> Inlet { get; } = new Inlet<TIn>("SinkRef.in");
        public override SinkShape<TIn> Shape { get; }
        public override ILogicAndMaterializedValue<Task<ISourceRef<TIn>>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<ISourceRef<TIn>>();
            return new LogicAndMaterializedValue<Task<ISourceRef<TIn>>>(new Logic(this, promise), promise.Task);
        }
    }
    
    
    /// <summary>
    /// INTERNAL API: Actual stage implementation backing [[SourceRef]]s.
    /// 
    /// If initialPartnerRef is set, then the remote side is already set up.
    /// If it is none, then we are the side creating the ref.
    /// </summary>
    internal sealed class SourceRefStageImpl<TOut> : GraphStageWithMaterializedValue<SourceShape<TOut>, Task<ISinkRef<TOut>>>
    {

        #region logic

        private sealed class Logic : TimerGraphStageLogic
        {
            private const string SubscriptionTimeoutKey = "SubscriptionTimeoutKey";
            private const string DemandRedeliveryTimerKey = "DemandRedeliveryTimerKey";
            
            private readonly SourceRefStageImpl<TOut> _stage;
            private readonly TaskCompletionSource<ISinkRef<TOut>> _promise;
            
            private readonly StreamRefsMaster _streamRefsMaster;
            private readonly StreamRefSettings _settings;
            private readonly StreamRefAttributes.SubscriptionTimeout _subscriptionTimeout;
            private readonly string _stageActorName;
            
            private StageActorRef _self;
            private IActorRef _partnerRef = null;

            public IActorRef PartnerRef
            {
                get
                {
                    if (_partnerRef == null) throw new TargetRefNotInitializedYetException();
                    return _partnerRef;
                }
            }

            #region demand management

            private bool _completed = false;
            private long _expectingSeqNr = 0L;
            private long _localCumulativeDemand = 0L;
            private long _localRemainingRequested = 0L;
            private FixedSizeBuffer<TOut> _receiveBuffer; // initialized in preStart since depends on settings
            private IRequestStrategy _requestStrategy; // initialized in preStart since depends on receiveBuffer's size
            #endregion

            public Logic(SourceRefStageImpl<TOut> stage, TaskCompletionSource<ISinkRef<TOut>> promise) : base(stage.Shape)
            {
                _stage = stage;
                _promise = promise;
                
                var materializer = ActorMaterializerHelper.Downcast(Materializer);
                _streamRefsMaster = StreamRefsMaster.Get(materializer.System);
                _settings = materializer.Settings.StreamRefSettings;
                _subscriptionTimeout = _stage.InitialAttributes.GetAttribute<StreamRefAttributes.SubscriptionTimeout>();
                _stageActorName = _streamRefsMaster.NextSinkRefName();
                
                SetHandler(_stage.Outlet, onPull: OnPull);
            }

            public override void PreStart()
            {
                _receiveBuffer = new ModuloFixedSizeBuffer<TOut>(_settings.BufferCapacity);
                _requestStrategy = new WatermarkRequestStrategy(highWatermark: _receiveBuffer.Capacity);

                _self = GetStageActorRef(InitialReceive);
                
                Log.Debug("[{0}] Allocated receiver: {1}", _stageActorName, _self);

                var initialPartnerRef = _stage._initialPartnerRef;
                if (initialPartnerRef != null) // this will set the partnerRef
                    ObserveAndValidateSender(initialPartnerRef, "<should never happen>");
                
                _promise.SetResult(new SinkRefImpl<TOut>(_self));
                
                ScheduleOnce(SubscriptionTimeoutKey, _subscriptionTimeout.Timeout);
            }

            private void OnPull()
            {
                TryPush();
                TriggerCumulativeDemand();
            }

            private void TriggerCumulativeDemand()
            {
                var i = _receiveBuffer.RemainingCapacity - _localRemainingRequested;
                if (_partnerRef != null && i > 0)
                {
                    var addDemand = _requestStrategy.RequestDemand((int)(_receiveBuffer.Used + _localRemainingRequested));
                    
                    // only if demand has increased we shoot it right away
                    // otherwise it's the same demand level, so it'd be triggered via redelivery anyway
                    if (addDemand > 0)
                    {
                        _localCumulativeDemand += addDemand;
                        _localRemainingRequested += addDemand;
                        var demand = new CumulativeDemand(_localCumulativeDemand);
                        
                        Log.Debug("[{0}] Demanding until [{1}] (+{2})", _stageActorName, _localCumulativeDemand, addDemand);
                        PartnerRef.Tell(demand);
                        ScheduleDemandRedelivery();
                    }
                }
            }

            private void ScheduleDemandRedelivery() => 
                ScheduleOnce(DemandRedeliveryTimerKey, _settings.DemandRedeliveryInterval);

            protected internal override void OnTimer(object timerKey)
            {
                switch (timerKey)
                {
                    case SubscriptionTimeoutKey:
                        var ex = new StreamRefSubscriptionTimeoutException(
                            // we know the future has been competed by now, since it is in preStart
                            $"[{_stageActorName}] Remote side did not subscribe (materialize) handed out Sink reference [{_promise.Task.Result}]," +
                            $"within subscription timeout: {_subscriptionTimeout.Timeout}!");
                        throw ex;
                    case DemandRedeliveryTimerKey:
                        Log.Debug("[{0}] Scheduled re-delivery of demand until [{1}]", _stageActorName, _localCumulativeDemand);
                        PartnerRef.Tell(new CumulativeDemand(_localCumulativeDemand));
                        ScheduleDemandRedelivery();
                        break;
                }
            }

            private void InitialReceive(Tuple<IActorRef, object> args)
            {
                var sender = args.Item1;
                var message = args.Item2;

                switch (message)
                {
                    case OnSubscribeHandshake handshake: 
                        CancelTimer(SubscriptionTimeoutKey);
                        ObserveAndValidateSender(sender, "Illegal sender in OnSubscribeHandshake");
                        Log.Debug("[{0}] Received handshake {1} from {2}", _stageActorName, message, sender);
                        TriggerCumulativeDemand();
                        break;
                    case SequencedOnNext onNext: 
                        ObserveAndValidateSender(sender, "Illegal sender in SequencedOnNext");
                        ObserveAndValidateSequenceNr(onNext.SeqNr, "Illegal sequence nr in SequencedOnNext");
                        Log.Debug("[{0}] Received seq {1} from {2}", _stageActorName, message, sender);
                        OnReceiveElement(onNext.Payload);
                        TriggerCumulativeDemand();
                        break;
                    case RemoteStreamCompleted completed:
                        ObserveAndValidateSender(sender, "Illegal sender in RemoteStreamCompleted");
                        ObserveAndValidateSequenceNr(completed.SeqNr, "Illegal sequence nr in RemoteStreamCompleted");
                        Log.Debug("[{0}] The remote stream has completed, completing as well...", _stageActorName);
                        _self.Unwatch(sender);
                        _completed = true;
                        TryPush();
                        break;
                    case RemoteStreamFailure failure:
                        ObserveAndValidateSender(sender, "Illegal sender in RemoteStreamFailure"); 
                        Log.Warning("[{0}] The remote stream has failed, failing (reason: {1})", _stageActorName, failure.Message);
                        _self.Unwatch(sender);
                        FailStage(new RemoteStreamRefActorTerminatedException($"Remote stream ({sender.Path}) failed, reason: {failure.Message}"));
                        break;
                    case Terminated terminated:
                        if (Equals(_partnerRef, terminated.ActorRef))
                            FailStage(new RemoteStreamRefActorTerminatedException(
                                "The remote partner $ref has terminated! Tearing down this side of the stream as well."));
                        else
                            FailStage(new RemoteStreamRefActorTerminatedException(
                                $"Received UNEXPECTED Terminated({terminated.ActorRef}) message! This actor was NOT our trusted remote partner, which was: {_partnerRef}. Tearing down."));

                        break;
                }
            }

            private void TryPush()
            {
                if (!_receiveBuffer.IsEmpty) Push(_stage.Outlet, _receiveBuffer.Dequeue());
                else if (_completed) CompleteStage();
            }

            private void OnReceiveElement(object payload)
            {
                var outlet = _stage.Outlet;
                _localRemainingRequested--;
                if (_receiveBuffer.IsEmpty && IsAvailable(outlet))
                    Push(outlet, (TOut)payload);
                else if (_receiveBuffer.IsFull)
                    throw new IllegalStateException($"Attempted to overflow buffer! Capacity: {_receiveBuffer.Capacity}, incoming element: {payload}, localRemainingRequested: {_localRemainingRequested}, localCumulativeDemand: {_localCumulativeDemand}");
                else 
                    _receiveBuffer.Enqueue((TOut)payload);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <exception cref="InvalidPartnerActorException"> Thrown when <paramref name="partner"/> is invalid</exception>
            private void ObserveAndValidateSender(IActorRef partner, string failureMessage)
            {
                if (_partnerRef == null)
                {
                    Log.Debug("Received first message from {0}, assuming it to be the remote partner for this stage", partner);
                    _partnerRef = partner;
                    _self.Watch(partner);
                }
                else if (_partnerRef != partner)
                {
                    var ex = new InvalidPartnerActorException(partner, PartnerRef, failureMessage);
                    partner.Tell(new RemoteStreamFailure(ex.Message));
                    throw ex;
                }
            }

            private void ObserveAndValidateSequenceNr(long seqNr, string failureMessage)
            {
                if (seqNr != _expectingSeqNr)
                    throw new InvalidSequenceNumberException(_expectingSeqNr, seqNr, failureMessage);
                else
                    _expectingSeqNr++;
            }
        }

        #endregion
        
        private readonly IActorRef _initialPartnerRef;
        
        public SourceRefStageImpl(IActorRef initialPartnerRef)
        {
            _initialPartnerRef = initialPartnerRef;
            
            Shape = new SourceShape<TOut>(Outlet);
        }
        
        public Outlet<TOut> Outlet { get; } = new Outlet<TOut>("SourceRef.out");
        public override SourceShape<TOut> Shape { get; }
        
        public override ILogicAndMaterializedValue<Task<ISinkRef<TOut>>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var promise= new TaskCompletionSource<ISinkRef<TOut>>();
            return new LogicAndMaterializedValue<Task<ISinkRef<TOut>>>(new Logic(this, promise), promise.Task);
        }
    }
}