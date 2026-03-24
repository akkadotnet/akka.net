//-----------------------------------------------------------------------
// <copyright file="TcpStreamProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.IO
{
    /// <summary>
    /// Default IStreamProvider that creates a plaintext NetworkStream from a connected Socket.
    /// </summary>
    public sealed class TcpStreamProvider : IStreamProvider
    {
        private Socket? _socket;

        public async Task<Stream> ConnectAsync(string host, int port, CancellationToken ct)
        {
            _socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await _socket.ConnectAsync(host, port, ct).ConfigureAwait(false);
            return new NetworkStream(_socket, ownsSocket: true);
        }

        public void Close()
        {
            _socket?.Dispose();
            _socket = null;
        }
    }
}
