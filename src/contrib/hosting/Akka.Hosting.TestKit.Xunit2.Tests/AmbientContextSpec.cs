// -----------------------------------------------------------------------
// <copyright file="AmbientContextSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Hosting.TestKit.Regression.Tests;

public abstract class AmbientFixture : TestKit
{
    protected static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private IActorRef _reporter = ActorRefs.Nobody;
    protected IActorRef ReportingActor => _reporter;
    protected IActorRef SystemDeadLetters { get; private set; } = ActorRefs.Nobody;

    protected AmbientFixture(ITestOutputHelper output)
        : base(actorSystemName: $"ambient-{Guid.NewGuid():N}", output: output)
    {
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.WithActors((system, _) =>
        {
            _reporter = system.ActorOf(Props.Create(() => new SenderReporter()));
            SystemDeadLetters = system.DeadLetters;
        });
    }

    protected sealed record AmbientState(ActorCell? Cell, SynchronizationContext? Context, int ThreadId)
    {
        public static AmbientState Capture() => new(
            InternalCurrentActorCellKeeper.Current,
            SynchronizationContext.Current,
            Environment.CurrentManagedThreadId);

        public override string ToString() =>
            $"thread={ThreadId}, cell={Cell?.Self.ToString() ?? "null"}, context={Context?.GetType().FullName ?? "null"}";
    }

    protected sealed record Sample(string Id, AmbientState State, Task<IActorRef> Sender);
    private sealed record Report(string Id, TaskCompletionSource<IActorRef> Observation, bool Echo);

    protected Sample Send(bool echo = false)
    {
        var observation = new TaskCompletionSource<IActorRef>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Guid.NewGuid().ToString("N");
        var state = AmbientState.Capture();
        _reporter.Tell(new Report(id, observation, echo));
        return new Sample(id, state, observation.Task);
    }

    protected Sample SendExplicit(IActorRef? sender)
    {
        var observation = new TaskCompletionSource<IActorRef>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Guid.NewGuid().ToString("N");
        var state = AmbientState.Capture();
        _reporter.Tell(new Report(id, observation, false), sender);
        return new Sample(id, state, observation.Task);
    }

    protected async Task AssertSenderAsync(Sample sample, IActorRef expected)
    {
        var actual = await sample.Sender.WaitAsync(Deadline);
        Output!.WriteLine($"sample={sample.Id}, expected={expected}, observed={actual}, {sample.State}");
        Assert.Equal(expected, actual);
    }

    private sealed class SenderReporter : ReceiveActor
    {
        public SenderReporter()
        {
            Receive<Report>(report =>
            {
                report.Observation.SetResult(Sender);
                if (report.Echo)
                    Sender.Tell(report.Id, Self);
            });
        }
    }
}

public sealed class RunnerSenderSpec(ITestOutputHelper output) : AmbientFixture(output)
{
    [Fact(DisplayName = "Should_use_own_TestActor_in_first_test_body_segment")]
    public async Task Should_UseOwnTestActor_InFirstTestBodySegment()
    {
        var sample = Send(echo: true);
        await AssertSenderAsync(sample, TestActor);
        await ExpectMsgAsync(sample.Id, Deadline);
    }

    [Fact(DisplayName = "Should_use_own_TestActor_after_yield_without_getter_reseeding")]
    public async Task Should_UseOwnTestActor_AfterYield()
    {
        await Task.Yield();
        var sample = Send();
        await AssertSenderAsync(sample, TestActor);
    }

    [Fact(DisplayName = "Should_honor_explicit_senders_after_yield")]
    public async Task Should_HonorExplicitSenders_AfterYield()
    {
        await Task.Yield();
        await AssertSenderAsync(SendExplicit(TestActor), TestActor);
        await AssertSenderAsync(SendExplicit(ActorRefs.NoSender), SystemDeadLetters);
    }
}

public sealed class GetterIsolationSpec(ITestOutputHelper output) : AmbientFixture(output)
{
    [Theory(DisplayName = "Should_preserve_clean_worker_state_when_reading_hosting_getter")]
    [InlineData("Sys")]
    [InlineData("Host")]
    public async Task Should_PreserveCleanWorkerState_WhenReadingGetter(string getter)
    {
        await using var worker = new DedicatedWorker();
        var first = await worker.InvokeAsync(() =>
        {
            var before = AmbientState.Capture();
            ReadGetter(getter);
            return (Before: before, After: AmbientState.Capture());
        });
        var next = await worker.InvokeAsync(() => Send());
        Output!.WriteLine($"getter={getter}, before=({first.Before}), after=({first.After}), next=({next.State}), sample={next.Id}");
        var actual = await next.Sender.WaitAsync(Deadline);
        Output.WriteLine($"sample={next.Id}, observed={actual}, expected={SystemDeadLetters}");
        Assert.Null(first.Before.Cell);
        Assert.Null(first.Before.Context);
        Assert.Equal(first.Before.ThreadId, first.After.ThreadId);
        Assert.Equal(first.Before.ThreadId, next.State.ThreadId);
        Assert.Equal(SystemDeadLetters, actual);
        Assert.Equal(first.Before.Cell, first.After.Cell);
        Assert.Equal(first.Before.Context, first.After.Context);
        Assert.Null(next.State.Cell);
    }

