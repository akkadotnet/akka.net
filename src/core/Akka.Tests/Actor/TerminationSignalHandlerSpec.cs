//-----------------------------------------------------------------------
// <copyright file="TerminationSignalHandlerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Actor.CoordinatedShutdown;

namespace Akka.Tests.Actor;

/// <summary>
/// Tests for the CLR termination signal handling in <see cref="CoordinatedShutdown"/>.
/// </summary>
public class TerminationSignalHandlerSpec : AkkaSpec
{
    public TerminationSignalHandlerSpec(ITestOutputHelper output) : base(output)
    {
    }

    public ExtendedActorSystem ExtSys => Sys.AsInstanceOf<ExtendedActorSystem>();

    private static readonly Phase EmptyPhase = new(ImmutableHashSet<string>.Empty, TimeSpan.FromSeconds(10), true);

    /// <summary>
    /// Test double for <see cref="ITerminationSignalHandler"/> that allows simulating termination signals.
    /// </summary>
    private class TestTerminationSignalHandler : ITerminationSignalHandler
    {
        public Action RegisteredCallback { get; private set; }
        public bool IsDisposed { get; private set; }
        public int RegisterCallCount { get; private set; }

        public void Register(Action onTerminationSignal)
        {
            RegisterCallCount++;
            RegisteredCallback = onTerminationSignal;
        }

