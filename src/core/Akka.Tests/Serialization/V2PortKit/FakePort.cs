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
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Serialization;

namespace Akka.Tests.Serialization.V2PortKit
{
    /// <summary>The fake subsystem's protocol, bound to the legacy fake serializer.</summary>
    public interface IFakeProtocol
    {
    }

    /// <summary>Legacy manifest <c>P</c>.</summary>
    public sealed record FakePing(string Id, int Count) : IFakeProtocol;

    /// <summary>Legacy manifest <c>Q</c>.</summary>
    public sealed record FakePong(string Id, long Sequence, bool Ok) : IFakeProtocol;

    /// <summary>What a broken fake V2 serializer gets wrong, so the kit's checks can be shown to catch it.</summary>
    public enum FakeDefect
    {
        None,
        WrongId,
        WrongManifest,
        LosesCount,
        TrailingByte,
        InexactSizeHint,
        BufferPathDiffers,
        NeedsDynamicTypeLoading
    }

    /// <summary>The "legacy" serializer: text, <c>P|id|count</c>. Id 39, the top of the legacy range, so the V2 id is 79.</summary>
    public sealed class FakeLegacySerializer : Serializer
    {
        public const int LegacyId = 39;

        private readonly char _separator;

        public FakeLegacySerializer(ExtendedActorSystem system) : this(system, '|')
        {
        }

        public FakeLegacySerializer(ExtendedActorSystem system, char separator) : base(system)
        {
            _separator = separator;
        }

        public override int Identifier => LegacyId;

        public override bool IncludeManifest => true;

        public override string Manifest(object obj) => obj switch
        {
            FakePing => "P",
            FakePong => "Q",
            _ => throw new ArgumentException($"Cannot serialize {obj.GetType()}")
        };

        public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(obj switch
        {
            FakePing p => $"P{_separator}{p.Id}{_separator}{p.Count}",
            FakePong q => $"Q{_separator}{q.Id}{_separator}{q.Sequence}{_separator}{q.Ok}",
            _ => throw new ArgumentException($"Cannot serialize {obj.GetType()}")
        });

        public override object FromBinary(byte[] bytes, Type type) => throw new NotSupportedException("manifest only");

        public override object FromBinary(byte[] bytes, string manifest)
        {
            var parts = Encoding.UTF8.GetString(bytes).Split(_separator);
            return manifest switch
            {
                "P" => new FakePing(parts[1], int.Parse(parts[2])),
                "Q" => new FakePong(parts[1], long.Parse(parts[2]), bool.Parse(parts[3])),
                _ => throw new ArgumentException($"Unknown manifest {manifest}")
            };
        }
    }

    /// <summary>The "V2" serializer: a binary format of its own, same manifests, id legacy + 40.</summary>
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

        public override string Manifest(object obj) => _defect == FakeDefect.WrongManifest
            ? "X"
            : obj switch
            {
                FakePing => "P",
                FakePong => "Q",
                _ => throw new ArgumentException($"Cannot serialize {obj.GetType()}")
            };

        public override int SizeHint(object obj) => _defect == FakeDefect.InexactSizeHint ? 3 : Encode(obj).Length;

        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var bytes = Encode(obj);
            if (_defect == FakeDefect.BufferPathDiffers)
                bytes = bytes.Reverse().ToArray();
            writer.Write(bytes);
            return bytes.Length;
        }

        public override byte[] ToBinary(object obj) => Encode(obj);

        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            if (_defect == FakeDefect.NeedsDynamicTypeLoading &&
                AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var enabled) && !enabled)
                throw new InvalidOperationException("this serializer resolves types by name");

            var b = bytes.ToArray();
            switch (manifest)
            {
                case "P":
                    var count = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(1, 4));
                    return new FakePing(Encoding.UTF8.GetString(b, 6, b[5]), _defect == FakeDefect.LosesCount ? 0 : count);
                case "Q":
                    return new FakePong(
                        Encoding.UTF8.GetString(b, 10, b[9]), BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(1, 8)), b[10 + b[9]] == 1);
                default:
                    throw new ArgumentException($"Unknown manifest {manifest}");
            }
        }

        private byte[] Encode(object obj)
        {
            byte[] bytes;
            switch (obj)
            {
                case FakePing p:
                    var id = Encoding.UTF8.GetBytes(p.Id);
                    bytes = new byte[6 + id.Length];
                    bytes[0] = 1;
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1, 4), p.Count);
                    bytes[5] = (byte)id.Length;
                    id.CopyTo(bytes, 6);
                    break;
                case FakePong q:
                    var qid = Encoding.UTF8.GetBytes(q.Id);
                    bytes = new byte[11 + qid.Length];
                    bytes[0] = 2;
                    BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(1, 8), q.Sequence);
                    bytes[9] = (byte)qid.Length;
                    qid.CopyTo(bytes, 10);
                    bytes[10 + qid.Length] = (byte)(q.Ok ? 1 : 0);
                    break;
                default:
                    throw new ArgumentException($"Cannot serialize {obj.GetType()}");
            }

            return _defect == FakeDefect.TrailingByte ? bytes.Append((byte)0).ToArray() : bytes;
        }
    }

    internal static class FakePort
    {
        public static readonly V2PortCase[] Cases =
        {
            new(new FakePing("ping-1", 7), "P", "ping"),
            new(new FakePing("", -1), "P", "ping-empty-id"),
            new(new FakePong("pong-1", 9_000_000_000L, true), "Q", "pong"),
            new(new FakePong("pong-2", 0, false), "Q", "pong-false")
        };

        /// <summary>Registers the two rows the way a module's table does: legacy bound to the protocol, V2 read-only.</summary>
        public static SerializationSetup ReadOnlyRows() => SerializationSetup.Create(system => ImmutableHashSet.Create(
            SerializerDetails.Create("fake", new FakeLegacySerializer(system), ImmutableHashSet.Create(typeof(IFakeProtocol))),
            SerializerDetails.Create("fake-v2", new FakeV2Serializer(system), ImmutableHashSet<Type>.Empty)));

        /// <summary>Registers both aliases with no bound types, for specs that bind through config.</summary>
        public static SerializationSetup UnboundRows() => SerializationSetup.Create(system => ImmutableHashSet.Create(
            SerializerDetails.Create("fake", new FakeLegacySerializer(system), ImmutableHashSet<Type>.Empty),
            SerializerDetails.Create("fake-v2", new FakeV2Serializer(system), ImmutableHashSet<Type>.Empty)));

        public static ActorSystemSetup Setup() => ActorSystemSetup.Create(ReadOnlyRows());

        public static V2Port Port(FakeDefect defect = FakeDefect.None) => new(
            system => new FakeLegacySerializer(system),
            system => new FakeV2Serializer(system, defect),
            Cases)
        {
            RequiredManifests = new[] { "P", "Q" }
        };
    }
}
