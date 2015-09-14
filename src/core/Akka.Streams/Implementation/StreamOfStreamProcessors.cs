using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Pattern;
using Akka.Util;

namespace Akka.Streams.Implementation
{
    public abstract class MultiStreamOutputProcessor : ActorProcessorImpl, IMultiStreamOutputProcessorLike
    {
        #region Internal classes

        [Serializable]
        public sealed class SubstreamKey
        {
            public readonly long Id;

            public SubstreamKey(long id)
            {
                Id = id;
            }
        }

        [Serializable]
        public sealed class SubstreamRequestMore : INoSerializationVerificationNeeded
        {
            public readonly SubstreamKey Substream;
            public readonly long Demand;

            public SubstreamRequestMore(SubstreamKey substream, long demand)
            {
                Substream = substream;
                Demand = demand;
            }
        }

        [Serializable]
        public sealed class SubstreamCancel : INoSerializationVerificationNeeded
        {
            public readonly SubstreamKey Substream;

            public SubstreamCancel(SubstreamKey substream)
            {
                Substream = substream;
            }
        }

        [Serializable]
        public sealed class SubstreamSubscribe : INoSerializationVerificationNeeded
        {
            public readonly SubstreamKey Substream;
            public readonly ISubscriber Subscriber;

            public SubstreamSubscribe(SubstreamKey substream, ISubscriber subscriber)
            {
                Substream = substream;
                Subscriber = subscriber;
            }
        }

        [Serializable]
        public sealed class SubstreamSubscriptionTimeout : INoSerializationVerificationNeeded
        {
            public readonly SubstreamKey Substream;

            public SubstreamSubscriptionTimeout(SubstreamKey substream)
            {
                Substream = substream;
            }
        }

        public class SubstreamSubscription : ISubscription
        {
            public readonly IActorRef Parent;
            public readonly SubstreamKey Substream;

            public SubstreamSubscription(IActorRef parent, SubstreamKey substream)
            {
                Parent = parent;
                Substream = substream;
            }

            public void Request(long n)
            {
                Parent.Tell(new SubstreamRequestMore(Substream, n));
            }

            public void Cancel()
            {
                Parent.Tell(new SubstreamCancel(Substream));
            }

            public override string ToString()
            {
                return "SubstreamSubscription" + GetHashCode();
            }
        }

        #endregion

        protected MultiStreamOutputProcessor(ActorMaterializerSettings settings) : base(settings) { }

        public abstract long NextId();
    }

    public class SubstreamOutput : SimpleOutputs, IPublisher
    {
        #region Internal classes

        public interface IPublisherState { }
        public interface ICompletedState : IPublisherState { }

        [Serializable]
        public sealed class Open : IPublisherState
        {
            public static readonly Open Instance = new Open();
            private Open() { }
        }

        [Serializable]
        public sealed class Attached : IPublisherState
        {
            public readonly ISubscriber Subscriber;

            public Attached(ISubscriber subscriber)
            {
                Subscriber = subscriber;
            }
        }

        [Serializable]
        public sealed class Completed : ICompletedState
        {
            public static readonly Completed Instance = new Completed();
            private Completed() { }
        }

        [Serializable]
        public sealed class Cancelled : ICompletedState
        {
            public static readonly Cancelled Instance = new Cancelled();
            private Cancelled() { }
        }

        [Serializable]
        public sealed class Failed : ICompletedState
        {
            public readonly Exception Reason;

            public Failed(Exception reason)
            {
                Reason = reason;
            }
        }

        #endregion

        public readonly MultiStreamOutputProcessor.SubstreamKey Key;
        public readonly ICancelable SubscriptionTimeout;

        private readonly MultiStreamOutputProcessor.SubstreamSubscription _subscription;
        private readonly AtomicReference<IPublisherState> _state = new AtomicReference<IPublisherState>(Open.Instance);

        public SubstreamOutput(MultiStreamOutputProcessor.SubstreamKey key, IActorRef actor, IPump pump, ICancelable subscriptionTimeout) : base(actor, pump)
        {
            Key = key;
            SubscriptionTimeout = subscriptionTimeout;
        }

        public override SubReceive SubReceive { get { throw new NotSupportedException("Substream outputs are managed in a dedicated receive block"); } }
        public bool IsAttached { get { return _state.Value is Attached; } }

