//-----------------------------------------------------------------------
// <copyright file="TestUtils.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Streams.TestKit
{
    public static class TestUtils
    {
        public static IPEndPoint TemporaryServerAddress(string hostName = "127.0.0.1", bool udp = false)
        {
            var host = new IPEndPoint(IPAddress.Parse(hostName), 0);
            using (var socket = new Socket(
                udp ? SocketType.Dgram : SocketType.Stream,
                udp ? ProtocolType.Udp : ProtocolType.Tcp))
            {
                socket.Bind(host);
                return new IPEndPoint(IPAddress.Loopback, ((IPEndPoint) socket.LocalEndPoint).Port);
            }
        }

        public static IEnumerable<IPEndPoint> TemporaryServerAddresses(int numberOfAddresses,
            string hostName = "127.0.0.1", bool udp = false)
        {
            return Enumerable.Range(0, numberOfAddresses).Select(_ => TemporaryServerAddress(hostName, udp));
        }
    }

#if NETSTANDARD2_1
    /// <summary>
    /// INTERNAL API: Polyfills for <c>Task.WaitAsync</c> overloads on netstandard2.1.
    /// </summary>
    internal static class TaskWaitAsyncPolyfill
    {
        public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(timeout, linked.Token);
            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (completed == delay)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException();
            }
            linked.Cancel();
            await task.ConfigureAwait(false);
        }

        public static async Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(timeout, linked.Token);
            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (completed == delay)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException();
            }
            linked.Cancel();
            return await task.ConfigureAwait(false);
        }
    }
#endif
}
