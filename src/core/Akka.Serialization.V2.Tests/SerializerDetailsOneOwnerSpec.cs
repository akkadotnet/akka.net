//-----------------------------------------------------------------------
// <copyright file="SerializerDetailsOneOwnerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using Akka.Serialization;
using FluentAssertions;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class SerializerDetailsOneOwnerSpec
{
    private sealed class MarkerA
    {
    }

    private sealed class MarkerB
    {
    }

    private sealed class MarkerC
    {
    }

    private static SerializerDetails Registration(string alias, params Type[] useFor)
    {
        return SerializerDetails.Create(
            alias,
            new ByteArraySerializer(null!),
            ImmutableHashSet.CreateRange(useFor));
    }

    private static void Compose(params SerializerDetails[] details)
    {
        var setup = SerializationSetup.Create(_ => ImmutableHashSet.CreateRange(details));
        setup.CreateSerializers(null!);
    }

    [Fact(DisplayName = "Should_ComposeBindings_When_TypesAreDisjoint")]
    public void Should_ComposeBindings_When_TypesAreDisjoint()
    {
        var first = Registration("first", typeof(MarkerA));
        var second = Registration("second", typeof(MarkerB), typeof(MarkerC));

        var act = () => Compose(first, second);

        act.Should().NotThrow();
    }

    [Fact(DisplayName = "Should_RejectBindings_When_DifferentAliasesClaimTheSameType")]
    public void Should_RejectBindings_When_DifferentAliasesClaimTheSameType()
    {
        var first = Registration("first", typeof(MarkerA), typeof(MarkerB));
        var second = Registration("second", typeof(MarkerB), typeof(MarkerC));

        var act = () => Compose(first, second);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MarkerB*")
            .Where(exception => exception.Message.Contains("first", StringComparison.Ordinal) && exception.Message.Contains("second", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Should_AcceptBindings_When_OnlyOneSerializerIsRegistered")]
    public void Should_AcceptBindings_When_OnlyOneSerializerIsRegistered()
    {
        var solo = Registration("solo", typeof(MarkerA), typeof(MarkerB));

        var act = () => Compose(solo);

        act.Should().NotThrow();
    }

    [Fact(DisplayName = "Should_RejectBindings_When_NonAdjacentAliasesClaimTheSameType")]
    public void Should_RejectBindings_When_NonAdjacentAliasesClaimTheSameType()
    {
        var first = Registration("first", typeof(MarkerA));
        var second = Registration("second", typeof(MarkerB));
        var third = Registration("third", typeof(MarkerA));

        var act = () => Compose(first, second, third);

        act.Should().Throw<InvalidOperationException>()
            .Where(exception => exception.Message.Contains("first", StringComparison.Ordinal) && exception.Message.Contains("third", StringComparison.Ordinal));
    }
    [Fact(DisplayName = "Should_DeferFactoryAndValidation_When_CreatingSetup")]
    public void Should_DeferFactoryAndValidation_When_CreatingSetup()
    {
        var invoked = false;
        var setup = SerializationSetup.Create(_ =>
        {
            invoked = true;
            return ImmutableHashSet.Create(
                Registration("first", typeof(MarkerA)),
                Registration("second", typeof(MarkerA)));
        });

        invoked.Should().BeFalse();
        var act = () => setup.CreateSerializers(null!);
        act.Should().Throw<InvalidOperationException>();
        invoked.Should().BeTrue();
    }

}
