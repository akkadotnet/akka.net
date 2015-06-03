//-----------------------------------------------------------------------
// <copyright file="SmallestMailboxSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

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
                    .With<Tuple<TestLatch, TestLatch>>(t =>
                    {
                        TestLatch busy = t.Item1, receivedLatch = t.Item2;
                        usedActors.TryAdd(0, Self.Path.ToString());
                        Self.Tell("another in busy mailbox");
                        receivedLatch.CountDown();
                        busy.Ready(TestLatch.DefaultTimeout);
                    })
                    .With<Tuple<int, TestLatch>>(t =>
                    {
                        var msg = t.Item1; var receivedLatch = t.Item2;
                        usedActors.TryAdd(msg, Self.Path.ToString());
                        receivedLatch.CountDown();
                    })
                    .With<string>(t => { });
            }
        }

        [Fact]
        public void Smallest_mailbox_router_must_deliver_messages_to_idle_actor()
        {
            var usedActors = new ConcurrentDictionary<int, string>();
            var router = Sys.ActorOf(new SmallestMailboxPool(3).Props(Props.Create(() => new SmallestMailboxActor(usedActors))));

            var busy = new TestLatch(1);
            var received0 = new TestLatch(1);
            router.Tell(Tuple.Create(busy, received0));
            received0.Ready(TestLatch.DefaultTimeout);

            var received1 = new TestLatch(1);
            router.Tell(Tuple.Create(1, received1));
            received1.Ready(TestLatch.DefaultTimeout);

            var received2 = new TestLatch(1);
            router.Tell(Tuple.Create(2, received2));
            received2.Ready(TestLatch.DefaultTimeout);

            var received3 = new TestLatch(1);
            router.Tell(Tuple.Create(3, received3));
            received3.Ready(TestLatch.DefaultTimeout);

            busy.CountDown();

            var busyPath = usedActors[0];
            Assert.NotEqual(busyPath, null);

            Assert.Equal(usedActors.Count, 4);
            var path1 = usedActors[1];
            var path2 = usedActors[2];
            var path3 = usedActors[3];

            Assert.NotEqual(path1, busyPath);
            Assert.NotEqual(path2, busyPath);
            Assert.NotEqual(path3, busyPath);
        }

        [Fact]
        public void SmallestMail_should_not_throw_IndexOutOfRangeException_when_counter_wraps_to_be_negative()
        {
            var routees = new[] {Routee.NoRoutee, Routee.NoRoutee, Routee.NoRoutee};
            var routingLogic = new SmallestMailboxRoutingLogic(int.MaxValue - 5);
            for (var i = 0; i < 10; i++)
            {
                routingLogic.Select(i, routees);
            }
        }
    }
}

