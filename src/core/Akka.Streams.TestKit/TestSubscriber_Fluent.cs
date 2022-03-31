// //-----------------------------------------------------------------------
// // <copyright file="TestSubscriber_Fluent.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;

namespace Akka.Streams.TestKit
{
    public static partial class TestSubscriber
    {
        public partial class ManualProbe<T>
        {
            /// <summary>
            /// Fluent DSL. Expect and return <see cref="ISubscriberEvent"/> (any of: <see cref="OnSubscribe"/>, <see cref="OnNext"/>, <see cref="OnError"/> or <see cref="OnComplete"/>).
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectEvent(ISubscriberEvent e, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectEvent(e, cancellationToken);

            /// <inheritdoc cref="ExpectEvent(ISubscriberEvent,CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectEventAsync(ISubscriberEvent e, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectEventAsync(e, cancellationToken);

            /// <summary>
            /// Fluent DSL. Expect a stream element.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectNext(T element, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNext(element, timeout, cancellationToken);

            /// <inheritdoc cref="ExpectNext(T,Nullable{TimeSpan},CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectNextAsync(T element, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNextAsync(element, timeout, cancellationToken);

            /// <summary>
            /// Fluent DSL. Expect a stream element during specified time or timeout.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectNext(TimeSpan? timeout, T element, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNext(element, timeout, cancellationToken);

            /// <inheritdoc cref="ExpectNext(Nullable{TimeSpan},T,CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectNextAsync(TimeSpan? timeout, T element, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNextAsync(element, timeout, cancellationToken);

            /// <summary>
            /// Fluent DSL. Expect multiple stream elements.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectNext(params T[] elems)
                => new SubscriberFluentBuilder<T>(this).ExpectNext(elems);

            /// <inheritdoc cref="ExpectNext(T[])"/>
            public SubscriberFluentBuilder<T> ExpectNextAsync(params T[] elems)
                => new SubscriberFluentBuilder<T>(this).ExpectNextAsync(elems);

            /// <summary>
            /// Fluent DSL. Expect multiple stream elements.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectNext(TimeSpan? timeout, params T[] elems)
                => new SubscriberFluentBuilder<T>(this).ExpectNext(timeout, elems);

            /// <inheritdoc cref="ExpectNext(Nullable{TimeSpan},T[])"/>
            public SubscriberFluentBuilder<T> ExpectNextAsync(TimeSpan? timeout, params T[] elems)
                => new SubscriberFluentBuilder<T>(this).ExpectNextAsync(timeout, elems);

            /// <summary>
            /// FluentDSL. Expect multiple stream elements in arbitrary order.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectNextUnordered(params T[] elems)
                => new SubscriberFluentBuilder<T>(this).ExpectNextUnordered(elems);

            /// <inheritdoc cref="ExpectNextUnordered(T[])"/>
            public SubscriberFluentBuilder<T> ExpectNextUnorderedAsync(params T[] elems)
                => new SubscriberFluentBuilder<T>(this).ExpectNextUnorderedAsync(elems);

            /// <summary>
            /// FluentDSL. Expect multiple stream elements in arbitrary order during specified timeout.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectNextUnordered(TimeSpan? timeout, params T[] elems)
                => new SubscriberFluentBuilder<T>(this).ExpectNextUnordered(timeout, elems);

            /// <inheritdoc cref="ExpectNextUnordered(Nullable{TimeSpan},T[])"/>
            public SubscriberFluentBuilder<T> ExpectNextUnorderedAsync(TimeSpan? timeout, params T[] elems)
                => new SubscriberFluentBuilder<T>(this).ExpectNextUnorderedAsync(timeout, elems);

            /// <summary>
            /// FluentDSL. Expect a single stream element matching one of the element in a list.
            /// Found element is removed from the list.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectNextWithinSet(ICollection<T> elems, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNextWithinSet(elems, cancellationToken);

            /// <inheritdoc cref="ExpectNextWithinSet(ICollection{T},CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectNextWithinSetAsync(ICollection<T> elems, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNextWithinSetAsync(elems, cancellationToken);

            /// <summary>
            /// Fluent DSL. Expect the given elements to be signalled in order.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectNextN(IEnumerable<T> all, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNextN(all, timeout, cancellationToken);
            
            /// <inheritdoc cref="ExpectNextN(IEnumerable{T},Nullable{TimeSpan},CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectNextNAsync(IEnumerable<T> all, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNextNAsync(all, timeout, cancellationToken);
            
            /// <summary>
            /// Fluent DSL. Expect the given elements to be signalled in any order.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectNextUnorderedN(IEnumerable<T> all, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNextUnorderedN(all, timeout, cancellationToken);

            /// <inheritdoc cref="ExpectNextUnorderedN(IEnumerable{T},Nullable{TimeSpan},CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectNextUnorderedNAsync(IEnumerable<T> all, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNextUnorderedNAsync(all, timeout, cancellationToken);

            /// <summary>
            /// Fluent DSL. Expect completion.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectComplete(CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectComplete(cancellationToken);
            
            /// <inheritdoc cref="ExpectComplete(CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectCompleteAsync(CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectCompleteAsync(cancellationToken);
            
            /// <summary>
            /// Fluent DSL. Expect completion with a timeout.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectComplete(TimeSpan? timeout, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectComplete(timeout, cancellationToken);

            /// <inheritdoc cref="ExpectComplete(Nullable{TimeSpan},CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectCompleteAsync(TimeSpan? timeout, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectCompleteAsync(timeout, cancellationToken);

            /// <summary>
            /// Fluent DSL. Expect subscription followed by immediate stream completion. By default single demand will be signaled in order to wake up a possibly lazy upstream
            /// </summary>
            /// <seealso cref="ExpectSubscriptionAndComplete(bool, CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectSubscriptionAndComplete(CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectSubscriptionAndComplete(cancellationToken);

            /// <inheritdoc cref="ExpectSubscriptionAndComplete(CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectSubscriptionAndCompleteAsync(CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectSubscriptionAndCompleteAsync(cancellationToken);

            /// <summary>
            /// Fluent DSL. Expect subscription followed by immediate stream completion. Depending on the `signalDemand` parameter 
            /// demand may be signaled immediately after obtaining the subscription in order to wake up a possibly lazy upstream.
            /// You can disable this by setting the `signalDemand` parameter to `false`.
            /// </summary>
            /// <seealso cref="ExpectSubscriptionAndComplete(CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectSubscriptionAndComplete(bool signalDemand, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectSubscriptionAndComplete(signalDemand, cancellationToken);

            /// <inheritdoc cref="ExpectSubscriptionAndComplete(bool, CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectSubscriptionAndCompleteAsync(bool signalDemand, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectSubscriptionAndCompleteAsync(signalDemand, cancellationToken);

            /// <summary>
            /// Fluent DSL. Expect given next element or error signal.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectNextOrError(T element, Exception cause, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNextOrError(element, cause, cancellationToken);
            
            /// <inheritdoc cref="ExpectNextOrError(T,Exception,CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectNextOrErrorAsync(T element, Exception cause, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNextOrErrorAsync(element, cause, cancellationToken);
            
            /// <summary>
            /// Fluent DSL. Expect given next element or stream completion.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectNextOrComplete(T element, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNextOrComplete(element, cancellationToken);

            /// <inheritdoc cref="ExpectNextOrComplete(T,CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectNextOrCompleteAsync(T element, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNextOrCompleteAsync(element, cancellationToken);

            /// <summary>
            /// Fluent DSL. Same as <see cref="ExpectNoMsg(TimeSpan, CancellationToken)"/>, but correctly treating the timeFactor.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectNoMsg(CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNoMsg(cancellationToken);
            
            /// <inheritdoc cref="ExpectNoMsg(CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectNoMsgAsync(CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNoMsgAsync(cancellationToken);
            
            /// <summary>
            /// Fluent DSL. Assert that no message is received for the specified time.
            /// </summary>
            public SubscriberFluentBuilder<T> ExpectNoMsg(TimeSpan remaining, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNoMsg(remaining, cancellationToken);

            /// <inheritdoc cref="ExpectNoMsg(TimeSpan,CancellationToken)"/>
            public SubscriberFluentBuilder<T> ExpectNoMsgAsync(TimeSpan remaining, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).ExpectNoMsgAsync(remaining, cancellationToken);

            /// <summary>
            /// Expect next element and test it with the <paramref name="predicate"/>
            /// </summary>
            /// <typeparam name="TOther">The System.Type of the expected message</typeparam>
            /// <param name="predicate">The System.Predicate{T} that is applied to the message</param>
            /// <param name="cancellationToken"></param>
            /// <returns>this</returns>
            public SubscriberFluentBuilder<T> MatchNext<TOther>(Predicate<TOther> predicate, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).MatchNext(predicate, cancellationToken);
            
            /// <inheritdoc cref="MatchNext{TOther}(Predicate{TOther},CancellationToken)"/>
            public SubscriberFluentBuilder<T> MatchNextAsync<TOther>(Predicate<TOther> predicate, CancellationToken cancellationToken = default)
                => new SubscriberFluentBuilder<T>(this).MatchNextAsync(predicate, cancellationToken);
        }
    }
}