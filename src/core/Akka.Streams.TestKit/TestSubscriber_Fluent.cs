//-----------------------------------------------------------------------
// <copyright file="TestSubscriber_Fluent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Streams.TestKit
{
    public static partial class TestSubscriber
    {
        public partial class ManualProbe<T>
        {
            /// <summary>
            /// Fluent async DSL.
            /// This will return an instance of <see cref="SubscriberFluentBuilder{T}"/> that will compose and run
            /// all of its method call asynchronously.
            /// Note that <see cref="SubscriberFluentBuilder{T}"/> contains two types of methods:
            /// * Methods that returns <see cref="SubscriberFluentBuilder{T}"/> are used to chain test methods together
            ///   using a fluent builder pattern. 
            /// * Methods with names that ends with the postfix "Async" and returns either a <see cref="Task"/> or
            ///   a <see cref="Task{TResult}"/>. These methods invokes the previously chained methods asynchronously one
            ///   after another before executing its own code.
            /// </summary>
            /// <returns></returns>
            public SubscriberFluentBuilder<T> AsyncBuilder()
                => new SubscriberFluentBuilder<T>(this);

            /// <summary>
            /// Fluent DSL. Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public ManualProbe<T> ExpectEvent(ISubscriberEvent e,
                CancellationToken cancellationToken = default)
            {
                ExpectEventTask(TestProbe, e, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect a stream element.
            /// </summary>
            public ManualProbe<T> ExpectNext(T element, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            {
                ExpectNextTask(TestProbe, element, timeout, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect a stream element during specified time or timeout.
            /// </summary>
            public ManualProbe<T> ExpectNext(TimeSpan? timeout, T element, CancellationToken cancellationToken = default)
            {
                //=> new SubscriberFluentBuilder<T>(this).ExpectNext(element, timeout, cancellationToken);
                ExpectNextTask(TestProbe, element, timeout, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect multiple stream elements.
            /// </summary>
            public ManualProbe<T> ExpectNext(params T[] elems)
            {
                ExpectNextTask(this, null, default, elems)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect multiple stream elements.
            /// </summary>
            public ManualProbe<T> ExpectNext(CancellationToken cancellationToken, params T[] elems)
            {
                ExpectNextTask(this, null, cancellationToken, elems)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect multiple stream elements.
            /// </summary>
            [Obsolete("Use the method with CancellationToken support instead")]
            public ManualProbe<T> ExpectNext(TimeSpan? timeout, params T[] elems)
            {
                ExpectNextTask(this, timeout, default, elems)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect multiple stream elements.
            /// </summary>
            public ManualProbe<T> ExpectNext(TimeSpan? timeout, CancellationToken cancellationToken, params T[] elems)
            {
                ExpectNextTask(this, timeout, cancellationToken, elems)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect multiple stream elements in arbitrary order.
            /// </summary>
            public ManualProbe<T> ExpectNextUnordered(params T[] elems)
            {
                ExpectNextUnorderedTask(this, null, default, elems)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// FluentDSL. Expect multiple stream elements in arbitrary order.
            /// </summary>
            public ManualProbe<T> ExpectNextUnordered(CancellationToken cancellationToken, params T[] elems)
            {
                ExpectNextUnorderedTask(this, null, cancellationToken, elems)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// FluentDSL. Expect multiple stream elements in arbitrary order during specified timeout.
            /// </summary>
            [Obsolete("Use the method with CancellationToken support instead")]
            public ManualProbe<T> ExpectNextUnordered(TimeSpan? timeout, params T[] elems)
            {
                ExpectNextUnorderedTask(this, timeout, default, elems)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// FluentDSL. Expect multiple stream elements in arbitrary order during specified timeout.
            /// </summary>
            public ManualProbe<T> ExpectNextUnordered(TimeSpan? timeout, CancellationToken cancellationToken, params T[] elems)
            {
                ExpectNextUnorderedTask(this, timeout, cancellationToken, elems)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// FluentDSL. Expect a single stream element matching one of the element in a list.
            /// Found element is removed from the list.
            /// </summary>
            public ManualProbe<T> ExpectNextWithinSet(ICollection<T> elems, CancellationToken cancellationToken = default)
            {
                ExpectNextWithinSetTask(TestProbe, elems, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect the given elements to be signalled in order.
            /// </summary>
            public ManualProbe<T> ExpectNextN(IEnumerable<T> all, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            {
                ExpectNextNTask(TestProbe, all, timeout, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }
            
            /// <summary>
            /// Fluent DSL. Expect the given elements to be signalled in any order.
            /// </summary>
            public ManualProbe<T> ExpectNextUnorderedN(IEnumerable<T> all, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            {
                ExpectNextUnorderedNTask(this, all, timeout, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect completion.
            /// </summary>
            public ManualProbe<T> ExpectComplete(CancellationToken cancellationToken = default)
            {
                ExpectCompleteTask(TestProbe, null, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }
            
            public async Task ExpectCompleteAsync(CancellationToken cancellationToken = default)
                => await ExpectCompleteTask(TestProbe, null, cancellationToken);
            
            /// <summary>
            /// Fluent DSL. Expect completion with a timeout.
            /// </summary>
            public ManualProbe<T> ExpectComplete(TimeSpan? timeout, CancellationToken cancellationToken = default)
            {
                ExpectCompleteTask(TestProbe, timeout, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect subscription followed by immediate stream completion. By default single demand will be signaled in order to wake up a possibly lazy upstream
            /// </summary>
            /// <seealso cref="ExpectSubscriptionAndComplete(bool, CancellationToken)"/>
            public ManualProbe<T> ExpectSubscriptionAndComplete(CancellationToken cancellationToken = default)
            {
                ExpectSubscriptionAndCompleteTask(this, true, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect subscription followed by immediate stream completion. Depending on the `signalDemand` parameter 
            /// demand may be signaled immediately after obtaining the subscription in order to wake up a possibly lazy upstream.
            /// You can disable this by setting the `signalDemand` parameter to `false`.
            /// </summary>
            /// <seealso cref="ExpectSubscriptionAndComplete(CancellationToken)"/>
            public ManualProbe<T> ExpectSubscriptionAndComplete(bool signalDemand, CancellationToken cancellationToken = default)
            {
                ExpectSubscriptionAndCompleteTask(this, signalDemand, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Expect given next element or error signal.
            /// </summary>
            public ManualProbe<T> ExpectNextOrError(T element, Exception cause, CancellationToken cancellationToken = default)
            {
                ExpectNextOrErrorTask(TestProbe, element, cause, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }
            
            /// <summary>
            /// Fluent DSL. Expect given next element or stream completion.
            /// </summary>
            public ManualProbe<T> ExpectNextOrComplete(T element, CancellationToken cancellationToken = default)
            {
                ExpectNextOrCompleteTask(TestProbe, element, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL. Same as <see cref="ExpectNoMsg(TimeSpan, CancellationToken)"/>, but correctly treating the timeFactor.
            /// </summary>
            public ManualProbe<T> ExpectNoMsg(CancellationToken cancellationToken = default)
            {
                TestProbe.ExpectNoMsgAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }
            
            /// <summary>
            /// Fluent DSL. Assert that no message is received for the specified time.
            /// </summary>
            public ManualProbe<T> ExpectNoMsg(TimeSpan remaining, CancellationToken cancellationToken = default)
            {
                TestProbe.ExpectNoMsgAsync(remaining, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;                
            }

            /// <summary>
            /// Expect next element and test it with the <paramref name="predicate"/>
            /// </summary>
            /// <typeparam name="TOther">The System.Type of the expected message</typeparam>
            /// <param name="predicate">The System.Predicate{T} that is applied to the message</param>
            /// <param name="cancellationToken"></param>
            /// <returns>this</returns>
            public ManualProbe<T> MatchNext<TOther>(Predicate<TOther> predicate, CancellationToken cancellationToken = default)
            {
                MatchNextTask(TestProbe, predicate, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;                
            }
        }

        public partial class Probe<T>
        {
            public Probe<T> Request(long n)
            {
                EnsureSubscription();
                Subscription.Request(n);
                return this;
            }

            public Probe<T> Cancel()
            {
                EnsureSubscription();
                Subscription.Cancel();
                return this;
            }

            public Probe<T> RequestNext(T element)
            {
                EnsureSubscription();
                Subscription.Request(1);
                ExpectNext(element);
                return this;
            }


        }
    }
}
