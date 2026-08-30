//-----------------------------------------------------------------------
// <copyright file="UniqueAddressWireCompatSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.IO;
using Akka.Actor;
using Akka.Cluster.Serialization;
using FluentAssertions;
using Google.Protobuf;
using Xunit;
using ProtoUniqueAddress = Akka.Cluster.Serialization.Proto.Msg.UniqueAddress;

namespace Akka.Cluster.Tests.Serialization
{
    /// <summary>
    /// Verifies the v1.5 &lt;-&gt; v1.6 rolling-upgrade wire compatibility of <c>ClusterMessages.proto</c>'s
    /// <c>UniqueAddress.uid</c> field, widened from <c>uint32</c> to <c>uint64</c> (same varint wire type, per
    /// Decision 2/3 of the widen-system-uid-to-64bit design). A not-yet-upgraded v1.5 node must be able to read
    /// a v1.6-emitted field (for uids &lt;= uint32.MaxValue, i.e. default int-range generation), and a v1.6 node
    /// must be able to read a legacy v1.5 uint32 field unchanged.
    ///
    /// These tests operate directly at the protobuf level with <see cref="CodedOutputStream"/>/
    /// <see cref="CodedInputStream"/> to simulate both sides of the gossip boundary without needing a live
    /// v1.5 build.
    /// </summary>
    public class UniqueAddressWireCompatSpec
    {
        // Task 8.2: uids that only fit in the widened (>32-bit) range.
        public static readonly long[] GreaterThan32BitUids =
        {
            long.MaxValue,
            unchecked((long)0x8000_0000_0000_0001), // negative
            1L << 40,
        };

        [Theory(DisplayName = "Should_not_truncate_uid_When_a_v1_5_node_reads_a_v1_6_widened_UniqueAddress_field")]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData((long)int.MaxValue)]
        [InlineData((long)uint.MaxValue)]
        public void Should_not_truncate_uid_When_a_v1_5_node_reads_a_v1_6_widened_UniqueAddress_field(long uid)
        {
            // v1.6 builds + serializes the generated message, whose uid field is now a 64-bit (ulong) property.
            var proto = new ProtoUniqueAddress { Uid = (ulong)uid };
            var bytes = proto.ToByteArray();

            // A v1.5 node's generated parser reads field 2 as a uint32 - replicate that read by hand.
            var parsedByV15 = ReadField2AsUInt32(bytes);

            parsedByV15.Should().Be((uint)uid);
        }

        [Theory(DisplayName = "Should_read_legacy_uid_unchanged_When_a_v1_6_node_reads_a_v1_5_uint32_UniqueAddress_field")]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData((long)int.MaxValue)]
        [InlineData((long)uint.MaxValue)]
        public void Should_read_legacy_uid_unchanged_When_a_v1_6_node_reads_a_v1_5_uint32_UniqueAddress_field(long uid)
        {
            // Hand-write the wire bytes exactly as a v1.5 node's uint32 field would - proto3 fields are all
            // optional, so writing ONLY field 2 (uid) is a valid, minimal UniqueAddress message on the wire.
            var bytes = WriteField2AsUInt32((uint)uid);

            // v1.6's generated parser reads the same varint into its widened uint64 field.
            var parsedByV16 = ProtoUniqueAddress.Parser.ParseFrom(bytes);

            parsedByV16.Uid.Should().Be((ulong)uid);
        }

        [Fact(DisplayName = "Should_round_trip_unchanged_When_ClusterMessageSerializer_converts_an_int_range_uid")]
        public void Should_round_trip_unchanged_When_ClusterMessageSerializer_converts_an_int_range_uid()
        {
            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var uniqueAddress = new UniqueAddress(address, 12345);

            var proto = ClusterMessageSerializer.UniqueAddressToProto(uniqueAddress);
            var roundTripped = ClusterMessageSerializer.UniqueAddressFrom(proto);

            roundTripped.Should().Be(uniqueAddress);
        }

        [Theory(DisplayName = "Should_round_trip_unchanged_When_ClusterMessageSerializer_converts_a_greater_than_32_bit_uid")]
        [MemberData(nameof(GreaterThan32BitUidsMemberData))]
        public void Should_round_trip_unchanged_When_ClusterMessageSerializer_converts_a_greater_than_32_bit_uid(long uid)
        {
            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var uniqueAddress = new UniqueAddress(address, uid);

            var proto = ClusterMessageSerializer.UniqueAddressToProto(uniqueAddress);
            var roundTripped = ClusterMessageSerializer.UniqueAddressFrom(proto);

            roundTripped.Should().Be(uniqueAddress);
        }

        public static TheoryData<long> GreaterThan32BitUidsMemberData()
        {
            var data = new TheoryData<long>();
            foreach (var uid in GreaterThan32BitUids)
                data.Add(uid);
            return data;
        }

        private static uint ReadField2AsUInt32(byte[] bytes)
        {
            var cis = new CodedInputStream(bytes);
            uint value = 0;
            uint tag;
            while ((tag = cis.ReadTag()) != 0)
            {
                if (WireFormat.GetTagFieldNumber(tag) == ProtoUniqueAddress.UidFieldNumber)
                {
                    value = cis.ReadUInt32();
                }
                else
                {
                    cis.SkipLastField();
                }
            }

            return value;
        }

        private static byte[] WriteField2AsUInt32(uint value)
        {
            using var stream = new MemoryStream();
            var cos = new CodedOutputStream(stream);
            cos.WriteTag(ProtoUniqueAddress.UidFieldNumber, WireFormat.WireType.Varint);
            cos.WriteUInt32(value);
            cos.Flush();
            return stream.ToArray();
        }
    }
}