        public void SimulateTerminationSignal()
        {
            RegisteredCallback?.Invoke();
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    [Fact(DisplayName = "CoordinatedShutdown should register handler when run-by-clr-shutdown-hook is enabled")]
    public void CoordinatedShutdown_should_register_handler_when_enabled()
    {
        // Arrange
        var phases = new Dictionary<string, Phase> { { "a", EmptyPhase } };
        var coord = new CoordinatedShutdown(ExtSys, phases);
        var testHandler = new TestTerminationSignalHandler();
        var conf = ConfigurationFactory.ParseString("run-by-clr-shutdown-hook = on");

        // Act
        CoordinatedShutdown.InitClrHook(Sys, conf, coord, testHandler);

        // Assert
        testHandler.RegisterCallCount.Should().Be(1);
        testHandler.RegisteredCallback.Should().NotBeNull();
    }

    [Fact(DisplayName = "CoordinatedShutdown should not register handler when run-by-clr-shutdown-hook is disabled")]
    public void CoordinatedShutdown_should_not_register_handler_when_disabled()
    {
        // Arrange
        var phases = new Dictionary<string, Phase> { { "a", EmptyPhase } };
        var coord = new CoordinatedShutdown(ExtSys, phases);
        var testHandler = new TestTerminationSignalHandler();
        var conf = ConfigurationFactory.ParseString("run-by-clr-shutdown-hook = off");

        // Act
        CoordinatedShutdown.InitClrHook(Sys, conf, coord, testHandler);

        // Assert
        testHandler.RegisterCallCount.Should().Be(0);
        testHandler.RegisteredCallback.Should().BeNull();
    }

    [Fact(DisplayName = "CoordinatedShutdown should run shutdown tasks when termination signal is received")]
    public async Task CoordinatedShutdown_should_run_when_termination_signal_received()
    {
        // Arrange
        var sys = ActorSystem.Create(
            "TerminationSignalTest",
            ConfigurationFactory.ParseString(@"
                    akka.coordinated-shutdown.terminate-actor-system = on
                    akka.coordinated-shutdown.run-by-clr-shutdown-hook = on
                    akka.coordinated-shutdown.run-by-actor-system-terminate = off"));

        try
        {
            var testHandler = new TestTerminationSignalHandler();
            var coord = CoordinatedShutdown.Get(sys);

            var taskExecuted = new TaskCompletionSource<bool>();
            coord.AddTask(PhaseBeforeServiceUnbind, "test-task", () =>
            {
                taskExecuted.SetResult(true);
                return Task.FromResult(Done.Instance);
            });

            // Re-initialize with test handler
            var conf = sys.Settings.Config.GetConfig("akka.coordinated-shutdown");
            CoordinatedShutdown.InitClrHook(sys, conf, coord, testHandler);

            // Act - simulate termination signal
            testHandler.SimulateTerminationSignal();

            // Assert
            var result = await taskExecuted.Task.AwaitWithTimeout(TimeSpan.FromSeconds(10));
            result.Should().BeTrue();
            coord.ShutdownReason.Should().Be(ClrExitReason.Instance);
        }
        finally
        {
            await sys.Terminate();
        }
    }

    [Fact(DisplayName = "CoordinatedShutdown should set _runningClrHook flag during CLR shutdown")]
    public async Task CoordinatedShutdown_should_set_running_flag_during_clr_shutdown()
    {
        // Arrange
        var sys = ActorSystem.Create(
            "RunningFlagTest",
            ConfigurationFactory.ParseString(@"
                    akka.coordinated-shutdown.terminate-actor-system = on
                    akka.coordinated-shutdown.run-by-clr-shutdown-hook = on
                    akka.coordinated-shutdown.run-by-actor-system-terminate = off"));

        try
        {
            var testHandler = new TestTerminationSignalHandler();
            var coord = CoordinatedShutdown.Get(sys);

            var flagObserved = new TaskCompletionSource<bool>();
            coord.AddTask(PhaseBeforeServiceUnbind, "flag-check-task", () =>
            {
                // The _runningClrHook flag should be set by now
                // We can't directly access the private field, but we can verify
                // the shutdown is running with ClrExitReason
                flagObserved.SetResult(coord.ShutdownReason == ClrExitReason.Instance);
                return Task.FromResult(Done.Instance);
            });

            var conf = sys.Settings.Config.GetConfig("akka.coordinated-shutdown");
            CoordinatedShutdown.InitClrHook(sys, conf, coord, testHandler);

            // Act
            testHandler.SimulateTerminationSignal();

            // Assert
            var result = await flagObserved.Task.AwaitWithTimeout(TimeSpan.FromSeconds(10));
            result.Should().BeTrue();
        }
        finally
        {
            await sys.Terminate();
        }
    }

    [Fact(DisplayName = "CoordinatedShutdown should dispose handler when ActorSystem terminates normally")]
    public async Task CoordinatedShutdown_should_dispose_handler_on_normal_termination()
    {
        // Arrange
        var sys = ActorSystem.Create(
            "DisposeTest",
            ConfigurationFactory.ParseString(@"
                    akka.coordinated-shutdown.terminate-actor-system = on
                    akka.coordinated-shutdown.run-by-clr-shutdown-hook = on
                    akka.coordinated-shutdown.run-by-actor-system-terminate = on"));

        var testHandler = new TestTerminationSignalHandler();
        var coord = CoordinatedShutdown.Get(sys);
        var conf = sys.Settings.Config.GetConfig("akka.coordinated-shutdown");
        CoordinatedShutdown.InitClrHook(sys, conf, coord, testHandler);

        // Act - terminate system normally (not via signal)
        await sys.Terminate();

        // Give continuation time to run
        await Task.Delay(100);

        // Assert
        testHandler.IsDisposed.Should().BeTrue();
    }

    [Fact(DisplayName = "CoordinatedShutdown CLR hooks should only execute once even if signal fires multiple times")]
    public async Task CoordinatedShutdown_clr_hooks_should_only_execute_once()
    {
        // Arrange
        var sys = ActorSystem.Create(
            "IdempotencyTest",
            ConfigurationFactory.ParseString(@"
                    akka.coordinated-shutdown.terminate-actor-system = on
                    akka.coordinated-shutdown.run-by-clr-shutdown-hook = on
                    akka.coordinated-shutdown.run-by-actor-system-terminate = off"));

        try
        {
            var testHandler = new TestTerminationSignalHandler();
            var coord = CoordinatedShutdown.Get(sys);

            var executionCount = 0;
            coord.AddTask(PhaseBeforeServiceUnbind, "count-task", () =>
            {
                executionCount++;
                return Task.FromResult(Done.Instance);
            });

            var conf = sys.Settings.Config.GetConfig("akka.coordinated-shutdown");
            CoordinatedShutdown.InitClrHook(sys, conf, coord, testHandler);

            // Act - simulate multiple termination signals
            testHandler.SimulateTerminationSignal();
            testHandler.SimulateTerminationSignal();
            testHandler.SimulateTerminationSignal();

            // Wait for shutdown to complete
            await sys.WhenTerminated.AwaitWithTimeout(TimeSpan.FromSeconds(10));

            // Assert - task should only have executed once
            executionCount.Should().Be(1);
        }
        finally
        {
            if (!sys.WhenTerminated.IsCompleted)
                await sys.Terminate();
        }
    }

    [Fact(DisplayName = "CoordinatedShutdown should handle exceptions in shutdown tasks gracefully")]
    public async Task CoordinatedShutdown_should_handle_task_exceptions_gracefully()
    {
        // Arrange
        var sys = ActorSystem.Create(
            "ExceptionTest",
            ConfigurationFactory.ParseString(@"
                    akka.coordinated-shutdown.terminate-actor-system = on
                    akka.coordinated-shutdown.run-by-clr-shutdown-hook = on
                    akka.coordinated-shutdown.run-by-actor-system-terminate = off"));

        try
        {
            var testHandler = new TestTerminationSignalHandler();
            var coord = CoordinatedShutdown.Get(sys);

            var secondTaskExecuted = new TaskCompletionSource<bool>();

            coord.AddTask(PhaseBeforeServiceUnbind, "failing-task", () =>
            {
                throw new Exception("Test exception");
            });

            coord.AddTask(PhaseServiceUnbind, "second-task", () =>
            {
                secondTaskExecuted.SetResult(true);
                return Task.FromResult(Done.Instance);
            });

            var conf = sys.Settings.Config.GetConfig("akka.coordinated-shutdown");
            CoordinatedShutdown.InitClrHook(sys, conf, coord, testHandler);

            // Act
            testHandler.SimulateTerminationSignal();

            // Assert - second task should still execute despite first task throwing
            var result = await secondTaskExecuted.Task.AwaitWithTimeout(TimeSpan.FromSeconds(10));
            result.Should().BeTrue();
        }
        finally
        {
            if (!sys.WhenTerminated.IsCompleted)
                await sys.Terminate();
        }
    }
}