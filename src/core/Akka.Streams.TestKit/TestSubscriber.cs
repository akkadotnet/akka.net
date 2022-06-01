//-----------------------------------------------------------------------
// <copyright file="TestSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Reactive.Streams;

namespace Akka.Streams.TestKit
{
    public static partial class TestSubscriber
    {
        #region messages

        public interface ISubscriberEvent : INoSerializationVerificationNeeded, IDeadLetterSuppression { }

        public struct OnSubscribe : ISubscriberEvent
        {
            public readonly ISubscription Subscription;

            public OnSubscribe(ISubscription subscription)
            {
                Subscription = subscription;
            }

            public override string ToString() => $"TestSubscriber.OnSubscribe({Subscription})";
        }

        public struct OnNext<T> : ISubscriberEvent, IEquatable<OnNext<T>>
        {
            public readonly T Element;

            public OnNext(T element)
            {
                Element = element;
            }

            public override string ToString() => $"TestSubscriber.OnNext({Element})";

            public bool Equals(OnNext<T> other)
            {
                return EqualityComparer<T>.Default.Equals(Element, other.Element);
            }

            public override bool Equals(object obj)
            {
                return obj is OnNext<T> other && Equals(other);
            }

            public override int GetHashCode()
            {
                return EqualityComparer<T>.Default.GetHashCode(Element);
            }
        }

        public sealed class OnComplete: ISubscriberEvent
        {
            public static readonly OnComplete Instance = new OnComplete();
            private OnComplete() { }

            public override string ToString() => "TestSubscriber.OnComplete";
        }

        public struct OnError : ISubscriberEvent
        {
            public readonly Exception Cause;

            public OnError(Exception cause)
            {
                Cause = cause;
            }
            public override string ToString() => $"TestSubscriber.OnError({Cause.Message})";
        }

        #endregion

        /// <summary>
        /// Implementation of Reactive.Streams.ISubscriber{T} that allows various assertions. All timeouts are dilated automatically, 
        /// for more details about time dilation refer to <see cref="TestKit"/>.
        /// </summary>
        public partial class ManualProbe<T> : ISubscriber<T>
        {
            private readonly TestKitBase _testKit;
            internal readonly TestProbe TestProbe;
            private volatile ISubscription _subscription_DoNotUseDirectly;

            internal ManualProbe(TestKitBase testKit)
            {
                _testKit = testKit;
                TestProbe = testKit.CreateTestProbe();
            }

            public ISubscription Subscription
            {
#pragma warning disable CS0420
                get => Volatile.Read(ref _subscription_DoNotUseDirectly);
                protected set => Volatile.Write(ref _subscription_DoNotUseDirectly, value);
#pragma warning restore CS0420
            }

            public void OnSubscribe(ISubscription subscription) => TestProbe.Ref.Tell(new OnSubscribe(subscription));

            public void OnError(Exception cause) => TestProbe.Ref.Tell(new OnError(cause));

            public void OnComplete() => TestProbe.Ref.Tell(TestSubscriber.OnComplete.Instance);

            public void OnNext(T element) => TestProbe.Ref.Tell(new OnNext<T>(element));

