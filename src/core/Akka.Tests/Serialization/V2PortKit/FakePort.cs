//-----------------------------------------------------------------------
// <copyright file="FakePort.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;

namespace Akka.Tests.Serialization.V2PortKit
{
    public interface IFakeProtocol
    {
    }

    /// <summary>Legacy manifest <c>P</c>.</summary>
    public sealed record FakePing(string Id, int Count) : IFakeProtocol;

    /// <summary>Legacy manifest <c>Q</c>.</summary>
    public sealed record FakePong(string Id) : IFakeProtocol;

    /// <summary>What a broken fake V2 serializer gets wrong, so the kit can be shown to catch it.</summary>
    public enum FakeDefect
    {
        None,
        WrongId,
        LosesCount
    }

    /// <summary>The "legacy" serializer: text, <c>P|id|count</c>. Id 39, so the V2 id is 79.</summary>
    public sealed class FakeLegacySerializer : Serializer
    {
        public const int LegacyId = 39;

        public FakeLegacySerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => LegacyId;

        public override bool IncludeManifest => true;

        public override string Manifest(object obj) => obj is FakePing ? "P" : "Q";

        public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(obj switch
        {
            FakePing p => $"P|{p.Id}|{p.Count}",
            FakePong q => $"Q|{q.Id}",
            _ => throw new ArgumentException($"Cannot serialize {obj.GetType()}")
        });

        public override object FromBinary(byte[] bytes, Type type) => throw new NotSupportedException("manifest only");

        public override object FromBinary(byte[] bytes, string manifest)
        {
            var parts = Encoding.UTF8.GetString(bytes).Split('|');
            return manifest == "P" ? new FakePing(parts[1], int.Parse(parts[2])) : new FakePong(parts[1]);
        }
    }

    /// <summary>The "V2" serializer: binary, same manifests, id legacy + 40.</summary>
    public sealed class FakeV2Serializer : SerializerV2
    {
        public const int V2Id = FakeLegacySerializer.LegacyId + 40;

        private readonly FakeDefect _defect;

        public FakeV2Serializer(ExtendedActorSystem system) : this(system, FakeDefect.None)
        {
        }

        public FakeV2Serializer(ExtendedActorSystem system, FakeDefect defect) : base(system)
        {
            _defect = defect;
        }

        public override int Identifier => _defect == FakeDefect.WrongId ? V2Id - 1 : V2Id;

        public override string Manifest(object obj) => obj is FakePing ? "P" : "Q";

        public override int SizeHint(object obj) => Encode(obj).Length;

        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var bytes = Encode(obj);
            writer.Write(bytes);
            return bytes.Length;
        }

        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            var b = bytes.ToArray();
            if (manifest == "Q")
                return new FakePong(Encoding.UTF8.GetString(b, 1, b.Length - 1));

            var count = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(1, 4));
            return new FakePing(Encoding.UTF8.GetString(b, 5, b.Length - 5), _defect == FakeDefect.LosesCount ? 0 : count);
        }

        // ping: 1, count (4 bytes), id. pong: 2, id.
        private static byte[] Encode(object obj)
        {
            switch (obj)
            {
                case FakePing p:
                    var ping = new byte[5 + Encoding.UTF8.GetByteCount(p.Id)];
                    ping[0] = 1;
                    BinaryPrimitives.WriteInt32LittleEndian(ping.AsSpan(1, 4), p.Count);
                    Encoding.UTF8.GetBytes(p.Id, 0, p.Id.Length, ping, 5);
                    return ping;
                case FakePong q:
                    var pong = new byte[1 + Encoding.UTF8.GetByteCount(q.Id)];
                    pong[0] = 2;
                    Encoding.UTF8.GetBytes(q.Id, 0, q.Id.Length, pong, 1);
                    return pong;
                default:
                    throw new ArgumentException($"Cannot serialize {obj.GetType()}");
            }
        }
    }

    internal static class FakePort
    {
        public static readonly V2PortCase[] Cases =
        {
            new(new FakePing("ping-1", 7), "P", "ping"),
            new(new FakePing("", -1), "P", "ping-empty-id"),
            new(new FakePong("pong-1"), "Q", "pong")
        };

        /// <summary>Registers the rows the way a module's table does: legacy bound to the protocol, V2 read-only.</summary>
        public static ActorSystemSetup Setup() => ActorSystemSetup.Create(SerializationSetup.Create(system => ImmutableHashSet.Create(
            SerializerDetails.Create("fake", new FakeLegacySerializer(system), ImmutableHashSet.Create(typeof(IFakeProtocol))),
            SerializerDetails.Create("fake-v2", new FakeV2Serializer(system), ImmutableHashSet<Type>.Empty))));

        /// <summary>Registers both aliases in HOCON, with no bindings.</summary>
        public static Config Aliases() => Akka.Configuration.ConfigurationFactory.ParseString(@"
            akka.actor.serializers {
                fake = ""Akka.Tests.Serialization.V2PortKit.FakeLegacySerializer, Akka.Tests""
                fake-v2 = ""Akka.Tests.Serialization.V2PortKit.FakeV2Serializer, Akka.Tests""
            }");
    }
}
