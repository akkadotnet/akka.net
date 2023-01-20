//-----------------------------------------------------------------------
// <copyright file="PublisherFluentBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.TestKit;
using Reactive.Streams;
using static Akka.Streams.TestKit.TestPublisher;

namespace Akka.Streams.TestKit
{
    public class PublisherFluentBuilder<T>
    {
        private readonly List<Func<CancellationToken, Task>> _tasks = new List<Func<CancellationToken, Task>>();
        private bool _executed;

        internal PublisherFluentBuilder(ManualProbe<T> probe)
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

        #region ManualProbe<T> wrapper

        /// <summary>
        /// Execute the async chain and then receive messages for a given duration or until one does not match a given partial function.
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async IAsyncEnumerable<TOther> ReceiveWhileAsync<TOther>(
            TimeSpan? max = null,
            TimeSpan? idle = null,
            Func<object, TOther>? filter = null,
            int msgCount = int.MaxValue,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) where TOther : class
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await foreach (var item in ManualProbe<T>.ReceiveWhileTask(Probe.Probe, max, idle, filter, msgCount, cancellationToken))
            {
                yield return item;
            }
        }
        
        /// <summary>
        /// Execute the async chain and then expect a publisher event from the stream.
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<IPublisherEvent> ExpectEventAsync(CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await ManualProbe<T>.ExpectEventTask(Probe.Probe, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Execute the async chain and then execute the code block while bounding its execution time between <paramref name="min"/> and
        /// <paramref name="max"/>. <see cref="WithinAsync{TOther}(TimeSpan,TimeSpan,Func{Task{TOther}},CancellationToken)"/> blocks may be nested. 
        /// All methods in this class which take maximum wait times are available in a version which implicitly uses
        /// the remaining time governed by the innermost enclosing <see cref="WithinAsync{TOther}(TimeSpan,TimeSpan,Func{Task{TOther}},CancellationToken)"/> block.
        /// 
        /// <para />
        /// 
        /// Note that the timeout is scaled using <see cref="TestKitBase.Dilated"/>, which uses the
        /// configuration entry "akka.test.timefactor", while the min Duration is not.
        /// 
        /// <![CDATA[
        /// var ret = await probe.AsyncBuilder().Within(Timespan.FromMilliseconds(50), Timespan.FromSeconds(3), async () =>
        /// {
        ///     test.Tell("ping");
        ///     return await ExpectMsgAsync<string>();
        /// });
        /// ]]>
        /// 
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<TOther> WithinAsync<TOther>(
            TimeSpan min,
            TimeSpan max,
            Func<Task<TOther>> function,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await Probe.WithinAsync(min, max, function, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Sane as calling WithinAsync(TimeSpan.Zero, max, function, cancellationToken).
        /// 
        /// NOTE: This method will execute the async chain
        /// </summary>
        public async Task<TOther> WithinAsync<TOther>(TimeSpan max, Func<Task<TOther>> execute, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await Probe.WithinAsync(max, execute, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        
        #endregion
        
        #region ManualProbe<T> fluent wrapper

        /// <summary>
        /// Fluent async DSL
        /// Expect demand from the given subscription.
        /// </summary>
        public PublisherFluentBuilder<T> ExpectRequest(ISubscription subscription, int nrOfElements)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectRequestTask(Probe.Probe, subscription, nrOfElements, ct));
            return this;
        }

        /// <summary>
        /// Fluent async DSL
        /// Expect no messages.
        /// </summary>
        public PublisherFluentBuilder<T> ExpectNoMsg()
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectNoMsgTask(Probe.Probe, ct));
            return this;
        }

        /// <summary>
        /// Fluent async DSL
        /// Expect no messages for given duration.
        /// </summary>
        public PublisherFluentBuilder<T> ExpectNoMsg(TimeSpan duration, CancellationToken cancellationToken = default)
        {
            _tasks.Add(ct => ManualProbe<T>.ExpectNoMsgTask(Probe.Probe, duration, ct));
            return this;
        }

        #endregion
        
        #region Probe<T> fluent wrapper
        
        /// <summary>
        /// Asserts that a subscription has been received or will be received
        /// </summary>
        public PublisherFluentBuilder<T> EnsureSubscription()
        {
            if (!(Probe is Probe<T> probe))
            {
                throw new InvalidOperationException($"{nameof(EnsureSubscription)} can only be used on a {nameof(Probe<T>)} instance");
            }
            _tasks.Add(ct => Probe<T>.EnsureSubscriptionTask(probe, ct));
            return this;
        }

        public PublisherFluentBuilder<T> SendNext(T element)
        {
            if (!(Probe is Probe<T> probe))
            {
                throw new InvalidOperationException($"{nameof(SendNext)} can only be used on a {nameof(Probe<T>)} instance");
            }
            _tasks.Add(ct => Probe<T>.SendNextTask(probe, element, ct));
            return this;
        }

        public PublisherFluentBuilder<T> SendNext(IEnumerable<T> elements)
        {
            if (!(Probe is Probe<T> probe))
            {
                throw new InvalidOperationException($"{nameof(SendNext)} can only be used on a {nameof(Probe<T>)} instance");
            }
            _tasks.Add(ct => Probe<T>.SendNextTask(probe, elements, ct));
            return this;
        }

        public PublisherFluentBuilder<T> UnsafeSendNext(T element)
        {
            if (!(Probe is Probe<T> probe))
            {
                throw new InvalidOperationException($"{nameof(UnsafeSendNext)} can only be used on a {nameof(Probe<T>)} instance");
            }
            _tasks.Add(ct => Probe<T>.UnsafeSendNextTask(probe, element, ct));
            return this;
        }

        public PublisherFluentBuilder<T> SendComplete()
        {
            if (!(Probe is Probe<T> probe))
            {
                throw new InvalidOperationException($"{nameof(SendComplete)} can only be used on a {nameof(Probe<T>)} instance");
            }
            _tasks.Add(ct => Probe<T>.SendCompleteTask(probe, ct));
            return this;
        }

        public PublisherFluentBuilder<T> SendError(Exception e)
        {
            if (!(Probe is Probe<T> probe))
            {
                throw new InvalidOperationException($"{nameof(SendError)} can only be used on a {nameof(Probe<T>)} instance");
            }
            _tasks.Add(ct => Probe<T>.SendErrorTask(probe, e, ct));
            return this;
        }

        public PublisherFluentBuilder<T> ExpectCancellation()
        {
            if (!(Probe is Probe<T> probe))
            {
                throw new InvalidOperationException($"{nameof(ExpectCancellation)} can only be used on a {nameof(Probe<T>)} instance");
            }
            _tasks.Add(ct => Probe<T>.ExpectCancellationTask(probe, ct));
            return this;
        }
        
        #endregion
        
        public async Task<long> ExpectRequestAsync(CancellationToken cancellationToken = default)
        {
            if (!(Probe is Probe<T> probe))
            {
                throw new InvalidOperationException($"{nameof(ExpectCancellation)} can only be used on a {nameof(Probe<T>)} instance");
            }

            await ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await Probe<T>.ExpectRequestTask(probe, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
