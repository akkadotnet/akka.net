//-----------------------------------------------------------------------
// <copyright file="Tcp.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.IO;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.IO;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// TBD
    /// </summary>
    public class Tcp : ExtensionIdProvider<TcpExt>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override TcpExt CreateExtension(ExtendedActorSystem system) => new TcpExt(system);

        /// <summary>
        /// Represents a successful TCP server binding.
        /// </summary>
        public struct ServerBinding
        {
            private readonly Func<Task> _unbindAction;

            /// <summary>
            /// Initializes a new instance of the <see cref="ServerBinding"/> class.
            /// </summary>
            /// <param name="localAddress">TBD</param>
            /// <param name="unbindAction">TBD</param>
            public ServerBinding(EndPoint localAddress, Func<Task> unbindAction)
            {
                _unbindAction = unbindAction;
                LocalAddress = localAddress;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public readonly EndPoint LocalAddress;

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public Task Unbind() => _unbindAction();
        }

        /// <summary>
        /// Represents an accepted incoming TCP connection.
        /// </summary>
        public struct IncomingConnection
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="IncomingConnection"/> class.
            /// </summary>
            /// <param name="localAddress">TBD</param>
            /// <param name="remoteAddress">TBD</param>
            /// <param name="flow">TBD</param>
            public IncomingConnection(EndPoint localAddress, EndPoint remoteAddress, Flow<ByteString, ByteString, NotUsed> flow)
            {
                LocalAddress = localAddress;
                RemoteAddress = remoteAddress;
                Flow = flow;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public readonly EndPoint LocalAddress;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly EndPoint RemoteAddress;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly Flow<ByteString, ByteString, NotUsed> Flow;

            /// <summary>
            /// Handles the connection using the given flow, which is materialized exactly once and the respective
            /// materialized instance is returned.
            /// <para/>
            /// Convenience shortcut for: flow.join(handler).run().
            /// </summary>
            /// <typeparam name="TMat">TBD</typeparam>
            /// <param name="handler">TBD</param>
            /// <param name="materializer">TBD</param>
            /// <returns>TBD</returns>
            public TMat HandleWith<TMat>(Flow<ByteString, ByteString, TMat> handler, IMaterializer materializer)
                => Flow.JoinMaterialized(handler, Keep.Right).Run(materializer);
        }

        /// <summary>
        /// Represents a prospective outgoing TCP connection.
        /// </summary>
        public struct OutgoingConnection
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="OutgoingConnection"/> class.
            /// </summary>
            /// <param name="remoteAddress">TBD</param>
            /// <param name="localAddress">TBD</param>
            public OutgoingConnection(EndPoint remoteAddress, EndPoint localAddress)
            {
                LocalAddress = localAddress;
                RemoteAddress = remoteAddress;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public readonly EndPoint LocalAddress;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly EndPoint RemoteAddress;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class TcpExt : IExtension
    {
        private readonly ExtendedActorSystem _system;

        /// <summary>
        /// Initializes a new instance of the <see cref="TcpExt"/> class.
        /// </summary>
        /// <param name="system">TBD</param>
        [InternalApi]
        public TcpExt(ExtendedActorSystem system)
        {
            _system = system;
            BindShutdownTimeout = ActorMaterializer.Create(system).Settings.SubscriptionTimeoutSettings.Timeout;
        }

        /// <summary>
        /// TBD
        /// </summary>
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
        /// <param name="idleTimeout">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public Source<Tcp.IncomingConnection, Task<Tcp.ServerBinding>> Bind(string host, int port, int backlog = 100,
            IImmutableList<Inet.SocketOption> options = null, bool halfClose = false, TimeSpan? idleTimeout = null)
        {
            // DnsEndpoint isn't allowed
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
        /// <param name="materializer">TBD</param>
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
        /// <param name="idleTimeout">TBD</param>
        /// <returns>TBD</returns>
        public Task<Tcp.ServerBinding> BindAndHandle(Flow<ByteString, ByteString, NotUsed> handler, IMaterializer materializer, string host, int port, int backlog = 100,
            IImmutableList<Inet.SocketOption> options = null, bool halfClose = false, TimeSpan? idleTimeout = null)
        {
            return Bind(host, port, backlog, options, halfClose, idleTimeout)
                .To(Sink.ForEach<Tcp.IncomingConnection>(connection => connection.Flow.Join(handler).Run(materializer)))
                .Run(materializer);
        }

        /// <summary>
        /// Creates a <see cref="Tcp.OutgoingConnection"/> instance representing a prospective TCP client connection to the given endpoint.
        /// <para>
        /// Note that the <see cref="ByteString"/> chunk boundaries are not retained across the network,
        /// to achieve application level chunks you have to introduce explicit framing in your streams,
        /// for example using the <see cref="Framing"/> stages.
        /// </para>
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
        /// <param name="connectionTimeout">TBD</param>
        /// <param name="idleTimeout">TBD</param>
        /// <returns>TBD</returns>
        public Flow<ByteString, ByteString, Task<Tcp.OutgoingConnection>> OutgoingConnection(EndPoint remoteAddress, EndPoint localAddress = null,
            IImmutableList<Inet.SocketOption> options = null, bool halfClose = true, TimeSpan? connectionTimeout = null, TimeSpan? idleTimeout = null)
        {
            //connectionTimeout = connectionTimeout ?? TimeSpan.FromMinutes(60);

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
        /// <para>
        /// Note that the <see cref="ByteString"/> chunk boundaries are not retained across the network,
        /// to achieve application level chunks you have to introduce explicit framing in your streams,
        /// for example using the <see cref="Framing"/> stages.
        /// </para>
        /// </summary>
        /// <param name="host">TBD</param>
        /// <param name="port">TBD</param>
        /// <returns>TBD</returns>
        public Flow<ByteString, ByteString, Task<Tcp.OutgoingConnection>> OutgoingConnection(string host, int port)
            => OutgoingConnection(CreateEndpoint(host, port));

        internal static EndPoint CreateEndpoint(string host, int port)
        {
            IPAddress address;
            return IPAddress.TryParse(host, out address)
                ? (EndPoint) new IPEndPoint(address, port)
                : new DnsEndPoint(host, port);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class TcpStreamExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static TcpExt TcpStream(this ActorSystem system) => system.WithExtension<TcpExt, Tcp>();
    }

    public sealed class TcpIdleTimeoutException : TimeoutException
    {
        public TcpIdleTimeoutException(string message, TimeSpan duration) : base(message)
        {
            Duration = duration;
        }

        public TimeSpan Duration { get; }
    }
}
