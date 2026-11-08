// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Execution;
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

    [Fact(DisplayName = "Should_InvokeDomainAssertion_When_NestedReachabilityUsesIdentityEquality")]
    public void Should_InvokeDomainAssertion_When_NestedReachabilityUsesIdentityEquality()
    {
        var expected = RichMessage.Create();
        var actual = RichMessage.Create();
        Assert.False(expected.Equals(actual));
        var called = false;
        var sample = new V2PortCase(expected, "G", "gossip")
        {
            AssertEquivalent = (left, right) =>
            {
                called = true;
                AssertRichMessage(left, right);
            }
        };

        V2PortSpec.AssertSame(sample, actual, "round trip");
        called.Should().BeTrue();
    }

    [Theory(DisplayName = "Should_RejectChangedDomainFields_When_ADelegateChecksIdentityEqualMembers")]
    [InlineData(true)]
    [InlineData(false)]
    public void Should_RejectChangedDomainFields_When_ADelegateChecksIdentityEqualMembers(bool changedMember)
    {
        var expected = RichMessage.Create();
        var actual = changedMember
            ? expected with { Member = new IdentityMember("node", 2) }
            : expected with { Reachability = new IdentityReachability(new Dictionary<string, long> { ["observer"] = 24 }) };
        if (changedMember)
            Assert.True(expected.Equals(actual));
        var sample = new V2PortCase(expected, "G", "gossip") { AssertEquivalent = AssertRichMessage };

        Action assertion = () => V2PortSpec.AssertSame(sample, actual, "round trip");
        assertion.Should().Throw<Exception>();
    }

    [Fact(DisplayName = "Should_RejectWrongRuntimeTypeBeforeDelegate_When_ACaseHasACustomAssertion")]
    public void Should_RejectWrongRuntimeTypeBeforeDelegate_When_ACaseHasACustomAssertion()
    {
        var called = false;
        var sample = new V2PortCase(RichMessage.Create(), "G", "gossip")
        {
            AssertEquivalent = (_, _) => called = true
        };
        Action assertion = () => V2PortSpec.AssertSame(sample, new object(), "round trip");

        assertion.Should().Throw<Exception>();
        called.Should().BeFalse();
    }

    [Fact(DisplayName = "Should_StopBeforeDelegate_When_AnOuterScopeCollectsTheRuntimeTypeFailure")]
    public void Should_StopBeforeDelegate_When_AnOuterScopeCollectsTheRuntimeTypeFailure()
    {
        var called = false;
        var sample = new V2PortCase(RichMessage.Create(), "G", "gossip")
        {
            AssertEquivalent = (_, _) => called = true
        };
        Action assertion = () =>
        {
            using var scope = new AssertionScope();
            V2PortSpec.AssertSame(sample, new object(), "round trip");
        };

        assertion.Should().Throw<Exception>();
        called.Should().BeFalse();
    }

    private static void AssertRichMessage(object expected, object actual)
    {
        var left = (RichMessage)expected;
        var right = (RichMessage)actual;
        right.Member.Address.Should().Be(left.Member.Address);
        right.Member.Status.Should().Be(left.Member.Status);
        right.Reachability.Counters.Should().BeEquivalentTo(left.Reachability.Counters);
    }

    private sealed record RichMessage(IdentityMember Member, IdentityReachability Reachability)
    {
        public static RichMessage Create() => new(new IdentityMember("node", 1),
            new IdentityReachability(new Dictionary<string, long> { ["observer"] = 23 }));
    }

    private sealed class IdentityMember(string address, int status)
    {
        public string Address { get; } = address;
        public int Status { get; } = status;
        public override bool Equals(object? obj) => obj is IdentityMember other && Address == other.Address;
        public override int GetHashCode() => Address.GetHashCode();
    }

    private sealed class IdentityReachability(IReadOnlyDictionary<string, long> counters)
    {
        public IReadOnlyDictionary<string, long> Counters { get; } = counters;
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
