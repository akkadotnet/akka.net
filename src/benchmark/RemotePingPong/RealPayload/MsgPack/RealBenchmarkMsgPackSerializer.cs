//-----------------------------------------------------------------------
// <copyright file="RealBenchmarkMsgPackSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Serialization;
using MessagePack;

namespace RemotePingPong.RealPayload.MsgPack
{
    /// <summary>
    /// Hand-written <see cref="SerializerWithStringManifest"/> for the benchmark's real-payload,
    /// MessagePack arm -- same shape as
    /// <see cref="RemotePingPong.RealPayload.Protobuf.RealBenchmarkProtobufSerializer"/> (a string
    /// manifest, a single-case switch in <see cref="FromBinary(byte[], string)"/>), but calls
    /// <see cref="MessagePackSerializer.Serialize{T}(T, MessagePackSerializerOptions?, System.Threading.CancellationToken)"/>/
    /// <see cref="MessagePackSerializer.Deserialize{T}(System.ReadOnlyMemory{byte}, MessagePackSerializerOptions?, System.Threading.CancellationToken)"/>
    /// against <see cref="RealBenchmarkMessage"/>'s own <c>[MessagePackObject]</c>/<c>[Key(n)]</c>
    /// POCO (see RealBenchmarkMessages.cs) instead of Google.Protobuf's generated
    /// <c>ToByteArray</c>/<c>Parser</c>. Deliberately uses <see cref="MessagePackSerializerOptions.Standard"/>
    /// -- the Standard resolver, contract ([Key]-attribute) mode, no compression -- rather than a
    /// typeless or LZ4-compressed options instance: this arm measures raw attribute-based MessagePack
    /// serializer speed, not typeless dynamic dispatch or block compression.
    /// </summary>
    /// <remarks>
    /// SerializerId 987003 is arbitrary but deliberately far outside both Akka's reserved internal
    /// range (0-40, see akka.conf) and the other two arms' ids (987001 V2, 987002 protobuf) to avoid
    /// any collision.
    /// </remarks>
    public sealed class RealBenchmarkMsgPackSerializer : SerializerWithStringManifest
    {
        public const int IdentifierValue = 987003;
        public const string ManifestName = "real-benchmark-v1";

        private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard;

        public RealBenchmarkMsgPackSerializer(ExtendedActorSystem system) : base(system)
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

            return MessagePackSerializer.Serialize(message, Options);
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (manifest != ManifestName)
                throw new SerializationException(
                    $"Unknown manifest [{manifest}] for [{nameof(RealBenchmarkMsgPackSerializer)}].");

            return MessagePackSerializer.Deserialize<RealBenchmarkMessage>(bytes, Options);
        }
    }
}
