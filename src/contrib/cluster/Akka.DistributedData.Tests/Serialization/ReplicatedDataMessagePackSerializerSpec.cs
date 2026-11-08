// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.DistributedData.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.DistributedData.Tests.Serialization;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ReplicatedDataMessagePackSerializerSpec : V2PortSpec
{
    public ReplicatedDataMessagePackSerializerSpec(ITestOutputHelper output) : base(output, ReplicatorMessagePackSerializerSpec.PortConfig)
    {
    }

    protected override Serializer CreateLegacy(ExtendedActorSystem system) => new ReplicatedDataSerializer(system);
    protected override SerializerV2 CreateV2(ExtendedActorSystem system) => new ReplicatedDataMessagePackSerializer(system);

    protected override IReadOnlyList<V2PortCase> Cases => ReplicatedDataWireCases.Create(Sys.Name,
        ((ExtendedActorSystem)Sys).Provider.ResolveActorRef, ((ExtendedActorSystem)Sys).Provider.DefaultAddress.Protocol)
        .Select(item => new V2PortCase(item.Message, item.Manifest, item.Name)
        {
            RequiresDynamicTypeLoading = RequiresDynamic(item.Manifest, item.Name),
            AssertEquivalent = AssertFields
        }).ToArray();

    private static bool RequiresDynamic(string manifest, string name)
        => manifest is "H" or "I" or "J" or "K" or "Ha" or "Hr" or "Hk" or "Hu" or "Hg" or "Ig" or "Jo" or "Ko" or "h" or "i" or "k"
           || name is "GSetOther" or "GSetNull" or "ORSetOther" or "RegisterNested";

    private static void AssertFields(object expected, object actual)
    {
        // The fixed corpus has one entry per unordered map. Require the complete domain
        // equality (including internal causal state), rather than a public-member fallback.
        Assert.True(expected.Equals(actual), $"Changed replicated-data state: {expected.GetType().Name}");
        if (expected is ORMultiValueDictionary<string, string> map)
            Assert.Equal(map.DeltaValues, ((ORMultiValueDictionary<string, string>)actual).DeltaValues);
        if (expected is ORMultiValueDictionary<string, string>.ORMultiValueDictionaryDelta delta)
            Assert.Equal(delta.WithValueDeltas, ((ORMultiValueDictionary<string, string>.ORMultiValueDictionaryDelta)actual).WithValueDeltas);
    }
}
