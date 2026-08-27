//-----------------------------------------------------------------------
// <copyright file="RealBenchmarkProtobufSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Serialization;
using Google.Protobuf;

namespace RemotePingPong.RealPayload.Protobuf
{
    /// <summary>
    /// Hand-written <see cref="SerializerWithStringManifest"/> for the benchmark's real-payload,
    /// Protobuf arm -- modeled directly on Akka.Remote's own hand-written protobuf serializers (see
    /// <c>Akka.Remote.Serialization.MiscMessageSerializer</c>): a string manifest, a switch on that
    /// manifest in <see cref="FromBinary(byte[], string)"/>, and <c>ToByteArray()</c>/
    /// <c>Parser.ParseFrom</c> on the way out/in. Only one message type is registered, so the
    /// manifest switch is trivial (a single case), but the shape mirrors the built-ins so it reads
    /// the same way a real contributor's serializer would.
    /// </summary>
    /// <remarks>
    /// SerializerId 987002 is arbitrary but deliberately far outside both Akka's reserved internal
    /// range (0-40, see akka.conf) and the V2 arm's id (987001) to avoid any collision.
    /// </remarks>
    public sealed class RealBenchmarkProtobufSerializer : SerializerWithStringManifest
    {
        public const int IdentifierValue = 987002;
        public const string ManifestName = "real-benchmark-v1";

        public RealBenchmarkProtobufSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => IdentifierValue;

        public override string Manifest(object o)
        {
            return o switch
            {
                RealBenchmarkMessage => ManifestName,
                _ => throw new ArgumentException($"Cannot serialize object of type [{o.GetType()}]", nameof(o))
            };
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is not RealBenchmarkMessage message)
                throw new ArgumentException($"Cannot serialize object of type [{obj.GetType()}]", nameof(obj));

            return message.ToByteArray();
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (manifest != ManifestName)
                throw new SerializationException(
                    $"Unknown manifest [{manifest}] for [{nameof(RealBenchmarkProtobufSerializer)}].");

            return RealBenchmarkMessage.Parser.ParseFrom(bytes);
        }
    }
}
