using System;
using System.Collections.Generic;
using System.Reactive.Streams;
using Akka.Actor;
using Akka.Streams.Implementation;
using Akka.TestKit;

namespace Akka.Streams.TestKit
{
    /// <summary>
    /// Provides factory methods for various Publishers.
    /// </summary>
    public static class TestPublisher
    {
        #region messages

        public interface IPublisherEvent : INoSerializationVerificationNeeded { }

        public struct Subscribe : IPublisherEvent
        {
            public readonly ISubscription Subscription;

            public Subscribe(ISubscription subscription)
            {
                Subscription = subscription;
            }
        }

        public struct CancelSubscription : IPublisherEvent
        {
            public readonly ISubscription Subscription;

            public CancelSubscription(ISubscription subscription)
            {
                Subscription = subscription;
            }
        }

        public struct RequestMore : IPublisherEvent
        {
            public readonly ISubscription Subscription;
            public readonly long NrOfElements;

            public RequestMore(ISubscription subscription, long nrOfElements)
            {
                Subscription = subscription;
                NrOfElements = nrOfElements;
            }
        }

        #endregion

        /// <summary>
        /// Implementation of <see cref="IPublisher{T}"/> that allows various assertions.
        /// This probe does not track demand.Therefore you need to expect demand before sending
        ///  elements downstream.
        /// </summary>
        public class ManualProbe<T> : IPublisher<T>
        {
            private readonly TestProbe _probe;

            internal ManualProbe(TestKitBase system, bool autoOnSubscribe = true)
            {
                _probe = system.CreateTestProbe();
                AutoOnSubscribe = autoOnSubscribe;
            }

            public bool AutoOnSubscribe { get; private set; }
            public IPublisher<T> Publisher { get { return this; } }

            /// <summary>
            /// Subscribes a given <paramref name="subscriber"/> to this probe.
            /// </summary>
            public void Subscribe(ISubscriber<T> subscriber)
            {
                var subscription = new StreamTestKit.PublisherProbeSubscription<T>(subscriber, _probe);
                _probe.Ref.Tell(new TestPublisher.Subscribe(subscription));
                if (AutoOnSubscribe) subscriber.OnSubscribe(subscription);
            }

            /// <summary>
            /// Expect a subscription.
            /// </summary>
            public StreamTestKit.PublisherProbeSubscription<T> ExpectSubscription()
            {
                return
                    (StreamTestKit.PublisherProbeSubscription<T>) _probe.ExpectMsg<Subscribe>().Subscription;
            }

            /// <summary>
            /// Expect demand from the given subscription.
            /// </summary>
            public ManualProbe<T> ExpectRequest(ISubscription subscription, int n)
            {
                _probe.ExpectMsg<TestPublisher.RequestMore>(x => x.NrOfElements == n && x.Subscription == subscription);
                return this;
            }

            /// <summary>
            /// Expect no messages.
            /// </summary>
            public ManualProbe<T> ExpectNoMsg()
            {
                _probe.ExpectNoMsg();
                return this;
            }

            /// <summary>
            /// Expect no messages for given duration.
            /// </summary>
            public ManualProbe<T> ExpectNoMsg(TimeSpan duration)
            {
                _probe.ExpectNoMsg(duration);
                return this;
            }

            /// <summary>
            /// Receive messages for a given duration or until one does not match a given partial function.
            /// </summary>
            public IEnumerable<TOther> ReceiveWhile<TOther>(TimeSpan? max = null, TimeSpan? idle = null, Func<object, TOther> filter = null, int msgs = int.MaxValue) where TOther : class
            {
                return _probe.ReceiveWhile(max, idle, filter, msgs);
            }

            public IPublisherEvent ExpectEvent()
            {
                return _probe.ExpectMsg<IPublisherEvent>();
            }

            void IPublisher.Subscribe(ISubscriber subscriber)
            {
                Subscribe((ISubscriber<T>)subscriber);
            }
        }

