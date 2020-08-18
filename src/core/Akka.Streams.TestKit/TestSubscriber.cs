//-----------------------------------------------------------------------
// <copyright file="TestSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Actors;
using Akka.TestKit;
using Reactive.Streams;

namespace Akka.Streams.TestKit
{
    public static class TestSubscriber
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

        public struct OnNext<T> : ISubscriberEvent
        {
            public readonly T Element;

            public OnNext(T element)
            {
                Element = element;
            }

            public override string ToString() => $"TestSubscriber.OnNext({Element})";
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
        /// Implementation of <see cref="ISubscriber{T}"/> that allows various assertions. All timeouts are dilated automatically, 
        /// for more details about time dilation refer to <see cref="TestKit"/>.
        /// </summary>
        public class ManualProbe<T> : ISubscriber<T>
        {
            private readonly TestKitBase _testKit;
            private readonly TestProbe _probe;

            internal ManualProbe(TestKitBase testKit)
            {
                _testKit = testKit;
                _probe = testKit.CreateTestProbe();
            }

            private volatile ISubscription _subscription;

            public void OnSubscribe(ISubscription subscription) => _probe.Ref.Tell(new OnSubscribe(subscription));

            public void OnError(Exception cause) => _probe.Ref.Tell(new OnError(cause));

            public void OnComplete() => _probe.Ref.Tell(TestSubscriber.OnComplete.Instance);

            public void OnNext(T element) => _probe.Ref.Tell(new OnNext<T>(element));

            /// <summary>
            /// Expects and returns <see cref="ISubscription"/>.
            /// </summary>
            public ISubscription ExpectSubscription()
            {
                _subscription = _probe.ExpectMsg<OnSubscribe>().Subscription;
                return _subscription;
            }

            /// <summary>
            /// Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public ISubscriberEvent ExpectEvent() => _probe.ExpectMsg<ISubscriberEvent>();

            /// <summary>
            /// Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public ISubscriberEvent ExpectEvent(TimeSpan max) => _probe.ExpectMsg<ISubscriberEvent>(max);

            /// <summary>
            /// Fluent DSL. Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public ManualProbe<T> ExpectEvent(ISubscriberEvent e)
            {
                _probe.ExpectMsg(e);
                return this;
            }

            /// <summary>
            /// Expect and return a stream element.
            /// </summary>
            public T ExpectNext()
            {
                return ExpectNext(_testKit.Dilated(_probe.TestKitSettings.SingleExpectDefault));
            }

            /// <summary>
            /// Expect and return a stream element during specified time or timeout.
            /// </summary>
            public T ExpectNext(TimeSpan timeout)
            {
                var t = _probe.RemainingOrDilated(timeout);
                switch (_probe.ReceiveOne(t))
                {
                    case null:
                        throw new Exception($"Expected OnNext(_), yet no element signaled during {timeout}");
                    case OnNext<T> message:
                        return message.Element;
                    case var other:
                        throw new Exception($"expected OnNext, found {other}");
                }
            }

