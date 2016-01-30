using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using Akka.Actor;
using Akka.TestKit;

namespace Akka.Streams.TestKit
{
    public static class TestSubscriber
    {
        #region messages

        public interface ISubscriberEvent : INoSerializationVerificationNeeded { }

        public struct OnSubscribe : ISubscriberEvent
        {
            public readonly ISubscription Subscription;

            public OnSubscribe(ISubscription subscription)
            {
                Subscription = subscription;
            }
        }

        public struct OnNext<T> : ISubscriberEvent
        {
            public readonly T Element;

            public OnNext(T element)
            {
                Element = element;
            }
        }

        public sealed class OnComplete: ISubscriberEvent
        {
            public static readonly OnComplete Instance = new OnComplete();
            private OnComplete() { }
        }

        public struct OnError : ISubscriberEvent
        {
            public readonly Exception Cause;

            public OnError(Exception cause)
            {
                Cause = cause;
            }
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

            public ISubscription Subscription { get; protected set; }

            public void OnSubscribe(ISubscription subscription)
            {
                _probe.Ref.Tell(new TestSubscriber.OnSubscribe(subscription));
            }

            public void OnError(Exception cause)
            {
                _probe.Ref.Tell(new TestSubscriber.OnError(cause));
            }

            public void OnComplete()
            {
                _probe.Ref.Tell(TestSubscriber.OnComplete.Instance);
            }

            void ISubscriber.OnNext(object element)
            {
                OnNext((T)element);
            }

            public void OnNext(T element)
            {
                _probe.Ref.Tell(new TestSubscriber.OnNext<T>(element));
            }

            /// <summary>
            /// Expects and returns <see cref="ISubscription"/>.
            /// </summary>
            public ISubscription ExpectSubscription()
            {
                return Subscription = _probe.ExpectMsg<OnSubscribe>().Subscription;
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
                return _probe.ExpectMsg<ISubscriberEvent>();
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
                var message = _probe.ReceiveOne(_probe.Remaining);
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
                AssertEquals(e[1], e2, "expected [1] element to be {0} but found {1}", e1, e[0]);
                for (int i = 0; i < elems.Length; i++)
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
                
                var expectedSet = new HashSet<T>(elems);
                expectedSet.Add(e1);
                expectedSet.Add(e2);
                expectedSet.ExceptWith(e);

                Assert(expectedSet.Count == 0, "unexpected elemenents [{0}] found in the result", string.Join(", ", expectedSet));
                return this;
            }

            /// <summary>
            /// Expect and return the next <paramref name="n"/> stream elements.
            /// </summary>
            public IEnumerable<T> ExpectNextN(long n)
            {
                var i = 0;
                while (i < n)
                {
                    var next = _probe.ExpectMsg<OnNext<T>>();
                    yield return next.Element;
                    i++;
                }
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
                foreach (var x in collection)
                    _probe.ExpectMsg<OnNext<T>>(y => collection.Contains(y.Element));

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
            public IEnumerable<T> ToStrict(TimeSpan atMost)
            {
                var deadline = DateTime.UtcNow + atMost;
                // if no subscription was obtained yet, we expect it
                if (Subscription == null) ExpectSubscription();
                Subscription.Request(long.MaxValue);

                while (true)
                {
                    var e = ExpectEvent(DateTime.UtcNow - deadline);
                    if (e is OnError) throw new Exception(string.Format("ToStrict received OnError({0}) while draining stream!", ((OnError) e).Cause.Message));
                    else if (e is OnComplete) yield break;
                    else if (e is OnNext<T>) yield return ((OnNext<T>) e).Element;
                }
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
            internal Probe(TestKitBase testKit) : base(testKit)
            {
            }

            /// <summary>
            /// Asserts that a subscription has been received or will be received
            /// </summary>
            public void EnsureSubscription()
            {
                ExpectSubscription();
            }

            public Probe<T> Request(long n)
            {
                Subscription.Request(n);
                return this;
            }

            public Probe<T> RequestNext(T element)
            {
                Subscription.Request(1);
                ExpectNext(element);
                return this;
            }

            public Probe<T> Cancel()
            {
                Subscription.Cancel();
                return this;
            }

            public T RequestNext()
            {
                Subscription.Request(1);
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