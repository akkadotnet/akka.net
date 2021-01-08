//-----------------------------------------------------------------------
// <copyright file="RandomSpec.cs" company="Akka.NET Project">
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
    public class RandomSpec : AkkaSpec
    {
        private class HelloWorldActor : ReceiveActor
        {
            private readonly TestLatch _testLatch;

            public HelloWorldActor(TestLatch testLatch)
            {
                _testLatch = testLatch;

                Receive<string>(s => s == "hello", c => Sender.Tell("world"));
            }

            protected override void PostStop()
            {
                _testLatch.CountDown();
            }
        }

        private class RandomActor : ReceiveActor
        {
            private readonly TestLatch _doneLatch;
            private static AtomicCounter _counter;
            private readonly Lazy<int> id = new Lazy<int>(() => _counter.GetAndIncrement());

            public RandomActor(TestLatch doneLatch, AtomicCounter counter)
            {
                _doneLatch = doneLatch;
                _counter = counter;

                Receive<string>(s => s == "hit", c => Sender.Tell(id.Value));
                Receive<string>(s => s == "end", c => _doneLatch.CountDown());
            }
        }

        private class RandomBroadcastActor : ReceiveActor
        {
            private readonly TestLatch _helloLatch;
            private readonly TestLatch _stopLatch;

            public RandomBroadcastActor(TestLatch helloLatch, TestLatch stopLatch)
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

        private class RandomGroupActor : ReceiveActor
        {
            private readonly TestLatch _doneLatch;

            public RandomGroupActor(TestLatch doneLatch)
            {
                this._doneLatch = doneLatch;

                Receive<string>(s => s == "hit", c => Sender.Tell(Self.Path.Name));
                Receive<string>(s => s == "end", c => _doneLatch.CountDown());
            }
        }

        private class RandomLogicActor : ReceiveActor
        {
            public RandomLogicActor()
            {
                var n = 0;
                var router = new Router(new RandomLogic());

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

        private class RandomLogicPropsActor : ReceiveActor
        {
            public RandomLogicPropsActor()
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
        public void Random_pool_must_be_able_to_shut_down_its_instance()
        {
            const int routeeCount = 7;
            var testLatch = new TestLatch(routeeCount);

            var actor = Sys.ActorOf(new RandomPool(routeeCount)
                .Props(Props.Create(() => new HelloWorldActor(testLatch))), "random-shutdown");

            actor.Tell("hello");
            actor.Tell("hello");
            actor.Tell("hello");
            actor.Tell("hello");
            actor.Tell("hello");

            Within(TimeSpan.FromSeconds(2), () => {
                for (int i = 1; i <= 5; i++)
                {
                    ExpectMsg("world");
                }
            });

            Sys.Stop(actor);
            testLatch.Ready(5.Seconds());
        }

        [Fact]
        public async Task Random_pool_must_deliver_messages_in_a_random_fashion()
        {
            const int connectionCount = 10;
            const int iterationCount = 100;
            var doneLatch = new TestLatch(connectionCount);

            var counter = new AtomicCounter(0);
            var replies = new Dictionary<int, int>();
            for (int i = 0; i < connectionCount; i++)
            {
                replies[i] = 0;
            }

            var actor = Sys.ActorOf(new RandomPool(connectionCount)
                .Props(Props.Create(() => new RandomActor(doneLatch, counter))), "random");

            for (int i = 0; i < iterationCount; i++)
            {
                for (int k = 0; k < connectionCount; k++)
                {
                    int id = await actor.Ask<int>("hit");
                    replies[id] = replies[id] + 1;
                }
            }

            counter.Current.ShouldBe(connectionCount);

            actor.Tell(new Broadcast("end"));
            doneLatch.Ready(TimeSpan.FromSeconds(5));

            replies.Values.ForEach(c => c.Should().BeGreaterThan(0));
            replies.Values.Any(c => c != iterationCount).ShouldBeTrue();
            replies.Values.Sum().Should().Be(iterationCount * connectionCount);
        }

        [Fact]
        public void Random_pool_must_deliver_a_broadcast_message_using_the_Tell()
        {
            const int routeeCount = 6;
            var helloLatch = new TestLatch(routeeCount);
            var stopLatch = new TestLatch(routeeCount);

            var actor = Sys.ActorOf(new RandomPool(routeeCount)
                .Props(Props.Create(() => new RandomBroadcastActor(helloLatch, stopLatch))), "random-broadcast");

            actor.Tell(new Broadcast("hello"));
            helloLatch.Ready(5.Seconds());

            Sys.Stop(actor);
            stopLatch.Ready(5.Seconds());
        }

        [Fact]
        public void Random_pool_must_be_controlled_with_management_messages()
        {
            IActorRef actor = Sys.ActorOf(new RandomPool(3)
                .Props(Props.Create<EmptyBehaviorActor>()), "random-managed");

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
        public async Task Random_group_must_deliver_messages_in_a_random_fashion()
        {
            const int connectionCount = 10;
            const int iterationCount = 100;
            var doneLatch = new TestLatch(connectionCount);

            var replies = new Dictionary<string, int>();
            for (int i = 1; i <= connectionCount; i++)
            {
                replies["target-" + i] = 0;
            }

            var paths = Enumerable.Range(1, connectionCount).Select(n =>
            {
                var routee = Sys.ActorOf(Props.Create(() => new RandomGroupActor(doneLatch)), "target-" + n);
                return routee.Path.ToStringWithoutAddress();
            });

            var actor = Sys.ActorOf(new RandomGroup(paths).Props(), "random-group1");

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

            replies.Values.ForEach(c => c.Should().BeGreaterThan(0));
            replies.Values.Any(c => c != iterationCount).ShouldBeTrue();
            replies.Values.Sum().Should().Be(iterationCount * connectionCount);
        }

        [Fact]
        public async Task Random_logic_used_in_actor_must_deliver_messages_in_a_random_fashion()
        {
            const int connectionCount = 10;
            const int iterationCount = 100;

            var replies = new Dictionary<string, int>();
            for (int i = 1; i <= connectionCount; i++)
            {
                replies["child-" + i] = 0;
            }

            var actor = Sys.ActorOf(Props.Create<RandomLogicActor>());
            var childProps = Props.Create<RandomLogicPropsActor>();

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

            replies.Values.ForEach(c => c.Should().BeGreaterThan(0));
            replies.Values.Any(c => c != iterationCount).ShouldBeTrue();
            replies.Values.Sum().Should().Be(iterationCount * connectionCount);
        }
    }
}
