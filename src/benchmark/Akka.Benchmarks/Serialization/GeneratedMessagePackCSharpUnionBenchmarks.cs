//-----------------------------------------------------------------------
// <copyright file="GeneratedMessagePackCSharpUnionBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Benchmarks.Configurations;
using Akka.Serialization;
using Akka.Serialization.V2;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Akka.Benchmarks.Serialization;

/// <summary>
/// A C# 15 <c>union</c> field against the equivalent closed record hierarchy
/// field. Both hold the SAME case objects and write the SAME bytes (checked in
/// <see cref="Setup"/>), so each category isolates the cost of the dispatch shape: the hierarchy
/// dispatches on the field value's runtime type, the union on the runtime type of its
/// <c>Value</c> and rebuilds the union struct on read. The hierarchy is the baseline in every category.
/// Each shape has its own serializer, so neither pays for the other's arm in the generated top-level
/// type switch (serialize dispatches through a sequential <c>switch</c> on the message type).
/// </summary>
/// <remarks>
/// This project builds with the .NET 10 SDK compiler, which cannot parse <c>union</c> syntax, so
/// <see cref="CoverageBenchmarkUnion"/> is written out as the struct the C# 15 compiler lowers a
/// union to: <c>[Union]</c>, <c>IUnion</c>, one public constructor per case, a public <c>Value</c>.
/// The generator only looks at that shape, so it emits exactly the code it emits for a real union.
/// "First" and "last" are the first and last declared case: write dispatch is a sequential
/// type-equality chain in declaration order, read dispatch is a manifest <c>switch</c>.
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class GeneratedMessagePackCSharpUnionBenchmarks
{
    private ExtendedActorSystem _system = null!;
    private Serializer _hierarchySerializer = null!;
    private Serializer _unionSerializer = null!;

    private HierarchyCovered _hierarchyFirst = null!;
    private HierarchyCovered _hierarchyLast = null!;
    private UnionCovered _unionFirst = null!;
    private UnionCovered _unionLast = null!;
    private UnionCovered _unionDefault = null!;

    private byte[] _hierarchyFirstBytes = null!;
    private byte[] _hierarchyLastBytes = null!;
    private byte[] _unionFirstBytes = null!;
    private byte[] _unionLastBytes = null!;
    private byte[] _unionDefaultBytes = null!;

    private string _hierarchyManifest = null!;
    private string _unionManifest = null!;

    [GlobalSetup]
    public void Setup()
    {
        var setup = ActorSystemSetup.Create(Akka.Serialization.SerializationSetup.Create(system =>
            ImmutableHashSet.Create(
                HierarchyUnionBenchmarkSerializer.CreateSerializerDetails(system),
                CSharpUnionBenchmarkSerializer.CreateSerializerDetails(system))));
        _system = (ExtendedActorSystem)ActorSystem.Create("generated-messagepack-csharp-union-bench", setup);

        var first = new CoverageBenchmark.PaidInvoice(90001L, 1_000_042L, 199.95m, Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var last = new CoverageBenchmark.Migrated("legacy-billing", DateTimeOffset.FromUnixTimeMilliseconds(1_735_776_000_000));

        _hierarchyFirst = new HierarchyCovered(7L, first);
        _hierarchyLast = new HierarchyCovered(8L, last);
        _unionFirst = new UnionCovered(7L, new CoverageBenchmarkUnion(first));
        _unionLast = new UnionCovered(8L, new CoverageBenchmarkUnion(last));
        _unionDefault = new UnionCovered(9L, default);

        _hierarchySerializer = _system.Serialization.FindSerializerFor(_hierarchyFirst);
        _unionSerializer = _system.Serialization.FindSerializerFor(_unionFirst);
        if (_hierarchySerializer is not HierarchyUnionBenchmarkSerializer || _unionSerializer is not CSharpUnionBenchmarkSerializer)
            throw new InvalidOperationException($"Expected generated serializers, got [{_hierarchySerializer.GetType()}] and [{_unionSerializer.GetType()}].");

        _hierarchyManifest = Akka.Serialization.Serialization.ManifestFor(_hierarchySerializer, _hierarchyFirst);
        _unionManifest = Akka.Serialization.Serialization.ManifestFor(_unionSerializer, _unionFirst);

        _hierarchyFirstBytes = _system.Serialization.Serialize(_hierarchyFirst);
        _hierarchyLastBytes = _system.Serialization.Serialize(_hierarchyLast);
        _unionFirstBytes = _system.Serialization.Serialize(_unionFirst);
        _unionLastBytes = _system.Serialization.Serialize(_unionLast);
        _unionDefaultBytes = _system.Serialization.Serialize(_unionDefault);

        // The comparison is only fair if both shapes do the same encoding work.
        if (!_hierarchyFirstBytes.SequenceEqual(_unionFirstBytes) || !_hierarchyLastBytes.SequenceEqual(_unionLastBytes))
            throw new InvalidOperationException("C# union and closed hierarchy fields must write identical bytes for the same case.");

        Console.WriteLine($"First-case payload: {_unionFirstBytes.Length} bytes; last-case payload: {_unionLastBytes.Length} bytes; default union payload: {_unionDefaultBytes.Length} bytes");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _system.Terminate();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("serialize-first-case")]
    public byte[] Hierarchy_first_case_serialize() => _system.Serialization.Serialize(_hierarchyFirst);

    [Benchmark, BenchmarkCategory("serialize-first-case")]
    public byte[] Union_first_case_serialize() => _system.Serialization.Serialize(_unionFirst);

    [Benchmark(Baseline = true), BenchmarkCategory("serialize-last-case")]
    public byte[] Hierarchy_last_case_serialize() => _system.Serialization.Serialize(_hierarchyLast);

    [Benchmark, BenchmarkCategory("serialize-last-case")]
    public byte[] Union_last_case_serialize() => _system.Serialization.Serialize(_unionLast);

    [Benchmark(Baseline = true), BenchmarkCategory("deserialize-first-case")]
    public object Hierarchy_first_case_deserialize() => _system.Serialization.Deserialize(_hierarchyFirstBytes, _hierarchySerializer.Identifier, _hierarchyManifest);

    [Benchmark, BenchmarkCategory("deserialize-first-case")]
    public object Union_first_case_deserialize() => _system.Serialization.Deserialize(_unionFirstBytes, _unionSerializer.Identifier, _unionManifest);

    [Benchmark(Baseline = true), BenchmarkCategory("deserialize-last-case")]
    public object Hierarchy_last_case_deserialize() => _system.Serialization.Deserialize(_hierarchyLastBytes, _hierarchySerializer.Identifier, _hierarchyManifest);

    [Benchmark, BenchmarkCategory("deserialize-last-case")]
    public object Union_last_case_deserialize() => _system.Serialization.Deserialize(_unionLastBytes, _unionSerializer.Identifier, _unionManifest);

    [Benchmark, BenchmarkCategory("default-union")]
    public byte[] Union_default_serialize() => _system.Serialization.Serialize(_unionDefault);

    [Benchmark, BenchmarkCategory("default-union")]
    public object Union_default_deserialize() => _system.Serialization.Deserialize(_unionDefaultBytes, _unionSerializer.Identifier, _unionManifest);
}

public interface IHierarchyUnionBenchmarkProtocol
{
}

[AkkaSerializer<IHierarchyUnionBenchmarkProtocol>("hierarchy-union-benchmark", 120007)]
public sealed partial class HierarchyUnionBenchmarkSerializer : AkkaSerializer
{
    public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
}

public interface ICSharpUnionBenchmarkProtocol
{
}

[AkkaSerializer<ICSharpUnionBenchmarkProtocol>("csharp-union-benchmark", 120006)]
public sealed partial class CSharpUnionBenchmarkSerializer : AkkaSerializer
{
    public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
}

public abstract record CoverageBenchmark
{
    private CoverageBenchmark()
    {
    }

    [AkkaSerializable(Manifest = "paid-invoice")]
    public sealed record PaidInvoice(
        [property: AkkaField(1)] long PublisherId,
        [property: AkkaField(2)] long InvoiceNumber,
        [property: AkkaField(3)] decimal Amount,
        [property: AkkaField(4)] Guid PaymentId) : CoverageBenchmark;

    [AkkaSerializable(Manifest = "paid-upstream")]
    public sealed record PaidUpstream(
        [property: AkkaField(1)] string? Reference,
        [property: AkkaField(2)] decimal? Amount) : CoverageBenchmark;

    [AkkaSerializable(Manifest = "trial", AllowEmpty = true)]
    public sealed record Trial : CoverageBenchmark;

    [AkkaSerializable(Manifest = "complimentary", AllowEmpty = true)]
    public sealed record Complimentary : CoverageBenchmark;

    [AkkaSerializable(Manifest = "migrated")]
    public sealed record Migrated(
        [property: AkkaField(1)] string Source,
        [property: AkkaField(2)] DateTimeOffset MigratedAt) : CoverageBenchmark;
}

/// <summary>
/// What <c>public union CoverageBenchmarkUnion(PaidInvoice, PaidUpstream, Trial, Complimentary, Migrated);</c>
/// lowers to. See the remarks on <see cref="GeneratedMessagePackCSharpUnionBenchmarks"/>.
/// </summary>
[System.Runtime.CompilerServices.Union]
public readonly struct CoverageBenchmarkUnion : System.Runtime.CompilerServices.IUnion
{
    public CoverageBenchmarkUnion(CoverageBenchmark.PaidInvoice value) => Value = value;

    public CoverageBenchmarkUnion(CoverageBenchmark.PaidUpstream value) => Value = value;

    public CoverageBenchmarkUnion(CoverageBenchmark.Trial value) => Value = value;

    public CoverageBenchmarkUnion(CoverageBenchmark.Complimentary value) => Value = value;

    public CoverageBenchmarkUnion(CoverageBenchmark.Migrated value) => Value = value;

    public object? Value { get; }
}

[AkkaSerializable(Manifest = "hierarchy-covered")]
public sealed record HierarchyCovered(
    [property: AkkaField(1)] long SubscriptionId,
    [property: AkkaField(2)] CoverageBenchmark Coverage) : IHierarchyUnionBenchmarkProtocol;

[AkkaSerializable(Manifest = "union-covered")]
public sealed record UnionCovered(
    [property: AkkaField(1)] long SubscriptionId,
    [property: AkkaField(2)] CoverageBenchmarkUnion Coverage) : ICSharpUnionBenchmarkProtocol;
