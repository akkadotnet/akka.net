//-----------------------------------------------------------------------
// <copyright file="TcpStreamTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Transport.DotNetty;
using Akka.Streams;
using Akka.Streams.Dsl;
using Google.Protobuf;
using DotNettyByteOrder = DotNetty.Buffers.ByteOrder;
using StreamTcp = Akka.Streams.Dsl.Tcp;

namespace Akka.Remote.Transport.Streams
{
    /// <summary>
    /// INTERNAL API.
    /// Experimental TCP transport backed by Akka.Streams TCP while preserving the classic Remote
    /// length-frame format consumed by <see cref="AkkaProtocolTransport" />.
    /// </summary>
    internal sealed class TcpStreamTransport : Transport
    {
        private readonly TaskCompletionSource<IAssociationEventListener> _associationListenerPromise = new();
        private readonly ConcurrentDictionary<StreamAssociationHandle, StreamAssociationHandle> _connections = new();
        private readonly ILoggingAdapter _log;
        private readonly ActorMaterializer _materializer;
        private readonly int _writeBufferSize;

        private StreamTcp.ServerBinding? _binding;
        private volatile bool _shutdown;

        public TcpStreamTransport(ActorSystem system, Config config)
        {
            System = system;
            Config = config;
            Settings = DotNettyTransportSettings.Create(config);
            if (Settings.EnableSsl)
                throw new NotSupportedException("TcpStreamTransport does not support SSL yet.");
            if (Settings.BackwardsCompatibilityModeEnabled)
                throw new NotSupportedException("TcpStreamTransport does not support Helios backwards-compatible framing.");

            SchemeIdentifier = Settings.TransportMode.ToString().ToLowerInvariant();
            _log = Logging.GetLogger(system, GetType());
            _materializer = ActorMaterializer.Create(system);
            _writeBufferSize = Math.Max(1, config.HasPath("stream-write-buffer-size")
                ? config.GetInt("stream-write-buffer-size")
                : 65536);
        }

        public DotNettyTransportSettings Settings { get; }

        public override long MaximumPayloadBytes => Settings.MaxFrameSize;

        public override bool IsResponsibleFor(Address remote) => true;

        public override async Task<(Address, TaskCompletionSource<IAssociationEventListener>)> Listen()
        {
            try
            {
                var binding = await System.TcpStream()
                    .Bind(Settings.Hostname, Settings.Port, Settings.Backlog, halfClose: false)
                    .ToMaterialized(Sink.ForEach<StreamTcp.IncomingConnection>(HandleIncomingConnection), Keep.Left)
                    .Run(_materializer)
                    .ConfigureAwait(false);

                _binding = binding;
                var localEndpoint = (IPEndPoint)binding.LocalAddress;
                var address = DotNettyTransport.MapSocketToAddress(
                    localEndpoint,
                    SchemeIdentifier,
                    System.Name,
                    Settings.PublicHostname,
                    Settings.PublicPort);

                return (address ?? throw new ConfigurationException($"Unknown local address type [{binding.LocalAddress}]"), _associationListenerPromise);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to bind stream TCP transport to [{0}:{1}]", Settings.Hostname, Settings.Port);
                await Shutdown().ConfigureAwait(false);
                throw;
            }
        }

