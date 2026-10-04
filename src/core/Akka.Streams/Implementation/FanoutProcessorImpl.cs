//-----------------------------------------------------------------------
// <copyright file="FanoutProcessorImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Util.Internal;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// Manages actor-backed fan-out output subscriptions and downstream demand for a processor.
    /// </summary>
    /// <typeparam name="T">The element type distributed to subscribers.</typeparam>
    /// <typeparam name="TStreamBuffer">The buffer implementation retaining elements for subscribers.</typeparam>
    internal class FanoutOutputs<T, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStreamBuffer> : SubscriberManagement<T, TStreamBuffer>, IOutputs where TStreamBuffer : IStreamBuffer<T>
    {
        private long _downstreamBufferSpace;
        private bool _downstreamCompleted;
        private readonly IActorRef _self;
        private readonly IPump _pump;
        private readonly Action _afterShutdown;

        /// <summary>
        /// Gets the exposed actor publisher used to accept downstream subscriptions.
        /// </summary>
        protected IActorPublisher ExposedPublisher;

        /// <summary>
        /// Gets the actor message handler for downstream publisher and subscription messages.
        /// </summary>
        public SubReceive SubReceive { get; }

        /// <summary>
        /// Gets the transfer state that waits for downstream buffer space.
        /// </summary>
        public TransferState NeedsDemand { get; }

        /// <summary>
        /// Gets the transfer state that is ready when space is available or output closes.
        /// </summary>
        public TransferState NeedsDemandOrCancel { get; }

        /// <summary>
        /// Gets whether output buffer space has been requested.
        /// </summary>
        public bool IsDemandAvailable => _downstreamBufferSpace > 0;

        /// <summary>
        /// Gets the number of downstream elements that can be enqueued before more demand is needed.
        /// </summary>
        public long DemandCount => _downstreamBufferSpace;

        /// <summary>
        /// Gets the initial shared buffer capacity.
        /// </summary>
        public override int InitialBufferSize { get; }

        /// <summary>
        /// Gets the maximum shared buffer capacity.
        /// </summary>
        public override int MaxBufferSize { get; }

        /// <summary>
        /// Creates output management and waits for the publisher-exposure message before handling subscriptions.
        /// </summary>
        /// <param name="maxBufferSize">The maximum buffer capacity.</param>
        /// <param name="initialBufferSize">The initial buffer capacity.</param>
        /// <param name="self">The actor that owns the output manager.</param>
        /// <param name="pump">The transfer pump resumed after demand or cancellation changes.</param>
        /// <param name="afterShutdown">An optional callback invoked after output shutdown.</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the first message isn't of type <see cref="ExposedPublisher"/>.
        /// </exception>
        public FanoutOutputs(int maxBufferSize, int initialBufferSize, IActorRef self, IPump pump, Action afterShutdown = null)
        {
            _self = self;
            _pump = pump;
            _afterShutdown = afterShutdown;
            MaxBufferSize = maxBufferSize;
            InitialBufferSize = initialBufferSize;
            NeedsDemand = DefaultOutputTransferStates.NeedsDemand(this);
            NeedsDemandOrCancel = DefaultOutputTransferStates.NeedsDemandOrCancel(this);
            SubReceive = new SubReceive(message =>
            {
                if (!(message is ExposedPublisher publisher))
                    throw new IllegalStateException($"The first message must be ExposedPublisher but was {message}");

                ExposedPublisher = publisher.Publisher;
                SubReceive.Become(DownstreamRunning);
                return true;
            });
        }

        /// <summary>
        /// Creates an actor subscription with a cursor into the shared output buffer.
        /// </summary>
        /// <param name="subscriber">The subscriber receiving output elements.</param>
        /// <returns>The actor subscription tracked by this output manager.</returns>
        protected override ISubscriptionWithCursor<T> CreateSubscription(ISubscriber<T> subscriber)
            => new ActorSubscriptionWithCursor<T>(_self, subscriber);

        /// <summary>
        /// Handles subscription requests, demand, and cancellation after the publisher is exposed.
        /// </summary>
        /// <param name="message">The actor message to handle.</param>
        /// <returns><see langword="true"/> if the message was handled; otherwise <see langword="false"/>.</returns>
        protected bool DownstreamRunning(object message)
        {
            switch (message)
            {
                case SubscribePending _:
                    SubscribePending();
                    return true;
                case RequestMore requestMore:
                    MoreRequested((ActorSubscriptionWithCursor<T>) requestMore.Subscription, requestMore.Demand);
                    _pump.Pump();
                    return true;
                case Cancel cancel:
                    UnregisterSubscription((ActorSubscriptionWithCursor<T>) cancel.Subscription);
                    _pump.Pump();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Records downstream demand as capacity available for elements from the input side.
        /// </summary>
        /// <param name="elements">The additional downstream capacity requested.</param>
        protected override void RequestFromUpstream(long elements) => _downstreamBufferSpace += elements;

        private void SubscribePending()
            =>
                ExposedPublisher.TakePendingSubscribers()
                    .ForEach(s => RegisterSubscriber(UntypedSubscriber.ToTyped<T>(s)));

        /// <summary>
        /// Shuts down the exposed publisher and invokes the configured callback after subscriber management finishes.
        /// </summary>
        /// <param name="isCompleted"><see langword="true"/> when output completes normally; <see langword="false"/> when shutdown follows removal of the last subscription, including cancellation or draining after completion.</param>
        protected override void Shutdown(bool isCompleted)
        {
            ExposedPublisher?.Shutdown(isCompleted ? null : ActorPublisher.NormalShutdownReason);

            _afterShutdown?.Invoke();
        }

        /// <summary>
        /// Marks downstream output closed when the last subscriber cancels.
        /// </summary>
        protected override void CancelUpstream() => _downstreamCompleted = true;

        /// <summary>
        /// Validates and enqueues one output element for registered subscribers.
        /// </summary>
        /// <param name="element">The element to distribute.</param>
        public void EnqueueOutputElement(object element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            _downstreamBufferSpace -= 1;
            PushToDownstream((T) element);
        }

        /// <summary>
        /// Completes output and delivers completion after each subscriber drains retained elements.
        /// </summary>
        public void Complete()
        {
            if (_downstreamCompleted)
                return;

            _downstreamCompleted = true;
            CompleteDownstream();
        }

        /// <summary>
        /// Treats output cancellation as successful completion.
        /// </summary>
        public void Cancel() => Complete();

        /// <summary>
        /// Fails current subscribers and shuts down the exposed publisher.
        /// </summary>
        /// <param name="e">The failure delivered to subscribers.</param>
        public void Error(Exception e)
        {
            if (_downstreamCompleted)
                return;

            _downstreamCompleted = true;
            AbortDownstream(e);

            ExposedPublisher?.Shutdown(e);
        }

        /// <summary>
        /// Gets whether output has reached a terminal state through completion, cancellation, or failure.
        /// </summary>
        public bool IsClosed => _downstreamCompleted;

        /// <summary>
        /// Gets whether output is still open.
        /// </summary>
        public bool IsOpen => !IsClosed;
    }

    /// <summary>
    /// Actor processor implementation that distributes each input element to its active output subscribers.
    /// </summary>
    /// <typeparam name="T">The element type passed through the processor.</typeparam>
    /// <typeparam name="TStreamBuffer">The buffer implementation retaining elements for subscribers.</typeparam>
    internal sealed class FanoutProcessorImpl<T, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStreamBuffer> : ActorProcessorImpl where TStreamBuffer : IStreamBuffer<T>
    {
        private readonly Action _onTerminated;

        /// <summary>
        /// Creates local actor properties for the fan-out processor.
        /// </summary>
        /// <param name="settings">Materializer settings for input/output buffering.</param>
        /// <param name="onTerminated">An optional callback invoked during subscriber-management shutdown before a stop request; it is not invoked on the error path.</param>
        /// <returns>Actor properties for creating the processor.</returns>
        public static Props Props(ActorMaterializerSettings settings, Action onTerminated = null)
            => Actor.Props.Create<FanoutProcessorImpl<T, TStreamBuffer>>(settings, onTerminated).WithDeploy(Deploy.Local);

        /// <summary>
        /// Gets the output manager used by the transfer pump.
        /// </summary>
        protected override IOutputs PrimaryOutputs { get; }

        /// <summary>
        /// Creates a processor with the materializer's buffer settings and an optional shutdown callback.
        /// </summary>
        /// <param name="settings">Materializer settings for input/output buffering.</param>
        /// <param name="onTerminated">An optional callback invoked during subscriber-management shutdown before a stop request; it is not invoked on the error path.</param>
        /// If this gets changed you must change <see cref="FanoutProcessorImpl{T,TStreamBuffer}.Props"/> as well!
        public FanoutProcessorImpl(ActorMaterializerSettings settings, Action onTerminated) : base(settings)
        {
            PrimaryOutputs = new FanoutOutputs<T, TStreamBuffer>(settings.MaxInputBufferSize,
                settings.InitialInputBufferSize, Self, this, AfterFlush);

            _onTerminated = onTerminated;

            var running = new TransferPhase(PrimaryInputs.NeedsInput.And(PrimaryOutputs.NeedsDemand),
                () => PrimaryOutputs.EnqueueOutputElement(PrimaryInputs.DequeueInputElement()));
            InitialPhase(1, running);
        }

        /// <summary>
        /// Fails the processor by canceling input and signaling an error to output subscribers.
        /// </summary>
        /// <param name="e">The failure to signal to downstream subscribers.</param>
        protected override void Fail(Exception e)
        {
            if (Settings.IsDebugLogging)
                Log.Debug("Failed due to: {0}", e.Message);

            PrimaryInputs.Cancel();
            PrimaryOutputs.Error(e);
            // Stopping will happen after flush
        }

        /// <summary>
        /// Completes the processor after canceling its upstream input.
        /// </summary>
        public override void PumpFinished()
        {
            PrimaryInputs.Cancel();
            PrimaryOutputs.Complete();
        }

        private void AfterFlush()
        {
            _onTerminated?.Invoke();
            Context.Stop(Self);
        }
    }
}
