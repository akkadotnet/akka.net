//-----------------------------------------------------------------------
// <copyright file="TestPublisher_Shared.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.TestKit;
using Reactive.Streams;

namespace Akka.Streams.TestKit
{
    public static partial class TestPublisher
    {
        public partial class ManualProbe<T>
        {
            protected static async Task<StreamTestKit.PublisherProbeSubscription<T>> ExpectSubscriptionTask(
                TestProbe probe,
                CancellationToken cancellationToken)
            {
                var msg = await probe.ExpectMsgAsync<Subscribe>(cancellationToken: cancellationToken); 
                return (StreamTestKit.PublisherProbeSubscription<T>) msg.Subscription;
            }

            #region void methods

            internal static async Task ExpectRequestTask(
                TestProbe probe,
                ISubscription subscription, 
                int nrOfElements,
                CancellationToken cancellationToken)
                => await probe.ExpectMsgAsync<RequestMore>(
                    isMessage: x => x.NrOfElements == nrOfElements && x.Subscription == subscription, 
                    cancellationToken: cancellationToken);
            
            internal static async Task ExpectNoMsgTask(TestProbe probe, CancellationToken cancellationToken)
                => await probe.ExpectNoMsgAsync(cancellationToken);

            internal static async Task ExpectNoMsgTask(
                TestProbe probe,
                TimeSpan duration,
                CancellationToken cancellationToken)
                => await probe.ExpectNoMsgAsync(duration, cancellationToken);

            #endregion

            #region Return type methods

            internal static IAsyncEnumerable<TOther> ReceiveWhileTask<TOther>(
                TestProbe probe,
                TimeSpan? max,
                TimeSpan? idle,
                Func<object, TOther> filter, 
                int msgCount,
                CancellationToken cancellationToken) where TOther : class
                => probe.ReceiveWhileAsync(max, idle, filter, msgCount, cancellationToken);

            internal static ValueTask<IPublisherEvent> ExpectEventTask(
                TestProbe probe,
                CancellationToken cancellationToken)
                => probe.ExpectMsgAsync<IPublisherEvent>(cancellationToken: cancellationToken);

            #endregion
        }
        
        public partial class Probe<T>
        {
            #region void methods

            internal static async Task EnsureSubscriptionTask(Probe<T> probe, CancellationToken cancellationToken)
            {
                probe.Subscription ??= await ExpectSubscriptionTask(probe.Probe, cancellationToken);
            }
            
            internal static async Task SendNextTask(Probe<T> probe, T element, CancellationToken cancellationToken)
            {
                await EnsureSubscriptionTask(probe, cancellationToken);
                var sub = probe.Subscription;
                if (probe.Pending == 0)
                    probe.Pending = await sub.ExpectRequestAsync(cancellationToken);
                probe.Pending--;
                sub.SendNext(element);
            }

            internal static async Task SendNextTask(Probe<T> probe, IEnumerable<T> elements, CancellationToken cancellationToken)
            {
                await EnsureSubscriptionTask(probe, cancellationToken);
                var sub = probe.Subscription;
                foreach (var element in elements)
                {
                    if (probe.Pending == 0)
                        probe.Pending = await sub.ExpectRequestAsync(cancellationToken);
                    probe.Pending--;
                    sub.SendNext(element);
                }
            }
            
            internal static async Task UnsafeSendNextTask(Probe<T> probe, T element, CancellationToken cancellationToken)
            {
                await EnsureSubscriptionTask(probe, cancellationToken);
                probe.Subscription.SendNext(element);
            }

            internal static async Task SendCompleteTask(Probe<T> probe, CancellationToken cancellationToken)
            {
                await EnsureSubscriptionTask(probe, cancellationToken);
                probe.Subscription.SendComplete();
            }
            
            internal static async Task SendErrorTask(Probe<T> probe, Exception e, CancellationToken cancellationToken)
            {
                await EnsureSubscriptionTask(probe, cancellationToken);
                probe.Subscription.SendError(e);
            }
            
            #endregion

            #region Return type methods

            internal static async Task<long> ExpectRequestTask(Probe<T> probe, CancellationToken cancellationToken)
            {
                await EnsureSubscriptionTask(probe, cancellationToken);
                var requests = await probe.Subscription.ExpectRequestAsync(cancellationToken);
                probe.Pending += requests;
                return requests;
            }

            internal static async Task ExpectCancellationTask(Probe<T> probe, CancellationToken cancellationToken = default)
            {
                await EnsureSubscriptionTask(probe, cancellationToken);
                await probe.Subscription.ExpectCancellationAsync(cancellationToken);
            }

            #endregion
        }
    }
}
