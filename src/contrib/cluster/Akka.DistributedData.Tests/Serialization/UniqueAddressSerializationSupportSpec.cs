//-----------------------------------------------------------------------
// <copyright file="UniqueAddressSerializationSupportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Serialization;
using FluentAssertions;
using Xunit;
using Address = Akka.Actor.Address;
using UniqueAddress = Akka.Cluster.UniqueAddress;

namespace Akka.DistributedData.Tests.Serialization
{
    /// <summary>
    /// Task 8.2: DData's <see cref="Proto.Msg.UniqueAddress"/> wire field was already <c>int64</c>
    /// (ReplicatorMessages.proto) before the widen-system-uid-to-64bit change - only the C# narrowing cast in
    /// <see cref="SerializationSupport.UniqueAddressFromProto"/> was removed. This verifies >32-bit uids
    /// round-trip cleanly through <see cref="SerializationSupport.UniqueAddressToProto"/> /
    /// <see cref="SerializationSupport.UniqueAddressFromProto"/>.
    /// </summary>
    [Collection("DistributedDataSpec")]
    public class UniqueAddressSerializationSupportSpec : TestKit.Xunit.TestKit
    {
        private static readonly Config BaseConfig = ConfigurationFactory.ParseString(@"
            akka.actor {
                provider=""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            }
            akka.remote.dot-netty.tcp.port = 0").WithFallback(DistributedData.DefaultConfig());

        private readonly SerializationSupport _support;

        public UniqueAddressSerializationSupportSpec(ITestOutputHelper output)
            : base(BaseConfig, "UniqueAddressSerializationSupportSpec", output)
        {
            _support = new SerializationSupport((ExtendedActorSystem)Sys);
        }

        [Theory(DisplayName = "Should_round_trip_unchanged_When_SerializationSupport_converts_a_64_bit_uid")]
        [InlineData(12345L)]
        [InlineData(long.MaxValue)]
        [InlineData(unchecked((long)0x8000_0000_0000_0001))] // negative
        [InlineData(1L << 40)]
        public void Should_round_trip_unchanged_When_SerializationSupport_converts_a_64_bit_uid(long uid)
        {
            var address = new UniqueAddress(new Address("akka.tcp", Sys.Name, "some.host.org", 4711), uid);

            var proto = SerializationSupport.UniqueAddressToProto(address);
            var roundTripped = _support.UniqueAddressFromProto(proto);

            roundTripped.Should().Be(address);
        }
    }
}
