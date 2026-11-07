// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization.V2PortKit;

public sealed class V2PortAssertionsSpec
{
    [Fact(DisplayName = "Should_CompareAllMapMembers_When_RootEqualityDependsOnEnumerationOrder")]
    public void Should_CompareAllMapMembers_When_RootEqualityDependsOnEnumerationOrder()
    {
        var expected = new OrderedEqualityMessage(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        var actual = new OrderedEqualityMessage(new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 });
        Assert.False(expected.Equals(actual));

        V2PortSpec.AssertSame(new V2PortCase(expected, "M", "map"), actual, "round trip");
    }

    [Theory(DisplayName = "Should_RejectChangedMapMembers_When_StructuralFallbackIsUsed")]
    [InlineData(false)]
    [InlineData(true)]
    public void Should_RejectChangedMapMembers_When_StructuralFallbackIsUsed(bool missingKey)
    {
        var expected = new OrderedEqualityMessage(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        var values = new Dictionary<string, int> { ["a"] = 1 };
        if (!missingKey)
            values.Add("b", 3);
        var actual = new OrderedEqualityMessage(values);

        Action assertion = () => V2PortSpec.AssertSame(new V2PortCase(expected, "M", "map"), actual, "round trip");

        assertion.Should().Throw<Exception>();
    }

    [Fact(DisplayName = "Should_RejectDifferentRuntimeTypes_When_MembersHaveTheSameShape")]
    public void Should_RejectDifferentRuntimeTypes_When_MembersHaveTheSameShape()
    {
        var values = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var expected = new OverlyBroadEqualityMessage(values);
        var actual = new DifferentMessage(values);
        Assert.True(expected.Equals(actual));
        Action assertion = () => V2PortSpec.AssertSame(new V2PortCase(expected, "M", "map"),
            actual, "round trip");

        assertion.Should().Throw<Exception>();
    }

    private sealed class OrderedEqualityMessage(IReadOnlyDictionary<string, int> values)
    {
        public IReadOnlyDictionary<string, int> Values { get; } = values;

        public override bool Equals(object? obj) => obj is OrderedEqualityMessage other && Values.SequenceEqual(other.Values);

        public override int GetHashCode() => Values.Count;
    }

    private sealed record DifferentMessage(IReadOnlyDictionary<string, int> Values);

    private sealed class OverlyBroadEqualityMessage(IReadOnlyDictionary<string, int> values)
    {
        public IReadOnlyDictionary<string, int> Values { get; } = values;

        public override bool Equals(object? obj) => obj is DifferentMessage other && Values.SequenceEqual(other.Values);

        public override int GetHashCode() => Values.Count;
    }
}
