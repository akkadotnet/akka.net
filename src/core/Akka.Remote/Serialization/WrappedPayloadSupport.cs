//-----------------------------------------------------------------------
// <copyright file="WrappedPayloadSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Buffers;
using Akka.Actor;
using Akka.Serialization;
using Google.Protobuf;

namespace Akka.Remote.Serialization
{
    internal sealed class WrappedPayloadSupport
    {
        private readonly ExtendedActorSystem _system;

        public WrappedPayloadSupport(ExtendedActorSystem system)
        {
            _system = system;
        }

        public Proto.Msg.Payload PayloadToProto(object payload)
        {
            if (payload == null) // TODO: handle null messages
                return new Proto.Msg.Payload();

            var payloadProto = new Proto.Msg.Payload();
            var serializer = _system.Serialization.FindSerializerV2For(payload);
            var writer = CreateWriter(serializer, payload);
            serializer.Serialize(payload, writer);

            payloadProto.Message = ByteString.CopyFrom(writer.WrittenSpan);
            payloadProto.SerializerId = serializer.Identifier;

            // get manifest
            var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, payload);
            if (!string.IsNullOrEmpty(manifest))
            {
                payloadProto.MessageManifest = ByteString.CopyFromUtf8(manifest);
            }

            return payloadProto;
        }

        public object PayloadFrom(Proto.Msg.Payload payload)
        {
            var manifest = !payload.MessageManifest.IsEmpty
                ? payload.MessageManifest.ToStringUtf8()
                : string.Empty;

            return _system.Serialization.Deserialize(
                new ReadOnlySequence<byte>(payload.Message.Memory),
                payload.SerializerId,
                manifest);
        }

        private static ArrayBufferWriter<byte> CreateWriter(SerializerV2 serializer, object payload)
        {
            var sizeHint = serializer.SizeHint(payload);
            return sizeHint > 0
                ? new ArrayBufferWriter<byte>(sizeHint)
                : new ArrayBufferWriter<byte>();
        }
    }
}
