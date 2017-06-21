//-----------------------------------------------------------------------
// <copyright file="ProtobufDecoder.cs" company="Akka.NET Project">
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
    /// Decodes a message from a <see cref="IByteBuffer"/> into a Google protobuff wire format
    /// </summary>
    public class ProtobufDecoder : ByteToMessageDecoder
    {
        private readonly ILogger _logger = InternalLoggerFactory.DefaultFactory.CreateLogger<ProtobufDecoder>();
        private readonly IMessageLite _prototype;
        private readonly ExtensionRegistry _extensions;

        public ProtobufDecoder(IMessageLite prototype)
            : this(prototype, null)
        {
        }

        public ProtobufDecoder(IMessageLite prototype, ExtensionRegistry extensions)
        {
            _prototype = prototype;
            _extensions = extensions;
        }

        protected override void Decode(IChannelHandlerContext context, IByteBuffer input, List<object> output)
        {
            _logger.LogDebug("[{0} --> {1}] Decoding {2} into Protobuf", context.Channel.LocalAddress, context.Channel.RemoteAddress, input);

            // short-circuit if there are no readable bytes

            var readable = input.ReadableBytes;
            var buf = new byte[readable];
            input.ReadBytes(buf);
            var bs = ByteString.CopyFrom(buf);
            var result = _extensions == null
                ? _prototype.WeakCreateBuilderForType().WeakMergeFrom(bs).WeakBuild()
                : _prototype.WeakCreateBuilderForType().WeakMergeFrom(bs, _extensions).WeakBuild();
            output.Add(result);
        }
    }
}

