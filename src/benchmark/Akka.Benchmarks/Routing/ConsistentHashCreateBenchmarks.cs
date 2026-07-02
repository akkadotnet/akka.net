// -----------------------------------------------------------------------
//  <copyright file="ConsistentHashCreateBenchmarks.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Linq;
using Akka.Benchmarks.Configurations;
using Akka.Routing;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Routing;

/// <summary>
/// Benchmarks <see cref="ConsistentHash.Create{T}"/> ring construction across node counts and
/// virtual-node factors. This is the code path changed for #8031 (canonical sort + linear-probe
/// collision handling); it runs once per membership change, not per message. <see cref="NodeFor"/>
/// is the per-message hot path and is included to confirm it did not regress.
/// </summary>
[Config(typeof(MicroBenchmarkConfig))]
public class ConsistentHashCreateBenchmarks
{
    [Params(10, 100, 1000, 5000)]
    public int NodeCount;

    [Params(3, 5, 10)]
    public int VirtualNodesFactor;

    private string[] _nodes;
    private string[] _lookupKeys;
    private ConsistentHash<string> _ring;
    private int _keyIndex;

    [GlobalSetup]
    public void Setup()
    {
        // Node strings shaped like full actor paths, which is what the router hashes in practice.
        _nodes = Enumerable.Range(0, NodeCount)
            .Select(i => "akka.tcp://sys@host:8080/user/router/routee-" + i)
            .ToArray();
        _ring = ConsistentHash.Create(_nodes, VirtualNodesFactor);
        _lookupKeys = Enumerable.Range(0, 1024).Select(i => "session-" + i).ToArray();
    }

    [Benchmark]
    public ConsistentHash<string> Create() => ConsistentHash.Create(_nodes, VirtualNodesFactor);

    [Benchmark]
    public string NodeFor()
    {
        _keyIndex = (_keyIndex + 1) & 1023;
        return _ring.NodeFor(_lookupKeys[_keyIndex]);
    }
}