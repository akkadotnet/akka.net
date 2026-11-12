// -----------------------------------------------------------------------
// <copyright file="ArteryQuicTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Versioning;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Util;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// Experimental byte adapter. Association, envelope and delivery state remain in ArteryRemoting.
    /// One connection is owned per outbound peer direction, with separate control/large streams and bounded ordinary stream groups.
    /// The fixed admission limits deliberately bound this first prototype's resource footprint.
    /// </summary>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("osx")]
    internal sealed class ArteryQuicTransport
    {
        internal static readonly SslApplicationProtocol ApplicationProtocol = new("akka-artery-quic-prototype-v1");
        private const int MaxConnections = 64;
        private const long ErrorCode = 1;
        private readonly ArteryTlsSettings _tls;
        private readonly TimeSpan _handshakeTimeout;
        private readonly int _ordinaryStreams;
        private readonly int _maximumIncomingStreams;
        private readonly int _maximumIncomingOrdinaryStreams;
        private readonly ILoggingAdapter _log;
        private readonly ArteryQuicIoCounters? _ioCounters;
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentDictionary<EndPoint, Peer> _outbound = new();
        private readonly ConcurrentDictionary<long, Task> _work = new();
        private readonly SemaphoreSlim _incomingSlots = new(MaxConnections);
        private readonly object _outboundGate = new();
        private QuicListener? _listener;
        private long _workId;

        internal ArteryQuicTransport(ArteryTlsSettings? tls, TimeSpan handshakeTimeout, ILoggingAdapter log,
            int ordinaryStreams = 1, int maximumInboundOrdinaryStreams = 8, bool enableIoStatistics = false)
        {
            if (tls is null)
                throw new ConfigurationException("Artery QUIC requires explicit ArteryTlsSetup credentials and peer policy.");
            if (tls.Protocols != SslProtocols.None && (tls.Protocols & SslProtocols.Tls13) == 0)
                throw new ConfigurationException("Artery QUIC requires TLS 1.3; the configured TLS protocols exclude it.");
            if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
                throw new PlatformNotSupportedException("Artery QUIC requires native MsQuic and TLS 1.3 support (libmsquic on Linux).");
            if (ordinaryStreams is < 1 or > ArterySettings.MaximumQuicOrdinaryStreams ||
                maximumInboundOrdinaryStreams is < 1 or > ArterySettings.MaximumQuicOrdinaryStreams)
                throw new ArgumentOutOfRangeException(nameof(ordinaryStreams), "QUIC ordinary stream limits must be between 1 and 64.");
            _ioCounters = enableIoStatistics ? new ArteryQuicIoCounters() : null;
            _ordinaryStreams = ordinaryStreams;
            _maximumIncomingOrdinaryStreams = maximumInboundOrdinaryStreams;
            _maximumIncomingStreams = maximumInboundOrdinaryStreams + 2;
            _tls = tls;
            _handshakeTimeout = handshakeTimeout;
            _log = log;
        }

        internal ArteryQuicIoSnapshot IoSnapshot => _ioCounters?.Snapshot ?? default;

        internal void RecordWrite(ArteryStreamId role, int bytes)
        {
            if (role == ArteryStreamId.Ordinary && bytes > 0)
                _ioCounters?.Write(bytes);
        }

        internal async Task<EndPoint> BindAsync(string hostname, int port, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            var addresses = await Dns.GetHostAddressesAsync(hostname, linked.Token).ConfigureAwait(false);
            var address = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?? addresses.First();
            var listener = await QuicListener.ListenAsync(new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(address, port),
                ListenBacklog = MaxConnections,
                ApplicationProtocols = new List<SslApplicationProtocol> { ApplicationProtocol },
                ConnectionOptionsCallback = (connection, _, _) =>
                {
                    var authentication = _tls.ServerSettings.CreateAuthenticationOptions(connection.RemoteEndPoint.ToString(), _log);
                    authentication.ApplicationProtocols = new List<SslApplicationProtocol> { ApplicationProtocol };
                    authentication.EnabledSslProtocols = SslProtocols.Tls13;
                    var validate = authentication.RemoteCertificateValidationCallback;
                    if (validate is not null)
                    {
                        // QuicConnection compares CLIENT certificates against the client's SNI
                        // (the SERVER name). SslStream correctly has no implicit client-name
                        // target. Normalize only that QUIC artifact to preserve server-role TLS
                        // policy semantics. Explicit ValidateHostname(expectedName) still checks
                        // the certificate directly; outbound name errors are never normalized.
                        authentication.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                            validate(sender, certificate, chain, errors & ~SslPolicyErrors.RemoteCertificateNameMismatch);
                    }
                    return ValueTask.FromResult(new QuicServerConnectionOptions
                    {
                        ServerAuthenticationOptions = authentication,
                        DefaultCloseErrorCode = ErrorCode,
                        DefaultStreamErrorCode = ErrorCode,
                        HandshakeTimeout = _tls.HandshakeTimeout,
                        IdleTimeout = TimeSpan.FromMinutes(1),
                        MaxInboundBidirectionalStreams = 0,
                        MaxInboundUnidirectionalStreams = _maximumIncomingStreams
                    });
                }
            }, linked.Token).ConfigureAwait(false);
            lock (_outboundGate)
            {
                if (!_stop.IsCancellationRequested)
                {
                    _listener = listener;
                    return listener.LocalEndPoint;
                }
            }
            await listener.DisposeAsync().ConfigureAwait(false);
            throw new OperationCanceledException(_stop.Token);
        }

        // Called only after Artery has published its bound address and inbound context.
        // The returned task must include downstream processing, including any detached lanes.
        // Raw source termination alone can precede both dispatch and a downstream failure.
        internal void StartAccepting(Func<Source<ReadOnlySequence<byte>, NotUsed>, EndPoint, Action, Task> accept) =>
            Track(AcceptConnectionsAsync(accept));

        internal Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, Task<Done>> Outgoing(EndPoint endpoint, ArteryStreamId role, int ordinaryStream = 0) =>
            Flow.FromGraph(new ArteryQuicWriteStage(this, endpoint, role, ordinaryStream));

        internal IReadOnlyDictionary<string, int> OrdinaryStreamCounts() => _outbound
            .ToDictionary(pair => pair.Key.ToString()!, pair => pair.Value.OrdinaryStreamCount);

        internal Task OrdinaryStreamsReadyAsync(CancellationToken cancellationToken) =>
            Task.WhenAll(_outbound.Values.Select(peer => peer.OrdinaryStreamsReady)).WaitAsync(cancellationToken);

        internal async Task<Channel> OpenAsync(EndPoint endpoint, ArteryStreamId role, CancellationToken cancellationToken, int ordinaryStream = 0)
        {
            if (ordinaryStream < 0 || (role == ArteryStreamId.Ordinary ? ordinaryStream >= _ordinaryStreams : ordinaryStream != 0))
                throw new ArgumentOutOfRangeException(nameof(ordinaryStream));
            Peer peer;
            lock (_outboundGate)
            {
                _stop.Token.ThrowIfCancellationRequested();
                if (!_outbound.TryGetValue(endpoint, out peer!))
                {
                    if (_outbound.Count >= MaxConnections)
                        throw new RemoteTransportException("Artery QUIC outbound connection limit reached.");
                    peer = new Peer(this, endpoint);
                    _outbound[endpoint] = peer;
                }
            }

            try
            {
                return await peer.OpenAsync(role, ordinaryStream, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                peer.Retire();
                throw;
            }
        }

        internal void Track(Task task)
        {
            var id = Interlocked.Increment(ref _workId);
            _work[id] = task;
            _ = task.ContinueWith(completed =>
            {
                if (completed.IsFaulted)
                    _log.Debug("Artery QUIC I/O ended: {0}", completed.Exception!.GetBaseException().Message);
                _work.TryRemove(id, out _);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private async Task AcceptConnectionsAsync(Func<Source<ReadOnlySequence<byte>, NotUsed>, EndPoint, Action, Task> accept)
        {
            while (!_stop.IsCancellationRequested)
            {
                await _incomingSlots.WaitAsync(_stop.Token).ConfigureAwait(false);
                QuicConnection connection;
                try
                {
                    connection = await _listener!.AcceptConnectionAsync(_stop.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!_stop.IsCancellationRequested &&
                    ex is AuthenticationException or QuicException)
                {
                    _incomingSlots.Release();
                    _log.Debug("Artery QUIC rejected incoming connection: {0}", ex.Message);
                    continue;
                }
                catch
                {
                    _incomingSlots.Release();
                    throw;
                }
                Track(ReceiveConnectionAsync(connection, accept));
            }
        }

        private async Task ReceiveConnectionAsync(QuicConnection connection,
            Func<Source<ReadOnlySequence<byte>, NotUsed>, EndPoint, Action, Task> accept)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            var streams = new List<QuicStream>();
            var roles = new HashSet<ArteryStreamId>();
            var ordinaryStreams = 0;
            var admissions = new List<Task>();
            var readers = new ConcurrentBag<Task>();
            var admitted = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                admissions.Add(EnforceHandshakeDeadlineAsync());
                // Accept/read every preamble concurrently: an idle ordinary stream must not hold
                // up control admission. Ordinary channels have a separate finite admission cap.
                for (var i = 0; i < _maximumIncomingStreams; i++)
                {
                    var stream = await connection.AcceptInboundStreamAsync(lifetime.Token).ConfigureAwait(false);
                    streams.Add(stream);
                    admissions.Add(AdmitAsync(stream));
                }
                await Task.WhenAll(admissions).ConfigureAwait(false);
                // Stay alive after all allowed streams have been admitted; connection shutdown or
                // an extra stream is observed here. Stream credit is not an admission policy.
                await using var unexpected = await connection.AcceptInboundStreamAsync(lifetime.Token).ConfigureAwait(false);
                throw new ArteryFramingException("Artery QUIC received more than the configured channel stream limit.");

                async Task AdmitAsync(QuicStream stream)
                {
                    try
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        deadline.CancelAfter(_tls.HandshakeTimeout);
                        var preamble = new byte[ArteryConnectionHeader.Length];
                        await stream.ReadExactlyAsync(preamble, deadline.Token).ConfigureAwait(false);
                        ArteryConnectionHeader.TryParse(new ReadOnlySequence<byte>(preamble), out var role, out _);
                        lock (roles)
                        {
                            if (role == ArteryStreamId.Ordinary)
                            {
                                if (++ordinaryStreams > _maximumIncomingOrdinaryStreams)
                                    throw new ArteryFramingException("Artery QUIC ordinary channel limit exceeded.");
                            }
                            else if (!roles.Add(role))
                                throw new ArteryFramingException($"Duplicate Artery QUIC channel [{role}].");
                        }
                        var reachedEof = false;
                        var source = Source.UnfoldResourceAsync<ReadOnlySequence<byte>, (QuicStream Stream, byte[] Buffer)>(
                            () => Task.FromResult((stream, GC.AllocateUninitializedArray<byte>(16 * 1024, pinned: true))),
                            async resource =>
                            {
                                var count = await resource.Stream.ReadAsync(resource.Buffer, lifetime.Token).ConfigureAwait(false);
                                if (count == 0)
                                    reachedEof = true;
                                if (role == ArteryStreamId.Ordinary && count > 0)
                                    _ioCounters?.Read(count);
                                // Keep one bounded scratch buffer per reader. Downstream frames can
                                // retain chunks across reads, so publish a separately owned array of
                                // only the received bytes; the scratch never escapes or returns to a pool.
                                return count == 0 ? Option<ReadOnlySequence<byte>>.None
                                    : Option<ReadOnlySequence<byte>>.Create(new ReadOnlySequence<byte>(resource.Buffer.AsSpan(0, count).ToArray()));
                            },
                            // The connection is the sole native disposal owner. A cancelled
                            // reader signals retirement through its processing task below.
                            _ => Task.FromResult(Done.Instance))
                            .Prepend(Source.Single(new ReadOnlySequence<byte>(preamble)))
                            .WatchTermination((_, done) =>
                            {
                                // A source failure/cancellation must retire promptly even when
                                // a downstream serializer or lane consumer is still blocked.
                                Track(done);
                                readers.Add(ObserveReadAsync(done));
                                return NotUsed.Instance;
                            });
                        var processing = accept(source, connection.RemoteEndPoint, () => admitted.TrySetResult(Done.Instance));
                        // Processing can outlive native teardown (e.g. a blocked serializer).
                        // Observe it, but cancel the connection's observer with its lifetime so
                        // native disposal never waits indefinitely for application code.
                        Track(processing);
                        readers.Add(ObserveReadAsync(processing));

                        async Task ObserveReadAsync(Task completion)
                        {
                            try
                            {
                                await completion.WaitAsync(lifetime.Token).ConfigureAwait(false);
                                // A healthy FIN may leave sibling channels flushing. A cancelled
                                // sink that never consumed EOF must instead retire this connection.
                                if (!reachedEof)
                                    lifetime.Cancel();
                            }
                            catch
                            {
                                lifetime.Cancel();
                                throw;
                            }
                        }
                    }
                    catch
                    {
                        lifetime.Cancel();
                        throw;
                    }
                }

                async Task EnforceHandshakeDeadlineAsync()
                {
                    try
                    {
                        await admitted.Task.WaitAsync(_handshakeTimeout, lifetime.Token).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        _log.Debug("Artery QUIC peer did not complete its Artery handshake before the admission deadline.");
                        lifetime.Cancel();
                    }
                }

            }
            finally
            {
                lifetime.Cancel();
                try
                {
                    await CloseConnectionAsync(connection).ConfigureAwait(false);
                    foreach (var stream in streams)
                        await SettleAsync(stream.DisposeAsync().AsTask()).ConfigureAwait(false);
                    await SettleAsync(Task.WhenAll(admissions)).ConfigureAwait(false);
                    await SettleAsync(Task.WhenAll(readers)).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        _incomingSlots.Release();
                    }
                }
            }
        }

        private async Task SettleAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Debug("Artery QUIC operation settled: {0}", ex.Message);
            }
        }

        private async Task CloseConnectionAsync(QuicConnection connection)
        {
            using var deadline = new CancellationTokenSource(_tls.HandshakeTimeout);
            await SettleAsync(connection.CloseAsync(ErrorCode, deadline.Token).AsTask()).ConfigureAwait(false);
        }

        internal async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            QuicListener? listener;
            Peer[] peers;
            lock (_outboundGate)
            {
                _stop.Cancel();
                listener = _listener;
                peers = _outbound.Values.ToArray();
            }
            foreach (var peer in peers)
                peer.Retire();
            if (listener is not null)
                Track(listener.DisposeAsync().AsTask());
            // The same deadline used for Artery's flush also bounds native teardown. Cleanup
            // remains tracked if the deadline expires, rather than abandoning unobserved tasks.
            while (!_work.IsEmpty)
            {
                var pending = Task.WhenAll(_work.Values.ToArray());
                try
                {
                    await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _log.Debug("Artery QUIC teardown is continuing after the shutdown deadline.");
                    return;
                }
                catch (Exception ex)
                {
                    _log.Debug("Artery QUIC shutdown observed: {0}", ex.Message);
                }
            }
        }

        internal sealed class Channel(Peer peer, QuicStream stream)
        {
            internal QuicStream Stream { get; } = stream;
            internal CancellationToken CancellationToken => peer.CancellationToken;
            internal void Retire() => peer.Retire();
        }

        internal sealed class Peer
        {
            private readonly ArteryQuicTransport _owner;
            private readonly EndPoint _endpoint;
            private readonly CancellationTokenSource _lifetime;
            private readonly CancellationToken _token;
            private readonly Task<QuicConnection> _connection;
            private readonly SemaphoreSlim _opening = new(1);
            private readonly HashSet<(ArteryStreamId Role, int Stream)> _opened = new();
            private int _ordinaryStreamCount;
            private readonly TaskCompletionSource<Done> _ordinaryReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly List<QuicStream> _streams = new();
            private QuicStream? _control;
            private int _retired;

            internal Peer(ArteryQuicTransport owner, EndPoint endpoint)
            {
                _owner = owner;
                _endpoint = endpoint;
                _lifetime = CancellationTokenSource.CreateLinkedTokenSource(owner._stop.Token);
                _token = _lifetime.Token;
                _connection = ConnectAsync();
            }

            internal CancellationToken CancellationToken => _token;
            internal int OrdinaryStreamCount => Volatile.Read(ref _ordinaryStreamCount);
            internal Task OrdinaryStreamsReady => _ordinaryReady.Task;

            private async Task<QuicConnection> ConnectAsync()
            {
                var targetHost = _endpoint is DnsEndPoint dns ? dns.Host : ((IPEndPoint)_endpoint).Address.ToString();
                var authentication = _owner._tls.ClientSettings.CreateAuthenticationOptions(targetHost, targetHost, _owner._log);
                authentication.ApplicationProtocols = new List<SslApplicationProtocol> { ApplicationProtocol };
                authentication.EnabledSslProtocols = SslProtocols.Tls13;
                var connection = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
                {
                    RemoteEndPoint = _endpoint,
                    ClientAuthenticationOptions = authentication,
                    DefaultCloseErrorCode = ErrorCode,
                    DefaultStreamErrorCode = ErrorCode,
                    HandshakeTimeout = _owner._tls.HandshakeTimeout,
                    IdleTimeout = TimeSpan.FromMinutes(1),
                    MaxInboundBidirectionalStreams = 0,
                    MaxInboundUnidirectionalStreams = 0
                }, _token).ConfigureAwait(false);
                try
                {
                    // Reserve the first native stream for control, even when ordinary demand
                    // wins the race to materialize its Artery graph.
                    _control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, _token).ConfigureAwait(false);
                    _streams.Add(_control);
                    return connection;
                }
                catch
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            internal async Task<Channel> OpenAsync(ArteryStreamId role, int ordinaryStream, CancellationToken cancellationToken)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _token);
                // A peer advertising too little stream credit must fail a bounded open, rather
                // than leaving a lane parked forever after the association handshake completed.
                linked.CancelAfter(_owner._tls.HandshakeTimeout);
                var connection = await _connection.WaitAsync(linked.Token).ConfigureAwait(false);
                await _opening.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    if (!_opened.Add((role, ordinaryStream)))
                        throw new RemoteTransportException($"Artery QUIC channel [{role}] is already open.");
                    var stream = role == ArteryStreamId.Control ? _control!
                        : await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, linked.Token).ConfigureAwait(false);
                    if (role != ArteryStreamId.Control)
                        _streams.Add(stream);
                    if (role == ArteryStreamId.Ordinary && Interlocked.Increment(ref _ordinaryStreamCount) == _owner._ordinaryStreams)
                        _ordinaryReady.TrySetResult(Done.Instance);
                    return new Channel(this, stream);
                }
                finally
                {
                    _opening.Release();
                }
            }

            internal void Retire()
            {
                if (Interlocked.Exchange(ref _retired, 1) != 0)
                    return;
                // Conditional removal fences old generation callbacks from deleting a replacement.
                lock (_owner._outboundGate)
                    ((ICollection<KeyValuePair<EndPoint, Peer>>)_owner._outbound).Remove(new(_endpoint, this));
                _lifetime.Cancel();
                _ordinaryReady.TrySetCanceled(_token);
                _owner.Track(DisposeAsync());
            }

            private async Task DisposeAsync()
            {
                try
                {
                    QuicConnection connection;
                    try
                    {
                        connection = await _connection.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _owner._log.Debug("Artery QUIC dial retired: {0}", ex.Message);
                        return;
                    }
                    try
                    {
                        await _owner.CloseConnectionAsync(connection).ConfigureAwait(false);
                        await _opening.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                        try
                        {
                            foreach (var stream in _streams)
                                await _owner.SettleAsync(stream.DisposeAsync().AsTask()).ConfigureAwait(false);
                        }
                        finally
                        {
                            _opening.Release();
                        }
                    }
                    finally
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    _lifetime.Dispose();
                }
            }
        }
    }
}
