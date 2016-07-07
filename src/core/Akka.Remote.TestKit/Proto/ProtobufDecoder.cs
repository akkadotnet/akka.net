//-----------------------------------------------------------------------
// <copyright file="ProtobufDecoder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Runtime.InteropServices;
using Google.ProtocolBuffers;
using Helios.Buffers;
using Helios.Channels;
using Helios.Codecs;
using Helios.Logging;
using Helios.Util;

namespace Akka.Remote.TestKit.Proto
{
    /// <summary>
    /// Decodes a message from a <see cref="IByteBuf"/> into a Google protobuff wire format
    /// </summary>
    public class ProtobufDecoder : ByteToMessageDecoder
    {
        private readonly ILogger _logger = LoggingFactory.GetLogger<ProtobufDecoder>();
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

        protected override void Decode(IChannelHandlerContext context, IByteBuf input, List<object> output)
        {
            _logger.Debug("Decoding {0} into Protobuf", input);

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