        /// <summary>
        /// Single subscription and demand tracking for <see cref="ManualProbe{T}"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public class Probe<T> : ManualProbe<T>
        {
            private readonly long _initialPendingRequests;
            private readonly Lazy<StreamTestKit.PublisherProbeSubscription<T>> _subscription;

            internal Probe(TestKitBase system, long initialPendingRequests) : base(system)
            {
                _initialPendingRequests = Pending = initialPendingRequests;
                _subscription = new Lazy<StreamTestKit.PublisherProbeSubscription<T>>(ExpectSubscription);
            }

            /// <summary>
            /// Current pending requests.
            /// </summary>
            public long Pending { get; private set; }

            /// <summary>
            /// Asserts that a subscription has been received or will be received
            /// </summary>
            public void EnsureSubscription()
            {
                var _ = _subscription.Value;
            }

            public Probe<T> SendNext(T element)
            {
                var sub = _subscription.Value;
                if (Pending == 0) Pending = sub.ExpectRequest();
                Pending--;
                sub.SendNext(element);
                return this;
            }

            public Probe<T> UnsafeSendNext(T element)
            {
                _subscription.Value.SendNext(element);
                return this;
            }

            public Probe<T> SendComplete()
            {
                _subscription.Value.SendComplete();
                return this;
            }

            public Probe<T> SendError(Exception e)
            {
                _subscription.Value.SendError(e);
                return this;
            }

            public long ExpectRequest()
            {
                return _subscription.Value.ExpectRequest();
            }

            public Probe<T> ExpectCancellation()
            {
                _subscription.Value.ExpectCancellation();
                return this;
            }
        }

        internal sealed class LazyEmptyPublisher<T> : IPublisher<T>
        {
            public static readonly IPublisher<T> Instance = new LazyEmptyPublisher<T>();
            private LazyEmptyPublisher() { }

            public void Subscribe(ISubscriber<T> subscriber)
            {
                subscriber.OnSubscribe(new StreamTestKit.CompletedSubscription<T>(subscriber));
            }

            public override string ToString()
            {
                return "soon-to-complete-publisher";
            }

            void IPublisher.Subscribe(ISubscriber subscriber)
            {
                Subscribe((ISubscriber<T>) subscriber);
            }
        }

        internal sealed class LazyErrorPublisher<T> : IPublisher<T>
        {
            public readonly string Name;
            public readonly Exception Cause;

            public LazyErrorPublisher(Exception cause, string name)
            {
                Name = name;
                Cause = cause;
            }

            public void Subscribe(ISubscriber<T> subscriber)
            {
                subscriber.OnSubscribe(new StreamTestKit.FailedSubscription<T>(subscriber, Cause));
            }

            public override string ToString()
            {
                return Name;
            }

            void IPublisher.Subscribe(ISubscriber subscriber)
            {
                Subscribe((ISubscriber<T>) subscriber);
            }
        }

        /// <summary>
        /// Publisher that signals complete to subscribers, after handing a void subscription.
        /// </summary>
        public static IPublisher<T> Empty<T>()
        {
            return EmptyPublisher<T>.Instance;
        }

        /// <summary>
        /// Publisher that subscribes the subscriber and completes after the first request.
        /// </summary>
        public static IPublisher<T> LazyEmpty<T>()
        {
            return LazyEmptyPublisher<T>.Instance;
        }

        /// <summary>
        /// Publisher that signals error to subscribers immediately after handing out subscription.
        /// </summary>
        public static IPublisher<T> Error<T>(Exception exception)
        {
            return new ErrorPublisher<T>(exception, "error");
        }

        /// <summary>
        /// Publisher subscribes the subscriber and signals error after the first request.
        /// </summary>
        public static IPublisher<T> LazyError<T>(Exception exception)
        {
            return new LazyErrorPublisher<T>(exception, "error");
        }

        /// <summary>
        /// Probe that implements <see cref="IPublisher{T}"/> interface.
        /// </summary>
        public static ManualProbe<T> CreateManualProbe<T>(this TestKitBase testKit, bool autoOnSubscribe = true)
        {
            return new ManualProbe<T>(testKit, autoOnSubscribe);
        }

        public static Probe<T> CreateProbe<T>(this TestKitBase testKit, long initialPendingRequests = 0L)
        {
            return new Probe<T>(testKit, initialPendingRequests);
        }
    }
}