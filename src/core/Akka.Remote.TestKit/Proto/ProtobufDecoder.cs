//-----------------------------------------------------------------------
// <copyright file="ProtobufDecoder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Common.Internal.Logging;
using DotNetty.Transport.Channels;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Akka.Remote.TestKit.Proto
{
    /// <summary>
    /// Decodes a message from a <see cref="IByteBuffer"/> into a Google protobuff wire format
    /// </summary>
    internal sealed class ProtobufDecoder : MessageToMessageDecoder<IByteBuffer>
    {
        private readonly ILogger _logger = InternalLoggerFactory.DefaultFactory.CreateLogger<ProtobufDecoder>();
        readonly MessageParser _messageParser;

        public ProtobufDecoder(MessageParser messageParser)
        {
            _messageParser = messageParser;
        }

        protected override void Decode(IChannelHandlerContext context, IByteBuffer message, List<object> output)
        {
            _logger.LogDebug("Decoding {0} into Protobuf", message);
            int length = message.ReadableBytes;
            if (length <= 0)
            {
                return;
            }

            Stream inputStream = null;
            try
            {
                CodedInputStream codedInputStream;
                if (message.IoBufferCount == 1)
                {
                    ArraySegment<byte> bytes = message.GetIoBuffer(message.ReaderIndex, length);
                    codedInputStream = new CodedInputStream(bytes.Array, bytes.Offset, length);
                }
                else
                {
                    inputStream = new ReadOnlyByteBufferStream(message, false);
                    codedInputStream = new CodedInputStream(inputStream);
                }

                //
                // Note that we do not dispose the input stream because there is no input stream attached. 
                // Ideally, it should be disposed. BUT if it is disposed, a null reference exception is 
                // thrown because CodedInputStream flag leaveOpen is set to false for direct byte array reads,
                // when it is disposed the input stream is null.
                // 
                // In this case it is ok because the CodedInputStream does not own the byte data.
                //
                IMessage decoded = _messageParser.ParseFrom(codedInputStream);
                if (decoded != null)
                {
                    output.Add(decoded);
                }
            }
            catch (Exception exception)
            {
                throw new CodecException(exception);
            }
            finally
            {
                inputStream?.Dispose();
            }
        }
    }
}
