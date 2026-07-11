//-----------------------------------------------------------------------
// <copyright file="TcpIncomingConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
        private readonly Stream? _stream;

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

        protected override ITransportConnection CreateTransport()
        {
            var pipeBufferSize = ResolvePipeBufferSize(Settings, _options);
            var inputPipeOptions = new PipeOptions(
                pauseWriterThreshold: pipeBufferSize * 2,
                resumeWriterThreshold: pipeBufferSize,
                useSynchronizationContext: false);

            if (_stream != null)
            {
                // Use the provided stream (for TLS or testing)
                return new TcpTransportConnection(Socket, _stream, inputPipeOptions: inputPipeOptions);
            }

            // Default: plaintext TCP using the socket directly
            return new TcpTransportConnection(Socket, inputPipeOptions: inputPipeOptions);
        }

        protected override void PreStart()
        {
            CompleteConnect(_bindHandler, _options);
        }
    }
}
