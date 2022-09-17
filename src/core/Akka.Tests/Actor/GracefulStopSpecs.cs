//-----------------------------------------------------------------------
// <copyright file="GracefulStopSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;
using FluentAssertions;

namespace Akka.Tests.Actor
{
    public class GracefulStopSpecs : AkkaSpec
    {
        [Fact(DisplayName = "GracefulStop should terminate target actor on-time")]
        public async Task GracefulStopShouldTerminateOnTime()
        {
            // arrange
            var actor = Sys.ActorOf(BlackHoleActor.Props);
            Watch(actor);

            // act
            var stopped = await actor.GracefulStop(TimeSpan.FromSeconds(3));
            await ExpectTerminatedAsync(actor);

            // assert
            stopped.Should().BeTrue();
           
        }
        
        private class CustomShutdown{}

        [Fact(DisplayName = "GracefulStop should return false if shutdown goes overtime", Skip = "GracefulStop currently throws a TaskCancellationException, which seems wrong")]
        public async Task GracefulStopShouldThrowIfShutdownGoesOvertime()
        {
            // arrange
            var actor = Sys.ActorOf(act => act.ReceiveAsync<CustomShutdown>(async (a, ctx) =>
            {
                await Task.Delay(200);
                ctx.Stop(ctx.Self);
            }));
            
            // act
            var stopped = await actor.GracefulStop(TimeSpan.FromMilliseconds(10), new CustomShutdown());
            
            // assert
            stopped.Should().BeFalse();
        }
    }
}