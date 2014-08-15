using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Akka.Tests;
using Akka.Util;
using Xunit;
using System.Threading;

namespace Akka.Tests.Routing
{
    public class ScatterGatherFirstCompletedSpec : AkkaSpec
    {
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

        [Fact]
        public void Scatter_gather_router_must_be_started_when_constructed()
        {
            /*val routedActor = system.actorOf(Props[TestActor].withRouter(
       ScatterGatherFirstCompletedRouter(routees = List(newActor(0)), within = 1 seconds)))
     routedActor.isTerminated should be(false)*/

            var routedActor = sys.ActorOf(Props.Create<TestActor>().WithRouter(new ScatterGatherFirstCompletedPool(1)));
            routedActor.IsTerminated.ShouldBe(false);
        }

        [Fact]
        public void Scatter_gather_router_must_deliver_a_broadcast_message_using_tell()
        {
            var doneLatch = new TestLatch(sys, 2);
            var counter1 = new AtomicCounter(0);
            var counter2 = new AtomicCounter(0);
            var actor1 = sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter1)));
            var actor2 = sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter2)));

            var routedActor = sys.ActorOf(Props.Create<TestActor>().WithRouter(new ScatterGatherFirstCompletedGroup(TimeSpan.FromSeconds(1), actor1.Path.ToString(), actor2.Path.ToString())));
            routedActor.Tell(new Broadcast(1));
            routedActor.Tell(new Broadcast("end"));

            doneLatch.Ready(TimeSpan.FromSeconds(1));

            counter1.Current.ShouldBe(1);
            counter2.Current.ShouldBe(1);

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
                    var s = (Stop) message;
                    if (s.Id == null || s.Id == _id)
                    {
                        Context.Stop(Self);
                    }
                }
                else
                {
                    Thread.Sleep(100*_id);
                    Sender.Tell(_id);
                }
            }
        }

        [Fact]
        public void Scatter_gather_router_must_return_response_even_if_one_of_the_actors_has_stopped()
        {
            var shutdownLatch = new TestLatch(sys,1);
            var actor1 = sys.ActorOf(Props.Create(() => new StopActor(1)));
            var actor2 = sys.ActorOf(Props.Create(() => new StopActor(14)));
            var paths = new []{actor1,actor2};
            var routedActor = sys.ActorOf(new ScatterGatherFirstCompletedGroup(paths, TimeSpan.FromSeconds(3)).Props());

            routedActor.Tell(new Broadcast(new Stop(1)));
            shutdownLatch.Open();
            var res = routedActor.Ask<int>(new Broadcast(0), TimeSpan.FromSeconds(10));
            res.Wait();
            res.Result.ShouldBe(14);
        }
    }
}
