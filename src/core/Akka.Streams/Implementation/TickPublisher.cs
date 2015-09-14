//using System;
//using Akka.Actor;
//using Akka.Pattern;
//using Akka.Util;

//namespace Akka.Streams.Implementation
//{
//    public class TickPublisherSubscription : ISubscription
//    {
//        #region messages

//        [Serializable]
//        public sealed class CancelMessage
//        {
//            public static readonly CancelMessage Instance = new CancelMessage();

//            private CancelMessage()
//            {
//            }
//        }

//        [Serializable]
//        public sealed class RequestMore
//        {
//            public readonly long Elements;

//            public RequestMore(long elements)
//            {
//                Elements = elements;
//            }
//        }

//        [Serializable]
//        public sealed class Tick
//        {
//            public static readonly Tick Instance = new Tick();

//            private Tick()
//            {
//            }
//        }

//        #endregion

//        public readonly IActorRef Ref;

//        public TickPublisherSubscription(IActorRef @ref)
//        {
//            if (@ref == null) throw new ArgumentNullException("ref", "TickPublisherSubscription actor ref should be defined");
//            Ref = @ref;
//        }

//        public void Request(long n)
//        {
//            Ref.Tell(new RequestMore(n));
//        }

//        public void Cancel()
//        {
//            Ref.Tell(CancelMessage.Instance);
//        }
//    }

//    /**
//     * INTERNAL API
//     *
//     * Elements are emitted with the specified interval. Supports only one subscriber.
//     * The subscriber will receive the tick element if it has requested any elements,
//     * otherwise the tick element is dropped.
//     */
//    public class TickPublisher : ActorBase
//    {
//        private readonly TimeSpan _initialDelay;
//        private readonly TimeSpan _interval;
//        private readonly object _tick;
//        private readonly ActorMaterializerSettings _settings;
//        private readonly AtomicReference<bool> _cancelled;

//        private ActorPublisher _exposedPublisher = null;
//        private ISubscriber _subscriber = null;
//        private long _demand = 0L;

//        private ICancelable _tickTask = null;

//        public static Props Props(TimeSpan initialDelay, TimeSpan interval, object tick,
//            ActorMaterializerSettings settings, AtomicReference<bool> cancelled)
//        {
//            return Actor.Props.Create(() => new TickPublisher(initialDelay, interval, tick, settings, cancelled))
//                .WithDispatcher(settings.Dispatcher)
//                .WithDeploy(Deploy.Local);
//        }

//        private TickPublisher(TimeSpan initialDelay, TimeSpan interval, object tick, ActorMaterializerSettings settings, AtomicReference<bool> cancelled)
//        {
//            _initialDelay = initialDelay;
//            _interval = interval;
//            _tick = tick;
//            _settings = settings;
//            _cancelled = cancelled;
//        }

//        protected override bool Receive(object message)
//        {
//            if (message is ExposedPublisher)
//            {
//                var exposedPublisher = (ExposedPublisher)message;
//                _exposedPublisher = exposedPublisher.Publisher;
//                Context.Become(WaitingForFirstSubscriber);
//                return true;
//            }
//            else
//                throw new IllegalStateException("First message must be ExposedPublisher");
//        }

//        private bool WaitingForFirstSubscriber(object message)
//        {
//            if (message is SubscribePending)
//            {
//                foreach (var subscriber in _exposedPublisher.TakePendingSubscribers())
//                    RegisterSubscriber(subscriber);
//                _tickTask = Context.System.Scheduler
//                    .ScheduleTellRepeatedlyCancelable(_initialDelay, _interval, Self, TickPublisherSubscription.Tick.Instance, Self);
//                Context.Become(Active);

//                return true;
//            }
//            else return false;
//        }

//        private bool Active(object message)
//        {
//            if (message is TickPublisherSubscription.Tick)
//            {
//                try
//                {
//                    if (_demand > 0)
//                    {
//                        _demand--;
//                        ReactiveStreamsCompliance.TryOnNext(_subscriber, _tick);
//                    }
//                }
//                catch (Exception cause)
//                {
//                    HandleFailure(cause);
//                }
//            }
//            else if (message is TickPublisherSubscription.RequestMore)
//            {
//                var requestMore = (TickPublisherSubscription.RequestMore) message;
//                if (requestMore.Elements < 1)
//                    HandleFailure(new IllegalStateException("Number of elements in request must be possitive"));
//                else
//                {
//                    _demand += requestMore.Elements;
//                    if (_demand < 0)
//                        // Long overflow, Reactive Streams Spec 3:17: effectively unbounded
//                        _demand = long.MaxValue;
//                }
//            }
//            else if (message is TickPublisherSubscription.CancelMessage)
//            {
//                _subscriber = null;
//                Context.Stop(Self);
//            }
//            else if (message is SubscribePending)
//            {
//                foreach (var subscriber in _exposedPublisher.TakePendingSubscribers())
//                    RegisterSubscriber(subscriber);
//            }
//            else return false;
//            return true;
//        }

//        private void RegisterSubscriber(ISubscriber subscriber)
//        {
//            if (_subscriber == null)
//            {
//                var subscription = new TickPublisherSubscription(Self);
//                _subscriber = subscriber;
//                ReactiveStreamsCompliance.TryOnSubscribe(subscriber, subscription);
//            }
//            else
//            {
//                ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, Self.Path.Name);
//            }
//        }

//        private void HandleFailure(Exception error)
//        {
//            try
//            {
//                if (!(error is ISpecViolation))
//                    ReactiveStreamsCompliance.TryOnError(_subscriber, error);
//            }
//            finally
//            {
//                _subscriber = null;
//                _exposedPublisher.Shutdown(new IllegalStateException("TickPublisher supports only a single subscriber"));
//                Context.Stop(Self);
//            }
//        }

//        protected override void PostStop()
//        {
//            base.PostStop();
//            if (_tickTask != null) _tickTask.Cancel();
//            _cancelled.CompareAndSet(true, true);

//            if(_exposedPublisher != null)
//                _exposedPublisher.Shutdown(new NormalShutdownException("Cannot subscribe to shut-down Publisher"));

//            if(_subscriber != null)
//                ReactiveStreamsCompliance.TryOnComplete(_subscriber);
//        }
//    }
//}