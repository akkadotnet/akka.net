//-----------------------------------------------------------------------
// <copyright file="ScatterGatherFirstCompletedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Routing
{
    public class ScatterGatherFirstCompletedSpec : AkkaSpec
    {
        [Fact]
        public void Scatter_gather_router_must_be_started_when_constructed()
        {
            var routedActor = Sys.ActorOf(Props.Create<TestActor>().WithRouter(new ScatterGatherFirstCompletedPool(1)));
            ((IInternalActorRef)routedActor).IsTerminated.ShouldBe(false);
        }

        [Fact]
        public void Scatter_gather_router_must_deliver_a_broadcast_message_using_tell()
        {
            var doneLatch = new TestLatch(2);
            var counter1 = new AtomicCounter(0);
            var counter2 = new AtomicCounter(0);
            var actor1 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter1)));
            var actor2 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter2)));

            var routedActor = Sys.ActorOf(Props.Create<TestActor>().WithRouter(new ScatterGatherFirstCompletedGroup(TimeSpan.FromSeconds(1), actor1.Path.ToString(), actor2.Path.ToString())));
            routedActor.Tell(new Broadcast(1));
            routedActor.Tell(new Broadcast("end"));

            doneLatch.Ready(TimeSpan.FromSeconds(1));

            counter1.Current.ShouldBe(1);
            counter2.Current.ShouldBe(1);
        }

        [Fact]
        public async Task Scatter_gather_router_must_return_response_even_if_one_of_the_actors_has_stopped()
        {
            var shutdownLatch = new TestLatch(1);
            var actor1 = Sys.ActorOf(Props.Create(() => new StopActor(1)));
            var actor2 = Sys.ActorOf(Props.Create(() => new StopActor(14)));
            var paths = new []{actor1,actor2};
            var routedActor = Sys.ActorOf(new ScatterGatherFirstCompletedGroup(paths, TimeSpan.FromSeconds(3)).Props());

            routedActor.Tell(new Broadcast(new Stop(1)));
            shutdownLatch.Open();

            var res = await routedActor.Ask<int>(0, TimeSpan.FromSeconds(10));
            res.ShouldBe(14);
        }

        [Fact]
        public void Scatter_gather_router_should_only_return_one_response()
        {
            var actor1 = Sys.ActorOf(Props.Create(() => new StopActor(1)));
            var actor2 = Sys.ActorOf(Props.Create(() => new StopActor(14)));

            var paths = new[] { actor1, actor2 };
            var routedActor = Sys.ActorOf(new ScatterGatherFirstCompletedGroup(paths, TimeSpan.FromSeconds(3)).Props());

            routedActor.Tell(0);

            ExpectMsg<int>(TimeSpan.FromSeconds(3));
            ExpectNoMsg(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task Scatter_gather_router_should_handle_failing_timeouts()
        {
            var actor1 = Sys.ActorOf(Props.Create(() => new StopActor(50)));

            var paths = new[] { actor1 };
            var routedActor = Sys.ActorOf(new ScatterGatherFirstCompletedGroup(paths, TimeSpan.FromSeconds(3)).Props());

            var exception = await routedActor.Ask<Status.Failure>(0, TimeSpan.FromSeconds(5));

            exception
                .Should()
                .NotBeNull();

            exception.Cause
                .Should()
                .BeOfType<AskTimeoutException>();
        }

        public new class TestActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {

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

        public class Stop
        {
            public Stop(int? id = null)
            {
                Id = id;
            }

            public int? Id { get; private set; }
        }

        public class StopActor : UntypedActor
        {
            private int _id;
            public StopActor(int id)
            {
                _id = id;
            }
            protected override void OnReceive(object message)
            {
                if (message is Stop)
                {
                    var s = (Stop)message;
                    if (s.Id == null || s.Id == _id)
                    {
                        Context.Stop(Self);
                    }
                }
                else
                {
                    Thread.Sleep(100 * _id);
                    Sender.Tell(_id);
                }
            }
        }
    }
}
