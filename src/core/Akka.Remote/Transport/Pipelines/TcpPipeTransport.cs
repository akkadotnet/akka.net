//-----------------------------------------------------------------------
// <copyright file="TcpPipeTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Transport.DotNetty;
using Akka.Util;

namespace Akka.Remote.Transport.Pipelines
{
    /// <summary>
    /// A <see cref="Transport"/> implementation backed by <see cref="System.IO.Pipelines"/>
    /// and raw BCL sockets — no DotNetty dependency on the hot path.
    ///
    /// <para>
    /// Activate by adding <c>akka.remote.pipe.tcp</c> to <c>akka.remote.enabled-transports</c>:
    /// <code>
    /// akka.remote.enabled-transports = ["akka.remote.pipe.tcp"]
    /// </code>
    /// </para>
    ///
    /// <para>
    /// Phase 1 design: this class implements the existing <see cref="Transport"/> SPI so
    /// every actor layer above it (<c>EndpointManager</c>, <c>AkkaProtocolTransport</c>,
    /// <c>ProtocolStateActor</c>) is unchanged.  See
    /// <c>akka-remote-transport-redux.md</c> for the full design rationale.
    /// </para>
    ///
    /// <!-- CopilotNotes: Public because the Remoting startup code reflects on the type
    ///      name stored in HOCON transport-class. The class must be constructible with
    ///      (ActorSystem, Config) — the same convention as TcpTransport. -->
    /// </summary>
    public sealed class TcpPipeTransport : Transport
    {
        // ── Fields ─────────────────────────────────────────────────────────────
        private readonly PipeTransportSettings _settings;
        private readonly ILoggingAdapter _log;

        // Server socket; set during Listen().
        private Socket? _serverSocket;

        // Resolved local Akka address; set during Listen().
        private Address? _localAddress;

        // Completed with the IAssociationEventListener when the upper layer is ready.
        private readonly TaskCompletionSource<IAssociationEventListener> _associationListenerPromise = new();

        // Cancellation source for the accept loop and all connections.
        private readonly CancellationTokenSource _shutdownCts = new();

        // Thread-safe set of active connections — used for graceful shutdown.
        private readonly ConcurrentSet<PipeConnection> _connections = new();

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Constructor called by the Akka.Remote transport loader (via reflection).
        /// Signature must be <c>(ActorSystem system, Config config)</c>.
        /// </summary>
        /// <param name="system">The hosting actor system.</param>
        /// <param name="config">
        /// The resolved <c>akka.remote.pipe.tcp</c> config block.
        /// </param>
        public TcpPipeTransport(ActorSystem system, Config config)
        {
            System             = system;
            Config             = config;
            _settings          = PipeTransportSettings.Create(config);
            _log               = Logging.GetLogger(system, this);
            SchemeIdentifier   = (_settings.EnableSsl ? "ssl." : string.Empty) + "tcp";
            MaximumPayloadBytes = _settings.MaxFrameSize;
        }

        // ── Transport contract ─────────────────────────────────────────────────

        /// <inheritdoc/>
        public override string SchemeIdentifier { get; protected set; }

        /// <inheritdoc/>
        public override long MaximumPayloadBytes { get; protected set; }

        /// <inheritdoc/>
        public override bool IsResponsibleFor(Address remote) => true;

        /// <inheritdoc/>
        /// <summary>
        /// Binds the server socket and schedules the accept loop to start once the
        /// <see cref="IAssociationEventListener"/> is registered.
        /// </summary>
        public override async Task<(Address, TaskCompletionSource<IAssociationEventListener>)> Listen()
        {
            if (_settings.EnableSsl)
                _settings.Ssl.ValidateCertificate(); // fail fast on bad cert

            var listenEndPoint = await ResolveEndpointAsync(_settings.Hostname, _settings.Port)
                .ConfigureAwait(false);

            _serverSocket = CreateServerSocket();
            _serverSocket.Bind(listenEndPoint);
            _serverSocket.Listen(_settings.Backlog);

            var localEp = (IPEndPoint)_serverSocket.LocalEndPoint!;
            _localAddress = DotNettyTransport.MapSocketToAddress(
                localEp,
                schemeIdentifier: SchemeIdentifier,
                systemName:       System.Name,
                hostName:         _settings.PublicHostname,
                publicPort:       _settings.PublicPort);

            if (_localAddress is null)
                throw new InvalidOperationException(
                    $"Could not map local endpoint [{localEp}] to an Akka.Remote Address.");

            // Defer accept loop until the upper layer has registered an IAssociationEventListener.
            // This mirrors DotNetty's AutoRead=false-until-ready behaviour.
            _ = _associationListenerPromise.Task.ContinueWith(
                _ => AcceptLoopAsync(_shutdownCts.Token),
                TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously);

            _log.Info("Pipe transport listening on [{0}]", _localAddress);
            return (_localAddress, _associationListenerPromise);
        }

