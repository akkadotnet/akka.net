﻿// -----------------------------------------------------------------------
//  <copyright file="ActorCellSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor;

public class ActorCellSpec : AkkaSpec
{
    [Fact]
    public async Task Cell_should_clear_current_message_after_receive()
    {
        // arrange
        var actor = Sys.ActorOf(Props.Create(() => new DummyActor()));

        // act
        await actor.Ask<string>("hello", RemainingOrDefault);

        // assert
        var refCell = (ActorRefWithCell)actor;
        //wait while current message is not null (that is, receive is not yet completed/exited)

        AwaitCondition(() => refCell.Underlying is ActorCell { CurrentMessage: null });
    }

    [Fact]
    public async Task Cell_should_clear_current_message_after_async_receive()
    {
        // arrange
        var actor = Sys.ActorOf(Props.Create(() => new DummyAsyncActor()));

        // act
        await actor.Ask<string>("hello", RemainingOrDefault);

        // assert

        var refCell = (ActorRefWithCell)actor;
        //wait while current message is not null (that is, receive is not yet completed/exited)

        AwaitCondition(() => refCell.Underlying is ActorCell { CurrentMessage: null });
    }

    public class DummyActor : ReceiveActor
    {
        public DummyActor()
        {
            ReceiveAny(m => Sender.Tell(m));
        }
    }

    public class DummyAsyncActor : ReceiveActor
    {
        public DummyAsyncActor()
        {
            ReceiveAsync<string>(async m =>
            {
                await Task.Delay(5);
                Sender.Tell(m);
            });
        }
    }
}