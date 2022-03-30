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
using System.Runtime.ExceptionServices;
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

            internal ManualProbe(TestKitBase testKit)
            {
                _testKit = testKit;
                TestProbe = testKit.CreateTestProbe();
            }

            private volatile ISubscription _subscription;

            public void OnSubscribe(ISubscription subscription) => TestProbe.Ref.Tell(new OnSubscribe(subscription));

            public void OnError(Exception cause) => TestProbe.Ref.Tell(new OnError(cause));

            public void OnComplete() => TestProbe.Ref.Tell(TestSubscriber.OnComplete.Instance);

            public void OnNext(T element) => TestProbe.Ref.Tell(new OnNext<T>(element));

            /// <summary>
            /// Expects and returnsReactive.Streams.ISubscription/>.
            /// </summary>
            public ISubscription ExpectSubscription(CancellationToken cancellationToken = default)
                => ExpectSubscriptionAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expects and returnsReactive.Streams.ISubscription/>.
            /// </summary>
            public async Task<ISubscription> ExpectSubscriptionAsync(CancellationToken cancellationToken = default)
            {
                var msg = await TestProbe.ExpectMsgAsync<OnSubscribe>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _subscription = msg.Subscription;
                return _subscription;
            }

            /// <summary>
            /// Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public ISubscriberEvent ExpectEvent(CancellationToken cancellationToken = default)
                => ExpectEventAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public async Task<ISubscriberEvent> ExpectEventAsync(CancellationToken cancellationToken = default) 
                => await TestProbe.ExpectMsgAsync<ISubscriberEvent>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public ISubscriberEvent ExpectEvent(TimeSpan max, CancellationToken cancellationToken = default)
                => ExpectEventAsync(max, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public async Task<ISubscriberEvent> ExpectEventAsync(
                TimeSpan? max,
                CancellationToken cancellationToken = default) 
                => await TestProbe.ExpectMsgAsync<ISubscriberEvent>(max, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect and return a stream element.
            /// </summary>
            public T ExpectNext(CancellationToken cancellationToken = default)
                => ExpectNextAsync(null, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect and return a stream element during specified time or timeout.
            /// </summary>
            public T ExpectNext(TimeSpan? timeout, CancellationToken cancellationToken = default)
                => ExpectNextAsync(timeout, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect and return a stream element.
            /// </summary>
            public async Task<T> ExpectNextAsync(CancellationToken cancellationToken = default)
                => await ExpectNextAsync(null, cancellationToken)
                    .ConfigureAwait(false);

            /// <summary>
            /// Expect and return a stream element during specified time or timeout.
            /// </summary>
            public async Task<T> ExpectNextAsync(TimeSpan? timeout, CancellationToken cancellationToken = default)
            {
                return await TestProbe.ReceiveOneAsync(timeout, cancellationToken) switch
                {
                    null => throw new Exception($"Expected OnNext(_), yet no element signaled during {timeout}"),
                    OnNext<T> message => message.Element,
                    var other => throw new Exception($"expected OnNext, found {other}")
                };
            }

            /// <summary>
            /// Expect and return the next <paramref name="n"/> stream elements.
            /// </summary>
            public IEnumerable<T> ExpectNextN(
                long n, 
                TimeSpan? timeout = null,
                CancellationToken cancellationToken = default)
                => ExpectNextNAsync(n, timeout, cancellationToken)
                    .ToListAsync(cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect and return the next <paramref name="n"/> stream elements.
            /// </summary>
            public async IAsyncEnumerable<T> ExpectNextNAsync(
                long n, 
                TimeSpan? timeout = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                for (var i = 0; i < n; i++)
                {
                    var next = await TestProbe.ExpectMsgAsync<OnNext<T>>(timeout, cancellationToken: cancellationToken);
                    yield return next.Element;
                }
            }

            /// <summary>
            /// Expect and return the signalled System.Exception/>.
            /// </summary>
            public Exception ExpectError(CancellationToken cancellationToken = default)
                => ExpectErrorAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect and return the signalled System.Exception/>.
            /// </summary>
            public async Task<Exception> ExpectErrorAsync(CancellationToken cancellationToken = default)
            {
                var msg = await TestProbe.ExpectMsgAsync<OnError>(cancellationToken: cancellationToken);
                return msg.Cause;
            }

            /// <summary>
            /// Expect subscription to be followed immediately by an error signal. By default single demand will be signaled in order to wake up a possibly lazy upstream. 
            /// <seealso cref="ExpectSubscriptionAndError(bool, CancellationToken)"/>
            /// </summary>
            public Exception ExpectSubscriptionAndError(CancellationToken cancellationToken = default) 
                => ExpectSubscriptionAndErrorAsync(true, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect subscription to be followed immediately by an error signal. By default single demand will be signaled in order to wake up a possibly lazy upstream. 
            /// <seealso cref="ExpectSubscriptionAndError(bool, CancellationToken)"/>
            /// </summary>
            public async Task<Exception> ExpectSubscriptionAndErrorAsync(CancellationToken cancellationToken = default) 
                => await ExpectSubscriptionAndErrorAsync(true, cancellationToken);

            /// <summary>
            /// Expect subscription to be followed immediately by an error signal. Depending on the `signalDemand` parameter demand may be signaled 
            /// immediately after obtaining the subscription in order to wake up a possibly lazy upstream.You can disable this by setting the `signalDemand` parameter to `false`.
            /// <seealso cref="ExpectSubscriptionAndError(CancellationToken)"/>
            /// </summary>
            public Exception ExpectSubscriptionAndError(
                bool signalDemand,
                CancellationToken cancellationToken = default)
                => ExpectSubscriptionAndErrorAsync(signalDemand, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect subscription to be followed immediately by an error signal. Depending on the `signalDemand` parameter demand may be signaled 
            /// immediately after obtaining the subscription in order to wake up a possibly lazy upstream.You can disable this by setting the `signalDemand` parameter to `false`.
            /// <seealso cref="ExpectSubscriptionAndError(CancellationToken)"/>
            /// </summary>
            public async Task<Exception> ExpectSubscriptionAndErrorAsync(
                bool signalDemand, 
                CancellationToken cancellationToken = default)
            {
                var sub = await ExpectSubscriptionAsync(cancellationToken);
                if(signalDemand)
                    sub.Request(1);

                return await ExpectErrorAsync(cancellationToken);
            }

            /// <summary>
            /// Expect given next element or error signal, returning whichever was signaled.
            /// </summary>
            public object ExpectNextOrError(CancellationToken cancellationToken = default)
                => ExpectNextOrErrorAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect given next element or error signal, returning whichever was signaled.
            /// </summary>
            public async Task<object> ExpectNextOrErrorAsync(CancellationToken cancellationToken = default)
            {
                var message = await TestProbe.FishForMessageAsync(
                    isMessage: m => m is OnNext<T> || m is OnError, 
                    hint: "OnNext(_) or error", 
                    cancellationToken: cancellationToken);

                return message switch
                {
                    OnNext<T> next => next.Element,
                    _ => ((OnError) message).Cause
                };
            }

            /// <summary>
            /// Expect given next element or stream completion, returning whichever was signaled.
            /// </summary>
            public object ExpectNextOrComplete(CancellationToken cancellationToken = default)
                => ExpectNextOrCompleteAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect given next element or stream completion, returning whichever was signaled.
            /// </summary>
            public async Task<object> ExpectNextOrCompleteAsync(CancellationToken cancellationToken = default)
            {
                var message = await TestProbe.FishForMessageAsync(
                    isMessage: m => m is OnNext<T> || m is OnComplete, 
                    hint: "OnNext(_) or OnComplete", 
                    cancellationToken: cancellationToken);

                return message switch
                {
                    OnNext<T> next => next.Element,
                    _ => message
                };
            }

            /// <summary>
            /// Expect next element and test it with the <paramref name="predicate"/>
            /// </summary>
            /// <typeparam name="TOther">The System.Type of the expected message</typeparam>
            /// <param name="predicate">The System.Predicate{T} that is applied to the message</param>
            /// <param name="cancellationToken"></param>
            /// <returns>The next element</returns>
            public TOther ExpectNext<TOther>(Predicate<TOther> predicate, CancellationToken cancellationToken = default)
                => ExpectNextAsync(predicate, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Expect next element and test it with the <paramref name="predicate"/>
            /// </summary>
            /// <typeparam name="TOther">The System.Type of the expected message</typeparam>
            /// <param name="predicate">The System.Predicate{T} that is applied to the message</param>
            /// <param name="cancellationToken"></param>
            /// <returns>The next element</returns>
            public async Task<TOther> ExpectNextAsync<TOther>(Predicate<TOther> predicate, CancellationToken cancellationToken = default)
            {
                var msg = await TestProbe.ExpectMsgAsync<OnNext<TOther>>(
                    isMessage: x => predicate(x.Element),
                    cancellationToken: cancellationToken);
                return msg.Element;
            }

            public TOther ExpectEvent<TOther>(
                Func<ISubscriberEvent, TOther> func,
                CancellationToken cancellationToken = default)
                => ExpectEventAsync(func, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            public async Task<TOther> ExpectEventAsync<TOther>(Func<ISubscriberEvent, TOther> func, CancellationToken cancellationToken = default)
            {
                var msg = await TestProbe.ExpectMsgAsync<ISubscriberEvent>(
                    hint: "message matching function",
                    cancellationToken: cancellationToken);
                return func(msg);
            }

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
                => ReceiveWithinAsync<TOther>(max, messages, cancellationToken)
                    .ToListAsync(cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

            /// <summary>
            /// Drains a given number of messages
            /// </summary>
            public IAsyncEnumerable<TOther> ReceiveWithinAsync<TOther>(
                TimeSpan? max,
                int messages = int.MaxValue,
                CancellationToken cancellationToken = default) 
            {
                return TestProbe.ReceiveWhileAsync(max, max, msg =>
                {
                    switch (msg)
                    {
                        case OnNext<TOther> onNext:
                            return onNext.Element;
                        case OnError onError:
                            ExceptionDispatchInfo.Capture(onError.Cause).Throw();
                            throw new Exception("Should never reach this code.", onError.Cause);
                        case var ex:
                            throw new Exception($"Expected OnNext or OnError, but found {ex.GetType()} instead");
                    }
                }, messages, cancellationToken);
            }
            
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
            public TOther Within<TOther>(TimeSpan min, TimeSpan max, Func<TOther> execute) => TestProbe.Within(min, max, execute);

            /// <summary>
            /// Sane as calling Within(TimeSpan.Zero, max, function).
            /// </summary>
            public TOther Within<TOther>(TimeSpan max, Func<TOther> execute) => TestProbe.Within(max, execute);

            /// <summary>
            /// Attempt to drain the stream into a strict collection (by requesting long.MaxValue elements).
            /// </summary>
            /// <remarks>
            /// Use with caution: Be warned that this may not be a good idea if the stream is infinite or its elements are very large!
            /// </remarks>
            public IList<T> ToStrict(TimeSpan atMost)
            {
                var deadline = DateTime.UtcNow + atMost;
                // if no subscription was obtained yet, we expect it
                if (_subscription == null) ExpectSubscription();
                _subscription.Request(long.MaxValue);

                var result = new List<T>();
                while (true)
                {
                    var e = ExpectEvent(TimeSpan.FromTicks(Math.Max(deadline.Ticks - DateTime.UtcNow.Ticks, 0)));
                    if (e is OnError error)
                        throw new ArgumentException(
                            $"ToStrict received OnError while draining stream! Accumulated elements: ${string.Join(", ", result)}",
                            error.Cause);
                    if (e is OnComplete)
                        break;
                    if (e is OnNext<T> next)
                        result.Add(next.Element);
                }
                return result;
            }
        }

        /// <summary>
        /// Single subscription tracking for <see cref="ManualProbe{T}"/>.
        /// </summary>
        public class Probe<T> : ManualProbe<T>
        {
            private ISubscription _subscription = null;

            internal Probe(TestKitBase testKit) : base(testKit)
            { }

            /// <summary>
            /// Asserts that a subscription has been received or will be received
            /// </summary>
            public Probe<T> EnsureSubscription(CancellationToken cancellationToken = default)
            {
                if (_subscription == null)
                    _subscription = ExpectSubscription(cancellationToken);
                return this;
            } 

            /// <summary>
            /// Asserts that a subscription has been received or will be received
            /// </summary>
            public async Task<Probe<T>> EnsureSubscriptionAsync(CancellationToken cancellationToken = default)
            {
                if (_subscription != null)
                    return this;
                
                _subscription = await ExpectSubscriptionAsync(cancellationToken)
                    .ConfigureAwait(false);
                return this;
            }

            public Probe<T> Request(long n)
            {
                EnsureSubscription();
                _subscription.Request(n);
                return this;
            }

            public Probe<T> RequestNext(T element)
            {
                EnsureSubscription();
                _subscription.Request(1);
                ExpectNext(element);
                return this;
            }

            public Probe<T> Cancel()
            {
                EnsureSubscription();
                _subscription.Cancel();
                return this;
            }

            /// <summary>
            /// Request and expect a stream element.
            /// </summary>
            public T RequestNext()
            {
                EnsureSubscription();
                _subscription.Request(1);
                return ExpectNext();
            }

            /// <summary>
            /// Request and expect a stream element during the specified time or timeout.
            /// </summary>
            public T RequestNext(TimeSpan timeout)
            {
                EnsureSubscription();
                _subscription.Request(1);
                return ExpectNext(timeout);
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
