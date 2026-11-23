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
    private IActorRef _asker = null!;

    private sealed class Echo : ReceiveActor
    {
        public Echo() => ReceiveAny(m => Sender.Tell(m));
    }

    private sealed class AskFromReceiveAsync : ReceiveActor
    {
        public AskFromReceiveAsync(IActorRef target)
        {
            ReceiveAsync<TaskCompletionSource<object>>(async done =>
                done.TrySetResult(await target.Ask<object>(Request, FiniteTimeout)));
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        _system = ActorSystem.Create("AskTimeout");
        _target = _system.ActorOf(Props.Create<Echo>());
        _asker = _system.ActorOf(Props.Create(() => new AskFromReceiveAsync(_target)));
    }

    [Benchmark(Baseline = true)]
    public async Task<object> AskFiniteTimeout() => await _target.Ask<object>(Request, FiniteTimeout);

    [Benchmark]
    public async Task<object> AskInfiniteTimeout() => await _target.Ask<object>(Request, Timeout.InfiniteTimeSpan);

    [Benchmark]
    public Task<object> AskInsideReceiveAsync()
    {
        var done = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        _asker.Tell(done);
        return done.Task;
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _system.Terminate();
}
