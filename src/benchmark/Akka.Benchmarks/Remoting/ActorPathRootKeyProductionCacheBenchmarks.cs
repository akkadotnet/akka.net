// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Remoting;

/// <summary>
/// Separates root-key computation under default cache pressure from complete-path hits.
/// Inputs are generated once through the ordinary provider; no temporary refs are registered.
/// </summary>
[MemoryDiagnoser]
public class ActorPathRootKeyProductionCacheBenchmarks
{
    private const int RingSize = 4096;
    private const int RingMask = RingSize - 1;
    private const int AuditCycles = 4;
    private ActorPathCache _churnCache = null!;
    private ActorPathCache _mixedCache = null!;
    private ActorPathCache _hitCache = null!;
    private string[] _paths = null!;
    private string[] _hot = null!;
    private int _churnNext;
    private int _mixedNext;

    [Params(1, 32)]
    public int RootCount { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        if (RootCount is not (1 or 32))
            throw new ArgumentOutOfRangeException(nameof(RootCount));

        _paths = new string[RingSize];
        _hot = new string[RootCount];
        var addresses = new Address[RootCount];
        for (var root = 0; root < addresses.Length; root++)
            addresses[root] = new Address("akka", "root-key-production", "127.0.0.1", 2552 + root);

        var system = ActorSystem.Create("root-key-production");
        try
        {
            var provider = ((ExtendedActorSystem)system).Provider;
            for (var index = 0; index < _paths.Length; index++)
                _paths[index] = provider.TempPath().ToSerializationFormatWithAddress(addresses[index % RootCount]);
            for (var root = 0; root < _hot.Length; root++)
                _hot[root] = provider.TempPath().ToSerializationFormatWithAddress(addresses[root]);
        }
        finally
        {
            await system.Terminate();
        }

        if (!system.WhenTerminated.IsCompletedSuccessfully)
            throw new InvalidOperationException("The setup-only actor system did not terminate successfully.");

        var distinctPaths = new HashSet<string>(_paths, StringComparer.Ordinal).Count;
        var distinctHotPaths = new HashSet<string>(_hot, StringComparer.Ordinal).Count;
        if (distinctPaths != RingSize || distinctHotPaths != RootCount)
            throw new InvalidOperationException("The generated input paths must be distinct.");

        // Untimed and separate from every measured cache. This audit has startup/GC cost;
        // it describes the offered sequence, not a production workload distribution.
        Console.WriteLine("ROOT_KEY_PRODUCTION_CACHE_AUDIT " + JsonSerializer.Serialize(new
        {
            RootCount,
            RingSize,
            AuditCycles,
            DistinctPaths = distinctPaths,
            DistinctHotPaths = distinctHotPaths,
            FirstPath = _paths[0],
            LastPath = _paths[^1],
            SetupActorSystemTerminated = system.WhenTerminated.IsCompletedSuccessfully,
            Sequences = AuditSequences()
        }));

        _churnCache = new ActorPathCache();
        _mixedCache = new ActorPathCache();
        _hitCache = new ActorPathCache();
        _churnNext = 0;
        _mixedNext = 0;
        foreach (var path in _hot)
            _mixedCache.GetOrCompute(path);
        _hitCache.GetOrCompute(_hot[0]);
    }

    [Benchmark]
    public ActorPath ChurnPaths() => _churnCache.GetOrCompute(_paths[_churnNext++ & RingMask]);

    [Benchmark]
    public ActorPath MixedPaths()
    {
        var n = _mixedNext++;
        var group = n >> 2;
        return (n & 3) == 0
            ? _mixedCache.GetOrCompute(_paths[group & RingMask])
            : _mixedCache.GetOrCompute(_hot[group & (RootCount - 1)]);
    }

    [Benchmark]
    public ActorPath HitFullPath() => _hitCache.GetOrCompute(_hot[0]);

    private SequenceAudit[] AuditSequences()
    {
        var audits = new SequenceAudit[3];
        for (var sequence = 0; sequence < audits.Length; sequence++)
        {
            var cache = new ActorPathCache();
            if (sequence == 1)
                foreach (var path in _hot)
                    cache.GetOrCompute(path);
            else if (sequence == 2)
                cache.GetOrCompute(_hot[0]);

            // MixedPaths needs four calls per group to traverse the full churn ring.
            var calls = RingSize * AuditCycles * (sequence == 1 ? 4 : 1);
            var hits = 0;
            var hotSelections = 0;
            var offeredCallsByRoot = new int[RootCount];
            for (var n = 0; n < calls; n++)
            {
                string path;
                int root;
                if (sequence == 0)
                {
                    path = _paths[n & RingMask];
                    root = n & (RootCount - 1);
                }
                else if (sequence == 1)
                {
                    var group = n >> 2;
                    root = group & (RootCount - 1);
                    if ((n & 3) == 0)
                        path = _paths[group & RingMask];
                    else
                    {
                        path = _hot[root];
                        hotSelections++;
                    }
                }
                else
                {
                    path = _hot[0];
                    root = 0;
                    hotSelections++;
                }

                offeredCallsByRoot[root]++;
                // TryGet observes membership without advancing the LRU epoch. Only the
                // following GetOrCompute performs the same touch as the benchmark.
                if (cache.TryGet(path, out _))
                    hits++;
                if (cache.GetOrCompute(path) is null)
                    throw new InvalidOperationException("A generated path was not parseable.");
            }

            var name = sequence == 0 ? nameof(ChurnPaths)
                : sequence == 1 ? nameof(MixedPaths) : nameof(HitFullPath);
            audits[sequence] = new SequenceAudit(name, cache.Capacity, cache.EvictAgeThreshold,
                calls, hits, calls - hits, hotSelections, offeredCallsByRoot);
        }
        return audits;
    }

    private sealed record SequenceAudit(string Sequence, int Capacity, int EvictAgeThreshold,
        int Calls, int FullPathHits, int FullPathMisses, int HotSelections, int[] OfferedCallsByRoot)
    {
        public double ObservedHitRate => (double)FullPathHits / Calls;
        public double OfferedHotFraction => (double)HotSelections / Calls;
    }
}