            /// <summary>
            /// Fluent DSL. Expect a stream element.
            /// </summary>
            public ManualProbe<T> ExpectNext(T element, TimeSpan? timeout = null)
            {
                _probe.ExpectMsg<OnNext<T>>(x => AssertEquals(x.Element, element, "Expected '{0}', but got '{1}'", element, x.Element), timeout);
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect a stream element during specified time or timeout.
            /// </summary>
            public ManualProbe<T> ExpectNext(TimeSpan timeout, T element)
            {
                _probe.ExpectMsg<OnNext<T>>(x => AssertEquals(x.Element, element, "Expected '{0}', but got '{1}'", element, x.Element), timeout);
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect a stream element during specified timeout.
            /// </summary>
            public ManualProbe<T> ExpectNext(T element, TimeSpan timeout)
            {
                _probe.ExpectMsg<OnNext<T>>(x => AssertEquals(x.Element, element, "Expected '{0}', but got '{1}'", element, x.Element), timeout);
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect multiple stream elements.
            /// </summary>
            public ManualProbe<T> ExpectNext(T e1, T e2, params T[] elems)
                => ExpectNext(null, e1, e2, elems);

            public ManualProbe<T> ExpectNext(TimeSpan? timeout, T e1, T e2, params T[] elems)
            {
                var len = elems.Length + 2;
                var e = ExpectNextN(len, timeout).ToArray();
                AssertEquals(e.Length, len, "expected to get {0} events, but got {1}", len, e.Length);
                AssertEquals(e[0], e1, "expected [0] element to be {0} but found {1}", e1, e[0]);
                AssertEquals(e[1], e2, "expected [1] element to be {0} but found {1}", e2, e[1]);
                for (var i = 0; i < elems.Length; i++)
                {
                    var j = i + 2;
                    AssertEquals(e[j], elems[i], "expected [{2}] element to be {0} but found {1}", elems[i], e[j], j);
                }

                return this;
            }

            /// <summary>
            /// FluentDSL. Expect multiple stream elements in arbitrary order.
            /// </summary>
            public ManualProbe<T> ExpectNextUnordered(T e1, T e2, params T[] elems)
            {
                return ExpectNextUnordered(null, e1, e2, elems);
            }

            public ManualProbe<T> ExpectNextUnordered(TimeSpan? timeout, T e1, T e2, params T[] elems)
            {
                var len = elems.Length + 2;
                var e = ExpectNextN(len, timeout).ToArray();
                AssertEquals(e.Length, len, "expected to get {0} events, but got {1}", len, e.Length);

                var expectedSet = new HashSet<T>(elems) { e1, e2 };
                expectedSet.ExceptWith(e);

                Assert(expectedSet.Count == 0, "unexpected elements [{0}] found in the result", string.Join(", ", expectedSet));
                return this;
            }

            public ManualProbe<T> ExpectNextWithinSet(List<T> elems)
            {
                var next = _probe.ExpectMsg<OnNext<T>>();
                if(!elems.Contains(next.Element))
                    Assert(false, "unexpected elements [{0}] found in the result", next.Element);
                elems.Remove(next.Element);
                _probe.Log.Info($"Received '{next.Element}' within OnNext().");
                return this;
            }

            /// <summary>
            /// Expect and return the next <paramref name="n"/> stream elements.
            /// </summary>
            public IEnumerable<T> ExpectNextN(long n, TimeSpan? timeout = null)
            {
                var res = new List<T>((int)n);
                for (int i = 0; i < n; i++)
                {
                    var next = _probe.ExpectMsg<OnNext<T>>(timeout);
                    res.Add(next.Element);
                }
                return res;
            }

            /// <summary>
            /// Fluent DSL. Expect the given elements to be signalled in order.
            /// </summary>
            public ManualProbe<T> ExpectNextN(IEnumerable<T> all, TimeSpan? timeout = null)
            {
                foreach (var x in all)
                    _probe.ExpectMsg<OnNext<T>>(y => AssertEquals(y.Element, x, "Expected one of ({0}), but got '{1}'", string.Join(", ", all), y.Element), timeout);

                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect the given elements to be signalled in any order.
            /// </summary>
            public ManualProbe<T> ExpectNextUnorderedN(IEnumerable<T> all, TimeSpan? timeout = null)
            {
                var collection = new HashSet<T>(all);
                while (collection.Count > 0)
                {
                    var next = timeout.HasValue ? ExpectNext(timeout.Value) : ExpectNext();
                    Assert(collection.Contains(next), $"expected one of (${string.Join(", ", collection)}), but received {next}");
                    collection.Remove(next);
                }

                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect completion.
            /// </summary>
            public ManualProbe<T> ExpectComplete()
            {
                _probe.ExpectMsg<OnComplete>();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect completion with a timeout.
            /// </summary>
            public ManualProbe<T> ExpectComplete(TimeSpan timeout)
            {
                _probe.ExpectMsg<OnComplete>(timeout);
                return this;
            }

            /// <summary>
            /// Expect and return the signalled <see cref="Exception"/>.
            /// </summary>
            public Exception ExpectError() => _probe.ExpectMsg<OnError>().Cause;

            /// <summary>
            /// Expect subscription to be followed immediately by an error signal. By default single demand will be signaled in order to wake up a possibly lazy upstream. 
            /// <seealso cref="ExpectSubscriptionAndError(bool)"/>
            /// </summary>
            public Exception ExpectSubscriptionAndError() => ExpectSubscriptionAndError(true);

            /// <summary>
            /// Expect subscription to be followed immediately by an error signal. Depending on the `signalDemand` parameter demand may be signaled 
            /// immediately after obtaining the subscription in order to wake up a possibly lazy upstream.You can disable this by setting the `signalDemand` parameter to `false`.
            /// <seealso cref="ExpectSubscriptionAndError()"/>
            /// </summary>
            public Exception ExpectSubscriptionAndError(bool signalDemand)
            {
                var sub = ExpectSubscription();
                if(signalDemand)
                    sub.Request(1);

                return ExpectError();
            }

            /// <summary>
            /// Fluent DSL. Expect subscription followed by immediate stream completion. By default single demand will be signaled in order to wake up a possibly lazy upstream
            /// </summary>
            /// <seealso cref="ExpectSubscriptionAndComplete(bool)"/>
            public ManualProbe<T> ExpectSubscriptionAndComplete() => ExpectSubscriptionAndComplete(true);

            /// <summary>
            /// Fluent DSL. Expect subscription followed by immediate stream completion. Depending on the `signalDemand` parameter 
            /// demand may be signaled immediately after obtaining the subscription in order to wake up a possibly lazy upstream.
            /// You can disable this by setting the `signalDemand` parameter to `false`.
            /// </summary>
            /// <seealso cref="ExpectSubscriptionAndComplete()"/>
            public ManualProbe<T> ExpectSubscriptionAndComplete(bool signalDemand)
            {
                var sub = ExpectSubscription();
                if (signalDemand)
                    sub.Request(1);
                ExpectComplete();
                return this;
            }

            /// <summary>
            /// Expect given next element or error signal, returning whichever was signaled.
            /// </summary>
            public object ExpectNextOrError()
            {
                var message = _probe.FishForMessage(m => m is OnNext<T> || m is OnError, hint: "OnNext(_) or error");
                if (message is OnNext<T> next)
                    return next.Element;
                return ((OnError) message).Cause;
            }

            /// <summary>
            /// Fluent DSL. Expect given next element or error signal.
            /// </summary>
            public ManualProbe<T> ExpectNextOrError(T element, Exception cause)
            {
                _probe.FishForMessage(
                    m =>
                        m is OnNext<T> next && next.Element.Equals(element) ||
                        m is OnError error && error.Cause.Equals(cause),
                    hint: $"OnNext({element}) or {cause.GetType().Name}");
                return this;
            }

            /// <summary>
            /// Expect given next element or stream completion, returning whichever was signaled.
            /// </summary>
            public object ExpectNextOrComplete()
            {
                var message = _probe.FishForMessage(m => m is OnNext<T> || m is OnComplete, hint: "OnNext(_) or OnComplete");
                if (message is OnNext<T> next)
                    return next.Element;
                return message;
            }

            /// <summary>
            /// Fluent DSL. Expect given next element or stream completion.
            /// </summary>
            public ManualProbe<T> ExpectNextOrComplete(T element)
            {
                _probe.FishForMessage(
                    m =>
                        m is OnNext<T> next && next.Element.Equals(element) ||
                        m is OnComplete,
                    hint: $"OnNext({element}) or OnComplete");
                return this;
            }

            /// <summary>
            /// Fluent DSL. Same as <see cref="ExpectNoMsg(TimeSpan)"/>, but correctly treating the timeFactor.
            /// </summary>
            public ManualProbe<T> ExpectNoMsg()
            {
                _probe.ExpectNoMsg();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Assert that no message is received for the specified time.
            /// </summary>
            public ManualProbe<T> ExpectNoMsg(TimeSpan remaining)
            {
                _probe.ExpectNoMsg(remaining);
                return this;
            }

            /// <summary>
            /// Expect next element and test it with the <paramref name="predicate"/>
            /// </summary>
            /// <typeparam name="TOther">The <see cref="Type"/> of the expected message</typeparam>
            /// <param name="predicate">The <see cref="Predicate{T}"/> that is applied to the message</param>
            /// <returns>The next element</returns>
            public TOther ExpectNext<TOther>(Predicate<TOther> predicate) => _probe.ExpectMsg<OnNext<TOther>>(x => predicate(x.Element)).Element;
            
            /// <summary>
            /// Expect next element and test it with the <paramref name="predicate"/>
            /// </summary>
            /// <typeparam name="TOther">The <see cref="Type"/> of the expected message</typeparam>
            /// <param name="predicate">The <see cref="Predicate{T}"/> that is applied to the message</param>
            /// <returns>this</returns>
            public ManualProbe<T> MatchNext<TOther>(Predicate<TOther> predicate)
            {
                _probe.ExpectMsg<OnNext<TOther>>(x => predicate(x.Element));
                return this;
            }

            public TOther ExpectEvent<TOther>(Func<ISubscriberEvent, TOther> func) => func(_probe.ExpectMsg<ISubscriberEvent>(hint: "message matching function"));

            /// <summary>
            /// Receive messages for a given duration or until one does not match a given partial function.
            /// </summary>
            public IEnumerable<TOther> ReceiveWhile<TOther>(TimeSpan? max = null, TimeSpan? idle = null, Func<object, TOther> filter = null, int msgs = int.MaxValue) where TOther : class
            {
                return _probe.ReceiveWhile(max, idle, filter, msgs);
            }

            /// <summary>
            /// Drains a given number of messages
            /// </summary>
            public IEnumerable<TOther> ReceiveWithin<TOther>(TimeSpan max, int messages = int.MaxValue) where TOther : class
            {
                return _probe.ReceiveWhile(max, max, msg => (msg as OnNext)?.Element as TOther, messages);
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
            public TOther Within<TOther>(TimeSpan min, TimeSpan max, Func<TOther> execute) => _probe.Within(min, max, execute);

            /// <summary>
            /// Sane as calling Within(TimeSpan.Zero, max, function).
            /// </summary>
            public TOther Within<TOther>(TimeSpan max, Func<TOther> execute) => _probe.Within(max, execute);

            /// <summary>
            /// Attempt to drain the stream into a strict collection (by requesting <see cref="long.MaxValue"/> elements).
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

            private void Assert(bool predicate, string format, params object[] args)
            {
                if (!predicate) throw new Exception(string.Format(format, args));
            }

            private void Assert(Func<bool> predicate, string format, params object[] args)
            {
                if (!predicate()) throw new Exception(string.Format(format, args));
            }

            private void AssertEquals<T1, T2>(T1 x, T2 y, string format, params object[] args)
            {
                if (!Equals(x, y)) throw new Exception(string.Format(format, args));
            }
        }

        /// <summary>
        /// Single subscription tracking for <see cref="ManualProbe{T}"/>.
        /// </summary>
        public class Probe<T> : ManualProbe<T>
        {
            private readonly Lazy<ISubscription> _subscription;

            internal Probe(TestKitBase testKit) : base(testKit)
            {
                _subscription = new Lazy<ISubscription>(ExpectSubscription);
            }

            /// <summary>
            /// Asserts that a subscription has been received or will be received
            /// </summary>
            public Probe<T> EnsureSubscription()
            {
                var _ = _subscription.Value; // initializes lazy val
                return this;
            }

            public Probe<T> Request(long n)
            {
                _subscription.Value.Request(n);
                return this;
            }

            public Probe<T> RequestNext(T element)
            {
                _subscription.Value.Request(1);
                ExpectNext(element);
                return this;
            }

            public Probe<T> Cancel()
            {
                _subscription.Value.Cancel();
                return this;
            }

            /// <summary>
            /// Request and expect a stream element.
            /// </summary>
            public T RequestNext()
            {
                _subscription.Value.Request(1);
                return ExpectNext();
            }

            /// <summary>
            /// Request and expect a stream element during the specified time or timeout.
            /// </summary>
            public T RequestNext(TimeSpan timeout)
            {
                _subscription.Value.Request(1);
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
