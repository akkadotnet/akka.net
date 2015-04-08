//-----------------------------------------------------------------------
// <copyright file="TailChoppingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Routing
{
    public class TailChoppingSpec : AkkaSpec
    {
        private TestActor testActor;

        private ActorSystem actorSystem;

        class TailChopTestActor : UntypedActor
        {
            private int timesResponded;

            private int sleepTime;

            public TailChopTestActor(int sleepTime)
            {
                this.sleepTime = sleepTime;
            }

            protected override void OnReceive(object message)
            {
                var command = message as string;
                switch (command)
                {
                    case "stop":
                        Context.Stop(Self);
                        break;
                    case "times":
                        Sender.Tell(timesResponded);
                        break;
                    default:
                        Thread.Sleep(sleepTime);
                        Sender.Tell("ack");
                        timesResponded++;
                        break;
                }
            }
        }

        public class BroadcastTarget : UntypedActor
        {
            private AtomicCounter _counter;
            private TestLatch _latch;
            public BroadcastTarget(TestLatch latch, AtomicCounter counter)
            {
                _latch = latch;
                _counter = counter;
            }
            protected override void OnReceive(object message)
            {
                if (message is string)
                {
                    var s = (string)message;
                    if (s == "end")
                    {
                        _latch.CountDown();
                    }
                }
                if (message is int)
                {
                    var i = (int)message;
                    _counter.GetAndAdd(i);
                }
            }
        }

        public new class TestActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {

            }
        }

        public Func<Func<IActorRef, int>, bool> OneOfShouldEqual(int what, IEnumerable<IActorRef> actors)
        {
            return func =>
            {
                var results = actors.Select(x => func(x));
                return (results.Any(x => x == what));
            };
        }

        public Func<Func<IActorRef, int>, bool> AllShouldEqual(int what, IEnumerable<IActorRef> actors)
        {
            return func =>
            {
                var results = actors.Select(x => func(x));
                return (results.All(x => x == what));
            };
        }

        [Fact]
        public void Tail_chopping_router_must_deliver_a_broadcast_message_using_tell()
        {
            var doneLatch = new TestLatch(2);
            var counter1 = new AtomicCounter(0);
            var counter2 = new AtomicCounter(0);

            var actor1 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter1)), "Actor1");
            var actor2 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter2)), "Actor2");

            var routedActor = Sys.ActorOf(Props.Create<TestActor>()
                .WithRouter(new TailChoppingGroup(new string[] { actor1.Path.ToString(), actor2.Path.ToString() }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100))
            ));

            routedActor.Tell(new Broadcast(1));
            routedActor.Tell(new Broadcast("end"));

            doneLatch.Ready(TimeSpan.FromSeconds(1));

            counter1.Current.ShouldBe(1);
            counter2.Current.ShouldBe(1);
        }

        [Fact]
        public void Tail_chopping_router_must_return_response_from_second_actor_after_inactivity_from_first_one()
        {
            var actor1 = Sys.ActorOf(Props.Create(() => new TailChopTestActor(1000)), "Actor3");
            var actor2 = Sys.ActorOf(Props.Create(() => new TailChopTestActor(100)), "Actor4");

            var probe = CreateTestProbe();
            var routedActor = Sys.ActorOf(Props.Create<TestActor>()
                .WithRouter(new TailChoppingGroup(new string[] { actor1.Path.ToString(), actor2.Path.ToString() }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50))
            ));

            probe.Send(routedActor, "");
            probe.ExpectMsg("ack");

            var actorList = new List<IActorRef> { actor1, actor2 };
            Assert.True(OneOfShouldEqual(1, actorList)((x => (int)x.Ask("times").Result)));

            routedActor.Tell(new Broadcast("stop"));
        }

        [Fact]
        public void Tail_chopping_router_must_throw_exception_if_no_result_will_arrive_within_the_given_time()
        {
            var actor1 = Sys.ActorOf(Props.Create(() => new TailChopTestActor(500)), "Actor5");
            var actor2 = Sys.ActorOf(Props.Create(() => new TailChopTestActor(500)), "Actor6");

            var probe = CreateTestProbe();
            var routedActor = Sys.ActorOf(Props.Create<TestActor>()
                .WithRouter(new TailChoppingGroup(new string[] { actor1.Path.ToString(), actor2.Path.ToString() }, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(50))
            ));

            probe.Send(routedActor, "");
            probe.ExpectMsg<Status.Failure>();

            var actorList = new List<IActorRef> { actor1, actor2 };
            Assert.True(AllShouldEqual(1, actorList)((x => (int)x.Ask("times").Result)));

            routedActor.Tell(new Broadcast("stop"));
        }
    }
}
