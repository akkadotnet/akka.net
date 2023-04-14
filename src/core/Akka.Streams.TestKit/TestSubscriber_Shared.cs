//-----------------------------------------------------------------------
// <copyright file="TestSubscriber_Shared.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using Akka.TestKit;
using Reactive.Streams;

namespace Akka.Streams.TestKit
{
    public static partial class TestSubscriber
    {
        public partial class ManualProbe<T>
        {
            #region void methods
            
            internal static async Task ExpectEventTask(TestProbe probe, ISubscriberEvent e, CancellationToken cancellationToken)
                => await probe.ExpectMsgAsync(e, cancellationToken: cancellationToken);

            internal static async Task ExpectNextTask(
                TestProbe probe,
                T element,
                TimeSpan? timeout,
                CancellationToken cancellationToken)
                => await probe.ExpectMsgAsync<OnNext<T>>(
                    assert: x => AssertEquals(x.Element, element, "Expected '{0}', but got '{1}'", element, x.Element),
                    timeout: timeout,
                    cancellationToken: cancellationToken);

            internal static async Task ExpectNextTask(
                ManualProbe<T> probe,
                TimeSpan? timeout,
                CancellationToken cancellationToken,
                params T[] elems)
            {
                var len = elems.Length;
                if (len < 2)
                    throw new ArgumentException("elems need to have at least 2 elements", nameof(elems));
                
                var e = await probe.ExpectNextNAsync(len, timeout, cancellationToken)
                    .ToListAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                AssertEquals(e.Count, len, "expected to get {0} events, but got {1}", len, e.Count);
                for (var i = 0; i < elems.Length; i++)
                {
                    AssertEquals(e[i], elems[i], "expected [{2}] element to be {0} but found {1}", elems[i], e[i], i);
                }
            }

            internal static async Task ExpectNextUnorderedTask(ManualProbe<T> probe, TimeSpan? timeout, CancellationToken cancellationToken, params T[] elems)
            {
                var len = elems.Length;
                var e = await probe.ExpectNextNAsync(len, timeout, cancellationToken)
                    .ToListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                AssertEquals(e.Count, len, "expected to get {0} events, but got {1}", len, e.Count);

                var expectedSet = new HashSet<T>(elems);
                expectedSet.ExceptWith(e);

                Assert(expectedSet.Count == 0, "unexpected elements [{0}] found in the result", string.Join(", ", expectedSet));
            }

            internal static async Task ExpectNextWithinSetTask(
                TestProbe probe, 
                ICollection<T> elems,
                CancellationToken cancellationToken)
            {
                var next = await probe.ExpectMsgAsync<OnNext<T>>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if(!elems.Contains(next.Element))
                    Assert(false, "unexpected elements [{0}] found in the result", next.Element);
                elems.Remove(next.Element);
                probe.Log.Info($"Received '{next.Element}' within OnNext().");
            }

            internal static async Task ExpectNextNTask(
                TestProbe probe,
                IEnumerable<T> all, 
                TimeSpan? timeout,
                CancellationToken cancellationToken)
            {
                var list = all.ToList();
                foreach (var x in list)
                    await probe.ExpectMsgAsync<OnNext<T>>(
                        assert: y => AssertEquals(y.Element, x, "Expected one of ({0}), but got '{1}'", string.Join(", ", list), y.Element), 
                        timeout: timeout, 
                        cancellationToken: cancellationToken);
            }

            internal static async Task ExpectNextUnorderedNTask(
                ManualProbe<T> probe,
                IEnumerable<T> all,
                TimeSpan? timeout,
                CancellationToken cancellationToken)
            {
                var collection = new HashSet<T>(all);
                while (collection.Count > 0)
                {
                    var next = await probe.ExpectNextAsync(timeout, cancellationToken);
                    Assert(collection.Contains(next), $"expected one of (${string.Join(", ", collection)}), but received {next}");
                    collection.Remove(next);
                }
            }

            internal static async Task ExpectCompleteTask(TestProbe probe, TimeSpan? timeout, CancellationToken cancellationToken)
                => await probe.ExpectMsgAsync<OnComplete>(timeout, cancellationToken: cancellationToken);

