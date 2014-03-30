using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Akka.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Tests.Routing
{
    [TestClass]
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

        [TestMethod]
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

        [TestMethod]
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
            routees.ForEach(r => r.Send(new PoisonPill(),testActor));
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

        [TestMethod]
        public void Router_in_general_must_be_able_to_send_their_routees()
        {
            
        }

        [TestMethod]
        public void Router_in_general_mulst_use_configured_nr_of_instances_when_FromConfig()
        {
            //TODO: make FromConfig copy values from config
            var router = sys.ActorOf(Props.Create<TestActor>().WithRouter(new FromConfig()), "router1");

            router.Tell(new GetRoutees());
            expectMsgType<Routees>().Members.Count().ShouldBe(3);
            watch(router);
            sys.Stop(router);
            expectTerminated(router);
        }
    }
}