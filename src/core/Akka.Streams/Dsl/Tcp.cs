//-----------------------------------------------------------------------
// <copyright file="Tcp.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Net;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.IO;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.IO;


namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Provides access to the Akka.Streams TCP extension.
    /// </summary>
    public class Tcp : ExtensionIdProvider<TcpExt>
    {
        /// <summary>
        /// Creates the TCP extension for an actor system.
        /// </summary>
        /// <param name="system">The actor system receiving the extension.</param>
        /// <returns>A TCP extension instance.</returns>
        public override TcpExt CreateExtension(ExtendedActorSystem system) => new(system);

        /// <summary>
        /// Represents a successful TCP server binding.
        /// </summary>
        public readonly struct ServerBinding
        {
            private readonly Func<Task> _unbindAction;

            /// <summary>
            /// Initializes a new instance of the <see cref="ServerBinding"/> class.
            /// </summary>
            /// <param name="localAddress">The local endpoint on which the server is bound.</param>
            /// <param name="unbindAction">An asynchronous operation that unbinds the server.</param>
            public ServerBinding(EndPoint localAddress, Func<Task> unbindAction)
            {
                _unbindAction = unbindAction;
                LocalAddress = localAddress;
            }

            /// <summary>
            /// The local endpoint used by the server binding.
            /// </summary>
            public readonly EndPoint LocalAddress;

            /// <summary>
            /// Unbinds the server from its local endpoint.
            /// </summary>
            /// <returns>A task that completes when unbinding finishes.</returns>
            public Task Unbind() => _unbindAction();
        }

        /// <summary>
        /// Represents an accepted incoming TCP connection.
        /// </summary>
        public readonly struct IncomingConnection
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="IncomingConnection"/> class.
            /// </summary>
            /// <param name="localAddress">The local endpoint for the accepted connection.</param>
            /// <param name="remoteAddress">The remote endpoint that opened the connection.</param>
            /// <param name="flow">The flow that sends and receives bytes for this connection.</param>
            public IncomingConnection(EndPoint localAddress, EndPoint remoteAddress, Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> flow)
            {
                LocalAddress = localAddress;
                RemoteAddress = remoteAddress;
                Flow = flow;
            }

            /// <summary>
            /// The local endpoint for the accepted connection.
            /// </summary>
            public readonly EndPoint LocalAddress;

            /// <summary>
            /// The remote endpoint for the accepted connection.
            /// </summary>
            public readonly EndPoint RemoteAddress;

            /// <summary>
            /// The bidirectional byte flow for the accepted connection.
            /// </summary>
            public readonly Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> Flow;

            /// <summary>
            /// Handles the connection using the given flow, which is materialized exactly once and the respective
            /// materialized instance is returned.
            /// <para/>
            /// Convenience shortcut for: flow.join(handler).run().
            /// </summary>
            /// <typeparam name="TMat">The handler flow's materialized value type.</typeparam>
            /// <param name="handler">The flow that handles this connection's incoming and outgoing bytes.</param>
            /// <param name="materializer">The materializer used to run the connected flow.</param>
            /// <returns>The handler flow's materialized value.</returns>
            public TMat HandleWith<TMat>(Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, TMat> handler, IMaterializer materializer)
                => Flow.JoinMaterialized(handler, Keep.Right).Run(materializer);
        }

        /// <summary>
        /// Represents a prospective outgoing TCP connection.
        /// </summary>
        public readonly struct OutgoingConnection
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="OutgoingConnection"/> class.
            /// </summary>
            /// <param name="remoteAddress">The remote endpoint for the outgoing connection.</param>
            /// <param name="localAddress">The optional local endpoint to bind for the connection.</param>
            public OutgoingConnection(EndPoint remoteAddress, EndPoint localAddress)
            {
                LocalAddress = localAddress;
                RemoteAddress = remoteAddress;
            }

            /// <summary>
            /// The local endpoint assigned to the established outgoing connection.
            /// </summary>
            public readonly EndPoint LocalAddress;

            /// <summary>
            /// The remote endpoint for the outgoing connection.
            /// </summary>
            public readonly EndPoint RemoteAddress;
        }
    }

    /// <summary>
    /// Actor-system extension that creates TCP stream sources and flows.
    /// </summary>
    public class TcpExt : IExtension
    {
        private readonly ExtendedActorSystem _system;

        /// <summary>
        /// Initializes a new instance of the <see cref="TcpExt"/> class.
        /// </summary>
        /// <param name="system">The extended actor system that owns the TCP manager.</param>
        [InternalApi]
        public TcpExt(ExtendedActorSystem system)
        {
            _system = system;
            BindShutdownTimeout = ActorMaterializer.Create(system).Settings.SubscriptionTimeoutSettings.Timeout;
        }

        /// <summary>
        /// The timeout used while shutting down a TCP server binding.
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
        /// <param name="idleTimeout">Optional maximum idle interval for an accepted connection; when elapsed without traffic in either direction, the connection fails.</param>
        /// <exception cref="ArgumentException">Thrown when the host resolves to no IP addresses.</exception>
        /// <returns>A source of incoming connections that materializes to a task for the server binding.</returns>
        // TODO: this really needs to be an async method
        public Source<Tcp.IncomingConnection, Task<Tcp.ServerBinding>> Bind(string host, int port, int backlog = 100,
            IImmutableList<Inet.SocketOption> options = null, bool halfClose = false, TimeSpan? idleTimeout = null)
        {
            // DnsEndpoint isn't allowed
            var ipAddresses = System.Net.Dns.GetHostAddressesAsync(host).Result;
            if (ipAddresses.Length == 0)
                throw new ArgumentException($"Couldn't resolve IpAddress for host {host}", nameof(host));

            return CreateBindingSource(new IPEndPoint(ipAddresses[0], port), backlog, options, halfClose, idleTimeout, null);
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
        /// <param name="materializer">The materializer used to run the handler flow for each accepted connection.</param>
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
        /// <param name="idleTimeout">Optional maximum idle interval for a connection; when elapsed without traffic in either direction, the flow fails.</param>
        /// <returns>A task that completes with the server binding once the listener is bound.</returns>
        public Task<Tcp.ServerBinding> BindAndHandle(Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> handler, IMaterializer materializer, string host, int port, int backlog = 100,
            IImmutableList<Inet.SocketOption> options = null, bool halfClose = false, TimeSpan? idleTimeout = null)
        {
            return Bind(host, port, backlog, options, halfClose, idleTimeout)
                .To(Sink.ForEach<Tcp.IncomingConnection>(connection => connection.Flow.Join(handler).Run(materializer)))
                .Run(materializer);
        }

        /// <summary>
        /// Creates a TLS-enabled TCP listener. The binding becomes ready as soon as the listener is bound; each incoming
        /// connection is emitted only after its TLS handshake succeeds.
        /// </summary>
        /// <param name="host">The host to listen on.</param>
        /// <param name="port">The port to listen on.</param>
        /// <param name="tlsSettings">The TLS settings for authenticating incoming connections.</param>
        /// <param name="backlog">Controls the size of the connection backlog.</param>
        /// <param name="options">TCP options for the connections.</param>
        /// <param name="halfClose">Whether accepted connections support independent input and output completion.</param>
        /// <param name="idleTimeout">Optional maximum idle interval for an accepted connection.</param>
        /// <returns>A source of authenticated incoming connections and a task for the server binding.</returns>

#nullable enable
        public Source<Tcp.IncomingConnection, Task<Tcp.ServerBinding>> BindTls(string host, int port,
            TlsServerSettings tlsSettings, int backlog = 100, IImmutableList<Inet.SocketOption>? options = null,
            bool halfClose = false, TimeSpan? idleTimeout = null)
        {
            if (tlsSettings == null) throw new ArgumentNullException(nameof(tlsSettings));
            var ipAddresses = System.Net.Dns.GetHostAddresses(host);
            if (ipAddresses.Length == 0)
                throw new ArgumentException($"Couldn't resolve IpAddress for host {host}", nameof(host));

            return CreateBindingSource(new IPEndPoint(ipAddresses[0], port), backlog, options, halfClose, idleTimeout, tlsSettings);
        }

        private Source<Tcp.IncomingConnection, Task<Tcp.ServerBinding>> CreateBindingSource(EndPoint endpoint, int backlog,
            IImmutableList<Inet.SocketOption>? options, bool halfClose, TimeSpan? idleTimeout, TlsServerSettings? tlsSettings)
        {
            return Source.FromGraph(new ConnectionSourceStage(_system.Tcp(), endpoint, backlog, options, halfClose,
                idleTimeout, BindShutdownTimeout, tlsSettings));
        }

        /// <summary>
        /// Binds a TLS-enabled listener and handles each authenticated incoming connection with <paramref name="handler"/>.
        /// </summary>
        /// <param name="handler">A flow that represents the server logic.</param>
        /// <param name="materializer">The materializer used to run the handler for each connection.</param>
        /// <param name="host">The host to listen on.</param>
        /// <param name="port">The port to listen on.</param>
        /// <param name="tlsSettings">The TLS settings for authenticating incoming connections.</param>
        /// <param name="backlog">Controls the size of the connection backlog.</param>
        /// <param name="options">TCP options for the connections.</param>
        /// <param name="halfClose">Whether accepted connections support independent input and output completion.</param>
        /// <param name="idleTimeout">Optional maximum idle interval for an accepted connection.</param>
        /// <returns>A task that completes with the server binding once the listener is bound.</returns>
        public Task<Tcp.ServerBinding> BindAndHandleTls(Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> handler,
            IMaterializer materializer, string host, int port, TlsServerSettings tlsSettings, int backlog = 100,
            IImmutableList<Inet.SocketOption>? options = null, bool halfClose = false, TimeSpan? idleTimeout = null)
        {
            return BindTls(host, port, tlsSettings, backlog, options, halfClose, idleTimeout)
                .To(Sink.ForEach<Tcp.IncomingConnection>(connection => connection.Flow.Join(handler).Run(materializer)))
                .Run(materializer);
        }
#nullable restore

        /// <summary>
        /// Creates a <see cref="Tcp.OutgoingConnection"/> instance representing a prospective TCP client connection to the given endpoint.
        /// <para>
        /// Note that the <c>ReadOnlySequence&lt;byte&gt;</c> chunk boundaries are not retained across the network,
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
        /// <param name="connectionTimeout">Optional maximum time allowed to establish the TCP connection.</param>
        /// <param name="idleTimeout">Optional maximum interval without traffic in either direction before the flow fails.</param>
        /// <returns>A byte flow whose materialized task completes, on connection, with the requested remote endpoint and the established local endpoint.</returns>
        public Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, Task<Tcp.OutgoingConnection>> OutgoingConnection(EndPoint remoteAddress, EndPoint localAddress = null,
            IImmutableList<Inet.SocketOption> options = null, bool halfClose = true, TimeSpan? connectionTimeout = null, TimeSpan? idleTimeout = null)
        {
            return CreateOutgoingConnection(remoteAddress, localAddress, options, halfClose, connectionTimeout, idleTimeout, null);
        }

#nullable enable
        private Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, Task<Tcp.OutgoingConnection>> CreateOutgoingConnection(
            EndPoint remoteAddress, EndPoint? localAddress, IImmutableList<Inet.SocketOption>? options, bool halfClose,
            TimeSpan? connectionTimeout, TimeSpan? idleTimeout, TlsClientSettings? tlsSettings)
        {
            var tcpFlow = Flow.FromGraph(new OutgoingConnectionStage(_system.Tcp(), remoteAddress, localAddress, options,
                    halfClose, connectionTimeout, tlsSettings))
                .Via(new Detacher<ReadOnlySequence<byte>>());

            if (idleTimeout.HasValue)
                return tcpFlow.Join(BidiFlow.BidirectionalIdleTimeout<ReadOnlySequence<byte>, ReadOnlySequence<byte>>(idleTimeout.Value));

            return tcpFlow;
        }

        /// <summary>
        /// Creates a TLS-enabled outgoing TCP connection. Its materialized task completes only after the TLS handshake succeeds.
        /// </summary>
        /// <param name="remoteAddress">The remote address to connect to.</param>
        /// <param name="tlsSettings">The TLS settings for authenticating the remote server.</param>
        /// <param name="localAddress">Optional local address for the connection.</param>
        /// <param name="options">TCP options for the connection.</param>
        /// <param name="halfClose">Whether input and output sides can complete independently.</param>
        /// <param name="connectionTimeout">Optional maximum time allowed to establish the TCP connection.</param>
        /// <param name="idleTimeout">Optional maximum interval without traffic in either direction.</param>
        /// <returns>A byte flow whose materialized task completes with the endpoints after TLS authentication.</returns>
        public Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, Task<Tcp.OutgoingConnection>> OutgoingConnectionTls(
            EndPoint remoteAddress, TlsClientSettings tlsSettings, EndPoint? localAddress = null,
            IImmutableList<Inet.SocketOption>? options = null, bool halfClose = true,
            TimeSpan? connectionTimeout = null, TimeSpan? idleTimeout = null)
        {
            if (tlsSettings == null) throw new ArgumentNullException(nameof(tlsSettings));
            return CreateOutgoingConnection(remoteAddress, localAddress, options, halfClose, connectionTimeout, idleTimeout, tlsSettings);
        }
#nullable restore

        /// <summary>
        /// Creates an <see cref="Tcp.OutgoingConnection"/> without specifying options.
        /// It represents a prospective TCP client connection to the given endpoint.
        /// <para>
        /// Note that the <c>ReadOnlySequence&lt;byte&gt;</c> chunk boundaries are not retained across the network,
        /// to achieve application level chunks you have to introduce explicit framing in your streams,
        /// for example using the <see cref="Framing"/> stages.
        /// </para>
        /// </summary>
        /// <param name="host">The remote host name or IP address.</param>
        /// <param name="port">The remote TCP port.</param>
        /// <returns>A byte flow whose materialized task completes, on connection, with the requested remote endpoint and the established local endpoint.</returns>
        public Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, Task<Tcp.OutgoingConnection>> OutgoingConnection(string host, int port)
            => OutgoingConnection(CreateEndpoint(host, port));

        /// <summary>
        /// Creates a TLS-enabled outgoing TCP connection to the specified host and port.
        /// </summary>
        /// <param name="host">The remote host name or IP address.</param>
        /// <param name="port">The remote TCP port.</param>
        /// <param name="tlsSettings">The TLS settings for authenticating the remote server.</param>
        /// <returns>A byte flow whose materialized task completes with the endpoints after TLS authentication.</returns>

#nullable enable
        public Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, Task<Tcp.OutgoingConnection>> OutgoingConnectionTls(
            string host, int port, TlsClientSettings tlsSettings) =>
            OutgoingConnectionTls(CreateEndpoint(host, port), tlsSettings);
#nullable restore

        internal static EndPoint CreateEndpoint(string host, int port)
        {
            return IPAddress.TryParse(host, out var address)
                ? (EndPoint) new IPEndPoint(address, port)
                : new DnsEndPoint(host, port);
        }
    }

    /// <summary>
    /// Extension methods for accessing the TCP stream extension from an actor system.
    /// </summary>
    public static class TcpStreamExtensions
    {
        /// <summary>
        /// Gets the TCP stream extension for the actor system.
        /// </summary>
        /// <param name="system">The actor system whose TCP extension is requested.</param>
        /// <returns>The TCP stream extension associated with <paramref name="system"/>.</returns>
        public static TcpExt TcpStream(this ActorSystem system) => system.WithExtension<TcpExt, Tcp>();
    }

    public sealed class TcpIdleTimeoutException : TimeoutException
    {
        public TcpIdleTimeoutException(string message, TimeSpan duration) : base(message)
        {
            Duration = duration;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TcpIdleTimeoutException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        public TcpIdleTimeoutException(SerializationInfo info, StreamingContext context) : base(info, context) { }

        public TimeSpan Duration { get; }
    }
}