    [Theory(DisplayName = "Should_preserve_existing_worker_state_when_reading_hosting_getter")]
    [InlineData("Sys")]
    [InlineData("Host")]
    public async Task Should_PreserveExistingWorkerState_WhenReadingGetter(string getter)
    {
        await using var worker = new DedicatedWorker();
        await AssertSenderAsync(SendExplicit(TestActor), TestActor);
        var sentinel = (ActorCell)((ActorRefWithCell)ReportingActor).Underlying;
        Assert.NotEqual(TestActor, sentinel.Self);
        var sentinelContext = new SynchronizationContext();
        try
        {
            var sample = await worker.InvokeAsync(() =>
            {
                InternalCurrentActorCellKeeper.Current = sentinel;
                SynchronizationContext.SetSynchronizationContext(sentinelContext);
                var before = AmbientState.Capture();
                ReadGetter(getter);
                return (Before: before, After: Send());
            });
            var next = await worker.InvokeAsync(() => Send());
            await AssertSenderAsync(sample.After, sentinel.Self);
            await AssertSenderAsync(next, sentinel.Self);
            Assert.Same(sentinel, sample.Before.Cell);
            Assert.Same(sentinel, sample.After.State.Cell);
            Assert.Same(sentinelContext, sample.After.State.Context);
            Assert.Same(sentinel, next.State.Cell);
            Assert.Same(sentinelContext, next.State.Context);
            Assert.Equal(sample.Before.ThreadId, sample.After.State.ThreadId);
            Assert.Equal(sample.Before.ThreadId, next.State.ThreadId);
        }
        finally
        {
            await worker.InvokeAsync(() =>
            {
                InternalCurrentActorCellKeeper.Current = null;
                SynchronizationContext.SetSynchronizationContext(null);
                return true;
            });
        }
    }

    [Theory(DisplayName = "Should_leave_TaskRun_worker_clean_after_hosting_getter")]
    [InlineData("Sys")]
    [InlineData("Host")]
    public async Task Should_LeaveTaskRunWorkerClean_AfterGetter(string getter)
    {
        var states = await Task.Run(() =>
        {
            var before = AmbientState.Capture();
            ReadGetter(getter);
            return (Before: before, After: AmbientState.Capture());
        });
        Output!.WriteLine($"getter={getter}, before=({states.Before}), after=({states.After})");
        Assert.Null(states.Before.Cell);
        Assert.Null(states.Before.Context);
        Assert.Equal(states.Before.ThreadId, states.After.ThreadId);
        Assert.Equal(states.Before.Cell, states.After.Cell);
        Assert.Equal(states.Before.Context, states.After.Context);
    }

    private void ReadGetter(string getter)
    {
        if (getter == "Sys")
            _ = Sys;
        else
            _ = Host;
    }

    private sealed class DedicatedWorker : IAsyncDisposable
    {
        private readonly BlockingCollection<Action> _work = new();
        private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DedicatedWorker()
        {
            using (ExecutionContext.SuppressFlow())
            {
                new Thread(Run) { IsBackground = true }.Start();
            }
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _work.Add(() =>
            {
                try
                {
                    completion.SetResult(action());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
            return completion.Task.WaitAsync(Deadline);
        }

        private void Run()
        {
            foreach (var action in _work.GetConsumingEnumerable())
                action();
            _stopped.SetResult(true);
        }

        public async ValueTask DisposeAsync()
        {
            _work.CompleteAdding();
            await _stopped.Task.WaitAsync(Deadline);
            _work.Dispose();
        }
    }
}

public sealed class NoImplicitSenderSpec(ITestOutputHelper output) : AmbientFixture(output), INoImplicitSender
{
    [Fact(DisplayName = "Should_use_DeadLetters_without_implicit_sender_before_and_after_yield")]
    public async Task Should_UseDeadLetters_BeforeAndAfterYield()
    {
        var first = Send();
        await AssertSenderAsync(first, SystemDeadLetters);
        await Task.Yield();
        await AssertSenderAsync(Send(), SystemDeadLetters);
    }
}

internal static class Overlap
{
    private static readonly ConcurrentDictionary<string, bool> Participants = new();
    private static readonly TaskCompletionSource<bool> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static async Task ArriveAsync(string system, ITestOutputHelper output)
    {
        Assert.True(Participants.TryAdd(system, true));
        output.WriteLine($"overlap arrival={system}, participants={Participants.Count}");
        if (Participants.Count == 2)
            Ready.TrySetResult(true);
        try
        {
            await Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException exception)
        {
            var failure = new InvalidOperationException("OVERLAP_PREREQUISITE_FAILED: both test classes must arrive", exception);
            Ready.TrySetException(failure);
            throw failure;
        }
        Assert.Equal(2, Participants.Count);
        output.WriteLine($"overlap verified={system}, participants={Participants.Count}");
    }
}

public abstract class ParallelSenderFixture(ITestOutputHelper output) : AmbientFixture(output)
{
    [Fact(DisplayName = "Should_use_own_TestActor_when_test_classes_overlap")]
    public async Task Should_UseOwnTestActor_WhenClassesOverlap()
    {
        var first = Send();
        await Overlap.ArriveAsync(ActorSystemName, Output!);
        await Task.Yield();
        var next = Send();
        await AssertSenderAsync(first, TestActor);
        await AssertSenderAsync(next, TestActor);
    }
}

public sealed class ParallelSenderASpec(ITestOutputHelper output) : ParallelSenderFixture(output);
public sealed class ParallelSenderBSpec(ITestOutputHelper output) : ParallelSenderFixture(output);