        public void EnqueueOutputDemand(long demand)
        {
            DownstreamDemand += demand;
            Pump.Pump();
        }

        public override void Error(Exception e)
        {
            if (!IsDownstreamCompleted)
            {
                ClosePublisher(new Failed(e));
                IsDownstreamCompleted = true;
            }
        }

        public override void Cancel()
        {
            if (!IsDownstreamCompleted)
            {
                ClosePublisher(Cancelled.Instance);
                IsDownstreamCompleted = true;
            }
        }

        public override void Complete()
        {
            if (!IsDownstreamCompleted)
            {
                ClosePublisher(Completed.Instance);
                IsDownstreamCompleted = true;
            }
        }

        public void Subscribe(ISubscriber subscriber)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscriber(subscriber);
            SubscriptionTimeout.Cancel();

            if (_state.CompareAndSet(Open.Instance, new Attached(subscriber)))
                Actor.Tell(new MultiStreamOutputProcessor.SubstreamSubscribe(Key, subscriber));
            else
            {
                var value = _state.Value;
                if (value is Attached || value is Cancelled) ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, "Substream publisher");
                else if (value is ICompletedState)
                {
                    ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CancelledSubscription.Instance);
                    CloseSubscriber(subscriber, (ICompletedState)value);
                }
                else if (value is Open)
                    throw new IllegalStateException("Publisher cannot become open after being used before");
            }
        }

        public void AttachSubscriber(ISubscriber subscriber)
        {
            if (Subscriber == null)
            {
                Subscriber = subscriber;
                ReactiveStreamsCompliance.TryOnSubscribe(subscriber, _subscription);
            }
            else ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, "Substream publisher");
        }

        private void ClosePublisher(ICompletedState withState)
        {
            SubscriptionTimeout.Cancel();
            var prev = _state.Value;
            _state.Value = withState;

            if (prev is ICompletedState) throw new IllegalStateException("Attempted to double shutdown publisher");
            else if (prev is Attached)
            {
                var sub = ((Attached)prev).Subscriber;
                if (Subscriber == null) ReactiveStreamsCompliance.TryOnSubscribe(sub, CancelledSubscription.Instance);
                CloseSubscriber(sub, withState);
            }
        }

        private void CloseSubscriber(ISubscriber subscriber, ICompletedState withState)
        {
            var f = withState as Failed;
            if (withState is Completed) ReactiveStreamsCompliance.TryOnComplete(subscriber);
            else if (f != null && !(f.Reason is ISpecViolation)) ReactiveStreamsCompliance.TryOnError(subscriber, f.Reason);
        }
    }

    public interface IMultiStreamOutputProcessorLike : IPump, IStreamSubscriptionTimeoutSupport
    {
        long NextId();
    }

    public static class MultiStreamOutputProcessorLike
    {

    }

    public sealed class TwoStreamInputProcessor : ActorProcessorImpl
    {
        #region Internal classes

        [Serializable]
        public sealed class OtherStreamOnComplete
        {
            public static readonly OtherStreamOnComplete Instance = new OtherStreamOnComplete();
            private OtherStreamOnComplete() { }
        }

        [Serializable]
        public sealed class OtherStreamOnNext
        {

        }

        [Serializable]
        public sealed class OtherStreamOnError
        {

        }

        [Serializable]
        public sealed class OtherStreamOnSubscribe
        {

        }

        public class OtherActorSubscriber<T> : ISubscriber<T>
        {
            protected readonly IActorRef Impl;

            public OtherActorSubscriber(IActorRef impl)
            {
                Impl = impl;
            }

            public void OnNext(T element)
            {
                OnNext((object)element);
            }

            public void OnSubscribe(ISubscription subscription)
            {
                ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
                Impl.Tell();
            }

            public void OnNext(object element)
            {
                throw new NotImplementedException();
            }

            public void OnError(Exception cause)
            {
                throw new NotImplementedException();
            }

            public void OnComplete()
            {
                throw new NotImplementedException();
            }
        }

        #endregion

        public readonly IPublisher Other;

        public TwoStreamInputProcessor(ActorMaterializerSettings settings, IPublisher other) : base(settings)
        {
            Other = other;
        }
    }

    public interface IMultiStreamInputProcessorLike : IPump
    {
        void InvalidateSubstreamInput(MultiStreamInputProcessor.SubstreamKey key, Exception exception);
    }

    public abstract class MultiStreamInputProcessor : ActorProcessorImpl, IMultiStreamInputProcessorLike
    {
        #region Internal classes

        [Serializable]
        public sealed class SubstreamKey
        {
            public readonly long Id;

            public SubstreamKey(long id)
            {
                Id = id;
            }
        }

        [Serializable]
        public sealed class SubstreamOnComplete : INoSerializationVerificationNeeded
        {
            public readonly SubstreamKey Key;

            public SubstreamOnComplete(SubstreamKey key)
            {
                Key = key;
            }
        }

        [Serializable]
        public sealed class SubstreamOnNext : INoSerializationVerificationNeeded
        {
            public readonly SubstreamKey Key;
            public readonly object Element;

            public SubstreamOnNext(SubstreamKey key, object element)
            {
                Key = key;
                Element = element;
            }
        }

        [Serializable]
        public sealed class SubstreamOnError : INoSerializationVerificationNeeded
        {
            public readonly SubstreamKey Key;
            public readonly Exception Reason;

            public SubstreamOnError(SubstreamKey key, Exception reason)
            {
                Key = key;
                Reason = reason;
            }
        }

        [Serializable]
        public sealed class SubstreamOnSubscribe : INoSerializationVerificationNeeded
        {
            public readonly SubstreamKey Key;
            public readonly ISubscription Subscription;

            public SubstreamOnSubscribe(SubstreamKey key, ISubscription subscription)
            {
                Key = key;
                Subscription = subscription;
            }
        }

        public class SubstreamSubscriber : AtomicReference<ISubscription>, ISubscriber
        {
            public readonly IActorRef Impl;
            public readonly SubstreamKey Key;

            public SubstreamSubscriber(IActorRef impl, SubstreamKey key)
            {
                Impl = impl;
                Key = key;
            }

            public void OnSubscribe(ISubscription subscription)
            {
                ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
                if (CompareAndSet(null, subscription)) Impl.Tell(new SubstreamOnSubscribe(Key, subscription));
                else subscription.Cancel();
            }

            public void OnNext(object element)
            {
                ReactiveStreamsCompliance.RequireNonNullElement(element);
                Impl.Tell(new SubstreamOnNext(Key, element));
            }

            public void OnError(Exception cause)
            {
                ReactiveStreamsCompliance.RequireNonNullException(cause);
                Impl.Tell(new SubstreamOnError(Key, cause));
            }

            public void OnComplete()
            {
                Impl.Tell(new SubstreamOnComplete(Key));
            }
        }

        public class SubstreamSubscriber<T> : SubstreamSubscriber, ISubscriber<T>
        {
            public SubstreamSubscriber(IActorRef impl, SubstreamKey key) : base(impl, key)
            {
            }

            public void OnNext(T element)
            {
                base.OnNext((object)element);
            }
        }

        public class SubstreamInput : BatchingInputBuffer
        {
            public readonly SubstreamKey Key;
            public readonly int BufferSize;
            public readonly IMultiStreamInputProcessorLike Processor;
            public readonly IPump Pump;

            public SubstreamInput(SubstreamKey key, int bufferSize, IMultiStreamInputProcessorLike processor, IPump pump) : base(bufferSize, pump)
            {
                Key = key;
                BufferSize = bufferSize;
                Processor = processor;
                Pump = pump;
            }

            private readonly SubReceive _subReceive = new SubReceive(_ => false);
            public override SubReceive SubReceive { get { return _subReceive; } }

            public void SubstreamOnComplete()
            {
                OnComplete();
            }

            public void SubstreamOnSubscribe(ISubscription subscription)
            {
                OnSubscribe(subscription);
            }

            public void SubstreamOnError(Exception e)
            {
                OnError(e);
            }

            public void SubstreamOnNext(object element)
            {
                EnqueueInputElement(element);
            }

            protected override void InputOnError(Exception e)
            {
                base.InputOnError(e);
                Processor.InvalidateSubstreamInput(Key, e);
            }
        }

        #endregion

        protected MultiStreamInputProcessor(ActorMaterializerSettings settings) : base(settings)
        {
        }
    }
}