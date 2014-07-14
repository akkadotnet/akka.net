using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Akka.Tests;
using Xunit;

namespace Akka.Tests.Routing
{
    
    public class RoutingSpec : AkkaSpec
    {
        protected override string GetConfig()
        {
            return @"
    akka.actor.serialize-messages = off
    akka.actor.debug {  
          receive = on 
          autoreceive = on
          lifecycle = on
          event-stream = on
          unhandled = on
        }
    akka.stdout-loglevel = DEBUG
    akka.actor.deployment {
      /router1 {
        router = round-robin-pool
        nr-of-instances = 3
      }
      /router2 {
        router = round-robin-pool
        nr-of-instances = 3
      }
      /router3 {
        router = round-robin-pool
        nr-of-instances = 0
      }
    }
    ";
        }

        public new class TestActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
               
            }
        }

        public class Echo : UntypedActor
        {

            protected override void OnReceive(object message)
            {
               Sender.Tell(Self);
            }
        }

        [Fact]
        public void Router_in_general_must_evict_terminated_routees()
        {
            var router = sys.ActorOf(new RoundRobinPool(2).Props(Props.Create<Echo>()),"router");
            router.Tell("",testActor);
            router.Tell("",testActor);
            var c1 = expectMsgType<ActorRef>();
            var c2 = expectMsgType<ActorRef>();
            watch(router);
            watch(c2);
            sys.Stop(c2);
            expectTerminated(c2).ExistenceConfirmed.ShouldBe(true);
            // it might take a while until the Router has actually processed the Terminated message
            Task.Delay(100).Wait();
            router.Tell("", testActor);
            router.Tell("", testActor);
            expectMsgType<ActorRef>().ShouldBe(c1);           
            expectMsgType<ActorRef>().ShouldBe(c1);
            sys.Stop(c1);
            expectTerminated(router).ExistenceConfirmed.ShouldBe(true);
        }

        public class TestResizer : Resizer
        {
            private readonly TestLatch _latch;
            public TestResizer(TestLatch latch)
            {
                _latch = latch;
            }
            public override bool IsTimeForResize(long messageCounter)
            {
                return messageCounter == 0;
            }

            public override int Resize(System.Collections.Generic.IEnumerable<Routee> currentRoutees)
            {
                _latch.CountDown();
                return 2;
            }
        }

        [Fact]
        public void Router_in_general_must_not_terminate_when_resizer_is_used()
        {
            var latch = new TestLatch(sys,1);
            var resizer = new TestResizer(latch);
            var router =
                sys.ActorOf(new RoundRobinPool( 0, resizer,SupervisorStrategy.DefaultStrategy,"").Props(Props.Create<TestActor>()));

            watch(router);
            
            latch.Open();
            //Await.ready(latch, remainingOrDefault); //TODO: what is remainingOrDefault
            
            router.Tell(new GetRoutees(),testActor);
            var routees = expectMsgType<Routees>().Members.ToList();

            routees.Count().ShouldBe(2);
            routees.ForEach(r => r.Send(PoisonPill.Instance,testActor));
            // expect no Terminated
            expectNoMsg(TimeSpan.FromSeconds(2));
        }


        public class TestRun
        {
            public TestRun(int id, string[] names, int actors)
            {
                Id = id;
                Names = names;
                Actors = actors;
            }

            public int Actors { get; set; }

            public string[] Names { get; set; }

            public int Id { get; set; }
        }

        public class TestRunActor : UntypedActor
        {

            protected override void OnReceive(object message)
            {
                message.Match()
                    .With<TestRun>(m =>
                    {
            //            val routerProps = Props[TestActor].withRouter(
            //  ScatterGatherFirstCompletedRouter(
            //    routees = names map { context.actorOf(Props(new TestActor), _) },
            //    within = 5 seconds))

            //1 to actors foreach { i ⇒ context.actorOf(routerProps, id + i).tell(CurrentRoutees, testActor) }
                    })
                    .Default(Unhandled);
            }
        }

        [Fact]
        public void Router_in_general_must_be_able_to_send_their_routees()
        {
            var router = sys.ActorOf(new BroadcastPool(5).Props(Props.Create<Echo>()));
            router.Tell("hello",testActor);
            expectMsgType<ActorRef>();
            expectMsgType<ActorRef>();
            expectMsgType<ActorRef>();
            expectMsgType<ActorRef>();
            expectMsgType<ActorRef>();
            expectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Router_in_general_must_use_configured_nr_of_instances_when_FromConfig()
        {
            var router = sys.ActorOf(Props.Create<TestActor>().WithRouter(new FromConfig()), "router1");

            router.Tell(new GetRoutees(),testActor);
            expectMsgType<Routees>().Members.Count().ShouldBe(3);
            watch(router);
            sys.Stop(router);
            expectTerminated(router);
        }

        /*
"use configured nr-of-instances when router is specified" in {
      val router = system.actorOf(Props[TestActor].withRouter(RoundRobinRouter(nrOfInstances = 2)), "router2")
      router ! CurrentRoutees
      expectMsgType[RouterRoutees].routees.size should be(3)
      system.stop(router)
    }
      */

        [Fact]
        public void Router_in_general_must_use_configured_nr_of_instances_when_router_is_specified()
        {
            var router = sys.ActorOf(Props.Create<TestActor>().WithRouter(new RoundRobinPool(3)), "router1");
            router.Tell(new GetRoutees(), testActor);
            expectMsgType<Routees>().Members.Count().ShouldBe(3);
            watch(router);
            sys.Stop(router);
            expectTerminated(router);
        }

/*
     "use specified resizer when resizer not configured" in {
      val latch = TestLatch(1)
      val resizer = new Resizer {
        def isTimeForResize(messageCounter: Long): Boolean = messageCounter == 0
        def resize(currentRoutees: immutable.IndexedSeq[Routee]): Int = {
          latch.countDown()
          3
        }
      }
      val router = system.actorOf(Props[TestActor].withRouter(RoundRobinRouter(resizer = Some(resizer))), "router3")
      Await.ready(latch, remaining)
      router ! CurrentRoutees
      expectMsgType[RouterRoutees].routees.size should be(3)
      system.stop(router)
    }
 */


        [Fact]
        public void Router_in_general_mulst_use_specified_resizer_when_resizer_not_configured()
        {
            var latch = new TestLatch(sys,1);
            var resizer = new TestResizer(latch);
            var router =
                sys.ActorOf(
                    Props.Create<TestActor>()
                        .WithRouter(new RoundRobinPool(0, resizer, SupervisorStrategy.DefaultStrategy, "")));
            latch.Open();
            router.Tell(new GetRoutees(),testActor);
            expectMsgType<Routees>().Members.Count().ShouldBe(2);
            sys.Stop(router);

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


        public class BroadcastTarget : UntypedActor
        {
            private AtomicInteger _counter;
            private TestLatch _latch;
            public BroadcastTarget(TestLatch latch,AtomicInteger counter)
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
        public void Scatter_gather_router_must_deliver_a_broadcast_message_using_tell()
        {
            var doneLatch = new TestLatch(sys,2);
            var counter1 = new AtomicInteger(0);
            var counter2 = new AtomicInteger(0);
            var actor1 = sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter1)));
            var actor2 = sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter2)));

            var routedActor = sys.ActorOf(Props.Create<TestActor>().WithRouter(new ScatterGatherFirstCompletedGroup(TimeSpan.FromSeconds(1),actor1.Path.ToString(),actor2.Path.ToString())));
            routedActor.Tell(new Broadcast(1));
            routedActor.Tell(new Broadcast("end"));
         
            doneLatch.Ready(TimeSpan.FromSeconds(1));

            counter1.Value.ShouldBe(1);
            counter2.Value.ShouldBe(1);

        }
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
            var router = sys.ActorOf(new SmallestMailboxPool(3).Props(Props.Create(() => new SmallestMailboxActor(usedActors))));

            var busy = new TestLatch(sys, 1);
            var received0 = new TestLatch(sys, 1);
            router.Tell(Tuple.Create(busy, received0));
            received0.Ready(TestLatch.DefaultTimeout);

            var received1 = new TestLatch(sys, 1);
            router.Tell(Tuple.Create(1, received1));
            received1.Ready(TestLatch.DefaultTimeout);

            var received2 = new TestLatch(sys, 1);
            router.Tell(Tuple.Create(2, received2));
            received2.Ready(TestLatch.DefaultTimeout);

            var received3 = new TestLatch(sys, 1);
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
    }
}