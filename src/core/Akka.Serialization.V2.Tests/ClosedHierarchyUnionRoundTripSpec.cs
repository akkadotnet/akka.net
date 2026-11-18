//-----------------------------------------------------------------------
// <copyright file="ClosedHierarchyUnionRoundTripSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// A closed record hierarchy (private base constructor, sealed nested cases) used as a field type
/// with no attribute on the base: every case, fieldless ones included, round-trips
/// through the serializer's id and manifest as its exact concrete type.
/// </summary>
public sealed class ClosedHierarchyUnionRoundTripSpec : AkkaSpec
{
    public ClosedHierarchyUnionRoundTripSpec(ITestOutputHelper output) : base(ConfigurationFactory.Empty, output)
    {
    }

    public static IEnumerable<object[]> EveryCoverageCase()
    {
        yield return new object[] { new Coverage.PaidInvoice(42, 1001) };
        yield return new object[] { new Coverage.PaidUpstream("ref-7", 19.99m) };
        yield return new object[] { new Coverage.PaidUpstream(null, null) };
        yield return new object[] { new Coverage.Trial() };
        yield return new object[] { new Coverage.Complimentary() };
        yield return new object[] { new Coverage.Migrated() };
    }

    [Theory(DisplayName = "Should_RoundTripEveryCase_When_FieldIsClosedHierarchyUnion")]
    [MemberData(nameof(EveryCoverageCase))]
    public void Should_RoundTripEveryCase_When_FieldIsClosedHierarchyUnion(Coverage coverage)
    {
        var serializer = new CoverageTestSerializer((ExtendedActorSystem)Sys);
        var start = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var original = new PeriodCovered(7, start, start.AddMonths(1), coverage);

        var bytes = serializer.ToBinary(original);
        var restored = (PeriodCovered)serializer.FromBinary(bytes, serializer.Manifest(original));

        restored.Should().Be(original);
        restored.Coverage.GetType().Should().Be(coverage.GetType());
    }
}

public interface ICoverageTestProtocol;

public abstract record Coverage
{
    private Coverage() { }

    [AkkaSerializable(Manifest = "paid-invoice")]
    public sealed record PaidInvoice([property: AkkaField(1)] long PublisherId, [property: AkkaField(2)] long InvoiceNumber) : Coverage;

    [AkkaSerializable(Manifest = "paid-upstream")]
    public sealed record PaidUpstream([property: AkkaField(1)] string? Reference, [property: AkkaField(2)] decimal? Amount) : Coverage;

    [AkkaSerializable(Manifest = "trial", AllowEmpty = true)]
    public sealed record Trial : Coverage;

    [AkkaSerializable(Manifest = "complimentary", AllowEmpty = true)]
    public sealed record Complimentary : Coverage;

    [AkkaSerializable(Manifest = "migrated", AllowEmpty = true)]
    public sealed record Migrated : Coverage;
}

[AkkaSerializable(Manifest = "period-covered")]
public sealed record PeriodCovered(
    [property: AkkaField(1)] long SubscriptionId,
    [property: AkkaField(2)] DateTime Start,
    [property: AkkaField(3)] DateTime End,
    [property: AkkaField(4)] Coverage Coverage) : ICoverageTestProtocol;

[AkkaSerializer<ICoverageTestProtocol>("coverage-test", 121101)]
public sealed partial class CoverageTestSerializer : AkkaSerializer
{
    public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}
