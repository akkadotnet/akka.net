using System;
using System.Collections.Generic;
using System.Reactive.Streams;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;

namespace Akka.Streams.Implementation
{
    internal abstract class MultiStreamOutputProcessor<T> : ActorProcessorImpl<T>, IStreamSubscriptionTimeoutSupport<T>
    {
        #region internal classes

        [Serializable]
        internal struct SubstreamKey : IEquatable<SubstreamKey>
        {
            public readonly long Id;

            public SubstreamKey(long id)
            {
                Id = id;
            }

            public bool Equals(SubstreamKey other)
            {
                return Id == other.Id;
            }

            public override bool Equals(object obj)
            {
                return obj is SubstreamKey && Equals((SubstreamKey)obj);
            }

            public override int GetHashCode()
            {
                return Id.GetHashCode();
            }

            public static bool operator ==(SubstreamKey x, SubstreamKey y)
            {
                return Equals(x, y);
            }

            public static bool operator !=(SubstreamKey x, SubstreamKey y)
            {
                return !(x == y);
            }
        }

        [Serializable]
        internal struct SubstreamRequestMore : INoSerializationVerificationNeeded
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
        internal struct SubstreamCancel : INoSerializationVerificationNeeded
        {
            public readonly SubstreamKey Substream;

            public SubstreamCancel(SubstreamKey substream)
            {
                Substream = substream;
            }
        }

        [Serializable]
        internal struct SubstreamSubscribe : INoSerializationVerificationNeeded
        {
            public readonly SubstreamKey Substream;
            public readonly ISubscriber<T> Subscriber;

            public SubstreamSubscribe(SubstreamKey substream, ISubscriber<T> subscriber)
            {
                Substream = substream;
                Subscriber = subscriber;
            }
        }

        [Serializable]
        internal struct SubstreamSubscriptionTimeout : INoSerializationVerificationNeeded
        {
            public readonly SubstreamKey Substream;

            public SubstreamSubscriptionTimeout(SubstreamKey substream)
            {
                Substream = substream;
            }
        }

        internal struct SubstreamSubscription : ISubscription, IEquatable<SubstreamSubscription>
        {
            public readonly IActorRef Parent;
            public readonly SubstreamKey Substream;

            public SubstreamSubscription(IActorRef parent, SubstreamKey substream)
            {
                if (ReferenceEquals(parent, null)) throw new ArgumentNullException("parent", "Parent actor ref cannot be null");
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

            public bool Equals(SubstreamSubscription other)
            {
                return Substream == other.Substream && Equals(Parent, other.Parent);
            }

            public override bool Equals(object obj)
            {
                return obj is SubstreamSubscription && Equals((SubstreamSubscription)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Parent.GetHashCode() * 397) ^ Substream.GetHashCode();
                }
            }

            public override string ToString()
            {
                return "SubstreamSubscription" + GetHashCode();
            }
        }

        internal class SubstreamOutput : SimpleOutputs<T>, IPublisher<T>
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
                public readonly ISubscriber<T> Subscriber;

                public Attached(ISubscriber<T> subscriber)
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

            public readonly SubstreamKey Key;
            public readonly ICancelable SubscriptionTimeout;

            private readonly SubstreamSubscription _subscription;
            private readonly AtomicReference<IPublisherState> _state = new AtomicReference<IPublisherState>(Open.Instance);

            public SubstreamOutput(SubstreamKey key, IActorRef actor, IPump pump, ICancelable subscriptionTimeout) : base(actor, pump)
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

            void IPublisher.Subscribe(ISubscriber subscriber)
            {
                Subscribe((ISubscriber<T>)subscriber);    
            }

            public void Subscribe(ISubscriber<T> subscriber)
            {
                ReactiveStreamsCompliance.RequireNonNullSubscriber(subscriber);
                SubscriptionTimeout.Cancel();

                if (_state.CompareAndSet(Open.Instance, new Attached(subscriber)))
                    Actor.Tell(new SubstreamSubscribe(Key, subscriber));
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

            public void AttachSubscriber(ISubscriber<T> subscriber)
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

            private void CloseSubscriber(ISubscriber<T> subscriber, ICompletedState withState)
            {
                var f = withState as Failed;
                if (withState is Completed) ReactiveStreamsCompliance.TryOnComplete(subscriber);
                else if (f != null && !(f.Reason is ISpecViolation)) ReactiveStreamsCompliance.TryOnError(subscriber, f.Reason);
            }
        }

