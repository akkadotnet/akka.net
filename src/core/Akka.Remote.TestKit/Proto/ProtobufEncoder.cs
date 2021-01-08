//-----------------------------------------------------------------------
// <copyright file="ProtobufEncoder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Common.Internal.Logging;
using DotNetty.Transport.Channels;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Akka.Remote.TestKit.Proto
{
    /// <summary>
    /// Encodes a generic object into a <see cref="IByteBuffer"/> using Google protobufs
    /// </summary>
    internal sealed class ProtobufEncoder : MessageToMessageEncoder<IMessage>
    {
        private readonly ILogger _logger = InternalLoggerFactory.DefaultFactory.CreateLogger<ProtobufEncoder>();

        protected override void Encode(IChannelHandlerContext context, IMessage message, List<object> output)
        {
            _logger.LogDebug("[{0} --> {1}] Encoding {2} into Protobuf", context.Channel.LocalAddress, context.Channel.RemoteAddress, message);
            IByteBuffer buffer = null;

            try
            {
                int size = message.CalculateSize();
                if (size == 0)
                {
                    return;
                }
                buffer = Unpooled.WrappedBuffer(message.ToByteArray());
                output.Add(buffer);
                buffer = null;
            }
            catch (Exception exception)
            {
                throw new CodecException(exception);
            }
            finally
            {
                buffer?.Release();
            }
        }
    }
}
