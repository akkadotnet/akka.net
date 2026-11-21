#nullable enable

using Akka.Actor;
using Akka.Remote.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Remoting;

[MemoryDiagnoser]
public class ActorPathRootKeyBenchmarks
{
    private ActorPathCache _missCache = null!;
    private ActorPathCache _hitCache = null!;
    private string[] _paths = null!;
    private int _next;

    [Params(1, 2, 32)]
    public int RootCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _missCache = new ActorPathCache(16, 8);
        _hitCache = new ActorPathCache(16, 8);
        _paths = new string[256];
        for (var i = 0; i < _paths.Length; i++)
            _paths[i] = $"akka://system@127.0.0.1:{2552 + i % RootCount}/temp/path-{i}#{i}";
        _hitCache.GetOrCompute(_paths[0]);
    }

    // Rotating preallocated inputs exceed LRU capacity. RootCount exposes the last-key
    // hit and mixed-root cases without charging input formatting to the measured operation.
    [Benchmark]
    public ActorPath ChurnPaths() => _missCache.GetOrCompute(_paths[_next++ & 255]);

    [Benchmark]
    public ActorPath HitFullPath() => _hitCache.GetOrCompute(_paths[0]);

    // Includes cache construction and first-root cost; it is distinct from the warm memo case.
    [Benchmark]
    public ActorPath ColdCache() => new ActorPathCache(16, 8).GetOrCompute(_paths[0]);
}
