//-----------------------------------------------------------------------
// <copyright file="ActorRefResolutionBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Remote;
using Akka.Remote.Serialization;
using BenchmarkDotNet.Attributes;
using static Akka.Benchmarks.Configurations.BenchmarkCategories;

namespace Akka.Benchmarks.Actor;

/// <summary>
/// Local reference-resolution costs after the inbound path has been parsed. No actor messages
/// or network traffic are measured. Compare each method across revisions: the Future must
/// consult its current registration, while the stable actor is eligible for the ref cache.
/// </summary>
[Config(typeof(MicroBenchmarkConfig))]
[BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
// BenchmarkDotNet generates a derived harness for this fixture.
public class ActorRefResolutionBenchmarks
{
    private ActorSystem _system = null!;
    private RemoteActorRefProvider _provider = null!;
    private FutureActorRef<object> _future = null!;
    private string _futurePath = null!;
    private string _stablePath = null!;
    private Address _localAddress = null!;

    [GlobalSetup]
    public void Setup()
    {
        _system = ActorSystem.Create("ActorRefResolution", ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.loglevel = WARNING
            """));
        _provider = (RemoteActorRefProvider)((ExtendedActorSystem)_system).Provider;
        _localAddress = _provider.DefaultAddress;
        _future = _provider.CreateFutureRef<object>(
            new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously));
        _provider.RegisterTempActor(_future, _future.Path);
        _futurePath = _future.Path.ToSerializationFormatWithAddress(_localAddress);
        var stable = _system.ActorOf(Props.Empty, "stable");
        _stablePath = stable.Path.ToSerializationFormatWithAddress(_localAddress);

        if (!ReferenceEquals(_provider.ResolveActorRefWithLocalAddress(_futurePath, _localAddress), _future)
            || !ReferenceEquals(_provider.ResolveActorRefWithLocalAddress(_stablePath, _localAddress), stable))
            throw new InvalidOperationException("Resolution must return the registered references.");
        if (ActorRefResolveThreadLocalCache.For(_system).Cache.TryGet(_futurePath, out _))
            throw new InvalidOperationException("The Future must remain excluded from the resolved-ref cache.");
    }

    // Each invocation primes/reads the path cache on its own executing thread through the
    // real inbound entry point; correctness does not depend on the GlobalSetup thread.
    [Benchmark]
    public IActorRef RegisteredFutureRef() =>
        _provider.ResolveActorRefWithLocalAddress(_futurePath, _localAddress);

    [Benchmark]
    public IActorRef StableActorRef() =>
        _provider.ResolveActorRefWithLocalAddress(_stablePath, _localAddress);

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _provider.UnregisterTempActor(_future.Path);
        await _system.Terminate();
    }
}
