using System;
using System.Reactive.Streams;
using Akka.Actor;
using Akka.Pattern;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation
{
    internal class FanoutOutputs<T> : SubscriberManagement<T>, IOutputs
    {
        private long _downstreamBufferSpace;
        private bool _downstreamCompleted;
        private readonly IActorRef _self;
        private readonly IPump _pump;
        private readonly Action _afterShutdown;

        protected ActorPublisher<T> ExposedPublisher;

        public SubReceive SubReceive { get; }

        public TransferState NeedsDemand { get; }

        public TransferState NeedsDemandOrCancel { get; }
        public bool IsDemandAvailable => _downstreamBufferSpace > 0;

        public long DemandCount => _downstreamBufferSpace;

        public override int InitialBufferSize { get; }

        public override int MaxBufferSize { get; }

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
                var publisher = message as ExposedPublisher<T>;
                if (publisher == null)
                    throw new IllegalStateException($"The first message must be ExposedPublisher but was {message}");

                ExposedPublisher = publisher.Publisher;
                SubReceive.Become(DownstreamRunning);
                return true;
            });
        }

        protected override ISubscriptionWithCursor<T> CreateSubscription(ISubscriber<T> subscriber)
            => new ActorSubscriptionWithCursor<T>(_self, subscriber);

        protected bool DownstreamRunning(object message)
        {
            if (message is SubscribePending)
            {
                SubscribePending();
            }
            else if (message is RequestMore<T>)
            {
                var requestMore = (RequestMore<T>) message;
                MoreRequested((ActorSubscriptionWithCursor<T>)requestMore.Subscription, requestMore.Demand);
                _pump.Pump();
            }
            else if (message is Cancel<T>)
            {
                var cancel = (Cancel<T>) message;
                UnregisterSubscription((ActorSubscriptionWithCursor<T>) cancel.Subscription);
                _pump.Pump();
            }
            else return false;

            return true;
        }

        protected override void RequestFromUpstream(long elements) => _downstreamBufferSpace += elements;

        private void SubscribePending()
        {
            ExposedPublisher.TakePendingSubscribers().ForEach(RegisterSubscriber);
        }

        protected override void Shutdown(bool isCompleted)
        {
            ExposedPublisher?.Shutdown(isCompleted ? null : ActorPublisher<object>.NormalShutdownReason);

            _afterShutdown?.Invoke();
        }

        protected override void CancelUpstream() => _downstreamCompleted = true;

        public void EnqueueOutputElement(object element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            _downstreamBufferSpace -= 1;
            PushToDownstream((T)element);
        }

        public void Complete()
        {
            if (_downstreamCompleted)
                return;

            _downstreamCompleted = true;
            CompleteDownstream();
        }

        public void Cancel() => Complete();

        public void Error(Exception e)
        {
            if (_downstreamCompleted)
                return;

            _downstreamCompleted = true;
            AbortDownstream(e);

            ExposedPublisher?.Shutdown(e);
        }

        public bool IsClosed => _downstreamCompleted;

        public bool IsOpen => !IsClosed;
    }

    internal class FanoutProcessorImpl<T> : ActorProcessorImpl<T>
    {
        public static Props Props(ActorMaterializerSettings settings)
        {
            return Actor.Props.Create(() => new FanoutProcessorImpl<T>(settings)).WithDeploy(Deploy.Local);
        }

        protected override IOutputs PrimaryOutputs { get; }

        public FanoutProcessorImpl(ActorMaterializerSettings settings) : base(settings)
        {
            PrimaryOutputs = new FanoutOutputs<T>(settings.MaxInputBufferSize,
                settings.InitialInputBufferSize, Self, this, AfterFlush);

            var running = new TransferPhase(PrimaryInputs.NeedsInput.And(PrimaryOutputs.NeedsDemand),
                () => PrimaryOutputs.EnqueueOutputElement(PrimaryInputs.DequeueInputElement()));
            InitialPhase(1, running);
        }

        protected new void Fail(Exception e)
        {
            if (Settings.IsDebugLogging)
                Log.Debug("Failed due to: {0}", e.Message);

            PrimaryInputs.Cancel();
            PrimaryOutputs.Error(e);

            // Stopping will happen after flush
        }

        public override void PumpFinished()
        {
            PrimaryInputs.Cancel();
            PrimaryOutputs.Complete();
        }

        private void AfterFlush() => Context.Stop(Self);
    }
}