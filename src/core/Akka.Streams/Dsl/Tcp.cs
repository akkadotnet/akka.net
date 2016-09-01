//-----------------------------------------------------------------------
// <copyright file="Tcp.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.IO;

namespace Akka.Streams.Dsl
{
    public class Tcp : ExtensionIdProvider<TcpExt>
    {
        public override TcpExt CreateExtension(ExtendedActorSystem system) => new TcpExt(system);

        /// <summary>
        /// Represents a successful TCP server binding.
        /// </summary>
        public struct ServerBinding
        {
            private readonly Func<Task> _unbindAction;

            public ServerBinding(EndPoint localAddress, Func<Task> unbindAction)
            {
                _unbindAction = unbindAction;
                LocalAddress = localAddress;
            }

            public readonly EndPoint LocalAddress;

            public Task Unbind() => _unbindAction();
        }

        /// <summary>
        /// Represents an accepted incoming TCP connection.
        /// </summary>
        public struct IncomingConnection
        {
            public IncomingConnection(EndPoint localAddress, EndPoint remoteAddress, Flow<ByteString, ByteString, NotUsed> flow)
            {
                LocalAddress = localAddress;
                RemoteAddress = remoteAddress;
                Flow = flow;
            }

            public readonly EndPoint LocalAddress;

            public readonly EndPoint RemoteAddress;

            public readonly Flow<ByteString, ByteString, NotUsed> Flow;

            /// <summary>
            /// Handles the connection using the given flow, which is materialized exactly once and the respective
            /// materialized instance is returned.
            /// <para/>
            /// Convenience shortcut for: flow.join(handler).run().
            /// </summary>
            public TMat HandleWith<TMat>(Flow<ByteString, ByteString, TMat> handler, IMaterializer materializer)
                => Flow.JoinMaterialized(handler, Keep.Right).Run(materializer);
        }

        /// <summary>
        /// Represents a prospective outgoing TCP connection.
        /// </summary>
        public struct OutgoingConnection
        {
            public OutgoingConnection(EndPoint remoteAddress, EndPoint localAddress)
            {
                LocalAddress = localAddress;
                RemoteAddress = remoteAddress;
            }

            public readonly EndPoint LocalAddress;

            public readonly EndPoint RemoteAddress;
        }
    }

    public class TcpExt : IExtension
    {
        private readonly ExtendedActorSystem _system;

        public TcpExt(ExtendedActorSystem system)
        {
            _system = system;
            BindShutdownTimeout = ActorMaterializer.Create(system).Settings.SubscriptionTimeoutSettings.Timeout;
        }

        protected readonly TimeSpan BindShutdownTimeout;

        /// <summary>
        /// Creates a <see cref="Tcp.ServerBinding"/> instance which represents a prospective TCP server binding on the given <paramref name="host"/> and <paramref name="port"/>/>.
        /// <para/>
        /// Please note that the startup of the server is asynchronous, i.e. after materializing the enclosing
        /// <see cref="RunnableGraph{TMat}"/> the server is not immediately available. Only after the materialized future
        /// completes is the server ready to accept client connections.
        /// </summary>
        /// <param name="host">The host to listen on</param>
        /// <param name="port">The port to listen on</param>
        /// <param name="backlog">Controls the size of the connection backlog</param>
        /// <param name="options">TCP options for the connections, see <see cref="Akka.IO.Tcp"/> for details</param>
        /// <param name="halfClose">Controls whether the connection is kept open even after writing has been completed to the accepted TCP connections.
        /// If set to true, the connection will implement the TCP half-close mechanism, allowing the client to
        /// write to the connection even after the server has finished writing. The TCP socket is only closed
        /// after both the client and server finished writing.
        /// If set to false, the connection will immediately closed once the server closes its write side,
        /// independently whether the client is still attempting to write. This setting is recommended
        /// for servers, and therefore it is the default setting.
        /// </param>
        public Source<Tcp.IncomingConnection, Task<Tcp.ServerBinding>> Bind(string host, int port, int backlog = 100,
            IImmutableList<Inet.SocketOption> options = null, bool halfClose = false, TimeSpan? idleTimeout = null)
        {
            // DnsEnpoint isn't allowed
            var ipAddresses = System.Net.Dns.GetHostAddressesAsync(host).Result;
            if (ipAddresses.Length == 0)
                throw new ArgumentException($"Couldn't resolve IpAdress for host {host}", nameof(host));

            return Source.FromGraph(new ConnectionSourceStage(_system.Tcp(), new IPEndPoint(ipAddresses[0], port), backlog,
                options, halfClose, idleTimeout, BindShutdownTimeout));
        }