        #endregion

        // stream keys will be removed from this map on cancellation/subscription-timeout, never assume a key is present
        private readonly IDictionary<SubstreamKey, SubstreamOutput> _substreamOutputs = new Dictionary<SubstreamKey, SubstreamOutput>();
        private long _nextId = 0L;

        protected MultiStreamOutputProcessor(ActorMaterializerSettings settings) : base(settings) { }

        private ILoggingAdapter _log;
        protected ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

        public StreamSubscriptionTimeoutSettings SubscriptionTimeoutSettings
        {
            get
            {
                return Settings.SubscriptionTimeoutSettings;
            }
        }

        protected override void Fail(Exception e)
        {
            FailOutputs(e);
            base.Fail(e);
        }

        public override void PumpFinished()
        {
            FinishOutputs();
            base.PumpFinished();
        }

        protected override bool ActiveReceive(object message)
        {
            return PrimaryInputs.SubReceive.CurrentReceive(message)
                   || PrimaryOutputs.SubReceive.CurrentReceive(message)
                   || OutputSubstreamManagement(message);
        }

        #region MultiStreamOutputProcessorLike

        protected long NextId()
        {
            return (++_nextId);
        }

        protected SubstreamOutput CreateSubstreamOutput()
        {
            var id = new SubstreamKey(NextId());
            var cancellable = ScheduleSubscriptionTimeout(Self, new SubstreamSubscriptionTimeout(id));
            var output = new SubstreamOutput(id, Self, this, cancellable);
            _substreamOutputs.Add(output.Key, output);
            return output;
        }

        protected virtual void InvalidateSubstreamOutput(SubstreamKey substream)
        {
            CancelSubstreamOutput(substream);
            Pump();
        }

        protected void CancelSubstreamOutput(SubstreamKey substream)
        {
            SubstreamOutput output;
            if (_substreamOutputs.TryGetValue(substream, out output))
            {
                output.Cancel();
                _substreamOutputs.Remove(substream);
            }
        }

        protected void CompleteSubstreamOutput(SubstreamKey substream)
        {
            SubstreamOutput output;
            if (_substreamOutputs.TryGetValue(substream, out output))
            {
                output.Complete();
                _substreamOutputs.Remove(substream);
            }
        }

        protected void FailOutputs(Exception cause)
        {
            foreach (var output in _substreamOutputs.Values)
                output.Error(cause);
        }

        protected void FinishOutputs()
        {
            foreach (var output in _substreamOutputs.Values)
                output.Complete();
        }

        public bool OutputSubstreamManagement(object message)
        {
            return message.Match()
                .With<SubstreamRequestMore>(request =>
                {
                    SubstreamOutput output;
                    if (_substreamOutputs.TryGetValue(request.Substream, out output))
                    {
                        if (request.Demand < 1)
                            output.Error(ReactiveStreamsCompliance.NumberOfElementsInRequestMustBePositiveException);
                        else
                            output.EnqueueOutputDemand(request.Demand);
                    }
                })
                .With<SubstreamSubscribe>(subscribe =>
                {
                    SubstreamOutput output;
                    if (_substreamOutputs.TryGetValue(subscribe.Substream, out output))
                    {
                        output.AttachSubscriber(subscribe.Subscriber);
                    }
                })
                .With<SubstreamSubscriptionTimeout>(timeout =>
                {
                    SubstreamOutput output;
                    if (_substreamOutputs.TryGetValue(timeout.Substream, out output) && !output.IsAttached)
                    {
                        SubscriptionTimedOut(output);
                    }
                })
                .With<SubstreamCancel>(cancel => InvalidateSubstreamOutput(cancel.Substream))
                .WasHandled;
        }

        #endregion

        #region StreamSubscriptionTimeoutSupport