        /// <inheritdoc/>
        /// <summary>
        /// Resolves the remote <paramref name="remoteAddress"/>, connects a socket,
        /// and returns a <see cref="PipeAssociationHandle"/> backed by a started
        /// <see cref="PipeConnection"/>.
        /// </summary>
        public override async Task<AssociationHandle> Associate(Address remoteAddress)
        {
            if (_localAddress is null)
                throw new InvalidOperationException(
                    "Transport has not been started. Call Listen() before Associate().");

            var remoteEp = await ResolveEndpointAsync(remoteAddress.Host!, remoteAddress.Port!.Value)
                .ConfigureAwait(false);

            var socket = CreateClientSocket();
            try
            {
                using var connectCts = new CancellationTokenSource(_settings.ConnectTimeout);
                await socket.ConnectAsync(remoteEp, connectCts.Token).ConfigureAwait(false);

                var stream = await BuildStreamAsync(socket, remoteAddress.Host!, isServer: false, connectCts.Token)
                    .ConfigureAwait(false);

                var handle = new PipeAssociationHandle(_localAddress!, remoteAddress);
                var conn   = new PipeConnection(
                    socket, stream, handle, this, _log, _settings.WriteChannelCapacity);

                _connections.TryAdd(conn);
                conn.Start();

                _log.Debug("Pipe transport: outbound connection established to [{0}]", remoteAddress);
                return handle;
            }
            catch (InvalidAssociationException)
            {
                // Already the right type — rethrow without wrapping.
                throw;
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw new InvalidAssociationException(
                    $"Connection to [{remoteAddress}] timed out after {_settings.ConnectTimeout}.");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                socket.Dispose();
                throw new InvalidAssociationException(
                    $"Connection refused by [{remoteAddress}].", ex);
            }
            catch (Exception ex)
            {
                // CopilotNotes: Wrap ALL connection-setup failures as InvalidAssociationException so
                // EndpointManager's gating logic works correctly — mirrors how DotNetty's
                // HandleConnectException wraps everything in InvalidAssociationException.
                socket.Dispose();
                throw new InvalidAssociationException(
                    $"Failed to associate with [{remoteAddress}]: {ex.Message}", ex);
            }
        }

        /// <inheritdoc/>
        public override async Task<bool> Shutdown()
        {
            _log.Debug("Pipe transport: shutting down.");
            _shutdownCts.Cancel();

            // Quietly close all active connections (no Disassociated events — system is going away).
            foreach (var conn in _connections)
                conn.DisassociateQuiet();
            _connections.Clear();

            try { _serverSocket?.Close(); } catch { /* Best-effort */ }

            await Task.CompletedTask.ConfigureAwait(false);
            return true;
        }

        // ── Internal API used by PipeConnection ────────────────────────────────

        /// <summary>
        /// Removes a closed connection from the tracking set.
        /// Called from <see cref="PipeConnection"/> after its read loop exits.
        /// </summary>
        internal void RemoveConnection(PipeConnection connection)
        {
            _connections.TryRemove(connection);
        }

        // ── Accept loop ────────────────────────────────────────────────────────

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            // The listener promise is already completed at this point (ContinueWith guard).
            var listener = _associationListenerPromise.Task.Result;

            _log.Debug("Pipe transport: accept loop started.");

            while (!ct.IsCancellationRequested)
            {
                Socket clientSocket;
                try
                {
                    clientSocket = await _serverSocket!.AcceptAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break; // Normal shutdown
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        _log.Warning("Pipe transport: accept loop error [{0}]", ex.Message);
                    break;
                }

                // Handle each inbound connection independently — never block the accept loop.
                _ = HandleInboundAsync(clientSocket, listener, ct);
            }

            _log.Debug("Pipe transport: accept loop stopped.");
        }

