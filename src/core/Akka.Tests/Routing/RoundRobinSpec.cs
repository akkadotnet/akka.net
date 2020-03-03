//-----------------------------------------------------------------------
// <copyright file="RoundRobinSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Routing
{
    public class RoundRobinSpec : AkkaSpec
    {
        private class HelloWorldPoolActor : UntypedActor
        {
            private readonly TestLatch _helloLatch;
            private readonly TestLatch _stopLatch;

            public HelloWorldPoolActor(TestLatch helloLatch, TestLatch stopLatch)
            {
                _helloLatch = helloLatch;
                _stopLatch = stopLatch;
            }

            protected override void OnReceive(object message)
            {
                if (message.Equals("hello"))
                {
                    _helloLatch.CountDown();
                }
            }

            protected override void PostStop()
            {
                _stopLatch.CountDown();
            }
        }

        private class RoundRobinPoolActor : ReceiveActor
        {
            private readonly TestLatch _doneLatch;
            private static AtomicCounter _counter;
            private readonly Lazy<int> id = new Lazy<int>(() => _counter.GetAndIncrement()); 

            public RoundRobinPoolActor(TestLatch doneLatch, AtomicCounter counter)
            {
                _doneLatch = doneLatch;
                _counter = counter;

                Receive<string>(s => s == "hit", c => Sender.Tell(id.Value));
                Receive<string>(s => s == "end", c => _doneLatch.CountDown());
            }
        }

        private class RoundRobinPoolBroadcastActor : ReceiveActor
        {
            private readonly TestLatch _helloLatch;
            private readonly TestLatch _stopLatch;

            public RoundRobinPoolBroadcastActor(TestLatch helloLatch, TestLatch stopLatch)
            {
                _helloLatch = helloLatch;
                _stopLatch = stopLatch;

                Receive<string>(s => s == "hello", c => _helloLatch.CountDown());
            }

            protected override void PostStop()
            {
                _stopLatch.CountDown();
            }
        }

        private class EmptyBehaviorActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
            }
        }

        private class RoundRobinGroupActor : ReceiveActor
        {
            private readonly TestLatch _doneLatch;

            public RoundRobinGroupActor(TestLatch doneLatch)
            {
                _doneLatch = doneLatch;

                Receive<string>(s => s == "hit", c => Sender.Tell(Self.Path.Name));
                Receive<string>(s => s == "end", c => _doneLatch.CountDown());
            }
        }

        private class RoundRobinLogicActor : ReceiveActor
        {
            public RoundRobinLogicActor()
            {
                var n = 0;
                var router = new Router(new RoundRobinRoutingLogic());

                Receive<Props>(p =>
                {
                    n++;
                    var c = Context.ActorOf(p, "child-" + n);
                    Context.Watch(c);
                    router = router.AddRoutee(c);
                });

                Receive<Terminated>(terminated =>
                {
                    router = router.RemoveRoutee(terminated.ActorRef);
                    if (!router.Routees.Any())
                    {
                        Context.Stop(Self);
                    }
                });

                ReceiveAny(other =>
                {
                    router.Route(other, Sender);
                });
            }
        }

        private class RoundRobinLogicPropsActor : ReceiveActor
        {
            public RoundRobinLogicPropsActor()
            {
                Receive<string>(s => s == "hit", c => Sender.Tell(Self.Path.Name));
                Receive<string>(s => s == "end", c => Context.Stop(Self));
            }
        }

        private int RouteeSize(IActorRef router)
        {
            return router.Ask<Routees>(new GetRoutees()).Result.Members.Count();
        }

        [Fact]
        public void Round_robin_pool_must_be_able_to_shut_down_its_instance()
        {
            const int routeeCount = 5;

            var helloLatch = new TestLatch(routeeCount);
            var stopLatch = new TestLatch(routeeCount);

            var actor = Sys.ActorOf(new RoundRobinPool(routeeCount)
                .Props(Props.Create(() => new HelloWorldPoolActor(helloLatch, stopLatch))));

            actor.Tell("hello", TestActor);
            actor.Tell("hello", TestActor);
            actor.Tell("hello", TestActor);
            actor.Tell("hello", TestActor);
            actor.Tell("hello", TestActor);

            helloLatch.Ready(5.Seconds());

            Sys.Stop(actor);
            stopLatch.Ready(5.Seconds());
        }

        [Fact]
        public async Task Round_robin_pool_must_deliver_messages_in_a_round_robin_fashion()
        {
            const int connectionCount = 10;
            const int iterationCount = 10;
            var doneLatch = new TestLatch(connectionCount);

            var counter = new AtomicCounter(0);

            var replies = new Dictionary<int, int>();
            for (int i = 0; i < connectionCount; i++)
            {
                replies[i] = 0;
            }

            var actor = Sys.ActorOf(new RoundRobinPool(connectionCount)
                .Props(Props.Create(() => new RoundRobinPoolActor(doneLatch, counter))), "round-robin");

            for (int i = 0; i < iterationCount; i++)
            {
                for (int k = 0; k < connectionCount; k++)
                {
                    int id = await actor.Ask<int>("hit");
                    replies[id] = replies[id] + 1;
                }
            }

            counter.Current.Should().Be(connectionCount);
            actor.Tell(new Broadcast("end"));
            doneLatch.Ready(5.Seconds());

            replies.Values.ForEach(c => c.Should().Be(iterationCount));
        }

        [Fact]
        public void Round_robin_pool_must_deliver_deliver_a_broadcast_message_using_Tell()
        {
            const int routeeCount = 5;
            var helloLatch = new TestLatch(routeeCount);
            var stopLatch = new TestLatch(routeeCount);

            var actor = Sys.ActorOf(new RoundRobinPool(routeeCount)
                .Props(Props.Create(() => new RoundRobinPoolBroadcastActor(helloLatch, stopLatch))), "round-robin-broadcast");

            actor.Tell(new Broadcast("hello"));
            helloLatch.Ready(5.Seconds());

            Sys.Stop(actor);
            stopLatch.Ready(5.Seconds());
        }

        [Fact]
        public void Round_robin_pool_must_be_controlled_with_management_messages()
        {
            IActorRef actor = Sys.ActorOf(new RoundRobinPool(3)
                .Props(Props.Create<EmptyBehaviorActor>()), "round-robin-managed");

            RouteeSize(actor).Should().Be(3);
            actor.Tell(new AdjustPoolSize(4));
            RouteeSize(actor).Should().Be(7);
            actor.Tell(new AdjustPoolSize(-2));
            RouteeSize(actor).Should().Be(5);

            var other = new ActorSelectionRoutee(Sys.ActorSelection("/user/other"));
            actor.Tell(new AddRoutee(other));
            RouteeSize(actor).Should().Be(6);
            actor.Tell(new RemoveRoutee(other));
            RouteeSize(actor).Should().Be(5);
        }

        [Fact]
        public async Task Round_robin_group_must_deliver_messages_in_a_round_robin_fashion()
        {
            const int connectionCount = 10;
            const int iterationCount = 10;
            var doneLatch = new TestLatch(connectionCount);

            var replies = new Dictionary<string, int>();
            for (int i = 1; i <= connectionCount; i++)
            {
                replies["target-" + i] = 0;
            }

            var paths = Enumerable.Range(1, connectionCount).Select(n =>
            {
                var routee = Sys.ActorOf(Props.Create(() => new RoundRobinGroupActor(doneLatch)), "target-" + n);
                return routee.Path.ToStringWithoutAddress();
            });

            var actor = Sys.ActorOf(new RoundRobinGroup(paths).Props(), "round-robin-group1");

            for (int i = 0; i < iterationCount; i++)
            {
                for (int k = 0; k < connectionCount; k++)
                {
                    string id = await actor.Ask<string>("hit");
                    replies[id] = replies[id] + 1;
                }
            }

            actor.Tell(new Broadcast("end"));
            doneLatch.Ready(5.Seconds());

            replies.Values.ForEach(c => c.Should().Be(iterationCount));
        }

        [Fact]
        public async Task Round_robin_logic_used_in_actor_must_deliver_messages_in_a_round_robin_fashion()
        {
            const int connectionCount = 10;
            const int iterationCount = 10;

            var replies = new Dictionary<string, int>();
            for (int i = 1; i <= connectionCount; i++)
            {
                replies["child-" + i] = 0;
            }

            var actor = Sys.ActorOf(Props.Create<RoundRobinLogicActor>());
            var childProps = Props.Create<RoundRobinLogicPropsActor>();

            Enumerable.Range(1, connectionCount).ForEach(_ => actor.Tell(childProps));

            for (int i = 0; i < iterationCount; i++)
            {
                for (int k = 0; k < connectionCount; k++)
                {
                    string id = await actor.Ask<string>("hit");
                    replies[id] = replies[id] + 1;
                }
            }

            Watch(actor);
            actor.Tell(new Broadcast("end"));
            ExpectTerminated(actor);

            replies.Values.ForEach(c => c.Should().Be(iterationCount));
        }

        // Resolved https://github.com/akkadotnet/akka.net/issues/90
        [Fact]
        public void RoundRobinRoutingLogic_must_not_throw_IndexOutOfRangeException_when_counter_wraps_to_be_negative()
        {
            var routees = new[] { Routee.NoRoutee, Routee.NoRoutee, Routee.NoRoutee };
            var routingLogic = new RoundRobinRoutingLogic(int.MaxValue - 5);
            for (var i = 0; i < 10; i++)
            {
                routingLogic.Select(i, routees);
            }
        }
    }
}
