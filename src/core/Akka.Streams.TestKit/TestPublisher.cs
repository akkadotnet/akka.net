//-----------------------------------------------------------------------
// <copyright file="TestPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    public static partial class TestPublisher
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
        public partial class ManualProbe<T> : IPublisher<T>
        {
            private volatile StreamTestKit.PublisherProbeSubscription<T> _subscription_DoNotUseDirectly;
            public TestProbe Probe { get; }

            internal ManualProbe(TestKitBase system, bool autoOnSubscribe = true)
            {
                Probe = system.CreateTestProbe();
                AutoOnSubscribe = autoOnSubscribe;
            }

            public bool AutoOnSubscribe { get; }

            public IPublisher<T> Publisher => this;

            public StreamTestKit.PublisherProbeSubscription<T> Subscription
            {
#pragma warning disable CS0420
                get => Volatile.Read(ref _subscription_DoNotUseDirectly);
                protected set => Volatile.Write(ref _subscription_DoNotUseDirectly, value);
#pragma warning restore CS0420
            }
            
            /// <summary>
            /// Subscribes a given <paramref name="subscriber"/> to this probe.
            /// </summary>
            public void Subscribe(ISubscriber<T> subscriber)
            {
                var subscription = new StreamTestKit.PublisherProbeSubscription<T>(subscriber, Probe);
                Probe.Ref.Tell(new Subscribe(subscription));
                if (AutoOnSubscribe) subscriber.OnSubscribe(subscription);
            }

            /// <summary>
            /// Expect a subscription.
            /// </summary>
            public StreamTestKit.PublisherProbeSubscription<T> ExpectSubscription(
                CancellationToken cancellationToken = default)
                => ExpectSubscriptionTask(Probe, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect a subscription.
            /// </summary>
            public async Task<StreamTestKit.PublisherProbeSubscription<T>> ExpectSubscriptionAsync(
                CancellationToken cancellationToken = default)
                => await ExpectSubscriptionTask(Probe, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect demand from the given subscription.
            /// </summary>
            public async Task ExpectRequestAsync(
                ISubscription subscription, 
                int nrOfElements,
                CancellationToken cancellationToken = default)
                => await ExpectRequestTask(Probe, subscription, nrOfElements, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect no messages.
            /// </summary>
            public async Task ExpectNoMsgAsync(CancellationToken cancellationToken = default)
                => await ExpectNoMsgTask(Probe, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect no messages for given duration.
            /// </summary>
            public async Task ExpectNoMsgAsync(TimeSpan duration, CancellationToken cancellationToken = default)
                => await ExpectNoMsgTask(Probe, duration, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Receive messages for a given duration or until one does not match a given partial function.
            /// </summary>
            public IEnumerable<TOther> ReceiveWhile<TOther>(
                TimeSpan? max = null,
                TimeSpan? idle = null,
                Func<object, TOther> filter = null,
                int msgCount = int.MaxValue,
                CancellationToken cancellationToken = default) where TOther : class
                => ReceiveWhileTask(Probe, max, idle, filter, msgCount, cancellationToken).ToListAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Receive messages for a given duration or until one does not match a given partial function.
            /// </summary>
            public IAsyncEnumerable<TOther> ReceiveWhileAsync<TOther>(
                TimeSpan? max = null,
                TimeSpan? idle = null,
                Func<object, TOther> filter = null, 
                int msgCount = int.MaxValue,
                CancellationToken cancellationToken = default) where TOther : class
                => ReceiveWhileTask(Probe, max, idle, filter, msgCount, cancellationToken);

            /// <summary>
            /// Expect a publisher event from the stream.
            /// </summary>
            public IPublisherEvent ExpectEvent(CancellationToken cancellationToken = default)
                => ExpectEventTask(Probe, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect a publisher event from the stream.
            /// </summary>
            public async Task<IPublisherEvent> ExpectEventAsync(CancellationToken cancellationToken = default)
                => await ExpectEventTask(Probe, cancellationToken)
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
                => WithinAsync(min, max, async () => execute(), cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Execute code block while bounding its execution time between <paramref name="min"/> and
            /// <paramref name="max"/>. <see cref="WithinAsync{TOther}(TimeSpan,TimeSpan,Func{Task{TOther}},CancellationToken)"/> blocks may be nested. 
            /// All methods in this class which take maximum wait times are available in a version which implicitly uses
            /// the remaining time governed by the innermost enclosing <see cref="WithinAsync{TOther}(TimeSpan,TimeSpan,Func{Task{TOther}},CancellationToken)"/> block.
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
            /// <param name="actionAsync"></param>
            /// <param name="cancellationToken"></param>
            /// <returns></returns>
            public async Task<TOther> WithinAsync<TOther>(
                TimeSpan min,
                TimeSpan max,
                Func<Task<TOther>> actionAsync,
                CancellationToken cancellationToken = default)
                => await Probe.WithinAsync(min, max, actionAsync, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Sane as calling Within(TimeSpan.Zero, max, function, cancellationToken).
            /// </summary>
            public TOther Within<TOther>(TimeSpan max, Func<TOther> execute, CancellationToken cancellationToken = default)
                => WithinAsync(max, async () => execute(), cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            
            /// <summary>
            /// Sane as calling WithinAsync(TimeSpan.Zero, max, function, cancellationToken).
            /// </summary>
            public async Task<TOther> WithinAsync<TOther>(TimeSpan max, Func<Task<TOther>> execute, CancellationToken cancellationToken = default) 
                => await Probe.WithinAsync(max, execute, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
        }

        /// <summary>
        /// Single subscription and demand tracking for <see cref="ManualProbe{T}"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public partial class Probe<T> : ManualProbe<T>
        {
            private readonly long _initialPendingRequests;
            
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
                => EnsureSubscriptionTask(this, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            public async Task EnsureSubscriptionAsync(CancellationToken cancellationToken = default)
                => await EnsureSubscriptionTask(this, cancellationToken)
                    .ConfigureAwait(false);

            public async Task SendNextAsync(T element, CancellationToken cancellationToken = default)
                => await SendNextTask(this, element, cancellationToken)
                    .ConfigureAwait(false);
            
            public async Task UnsafeSendNextAsync(T element, CancellationToken cancellationToken = default)
                => await UnsafeSendNextTask(this, element, cancellationToken)
                    .ConfigureAwait(false);

            public async Task SendCompleteAsync(CancellationToken cancellationToken = default)
                => await SendCompleteTask(this, cancellationToken)
                    .ConfigureAwait(false);

            public async Task SendErrorAsync(Exception e, CancellationToken cancellationToken = default)
                => await SendErrorTask(this, e, cancellationToken)
                    .ConfigureAwait(false);

            public long ExpectRequest(CancellationToken cancellationToken = default)
                => ExpectRequestTask(this, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            public async Task<long> ExpectRequestAsync(CancellationToken cancellationToken = default)
                => await ExpectRequestTask(this, cancellationToken)
                    .ConfigureAwait(false);

            public async Task ExpectCancellationAsync(CancellationToken cancellationToken = default)
                => await ExpectCancellationTask(this, cancellationToken)
                    .ConfigureAwait(false);
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
