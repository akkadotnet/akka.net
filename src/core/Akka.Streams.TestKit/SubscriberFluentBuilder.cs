// //-----------------------------------------------------------------------
// // <copyright file="SubscriberFluentBuilder.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.TestKit;
using static Akka.Streams.TestKit.TestSubscriber;

namespace Akka.Streams.TestKit
{
    public class SubscriberFluentBuilder<T>
    {
#region ManualProbe<T> wrapper

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectError(CancellationToken)"/>
        public Exception ExpectError(CancellationToken cancellationToken = default)
            => Probe.ExpectError(cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectErrorAsync(CancellationToken)"/>
        public Task<Exception> ExpectErrorAsync(CancellationToken cancellationToken = default)
            => Probe.ExpectErrorAsync(cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectSubscriptionAndError(CancellationToken)"/>
        public Exception ExpectSubscriptionAndError(CancellationToken cancellationToken = default) 
            => Probe.ExpectSubscriptionAndError(cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectSubscriptionAndErrorAsync(CancellationToken)"/>
        public Task<Exception> ExpectSubscriptionAndErrorAsync(CancellationToken cancellationToken = default)
            => Probe.ExpectSubscriptionAndErrorAsync(cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectSubscriptionAndError(bool,CancellationToken)"/>
        public Exception ExpectSubscriptionAndError(
            bool signalDemand,
            CancellationToken cancellationToken = default)
            => Probe.ExpectSubscriptionAndError(signalDemand, cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectSubscriptionAndErrorAsync(bool,CancellationToken)"/>
        public Task<Exception> ExpectSubscriptionAndErrorAsync(
            bool signalDemand, 
            CancellationToken cancellationToken = default)
            => Probe.ExpectSubscriptionAndErrorAsync(signalDemand, cancellationToken);
        
        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextOrError(CancellationToken)"/>
        public object ExpectNextOrError(CancellationToken cancellationToken = default)
            => Probe.ExpectNextOrError(cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextOrErrorAsync(CancellationToken)"/>
        public Task<object> ExpectNextOrErrorAsync(CancellationToken cancellationToken = default)
            => Probe.ExpectNextOrErrorAsync(cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextOrComplete(CancellationToken)"/>
        public object ExpectNextOrComplete(CancellationToken cancellationToken = default)
            => Probe.ExpectNextOrComplete(cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextOrCompleteAsync(CancellationToken)"/>
        public Task<object> ExpectNextOrCompleteAsync(CancellationToken cancellationToken = default)
            => Probe.ExpectNextOrCompleteAsync(cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNext{TOther}(Predicate{TOther},CancellationToken)"/>
        public TOther ExpectNext<TOther>(Predicate<TOther> predicate, CancellationToken cancellationToken = default)
            => Probe.ExpectNext(predicate, cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextAsync{TOther}(Predicate{TOther},CancellationToken)"/>
        public Task<TOther> ExpectNextAsync<TOther>(Predicate<TOther> predicate, CancellationToken cancellationToken = default)
            => Probe.ExpectNextAsync(predicate, cancellationToken);

        public TOther ExpectEvent<TOther>(Func<ISubscriberEvent, TOther> func, CancellationToken cancellationToken = default)
            => Probe.ExpectEvent(func, cancellationToken);

        public Task<TOther> ExpectEventAsync<TOther>(Func<ISubscriberEvent, TOther> func, CancellationToken cancellationToken = default)
            => Probe.ExpectEventAsync(func, cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ReceiveWhile{TOther}(Nullable{TimeSpan},Nullable{TimeSpan},Func{object, TOther},int,CancellationToken)"/>
        public IEnumerable<TOther> ReceiveWhile<TOther>(
            TimeSpan? max = null,
            TimeSpan? idle = null,
            Func<object, TOther> filter = null,
            int msgs = int.MaxValue,
            CancellationToken cancellationToken = default)
            => Probe.ReceiveWhile(max, idle, filter, msgs, cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ReceiveWhileAsync{TOther}(Nullable{TimeSpan},Nullable{TimeSpan},Func{object, TOther},int,CancellationToken)"/>
        public IAsyncEnumerable<TOther> ReceiveWhileAsync<TOther>(
            TimeSpan? max = null,
            TimeSpan? idle = null,
            Func<object, TOther> filter = null,
            int msgs = int.MaxValue,
            CancellationToken cancellationToken = default)
            => Probe.ReceiveWhileAsync(max, idle, filter, msgs, cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ReceiveWithin{TOther}(Nullable{TimeSpan},int,CancellationToken)"/>
        public IEnumerable<TOther> ReceiveWithin<TOther>(TimeSpan? max, int messages = int.MaxValue, CancellationToken cancellationToken = default)
            => Probe.ReceiveWithin<TOther>(max, messages, cancellationToken);

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ReceiveWithinAsync{TOther}(Nullable{TimeSpan},int,CancellationToken)"/>
        public IAsyncEnumerable<TOther> ReceiveWithinAsync<TOther>(TimeSpan? max, int messages = int.MaxValue, CancellationToken cancellationToken = default) 
            => Probe.ReceiveWithinAsync<TOther>(max, messages, cancellationToken);
        
#endregion
        
        internal SubscriberFluentBuilder(ManualProbe<T> probe)
        {
            Probe = probe;
        }
        
        public Task Task { get; private set; }
        public ManualProbe<T> Probe { get; }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectEvent(ISubscriberEvent,CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectEvent(
            ISubscriberEvent e,
            CancellationToken cancellationToken = default)
        {
            ExpectEventTask(Probe.TestProbe, e, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }
        
        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectEventAsync(ISubscriberEvent,CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectEventAsync(
            ISubscriberEvent e,
            CancellationToken cancellationToken = default)
        {
            Append(ExpectEventTask(Probe.TestProbe, e, cancellationToken));
            return this;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Task ExpectEventTask(TestProbe probe, ISubscriberEvent e, CancellationToken cancellationToken)
            => probe.ExpectMsgAsync(e, cancellationToken: cancellationToken).AsTask();
        

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNext(T,Nullable{TimeSpan},CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNext(
            T element,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            ExpectNextTask(Probe.TestProbe, element, timeout, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }
        
        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextAsync(T,Nullable{TimeSpan},CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNextAsync(
            T element,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Append(ExpectNextTask(Probe.TestProbe, element, timeout, cancellationToken));
            return this;
        }
        
        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNext(Nullable{TimeSpan},T,CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNext(
            TimeSpan? timeout,
            T element,
            CancellationToken cancellationToken = default)
        {
            ExpectNextTask(Probe.TestProbe, element, timeout, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }
        
        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextAsync(Nullable{TimeSpan},T,CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNextAsync(
            TimeSpan? timeout,
            T element,
            CancellationToken cancellationToken = default)
        {
            Append(ExpectNextTask(Probe.TestProbe, element, timeout, cancellationToken));
            return this;
        }
        
        /*
        /// <summary>
        /// Fluent DSL. Expect a stream element during specified timeout.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNext(T element, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            ExpectNextTask(_probe.Probe, element, timeout, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }
        
        /// <summary>
        /// Fluent DSL. Expect a stream element during specified timeout.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNextAsync(T element, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Append(ExpectNextTask(_probe.Probe, element, timeout, cancellationToken));
            return this;
        }
        */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Task ExpectNextTask(
            TestProbe probe,
            T element,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
            => probe.ExpectMsgAsync<OnNext<T>>(
                assert: x => AssertEquals(x.Element, element, "Expected '{0}', but got '{1}'", element, x.Element),
                timeout: timeout,
                cancellationToken: cancellationToken).AsTask();
        
        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNext(T[])"/>
        public SubscriberFluentBuilder<T> ExpectNext(params T[] elems)
        {
            ExpectNextTask(Probe, null, elems)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextAsync(T[])"/>
        public SubscriberFluentBuilder<T> ExpectNextAsync(params T[] elems)
        {
            Append(ExpectNextTask(Probe, null, elems));
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNext(Nullable{TimeSpan},T[])"/>
        public SubscriberFluentBuilder<T> ExpectNext(TimeSpan? timeout, params T[] elems)
        {
            ExpectNextTask(Probe, timeout, elems)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextAsync(Nullable{TimeSpan},T[])"/>
        public SubscriberFluentBuilder<T> ExpectNextAsync(TimeSpan? timeout, params T[] elems)
        {
            Append(ExpectNextTask(Probe, timeout, elems));
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static async Task ExpectNextTask(ManualProbe<T> probe, TimeSpan? timeout, params T[] elems)
        {
            var len = elems.Length;
            if (len < 2)
                throw new ArgumentException("elems need to have at least 2 elements", nameof(elems));
            
            var e = await probe.ExpectNextNAsync(len, timeout).ToListAsync()
                .ConfigureAwait(false);
            AssertEquals(e.Count, len, "expected to get {0} events, but got {1}", len, e.Count);
            for (var i = 0; i < elems.Length; i++)
            {
                AssertEquals(e[i], elems[i], "expected [{2}] element to be {0} but found {1}", elems[i], e[i], i);
            }
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextUnordered(T[])"/>
        public SubscriberFluentBuilder<T> ExpectNextUnordered(params T[] elems)
        {
            ExpectNextUnorderedTask(Probe, null, elems)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextUnorderedAsync(T[])"/>
        public SubscriberFluentBuilder<T> ExpectNextUnorderedAsync(params T[] elems)
        {
            Append(ExpectNextUnorderedTask(Probe, null, elems));
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextUnordered(Nullable{TimeSpan},T[])"/>
        public SubscriberFluentBuilder<T> ExpectNextUnordered(TimeSpan? timeout,params T[] elems)
        {
            ExpectNextUnorderedTask(Probe, timeout, elems)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }
        
        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextUnorderedAsync(Nullable{TimeSpan},T[])"/>
        public SubscriberFluentBuilder<T> ExpectNextUnorderedAsync(TimeSpan? timeout,params T[] elems)
        {
            Append(ExpectNextUnorderedTask(Probe, timeout, elems));
            return this;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static async Task ExpectNextUnorderedTask(ManualProbe<T> probe, TimeSpan? timeout, params T[] elems)
        {
            var len = elems.Length;
            var e = await probe.ExpectNextNAsync(len, timeout)
                .ToListAsync().ConfigureAwait(false);
            AssertEquals(e.Count, len, "expected to get {0} events, but got {1}", len, e.Count);

            var expectedSet = new HashSet<T>(elems);
            expectedSet.ExceptWith(e);

            Assert(expectedSet.Count == 0, "unexpected elements [{0}] found in the result", string.Join(", ", expectedSet));
        }
        
        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextWithinSet(ICollection{T},CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNextWithinSet(
            ICollection<T> elems,
            CancellationToken cancellationToken = default)
        {
            ExpectNextWithinSetTask(Probe.TestProbe, elems, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextWithinSetAsync(ICollection{T},CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNextWithinSetAsync(
            ICollection<T> elems,
            CancellationToken cancellationToken = default)
        {
            Append(ExpectNextWithinSetTask(Probe.TestProbe, elems, cancellationToken));
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static async Task ExpectNextWithinSetTask(
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
        
        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextN(IEnumerable{T},Nullable{TimeSpan},CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNextN(
            IEnumerable<T> all,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            ExpectNextNTask(Probe.TestProbe, all, timeout, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextNAsync(IEnumerable{T},Nullable{TimeSpan},CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNextNAsync(
            IEnumerable<T> all,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Append(ExpectNextNTask(Probe.TestProbe, all, timeout, cancellationToken));
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static async Task ExpectNextNTask(
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

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextUnorderedN(IEnumerable{T},Nullable{TimeSpan},CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNextUnorderedN(
            IEnumerable<T> all, 
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            ExpectNextUnorderedNTask(Probe, all, timeout, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextUnorderedNAsync(IEnumerable{T},Nullable{TimeSpan},CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNextUnorderedNAsync(
            IEnumerable<T> all,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Append(ExpectNextUnorderedNTask(Probe, all, timeout, cancellationToken));
            return this;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static async Task ExpectNextUnorderedNTask(
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
        
        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectComplete(CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectComplete(CancellationToken cancellationToken = default)
        {
            ExpectCompleteTask(Probe.TestProbe, null, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectCompleteAsync(CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectCompleteAsync(CancellationToken cancellationToken = default)
        {
            Append(ExpectCompleteTask(Probe.TestProbe, null, cancellationToken));
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectComplete(Nullable{TimeSpan},CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectComplete(
            TimeSpan? timeout,
            CancellationToken cancellationToken = default)
        {
            ExpectCompleteTask(Probe.TestProbe, timeout, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectCompleteAsync(Nullable{TimeSpan},CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectCompleteAsync(
            TimeSpan? timeout,
            CancellationToken cancellationToken = default)
        {
            Append(ExpectCompleteTask(Probe.TestProbe, timeout, cancellationToken));
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Task ExpectCompleteTask(TestProbe probe, TimeSpan? timeout, CancellationToken cancellationToken)
            => probe.ExpectMsgAsync<OnComplete>(timeout, cancellationToken: cancellationToken).AsTask();

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectSubscriptionAndComplete(CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectSubscriptionAndComplete(CancellationToken cancellationToken = default)
        {
            ExpectSubscriptionAndCompleteTask(Probe, true, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectSubscriptionAndCompleteAsync(CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectSubscriptionAndCompleteAsync(CancellationToken cancellationToken = default)
        {
            Append(ExpectSubscriptionAndCompleteTask(Probe, true, cancellationToken));
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectSubscriptionAndComplete(bool,CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectSubscriptionAndComplete(
            bool signalDemand,
            CancellationToken cancellationToken = default)
        {
            ExpectSubscriptionAndCompleteTask(Probe, signalDemand, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectSubscriptionAndCompleteAsync(bool,CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectSubscriptionAndCompleteAsync(
            bool signalDemand,
            CancellationToken cancellationToken = default)
        {
            Append(ExpectSubscriptionAndCompleteTask(Probe, signalDemand, cancellationToken));
            return this;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static async Task ExpectSubscriptionAndCompleteTask(
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

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextOrError(T,Exception,CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNextOrError(
            T element,
            Exception cause,
            CancellationToken cancellationToken = default)
        {
            ExpectNextOrErrorTask(Probe.TestProbe, element, cause, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }   
        
        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextOrErrorAsync(T,Exception,CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNextOrErrorAsync(
            T element,
            Exception cause,
            CancellationToken cancellationToken = default)
        {
            Append(ExpectNextOrErrorTask(Probe.TestProbe, element, cause, cancellationToken));
            return this;
        }   
        
        private static async Task ExpectNextOrErrorTask(
            TestProbe probe,
            T element,
            Exception cause,
            CancellationToken cancellationToken = default)
            => await probe.FishForMessageAsync(
                isMessage: m =>
                    m is OnNext<T> next && next.Element.Equals(element) ||
                    m is OnError error && error.Cause.Equals(cause),
                hint: $"OnNext({element}) or {cause.GetType().Name}", 
                cancellationToken: cancellationToken);
        
        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextOrComplete(T,CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNextOrComplete(
            T element,
            CancellationToken cancellationToken = default)
        {
            ExpectNextOrCompleteTask(Probe.TestProbe, element, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNextOrCompleteAsync(T,CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNextOrCompleteAsync(
            T element,
            CancellationToken cancellationToken = default)
        {
            Append(ExpectNextOrCompleteTask(Probe.TestProbe, element, cancellationToken));
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Task ExpectNextOrCompleteTask(TestProbe probe, T element, CancellationToken cancellationToken)
            => probe.FishForMessageAsync(
                isMessage: m =>
                    m is OnNext<T> next && next.Element.Equals(element) ||
                    m is OnComplete,
                hint: $"OnNext({element}) or OnComplete", 
                cancellationToken: cancellationToken).AsTask();
        
        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNoMsg(CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNoMsg(CancellationToken cancellationToken = default)
        {
            Probe.TestProbe.ExpectNoMsgAsync(cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNoMsgAsync(CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNoMsgAsync(CancellationToken cancellationToken = default)
        {
            Append(Probe.TestProbe.ExpectNoMsgAsync(cancellationToken).AsTask());
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNoMsg(TimeSpan,CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNoMsg(TimeSpan remaining, CancellationToken cancellationToken = default)
        {
            Probe.TestProbe.ExpectNoMsgAsync(remaining, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.ExpectNoMsgAsync(TimeSpan,CancellationToken)"/>
        public SubscriberFluentBuilder<T> ExpectNoMsgAsync(TimeSpan remaining, CancellationToken cancellationToken = default)
        {
            Append(Probe.TestProbe.ExpectNoMsgAsync(remaining, cancellationToken).AsTask());
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.MatchNext{TOther}(Predicate{TOther},CancellationToken)"/>
        public SubscriberFluentBuilder<T> MatchNext<TOther>(Predicate<TOther> predicate, CancellationToken cancellationToken = default)
        {
            MatchNextTask(Probe.TestProbe, predicate, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            return this;
        }

        /// <inheritdoc cref="TestSubscriber.ManualProbe{T}.MatchNextAsync{TOther}(Predicate{TOther},CancellationToken)"/>
        public SubscriberFluentBuilder<T> MatchNextAsync<TOther>(
            Predicate<TOther> predicate,
            CancellationToken cancellationToken = default)
        {
            Append(MatchNextTask(Probe.TestProbe, predicate, cancellationToken));
            return this;
        }
        
        private static async Task MatchNextTask<TOther>(
            TestProbe probe,
            Predicate<TOther> predicate,
            CancellationToken cancellationToken)
            => await probe.ExpectMsgAsync<OnNext<TOther>>(
                isMessage: x => predicate(x.Element),
                cancellationToken: cancellationToken);
        
        private void Append(Task task)
        {
            if(Task == null)
            {
                Task = task;
            }
            else
            {
                Task = Task.ContinueWith(async t =>
                {
                    if (t.Exception != null)
                    {
                        var flattened = t.Exception.Flatten();
                        ExceptionDispatchInfo.Capture(flattened).Throw();
                        return;
                    }
                    
                    await task;
                });
            }
        }
        
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
        
    }
}