        public override async Task<AssociationHandle> Associate(Address remoteAddress)
        {
            if (_binding == null)
                throw new InvalidAssociationException("Transport is not bound or not open");

            var remoteEndpoint = DotNettyTransport.AddressToSocketAddress(remoteAddress);
            StreamAssociationHandle handle = null;
            var inboundBridge = new DeferredInboundBridge();

            var sink = DecodeFrames().ToMaterialized(Sink.ForEach<ReadOnlySequence<byte>>(inboundBridge.NotifyInbound), Keep.Right);
            var source = CreateOutboundSource();
            var tcpFlow = System.TcpStream().OutgoingConnection(
                remoteEndpoint,
                connectionTimeout: Settings.ConnectTimeout,
                halfClose: false);

            var ((writer, connectionTask), inboundDone) = source
                .ViaMaterialized(tcpFlow, Keep.Both)
                .ToMaterialized(sink, Keep.Both)
                .Run(_materializer);

            try
            {
                var connection = await connectionTask.ConfigureAwait(false);
                var localAddress = DotNettyTransport.MapSocketToAddress(
                    (IPEndPoint)connection.LocalAddress,
                    SchemeIdentifier,
                    System.Name,
                    Settings.Hostname);
                handle = new StreamAssociationHandle(
                    localAddress ?? throw new ConfigurationException($"Unknown local address type [{connection.LocalAddress}]"),
                    remoteAddress,
                    writer,
                    Settings.MaxFrameSize,
                    Settings.ByteOrder,
                    RemoveConnection,
                    _log);
                TrackConnection(handle, inboundDone);
                inboundBridge.SetHandle(handle);
                return handle;
            }
            catch (Exception ex)
            {
                writer.Tell(new Status.Failure(ex));
                throw;
            }
        }

        public override async Task<bool> Shutdown()
        {
            _shutdown = true;

            foreach (var handle in _connections.Keys.ToArray())
#pragma warning disable CS0618 // Intentionally implementing classic transport shutdown contract.
                handle.Disassociate();
#pragma warning restore CS0618

            if (_binding.HasValue)
                await _binding.Value.Unbind().ConfigureAwait(false);

            _materializer.Shutdown();
            return true;
        }

