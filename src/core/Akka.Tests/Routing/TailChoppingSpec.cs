//-----------------------------------------------------------------------
// <copyright file="TailChoppingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using FluentAssertions;

namespace Akka.Tests.Routing
{
    public class TailChoppingSpec : AkkaSpec
    {
        class TailChopTestActor : ReceiveActor
        {
            private readonly TimeSpan _sleepTime;
            private int _times;

            public TailChopTestActor(TimeSpan sleepTime)
            {
                _sleepTime = sleepTime;

                Receive<string>(command =>
                {
                    switch (command)
                    {
                        case "stop":
                            Context.Stop(Self);
                            break;
                        case "times":
                            Sender.Tell(_times);
                            break;
                        default:
                            _times++;
                            Thread.Sleep(_sleepTime);
                            Sender.Tell("ack");
                            break;
                    }
                });
            }
        }

        private class BroadcastTarget : ReceiveActor
        {
            private readonly AtomicCounter _counter;
            private readonly TestLatch _doneLatch;

            public BroadcastTarget(TestLatch doneLatch, AtomicCounter counter)
            {
                _doneLatch = doneLatch;
                _counter = counter;

                Receive<string>(s => s == "end", c => _doneLatch.CountDown());
                Receive<int>(msg => _counter.AddAndGet(msg));
            }
        }

        public Func<Func<IActorRef, int>, bool> OneOfShouldEqual(int what, IEnumerable<IActorRef> actors)
        {
            return func =>
            {
                var results = actors.Select(func);
                return results.Any(x => x == what);
            };
        }

        public Func<Func<IActorRef, int>, bool> AllShouldEqual(int what, IEnumerable<IActorRef> actors)
        {
            return func =>
            {
                var results = actors.Select(func);
                return results.All(x => x == what);
            };
        }

        [Fact]
        public void Tail_chopping_group_router_must_deliver_a_broadcast_message_using_tell()
        {
            var doneLatch = new TestLatch(2);

            var counter1 = new AtomicCounter(0);
            var actor1 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter1)));

            var counter2 = new AtomicCounter(0);
            var actor2 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter2)));

            var paths = new List<string> { actor1.Path.ToString(), actor2.Path.ToString() };
            var routedActor = Sys.ActorOf(new TailChoppingGroup(paths, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100)).Props());

            routedActor.Tell(new Broadcast(1));
            routedActor.Tell(new Broadcast("end"));

            doneLatch.Ready(TestKitSettings.DefaultTimeout);

            counter1.Current.Should().Be(1);
            counter2.Current.Should().Be(1);
        }

        [Fact]
        public void Tail_chopping_group_router_must_return_response_from_second_actor_after_inactivity_from_first_one()
        {
            var actor1 = Sys.ActorOf(Props.Create(() => new TailChopTestActor(1.Milliseconds())), "Actor1");
            var actor2 = Sys.ActorOf(Props.Create(() => new TailChopTestActor(1.Milliseconds())), "Actor2");

            var probe = CreateTestProbe();
            var paths = new List<string> { actor1.Path.ToString(), actor2.Path.ToString() };
            var routedActor = Sys.ActorOf(new TailChoppingGroup(paths, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50)).Props());

            probe.Send(routedActor, "");
            probe.ExpectMsg("ack");

            var actorList = new List<IActorRef> { actor1, actor2 };
            OneOfShouldEqual(1, actorList)(x => (int)x.Ask("times").Result).Should().BeTrue();

            routedActor.Tell(new Broadcast("stop"));
        }

        [Fact(Skip = "Skip until fix from https://github.com/akkadotnet/akka.net/pull/3790 merged")]
        public void Tail_chopping_group_router_must_throw_exception_if_no_result_will_arrive_within_the_given_time()
        {
            var actor1 = Sys.ActorOf(Props.Create(() => new TailChopTestActor(1500.Milliseconds())), "Actor3");
            var actor2 = Sys.ActorOf(Props.Create(() => new TailChopTestActor(1500.Milliseconds())), "Actor4");

            var probe = CreateTestProbe();
            var paths = new List<string> { actor1.Path.ToString(), actor2.Path.ToString() };
            var routedActor = Sys.ActorOf(new TailChoppingGroup(paths, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(50)).Props());

            probe.Send(routedActor, "");
            var failure = probe.ExpectMsg<Status.Failure>();
            failure.Cause.Should().BeOfType<AskTimeoutException>();

            var actorList = new List<IActorRef> { actor1, actor2 };
            AllShouldEqual(1, actorList)(x => (int) x.Ask("times").Result).Should().BeTrue(); ;

            routedActor.Tell(new Broadcast("stop"));
        }

        [Fact]
        public void Tail_chopping_group_router_must_reply_ASAP()
        {
            var actor1 = Sys.ActorOf(Props.Create(() => new TailChopTestActor(1000.Milliseconds())), "Actor5");
            var actor2 = Sys.ActorOf(Props.Create(() => new TailChopTestActor(4000.Milliseconds())), "Actor6");

            var probe = CreateTestProbe();
            var paths = new List<string> { actor1.Path.ToString(), actor2.Path.ToString() };
            var routedActor = Sys.ActorOf(new TailChoppingGroup(paths, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100)).Props());

            probe.Send(routedActor, "");
            probe.ExpectMsg("ack", 2.Seconds());

            routedActor.Tell(new Broadcast("stop"));
        }
    }
}
