//-----------------------------------------------------------------------
// <copyright file="StreamTcpTlsDocTests.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Buffers;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Dsl;
using Tcp = Akka.Streams.Dsl.Tcp;

namespace Akka.Docs.Tests.Streams
{
    public static class StreamTcpTlsDocTests
    {
        #region tls-server
        public static Task<Tcp.ServerBinding> StartTlsEchoServer(
            ActorSystem system,
            IMaterializer materializer,
            X509Certificate2 serverCertificate)
        {
            return system.TcpStream().BindAndHandleTls(
                Flow.Create<ReadOnlySequence<byte>>(),
                materializer,
                "127.0.0.1",
                9000,
                TlsServerSettings.ServerOnly(serverCertificate));
        }
        #endregion

        #region tls-client
        public static Source<ReadOnlySequence<byte>, Task<Tcp.OutgoingConnection>> CreateTlsClient(
            ActorSystem system,
            EndPoint remoteAddress,
            TlsClientSettings tlsSettings,
            ReadOnlySequence<byte> payload)
        {
            return Source.Single(payload)
                .ViaMaterialized(system.TcpStream().OutgoingConnectionTls(remoteAddress, tlsSettings), Keep.Right);
        }
        #endregion
    }
}
