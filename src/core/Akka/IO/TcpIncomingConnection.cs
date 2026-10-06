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
using System.Net.Sockets;
using Akka.Actor;

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
        private ITransportConnection? _preparedTransport;
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
            _preparedTransport = CreateTcpTransport(stream);
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
            if (_preparedTransport is { } preparedTransport)
            {
                _preparedTransport = null;
                return preparedTransport;
            }

            return CreateTcpTransport(existingStream: null);
        }

        private ITransportConnection CreateTcpTransport(Stream? existingStream)
        {
            var pipeBufferSize = ResolvePipeBufferSize(Settings, _options);
            var inputPipeOptions = new PipeOptions(
                pauseWriterThreshold: pipeBufferSize * 2,
                resumeWriterThreshold: pipeBufferSize,
                minimumSegmentSize: Settings.MaxFrameSizeBytes,
                useSynchronizationContext: false);

            return TcpTransportConnection.CreateForIncoming(Socket, existingStream, inputPipeOptions,
                ResolveOutputPipeOptions(Settings, _options), _tlsSettings, Log);
        }

        protected override void PreStart()
        {
            InitializeTransport(_bindHandler, _options);
        }

    }
}
