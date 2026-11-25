//-----------------------------------------------------------------------
// <copyright file="BoundedMailboxProgressSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.Tests.Dispatch;

public sealed class BoundedMailboxProgressSpec : AkkaSpec
{
    public BoundedMailboxProgressSpec(ITestOutputHelper output) : base("""
        single-slot {
            mailbox-type = "Akka.Dispatch.BoundedMailbox, Akka"
            mailbox-capacity = 1
            mailbox-push-timeout-time = 2s
        }
        """, output)
    {
    }

    [Fact(DisplayName = "A bounded mailbox can drain while the sending actor is making its second Tell")]
    public async Task Should_deliver_both_messages_when_an_actor_sends_to_a_capacity_one_mailbox()
    {
        var target = Sys.ActorOf(ForwardActor.Props(TestActor).WithMailbox("single-slot"));
        var producer = Sys.ActorOf(dsl => dsl.Receive<int>((sequence, _) =>
        {
            target.Tell("first");
            target.Tell(sequence);
        }));
        Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));

        // Repeat across mailbox runs so cold actor startup and JIT do not mask the fast scheduling path.
        for (var sequence = 1; sequence <= 16; sequence++)
        {
            producer.Tell(sequence);
            for (var received = 0; received < 2; received++)
            {
                var outcome = await ExpectMsgAsync<object>(TimeSpan.FromSeconds(10));
                Assert.False(outcome is DeadLetter, $"Expected delivery to the bounded mailbox, observed {outcome}");
                Assert.Equal(received == 0 ? (object)"first" : sequence, outcome);
            }
        }
    }
}
