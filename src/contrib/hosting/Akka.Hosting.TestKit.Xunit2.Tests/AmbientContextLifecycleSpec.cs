// -----------------------------------------------------------------------
// <copyright file="AmbientContextLifecycleSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Hosting.TestKit.Tests;

public sealed class AmbientContextLifecycleSpec(ITestOutputHelper output)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact(DisplayName = "Should_revoke_captured_sender_after_host_disposal")]
    public async Task Should_RevokeCapturedSender_AfterHostDisposal()
    {
        var kit = new LifecycleKit(output);
        await kit.InitializeAsync().WaitAsync(Deadline);
        await kit.AssertOwnSenderAsync();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retained = Task.Run(async () =>
        {
            await release.Task.WaitAsync(Deadline);
            return (Cell: InternalCurrentActorCellKeeper.Current, Sender: ActorCell.GetCurrentSelfOrNoSender());
        });
        await kit.DisposeAsync().WaitAsync(Deadline);
        release.SetResult(true);
        var observed = await retained.WaitAsync(Deadline);
        Assert.Null(observed.Cell);
        Assert.Null(observed.Sender);
    }

    [Fact(DisplayName = "Should_revoke_sender_and_terminate_system_after_failed_async_startup")]
    public async Task Should_RevokeSenderAndTerminateSystem_AfterFailedAsyncStartup()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("expected-startup-failure");
        var kit = new LifecycleKit(output, gate.Task, failure);
        var initialization = kit.InitializeAsync();
        await kit.Started.Task.WaitAsync(Deadline);
        Assert.False(initialization.IsCompleted);
        var captured = ExecutionContext.Capture();
        Assert.NotNull(captured);
        gate.SetResult(true);
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => initialization.WaitAsync(Deadline));
        Assert.Same(failure, observed);
        await kit.System!.WhenTerminated.WaitAsync(Deadline);
        ExecutionContext.Run(captured, _ =>
        {
            Assert.Null(InternalCurrentActorCellKeeper.Current);
            Assert.Null(ActorCell.GetCurrentSelfOrNoSender());
        }, null);
    }

    [Fact(DisplayName = "Should_revoke_captured_sender_when_host_disposal_throws")]
    public async Task Should_RevokeCapturedSender_WhenHostDisposalThrows()
    {
        var kit = new LifecycleKit(output, disposalFailure: new InvalidOperationException("expected-disposal-failure"));
        await kit.InitializeAsync().WaitAsync(Deadline);
        var captured = ExecutionContext.Capture();
        Assert.NotNull(captured);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => kit.DisposeAsync().WaitAsync(Deadline));
        Assert.Contains("expected-disposal-failure", failure.ToString());
        await kit.System!.WhenTerminated.WaitAsync(Deadline);
        ExecutionContext.Run(captured, _ => Assert.Null(ActorCell.GetCurrentSelfOrNoSender()), null);
    }

    [Fact(DisplayName = "Should_use_recreated_TestActor_as_logical_sender_without_an_actor_cell")]
    public async Task Should_UseRecreatedTestActor_AsLogicalSender()
    {
        var kit = new LifecycleKit(output);
        try
        {
            await kit.InitializeAsync().WaitAsync(Deadline);
            var previous = kit.TestActor;
            var recreation = kit.ForceReinitializeTestActorAsync();
            Assert.Null(InternalCurrentActorCellKeeper.Current);
            await recreation.WaitAsync(Deadline);
            Assert.NotEqual(previous, kit.TestActor);
            await kit.AssertOwnSenderAsync();
            Assert.Null(InternalCurrentActorCellKeeper.Current);
        }
        finally
        {
            await kit.DisposeAsync().WaitAsync(Deadline);
        }
    }

    [Fact(DisplayName = "Should_preserve_parent_sender_after_nested_host_disposal")]
    public async Task Should_PreserveParentSender_AfterNestedHostDisposal()
    {
        var parent = new LifecycleKit(output);
        try
        {
            await parent.InitializeAsync().WaitAsync(Deadline);
            var child = new LifecycleKit(output);
            await child.InitializeAsync().WaitAsync(Deadline);
            await child.AssertOwnSenderAsync();
            await child.DisposeAsync().WaitAsync(Deadline);
            await parent.AssertOwnSenderAsync();
            Assert.Null(InternalCurrentActorCellKeeper.Current);
        }
        finally
        {
            await parent.DisposeAsync().WaitAsync(Deadline);
        }
    }

    [Fact(DisplayName = "Should_restore_parent_only_after_explicit_cleanup_of_failed_nested_host")]
    public async Task Should_RestoreParent_AfterExplicitCleanupOfFailedNestedHost()
    {
        var parent = new LifecycleKit(output);
        try
        {
            await parent.InitializeAsync().WaitAsync(Deadline);
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var child = new LifecycleKit(output, gate.Task, new InvalidOperationException("expected-child-startup-failure"));
            var initialization = child.InitializeAsync();
            await child.Started.Task.WaitAsync(Deadline);
            gate.SetResult(true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => initialization.WaitAsync(Deadline));
            Assert.Null(ActorCell.GetCurrentSelfOrNoSender());
            await child.DisposeAsync().WaitAsync(Deadline);
            await parent.AssertOwnSenderAsync();
        }
        finally
        {
            await parent.DisposeAsync().WaitAsync(Deadline);
        }
    }

    [Fact(DisplayName = "Should_stop_started_system_when_failed_initialization_also_fails_to_dispose_a_service")]
    public async Task Should_StopStartedSystem_WhenInitializationAndServiceDisposalFail()
    {
        var expected = new InvalidOperationException("expected-before-test-failure");
        var kit = new LifecycleKit(output,
            disposalFailure: new InvalidOperationException("expected-service-disposal-failure"),
            beforeStartFailure: expected);
        try
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => kit.InitializeAsync().WaitAsync(Deadline));
            Assert.Same(expected, failure);
            Assert.Contains("expected-service-disposal-failure", failure.Data["HostingTestKitCleanupException"]?.ToString());
            await kit.System!.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            if (kit.System is { } system)
                await system.Terminate().WaitAsync(Deadline);
        }
    }

    [Fact(DisplayName = "Should_preserve_initialization_failure_when_a_stop_service_ignores_cancellation")]
    public async Task Should_PreserveInitializationFailure_WhenStopIgnoresCancellation()
    {
        var expected = new InvalidOperationException("expected-initialization-failure-with-blocked-stop");
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var kit = new LifecycleKit(output, beforeStartFailure: expected) { StopRelease = release };
        try
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => kit.InitializeAsync().WaitAsync(Deadline));
            Assert.Same(expected, failure);
            Assert.True(kit.StopEntered.Task.IsCompletedSuccessfully);
            Assert.IsAssignableFrom<OperationCanceledException>(failure.Data["HostingTestKitStopException"]);
            await kit.System!.WhenTerminated.WaitAsync(Deadline);
        }
        finally
        {
            release.TrySetResult(true);
            if (kit.System is { } system)
                await system.Terminate().WaitAsync(Deadline);
        }
    }

    [Fact(DisplayName = "Should_preserve_asynchronous_stop_failure_alongside_original_initialization_failure")]
    public async Task Should_PreserveStopFailure_AlongsideOriginalInitializationFailure()
    {
        var expected = new InvalidOperationException("expected-initialization-failure-before-stop-fault");
        var stopFailure = new InvalidOperationException("expected-asynchronous-stop-failure");
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var kit = new LifecycleKit(output, beforeStartFailure: expected) { StopRelease = release };
        try
        {
            var initialization = kit.InitializeAsync();
            await kit.StopEntered.Task.WaitAsync(Deadline);
            Assert.False(initialization.IsCompleted);
            release.SetException(stopFailure);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => initialization.WaitAsync(Deadline));
            Assert.Same(expected, failure);
            Assert.Contains("expected-asynchronous-stop-failure", failure.Data["HostingTestKitStopException"]?.ToString());
        }
        finally
        {
            release.TrySetResult(true);
            if (kit.System is { } system)
                await system.Terminate().WaitAsync(Deadline);
        }
    }

    private sealed class LifecycleKit : TestKit
    {
        private readonly Task? _startupGate;
        private readonly Exception? _startupFailure;
        private readonly Exception? _disposalFailure;
        private readonly Exception? _beforeStartFailure;
        private IActorRef _reporter = ActorRefs.Nobody;

        public LifecycleKit(ITestOutputHelper output, Task? startupGate = null,
            Exception? startupFailure = null, Exception? disposalFailure = null, Exception? beforeStartFailure = null)
            : base($"lifecycle-{Guid.NewGuid():N}", output)
        {
            _startupGate = startupGate;
            _startupFailure = startupFailure;
            _disposalFailure = disposalFailure;
            _beforeStartFailure = beforeStartFailure;
        }

        public ActorSystem? System { get; private set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool>? StopRelease { get; init; }
        public TaskCompletionSource<bool> StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            if (_disposalFailure is { } failure)
                services.AddSingleton(_ => new DisposalFailure(failure));
            if (StopRelease is { } release)
                services.AddSingleton<IHostedService>(_ => new BlockedStop(release.Task, StopEntered));
        }

        protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
        {
            if (_disposalFailure is not null && _beforeStartFailure is null)
                _ = provider.GetRequiredService<DisposalFailure>();
            builder.WithActors((system, _) =>
            {
                Assert.Null(InternalCurrentActorCellKeeper.Current);
                System = system;
                _reporter = system.ActorOf(Props.Create(() => new SenderReporter()));
            });
            builder.AddStartup(async (_, _) =>
            {
                Started.SetResult(true);
                if (_startupGate is { } gate)
                    await gate;
                if (_startupFailure is { } failure)
                    throw failure;
            });
        }

        protected override Task BeforeTestStart()
        {
            if (_beforeStartFailure is { } failure)
            {
                if (_disposalFailure is not null)
                    _ = Host.Services.GetRequiredService<DisposalFailure>();
                throw failure;
            }
            return Task.CompletedTask;
        }

        public async Task AssertOwnSenderAsync()
        {
            var observation = new TaskCompletionSource<IActorRef>(TaskCreationOptions.RunContinuationsAsynchronously);
            _reporter.Tell(observation);
            Assert.Equal(TestActor, await observation.Task.WaitAsync(Deadline));
        }

        private sealed class SenderReporter : ReceiveActor
        {
            public SenderReporter()
            {
                Receive<TaskCompletionSource<IActorRef>>(observation => observation.SetResult(Sender));
            }
        }

        private sealed class DisposalFailure(Exception failure) : IDisposable
        {
            public void Dispose() => throw failure;
        }

        private sealed class BlockedStop(Task release, TaskCompletionSource<bool> entered) : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken)
            {
                entered.TrySetResult(true);
                return release;
            }
        }
    }
}