        private void HandleIncomingConnection(StreamTcp.IncomingConnection connection)
        {
            _associationListenerPromise.Task.ContinueWith(listenerTask =>
            {
                if (listenerTask.IsFaulted || listenerTask.IsCanceled || _shutdown)
                    return;

                StreamAssociationHandle handle = null;
                var inboundBridge = new DeferredInboundBridge();
                var sink = DecodeFrames().ToMaterialized(Sink.ForEach<ReadOnlySequence<byte>>(inboundBridge.NotifyInbound), Keep.Right);
                var source = CreateOutboundSource();
                var flow = Flow.FromSinkAndSource(sink, source, Keep.Both);
                var (inboundDone, writer) = connection.HandleWith(flow, _materializer);

                var localAddress = DotNettyTransport.MapSocketToAddress(
                    (IPEndPoint)connection.LocalAddress,
                    SchemeIdentifier,
                    System.Name,
                    Settings.Hostname);
                var remoteAddress = DotNettyTransport.MapSocketToAddress(
                    (IPEndPoint)connection.RemoteAddress,
                    SchemeIdentifier,
                    System.Name);

                if (localAddress == null || remoteAddress == null)
                {
                    writer.Tell(new Status.Success(NotUsed.Instance));
                    return;
                }

                handle = new StreamAssociationHandle(localAddress, remoteAddress, writer, Settings.MaxFrameSize, Settings.ByteOrder, RemoveConnection, _log);
                TrackConnection(handle, inboundDone);
                inboundBridge.SetHandle(handle);
                listenerTask.Result.Notify(new InboundAssociation(handle));
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private Source<ReadOnlySequence<byte>, IActorRef> CreateOutboundSource()
        {
            return Source.ActorRef<ReadOnlySequence<byte>>(_writeBufferSize, OverflowStrategy.Fail);
        }

        private Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> DecodeFrames()
        {
            return RemoteTcpFraming.Decoder(Settings.MaxFrameSize, Settings.ByteOrder);
        }

        private void TrackConnection(StreamAssociationHandle handle, Task<Done> inboundDone)
        {
            _connections.TryAdd(handle, handle);
            inboundDone.ContinueWith(task =>
            {
                RemoveConnection(handle);
                if (task.IsFaulted)
                    handle.NotifyError(task.Exception?.GetBaseException() ?? task.Exception, "Stream TCP connection failed");
                handle.NotifyDisassociated(DisassociateInfo.Unknown);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private void RemoveConnection(StreamAssociationHandle handle)
        {
            _connections.TryRemove(handle, out _);
        }

        private static ByteString ToByteString(ReadOnlySequence<byte> payload)
        {
            return payload.IsSingleSegment
                ? ByteString.CopyFrom(payload.FirstSpan)
                : ByteString.CopyFrom(payload.ToArray());
        }

        private sealed class DeferredInboundBridge
        {
            private readonly object _gate = new();
            private readonly System.Collections.Generic.Queue<ByteString> _pending = new();
            private StreamAssociationHandle _handle;

            public void NotifyInbound(ReadOnlySequence<byte> payload)
            {
                var handle = Volatile.Read(ref _handle);
                if (handle != null)
                {
                    handle.NotifyInbound(payload);
                    return;
                }

                var bytes = ToByteString(payload);
                lock (_gate)
                {
                    handle = _handle;
                    if (handle != null)
                    {
                        handle.NotifyInbound(bytes);
                    }
                    else
                    {
                        _pending.Enqueue(bytes);
                    }
                }
            }

            public void SetHandle(StreamAssociationHandle handle)
            {
                lock (_gate)
                {
                    while (_pending.Count > 0)
                        handle.NotifyInbound(_pending.Dequeue());

                    Volatile.Write(ref _handle, handle);
                }
            }
        }

        private sealed class StreamAssociationHandle : AssociationHandle
        {
            private readonly Action<StreamAssociationHandle> _remove;
            private readonly ILoggingAdapter _log;
            private readonly int _maxFrameSize;
            private readonly DotNettyByteOrder _byteOrder;
            private readonly object _gate = new();
            private readonly System.Collections.Generic.Queue<IHandleEvent> _pending = new();
            private volatile bool _closed;
            private IHandleEventListener _listener;

            public StreamAssociationHandle(
                Address localAddress,
                Address remoteAddress,
                IActorRef writer,
                int maxFrameSize,
                DotNettyByteOrder byteOrder,
                Action<StreamAssociationHandle> remove,
                ILoggingAdapter log) : base(localAddress, remoteAddress)
            {
                Writer = writer;
                _maxFrameSize = maxFrameSize;
                _byteOrder = byteOrder;
                _remove = remove;
                _log = log;
                ReadHandlerSource.Task.ContinueWith(task =>
                {
                    if (task.IsFaulted || task.IsCanceled)
                        return;

                    lock (_gate)
                    {
                        var listener = task.Result;
                        while (_pending.Count > 0)
                            listener.Notify(_pending.Dequeue());

                        Volatile.Write(ref _listener, listener);
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);
            }

            public IActorRef Writer { get; }

            public override bool Write(ByteString payload)
            {
                if (_closed)
                    return false;

                try
                {
                    Writer.Tell(RemoteTcpFraming.Encode(new ReadOnlySequence<byte>(payload.Memory), _maxFrameSize, _byteOrder));
                }
                catch (Exception ex)
                {
                    Writer.Tell(new Status.Failure(ex));
                    return false;
                }

                return true;
            }

            public override void Disassociate()
            {
                if (_closed)
                    return;

                _closed = true;
                Writer.Tell(new Status.Success(NotUsed.Instance));
                _remove(this);
            }

            public void NotifyInbound(ReadOnlySequence<byte> payload)
            {
                Notify(new InboundSequencePayload(payload));
            }

            public void NotifyInbound(ByteString payload)
            {
                Notify(new InboundPayload(payload));
            }

            public void NotifyDisassociated(DisassociateInfo info)
            {
                _closed = true;
                Notify(new Disassociated(info));
            }

            public void NotifyError(Exception cause, string message)
            {
                if (cause != null)
                    _log.Debug(cause, "{0} between local [{1}] and remote [{2}]", message, LocalAddress, RemoteAddress);
                Notify(new UnderlyingTransportError(cause, message));
            }

            private void Notify(IHandleEvent ev)
            {
                var listener = Volatile.Read(ref _listener);
                if (listener != null)
                {
                    listener.Notify(ev);
                    return;
                }

                lock (_gate)
                {
                    listener = _listener;
                    if (listener != null)
                    {
                        listener.Notify(ev);
                    }
                    else
                    {
                        _pending.Enqueue(ev);
                    }
                }
            }
        }
    }
}