            internal static async Task ExpectSubscriptionAndCompleteTask(
                ManualProbe<T> probe,
                bool signalDemand,
                CancellationToken cancellationToken)
            {
                var sub = await probe.ExpectSubscriptionAsync(cancellationToken)
                    .ConfigureAwait(false);
                
                if (signalDemand)
                    sub.Request(1);

                await ExpectCompleteTask(probe.TestProbe, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            internal static async Task ExpectNextOrErrorTask(
                TestProbe probe,
                T element,
                Exception cause,
                CancellationToken cancellationToken = default)
                => await probe.ExpectMsgAsync<ISubscriberEvent>(
                    isMessage: m =>
                        m is OnNext<T> next && next.Element.Equals(element) ||
                        m is OnError error && error.Cause.Equals(cause),
                    hint: $"OnNext({element}) or {cause.GetType().Name}",
                    cancellationToken: cancellationToken
                );

            internal static async Task ExpectNextOrCompleteTask(TestProbe probe, T element, CancellationToken cancellationToken)
                => await probe.FishForMessageAsync(
                    isMessage: m =>
                        m is OnNext<T> next && next.Element.Equals(element) ||
                        m is OnComplete,
                    hint: $"OnNext({element}) or OnComplete", 
                    cancellationToken: cancellationToken);

            internal static async Task MatchNextTask<TOther>(
                TestProbe probe,
                Predicate<TOther> predicate,
                CancellationToken cancellationToken)
                => await probe.ExpectMsgAsync<OnNext<TOther>>(
                    isMessage: x => predicate(x.Element),
                    cancellationToken: cancellationToken);            

            #endregion

            #region return type methods

            internal static async Task<ISubscription> ExpectSubscriptionTask(
                ManualProbe<T> probe,
                CancellationToken cancellationToken = default)
            {
                var msg = await probe.TestProbe.ExpectMsgAsync<OnSubscribe>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                probe.Subscription = msg.Subscription;
                return msg.Subscription;
            }
            
            internal static async Task<ISubscriberEvent> ExpectEventTask(
                TestProbe probe,
                TimeSpan? max,
                CancellationToken cancellationToken = default) 
                => await probe.ExpectMsgAsync<ISubscriberEvent>(max, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            internal static async Task<T> ExpectNextTask(
                TestProbe probe,
                TimeSpan? timeout,
                CancellationToken cancellationToken = default)
            {
                return await probe.ReceiveOneAsync(timeout, cancellationToken) switch
                {
                    null => throw new Exception($"Expected OnNext(_), yet no element signaled during {timeout}"),
                    OnNext<T> message => message.Element,
                    var other => throw new Exception($"expected OnNext, found {other}")
                };
            }
            
            internal static async IAsyncEnumerable<T> ExpectNextNTask(
                TestProbe probe,
                long n, 
                TimeSpan? timeout = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                var collected = new List<T>();
                for (var i = 0; i < n; i++)
                {
                    OnNext<T> next;
                    try
                    {
                        next = await probe.ExpectMsgAsync<OnNext<T>>(timeout, cancellationToken: cancellationToken);
                        collected.Add(next.Element);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(
                            $"[ExpectNextN] expected {n} next elements but received {collected.Count} elements " +
                            $"before an exception occured. Received: {Stringify(collected)}", 
                            ex);
                    }
                    yield return next.Element;
                }
            }

            private static string Stringify(object obj)
            {
                switch (obj)
                {
                    case null:
                        return "null";
                    case string str:
                        return str;
                    case IEnumerable enumerable:
                        var list = (from object o in enumerable select Stringify(o)).ToList();
                        return $"[{string.Join(", ", list)}]";
                    default:
                        return obj.ToString();
                }
            }
            
            internal static async Task<Exception> ExpectErrorTask(
                TestProbe probe,
                CancellationToken cancellationToken = default)
            {
                var msg = await probe.ExpectMsgAsync<OnError>(cancellationToken: cancellationToken);
                return msg.Cause;
            }
            
            internal static async Task<Exception> ExpectSubscriptionAndErrorTask(
                ManualProbe<T> probe,
                bool signalDemand, 
                CancellationToken cancellationToken = default)
            {
                var sub = await probe.ExpectSubscriptionAsync(cancellationToken);
                if(signalDemand)
                    sub.Request(1);

                return await probe.ExpectErrorAsync(cancellationToken);
            }
            
            internal static async Task<object> ExpectNextOrErrorTask(
                TestProbe probe,
                CancellationToken cancellationToken = default)
            {
                var message = await probe.FishForMessageAsync(
                    isMessage: m => m is OnNext<T> || m is OnError, 
                    hint: "OnNext(_) or error", 
                    cancellationToken: cancellationToken);

                return message switch
                {
                    OnNext<T> next => next.Element,
                    _ => ((OnError) message).Cause
                };
            }
            
            internal static async Task<object> ExpectNextOrCompleteTask(
                TestProbe probe,
                CancellationToken cancellationToken = default)
            {
                var message = await probe.FishForMessageAsync(
                    isMessage: m => m is OnNext<T> || m is OnComplete, 
                    hint: "OnNext(_) or OnComplete", 
                    cancellationToken: cancellationToken);

                return message switch
                {
                    OnNext<T> next => next.Element,
                    _ => message
                };
            }

            internal static async Task<TOther> ExpectNextTask<TOther>(
                TestProbe probe,
                Predicate<TOther> predicate,
                CancellationToken cancellationToken = default)
            {
                var msg = await probe.ExpectMsgAsync<OnNext<TOther>>(
                    isMessage: x => predicate(x.Element),
                    cancellationToken: cancellationToken);
                return msg.Element;
            }
            
            internal static async Task<TOther> ExpectEventTask<TOther>(
                TestProbe probe,
                Func<ISubscriberEvent, TOther> func, 
                CancellationToken cancellationToken = default)
            {
                var msg = await probe.ExpectMsgAsync<ISubscriberEvent>(
                    hint: "message matching function",
                    cancellationToken: cancellationToken);
                return func(msg);
            }
            
            internal static IAsyncEnumerable<TOther> ReceiveWithinTask<TOther>(
                TestProbe probe,
                TimeSpan? max,
                int messages = int.MaxValue,
                CancellationToken cancellationToken = default) 
            {
                return probe.ReceiveWhileAsync(max, max, msg =>
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
            
            internal static async IAsyncEnumerable<T> ToStrictTask(
                ManualProbe<T> probe,
                TimeSpan atMost,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                // if no subscription was obtained yet, we expect it
                await EnsureSubscriptionTask(probe, cancellationToken);
                probe.Subscription.Request(long.MaxValue);

                var deadline = DateTime.UtcNow + atMost;
                var result = new List<T>();
                while (true)
                {
                    var e = await ExpectEventTask(
                        probe.TestProbe, 
                        TimeSpan.FromTicks(Math.Max(deadline.Ticks - DateTime.UtcNow.Ticks, 0)),
                        cancellationToken);
                    
                    switch (e)
                    {
                        case OnError error:
                            throw new ArgumentException(
                                $"ToStrict received OnError while draining stream! Accumulated elements: [{string.Join(", ", result)}]",
                                error.Cause);
                        case OnComplete _:
                            yield break;
                        case OnNext<T> next:
                            result.Add(next.Element);
                            yield return next.Element;
                            break;
                        default:
                            throw new InvalidOperationException($"Invalid response, expected {nameof(OnError)}, {nameof(OnComplete)}, or {nameof(OnNext<T>)}, received [{e.GetType()}]");
                    }
                }
            }

            internal static async Task EnsureSubscriptionTask(
                ManualProbe<T> probe,
                CancellationToken cancellationToken = default)
            {
                probe.Subscription ??= await ExpectSubscriptionTask(probe, cancellationToken)
                    .ConfigureAwait(false);
            }
            
            #endregion
            
            #region Utility methods

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void Assert(bool predicate, string format, params object[] args)
            {
                if (!predicate) throw new Exception(string.Format(format, args));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void AssertEquals<T1, T2>(T1 x, T2 y, string format, params object[] args)
            {
                if (!Equals(x, y)) throw new Exception(string.Format(format, args));
            }

            #endregion
        }
        
        public partial class Probe<T>
        {
            internal static async Task EnsureSubscriptionTask(
                Probe<T> probe,
                CancellationToken cancellationToken = default)
            {
                probe.Subscription ??= await ExpectSubscriptionTask(probe, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
