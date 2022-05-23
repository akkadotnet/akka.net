// //-----------------------------------------------------------------------
// // <copyright file="SubscriberFluentBuilder.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.TestKit;
using Reactive.Streams;
using static Akka.Streams.TestKit.TestSubscriber;

namespace Akka.Streams.TestKit
{
    public class SubscriberFluentBuilder<T>
    {
        #region ManualProbe<T> wrapper

        /// <summary>
        /// Execute the async chain and then expect and returns a Reactive.Streams.ISubscription/>.
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<ISubscription> ExpectSubscriptionAsync(CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await ManualProbe<T>.ExpectSubscriptionTask(Probe, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Execute the async chain and then expect and return a <see cref="ISubscriberEvent"/>
        /// (any of: <see cref="OnSubscribe"/>, <see cref="OnNext{T}"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<ISubscriberEvent> ExpectEventAsync(CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await ManualProbe<T>.ExpectEventTask(Probe.TestProbe, (TimeSpan?)null, cancellationToken)
                .ConfigureAwait(false);
        }
        
        /// <summary>
        /// Execute the async chain and then expect and return a <see cref="ISubscriberEvent"/>
        /// (any of: <see cref="OnSubscribe"/>, <see cref="OnNext{T}"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<ISubscriberEvent> ExpectEventAsync(
            TimeSpan? max,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await ManualProbe<T>.ExpectEventTask(Probe.TestProbe, max, cancellationToken)
                .ConfigureAwait(false);
        }
        
        /// <summary>
        /// Execute the async chain and then expect and return a stream element.
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<T> ExpectNextAsync(CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await ManualProbe<T>.ExpectNextTask(Probe.TestProbe, null, cancellationToken)
                .ConfigureAwait(false);
        }
        
