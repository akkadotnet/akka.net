//-----------------------------------------------------------------------
// <copyright file="ActorProcessor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// Creates a processor facade and sends its publisher endpoint to the implementing actor.
    /// </summary>
    internal static class ActorProcessor
    {
        /// <summary>
        /// Creates a typed actor processor and exposes its publisher to the actor before returning.
        /// </summary>
        /// <typeparam name="TIn">The element type accepted from upstream.</typeparam>
        /// <typeparam name="TOut">The element type emitted to downstream.</typeparam>
        /// <param name="impl">The actor implementing the processor protocol.</param>
        /// <returns>A processor that forwards Reactive Streams signals to <paramref name="impl"/>.</returns>
        public static ActorProcessor<TIn, TOut> Create<TIn, TOut>(IActorRef impl)
        {
            var p = new ActorProcessor<TIn, TOut>(impl);
            // Resolve cyclic dependency with actor. This MUST be the first message no matter what.
            impl.Tell(new ExposedPublisher(p));
            return p;
        }
    }

    /// <summary>
    /// Adapts Reactive Streams processor callbacks to messages sent to an actor.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted from upstream.</typeparam>
    /// <typeparam name="TOut">The element type emitted to downstream.</typeparam>
    internal sealed class ActorProcessor<TIn, TOut> : ActorPublisher<TOut>, IProcessor<TIn, TOut>
    {
        /// <summary>
        /// Creates a processor adapter for the implementing actor.
        /// </summary>
        /// <param name="impl">The actor that handles processor and publisher messages.</param>
        public ActorProcessor(IActorRef impl) : base(impl)
        {
        }

        /// <summary>
        /// Forwards an upstream element to the implementing actor.
        /// </summary>
        /// <param name="element">The element received from upstream.</param>
        public void OnNext(TIn element) => OnNext((object)element);

        /// <summary>
        /// Forwards the upstream subscription to the implementing actor.
        /// </summary>
        /// <param name="subscription">The upstream subscription.</param>
        public void OnSubscribe(ISubscription subscription)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
            Impl.Tell(new OnSubscribe(subscription));
        }

        /// <summary>
        /// Validates and forwards an untyped upstream element to the implementing actor.
        /// </summary>
        /// <param name="element">The element received from upstream.</param>
        public void OnNext(object element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            Impl.Tell(new OnNext(element));
        }

        /// <summary>
        /// Validates and forwards upstream failure to the implementing actor.
        /// </summary>
        /// <param name="cause">The upstream failure.</param>
        public void OnError(Exception cause)
        {
            ReactiveStreamsCompliance.RequireNonNullException(cause);
            Impl.Tell(new OnError(cause));
        }

        /// <summary>
        /// Forwards upstream completion to the implementing actor.
        /// </summary>
        public void OnComplete() => Impl.Tell(Actors.OnComplete.Instance);
    }

    /// <summary>
    /// Buffers upstream elements and replenishes demand in batches for an actor processor.
    /// </summary>
    public abstract class BatchingInputBuffer : IInputs
    {
        /// <summary>
        /// Gets the ring-buffer capacity, which must be a positive power of two.
        /// </summary>
        public readonly int Count;
        /// <summary>
        /// Gets the transfer pump resumed when input state changes.
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
        /// Creates an input buffer that prefetches input and requests more after consuming a batch.
        /// </summary>
        /// <param name="count">The buffer capacity; it must be a positive power of two.</param>
        /// <param name="pump">The pump resumed after input changes.</param>
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
        /// Returns the buffer capacity, queued element count, and upstream state for diagnostics.
        /// </summary>
        /// <returns>A diagnostic representation of the input buffer state.</returns>
        public override string ToString() => $"BatchingInputBuffer(Count={Count}, elems={_inputBufferElements}, completed={_isUpstreamCompleted}, remaining={_batchRemaining})";

        /// <summary>
        /// Gets the receive handler for upstream signals.
        /// </summary>
        public virtual SubReceive SubReceive { get; }

        /// <summary>
        /// Removes and returns the next buffered input element, requesting another batch after the current batch is consumed.
        /// </summary>
        /// <returns>The next queued input element.</returns>
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
        /// Adds an upstream element to the buffer and resumes the pump.
        /// </summary>
        /// <param name="element">The element received from upstream.</param>
        /// <exception cref="IllegalStateException">The input buffer is already full.</exception>
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
        /// Cancels upstream input and discards any buffered elements.
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
        /// Gets the transfer state that waits for input and completes when upstream is closed and the buffer is drained.
        /// </summary>
        public TransferState NeedsInput { get; }

        /// <summary>
        /// Gets the transfer state that is ready for input or completion.
        /// </summary>
        public TransferState NeedsInputOrComplete { get; }

        /// <summary>
        /// Gets whether upstream has completed or been canceled.
        /// </summary>
        public bool IsClosed => _isUpstreamCompleted;
        /// <summary>
        /// Gets whether upstream input remains open.
        /// </summary>
        public bool IsOpen => !IsClosed;
        /// <summary>
        /// Gets whether upstream has completed and all buffered input has been consumed.
        /// </summary>
        public bool AreInputsDepleted => _isUpstreamCompleted && _inputBufferElements == 0;
        /// <summary>
        /// Gets whether at least one buffered input element is available.
        /// </summary>
        public bool AreInputsAvailable => _inputBufferElements > 0;

        /// <summary>
        /// Marks upstream complete, switches to the completed receive handler, and resumes the pump.
        /// </summary>
        protected virtual void OnComplete()
        {
            _isUpstreamCompleted = true;
            SubReceive.Become(Completed);
            Pump.Pump();
        }

        /// <summary>
        /// Registers an upstream subscription, prefetches up to the buffer capacity, and resumes the pump.
        /// </summary>
        /// <param name="subscription">The upstream subscription to register.</param>
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
        /// Marks upstream complete and forwards its failure to the processor implementation.
        /// </summary>
        /// <param name="e">The upstream failure.</param>
        protected virtual void OnError(Exception e)
        {
            _isUpstreamCompleted = true;
            SubReceive.Become(Completed);
            InputOnError(e);
        }

        /// <summary>
        /// Handles upstream protocol messages before its subscription is established.
        /// </summary>
        /// <param name="message">The actor message to handle.</param>
        /// <returns><see langword="true"/> for a recognized upstream signal; otherwise <see langword="false"/>.</returns>
        protected virtual bool WaitingForUpstream(object message)
        {
            if (message is OnComplete)
                OnComplete();
            else if (message is OnSubscribe subscribe)
                OnSubscribe(subscribe.Subscription);
            else if (message is OnError error)
                OnError(error.Cause);
            else
                return false;
            return true;
        }

        /// <summary>
        /// Handles upstream elements and terminal signals after subscription.
        /// </summary>
        /// <param name="message">The actor message to handle.</param>
        /// <returns><see langword="true"/> for a recognized upstream signal; otherwise <see langword="false"/>.</returns>
        protected virtual bool UpstreamRunning(object message)
        {
            if (message is OnNext next)
                EnqueueInputElement(next.Element);
            else if (message is OnComplete)
                OnComplete();
            else if (message is OnSubscribe subscribe)
                subscribe.Subscription.Cancel();
            else if (message is OnError error)
                OnError(error.Cause);
            else
                return false;
            return true;
        }

        /// <summary>
        /// Rejects a late subscription after upstream has terminated.
        /// </summary>
        /// <param name="message">The actor message to inspect.</param>
        /// <exception cref="IllegalStateException">An upstream subscription arrives after completion or failure.</exception>
        /// <returns><see langword="false"/> for messages not handled by this terminal state.</returns>
        protected virtual bool Completed(object message)
        {
            if (message is OnSubscribe)
                throw new IllegalStateException("OnSubscribe called after OnError or OnComplete");
            return false;
        }

        /// <summary>
        /// Clears buffered elements after upstream failure.
        /// </summary>
        /// <param name="e">The upstream failure.</param>
        protected virtual void InputOnError(Exception e) => Clear();
    }

    /// <summary>
    /// Handles a single downstream subscriber, demand, and terminal signals for an actor processor.
    /// </summary>
    public class SimpleOutputs : IOutputs
    {
        /// <summary>
        /// Gets the actor that receives output requests and cancellation.
        /// </summary>
        public readonly IActorRef Actor;
        /// <summary>
        /// Gets the transfer pump resumed when downstream state changes.
        /// </summary>
        public readonly IPump Pump;

        /// <summary>
        /// Gets the exposed actor publisher used to accept downstream subscriptions.
        /// </summary>
        protected IActorPublisher ExposedPublisher;
        /// <summary>
        /// Gets the downstream subscriber after registration.
        /// </summary>
        protected IUntypedSubscriber Subscriber;
        /// <summary>
        /// Gets the amount of outstanding downstream demand.
        /// </summary>
        protected long DownstreamDemand;
        /// <summary>
        /// Gets whether downstream has completed or canceled.
        /// </summary>
        protected bool IsDownstreamCompleted;

        /// <summary>
        /// Creates output management and waits for the publisher exposure message.
        /// </summary>
        /// <param name="actor">The actor that receives subscription requests.</param>
        /// <param name="pump">The transfer pump resumed after demand changes.</param>
        public SimpleOutputs(IActorRef actor, IPump pump)
        {
            Actor = actor;
            Pump = pump;

            SubReceive = new SubReceive(WaitingExposedPublisher);
            NeedsDemand = DefaultOutputTransferStates.NeedsDemand(this);
            NeedsDemandOrCancel = DefaultOutputTransferStates.NeedsDemandOrCancel(this);
        }

        /// <summary>
        /// Gets whether a downstream subscriber has been registered.
        /// </summary>
        public bool IsSubscribed => Subscriber != null;

        /// <summary>
        /// Gets the receive handler for downstream signals.
        /// </summary>
        public virtual SubReceive SubReceive { get; }
        /// <summary>
        /// Gets the transfer state that waits for positive downstream demand.
        /// </summary>
        public TransferState NeedsDemand { get; }
        /// <summary>
        /// Gets the transfer state that is ready when demand arrives or downstream closes.
        /// </summary>
        public TransferState NeedsDemandOrCancel { get; }
        /// <summary>
        /// Gets the remaining downstream demand.
        /// </summary>
        public long DemandCount => DownstreamDemand;
        /// <summary>
        /// Gets whether downstream demand is available.
        /// </summary>
        public bool IsDemandAvailable => DownstreamDemand > 0;

        /// <summary>
        /// Validates and sends an output element to the subscriber, decrementing outstanding demand.
        /// </summary>
        /// <param name="element">The element to deliver to downstream.</param>
        public void EnqueueOutputElement(object element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            DownstreamDemand--;
            ReactiveStreamsCompliance.TryOnNext(Subscriber, element);
        }

        /// <summary>
        /// Completes the publisher and signals completion to its registered subscriber.
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
        /// Cancels the publisher without sending a downstream terminal signal.
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
        /// Fails the publisher and signals the cause to its registered subscriber when allowed by compliance rules.
        /// </summary>
        /// <param name="e">The failure to signal.</param>
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
        /// Gets whether downstream is complete and a subscriber is present.
        /// </summary>
        public bool IsClosed => IsDownstreamCompleted && !ReferenceEquals(Subscriber, null);
        /// <summary>
        /// Gets whether the output side has not reached its closed state.
        /// </summary>
        public bool IsOpen => !IsClosed;

        /// <summary>
        /// Creates an actor-backed subscription for the registered subscriber.
        /// </summary>
        /// <returns>The subscription used to deliver request and cancellation messages.</returns>
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
        /// Waits for the publisher wrapper, then registers pending subscribers with this output manager.
        /// </summary>
        /// <param name="message">The actor message to process.</param>
        /// <exception cref="IllegalStateException">The first received message is not an exposed publisher.</exception>
        /// <returns><see langword="true"/> after handling the exposure message.</returns>
        protected bool WaitingExposedPublisher(object message)
        {
            if (message is ExposedPublisher publisher)
            {
                ExposedPublisher = publisher.Publisher;
                SubReceive.Become(DownstreamRunning);
                return true;
            }
            throw new IllegalStateException(
                $"The first message must be [{typeof (ExposedPublisher)}] but was [{message}]");
        }

        /// <summary>
        /// Handles publisher wake-ups, demand requests, and subscriber cancellation.
        /// </summary>
        /// <param name="message">The actor message to process.</param>
        /// <returns><see langword="true"/> when the message is handled; otherwise <see langword="false"/>.</returns>
        protected bool DownstreamRunning(object message)
        {
            if (message is SubscribePending)
                SubscribePending(ExposedPublisher.TakePendingSubscribers());
            else if (message is RequestMore requestMore)
            {
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
    /// Base actor implementation for an actor-backed Reactive Streams processor.
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
        /// Gets the settings used to configure input buffering and processor logging.
        /// </summary>
        public readonly ActorMaterializerSettings Settings;

        /// <summary>
        /// Gets the primary input buffer and upstream-signal handler.
        /// </summary>
        protected virtual IInputs PrimaryInputs { get; }
        /// <summary>
        /// Gets the primary output manager and downstream-signal handler.
        /// </summary>
        protected virtual IOutputs PrimaryOutputs { get; }

        private ILoggingAdapter _log;

        /// <summary>
        /// Creates the processor actor's default input buffer, output manager, and initial pump state.
        /// </summary>
        /// <param name="settings">Materializer settings used for buffering and logging.</param>
        protected ActorProcessorImpl(ActorMaterializerSettings settings)
        {
            Settings = settings;

            PrimaryInputs = new InternalBatchingInputBuffer(settings.InitialInputBufferSize, this);
            PrimaryOutputs = new SimpleOutputs(Self, this);

            _receive = new InternalExposedPublisherReceive(ActiveReceive, Unhandled, this);
            this.Init();
        }

        /// <summary>
        /// Gets the actor logger, creating it on first access.
        /// </summary>
        protected ILoggingAdapter Log => _log ??= Context.GetLogger();

        /// <summary>
        /// Gets or sets the current pump transfer state.
        /// </summary>
        public TransferState TransferState { get; set; }
        /// <summary>
        /// Gets or sets the current pump action.
        /// </summary>
        public Action CurrentAction { get; set; }
        /// <summary>
        /// Gets whether the current transfer state is completed.
        /// </summary>
        public bool IsPumpFinished => this.IsPumpFinished();

        private readonly ExposedPublisherReceive _receive;

        /// <summary>
        /// Subclass may override <see cref="ActiveReceive"/>
        /// </summary>
        /// <param name="message">The actor message to route to the active receive handler.</param>
        /// <returns><see langword="true"/> if handled; otherwise the configured unhandled behavior applies.</returns>
        protected sealed override bool Receive(object message) => _receive.Apply(message);

        /// <summary>
        /// Dispatches actor messages to the active input or output receive handler.
        /// </summary>
        /// <param name="message">The actor message to route.</param>
        /// <returns><see langword="true"/> if either side handled the message.</returns>
        protected virtual bool ActiveReceive(object message)
            => PrimaryInputs.SubReceive.CurrentReceive(message) || PrimaryOutputs.SubReceive.CurrentReceive(message);

        /// <summary>
        /// Configures the pump's initial phase, waiting for the specified upstream subscriptions if needed.
        /// </summary>
        /// <param name="waitForUpstream">The number of subscriptions required before running the phase.</param>
        /// <param name="andThen">The phase to run after subscriptions arrive.</param>
        public void InitialPhase(int waitForUpstream, TransferPhase andThen)
            => Pumps.InitialPhase(this, waitForUpstream, andThen);

        /// <summary>
        /// Pauses the current pump phase until upstream subscriptions arrive.
        /// </summary>
        /// <param name="waitForUpstream">The number of subscriptions to wait for.</param>
        public void WaitForUpstream(int waitForUpstream) => Pumps.WaitForUpstream(this, waitForUpstream);

        /// <summary>
        /// Notifies the pump that an upstream subscription arrived.
        /// </summary>
        public void GotUpstreamSubscription() => Pumps.GotUpstreamSubscription(this);

        /// <summary>
        /// Installs the next pump phase.
        /// </summary>
        /// <param name="phase">The phase to install.</param>
        public void NextPhase(TransferPhase phase) => Pumps.NextPhase(this, phase);

        /// <summary>
        /// Runs the current pump action while its state is executable.
        /// </summary>
        public void Pump() => Pumps.Pump(this);

        /// <summary>
        /// Routes pump action failures to the actor's failure handler.
        /// </summary>
        /// <param name="e">The exception thrown by the current pump action.</param>
        public void PumpFailed(Exception e) => Fail(e);

        /// <summary>
        /// Cancels upstream and completes downstream when the pump reaches its terminal state.
        /// </summary>
        public virtual void PumpFinished()
        {
            PrimaryInputs.Cancel();
            PrimaryOutputs.Complete();
            Context.Stop(Self);
        }

        /// <summary>
        /// Fails the processor using the actor's failure path.
        /// </summary>
        /// <param name="e">The upstream failure.</param>
        protected virtual void OnError(Exception e) => Fail(e);

        /// <summary>
        /// Cancels input and signals the failure to output subscribers, then stops the actor.
        /// </summary>
        /// <param name="e">The failure to signal.</param>
        protected virtual void Fail(Exception e)
        {
            if (Settings.IsDebugLogging)
                Log.Debug("Failed due to: {0}", e.Message);

            PrimaryInputs.Cancel();
            PrimaryOutputs.Error(e);
            Context.Stop(Self);
        }

        /// <summary>
        /// Cancels input and reports abrupt actor termination to the output manager.
        /// </summary>
        protected override void PostStop()
        {
            PrimaryInputs.Cancel();
            PrimaryOutputs.Error(new AbruptTerminationException(Self));
        }

        /// <summary>
        /// Rejects actor restart because processor state cannot be reconstructed.
        /// </summary>
        /// <param name="reason">The exception that caused the restart attempt.</param>
        /// <exception cref="IllegalStateException">Restart is unsupported for this processor actor.</exception>
        protected override void PostRestart(Exception reason)
        {
            base.PostRestart(reason);
            throw new IllegalStateException("This actor cannot be restarted", reason);
        }
    }
}