        /// <summary>
        /// Creates a <see cref="Tcp.ServerBinding"/> instance which represents a prospective TCP server binding on the given <paramref name="host"/> and <paramref name="port"/>/>
        /// handling the incoming connections using the provided Flow.
        /// <para/>
        /// Please note that the startup of the server is asynchronous, i.e. after materializing the enclosing
        /// <see cref="RunnableGraph{TMat}"/> the server is not immediately available. Only after the materialized future
        /// completes is the server ready to accept client connections.
        /// </summary>
        /// <param name="handler">A Flow that represents the server logic</param>
        /// <param name="host">The host to listen on</param>
        /// <param name="port">The port to listen on</param>
        /// <param name="backlog">Controls the size of the connection backlog</param>
        /// <param name="options">TCP options for the connections, see <see cref="Akka.IO.Tcp"/> for details</param>
        /// <param name="halfClose">Controls whether the connection is kept open even after writing has been completed to the accepted TCP connections.
        /// If set to true, the connection will implement the TCP half-close mechanism, allowing the client to
        /// write to the connection even after the server has finished writing. The TCP socket is only closed
        /// after both the client and server finished writing.
        /// If set to false, the connection will immediately closed once the server closes its write side,
        /// independently whether the client is still attempting to write. This setting is recommended
        /// for servers, and therefore it is the default setting.
        /// </param>
        public Task<Tcp.ServerBinding> BindAndHandle(Flow<ByteString, ByteString, NotUsed> handler, IMaterializer materializer, string host, int port, int backlog = 100,
            IImmutableList<Inet.SocketOption> options = null, bool halfClose = false, TimeSpan? idleTimeout = null)
        {
            return Bind(host, port, backlog, options, halfClose, idleTimeout)
                .To(Sink.ForEach<Tcp.IncomingConnection>(connection => connection.Flow.Join(handler).Run(materializer)))
                .Run(materializer);
        }

        /// <summary>
        /// Creates a <see cref="Tcp.OutgoingConnection"/> instance representing a prospective TCP client connection to the given endpoint.
        /// </summary>
        /// <param name="remoteAddress"> The remote address to connect to</param>
        /// <param name="localAddress">Optional local address for the connection</param>
        /// <param name="options">TCP options for the connections, see <see cref="Akka.IO.Tcp"/> for details</param>
        /// <param name="halfClose"> Controls whether the connection is kept open even after writing has been completed to the accepted TCP connections.
        /// If set to true, the connection will implement the TCP half-close mechanism, allowing the server to
        /// write to the connection even after the client has finished writing.The TCP socket is only closed
        /// after both the client and server finished writing. This setting is recommended for clients and therefore it is the default setting.
        /// If set to false, the connection will immediately closed once the client closes its write side,
        /// independently whether the server is still attempting to write.
        /// </param>
        public Flow<ByteString, ByteString, Task<Tcp.OutgoingConnection>> OutgoingConnection(EndPoint remoteAddress, EndPoint localAddress = null,
            IImmutableList<Inet.SocketOption> options = null, bool halfClose = true, TimeSpan? connectionTimeout = null, TimeSpan? idleTimeout = null)
        {
            connectionTimeout = connectionTimeout ?? TimeSpan.MaxValue;

            var tcpFlow =
                Flow.FromGraph(new OutgoingConnectionStage(_system.Tcp(), remoteAddress, localAddress, options,
                    halfClose, connectionTimeout)).Via(new Detacher<ByteString>());

            if (idleTimeout.HasValue)
                return tcpFlow.Join(BidiFlow.BidirectionalIdleTimeout<ByteString, ByteString>(idleTimeout.Value));

            return tcpFlow;
        }

        /// <summary>
        /// Creates an <see cref="Tcp.OutgoingConnection"/> without specifying options.
        /// It represents a prospective TCP client connection to the given endpoint.
        /// </summary>
        public Flow<ByteString, ByteString, Task<Tcp.OutgoingConnection>> OutgoingConnection(string host, int port)
            => OutgoingConnection(new DnsEndPoint(host, port));
    }

    public static class TcpStreamExtensions
    {
        public static TcpExt TcpStream(this ActorSystem system) => system.WithExtension<TcpExt, Tcp>();
    }
}
