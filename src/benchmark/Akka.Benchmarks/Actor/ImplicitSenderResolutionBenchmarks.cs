// -----------------------------------------------------------------------
// <copyright file="ImplicitSenderResolutionBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Actor;

[MemoryDiagnoser]
public class ImplicitSenderResolutionBenchmarks
{
    private ActorSystem? _system;
    private IActorRef? _expected;
    private ActorCell? _previous;

    [Params(false, true)]
    public bool InsideActor { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _previous = InternalCurrentActorCellKeeper.Current;
        ActorCell? cell = null;
        if (InsideActor)
        {
            _system = ActorSystem.Create("sender-benchmark");
            cell = (ActorCell)((ActorRefWithCell)((ActorSystemImpl)_system).Provider.Guardian).Underlying;
            _expected = cell.Self;
        }
        InternalCurrentActorCellKeeper.Current = cell;
    }

    [Benchmark]
    public IActorRef? Resolve()
    {
        var sender = ActorCell.GetCurrentSelfOrNoSender();
        if (!ReferenceEquals(sender, _expected))
            throw new InvalidOperationException("The measured lookup used an unexpected actor context.");
        return sender;
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        InternalCurrentActorCellKeeper.Current = _previous;
        if (_system is { } system)
            await system.Terminate();
    }
}
