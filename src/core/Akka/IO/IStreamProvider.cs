//-----------------------------------------------------------------------
// <copyright file="IStreamProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.IO
{
    /// <summary>
    /// Abstraction for creating connected Stream instances for TCP connections.
    /// Enables transparent switching between plaintext (NetworkStream) and TLS (SslStream).
    /// </summary>
    public interface IStreamProvider
    {
        /// <summary>
        /// Establishes a connection and returns the connected stream.
        /// For TLS, the handshake completes inside this method.
        /// </summary>
        Task<Stream> ConnectAsync(string host, int port, CancellationToken ct);

        /// <summary>
        /// Closes the stream and releases resources.
        /// </summary>
        void Close();
    }
}
