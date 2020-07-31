//-----------------------------------------------------------------------
// <copyright file="FanoutProcessorImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Pattern;
using Akka.Util.Internal;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="TStreamBuffer">TBD</typeparam>
    internal class FanoutOutputs<T, TStreamBuffer> : SubscriberManagement<T, TStreamBuffer>, IOutputs where TStreamBuffer : IStreamBuffer<T>
    {
        private long _downstreamBufferSpace;
        private bool _downstreamCompleted;
        private readonly IActorRef _self;
        private readonly IPump _pump;
        private readonly Action _afterShutdown;

        /// <summary>
        /// TBD
        /// </summary>
        protected IActorPublisher ExposedPublisher;

        /// <summary>
        /// TBD
        /// </summary>
        public SubReceive SubReceive { get; }

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
        public bool IsDemandAvailable => _downstreamBufferSpace > 0;

        /// <summary>
        /// TBD
        /// </summary>
        public long DemandCount => _downstreamBufferSpace;

        /// <summary>
        /// TBD
        /// </summary>
        public override int InitialBufferSize { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override int MaxBufferSize { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="maxBufferSize">TBD</param>
        /// <param name="initialBufferSize">TBD</param>
        /// <param name="self">TBD</param>
        /// <param name="pump">TBD</param>
        /// <param name="afterShutdown">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        /// <returns>TBD</returns>
        protected override ISubscriptionWithCursor<T> CreateSubscription(ISubscriber<T> subscriber)
            => new ActorSubscriptionWithCursor<T>(_self, subscriber);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <param name="elements">TBD</param>
        protected override void RequestFromUpstream(long elements) => _downstreamBufferSpace += elements;

        private void SubscribePending()
            =>
                ExposedPublisher.TakePendingSubscribers()
                    .ForEach(s => RegisterSubscriber(UntypedSubscriber.ToTyped<T>(s)));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="isCompleted">TBD</param>
        protected override void Shutdown(bool isCompleted)
        {
            ExposedPublisher?.Shutdown(isCompleted ? null : ActorPublisher.NormalShutdownReason);

            _afterShutdown?.Invoke();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void CancelUpstream() => _downstreamCompleted = true;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public void EnqueueOutputElement(object element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            _downstreamBufferSpace -= 1;
            PushToDownstream((T) element);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Complete()
        {
            if (_downstreamCompleted)
                return;

            _downstreamCompleted = true;
            CompleteDownstream();
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Cancel() => Complete();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        public void Error(Exception e)
        {
            if (_downstreamCompleted)
                return;

            _downstreamCompleted = true;
            AbortDownstream(e);

            ExposedPublisher?.Shutdown(e);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsClosed => _downstreamCompleted;

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsOpen => !IsClosed;
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="TStreamBuffer">TBD</typeparam>
    internal sealed class FanoutProcessorImpl<T, TStreamBuffer> : ActorProcessorImpl where TStreamBuffer : IStreamBuffer<T>
    {
        private readonly Action _onTerminated;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        /// <param name="onTerminated">TBD</param>
        /// <returns>TBD</returns>
        public static Props Props(ActorMaterializerSettings settings, Action onTerminated = null)
            => Actor.Props.Create(() => new FanoutProcessorImpl<T, TStreamBuffer>(settings, onTerminated)).WithDeploy(Deploy.Local);

        /// <summary>
        /// TBD
        /// </summary>
        protected override IOutputs PrimaryOutputs { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        /// <param name="onTerminated">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        protected override void Fail(Exception e)
        {
            if (Settings.IsDebugLogging)
                Log.Debug("Failed due to: {0}", e.Message);

            PrimaryInputs.Cancel();
            PrimaryOutputs.Error(e);
            // Stopping will happen after flush
        }

        /// <summary>
        /// TBD
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