            /// <summary>
            /// Expects and returns Reactive.Streams.ISubscription/>.
            /// </summary>
            public ISubscription ExpectSubscription(CancellationToken cancellationToken = default)
                => ExpectSubscriptionTask(this, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expects and returns Reactive.Streams.ISubscription/>.
            /// </summary>
            public async Task<ISubscription> ExpectSubscriptionAsync(CancellationToken cancellationToken = default)
                => await ExpectSubscriptionTask(this, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public ISubscriberEvent ExpectEvent(CancellationToken cancellationToken = default)
                => ExpectEventTask(TestProbe, (TimeSpan?)null, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public async Task<ISubscriberEvent> ExpectEventAsync(CancellationToken cancellationToken = default)
                => await ExpectEventTask(TestProbe, (TimeSpan?)null, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public ISubscriberEvent ExpectEvent(TimeSpan max, CancellationToken cancellationToken = default)
                => ExpectEventTask(TestProbe, max, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public async Task<ISubscriberEvent> ExpectEventAsync(
                TimeSpan? max,
                CancellationToken cancellationToken = default) 
                => await ExpectEventTask(TestProbe, max, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect and return a stream element.
            /// </summary>
            public T ExpectNext(CancellationToken cancellationToken = default)
                => ExpectNextTask(TestProbe, null, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect and return a stream element during specified time or timeout.
            /// </summary>
            public T ExpectNext(TimeSpan? timeout, CancellationToken cancellationToken = default)
                => ExpectNextTask(TestProbe, timeout, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect and return a stream element.
            /// </summary>
            public async Task<T> ExpectNextAsync(CancellationToken cancellationToken = default)
                => await ExpectNextTask(TestProbe, null, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect and return a stream element during specified time or timeout.
            /// </summary>
            public async Task<T> ExpectNextAsync(TimeSpan? timeout, CancellationToken cancellationToken = default)
                => await ExpectNextTask(TestProbe, timeout, cancellationToken)
                    .ConfigureAwait(false);

            public async Task ExpectNextAsync(T element, CancellationToken cancellationToken = default)
                => await ExpectNextTask(probe: TestProbe, element: element, timeout: null, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            
            /// <summary>
            /// Expect and return the next <paramref name="n"/> stream elements.
            /// </summary>
            public IEnumerable<T> ExpectNextN(
                long n, 
                TimeSpan? timeout = null,
                CancellationToken cancellationToken = default)
                => ExpectNextNTask(TestProbe, n, timeout, cancellationToken)
                    .ToListAsync(cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect and return the next <paramref name="n"/> stream elements.
            /// </summary>
            public IAsyncEnumerable<T> ExpectNextNAsync(
                long n,
                TimeSpan? timeout = null,
                CancellationToken cancellationToken = default)
                => ExpectNextNTask(TestProbe, n, timeout, cancellationToken);

            /// <summary>
            /// Assert that no message is received for the specified time.
            /// </summary>
            public async Task ExpectNoMsgAsync(TimeSpan remaining, CancellationToken cancellationToken = default)
            {
                await TestProbe.ExpectNoMsgAsync(remaining, cancellationToken)
                    .ConfigureAwait(false);
            }
            
            /// <summary>
            /// Expect and return the signalled System.Exception/>.
            /// </summary>
            public Exception ExpectError(CancellationToken cancellationToken = default)
                => ExpectErrorTask(TestProbe, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect and return the signalled System.Exception/>.
            /// </summary>
            public async Task<Exception> ExpectErrorAsync(CancellationToken cancellationToken = default)
                => await ExpectErrorTask(TestProbe, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect subscription to be followed immediately by an error signal. By default single demand will be signaled in order to wake up a possibly lazy upstream. 
            /// <seealso cref="ExpectSubscriptionAndError(bool, CancellationToken)"/>
            /// </summary>
            public Exception ExpectSubscriptionAndError(CancellationToken cancellationToken = default) 
                => ExpectSubscriptionAndErrorTask(this, true, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect subscription to be followed immediately by an error signal. By default single demand will be signaled in order to wake up a possibly lazy upstream. 
            /// <seealso cref="ExpectSubscriptionAndError(bool, CancellationToken)"/>
            /// </summary>
            public async Task<Exception> ExpectSubscriptionAndErrorAsync(CancellationToken cancellationToken = default) 
                => await ExpectSubscriptionAndErrorTask(this, true, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect subscription to be followed immediately by an error signal. Depending on the `signalDemand` parameter demand may be signaled 
            /// immediately after obtaining the subscription in order to wake up a possibly lazy upstream.You can disable this by setting the `signalDemand` parameter to `false`.
            /// <seealso cref="ExpectSubscriptionAndError(CancellationToken)"/>
            /// </summary>
            public Exception ExpectSubscriptionAndError(
                bool signalDemand,
                CancellationToken cancellationToken = default)
                => ExpectSubscriptionAndErrorTask(this, signalDemand, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect subscription to be followed immediately by an error signal. Depending on the `signalDemand` parameter demand may be signaled 
            /// immediately after obtaining the subscription in order to wake up a possibly lazy upstream.You can disable this by setting the `signalDemand` parameter to `false`.
            /// <seealso cref="ExpectSubscriptionAndError(CancellationToken)"/>
            /// </summary>
            public async Task<Exception> ExpectSubscriptionAndErrorAsync(
                bool signalDemand,
                CancellationToken cancellationToken = default)
                => await ExpectSubscriptionAndErrorTask(this, signalDemand, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect given next element or error signal, returning whichever was signaled.
            /// </summary>
            public object ExpectNextOrError(CancellationToken cancellationToken = default)
                => ExpectNextOrErrorTask(TestProbe, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect given next element or error signal, returning whichever was signaled.
            /// </summary>
            public async Task<object> ExpectNextOrErrorAsync(CancellationToken cancellationToken = default)
                => await ExpectNextOrErrorTask(TestProbe, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect given next element or stream completion, returning whichever was signaled.
            /// </summary>
            public object ExpectNextOrComplete(CancellationToken cancellationToken = default)
                => ExpectNextOrCompleteTask(TestProbe, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect given next element or stream completion, returning whichever was signaled.
            /// </summary>
            public async Task<object> ExpectNextOrCompleteAsync(CancellationToken cancellationToken = default)
                => await ExpectNextOrCompleteTask(TestProbe, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect next element and test it with the <paramref name="predicate"/>
            /// </summary>
            /// <typeparam name="TOther">The System.Type of the expected message</typeparam>
            /// <param name="predicate">The System.Predicate{T} that is applied to the message</param>
            /// <param name="cancellationToken"></param>
            /// <returns>The next element</returns>
            public TOther ExpectNext<TOther>(Predicate<TOther> predicate, CancellationToken cancellationToken = default)
                => ExpectNextTask(TestProbe, predicate, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect next element and test it with the <paramref name="predicate"/>
            /// </summary>
            /// <typeparam name="TOther">The System.Type of the expected message</typeparam>
            /// <param name="predicate">The System.Predicate{T} that is applied to the message</param>
            /// <param name="cancellationToken"></param>
            /// <returns>The next element</returns>
            public async Task<TOther> ExpectNextAsync<TOther>(
                Predicate<TOther> predicate,
                CancellationToken cancellationToken = default)
                => await ExpectNextTask(TestProbe, predicate, cancellationToken)
                    .ConfigureAwait(false);

            public TOther ExpectEvent<TOther>(
                Func<ISubscriberEvent, TOther> func,
                CancellationToken cancellationToken = default)
                => ExpectEventTask(TestProbe, func, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            public async Task<TOther> ExpectEventAsync<TOther>(
                Func<ISubscriberEvent, TOther> func,
                CancellationToken cancellationToken = default)
                => await ExpectEventTask(TestProbe, func, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Receive messages for a given duration or until one does not match a given partial function.
            /// </summary>
            public IEnumerable<TOther> ReceiveWhile<TOther>(
                TimeSpan? max = null,
                TimeSpan? idle = null,
                Func<object, TOther> filter = null,
                int msgs = int.MaxValue,
                CancellationToken cancellationToken = default)
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
                CancellationToken cancellationToken = default)
                => TestProbe.ReceiveWhileAsync(max, idle, filter, msgs, cancellationToken);

            /// <summary>
            /// Drains a given number of messages
            /// </summary>
            public IEnumerable<TOther> ReceiveWithin<TOther>(TimeSpan? max, int messages = int.MaxValue,
                CancellationToken cancellationToken = default)
                => ReceiveWithinTask<TOther>(TestProbe, max, messages, cancellationToken)
                    .ToListAsync(cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Drains a given number of messages
            /// </summary>
            public IAsyncEnumerable<TOther> ReceiveWithinAsync<TOther>(
                TimeSpan? max,
                int messages = int.MaxValue,
                CancellationToken cancellationToken = default)
                => ReceiveWithinTask<TOther>(TestProbe, max, messages, cancellationToken);
            
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
            public TOther Within<TOther>(TimeSpan min, TimeSpan max, Func<TOther> execute, CancellationToken cancellationToken = default) => 
                TestProbe.Within(min, max, execute, cancellationToken: cancellationToken);

            public async Task<TOther> WithinAsync<TOther>(TimeSpan min, TimeSpan max, Func<Task<TOther>> execute, CancellationToken cancellationToken = default) => 
                await TestProbe.WithinAsync(min, max, execute, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Sane as calling Within(TimeSpan.Zero, max, function).
            /// </summary>
            public TOther Within<TOther>(TimeSpan max, Func<TOther> execute, CancellationToken cancellationToken = default) =>
                TestProbe.Within(max, execute, cancellationToken: cancellationToken);

            /// <summary>
            /// Sane as calling WithinAsync(TimeSpan.Zero, max, function).
            /// </summary>
            public async Task<TOther> WithinAsync<TOther>(TimeSpan max, Func<Task<TOther>> execute, CancellationToken cancellationToken = default) => 
                await TestProbe.WithinAsync(max, execute, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            public async Task WithinAsync(
                TimeSpan max,
                Func<Task> actionAsync,
                TimeSpan? epsilonValue = null,
                CancellationToken cancellationToken = default)
                => await TestProbe.WithinAsync(max, actionAsync, epsilonValue, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Attempt to drain the stream into a strict collection (by requesting long.MaxValue elements).
            /// </summary>
            /// <remarks>
            /// Use with caution: Be warned that this may not be a good idea if the stream is infinite or its elements are very large!
            /// </remarks>
            public IList<T> ToStrict(TimeSpan atMost, CancellationToken cancellationToken = default)
                => ToStrictTask(this, atMost, cancellationToken).ToListAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Attempt to drain the stream into a strict collection (by requesting long.MaxValue elements).
            /// </summary>
            /// <remarks>
            /// Use with caution: Be warned that this may not be a good idea if the stream is infinite or its elements are very large!
            /// </remarks>
            public IAsyncEnumerable<T> ToStrictAsync(
                TimeSpan atMost,
                CancellationToken cancellationToken = default)
                => ToStrictTask(this, atMost, cancellationToken);

        }

        /// <summary>
        /// Single subscription tracking for <see cref="ManualProbe{T}"/>.
        /// </summary>
        public partial class Probe<T> : ManualProbe<T>
        {
            internal Probe(TestKitBase testKit) : base(testKit)
            { }

            /// <summary>
            /// Ensure that the probe has received or will receive a subscription
            /// </summary>
            public Probe<T> EnsureSubscription(CancellationToken cancellationToken = default)
            {
                EnsureSubscriptionTask(this, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            } 

            /// <summary>
            /// Ensure that the probe has received or will receive a subscription
            /// </summary>
            public async Task EnsureSubscriptionAsync(CancellationToken cancellationToken = default)
                => await EnsureSubscriptionTask(this, cancellationToken)
                    .ConfigureAwait(false);

            public async Task RequestAsync(long n)
            {
                await EnsureSubscriptionAsync();
                Subscription.Request(n);
            }

            public async Task RequestNextAsync(T element)
            {
                await EnsureSubscriptionAsync();
                Subscription.Request(1);
                await ExpectNextAsync(element);
            }

            public async Task CancelAsync()
            {
                await EnsureSubscriptionAsync();
                Subscription.Cancel();
            }

            /// <summary>
            /// Request and expect a stream element.
            /// </summary>
            public T RequestNext()
            {
                EnsureSubscription();
                Subscription.Request(1);
                return ExpectNext();
            }

            /// <summary>
            /// Request and expect a stream element.
            /// </summary>
            public async Task<T> RequestNextAsync()
            {
                await EnsureSubscriptionAsync();
                Subscription.Request(1);
                return await ExpectNextAsync();
            }

            /// <summary>
            /// Request and expect a stream element during the specified time or timeout.
            /// </summary>
            public T RequestNext(TimeSpan timeout)
            {
                EnsureSubscription();
                Subscription.Request(1);
                return ExpectNext(timeout);
            }
            
            public async Task<T> RequestNextAsync(TimeSpan timeout)
            {
                await EnsureSubscriptionAsync();
                Subscription.Request(1);
                return await ExpectNextAsync(timeout);
            }
            
        }

        public static ManualProbe<T> CreateManualSubscriberProbe<T>(this TestKitBase testKit)
        {
            return new ManualProbe<T>(testKit);
        }

        public static Probe<T> CreateSubscriberProbe<T>(this TestKitBase testKit)
        {
            return new Probe<T>(testKit);
        }
    }
}
