// -----------------------------------------------------------------------
// <copyright file="MultiNodeRestartStartupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Xunit;

namespace Akka.Remote.TestKit.Tests;

public sealed class MultiNodeRestartStartupSpec : Akka.TestKit.Xunit.TestKit
{
    public MultiNodeRestartStartupSpec(ITestOutputHelper output) : base(output: output) { }

    private static ActorSystem CreateSystem() => ActorSystem.Create("RestartStartup", ConfigurationFactory.ParseString("""
        akka.actor.provider = remote
        akka.remote.artery.enabled = on
        akka.remote.artery.canonical.hostname = "127.0.0.1"
        akka.remote.artery.canonical.port = 0
        """));

    [Theory(DisplayName = "Failed multi-node replacement initialization terminates its new actor system and preserves the failure")]
    [InlineData("sync")]
    [InlineData("async")]
    [InlineData("cancel")]
    public async Task Should_TerminateReplacement_When_InitializationFails(string mode)
    {
        var system = CreateSystem();
        var failure = new InvalidOperationException("initialization failed");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Task Initialize(ActorSystem _) => mode switch
        {
            "sync" => throw failure,
            "async" => Task.FromException(failure),
            _ => Task.FromCanceled(cancelled.Token)
        };
        try
        {
            var observed = await Record.ExceptionAsync(() => MultiNodeSpec.InitializeRestartedSystemAsync(
                system, Initialize, TimeSpan.FromSeconds(5)));
            Assert.NotNull(observed);
            if (mode == "cancel")
                Assert.IsAssignableFrom<OperationCanceledException>(observed);
            else
                Assert.Same(failure, observed);
            Assert.True(system.WhenTerminated.IsCompletedSuccessfully,
                "failure must not escape while the replacement actor system is still running");
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "A real conductor bind failure closes the replacement system")]
    public async Task Should_TerminateReplacement_When_ConductorPortIsOccupied()
    {
        using var occupied = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        occupied.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        occupied.Listen(1);
        var endpoint = (IPEndPoint)occupied.LocalEndPoint!;
        var system = CreateSystem();
        try
        {
            await Assert.ThrowsAsync<ConductorBindException>(() => MultiNodeSpec.InitializeRestartedSystemAsync(system,
                async replacement => await TestConductor.Get(replacement).StartControllerAsync(1, new RoleName("node"), endpoint),
                TimeSpan.FromSeconds(5)));
            Assert.True(system.WhenTerminated.IsCompletedSuccessfully);
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Successful replacement initialization returns the same running actor system")]
    public async Task Should_KeepReplacementRunning_When_InitializationSucceeds()
    {
        var system = CreateSystem();
        try
        {
            var initialized = await MultiNodeSpec.InitializeRestartedSystemAsync(system,
                async replacement => await TestConductor.Get(replacement).StartControllerAsync(1, new RoleName("node"),
                    new IPEndPoint(IPAddress.Loopback, 0)), TimeSpan.FromSeconds(5));
            Assert.Same(system, initialized);
            Assert.False(system.WhenTerminated.IsCompleted);
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Bounded replacement cleanup retains both initialization and cleanup failures")]
    public async Task Should_PreserveBothFailures_When_ReplacementShutdownCannotFinish()
    {
        var system = CreateSystem();
        var release = new TaskCompletionSource<global::Akka.Done>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("initialization failed");
        CoordinatedShutdown.Get(system).AddTask(CoordinatedShutdown.PhaseBeforeServiceUnbind, "hold-cleanup", () => release.Task);
        try
        {
            var observed = await Assert.ThrowsAsync<AggregateException>(() => MultiNodeSpec.InitializeRestartedSystemAsync(
                system, _ => Task.FromException(failure), TimeSpan.Zero));
            Assert.Same(failure, observed.InnerExceptions[0]);
            Assert.IsType<TimeoutException>(observed.InnerExceptions[1]);
            Assert.False(system.WhenTerminated.IsCompleted);
        }
        finally
        {
            release.TrySetResult(global::Akka.Done.Instance);
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
