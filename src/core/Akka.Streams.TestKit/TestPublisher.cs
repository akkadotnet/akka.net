//-----------------------------------------------------------------------
// <copyright file="TestPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        /// Implementation of Reactive.Streams.IPublisher{T} that allows various assertions.
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
            public StreamTestKit.PublisherProbeSubscription<T> ExpectSubscription(
                CancellationToken cancellationToken = default)
                => ExpectSubscriptionAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect a subscription.
            /// </summary>
            public async Task<StreamTestKit.PublisherProbeSubscription<T>> ExpectSubscriptionAsync(
                CancellationToken cancellationToken = default)
            {
                var msg = await _probe.ExpectMsgAsync<Subscribe>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false); 
                return (StreamTestKit.PublisherProbeSubscription<T>) msg.Subscription;
            }

            /// <summary>
            /// Expect demand from the given subscription.
            /// </summary>
            public void ExpectRequest(
                ISubscription subscription,
                int n,
                CancellationToken cancellationToken = default)
                => ExpectRequestAsync(subscription, n, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            
            /// <summary>
            /// Expect demand from the given subscription.
            /// </summary>
            public async Task ExpectRequestAsync(
                ISubscription subscription, 
                int n,
                CancellationToken cancellationToken = default)
            {
                await _probe.ExpectMsgAsync<RequestMore>(
                    isMessage: x => x.NrOfElements == n && x.Subscription == subscription, 
                    cancellationToken: cancellationToken);
            }

            /// <summary>
            /// Expect no messages.
            /// </summary>
            public void ExpectNoMsg(CancellationToken cancellationToken = default)
                => ExpectNoMsgAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect no messages.
            /// </summary>
            public async Task ExpectNoMsgAsync(CancellationToken cancellationToken = default)
            {
                await _probe.ExpectNoMsgAsync(cancellationToken);
            }

            /// <summary>
            /// Expect no messages for given duration.
            /// </summary>
            public void ExpectNoMsg(TimeSpan duration, CancellationToken cancellationToken = default)
                => ExpectNoMsgAsync(duration, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect no messages for given duration.
            /// </summary>
            public async Task ExpectNoMsgAsync(TimeSpan duration, CancellationToken cancellationToken = default)
            {
                await _probe.ExpectNoMsgAsync(duration, cancellationToken);
            }

            /// <summary>
            /// Receive messages for a given duration or until one does not match a given partial function.
            /// </summary>
            public IEnumerable<TOther> ReceiveWhile<TOther>(
                TimeSpan? max = null,
                TimeSpan? idle = null,
                Func<object, TOther> filter = null,
                int msgs = int.MaxValue,
                CancellationToken cancellationToken = default) where TOther : class
                => ReceiveWhileAsync(max, idle, filter, msgs, cancellationToken)
                    .ToListAsync(cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Receive messages for a given duration or until one does not match a given partial function.
            /// </summary>
            public IAsyncEnumerable<TOther> ReceiveWhileAsync<TOther>(
                TimeSpan? max = null,
                TimeSpan? idle = null,
                Func<object, TOther> filter = null, 
                int msgs = int.MaxValue,
                CancellationToken cancellationToken = default) where TOther : class
                => _probe.ReceiveWhileAsync(max, idle, filter, msgs, cancellationToken);

            public IPublisherEvent ExpectEvent(CancellationToken cancellationToken = default)
                => ExpectEventAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            public async Task<IPublisherEvent> ExpectEventAsync(CancellationToken cancellationToken = default)
                => await _probe.ExpectMsgAsync<IPublisherEvent>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Execute code block while bounding its execution time between <paramref name="min"/> and
            /// <paramref name="max"/>. <see cref="Within{TOther}(TimeSpan,TimeSpan,Func{TOther},CancellationToken)"/> blocks may be nested. 
            /// All methods in this class which take maximum wait times are available in a version which implicitly uses
            /// the remaining time governed by the innermost enclosing <see cref="Within{TOther}(TimeSpan,TimeSpan,Func{TOther},CancellationToken)"/> block.
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
            /// <param name="cancellationToken"></param>
            /// <returns></returns>
            public TOther Within<TOther>(
                TimeSpan min,
                TimeSpan max,
                Func<TOther> execute,
                CancellationToken cancellationToken = default)
                => WithinAsync(min, max, execute, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Execute code block while bounding its execution time between <paramref name="min"/> and
            /// <paramref name="max"/>. <see cref="WithinAsync{TOther}(TimeSpan,TimeSpan,Func{TOther},CancellationToken)"/> blocks may be nested. 
            /// All methods in this class which take maximum wait times are available in a version which implicitly uses
            /// the remaining time governed by the innermost enclosing <see cref="WithinAsync{TOther}(TimeSpan,TimeSpan,Func{TOther},CancellationToken)"/> block.
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
            /// <param name="cancellationToken"></param>
            /// <returns></returns>
            public async Task<TOther> WithinAsync<TOther>(
                TimeSpan min,
                TimeSpan max,
                Func<TOther> execute,
                CancellationToken cancellationToken = default)
                => await _probe.WithinAsync(min, max, execute, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            
            /// <summary>
            /// Execute code block while bounding its execution time between <paramref name="min"/> and
            /// <paramref name="max"/>. <see cref="WithinAsync{TOther}(TimeSpan,TimeSpan,Func{TOther},CancellationToken)"/> blocks may be nested. 
            /// All methods in this class which take maximum wait times are available in a version which implicitly uses
            /// the remaining time governed by the innermost enclosing <see cref="WithinAsync{TOther}(TimeSpan,TimeSpan,Func{TOther},CancellationToken)"/> block.
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
            /// <param name="cancellationToken"></param>
            /// <returns></returns>
            public async Task<TOther> WithinAsync<TOther>(
                TimeSpan min,
                TimeSpan max,
                Func<Task<TOther>> actionAsync,
                CancellationToken cancellationToken = default)
                => await _probe.WithinAsync(min, max, actionAsync, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Sane as calling Within(TimeSpan.Zero, max, function, cancellationToken).
            /// </summary>
            public TOther Within<TOther>(TimeSpan max, Func<TOther> execute)
                => WithinAsync(max, execute)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            
            /// <summary>
            /// Sane as calling WithinAsync(TimeSpan.Zero, max, function, cancellationToken).
            /// </summary>
            public async Task<TOther> WithinAsync<TOther>(TimeSpan max, Func<TOther> execute, CancellationToken cancellationToken = default) 
                => await _probe.WithinAsync(max, execute, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            
            /// <summary>
            /// Sane as calling WithinAsync(TimeSpan.Zero, max, function, cancellationToken).
            /// </summary>
            public async Task<TOther> WithinAsync<TOther>(TimeSpan max, Func<Task<TOther>> execute, CancellationToken cancellationToken = default) 
                => await _probe.WithinAsync(max, execute, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
        }

        /// <summary>
        /// Single subscription and demand tracking for <see cref="ManualProbe{T}"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public class Probe<T> : ManualProbe<T>
        {
            private readonly long _initialPendingRequests;
            private StreamTestKit.PublisherProbeSubscription<T> _subscription = null;

            internal Probe(TestKitBase system, long initialPendingRequests) : base(system)
            {
                _initialPendingRequests = Pending = initialPendingRequests;
            }

            /// <summary>
            /// Current pending requests.
            /// </summary>
            public long Pending { get; private set; }

            /// <summary>
            /// Asserts that a subscription has been received or will be received
            /// </summary>
            public void EnsureSubscription(CancellationToken cancellationToken = default)
            {
                if(_subscription == null)
                    _subscription = ExpectSubscription(cancellationToken);
            }

            public async Task EnsureSubscriptionAsync(CancellationToken cancellationToken = default)
            {
                if(_subscription == null)
                    _subscription = await ExpectSubscriptionAsync(cancellationToken)
                        .ConfigureAwait(false);
            }
            public Probe<T> SendNext(T element)
            {
                EnsureSubscription();
                var sub = _subscription;
                if (Pending == 0)
                    Pending = sub.ExpectRequest();
                Pending--;
                sub.SendNext(element);
                return this;
            }

            public Probe<T> UnsafeSendNext(T element)
            {
                EnsureSubscription();
                _subscription.SendNext(element);
                return this;
            }

            public Probe<T> SendComplete()
            {
                EnsureSubscription();
                _subscription.SendComplete();
                return this;
            }

            public Probe<T> SendError(Exception e)
            {
                EnsureSubscription();
                _subscription.SendError(e);
                return this;
            }

            public long ExpectRequest(CancellationToken cancellationToken = default)
                => ExpectRequestAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            public async Task<long> ExpectRequestAsync(CancellationToken cancellationToken = default)
            {
                await EnsureSubscriptionAsync(cancellationToken);
                var requests = await _subscription.ExpectRequestAsync(cancellationToken)
                    .ConfigureAwait(false);
                Pending += requests;
                return requests;
            }

            public void ExpectCancellation(CancellationToken cancellationToken = default)
                => ExpectCancellationAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            
            public async Task ExpectCancellationAsync(CancellationToken cancellationToken = default)
            {
                await EnsureSubscriptionAsync(cancellationToken);
                await _subscription.ExpectCancellationAsync(cancellationToken);
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
        /// Probe that implements Reactive.Streams.IPublisher{T} interface.
        /// </summary>
        public static ManualProbe<T> CreateManualPublisherProbe<T>(this TestKitBase testKit, bool autoOnSubscribe = true) 
            => new ManualProbe<T>(testKit, autoOnSubscribe);

        public static Probe<T> CreatePublisherProbe<T>(this TestKitBase testKit, long initialPendingRequests = 0L)
            => new Probe<T>(testKit, initialPendingRequests);
    }
}
