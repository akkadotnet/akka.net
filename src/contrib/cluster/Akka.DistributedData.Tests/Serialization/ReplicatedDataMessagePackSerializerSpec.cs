// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
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

    [Theory(DisplayName = "Should_PreserveEarlierBytes_When_A_LaterPayloadIsMutated")]
    [InlineData("Counter")]
    [InlineData("ORSetString")]
    public void Should_PreserveEarlierBytes_When_A_LaterPayloadIsMutated(string name)
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        var message = Cases.Single(sample => sample.Name == name).Message;
        var earlier = serializer.ToBinary(message);
        var expected = (byte[])earlier.Clone();

        var later = serializer.ToBinary(message);
        later.AsSpan().Clear();

        Assert.Equal(expected, earlier);
    }

    [Theory(DisplayName = "Should_RetainCausalState_When_CompressedInputSpansSegments")]
    [InlineData("ORSetString")]
    [InlineData("Dictionary")]
    public void Should_RetainCausalState_When_CompressedInputSpansSegments(string name)
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        var sample = Cases.Single(item => item.Name == name);
        var bytes = serializer.ToBinary(sample.Message);

        foreach (var cut in new[] { 1, bytes.Length / 2, bytes.Length - 1 }.Distinct())
        {
            var first = new SequencePart(bytes.AsMemory(0, cut));
            var last = new SequencePart(bytes.AsMemory(cut), first);
            var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
            var restored = serializer.Deserialize(sequence, sample.Manifest);

            Assert.Equal(sample.Message.GetType(), restored.GetType());
            AssertFields(sample.Message, restored);
        }
    }

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

    private sealed class SequencePart : ReadOnlySequenceSegment<byte>
    {
        public SequencePart(ReadOnlyMemory<byte> memory, SequencePart? previous = null)
        {
            Memory = memory;
            if (previous is null)
                return;
            RunningIndex = previous.RunningIndex + previous.Memory.Length;
            previous.Next = this;
        }
    }
}