        public ICancelable ScheduleSubscriptionTimeout(IActorRef actorRef, object message)
        {
            return SubscriptionTimeoutSettings.Mode == StreamSubscriptionTimeoutTerminationMode.NoopTermination
                ? NoopSubscriptionTimeout.Instance
                : Context.System.Scheduler.ScheduleTellOnceCancelable(SubscriptionTimeoutSettings.Timeout, actorRef, message, Self);
        }

        public void SubscriptionTimedOut(IPublisher<T> target)
        {
            switch (SubscriptionTimeoutSettings.Mode)
            {
                case StreamSubscriptionTimeoutTerminationMode.CancelTermination: Cancel(target, SubscriptionTimeoutSettings.Timeout); break;
                case StreamSubscriptionTimeoutTerminationMode.WarnTermination: Warn(target, SubscriptionTimeoutSettings.Timeout); break;
            }
        }

        /// <summary>
        /// Callback that should ensure that the target is canceled with the given cause.
        /// </summary>
        public void HandleSubscriptionTimeout(IPublisher<T> target, Exception cause)
        {
            SubstreamOutput output;
            if ((output = target as SubstreamOutput) != null)
            {
                output.Error(cause);
                output.AttachSubscriber(CancelingSubscriber<T>.Instance);
            }
        }

        private void Cancel(IPublisher<T> target, TimeSpan timeout)
        {
            Log.Debug("Cancelling {0} (after {1})", target, timeout);
            HandleSubscriptionTimeout(target, new SubscriptionTimeoutException(string.Format("Publisher was not attached within {0} or it has been shut-down", timeout)));
            //val millis = timeout.toMillis
            //target match {
            //  case p: Processor[_, _] ⇒
            //    log.debug("Cancelling {} Processor's publisher and subscriber sides (after {} ms)", p, millis)
            //    handleSubscriptionTimeout(target, new SubscriptionTimeoutException(s"Publisher was not attached to upstream within deadline ($millis) ms") with NoStackTrace)

            //  case p: Publisher[_] ⇒
            //    log.debug("Cancelling {} (after: {} ms)", p, millis)
            //    handleSubscriptionTimeout(target, new SubscriptionTimeoutException(s"Publisher ($p) you are trying to subscribe to has been shut-down " +
            //      s"because exceeding it's subscription-timeout.") with NoStackTrace)
            //}
        }

        private void Warn(IPublisher<T> target, TimeSpan timeout)
        {
            Log.Warning("Timed out {0} detected (after {1})! You should investigate if you either cancel or consume all {2} instances", target, timeout, target.GetType().Name);
        }

        #endregion
    }

    internal sealed class TwoStreamInputProcessor<T> : ActorProcessorImpl<T>
    {
        #region Internal classes

        [Serializable]
        public sealed class OtherStreamOnComplete
        {
            public static readonly OtherStreamOnComplete Instance = new OtherStreamOnComplete();
            private OtherStreamOnComplete() { }
        }

        [Serializable]
        public struct OtherStreamOnNext
        {
            public readonly object Element;

            public OtherStreamOnNext(object element)
            {
                Element = element;
            }
        }

        [Serializable]
        public struct OtherStreamOnError
        {
            public readonly Exception Cause;

            public OtherStreamOnError(Exception cause)
            {
                Cause = cause;
            }
        }

        [Serializable]
        public struct OtherStreamOnSubscribe
        {
            public readonly ISubscription Subscription;

            public OtherStreamOnSubscribe(ISubscription subscription)
            {
                Subscription = subscription;
            }
        }

        public sealed class OtherActorSubscriber : ISubscriber<T>
        {
            protected readonly IActorRef Impl;

            public OtherActorSubscriber(IActorRef impl)
            {
                Impl = impl;
            }

            public void OnNext(T element)
            {
                ReactiveStreamsCompliance.RequireNonNullElement(element);
                Impl.Tell(new OtherStreamOnNext(element));
            }

            void ISubscriber.OnNext(object element)
            {
                OnNext((T)element);
            }

