//-----------------------------------------------------------------------
// <copyright file="GeneratedV2WrapperSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.Actor;
using Akka.Cluster;
using Akka.Configuration;
using Akka.DistributedData.Serialization;
using Akka.Serialization.V2;
using FluentAssertions;
using Xunit;

namespace Akka.DistributedData.Tests.Serialization
{
    /// <summary>
    /// Integration proof for openspec task 6.10: a source-generated V2 MessagePack payload used as
    /// the value type of a DistributedData CRDT must survive the real
    /// <see cref="ReplicatedDataSerializer"/>'s generic <c>OtherMessage</c> wrapper (serializer id +
    /// manifest + opaque bytes -- <c>SerializationSupport.OtherMessageToProto</c>/<c>OtherMessageFromProto</c>),
    /// the same mechanism <see cref="LWWRegister{T}"/>/<see cref="ORSet{T}"/> use for arbitrary
    /// application-owned element/value types. This mirrors
    /// <see cref="ReplicatedDataSerializerSpec"/>'s existing coverage but uses a generated V2 payload
    /// as the wrapped value instead of a primitive/<see cref="IActorRef"/> value.
    /// </summary>
    [Collection("DistributedDataSpec")]
    public class GeneratedV2WrapperSpec : TestKit.Xunit.TestKit
    {
        private static readonly Config BaseConfig = ConfigurationFactory.ParseString(@"
            akka.actor {
                provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                serializers {
                    distributed-data-test = ""Akka.DistributedData.Tests.Serialization.CargoProtocolSerializer, Akka.DistributedData.Tests""
                }
                serialization-bindings {
                    ""Akka.DistributedData.Tests.Serialization.ICargoProtocol, Akka.DistributedData.Tests"" = distributed-data-test
                }
            }
            akka.remote.dot-netty.tcp.port = 0").WithFallback(DistributedData.DefaultConfig());

        private readonly UniqueAddress _address1;

        public GeneratedV2WrapperSpec(ITestOutputHelper output)
            : base(BaseConfig, "GeneratedV2WrapperSpec", output: output)
        {
            _address1 = new UniqueAddress(new Address("akka.tcp", Sys.Name, "some.host.org", 4711), 1);
        }

        [Fact(DisplayName = "ReplicatedDataSerializer should preserve a generated V2 payload wrapped inside LWWRegister")]
        public void ReplicatedDataSerializer_should_preserve_generated_V2_payload_inside_LWWRegister()
        {
            var payload = new CargoManifest("cargo-1", 42);
            var register = new LWWRegister<ICargoProtocol>(_address1, payload);

            // the wrapped payload itself must resolve to our generated V2 serializer.
            Sys.Serialization.FindSerializerFor(payload).Should().BeOfType<CargoProtocolSerializer>();

            var serializer = Sys.Serialization.FindSerializerFor(register);
            serializer.Should().BeOfType<ReplicatedDataSerializer>();

            var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, register);
            var bytes = serializer.ToBinary(register);
            var recovered = (LWWRegister<ICargoProtocol>)Sys.Serialization.Deserialize(bytes, serializer.Identifier, manifest);

            recovered.Value.Should().Be(payload);
            ReferenceEquals(recovered.Value, payload).Should().BeFalse();
        }

        [Fact(DisplayName = "ReplicatedDataSerializer should preserve generated V2 payloads wrapped inside ORSet elements")]
        public void ReplicatedDataSerializer_should_preserve_generated_V2_payloads_inside_ORSet()
        {
            var first = new CargoManifest("cargo-2", 7);
            var second = new CargoManifest("cargo-3", 11);
            var set = ORSet.Create<ICargoProtocol>(_address1, first).Add(_address1, second);

            var serializer = Sys.Serialization.FindSerializerFor(set);
            serializer.Should().BeOfType<ReplicatedDataSerializer>();

            var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, set);
            var bytes = serializer.ToBinary(set);
            var recovered = (ORSet<ICargoProtocol>)Sys.Serialization.Deserialize(bytes, serializer.Identifier, manifest);

            recovered.Elements.Should().BeEquivalentTo(new[] { first, second });
        }
    }

    public interface ICargoProtocol
    {
    }

    [AkkaSerializer<ICargoProtocol>("distributed-data-test", 120420)]
    public sealed partial class CargoProtocolSerializer : AkkaSerializer
    {
        public static partial SerializerRegistration CreateRegistration();
    }

    [AkkaSerializable(Manifest = "cargo-manifest-v1")]
    public sealed record CargoManifest(
        [property: AkkaField(1)] string CargoId,
        [property: AkkaField(2)] int Weight) : ICargoProtocol;
}
