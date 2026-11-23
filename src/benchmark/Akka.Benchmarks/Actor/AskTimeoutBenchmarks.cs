#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Actor;

[MemoryDiagnoser]
public class AskTimeoutBenchmarks
{
    private static readonly object Request = new();
    private static readonly TimeSpan FiniteTimeout = TimeSpan.FromSeconds(10);
    private ActorSystem _system = null!;
    private IActorRef _target = null!;

    private sealed class Echo : ReceiveActor
    {
        public Echo() => ReceiveAny(m => Sender.Tell(m));
    }

    [GlobalSetup]
    public void Setup()
    {
        _system = ActorSystem.Create("AskTimeout");
        _target = _system.ActorOf(Props.Create<Echo>());
    }

    [Benchmark(Baseline = true)]
    public async Task<object> AskFiniteTimeout() => await _target.Ask<object>(Request, FiniteTimeout);

    [Benchmark]
    public async Task<object> AskInfiniteTimeout() => await _target.Ask<object>(Request, Timeout.InfiniteTimeSpan);

    [GlobalCleanup]
    public async Task Cleanup() => await _system.Terminate();
}