            public void OnSubscribe(ISubscription subscription)
            {
                ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
                Impl.Tell(new OtherStreamOnSubscribe(subscription));
            }

            public void OnError(Exception cause)
            {
                ReactiveStreamsCompliance.RequireNonNullException(cause);
                Impl.Tell(new OtherStreamOnError(cause));
            }

            public void OnComplete()
            {
                Impl.Tell(OtherStreamOnComplete.Instance);
            }
        }

        private sealed class AnonymousBatchingInputBuffer : BatchingInputBuffer
        {
            private readonly TwoStreamInputProcessor<T> _that;
            public AnonymousBatchingInputBuffer(int count, TwoStreamInputProcessor<T> that) : base(count, that)
            {
                _that = that;
                SubReceive = new SubReceive(WaitingForUpstream);
            }

            public override SubReceive SubReceive { get; }

            protected override void InputOnError(Exception e)
            {
                _that.OnError(e);
            }

            protected override bool WaitingForUpstream(object message)
            {
                return message.Match()
                    .With<OtherStreamOnComplete>(_ => OnComplete())
                    .With<OtherStreamOnSubscribe>(subscribe => OnSubscribe(subscribe.Subscription))
                    .With<OtherStreamOnError>(error => _that.OnError(error.Cause))
                    .WasHandled;
            }

            protected override bool UpstreamRunning(object message)
            {
                return message.Match()
                    .With<OtherStreamOnNext>(next => EnqueueInputElement(next.Element))
                    .With<OtherStreamOnComplete>(_ => OnComplete())
                    .With<OtherStreamOnError>(error => _that.OnError(error.Cause))
                    .WasHandled;
            }

            protected override bool Completed(object message)
            {
                if (message is OtherStreamOnSubscribe)
                {
                    throw ActorPublisher.NormalShutdownReason;
                }

                return false;
            }
        }

        #endregion

        public readonly IPublisher<T> Other;
        public readonly BatchingInputBuffer SecondaryInputs;

        public TwoStreamInputProcessor(ActorMaterializerSettings settings, IPublisher<T> other) : base(settings)
        {
            Other = other;
            SecondaryInputs = new AnonymousBatchingInputBuffer(settings.InitialInputBufferSize, this);

            Other.Subscribe(new OtherActorSubscriber(Self));
        }

        protected override bool ActiveReceive(object message)
        {
            return SecondaryInputs.SubReceive.CurrentReceive(message)
                   || PrimaryInputs.SubReceive.CurrentReceive(message)
                   || PrimaryOutputs.SubReceive.CurrentReceive(message);
        }

        public override void PumpFinished()
        {
            SecondaryInputs.Cancel();
            base.PumpFinished();
        }
    }

    internal abstract class MultiStreamInputProcessor<T> : ActorProcessorImpl<T>
    {
        #region Internal classes

        [Serializable]
        public struct SubstreamKey : IEquatable<SubstreamKey>
        {
            public readonly long Id;

            public SubstreamKey(long id)
            {
                Id = id;
            }

            public bool Equals(SubstreamKey other)
            {
                return Id == other.Id;
            }

            public override bool Equals(object obj)
            {
                return obj is SubstreamKey && Equals((SubstreamKey)obj);
            }

            public override int GetHashCode()
            {
                return Id.GetHashCode();
            }

            public static bool operator ==(SubstreamKey x, SubstreamKey y)
            {
                return Equals(x, y);
            }

            public static bool operator !=(SubstreamKey x, SubstreamKey y)
            {
                return !(x == y);
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
        public struct SubstreamOnNext : INoSerializationVerificationNeeded
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
        public struct SubstreamOnError : INoSerializationVerificationNeeded
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
        public struct SubstreamOnSubscribe : INoSerializationVerificationNeeded
        {
            public readonly SubstreamKey Key;
            public readonly ISubscription Subscription;

            public SubstreamOnSubscribe(SubstreamKey key, ISubscription subscription)
            {
                Key = key;
                Subscription = subscription;
            }
        }

        public sealed class SubstreamSubscriber : AtomicReference<ISubscription>, ISubscriber<T>
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

            public void OnNext(T element)
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

            void ISubscriber.OnNext(object element)
            {
                OnNext((T)element);
            }
        }
        
