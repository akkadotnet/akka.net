//-----------------------------------------------------------------------
// <copyright file="SerializerRegistrationOneOwnerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using Akka.Serialization.V2;
using FluentAssertions;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Design.md Decision 19's startup one-owner check: <see cref="SerializerRegistration.CreateSetup(SerializerRegistration[])"/>
/// throws when two composed registrations claim the same message type. This is the last line of
/// defense for two serializers declared in assemblies that cannot see each other at build time --
/// a build-time check (AKKASG031/AKKASG041, and the new cross-assembly placement diagnostics) only
/// catches the case where one assembly can see the other. Exercised directly against
/// <see cref="SerializerRegistration"/>, with no generator run and no <c>ActorSystem</c>: the check
/// is a pure function of each registration's own <see cref="SerializerRegistration.UseFor"/> set,
/// so it needs neither.
/// </summary>
public sealed class SerializerRegistrationOneOwnerSpec
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

    // The factory delegate is never invoked by CreateSetup itself (SerializationSetup defers
    // construction until an ActorSystem asks for it) -- these tests never reach that point, so a
    // delegate that would throw if it ever ran is a deliberate, cheap correctness guard.
    private static SerializerRegistration Registration(string alias, params Type[] useFor)
    {
        return SerializerRegistration.Create(
            alias,
            _ => throw new InvalidOperationException("The serializer factory should never be invoked by this test."),
            ImmutableHashSet.CreateRange(useFor));
    }

    [Fact(DisplayName = "CreateSetup should compose registrations whose claimed types are disjoint without throwing")]
    public void CreateSetup_should_compose_disjoint_registrations()
    {
        var first = Registration("first", typeof(MarkerA));
        var second = Registration("second", typeof(MarkerB), typeof(MarkerC));

        var act = () => SerializerRegistration.CreateSetup(first, second);

        act.Should().NotThrow();
    }

    [Fact(DisplayName = "CreateSetup should throw naming both serializers when two registrations claim the same type")]
    public void CreateSetup_should_throw_when_two_registrations_claim_the_same_type()
    {
        var first = Registration("first", typeof(MarkerA), typeof(MarkerB));
        var second = Registration("second", typeof(MarkerB), typeof(MarkerC));

        var act = () => SerializerRegistration.CreateSetup(first, second);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MarkerB*")
            .Where(exception => exception.Message.Contains("first", StringComparison.Ordinal) && exception.Message.Contains("second", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "CreateSetup should not throw when the SAME registration instance appears once, even though its own claimed types repeat across a re-composition")]
    public void CreateSetup_should_not_throw_for_a_single_registration_composed_alone()
    {
        var solo = Registration("solo", typeof(MarkerA), typeof(MarkerB));

        var act = () => SerializerRegistration.CreateSetup(solo);

        act.Should().NotThrow();
    }

    [Fact(DisplayName = "CreateSetup should throw for three or more registrations when any two collide, not just adjacent ones")]
    public void CreateSetup_should_throw_when_non_adjacent_registrations_collide()
    {
        var first = Registration("first", typeof(MarkerA));
        var second = Registration("second", typeof(MarkerB));
        var third = Registration("third", typeof(MarkerA));

        var act = () => SerializerRegistration.CreateSetup(first, second, third);

        act.Should().Throw<InvalidOperationException>()
            .Where(exception => exception.Message.Contains("first", StringComparison.Ordinal) && exception.Message.Contains("third", StringComparison.Ordinal));
    }
}
