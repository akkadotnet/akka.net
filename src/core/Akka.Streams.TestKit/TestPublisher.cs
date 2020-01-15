//-----------------------------------------------------------------------
// <copyright file="TestPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Implementation;
using Akka.TestKit;
using Reactive.Streams;

namespace Akka.Streams.TestKit
{
    /// <summary>
    /// Provides factory methods for various Publishers.
    /// </summary>
    public static class TestPublisher
    {
        #region messages

        public interface IPublisherEvent : INoSerializationVerificationNeeded, IDeadLetterSuppression { }

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

            public bool AutoOnSubscribe { get; }

            public IPublisher<T> Publisher => this;

            /// <summary>
            /// Subscribes a given <paramref name="subscriber"/> to this probe.
            /// </summary>
            public void Subscribe(ISubscriber<T> subscriber)
            {
                var subscription = new StreamTestKit.PublisherProbeSubscription<T>(subscriber, _probe);
                _probe.Ref.Tell(new Subscribe(subscription));
                if (AutoOnSubscribe) subscriber.OnSubscribe(subscription);
            }

            /// <summary>
            /// Expect a subscription.
            /// </summary>
            public StreamTestKit.PublisherProbeSubscription<T> ExpectSubscription() =>
                (StreamTestKit.PublisherProbeSubscription<T>)_probe.ExpectMsg<Subscribe>().Subscription;

            /// <summary>
            /// Expect demand from the given subscription.
            /// </summary>
            public ManualProbe<T> ExpectRequest(ISubscription subscription, int n)
            {
                _probe.ExpectMsg<RequestMore>(x => x.NrOfElements == n && x.Subscription == subscription);
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

            public IPublisherEvent ExpectEvent() => _probe.ExpectMsg<IPublisherEvent>();

            /// <summary>
            /// Execute code block while bounding its execution time between <paramref name="min"/> and
            /// <paramref name="max"/>. <see cref="Within{TOther}(TimeSpan,TimeSpan,Func{TOther})"/> blocks may be nested. 
            /// All methods in this class which take maximum wait times are available in a version which implicitly uses
            /// the remaining time governed by the innermost enclosing <see cref="Within{TOther}(TimeSpan,TimeSpan,Func{TOther})"/> block.
            /// 
            /// <para />
            /// 
            /// Note that the timeout is scaled using <see cref="TestKitBase.Dilated"/>, which uses the
            /// configuration entry "akka.test.timefactor", while the min Duration is not.
            /// 
            /// <![CDATA[
            /// var ret = probe.Within(Timespan.FromMilliseconds(50), () =>
            /// {
            ///     test.Tell("ping");
            ///     return ExpectMsg<string>();
            /// });
            /// ]]>
            /// </summary>
            /// <param name="min"></param>
            /// <param name="max"></param>
            /// <param name="execute"></param>
            /// <returns></returns>
            public TOther Within<TOther>(TimeSpan min, TimeSpan max, Func<TOther> execute) => _probe.Within(min, max, execute);

            /// <summary>
            /// Sane as calling Within(TimeSpan.Zero, max, function).
            /// </summary>
            public TOther Within<TOther>(TimeSpan max, Func<TOther> execute) => _probe.Within(max, execute);
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
                if (Pending == 0)
                    Pending = sub.ExpectRequest();
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
                var requests = _subscription.Value.ExpectRequest();
                Pending += requests;
                return requests;
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
                => subscriber.OnSubscribe(new StreamTestKit.CompletedSubscription<T>(subscriber));

            public override string ToString() => "soon-to-complete-publisher";
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
                => subscriber.OnSubscribe(new StreamTestKit.FailedSubscription<T>(subscriber, Cause));

            public override string ToString() => Name;
        }

        /// <summary>
        /// Publisher that signals complete to subscribers, after handing a void subscription.
        /// </summary>
        public static IPublisher<T> Empty<T>() => EmptyPublisher<T>.Instance;

        /// <summary>
        /// Publisher that subscribes the subscriber and completes after the first request.
        /// </summary>
        public static IPublisher<T> LazyEmpty<T>() => LazyEmptyPublisher<T>.Instance;

        /// <summary>
        /// Publisher that signals error to subscribers immediately after handing out subscription.
        /// </summary>
        public static IPublisher<T> Error<T>(Exception exception) => new ErrorPublisher<T>(exception, "error");

        /// <summary>
        /// Publisher subscribes the subscriber and signals error after the first request.
        /// </summary>
        public static IPublisher<T> LazyError<T>(Exception exception) => new LazyErrorPublisher<T>(exception, "error");

        /// <summary>
        /// Probe that implements <see cref="IPublisher{T}"/> interface.
        /// </summary>
        public static ManualProbe<T> CreateManualPublisherProbe<T>(this TestKitBase testKit, bool autoOnSubscribe = true) 
            => new ManualProbe<T>(testKit, autoOnSubscribe);

        public static Probe<T> CreatePublisherProbe<T>(this TestKitBase testKit, long initialPendingRequests = 0L)
            => new Probe<T>(testKit, initialPendingRequests);
    }
}
