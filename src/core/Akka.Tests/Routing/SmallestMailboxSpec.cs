//-----------------------------------------------------------------------
// <copyright file="SmallestMailboxSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Xunit;
using FluentAssertions;

namespace Akka.Tests.Routing
{
    public class SmallestMailboxSpec : AkkaSpec
    {
        public class SmallestMailboxActor : UntypedActor
        {
            private ConcurrentDictionary<int, string> usedActors;

            public SmallestMailboxActor(ConcurrentDictionary<int, string> usedActors)
            {
                this.usedActors = usedActors;
            }

            protected override void OnReceive(object message)
            {
                message.Match()
                    .With<(TestLatch, TestLatch)>(t =>
                    {
                        TestLatch busy = t.Item1, receivedLatch = t.Item2;
                        usedActors.TryAdd(0, Self.Path.ToString());
                        Self.Tell("another in busy mailbox");
                        receivedLatch.CountDown();
                        busy.Ready(TestLatch.DefaultTimeout);
                    })
                    .With<(int, TestLatch)>(t =>
                    {
                        var msg = t.Item1; var receivedLatch = t.Item2;
                        usedActors.TryAdd(msg, Self.Path.ToString());
                        receivedLatch.CountDown();
                    })
                    .With<string>(t => { });
            }
        }

        [Fact]
        public void Smallest_mailbox_pool_must_deliver_messages_to_idle_actor()
        {
            var usedActors = new ConcurrentDictionary<int, string>();
            var router = Sys.ActorOf(new SmallestMailboxPool(3).Props(Props.Create(() => new SmallestMailboxActor(usedActors))));

            var busy = new TestLatch(1);
            var received0 = new TestLatch(1);
            router.Tell((busy, received0));
            received0.Ready(TestKitSettings.DefaultTimeout);

            var received1 = new TestLatch(1);
            router.Tell((1, received1));
            received1.Ready(TestKitSettings.DefaultTimeout);

            var received2 = new TestLatch(1);
            router.Tell((2, received2));
            received2.Ready(TestKitSettings.DefaultTimeout);

            var received3 = new TestLatch(1);
            router.Tell((3, received3));
            received3.Ready(TestKitSettings.DefaultTimeout);

            busy.CountDown();

            var busyPath = usedActors[0];
            busyPath.Should().NotBeNull();

            var path1 = usedActors[1];
            var path2 = usedActors[2];
            var path3 = usedActors[3];

            path1.Should().NotBeNull(busyPath);
            path2.Should().NotBeNull(busyPath);
            path3.Should().NotBeNull(busyPath);
        }

        // Resolved https://github.com/akkadotnet/akka.net/issues/90
        [Fact]
        public void SmallestMailboxRoutingLogic_must_not_throw_IndexOutOfRangeException_when_counter_wraps_to_be_negative()
        {
            var routees = new[] { Routee.NoRoutee, Routee.NoRoutee, Routee.NoRoutee };
            var routingLogic = new SmallestMailboxRoutingLogic(int.MaxValue - 5);
            for (var i = 0; i < 10; i++)
            {
                routingLogic.Select(i, routees);
            }
        }
    }
}
