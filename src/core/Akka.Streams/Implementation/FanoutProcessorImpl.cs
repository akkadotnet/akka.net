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

        public long DemandCount => _downstreamBufferSpace;

        public bool IsDemandAvailable => _downstreamBufferSpace > 0;

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

        protected bool DownstreamRunning(object message)
        {
            var subscribePending = message as SubscribePending;
            if (subscribePending != null)
            {
                ExposedPublisher.TakePendingSubscribers().ForEach(RegisterSubscriber);
                return true;
            }

            var requestMore = message as RequestMore<T>;
            if (requestMore != null)
            {
                MoreRequested(requestMore.Subscription as ActorSubscriptionWithCursor<T>, requestMore.Demand);
                _pump.Pump();
                return true;
            }

            var cancel = message as Cancel<T>;
            if (cancel != null)
            {
                UnregisterSubscription(cancel.Subscription as ActorSubscriptionWithCursor<T>);
                _pump.Pump();
                return true;
            }

            return false;
        }

        protected override void RequestFromUpstream(long elements) => _downstreamBufferSpace += elements;

        protected override void CancelUpstream() => _downstreamCompleted = true;

        protected override void Shutdown(bool isCompleted)
        {
            if (ExposedPublisher != null)
                ExposedPublisher.Shutdown(isCompleted ? null : ActorPublisher<object>.NormalShutdownReason);

            if (_afterShutdown != null)
                _afterShutdown();
        }

        protected override ISubscriptionWithCursor<T> CreateSubscription(ISubscriber<T> subscriber)
            => new ActorSubscriptionWithCursor<T>(_self, subscriber);

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

            if (ExposedPublisher != null)
                ExposedPublisher.Shutdown(e);
        }

        public bool IsClosed => _downstreamCompleted;

        public bool IsOpen => !IsClosed;
    }

    internal class FanoutProcessorImpl<T> : ActorProcessorImpl<T>
    {
        public static Props Props(ActorMaterializerSettings effectiveSettingsy)
        {
            return Actor.Props.Create(() => new FanoutProcessorImpl<T>(effectiveSettingsy)).WithDeploy(Deploy.Local);
        }


        protected override IOutputs PrimaryOutputs { get; }

        public FanoutProcessorImpl(ActorMaterializerSettings effectiveSettings) : base(effectiveSettings)
        {
            PrimaryOutputs = new FanoutOutputs<T>(effectiveSettings.MaxInputBufferSize,
                effectiveSettings.InitialInputBufferSize, Self, this, AfterFlush);

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

        protected new void PumpFinished()
        {
            PrimaryInputs.Cancel();
            PrimaryOutputs.Complete();
        }

        private void AfterFlush() => Context.Stop(Self);
    }
}