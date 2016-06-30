//-----------------------------------------------------------------------
// <copyright file="StreamOfStreamProcessors.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    internal abstract class MultiStreamOutputProcessor<T> : ActorProcessorImpl, IStreamSubscriptionTimeoutSupport
    {
        #region internal classes

#if SERIALIZATION
        [Serializable]
#endif
        internal struct SubstreamKey : IEquatable<SubstreamKey>
        {
            public readonly long Id;

            public SubstreamKey(long id)
            {
                Id = id;
            }

            public bool Equals(SubstreamKey other) => Id == other.Id;

            public override bool Equals(object obj) => obj is SubstreamKey && Equals((SubstreamKey)obj);

            public override int GetHashCode() => Id.GetHashCode();

            public static bool operator ==(SubstreamKey x, SubstreamKey y) => Equals(x, y);

            public static bool operator !=(SubstreamKey x, SubstreamKey y) => !(x == y);
        }

#if SERIALIZATION
        [Serializable]
#endif
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

#if SERIALIZATION
        [Serializable]
#endif
        internal struct SubstreamCancel : INoSerializationVerificationNeeded
        {
            public readonly SubstreamKey Substream;

            public SubstreamCancel(SubstreamKey substream)
            {
                Substream = substream;
            }
        }

#if SERIALIZATION
        [Serializable]
#endif
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

#if SERIALIZATION
        [Serializable]
#endif
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
                if (ReferenceEquals(parent, null)) throw new ArgumentNullException(nameof(parent), "Parent actor ref cannot be null");
                Parent = parent;
                Substream = substream;
            }

            public void Request(long n) => Parent.Tell(new SubstreamRequestMore(Substream, n));

            public void Cancel() => Parent.Tell(new SubstreamCancel(Substream));

            public bool Equals(SubstreamSubscription other) => Substream == other.Substream && Equals(Parent, other.Parent);

            public override bool Equals(object obj) => obj is SubstreamSubscription && Equals((SubstreamSubscription)obj);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Parent.GetHashCode() * 397) ^ Substream.GetHashCode();
                }
            }

            public override string ToString() => "SubstreamSubscription" + GetHashCode();
        }

        internal class SubstreamOutput : SimpleOutputs, IPublisher<T>
        {
            #region Internal classes

            public interface IPublisherState { }
            public interface ICompletedState : IPublisherState { }

#if SERIALIZATION
            [Serializable]
#endif
            public sealed class Open : IPublisherState
            {
                public static readonly Open Instance = new Open();
                private Open() { }
            }

#if SERIALIZATION
            [Serializable]
#endif
            public sealed class Attached : IPublisherState
            {
                public readonly ISubscriber<T> Subscriber;

                public Attached(ISubscriber<T> subscriber)
                {
                    Subscriber = subscriber;
                }
            }

#if SERIALIZATION
            [Serializable]
#endif
            public sealed class Completed : ICompletedState
            {
                public static readonly Completed Instance = new Completed();
                private Completed() { }
            }

#if SERIALIZATION
            [Serializable]
#endif
            public sealed class Cancelled : ICompletedState
            {
                public static readonly Cancelled Instance = new Cancelled();
                private Cancelled() { }
            }

#if SERIALIZATION
            [Serializable]
#endif
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
                _subscription = new SubstreamSubscription(actor, key);
            }

            public override SubReceive SubReceive { get { throw new NotSupportedException("Substream outputs are managed in a dedicated receive block"); } }
            public bool IsAttached => _state.Value is Attached;

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

            public void Subscribe(ISubscriber<T> subscriber)
            {
                ReactiveStreamsCompliance.RequireNonNullSubscriber(subscriber);
                SubscriptionTimeout.Cancel();

                if (_state.CompareAndSet(Open.Instance, new Attached(subscriber)))
                    Actor.Tell(new SubstreamSubscribe(Key, subscriber));
                else
                {
                    var value = _state.Value;
                    if (value is Attached || value is Cancelled)
                        ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, "Substream publisher");
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
                    Subscriber = UntypedSubscriber.FromTyped(subscriber);
                    ReactiveStreamsCompliance.TryOnSubscribe(subscriber, _subscription);
                }
                else
                    ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, "Substream publisher");
            }

            private void ClosePublisher(ICompletedState withState)
            {
                SubscriptionTimeout.Cancel();
                var prev = _state.Value;
                _state.Value = withState;

                if (prev is ICompletedState)
                    throw new IllegalStateException("Attempted to double shutdown publisher");

                if (prev is Attached)
                {
                    var sub = ((Attached)prev).Subscriber;
                    if (Subscriber == null)
                        ReactiveStreamsCompliance.TryOnSubscribe(sub, CancelledSubscription.Instance);
                    CloseSubscriber(sub, withState);
                }
            }

            private void CloseSubscriber(ISubscriber<T> subscriber, ICompletedState withState)
            {
                var f = withState as Failed;
                if (withState is Completed)
                    ReactiveStreamsCompliance.TryOnComplete(subscriber);
                else if (f != null && !(f.Reason is ISpecViolation))
                    ReactiveStreamsCompliance.TryOnError(subscriber, f.Reason);
            }
        }

        #endregion

        // stream keys will be removed from this map on cancellation/subscription-timeout, never assume a key is present
        private readonly IDictionary<SubstreamKey, SubstreamOutput> _substreamOutputs = new Dictionary<SubstreamKey, SubstreamOutput>();
        private long _nextId;

        protected MultiStreamOutputProcessor(ActorMaterializerSettings settings) : base(settings) { }

        private ILoggingAdapter _log;
        protected new ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        public StreamSubscriptionTimeoutSettings SubscriptionTimeoutSettings => Settings.SubscriptionTimeoutSettings;

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

        protected long NextId() => (++_nextId);

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
                        output.AttachSubscriber(subscribe.Subscriber);
                })
                .With<SubstreamSubscriptionTimeout>(timeout =>
                {
                    SubstreamOutput output;
                    if (_substreamOutputs.TryGetValue(timeout.Substream, out output) && !output.IsAttached)
                        SubscriptionTimedOut(UntypedPublisher.FromTyped(output));
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

        public void SubscriptionTimedOut(IUntypedPublisher target)
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
        public void HandleSubscriptionTimeout(IUntypedPublisher target, Exception cause)
        {
            SubstreamOutput output;
            if ((output = UntypedPublisher.ToTyped<T>(target) as SubstreamOutput) != null)
            {
                output.Error(cause);
                output.AttachSubscriber(CancelingSubscriber<T>.Instance);
            }
        }

        private void Cancel(IUntypedPublisher target, TimeSpan timeout)
        {
            var typedTarget = UntypedPublisher.ToTyped<T>(target);
            if (
                typedTarget.GetType()
                    .GetTypeInfo().GetInterfaces()
                    .Any(i => i.GetTypeInfo().IsGenericType && i.GetGenericTypeDefinition() == typeof(IProcessor<,>)))
            {
                if (Log.IsDebugEnabled)
                    Log.Debug(
                        $"Cancelling {typedTarget} Processor's publisher and subscriber sides (after {timeout.TotalMilliseconds} ms)");
                HandleSubscriptionTimeout(target, new SubscriptionTimeoutException(
                    $"Publisher was not attached to upstream within deadline {timeout.TotalMilliseconds} ms"));
                
            }
            else
                if (Log.IsDebugEnabled)
                    Log.Debug(
                        $"Cancelling {typedTarget} (after {timeout.TotalMilliseconds} ms)");
                HandleSubscriptionTimeout(target, new SubscriptionTimeoutException(
                    $"Publisher {typedTarget} you are trying to subscribe to has been shut-down because exceeing its subscription timeout"));
        }

        private void Warn(IUntypedPublisher target, TimeSpan timeout)
        {
            var typedTarget = UntypedPublisher.ToTyped<T>(target);
            Log.Warning(
                "Timed out {0} detected (after {1})! You should investigate if you either cancel or consume all {2} instances",
                typedTarget, timeout, typedTarget.GetType().Name);
        }

        #endregion
    }
}