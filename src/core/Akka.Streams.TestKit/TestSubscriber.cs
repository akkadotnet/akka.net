//-----------------------------------------------------------------------
// <copyright file="TestSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
            private readonly TestProbe _probe;

            internal ManualProbe(TestKitBase testKit)
            {
                _probe = testKit.CreateTestProbe();
            }

            private volatile ISubscription _subscription;

            public void OnSubscribe(ISubscription subscription)
            {
                _probe.Ref.Tell(new OnSubscribe(subscription));
            }

            public void OnError(Exception cause)
            {
                _probe.Ref.Tell(new OnError(cause));
            }

            public void OnComplete()
            {
                _probe.Ref.Tell(TestSubscriber.OnComplete.Instance);
            }

            public void OnNext(T element)
            {
                _probe.Ref.Tell(new OnNext<T>(element));
            }

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
            public ISubscriberEvent ExpectEvent()
            {
                return _probe.ExpectMsg<ISubscriberEvent>();
            }

            /// <summary>
            /// Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public ISubscriberEvent ExpectEvent(TimeSpan max)
            {
                return _probe.ExpectMsg<ISubscriberEvent>(max);
            }

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
                var t = _probe.RemainingOrDilated(null);
                var message = _probe.ReceiveOne(t);
                if (message is OnNext<T>) return ((OnNext<T>) message).Element;
                else throw new Exception("expected OnNext, found " + message);
            }

            /// <summary>
            /// Fluent DSL. Expect a stream element.
            /// </summary>
            public ManualProbe<T> ExpectNext(T element)
            {
                _probe.ExpectMsg<OnNext<T>>(x => Equals(x.Element, element));
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect a stream element during specified timeout.
            /// </summary>
            public ManualProbe<T> ExpectNext(T element, TimeSpan timeout)
            {
                _probe.ExpectMsg<OnNext<T>>(x => Equals(x.Element, element), timeout);
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect multiple stream elements.
            /// </summary>
            public ManualProbe<T> ExpectNext(T e1, T e2, params T[] elems)
            {
                var len = elems.Length + 2;
                var e = ExpectNextN(len).ToArray();
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
                var len = elems.Length + 2;
                var e = ExpectNextN(len).ToArray();
                AssertEquals(e.Length, len, "expected to get {0} events, but got {1}", len, e.Length);

                var expectedSet = new HashSet<T>(elems) {e1, e2};
                expectedSet.ExceptWith(e);

                Assert(expectedSet.Count == 0, "unexpected elemenents [{0}] found in the result", string.Join(", ", expectedSet));
                return this;
            }

            /// <summary>
            /// Expect and return the next <paramref name="n"/> stream elements.
            /// </summary>
            public IEnumerable<T> ExpectNextN(long n)
            {
                var res = new List<T>((int)n);
                for (int i = 0; i < n; i++)
                {
                    var next = _probe.ExpectMsg<OnNext<T>>();
                    res.Add(next.Element);
                }
                return res;
            }

            /// <summary>
            /// Fluent DSL. Expect the given elements to be signalled in order.
            /// </summary>
            public ManualProbe<T> ExpectNextN(IEnumerable<T> all)
            {
                foreach (var x in all)
                    _probe.ExpectMsg<OnNext<T>>(y => Equals(y.Element, x));

                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect the given elements to be signalled in any order.
            /// </summary>
            public ManualProbe<T> ExpectNextUnorderedN(IEnumerable<T> all)
            {
                var collection = new HashSet<T>(all);
                while (collection.Count > 0)
                {
                    var next = ExpectNext();
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
            /// Expect and return the signalled <see cref="Exception"/>.
            /// </summary>
            public Exception ExpectError()
            {
                return _probe.ExpectMsg<OnError>().Cause;
            }

            /// <summary>
            /// Expect subscription to be followed immediatly by an error signal. By default single demand will be signalled in order to wake up a possibly lazy upstream. 
            /// <seealso cref="ExpectSubscriptionAndError(bool)"/>
            /// </summary>
            public Exception ExpectSubscriptionAndError()
            {
                return ExpectSubscriptionAndError(true);
            }

            /// <summary>
            /// Expect subscription to be followed immediatly by an error signal. Depending on the `signalDemand` parameter demand may be signalled 
            /// immediatly after obtaining the subscription in order to wake up a possibly lazy upstream.You can disable this by setting the `signalDemand` parameter to `false`.
            /// <seealso cref="ExpectSubscriptionAndError()"/>
            /// </summary>
            public Exception ExpectSubscriptionAndError(bool signalDemand)
            {
                var sub = ExpectSubscription();
                if(signalDemand) sub.Request(1);
                return ExpectError();
            }

            /// <summary>
            /// Fluent DSL. Expect subscription followed by immediate stream completion. By default single demand will be signalled in order to wake up a possibly lazy upstream
            /// </summary>
            /// <seealso cref="ExpectSubscriptionAndComplete(bool)"/>
            public ManualProbe<T> ExpectSubscriptionAndComplete()
            {
                return ExpectSubscriptionAndComplete(true);
            }

            /// <summary>
            /// Fluent DSL. Expect subscription followed by immediate stream completion. Depending on the `signalDemand` parameter 
            /// demand may be signalled immediatly after obtaining the subscription in order to wake up a possibly lazy upstream.
            /// You can disable this by setting the `signalDemand` parameter to `false`.
            /// </summary>
            /// <seealso cref="ExpectSubscriptionAndComplete()"/>
            public ManualProbe<T> ExpectSubscriptionAndComplete(bool signalDemand)
            {
                var sub = ExpectSubscription();
                if (signalDemand) sub.Request(1);
                ExpectComplete();
                return this;
            }

            /// <summary>
            /// Expect given next element or error signal, returning whichever was signalled.
            /// </summary>
            public object ExpectNextOrError()
            {
                var message = _probe.FishForMessage(m => m is OnNext<T> || m is OnError, hint: "OnNext(_) or error");
                if (message is OnNext<T>)
                    return ((OnNext<T>) message).Element;
                return ((OnError) message).Cause;
            }

            /// <summary>
            /// Fluent DSL. Expect given next element or error signal.
            /// </summary>
            public ManualProbe<T> ExpectNextOrError(T element, Exception cause)
            {
                _probe.FishForMessage(
                    m =>
                        (m is OnNext<T> && ((OnNext<T>) m).Element.Equals(element)) ||
                        (m is OnError && ((OnError) m).Cause.Equals(cause)),
                    hint: $"OnNext({element}) or {cause.GetType().Name}");
                return this;
            }

            /// <summary>
            /// Expect given next element or stream completion, returning whichever was signalled.
            /// </summary>
            public object ExpectNextOrComplete()
            {
                var message = _probe.FishForMessage(m => m is OnNext<T> || m is OnComplete, hint: "OnNext(_) or OnComplete");
                if (message is OnNext<T>)
                    return ((OnNext<T>) message).Element;
                return message;
            }

            /// <summary>
            /// Fluent DSL. Expect given next element or stream completion.
            /// </summary>
            public ManualProbe<T> ExpectNextOrComplete(T element)
            {
                _probe.FishForMessage(
                    m =>
                        (m is OnNext<T> && ((OnNext<T>) m).Element.Equals(element)) ||
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

            public TOther ExpectNext<TOther>(Predicate<TOther> predicate)
            {
                return _probe.ExpectMsg<OnNext<TOther>>(x => predicate(x.Element)).Element;
            }

            public TOther ExpectEvent<TOther>(Func<ISubscriberEvent, TOther> func)
            {
                return func(_probe.ExpectMsg<ISubscriberEvent>());
            }

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

            public TOther Within<TOther>(TimeSpan max, Func<TOther> func)
            {
                return _probe.Within(TimeSpan.Zero, max, func);
            }

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
                    if (e is OnError)
                        throw new ArgumentException(
                            $"ToStrict received OnError while draining stream! Accumulated elements: ${string.Join(", ", result)}",
                            ((OnError) e).Cause);
                    if (e is OnComplete)
                        break;
                    if (e is OnNext<T>)
                        result.Add(((OnNext<T>) e).Element);
                }
                return result;
            }

            private void Assert(bool predicate, string format, params object[] args)
            {
                if (!predicate) throw new Exception(string.Format(format, args));
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

            public T RequestNext()
            {
                _subscription.Value.Request(1);
                return ExpectNext();
            }
        }

        public static ManualProbe<T> CreateManualProbe<T>(this TestKitBase testKit)
        {
            return new ManualProbe<T>(testKit);
        }

        public static Probe<T> CreateProbe<T>(this TestKitBase testKit)
        {
            return new Probe<T>(testKit);
        }
    }
}