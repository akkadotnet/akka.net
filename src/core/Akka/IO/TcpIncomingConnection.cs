//-----------------------------------------------------------------------
// <copyright file="TcpIncomingConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Event;

#nullable enable

namespace Akka.IO
{
    /// <summary>
    /// An actor handling the connection state machine for an incoming, already connected SocketChannel.
    /// </summary>
    internal sealed class TcpIncomingConnection : TcpConnection
    {
        private readonly IActorRef _bindHandler;
        private readonly IEnumerable<Inet.SocketOption> _options;
        private readonly Stream? _stream;
        private readonly TlsServerSettings? _tlsSettings;

        public TcpIncomingConnection(TcpSettings settings,
                                     Socket socket,
                                     IActorRef bindHandler,
                                     IEnumerable<Inet.SocketOption> options,
                                     bool readThrottling)
            : this(settings, socket, bindHandler, options, readThrottling, stream: null)
        {
        }

        public TcpIncomingConnection(TcpSettings settings,
                                     Socket socket,
                                     IActorRef bindHandler,
                                     IEnumerable<Inet.SocketOption> options,
                                     bool readThrottling,
                                     Stream? stream)
            : base(settings, socket, readThrottling)
        {
            _bindHandler = bindHandler;
            _options = options;
            _stream = stream;
            Context.Watch(bindHandler); // sign death pact
        }

        public TcpIncomingConnection(TcpSettings settings,
                                     Socket socket,
                                     IActorRef bindHandler,
                                     IEnumerable<Inet.SocketOption> options,
                                     bool readThrottling,
                                     TlsServerSettings tlsSettings)
            : base(settings, socket, readThrottling)
        {
            _bindHandler = bindHandler;
            _options = options;
            _tlsSettings = tlsSettings;

            Context.Watch(bindHandler); // sign death pact
        }

        protected override ITransportConnection CreateTransport()
        {
            var pipeBufferSize = ResolvePipeBufferSize(Settings, _options);
            var inputPipeOptions = new PipeOptions(
                pauseWriterThreshold: pipeBufferSize * 2,
                resumeWriterThreshold: pipeBufferSize,
                minimumSegmentSize: Settings.MaxFrameSizeBytes,
                useSynchronizationContext: false);

            if (_stream != null)
            {
                // Use the provided stream (for TLS or testing)
                return new TcpTransportConnection(Socket, _stream, inputPipeOptions, ResolveOutputPipeOptions(Settings, _options));
            }

            if (AuthenticatedStream is { } authenticatedStream)
                return new TcpTransportConnection(Socket, authenticatedStream, inputPipeOptions, ResolveOutputPipeOptions(Settings, _options));

            // Default: plaintext TCP using the socket directly
            return new TcpTransportConnection(Socket, inputPipeOptions, ResolveOutputPipeOptions(Settings, _options));
        }

        protected override void PreStart()
        {
            if (_tlsSettings is null)
            {
                CompleteConnect(_bindHandler, _options);
                return;
            }

            StartTlsAuthentication(_tlsSettings);
        }

        private void StartTlsAuthentication(TlsServerSettings settings)
        {
            try
            {
                var remotePeer = Socket.RemoteEndPoint?.ToString() ?? "unknown remote peer";
                var options = settings.CreateAuthenticationOptions(remotePeer, Log);
                var stream = new SslStream(new NetworkStream(Socket, ownsSocket: false), leaveInnerStreamOpen: false);

                Become(TlsAuthenticating);
                StartTlsHandshake(stream, settings.HandshakeTimeout,
                    cancellationToken => stream.AuthenticateAsServerAsync(options, cancellationToken));
            }
            catch (Exception e)
            {
                Log.Warning(e, "TLS handshake failed for incoming TCP connection from [{0}]", Socket.RemoteEndPoint);
                Context.Stop(Self);
            }
        }

        private void TlsAuthenticating()
        {
            Receive<TlsHandshakeCompleted>(result =>
            {
                if (result.Failure is not null)
                {
                    Log.Warning(result.Failure, "TLS handshake failed for incoming TCP connection from [{0}]", Socket.RemoteEndPoint);
                    Context.Stop(Self);
                    return;
                }

                CompleteConnect(_bindHandler, _options);
            });
        }
    }
}
