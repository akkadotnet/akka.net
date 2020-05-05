//-----------------------------------------------------------------------
// <copyright file="SourceRefImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.Pattern;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.Serialization;
using Akka.Streams.Serialization.Proto.Msg;
using Akka.Streams.Stage;
using Akka.Util;
using Google.Protobuf.WellKnownTypes;
using Type = System.Type;

namespace Akka.Streams.Implementation.StreamRef
{
    /// <summary>
    /// Abstract class defined serialization purposes of <see cref="SourceRefImpl{T}"/>.
    /// </summary>
    [InternalApi]
    internal abstract class SourceRefImpl : ISurrogated
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
        public abstract ISurrogate ToSurrogate(ActorSystem system);
    }

    /// <summary>
    /// INTERNAL API:  Implementation class, not intended to be touched directly by end-users.
    /// </summary>
    [InternalApi]   
    internal sealed class SourceRefImpl<T> : SourceRefImpl, ISourceRef<T>
    {
        public SourceRefImpl(IActorRef initialPartnerRef) : base(initialPartnerRef) { }
        public override Type EventType => typeof(T);
        public Source<T, NotUsed> Source =>
            Dsl.Source.FromGraph(new SourceRefStageImpl<T>(InitialPartnerRef)).MapMaterializedValue(_ => NotUsed.Instance);

        public override ISurrogate ToSurrogate(ActorSystem system) => SerializationTools.ToSurrogate(this);
    }

    /// <summary>
    /// INTERNAL API: Actual stage implementation backing [[SourceRef]]s.
    /// 
    /// If initialPartnerRef is set, then the remote side is already set up.
    /// If it is none, then we are the side creating the ref.
    /// </summary>
    [InternalApi]
    internal sealed class SourceRefStageImpl<TOut> : GraphStageWithMaterializedValue<SourceShape<TOut>, Task<ISinkRef<TOut>>>
    {

        #region logic

        private sealed class Logic : TimerGraphStageLogic, IOutHandler
        {
            private const string SubscriptionTimeoutKey = "SubscriptionTimeoutKey";
            private const string DemandRedeliveryTimerKey = "DemandRedeliveryTimerKey";
            private const string TerminationDeadlineTimerKey = "TerminationDeadlineTimerKey";

            private readonly SourceRefStageImpl<TOut> _stage;
            private readonly TaskCompletionSource<ISinkRef<TOut>> _promise;
            private readonly Attributes _inheritedAttributes;

            private StreamRefsMaster _streamRefsMaster;
            private StreamRefSettings _settings;
            private StreamRefAttributes.SubscriptionTimeout _subscriptionTimeout;
            private string _stageActorName;

            private StageActor _stageActor;
            private IActorRef _partnerRef = null;

            private StreamRefsMaster StreamRefsMaster => _streamRefsMaster ?? (_streamRefsMaster = StreamRefsMaster.Get(ActorMaterializerHelper.Downcast(Materializer).System));
            private StreamRefSettings Settings => _settings ?? (_settings = ActorMaterializerHelper.Downcast(Materializer).Settings.StreamRefSettings);
            private StreamRefAttributes.SubscriptionTimeout SubscriptionTimeout => _subscriptionTimeout ?? (_subscriptionTimeout =
                                                                                       _inheritedAttributes.GetAttribute(new StreamRefAttributes.SubscriptionTimeout(Settings.SubscriptionTimeout)));
            protected override string StageActorName => _stageActorName ?? (_stageActorName = StreamRefsMaster.NextSourceRefName());

            public IActorRef Self => _stageActor.Ref;
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

            public Logic(SourceRefStageImpl<TOut> stage, TaskCompletionSource<ISinkRef<TOut>> promise, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                _promise = promise;
                _inheritedAttributes = inheritedAttributes;

                SetHandler(_stage.Outlet, this);
            }

            public override void PreStart()
            {
                _receiveBuffer = new ModuloFixedSizeBuffer<TOut>(Settings.BufferCapacity);
                _requestStrategy = new WatermarkRequestStrategy(highWatermark: _receiveBuffer.Capacity);

                _stageActor = GetStageActor(InitialReceive);

                Log.Debug("[{0}] Allocated receiver: {1}", StageActorName, Self);

                var initialPartnerRef = _stage._initialPartnerRef;
                if (initialPartnerRef != null) // this will set the partnerRef
                    ObserveAndValidateSender(initialPartnerRef, "<should never happen>");

                _promise.SetResult(new SinkRefImpl<TOut>(Self));

                //this timer will be cancelled if we receive the handshake from the remote SinkRef
                // either created in this method and provided as self.ref as initialPartnerRef
                // or as the response to first CumulativeDemand request sent to remote SinkRef
                ScheduleOnce(SubscriptionTimeoutKey, SubscriptionTimeout.Timeout);
            }

            public void OnPull()
            {
                TryPush();
                TriggerCumulativeDemand();
            }

            public void OnDownstreamFinish()
            {
                CompleteStage();
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
                        PartnerRef.Tell(demand, Self);
                        ScheduleDemandRedelivery();
                    }
                }
            }

            private void ScheduleDemandRedelivery() =>
                ScheduleOnce(DemandRedeliveryTimerKey, Settings.DemandRedeliveryInterval);

            protected internal override void OnTimer(object timerKey)
            {
                switch (timerKey)
                {
                    case SubscriptionTimeoutKey:
                        var ex = new StreamRefSubscriptionTimeoutException(
                            // we know the future has been competed by now, since it is in preStart
                            $"[{StageActorName}] Remote side did not subscribe (materialize) handed out Sink reference [{_promise.Task.Result}]," +
                            $"within subscription timeout: {SubscriptionTimeout.Timeout}!");
                        throw ex;
                    case DemandRedeliveryTimerKey:
                        Log.Debug("[{0}] Scheduled re-delivery of demand until [{1}]", StageActorName, _localCumulativeDemand);
                        PartnerRef.Tell(new CumulativeDemand(_localCumulativeDemand), Self);
                        ScheduleDemandRedelivery();
                        break;
                    case TerminationDeadlineTimerKey:
                        FailStage(new RemoteStreamRefActorTerminatedException(
                            $"Remote partner [{PartnerRef}] has terminated unexpectedly and no clean completion/failure message was received " +
                            $"(possible reasons: network partition or subscription timeout triggered termination of partner). Tearing down."));
                        break;
                }
            }

            private void InitialReceive((IActorRef, object) args)
            {
                var sender = args.Item1;
                var message = args.Item2;

                switch (message)
                {
                    case OnSubscribeHandshake handshake:
                        CancelTimer(SubscriptionTimeoutKey);
                        ObserveAndValidateSender(sender, "Illegal sender in OnSubscribeHandshake");
                        Log.Debug("[{0}] Received handshake {1} from {2}", StageActorName, message, sender);
                        TriggerCumulativeDemand();
                        break;
                    case SequencedOnNext onNext:
                        ObserveAndValidateSender(sender, "Illegal sender in SequencedOnNext");
                        ObserveAndValidateSequenceNr(onNext.SeqNr, "Illegal sequence nr in SequencedOnNext");
                        Log.Debug("[{0}] Received seq {1} from {2}", StageActorName, message, sender);
                        OnReceiveElement(onNext.Payload);
                        TriggerCumulativeDemand();
                        break;
                    case RemoteStreamCompleted completed:
                        ObserveAndValidateSender(sender, "Illegal sender in RemoteStreamCompleted");
                        ObserveAndValidateSequenceNr(completed.SeqNr, "Illegal sequence nr in RemoteStreamCompleted");
                        Log.Debug("[{0}] The remote stream has completed, completing as well...", StageActorName);
                        _stageActor.Unwatch(sender);
                        _completed = true;
                        TryPush();
                        break;
                    case RemoteStreamFailure failure:
                        ObserveAndValidateSender(sender, "Illegal sender in RemoteStreamFailure");
                        Log.Warning("[{0}] The remote stream has failed, failing (reason: {1})", StageActorName, failure.Message);
                        _stageActor.Unwatch(sender);
                        FailStage(new RemoteStreamRefActorTerminatedException($"Remote stream ({sender.Path}) failed, reason: {failure.Message}"));
                        break;
                    case Terminated terminated:
                        if (Equals(_partnerRef, terminated.ActorRef))
                            // we need to start a delayed shutdown in case we were network partitioned and the final signal complete/fail
                            // will never reach us; so after the given timeout we need to forcefully terminate this side of the stream ref
                            // the other (sending) side terminates by default once it gets a Terminated signal so no special handling is needed there.
                            ScheduleOnce(TerminationDeadlineTimerKey, Settings.FinalTerminationSignalDeadline);
                        else
                            FailStage(new RemoteStreamRefActorTerminatedException(
                                $"Received UNEXPECTED Terminated({terminated.ActorRef}) message! This actor was NOT our trusted remote partner, which was: {_partnerRef}. Tearing down."));

                        break;
                }
            }

            private void TryPush()
            {
                if (!_receiveBuffer.IsEmpty && IsAvailable(_stage.Outlet)) Push(_stage.Outlet, _receiveBuffer.Dequeue());
                else if (_receiveBuffer.IsEmpty && _completed) CompleteStage();
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
                    _stageActor.Watch(partner);
                }
                else if (!Equals(_partnerRef, partner))
                {
                    var ex = new InvalidPartnerActorException(partner, PartnerRef, failureMessage);
                    partner.Tell(new RemoteStreamFailure(ex.Message), Self);
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
            var promise = new TaskCompletionSource<ISinkRef<TOut>>();
            return new LogicAndMaterializedValue<Task<ISinkRef<TOut>>>(new Logic(this, promise, inheritedAttributes), promise.Task);
        }
    }
}
