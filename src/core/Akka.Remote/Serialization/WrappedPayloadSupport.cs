//-----------------------------------------------------------------------
// <copyright file="WrappedPayloadSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;

namespace Akka.Remote.Serialization
{
    internal class WrappedPayloadSupport
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
            var serializer = _system.Serialization.FindSerializerFor(payload);

            payloadProto.Message = ByteString.CopyFrom(serializer.ToBinary(payload));
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
                payload.Message.ToByteArray(),
                payload.SerializerId,
                manifest);
        }
    }
}