        public class SubstreamInput : BatchingInputBuffer
        {
            public readonly SubstreamKey Key;
            public readonly int BufferSize;
            public readonly MultiStreamInputProcessor<T> Processor;
            public readonly IPump Pump;

            public SubstreamInput(SubstreamKey key, int bufferCount, MultiStreamInputProcessor<T> processor, IPump pump) : base(bufferCount, pump)
            {
                Key = key;
                BufferSize = bufferCount;
                Processor = processor;
                Pump = pump;
            }
            
            public override SubReceive SubReceive { get; } = new SubReceive(_ => false);

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

        private readonly IDictionary<SubstreamKey, SubstreamInput> _substreamInputs = new Dictionary<SubstreamKey, SubstreamInput>();
        private readonly IDictionary<SubstreamKey, SubstreamSubscriber> _waitingForOnSubscribe = new Dictionary<SubstreamKey, SubstreamSubscriber>();

        private long _nextId = 0L;

        protected MultiStreamInputProcessor(ActorMaterializerSettings settings) : base(settings)
        {
        }

        protected override void Fail(Exception e)
        {
            FailInputs(e);
            base.Fail(e);
        }

        public override void PumpFinished()
        {
            FinishInputs();
            base.PumpFinished();
        }

        protected override bool ActiveReceive(object message)
        {
            return PrimaryInputs.SubReceive.CurrentReceive(message)
                   || PrimaryOutputs.SubReceive.CurrentReceive(message)
                   || InputSubstreamManagement(message);
        }

        #region MultiStreamInputProcessorLike

        protected long NextId()
        {
            return (++_nextId);
        }

        protected int InputBufferSize { get { return Settings.InitialInputBufferSize; } }

        public bool InputSubstreamManagement(object message)
        {
            return message.Match()
                .With<SubstreamOnSubscribe>(subscribe =>
                {
                    _substreamInputs[subscribe.Key].SubstreamOnSubscribe(subscribe.Subscription);
                    _waitingForOnSubscribe.Remove(subscribe.Key);
                })
                .With<SubstreamOnNext>(next => _substreamInputs[next.Key].SubstreamOnNext(next.Element))
                .With<SubstreamOnComplete>(complete =>
                {
                    _substreamInputs[complete.Key].SubstreamOnComplete();
                    _substreamInputs.Remove(complete.Key);
                })
                .With<SubstreamOnError>(error => _substreamInputs[error.Key].SubstreamOnError(error.Reason))
                .WasHandled;
        }

        public SubstreamInput CreateSubstreamInput()
        {
            var key = new SubstreamKey(NextId());
            var input = new SubstreamInput(key, InputBufferSize, this, this);
            _substreamInputs.Add(key, input);
            return input;
        }

        public SubstreamInput CreateAndSubscribeSubstreamInput(IPublisher<T> publisher)
        {
            var input = CreateSubstreamInput();
            var subscriber = new SubstreamSubscriber(Self, input.Key);
            _waitingForOnSubscribe.Add(input.Key, subscriber);
            publisher.Subscribe(subscriber);
            return input;
        }

        public void InvalidateSubstreamInput(SubstreamKey substream, Exception cause)
        {
            _substreamInputs[substream].Cancel();
            _substreamInputs.Remove(substream);
            Pump();
        }

        protected virtual void FailInputs(Exception cause)
        {
            CancelWaitingForOnSubscribe();
            foreach (var input in _substreamInputs.Values)
            {
                input.Cancel();
            }
        }
        protected virtual void FinishInputs()
        {
            CancelWaitingForOnSubscribe();
            foreach (var input in _substreamInputs.Values)
            {
                input.Cancel();
            }
        }

        private void CancelWaitingForOnSubscribe()
        {
            foreach (var subscriber in _waitingForOnSubscribe.Values)
            {
                var subscription = subscriber.GetAndSet(CancelledSubscription.Instance);
                if (subscription != null)
                {
                    subscription.Cancel();
                }
            }
        }

        #endregion
    }
}