        /// <summary>
        /// Execute the async chain and then expect and return a stream element during specified time or timeout.
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<T> ExpectNextAsync(TimeSpan? timeout, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await ManualProbe<T>.ExpectNextTask(Probe.TestProbe, timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        
        /// <summary>
        /// Execute the async chain and then expect and return the next <paramref name="n"/> stream elements.
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async IAsyncEnumerable<T> ExpectNextNAsync(
            long n,
            TimeSpan? timeout = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await foreach (var item in ManualProbe<T>.ExpectNextNTask(Probe.TestProbe, n, timeout, cancellationToken))
            {
                yield return item;
            }
        }
        
        /// <summary>
        /// Execute the async chain and then expect and return the signalled System.Exception/>.
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<Exception> ExpectErrorAsync(CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await ManualProbe<T>.ExpectErrorTask(Probe.TestProbe, cancellationToken)
                .ConfigureAwait(false);
        }
        
        /// <summary>
        /// Execute the async chain and then expect subscription to be followed immediately by an error signal.
        /// By default single demand will be signaled in order to wake up a possibly lazy upstream. 
        /// NOTE: This method will execute the async chain
        /// <seealso cref="ExpectSubscriptionAndErrorAsync(bool,CancellationToken)"/>
        /// </summary>
        public async Task<Exception> ExpectSubscriptionAndErrorAsync(CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await ManualProbe<T>.ExpectSubscriptionAndErrorTask(Probe, true, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Execute the async chain and then expect subscription to be followed immediately by an error signal.
        /// Depending on the `signalDemand` parameter demand may be signaled immediately after obtaining
        /// the subscription in order to wake up a possibly lazy upstream.
        /// You can disable this by setting the `signalDemand` parameter to `false`.
        /// NOTE: This method will execute the async chain
        /// <seealso cref="ExpectSubscriptionAndErrorAsync(CancellationToken)"/>
        /// </summary>
        public async Task<Exception> ExpectSubscriptionAndErrorAsync(
            bool signalDemand, 
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await ManualProbe<T>.ExpectSubscriptionAndErrorTask(Probe, signalDemand, cancellationToken)
                .ConfigureAwait(false);
        }
        
        /// <summary>
        /// Execute the async chain and then expect given next element or error signal, returning whichever was signaled.
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<object> ExpectNextOrErrorAsync(CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await ManualProbe<T>.ExpectNextOrErrorTask(Probe.TestProbe, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Execute the async chain and then expect given next element or stream completion, returning whichever was signaled.
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<object> ExpectNextOrCompleteAsync(CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await ManualProbe<T>.ExpectNextOrCompleteTask(Probe.TestProbe, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Execute the async chain and then expect next element and test it with the <paramref name="predicate"/>
        /// NOTE: This method will execute the async chain
        /// </summary>
        /// <typeparam name="TOther">The System.Type of the expected message</typeparam>
        /// <param name="predicate">The System.Predicate{T} that is applied to the message</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The next element</returns>
        public async Task<TOther> ExpectNextAsync<TOther>(
            Predicate<TOther> predicate,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await ManualProbe<T>.ExpectNextTask(Probe.TestProbe, predicate, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<TOther> ExpectEventAsync<TOther>(
            Func<ISubscriberEvent, TOther> func,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await ManualProbe<T>.ExpectEventTask(Probe.TestProbe, func, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Execute the async chain and then receive messages for a given duration or until one does not match a given filter function.
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async IAsyncEnumerable<TOther> ReceiveWhileAsync<TOther>(
            TimeSpan? max = null,
            TimeSpan? idle = null,
            Func<object, TOther>? filter = null,
            int msgs = int.MaxValue,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await foreach (var item in Probe.TestProbe.ReceiveWhileAsync(max, idle, filter, msgs, cancellationToken))
            {
                yield return item;
            }
        }

        /// <summary>
        /// Execute the async chain and then drains a given number of messages
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async IAsyncEnumerable<TOther> ReceiveWithinAsync<TOther>(
            TimeSpan? max, 
            int messages = int.MaxValue,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await foreach (var item in ManualProbe<T>.ReceiveWithinTask<TOther>(Probe.TestProbe, max, messages, cancellationToken))
            {
                yield return item;
            }
        }
        
        /// <summary>
        /// Execute the async chain and then execute the code block while bounding its execution time between <paramref name="min"/> and <paramref name="max"/>.
        /// <para />
        /// Note that the timeout is scaled using <see cref="TestKitBase.Dilated"/>, which uses the
        /// configuration entry "akka.test.timefactor", while the min Duration is not.
        /// 
        /// <![CDATA[
        /// var ret = await probe.AsyncBuilder().WithinAsync(Timespan.FromMilliseconds(50), Timespan.FromSeconds(3), () =>
        /// {
        ///     test.Tell("ping");
        ///     return ExpectMsg<string>();
        /// });
        /// ]]>
        ///
        /// <![CDATA[
        /// await probe.AsyncBuilder().WithinAsync(Timespan.FromMilliseconds(50), Timespan.FromSeconds(3), async () =>
        /// {
        ///     test.Tell("ping");
        ///     await ExpectMsgAsync<string>("expected");
        /// });
        /// ]]>
        /// 
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<TOther> WithinAsync<TOther>(
            TimeSpan min,
            TimeSpan max,
            Func<TOther> function,
            string? hint = null,
            TimeSpan? epsilonValue = null, 
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await Probe.TestProbe.WithinAsync(min, max, function, hint, epsilonValue, cancellationToken)
                .ConfigureAwait(false);
        }
        
        /// <summary>
        /// Execute the async chain and then execute the code block while bounding its execution time between <paramref name="min"/> and <paramref name="max"/>.
        /// <para />
        /// Note that the timeout is scaled using <see cref="TestKitBase.Dilated"/>, which uses the
        /// configuration entry "akka.test.timefactor", while the min Duration is not.
        /// 
        /// <![CDATA[
        /// var ret = await probe.AsyncBuilder().WithinAsync(Timespan.FromMilliseconds(50), Timespan.FromSeconds(3), async () =>
        /// {
        ///     test.Tell("ping");
        ///     await ExpectMsgAsync<string>("expected");
        /// });
        /// ]]>
        /// 
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<TOther> WithinAsync<TOther>(
            TimeSpan min,
            TimeSpan max,
            Func<Task<TOther>> asyncFunction,
            string? hint = null,
            TimeSpan? epsilonValue = null, 
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await Probe.TestProbe.WithinAsync(min, max, asyncFunction, hint, epsilonValue, cancellationToken)
                .ConfigureAwait(false);
        }
        
        /// <summary>
        /// Execute the async chain and then execute code block while bounding its execution time with a <paramref name="max"/> timeout.
        /// 
        /// <![CDATA[
        /// var ret = await probe.AsyncBuilder().WithinAsync(Timespan.FromSeconds(3), () =>
        /// {
        ///     test.Tell("ping");
        ///     return ExpectMsg<string>();
        /// });
        /// ]]>
        ///
        /// <![CDATA[
        /// await probe.AsyncBuilder().WithinAsync(Timespan.FromSeconds(3), async () =>
        /// {
        ///     test.Tell("ping");
        ///     await ExpectMsgAsync<string>("expected");
        /// });
        /// ]]>
        /// 
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<TOther> WithinAsync<TOther>(TimeSpan max, Func<TOther> execute, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return await Probe.TestProbe.WithinAsync(max, execute, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        
        /// <summary>
        /// Execute the async chain and then execute code block while bounding its execution time with a <paramref name="max"/> timeout.
        /// 
        /// <![CDATA[
        /// var ret = await probe.AsyncBuilder().WithinAsync(Timespan.FromSeconds(3), async () =>
        /// {
        ///     test.Tell("ping");
        ///     return await ExpectMsgAsync<string>();
        /// });
        /// ]]>
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<TOther> WithinAsync<TOther>(
            TimeSpan max,
            Func<Task<TOther>> actionAsync,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await Probe.TestProbe.WithinAsync(max, actionAsync, epsilonValue, cancellationToken)
                .ConfigureAwait(false);
        }
        
        /// <summary>
        /// Execute the async chain and then attempt to drain the stream into an IAsyncEnumerable (by requesting long.MaxValue elements).
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async IAsyncEnumerable<T> ToStrictAsync(
            TimeSpan atMost,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await foreach (var item in ManualProbe<T>.ToStrictTask(Probe, atMost, cancellationToken))
            {
                yield return item;
            }
        }

        #endregion

        private readonly List<Func<CancellationToken, Task>> _tasks = new List<Func<CancellationToken, Task>>();
        private bool _executed;

        internal SubscriberFluentBuilder(ManualProbe<T> probe)
        {
            Probe = probe;
        }
        
        public ManualProbe<T> Probe { get; }

        /// <summary>
        /// Execute the async chain.
        /// </summary>
        /// <param name="asyncAction"></param>
        /// <param name="cancellationToken"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task ExecuteAsync(Func<Task>? asyncAction = null, CancellationToken cancellationToken = default)
        {
            if (_executed)
                throw new InvalidOperationException("Fluent async builder has already been executed.");
            _executed = true;
            
            foreach (var func in _tasks)
            {
                await func(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (asyncAction != null)
                await asyncAction()
                    .ConfigureAwait(false);
        }
        
        #region Probe<T> wrapper

        /// <summary>
        /// Fluent async DSL.
        /// Ensure that the probe has received a subscription
        /// </summary>
        public SubscriberFluentBuilder<T> EnsureSubscription()
        {
            if (!(Probe is Probe<T> probe))
            {
                throw new InvalidOperationException("EnsureSubscription() can only be used on a TestSubscriber.Probe<T> instance");
            }
            _tasks.Add(async ct => await Probe<T>.EnsureSubscriptionTask(probe, ct));
            return this;
        } 
        
        /// <summary>
        /// Fluent async DSL.
        /// Request a specified number of elements.
        /// </summary>
        public SubscriberFluentBuilder<T> Request(long n)
        {
            if (!(Probe is Probe<T> probe))
            {
                throw new InvalidOperationException("Request() can only be used on a TestSubscriber.Probe<T> instance");
            }
            _tasks.Add(async ct =>
            {
                await Probe<T>.EnsureSubscriptionTask(probe, ct);
                probe.Subscription.Request(n);
            });
            return this;
        }

        /// <summary>
        /// Fluent async DSL.
        /// Request and expect a stream element.
        /// </summary>
        public SubscriberFluentBuilder<T> RequestNext(T element)
        {
            if (!(Probe is Probe<T> probe))
            {
                throw new InvalidOperationException("RequestNext() can only be used on a TestSubscriber.Probe<T> instance");
            }
            _tasks.Add(async ct =>
            {
                await Probe<T>.EnsureSubscriptionTask(probe, ct);
                probe.Subscription.Request(1);
            });
            return ExpectNext(element);
        }

        /// <summary>
        /// Fluent async DSL.
        /// Cancel the stream.
        /// </summary>
        public SubscriberFluentBuilder<T> Cancel()
        {
            if (!(Probe is Probe<T> probe))
            {
                throw new InvalidOperationException("Cancel() can only be used on a TestSubscriber.Probe<T> instance");
            }
            _tasks.Add(async ct =>
            {
                await Probe<T>.EnsureSubscriptionTask(probe, ct);
                probe.Subscription.Cancel();
            });
            return this;
        }

        #endregion

        #region ManualProbe<T> fluent wrapper

        /// <summary>
        /// Fluent async DSL.
        /// Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext{T}"/>,
        /// <see cref="OnError"/> or <see cref="OnComplete"/>).
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectEvent(ISubscriberEvent e)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectEventTask(Probe.TestProbe, e, ct));
            return this;
        }
        
        /// <summary>
        /// Fluent async DSL.
        /// Expect a stream element.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNext(T element, TimeSpan? timeout = null)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectNextTask(Probe.TestProbe, element, timeout, ct));
            return this;
        }
        
        /// <summary>
        /// Fluent async DSL.
        /// Expect a stream element during specified time or timeout.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNext(TimeSpan? timeout, T element)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectNextTask(Probe.TestProbe, element, timeout, ct));
            return this;
        }
        
        /// <summary>
        /// Fluent async DSL.
        /// Expect multiple stream elements.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNext(params T[] elems)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectNextTask(Probe, null, ct, elems));
            return this;
        }

        /// <summary>
        /// Fluent async DSL.
        /// Expect multiple stream elements during specified time or timeout.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNext(TimeSpan? timeout, params T[] elems)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectNextTask(Probe, timeout, ct, elems));
            return this;
        }

        /// <summary>
        /// Fluent async DSL.
        /// Expect multiple stream elements in arbitrary order.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNextUnordered(params T[] elems)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectNextUnorderedTask(Probe, null, ct, elems));
            return this;
        }

        /// <summary>
        /// Fluent async DSL.
        /// Expect multiple stream elements in arbitrary order during specified time or timeout.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNextUnordered(TimeSpan? timeout, params T[] elems)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectNextUnorderedTask(Probe, timeout, ct, elems));
            return this;
        }
        
        /// <summary>
        /// Fluent async DSL.
        /// Expect a single stream element matching one of the element in a list. Found element is removed from the list.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNextWithinSet(ICollection<T> elems)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectNextWithinSetTask(Probe.TestProbe, elems, ct));
            return this;
        }

        /// <summary>
        /// Fluent async DSL.
        /// Expect the given elements to be signalled in order.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNextN(IEnumerable<T> all, TimeSpan? timeout = null)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectNextNTask(Probe.TestProbe, all, timeout, ct));
            return this;
        }

        /// <summary>
        /// Fluent async DSL.
        /// Expect the given elements to be signalled in any order.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNextUnorderedN(IEnumerable<T> all, TimeSpan? timeout = null)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectNextUnorderedNTask(Probe, all, timeout, ct));
            return this;
        }
        
        /// <summary>
        /// Fluent async DSL.
        /// Expect completion.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectComplete()
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectCompleteTask(Probe.TestProbe, null, ct));
            return this;
        }

        /// <summary>
        /// Fluent async DSL.
        /// Expect completion with a timeout.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectComplete(TimeSpan? timeout)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectCompleteTask(Probe.TestProbe, timeout, ct));
            return this;
        }

        /// <summary>
        /// Fluent async DSL.
        /// Expect subscription followed by immediate stream completion.
        /// By default single demand will be signaled in order to wake up a possibly lazy upstream.
        /// </summary>
        /// <seealso cref="ExpectSubscriptionAndComplete(bool)"/>
        public SubscriberFluentBuilder<T> ExpectSubscriptionAndComplete()
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectSubscriptionAndCompleteTask(Probe, true, ct));
            return this;
        }

