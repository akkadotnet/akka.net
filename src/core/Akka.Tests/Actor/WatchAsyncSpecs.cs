//-----------------------------------------------------------------------
// <copyright file="WatchAsyncSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor
{
    public class WatchAsyncSpecs : AkkaSpec
    {
        [Fact(DisplayName = "WatchAsync should return true when actor is terminated")]
        public async Task WatchAsync_should_return_true_when_actor_is_terminated()
        {
            // arrange
            var actor = Sys.ActorOf(BlackHoleActor.Props);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var terminatedTask = actor.WatchAsync(cts.Token);

            // act
            Sys.Stop(actor);
            var terminated = await terminatedTask;

            // assert
            terminated.Should().BeTrue();
        }

        [Fact(DisplayName = "WatchAsync should return true when called on actor that is already terminated")]
        public async Task WatchAsync_should_return_true_when_actor_is_already_terminated()
        {
            // arrange
            var actor = Sys.ActorOf(BlackHoleActor.Props);
            Watch(actor);
            Sys.Stop(actor);
            await ExpectTerminatedAsync(actor);

            // act

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var terminated = await actor.WatchAsync(cts.Token);

            // assert
            terminated.Should().BeTrue();
        }

        [Fact(DisplayName = "WatchAsync should return false when cancellation token is cancelled")]
        public async Task WatchAsync_should_return_true_when_cancelled()
        {
            // arrange
            var actor = Sys.ActorOf(BlackHoleActor.Props);
            using var cts = new CancellationTokenSource();
            var terminatedTask = actor.WatchAsync(cts.Token);

            // act
            cts.Cancel();
            var terminated = await terminatedTask;

            // assert
            terminated.Should().BeFalse();
        }
    }
}