        private async Task HandleInboundAsync(
            Socket clientSocket,
            IAssociationEventListener listener,
            CancellationToken ct = default)
        {
            try
            {
                var remoteEp = (IPEndPoint)clientSocket.RemoteEndPoint!;

                // CopilotNotes: MapSocketToAddress is a static helper on DotNettyTransport.
                // Since we're in the same assembly we can call it here directly.
                // In a future standalone Akka.Remote.Transport.Pipelines project this would
                // need to be copied or the helper promoted to a shared utility class.
                var remoteAddress = DotNettyTransport.MapSocketToAddress(
                    remoteEp,
                    schemeIdentifier: SchemeIdentifier,
                    systemName:       System.Name);

                if (remoteAddress is null)
                {
                    _log.Warning(
                        "Pipe transport: could not map remote endpoint [{0}] to an Akka Address. Dropping.",
                        remoteEp);
                    clientSocket.Dispose();
                    return;
                }

                var host   = remoteEp.Address.ToString();
                var stream = await BuildStreamAsync(clientSocket, host, isServer: true, ct).ConfigureAwait(false);

                var handle = new PipeAssociationHandle(_localAddress!, remoteAddress);
                var conn   = new PipeConnection(
                    clientSocket, stream, handle, this, _log, _settings.WriteChannelCapacity);

                _connections.TryAdd(conn);
                conn.Start();

                listener.Notify(new InboundAssociation(handle));
                _log.Debug("Pipe transport: inbound connection from [{0}]", remoteAddress);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Pipe transport: error setting up inbound connection from [{0}]",
                    clientSocket.RemoteEndPoint);
                clientSocket.Dispose();
            }
        }

        // ── TLS helper ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a plain <see cref="System.Net.Sockets.NetworkStream"/> when TLS is disabled,
        /// or a fully authenticated <see cref="SslStream"/> when enabled.
        /// </summary>
        private async Task<System.IO.Stream> BuildStreamAsync(
            Socket socket,
            string host,
            bool isServer,
            CancellationToken ct = default)
        {
            var networkStream = new NetworkStream(socket, ownsSocket: false);
            if (!_settings.EnableSsl)
                return networkStream;

            var ssl = new SslStream(networkStream, leaveInnerStreamOpen: false);
            try
            {
                if (isServer)
                {
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate              = _settings.Ssl.Certificate,
                        ClientCertificateRequired      = _settings.Ssl.RequireMutualAuthentication,
                        RemoteCertificateValidationCallback = BuildValidationCallback(host),
                    }, ct).ConfigureAwait(false);
                }
                else
                {
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost         = host,
                        ClientCertificates = _settings.Ssl.Certificate is not null
                            ? new X509CertificateCollection { _settings.Ssl.Certificate }
                            : null,
                        RemoteCertificateValidationCallback = BuildValidationCallback(host),
                    }, ct).ConfigureAwait(false);
                }
            }
            catch (AuthenticationException ex)
            {
                await ssl.DisposeAsync().ConfigureAwait(false);
                throw new InvalidAssociationException(
                    $"TLS handshake with [{host}] failed: {ex.Message}", ex);
            }

            return ssl;
        }

        /// <summary>
        /// Builds a <see cref="RemoteCertificateValidationCallback"/> from the current SSL settings.
        /// Honours <see cref="DotNetty.SslSettings.SuppressValidation"/> and
        /// <see cref="DotNetty.SslSettings.CustomValidator"/>.
        /// </summary>
        private RemoteCertificateValidationCallback BuildValidationCallback(string host)
        {
            var ssl = _settings.Ssl;

            if (ssl.SuppressValidation)
                return (_, _, _, _) => true;

            return (_, cert, chain, errors) =>
            {
                if (ssl.CustomValidator is not null)
                {
#if NET10_0_OR_GREATER
                    var cert2 = cert as X509Certificate2
                                ?? (cert is not null
                                    ? X509CertificateLoader.LoadCertificate(cert.GetRawCertData())
                                    : null);
#else
                    var cert2 = cert as X509Certificate2
                                ?? (cert is not null ? new X509Certificate2(cert) : null);
#endif
                    return ssl.CustomValidator(cert2, chain, host, errors, _log);
                }

                if (errors == SslPolicyErrors.None)
                    return true;

                _log.Warning("Pipe transport: TLS cert validation failed for [{0}]: {1}", host, errors);
                return false;
            };
        }

        // ── Socket factory helpers ─────────────────────────────────────────────

        private Socket CreateServerSocket()
        {
            var family = _settings.DnsUseIpv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            var socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            ApplyCommonSocketOptions(socket);
            return socket;
        }

        private Socket CreateClientSocket()
        {
            var family = _settings.DnsUseIpv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            var socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
            ApplyCommonSocketOptions(socket);
            return socket;
        }

        private void ApplyCommonSocketOptions(Socket socket)
        {
            if (_settings.TcpNoDelay)
                socket.NoDelay = true;

            if (_settings.TcpKeepAlive)
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            if (_settings.ReceiveBufferSize > 0)
                socket.ReceiveBufferSize = _settings.ReceiveBufferSize;

            if (_settings.SendBufferSize > 0)
                socket.SendBufferSize = _settings.SendBufferSize;
        }

        // ── DNS resolution ─────────────────────────────────────────────────────

        private async Task<IPEndPoint> ResolveEndpointAsync(string hostname, int port)
        {
            // Bind-all shortcut
            if (string.IsNullOrWhiteSpace(hostname)
                || hostname == IPAddress.Any.ToString()
                || hostname == IPAddress.IPv6Any.ToString())
            {
                return new IPEndPoint(IPAddress.Any, port);
            }

            // Already an IP address
            if (IPAddress.TryParse(hostname, out var ip))
                return new IPEndPoint(ip, port);

            // DNS resolution
            var addresses = await Dns.GetHostAddressesAsync(hostname).ConfigureAwait(false);

            // Filter link-local (APIPA) addresses to avoid connecting to unreachable NICs.
            var filtered = DotNettyTransport.FilterLinkLocalAddresses(addresses).ToArray();
            var candidates = filtered.Length > 0 ? filtered : addresses;

            var preferredFamily = _settings.DnsUseIpv6
                ? AddressFamily.InterNetworkV6
                : AddressFamily.InterNetwork;

            var found = Array.Find(candidates, a => a.AddressFamily == preferredFamily)
                        ?? candidates.FirstOrDefault();

            if (found is null)
                throw new InvalidOperationException(
                    $"Could not resolve hostname [{hostname}] to any IP address.");

            return new IPEndPoint(found, port);
        }
    }
}








