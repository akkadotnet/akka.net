using System;
using System.Collections;
using System.Collections.Generic;
using System.Reactive.Streams;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Actors;

namespace Akka.Streams.Implementation
{
    internal static class ActorProcessor
    {
        public static ActorProcessor<TIn, TOut> Create<TIn, TOut>(IActorRef impl)
        {
            var p = new ActorProcessor<TIn, TOut>(impl);
            // Resolve cyclic dependency with actor. This MUST be the first message no matter what.
            impl.Tell(new ExposedPublisher<TOut>(p));
            return p;
        }
    }

    internal class ActorProcessor<TIn, TOut> : ActorPublisher<TOut>, IProcessor<TIn, TOut>
    {
        public ActorProcessor(IActorRef impl) : base(impl)
        {
        }

        public void OnNext(TIn element)
        {
            OnNext((object)element);
        }

        public void OnSubscribe(ISubscription subscription)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
            Impl.Tell(new OnSubscribe(subscription));
        }

        public void OnNext(object element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            Impl.Tell(new OnNext(element));
        }

        public void OnError(Exception cause)
        {
            ReactiveStreamsCompliance.RequireNonNullException(cause);
            Impl.Tell(new OnError(cause));
        }

        public void OnComplete()
        {
            Impl.Tell(Actors.OnComplete.Instance);
        }
    }

    internal abstract class BatchingInputBuffer : IInputs
    {
        public readonly int Count;
        public readonly IPump Pump;

        private readonly object[] _inputBuffer;
        private readonly int _indexMask;
        private ISubscription _upstream;
        private int _inputBufferElements = 0;
        private int _nextInputElementCursor = 0;
        private bool _isUpstreamCompleted = false;
        private int _batchRemaining;

        protected BatchingInputBuffer(int count, IPump pump)
        {
            if (count <= 0) throw new ArgumentException("Buffer Count must be > 0");
            if ((count & (count - 1)) != 0) throw new ArgumentException("Buffer Count must be power of two");
            // TODO: buffer and batch sizing heuristics

            Count = count;
            Pump = pump;

            _indexMask = count - 1;
            _inputBuffer = new object[count];
            _batchRemaining = RequestBatchSize;
            _subReceive = new SubReceive(WaitingForUpstream);

            _needsInput = DefaultInputTransferStates.NeedsInput(this);
            _needsInputOrComplete = DefaultInputTransferStates.NeedsInputOrComplete(this);
        }

        private int RequestBatchSize { get { return Math.Max(1, _inputBuffer.Length / 2); } }

        public override string ToString()
        {
            return string.Format("BatchingInputBuffer(Count={0}, elems={1}, completed={2}, remaining={3})", Count, _inputBufferElements, _isUpstreamCompleted, _batchRemaining);
        }

        private readonly SubReceive _subReceive;
        public virtual SubReceive SubReceive { get { return _subReceive; } }

        public virtual object DequeueInputElement()
        {
            var elem = _inputBuffer[_nextInputElementCursor];
            _inputBuffer[_nextInputElementCursor] = null;

            _batchRemaining--;
            if (_batchRemaining == 0 && !_isUpstreamCompleted)
            {
                _upstream.Request(RequestBatchSize);
                _batchRemaining = RequestBatchSize;
            }

            _inputBufferElements--;
            _nextInputElementCursor++;
            _nextInputElementCursor &= _indexMask;
            return elem;
        }

        protected virtual void EnqueueInputElement(object element)
        {
            if (IsOpen)
            {
                if (_inputBufferElements == Count) throw new IllegalStateException("Input buffer overrun");
                _inputBuffer[(_nextInputElementCursor + _inputBufferElements) & _indexMask] = element;
                _inputBufferElements++;
            }

            Pump.Pump();
        }

        public virtual void Cancel()
        {
            if (!_isUpstreamCompleted)
            {
                _isUpstreamCompleted = true;
                if (!ReferenceEquals(_upstream, null)) _upstream.Cancel();
                Clear();
            }
        }

        private void Clear()
        {
            _inputBuffer.Initialize();
            _inputBufferElements = 0;
        }

        private readonly TransferState _needsInput;
        public TransferState NeedsInput { get { return _needsInput; } }

        private readonly TransferState _needsInputOrComplete;
        public TransferState NeedsInputOrComplete { get { return _needsInputOrComplete; } }

        public bool IsClosed { get { return _isUpstreamCompleted; } }
        public bool IsOpen { get { return !IsClosed; } }
        public bool AreInputsDepleted { get { return _isUpstreamCompleted && _inputBufferElements == 0; } }
        public bool AreInputsAvailable { get { return _inputBufferElements > 0; } }

        protected virtual void OnComplete()
        {
            _isUpstreamCompleted = true;
            SubReceive.Become(Completed);
            Pump.Pump();
        }

        protected virtual void OnSubscribe(ISubscription subscription)
        {
            if (subscription == null) throw new ArgumentException("OnSubscribe require subscription not to be null");

            if (_isUpstreamCompleted) subscription.Cancel();
            else
            {
                _upstream = subscription;
                // prefetch
                _upstream.Request(_inputBuffer.Length);
                SubReceive.Become(UpstreamRunning);
            }

            Pump.GotUpstreamSubscription();
        }

        protected virtual void OnError(Exception e)
        {
            _isUpstreamCompleted = true;
            SubReceive.Become(Completed);
            InputOnError(e);
        }

        protected virtual bool WaitingForUpstream(object message)
        {
            if (message is OnComplete) OnComplete();
            else if (message is OnSubscribe) OnSubscribe(((OnSubscribe)message).Subscription);
            else if (message is OnError) OnError(((OnError)message).Cause);
            else return false;
            return true;
        }

        protected virtual bool UpstreamRunning(object message)
        {
            if (message is OnNext) EnqueueInputElement(((OnNext)message).Element);
            else if (message is OnComplete) OnComplete();
            else if (message is OnSubscribe) ((OnSubscribe)message).Subscription.Cancel();
            else if (message is OnError) OnError(((OnError)message).Cause);
            else return false;
            return true;
        }

        protected virtual bool Completed(object message)
        {
            if (message is OnSubscribe)
                throw new IllegalStateException("OnSubscribe called after OnError or OnComplete");
            else return false;
        }

        protected virtual void InputOnError(Exception e)
        {
            Clear();
        }
    }

    internal class SimpleOutputs<TOut> : IOutputs
    {
        public readonly IActorRef Actor;
        public readonly IPump Pump;

        protected ActorPublisher<TOut> ExposedPublisher;
        protected ISubscriber<TOut> Subscriber;
        protected long DownstreamDemand = 0L;
        protected bool IsDownstreamCompleted = false;

        private readonly SubReceive _subReceive;

        public SimpleOutputs(IActorRef actor, IPump pump)
        {
            Actor = actor;
            Pump = pump;

            _subReceive = new SubReceive(WaitingExposedPublisher);
            NeedsDemand = DefaultOutputTransferStates.NeedsDemand(this);
            NeedsDemandOrCancel = DefaultOutputTransferStates.NeedsDemandOrCancel(this);
        }

        public bool IsSubscribed { get { return Subscriber != null; } }

        public virtual SubReceive SubReceive { get { return _subReceive; } }
        public TransferState NeedsDemand { get; }
        public TransferState NeedsDemandOrCancel { get; }
        public long DemandCount { get { return DownstreamDemand; } }
        public bool IsDemandAvailable { get { return DownstreamDemand > 0; } }

        public void EnqueueOutputElement(TOut element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            DownstreamDemand--;
            ReactiveStreamsCompliance.TryOnNext(Subscriber, element);
        }

        public void EnqueueOutputElement(object element)
        {
            EnqueueOutputElement((TOut)element);
        }

        public virtual void Complete()
        {
            if (!IsDownstreamCompleted)
            {
                IsDownstreamCompleted = true;
                if (!ReferenceEquals(ExposedPublisher, null)) ExposedPublisher.Shutdown(null);
                if (!ReferenceEquals(Subscriber, null)) ReactiveStreamsCompliance.TryOnComplete(Subscriber);
            }
        }

        public virtual void Cancel()
        {
            if (!IsDownstreamCompleted)
            {
                IsDownstreamCompleted = true;
                if (!ReferenceEquals(ExposedPublisher, null)) ExposedPublisher.Shutdown(null);
            }
        }

        public virtual void Error(Exception e)
        {
            if (!IsDownstreamCompleted)
            {
                IsDownstreamCompleted = true;
                if (!ReferenceEquals(ExposedPublisher, null)) ExposedPublisher.Shutdown(e);
                if (!ReferenceEquals(Subscriber, null) && !(e is ISpecViolation)) ReactiveStreamsCompliance.TryOnError(Subscriber, e);
            }
        }

        public bool IsClosed { get { return IsDownstreamCompleted && !ReferenceEquals(Subscriber, null); } }
        public bool IsOpen { get { return !IsClosed; } }

        protected ISubscription CreateSubscription()
        {
            return new ActorSubscription<TOut>(Actor, Subscriber);
        }

        private void SubscribePending(IEnumerable<ISubscriber<TOut>> subscribers)
        {
            foreach (var subscriber in subscribers)
            {
                if (ReferenceEquals(Subscriber, null))
                {
                    Subscriber = subscriber;
                    ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CreateSubscription());
                }
                else ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, GetType().Name);
            }
        }

        protected bool WaitingExposedPublisher(object message)
        {
            if (message is ExposedPublisher<TOut>)
            {
                ExposedPublisher = ((ExposedPublisher<TOut>)message).Publisher;
                SubReceive.Become(DownstreamRunning);
                return true;
            }
            else throw new IllegalStateException(string.Format("The first message must be [{0}] but was [{1}]", typeof(ExposedPublisher<TOut>), message));
        }

        protected bool DownstreamRunning(object message)
        {
            if (message is SubscribePending) SubscribePending(ExposedPublisher.TakePendingSubscribers());
            else if (message is RequestMore<TOut>)
            {
                var requestMore = (RequestMore<TOut>)message;
                if (requestMore.Demand < 1)
                    Error(ReactiveStreamsCompliance.NumberOfElementsInRequestMustBePositiveException);
                else
                {
                    DownstreamDemand += requestMore.Demand;
                    if (DownstreamDemand < 1)
                        DownstreamDemand = long.MaxValue;   // Long overflow, Reactive Streams Spec 3:17: effectively unbounded
                    Pump.Pump();
                }
            }
            else if (message is Cancel)
            {
                IsDownstreamCompleted = true;
                ExposedPublisher.Shutdown(new NormalShutdownException(string.Empty));
                Pump.Pump();
            }
            else return false;
            return true;
        }
    }

    internal abstract class ActorProcessorImpl<T> : ActorBase, IPump
    {
        #region Internal classes

        private sealed class InternalBatchingInputBuffer : BatchingInputBuffer
        {
            private readonly ActorProcessorImpl<T> _impl;
            public InternalBatchingInputBuffer(int count, ActorProcessorImpl<T> impl) : base(count, impl)
            {
                _impl = impl;
            }

            protected override void InputOnError(Exception e)
            {
                _impl.OnError(e);
            }
        }

        private sealed class InternalExposedPublisherReceive : ExposedPublisherReceive<T>
        {
            private readonly ActorProcessorImpl<T> _self;
            public InternalExposedPublisherReceive(Receive activeReceive, Action<object> unhandled, ActorProcessorImpl<T> self) : base(activeReceive, unhandled)
            {
                _self = self;
            }

            public override void ReceiveExposedPublisher(ExposedPublisher<T> publisher)
            {
                _self.PrimaryOutputs.SubReceive.CurrentReceive(publisher);
                Context.Become(ActiveReceive);
            }
        }

        #endregion

        public readonly ActorMaterializerSettings Settings;

        protected readonly IInputs PrimaryInputs;
        protected readonly IOutputs PrimaryOutputs;

        private ILoggingAdapter _log;

        protected ActorProcessorImpl(ActorMaterializerSettings settings)
        {
            Settings = settings;

            PrimaryInputs = new InternalBatchingInputBuffer(settings.InitialInputBufferSize, this);
            PrimaryOutputs = new SimpleOutputs<T>(Self, this);

            _receive = new InternalExposedPublisherReceive(ActiveReceive, Unhandled, this);
        }

        protected ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

        public TransferState TransferState { get; set; }
        public Action CurrentAction { get; set; }
        public bool IsPumpFinished { get; }

        private readonly ExposedPublisherReceive<T> _receive;
        /**
         * Subclass may override [[#activeReceive]]
         */
        protected sealed override bool Receive(object message)
        {
            return _receive.Apply(message);
        }

        protected virtual bool ActiveReceive(object message)
        {
            return PrimaryInputs.SubReceive.CurrentReceive(message) || PrimaryOutputs.SubReceive.CurrentReceive(message);
        }

        public void InitialPhase(int waitForUpstream, TransferPhase andThen)
        {
            Pumps.InitialPhase(this, waitForUpstream, andThen);
        }

        public void WaitForUpstream(int waitForUpstream)
        {
            Pumps.WaitForUpstream(this, waitForUpstream);
        }

        public void GotUpstreamSubscription()
        {
            Pumps.GotUpstreamSubscription(this);
        }

        public void NextPhase(TransferPhase phase)
        {
            Pumps.NextPhase(this, phase);
        }

        public void Pump()
        {
            Pumps.Pump(this);
        }

        public void PumpFailed(Exception e)
        {
            Fail(e);
        }

        public virtual void PumpFinished()
        {
            PrimaryInputs.Cancel();
            PrimaryOutputs.Complete();
            Context.Stop(Self);
        }

        protected virtual void OnError(Exception e)
        {
            Fail(e);
        }

        protected virtual void Fail(Exception e)
        {
            if (Settings.IsDebugLogging)
                Log.Debug("Failed due to: {0}", e.Message);

            PrimaryInputs.Cancel();
            PrimaryOutputs.Error(e);
            Context.Stop(Self);
        }

        protected override void PostStop()
        {
            base.PostStop();
            PrimaryInputs.Cancel();
            PrimaryOutputs.Error(new AbruptTerminationException(Self));
        }

        protected override void PostRestart(Exception reason)
        {
            base.PostRestart(reason);
            throw new IllegalStateException("This actor cannot be restarted", reason);
        }
    }
}