        /// <summary>
        /// Fluent async DSL.
        /// Expect subscription followed by immediate stream completion. Depending on the `signalDemand` parameter 
        /// demand may be signaled immediately after obtaining the subscription in order to wake up a possibly lazy upstream.
        /// You can disable this by setting the `signalDemand` parameter to `false`.
        /// </summary>
        /// <seealso cref="ExpectSubscriptionAndComplete()"/>
        public SubscriberFluentBuilder<T> ExpectSubscriptionAndComplete(bool signalDemand)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectSubscriptionAndCompleteTask(Probe, signalDemand, ct));
            return this;
        }
        
        /// <summary>
        /// Fluent async DSL.
        /// Expect given next element or error signal.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNextOrError(T element, Exception cause)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectNextOrErrorTask(Probe.TestProbe, element, cause, ct));
            return this;
        }   
        
        /// <summary>
        /// Fluent async DSL.
        /// Expect given next element or stream completion.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNextOrComplete(T element)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectNextOrCompleteTask(Probe.TestProbe, element, ct));
            return this;
        }

        /// <summary>
        /// Fluent async DSL.
        /// Same as <see cref="ExpectNoMsg(TimeSpan)"/>, but correctly treating the timeFactor.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNoMsg()
        {
            _tasks.Add(ct => Probe.TestProbe.ExpectNoMsgAsync(ct).AsTask());
            return this;
        }

        /// <summary>
        /// Fluent async DSL.
        /// Assert that no message is received for the specified time.
        /// </summary>
        public SubscriberFluentBuilder<T> ExpectNoMsg(TimeSpan remaining)
        {
            _tasks.Add(ct => Probe.TestProbe.ExpectNoMsgAsync(remaining, ct).AsTask());
            return this;
        }

        /// <summary>
        /// Fluent async DSL.
        /// Expect next element and test it with the <paramref name="predicate"/>
        /// </summary>
        /// <typeparam name="TOther">The System.Type of the expected message</typeparam>
        /// <param name="predicate">The System.Predicate{T} that is applied to the message</param>
        /// <returns>this</returns>
        public SubscriberFluentBuilder<T> MatchNext<TOther>(Predicate<TOther> predicate)
        {
            _tasks.Add(ct => ManualProbe<T>.MatchNextTask(Probe.TestProbe, predicate, ct));
            return this;
        }
        
        #endregion
    }
}