//-----------------------------------------------------------------------
// <copyright file="ShutdownAsyncTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.Extensions;
using FluentAssertions;
using Xunit;

namespace Akka.TestKit.Tests.TestKitBaseTests
{
    public class ShutdownAsyncTests : AkkaSpec
    {
        private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

        [Fact(DisplayName = "ShutdownAsync should terminate the system before the returned task completes")]
        public async Task Should_TerminateSystem_When_Awaited()
        {
            var sys = ActorSystem.Create("shutdown-async-completes", Sys.Settings.Config);

            await ShutdownAsync(sys);

            sys.WhenTerminated.IsCompleted.Should().BeTrue(
                "ShutdownAsync must not complete before the system has terminated");
        }

        [Fact(DisplayName = "ShutdownAsync should throw TimeoutException when verifySystemShutdown is true and the system will not stop")]
        public async Task Should_Throw_When_SystemDoesNotStop_And_VerifyIsSet()
        {
            var release = NewRelease();
            var sys = CreateStuckSystem("shutdown-async-throws", release);

            try
            {
                var ex = await Assert.ThrowsAsync<TimeoutException>(
                    () => ShutdownAsync(sys, ShortTimeout, verifySystemShutdown: true));

                ex.Message.Should().Contain("Failed to stop [shutdown-async-throws]");
            }
            finally
            {
                await ReleaseAndCleanUp(sys, release);
            }
        }

        [Fact(DisplayName = "ShutdownAsync should log instead of throwing when verifySystemShutdown is not set")]
        public async Task Should_Log_When_SystemDoesNotStop_And_VerifyIsNotSet()
        {
            var release = NewRelease();
            var sys = CreateStuckSystem("shutdown-async-logs", release);
            var probe = CreateTestProbe();
            sys.EventStream.Subscribe(probe.Ref, typeof(Warning));

            try
            {
                await ShutdownAsync(sys, ShortTimeout);

                await probe.FishForMessageAsync<Warning>(
                    w => w.Message.ToString().Contains("Failed to stop [shutdown-async-logs]"),
                    TimeSpan.FromSeconds(3));
            }
            finally
            {
                await ReleaseAndCleanUp(sys, release);
            }
        }

        private static TaskCompletionSource<Done> NewRelease()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Builds a system whose coordinated shutdown parks on a task we control, so
        /// <see cref="ActorSystem.Terminate"/> cannot finish until the test releases it.
        /// </summary>
        private ActorSystem CreateStuckSystem(string name, TaskCompletionSource<Done> release)
        {
            var sys = ActorSystem.Create(name, Sys.Settings.Config);
            CoordinatedShutdown.Get(sys).AddTask(
                CoordinatedShutdown.PhaseBeforeActorSystemTerminate,
                "block-until-released",
                () => release.Task);
            return sys;
        }

        private static async Task ReleaseAndCleanUp(ActorSystem sys, TaskCompletionSource<Done> release)
        {
            release.TrySetResult(Done.Instance);
            await sys.Terminate().AwaitWithTimeout(CleanupTimeout);
        }
    }
}
