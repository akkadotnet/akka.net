#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Actor;

[MemoryDiagnoser]
public class AskMessageEntryBenchmarks
{
    private static readonly object Request = new();
    private static readonly System.Func<IActorRef, object> Factory = static _ => Request;
    private ActorSystem _system = null!;
    private IActorRef _target = null!;

    private sealed class Responder : ReceiveActor
    {
        public Responder() => ReceiveAny(_ => Sender.Tell("reply"));
    }

    [GlobalSetup]
    public void Setup()
    {
        _system = ActorSystem.Create("AskMessageEntry");
        _target = _system.ActorOf(Props.Create<Responder>());
    }

    // Both methods use ordinary Ask tasks, temporary registration and delivery. This is
    // inclusive request/reply allocation, not transport-only or cleanup-exclusive work.
    [Benchmark(Baseline = true)]
    public Task<string> DirectMessage() => _target.Ask<string>(Request, Timeout.InfiniteTimeSpan, CancellationToken.None);

    [Benchmark]
    public Task<string> CachedFactory() => _target.Ask<string>(Factory, Timeout.InfiniteTimeSpan, CancellationToken.None);

    [GlobalCleanup]
    public async Task Cleanup() => await _system.Terminate();
}
