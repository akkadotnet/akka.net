//-----------------------------------------------------------------------
// <copyright file="TestPublisher_Fluent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Reactive.Streams;

namespace Akka.Streams.TestKit
{
    // Fluent implementation
    public static partial class TestPublisher
    {
        public partial class ManualProbe<T>
        {
            /// <summary>
            /// Fluent async DSL.
            /// This will return an instance of <see cref="PublisherFluentBuilder{T}"/> that will compose and run
            /// all of its method call asynchronously.
            /// Note that <see cref="PublisherFluentBuilder{T}"/> contains two types of methods:
            /// * Methods that returns <see cref="PublisherFluentBuilder{T}"/> are used to chain test methods together
            ///   using a fluent builder pattern. 
            /// * Methods with names that ends with the postfix "Async" and returns either a <see cref="Task"/> or
            ///   a <see cref="Task{TResult}"/>. These methods invokes the previously chained methods asynchronously one
            ///   after another before executing its own code.
            /// </summary>
            /// <returns></returns>
            public PublisherFluentBuilder<T> AsyncBuilder()
                => new PublisherFluentBuilder<T>(this);

            /// <summary>
            /// Fluent DSL
            /// Expect demand from the given subscription.
            /// </summary>
            public ManualProbe<T> ExpectRequest(
                ISubscription subscription,
                int nrOfElements,
                CancellationToken cancellationToken = default)
            {
                ExpectRequestTask(Probe, subscription, nrOfElements, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL
            /// Expect no messages.
            /// </summary>
            public ManualProbe<T> ExpectNoMsg(CancellationToken cancellationToken = default)
            {
                ExpectNoMsgTask(Probe, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            /// <summary>
            /// Fluent DSL
            /// Expect no messages for given duration.
            /// </summary>
            public ManualProbe<T> ExpectNoMsg(TimeSpan duration, CancellationToken cancellationToken = default)
            {
                ExpectNoMsgTask(Probe, duration, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            
        }
        
        public partial class Probe<T>
        {
            public Probe<T> SendNext(T element, CancellationToken cancellationToken = default)
            {
                SendNextTask(this, element, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            public Probe<T> UnsafeSendNext(T element, CancellationToken cancellationToken = default)
            {
                UnsafeSendNextTask(this, element, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            public Probe<T> SendComplete(CancellationToken cancellationToken = default)
            {
                SendCompleteTask(this, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }

            public Probe<T> SendError(Exception e, CancellationToken cancellationToken = default)
            {
                SendErrorTask(this, e, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }
            
            public Probe<T> ExpectCancellation(CancellationToken cancellationToken = default)
            {
                ExpectCancellationTask(this, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return this;
            }
            
        }
    }
}
