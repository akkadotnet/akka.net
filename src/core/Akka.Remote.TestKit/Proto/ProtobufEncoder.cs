﻿//-----------------------------------------------------------------------
// <copyright file="ProtobufEncoder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Common.Internal.Logging;
using DotNetty.Transport.Channels;
using Google.ProtocolBuffers;
using Microsoft.Extensions.Logging;

namespace Akka.Remote.TestKit.Proto
{
    /// <summary>
    /// Encodes a generic object into a <see cref="IByteBuffer"/> using Google protobufs
    /// </summary>
    public class ProtobufEncoder : MessageToMessageEncoder<object>
    {
        private readonly ILogger _logger = InternalLoggerFactory.DefaultFactory.CreateLogger<ProtobufEncoder>();

        protected override void Encode(IChannelHandlerContext context, object message, List<object> output)
        {
            _logger.LogDebug("Encoding {0}", message);
            var messageLite = message as IMessageLite;
            if (messageLite != null)
            {
                var bytes = messageLite.ToByteArray();
                var buffer = context.Allocator.Buffer(bytes.Length);
                buffer.WriteBytes(bytes);
                _logger.LogDebug("Encoded {0}", buffer);
                output.Add(buffer);
                return;
            }

            var builderLite = message as IBuilderLite;
            if (builderLite != null)
            {
                var bytes = builderLite.WeakBuild().ToByteArray();
                var buffer = context.Allocator.Buffer(bytes.Length);
                buffer.WriteBytes(bytes);
                _logger.LogDebug("Encoded {0}", buffer);
                output.Add(buffer);
                return;
            }

            // if the message is neither
            _logger.LogDebug("Encoded {0}", message);
            output.Add(message);
        }
    }
}

