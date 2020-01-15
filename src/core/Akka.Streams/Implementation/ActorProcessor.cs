//-----------------------------------------------------------------------
// <copyright file="ActorProcessor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Actors;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    internal static class ActorProcessor
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <param name="impl">TBD</param>
        /// <returns>TBD</returns>
        public static ActorProcessor<TIn, TOut> Create<TIn, TOut>(IActorRef impl)
        {
            var p = new ActorProcessor<TIn, TOut>(impl);
            // Resolve cyclic dependency with actor. This MUST be the first message no matter what.
            impl.Tell(new ExposedPublisher(p));
            return p;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    internal class ActorProcessor<TIn, TOut> : ActorPublisher<TOut>, IProcessor<TIn, TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="impl">TBD</param>
        public ActorProcessor(IActorRef impl) : base(impl)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public void OnNext(TIn element) => OnNext((object)element);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscription">TBD</param>
        public void OnSubscribe(ISubscription subscription)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
            Impl.Tell(new OnSubscribe(subscription));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public void OnNext(object element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            Impl.Tell(new OnNext(element));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        public void OnError(Exception cause)
        {
            ReactiveStreamsCompliance.RequireNonNullException(cause);
            Impl.Tell(new OnError(cause));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void OnComplete() => Impl.Tell(Actors.OnComplete.Instance);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract class BatchingInputBuffer : IInputs
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int Count;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IPump Pump;

        private readonly object[] _inputBuffer;
        private readonly int _indexMask;
        private ISubscription _upstream;
        private int _inputBufferElements;
        private int _nextInputElementCursor;
        private bool _isUpstreamCompleted;
        private int _batchRemaining;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="count">TBD</param>
        /// <param name="pump">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="count"/> is either less than or equal to zero or is not a power of two.
        /// </exception>
        protected BatchingInputBuffer(int count, IPump pump)
        {
            if (count <= 0) throw new ArgumentException("Buffer Count must be > 0", nameof(count));
            if ((count & (count - 1)) != 0) throw new ArgumentException("Buffer Count must be power of two", nameof(count));
            // TODO: buffer and batch sizing heuristics

            Count = count;
            Pump = pump;

            _indexMask = count - 1;
            _inputBuffer = new object[count];
            _batchRemaining = RequestBatchSize;
            SubReceive = new SubReceive(WaitingForUpstream);

            NeedsInput = DefaultInputTransferStates.NeedsInput(this);
            NeedsInputOrComplete = DefaultInputTransferStates.NeedsInputOrComplete(this);
        }

        private int RequestBatchSize => Math.Max(1, _inputBuffer.Length / 2);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"BatchingInputBuffer(Count={Count}, elems={_inputBufferElements}, completed={_isUpstreamCompleted}, remaining={_batchRemaining})";

        /// <summary>
        /// TBD
        /// </summary>
        public virtual SubReceive SubReceive { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <exception cref="IllegalStateException">TBD</exception>
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

        /// <summary>
        /// TBD
        /// </summary>
        public virtual void Cancel()
        {
            if (!_isUpstreamCompleted)
            {
                _isUpstreamCompleted = true;
                if (!ReferenceEquals(_upstream, null))
                    _upstream.Cancel();
                Clear();
            }
        }

        private void Clear()
        {
            _inputBuffer.Initialize();
            _inputBufferElements = 0;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public TransferState NeedsInput { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TransferState NeedsInputOrComplete { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsClosed => _isUpstreamCompleted;
        /// <summary>
        /// TBD
        /// </summary>
        public bool IsOpen => !IsClosed;
        /// <summary>
        /// TBD
        /// </summary>
        public bool AreInputsDepleted => _isUpstreamCompleted && _inputBufferElements == 0;
        /// <summary>
        /// TBD
        /// </summary>
        public bool AreInputsAvailable => _inputBufferElements > 0;

        /// <summary>
        /// TBD
        /// </summary>
        protected virtual void OnComplete()
        {
            _isUpstreamCompleted = true;
            SubReceive.Become(Completed);
            Pump.Pump();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscription">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="subscription"/> is undefined.
        /// </exception>
        protected virtual void OnSubscribe(ISubscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription), "OnSubscribe require subscription not to be null");

            if (_isUpstreamCompleted)
                subscription.Cancel();
            else
            {
                _upstream = subscription;
                // prefetch
                _upstream.Request(_inputBuffer.Length);
                SubReceive.Become(UpstreamRunning);
            }

            Pump.GotUpstreamSubscription();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        protected virtual void OnError(Exception e)
        {
            _isUpstreamCompleted = true;
            SubReceive.Become(Completed);
            InputOnError(e);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected virtual bool WaitingForUpstream(object message)
        {
            if (message is OnComplete)
                OnComplete();
            else if (message is OnSubscribe)
                OnSubscribe(((OnSubscribe)message).Subscription);
            else if (message is OnError)
                OnError(((OnError)message).Cause);
            else
                return false;
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected virtual bool UpstreamRunning(object message)
        {
            if (message is OnNext)
                EnqueueInputElement(((OnNext)message).Element);
            else if (message is OnComplete)
                OnComplete();
            else if (message is OnSubscribe)
                ((OnSubscribe)message).Subscription.Cancel();
            else if (message is OnError)
                OnError(((OnError)message).Cause);
            else
                return false;
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <exception cref="IllegalStateException">TBD</exception>
        /// <returns>TBD</returns>
        protected virtual bool Completed(object message)
        {
            if (message is OnSubscribe)
                throw new IllegalStateException("OnSubscribe called after OnError or OnComplete");
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        protected virtual void InputOnError(Exception e) => Clear();
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class SimpleOutputs : IOutputs
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IActorRef Actor;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IPump Pump;

        /// <summary>
        /// TBD
        /// </summary>
        protected IActorPublisher ExposedPublisher;
        /// <summary>
        /// TBD
        /// </summary>
        protected IUntypedSubscriber Subscriber;
        /// <summary>
        /// TBD
        /// </summary>
        protected long DownstreamDemand;
        /// <summary>
        /// TBD
        /// </summary>
        protected bool IsDownstreamCompleted;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="pump">TBD</param>
        public SimpleOutputs(IActorRef actor, IPump pump)
        {
            Actor = actor;
            Pump = pump;

            SubReceive = new SubReceive(WaitingExposedPublisher);
            NeedsDemand = DefaultOutputTransferStates.NeedsDemand(this);
            NeedsDemandOrCancel = DefaultOutputTransferStates.NeedsDemandOrCancel(this);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsSubscribed => Subscriber != null;

        /// <summary>
        /// TBD
        /// </summary>
        public virtual SubReceive SubReceive { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public TransferState NeedsDemand { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public TransferState NeedsDemandOrCancel { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public long DemandCount => DownstreamDemand;
        /// <summary>
        /// TBD
        /// </summary>
        public bool IsDemandAvailable => DownstreamDemand > 0;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public void EnqueueOutputElement(object element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            DownstreamDemand--;
            ReactiveStreamsCompliance.TryOnNext(Subscriber, element);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public virtual void Complete()
        {
            if (!IsDownstreamCompleted)
            {
                IsDownstreamCompleted = true;
                if (!ReferenceEquals(ExposedPublisher, null))
                    ExposedPublisher.Shutdown(null);
                if (!ReferenceEquals(Subscriber, null))
                    ReactiveStreamsCompliance.TryOnComplete(Subscriber);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public virtual void Cancel()
        {
            if (!IsDownstreamCompleted)
            {
                IsDownstreamCompleted = true;
                if (!ReferenceEquals(ExposedPublisher, null))
                    ExposedPublisher.Shutdown(null);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        public virtual void Error(Exception e)
        {
            if (!IsDownstreamCompleted)
            {
                IsDownstreamCompleted = true;
                if (!ReferenceEquals(ExposedPublisher, null))
                    ExposedPublisher.Shutdown(e);
                if (!ReferenceEquals(Subscriber, null) && !(e is ISpecViolation))
                    ReactiveStreamsCompliance.TryOnError(Subscriber, e);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsClosed => IsDownstreamCompleted && !ReferenceEquals(Subscriber, null);
        /// <summary>
        /// TBD
        /// </summary>
        public bool IsOpen => !IsClosed;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        protected ISubscription CreateSubscription() => ActorSubscription.Create(Actor, Subscriber);

        private void SubscribePending(IEnumerable<IUntypedSubscriber> subscribers)
        {
            foreach (var subscriber in subscribers)
            {
                if (ReferenceEquals(Subscriber, null))
                {
                    Subscriber = subscriber;
                    ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CreateSubscription());
                }
                else
                    ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, GetType().Name);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <exception cref="IllegalStateException">TBD</exception>
        /// <returns>TBD</returns>
        protected bool WaitingExposedPublisher(object message)
        {
            if (message is ExposedPublisher)
            {
                ExposedPublisher = ((ExposedPublisher)message).Publisher;
                SubReceive.Become(DownstreamRunning);
                return true;
            }
            throw new IllegalStateException(
                $"The first message must be [{typeof (ExposedPublisher)}] but was [{message}]");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected bool DownstreamRunning(object message)
        {
            if (message is SubscribePending)
                SubscribePending(ExposedPublisher.TakePendingSubscribers());
            else if (message is RequestMore)
            {
                var requestMore = (RequestMore)message;
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
            else
                return false;
            return true;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal abstract class ActorProcessorImpl : ActorBase, IPump
    {
        #region Internal classes

        private sealed class InternalBatchingInputBuffer : BatchingInputBuffer
        {
            private readonly ActorProcessorImpl _impl;
            public InternalBatchingInputBuffer(int count, ActorProcessorImpl impl) : base(count, impl)
            {
                _impl = impl;
            }

            protected override void InputOnError(Exception e) => _impl.OnError(e);
        }

        private sealed class InternalExposedPublisherReceive : ExposedPublisherReceive
        {
            private readonly ActorProcessorImpl _self;
            public InternalExposedPublisherReceive(Receive activeReceive, Action<object> unhandled, ActorProcessorImpl self) : base(activeReceive, unhandled)
            {
                _self = self;
            }

            internal override void ReceiveExposedPublisher(ExposedPublisher publisher)
            {
                _self.PrimaryOutputs.SubReceive.CurrentReceive(publisher);
                Context.Become(ActiveReceive);
            }
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        public readonly ActorMaterializerSettings Settings;

        /// <summary>
        /// TBD
        /// </summary>
        protected virtual IInputs PrimaryInputs { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected virtual IOutputs PrimaryOutputs { get; }

        private ILoggingAdapter _log;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        protected ActorProcessorImpl(ActorMaterializerSettings settings)
        {
            Settings = settings;

            PrimaryInputs = new InternalBatchingInputBuffer(settings.InitialInputBufferSize, this);
            PrimaryOutputs = new SimpleOutputs(Self, this);

            _receive = new InternalExposedPublisherReceive(ActiveReceive, Unhandled, this);
            this.Init();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        /// <summary>
        /// TBD
        /// </summary>
        public TransferState TransferState { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public Action CurrentAction { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public bool IsPumpFinished => this.IsPumpFinished();

        private readonly ExposedPublisherReceive _receive;

        /// <summary>
        /// Subclass may override <see cref="ActiveReceive"/>
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected sealed override bool Receive(object message) => _receive.Apply(message);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected virtual bool ActiveReceive(object message)
            => PrimaryInputs.SubReceive.CurrentReceive(message) || PrimaryOutputs.SubReceive.CurrentReceive(message);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="waitForUpstream">TBD</param>
        /// <param name="andThen">TBD</param>
        public void InitialPhase(int waitForUpstream, TransferPhase andThen)
            => Pumps.InitialPhase(this, waitForUpstream, andThen);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="waitForUpstream">TBD</param>
        public void WaitForUpstream(int waitForUpstream) => Pumps.WaitForUpstream(this, waitForUpstream);

        /// <summary>
        /// TBD
        /// </summary>
        public void GotUpstreamSubscription() => Pumps.GotUpstreamSubscription(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="phase">TBD</param>
        public void NextPhase(TransferPhase phase) => Pumps.NextPhase(this, phase);

        /// <summary>
        /// TBD
        /// </summary>
        public void Pump() => Pumps.Pump(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        public void PumpFailed(Exception e) => Fail(e);

        /// <summary>
        /// TBD
        /// </summary>
        public virtual void PumpFinished()
        {
            PrimaryInputs.Cancel();
            PrimaryOutputs.Complete();
            Context.Stop(Self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        protected virtual void OnError(Exception e) => Fail(e);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        protected virtual void Fail(Exception e)
        {
            if (Settings.IsDebugLogging)
                Log.Debug("Failed due to: {0}", e.Message);

            PrimaryInputs.Cancel();
            PrimaryOutputs.Error(e);
            Context.Stop(Self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            PrimaryInputs.Cancel();
            PrimaryOutputs.Error(new AbruptTerminationException(Self));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <exception cref="IllegalStateException">TBD</exception>
        protected override void PostRestart(Exception reason)
        {
            base.PostRestart(reason);
            throw new IllegalStateException("This actor cannot be restarted", reason);
        }
    }
}
