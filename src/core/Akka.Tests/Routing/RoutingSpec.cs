//-----------------------------------------------------------------------
// <copyright file="RoutingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Routing
{
    
    public class RoutingSpec : AkkaSpec
    {
        public RoutingSpec() :base(GetConfig())   
        {
        }

        private static string GetConfig()
        {
            return @"
    akka.actor.serialize-messages = off
#    akka.actor.debug {  
#          receive = on 
#          autoreceive = on
#          lifecycle = on
#          event-stream = on
#          unhandled = on
#        }
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
            var router = Sys.ActorOf(new RoundRobinPool(2).Props(Props.Create<Echo>()), "router");
            router.Tell("", TestActor);
            router.Tell("", TestActor);

            var c1 = ExpectMsg<IActorRef>();
            var c2 = ExpectMsg<IActorRef>();

            Watch(router);
            Watch(c2);

            c2.Tell(PoisonPill.Instance);

            ExpectMsg<Terminated>();

            AwaitCondition(() => ((RoutedActorRef) router).Children.Count() == 1);

            router.Tell("", TestActor);
            var msg1 = ExpectMsg<IActorRef>();
            msg1.ShouldBe(c1);

            router.Tell("", TestActor);
            var msg2 = ExpectMsg<IActorRef>();
            msg2.ShouldBe(c1);
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
            var latch = new TestLatch(1);
            var resizer = new TestResizer(latch);
            var router =
                Sys.ActorOf(new RoundRobinPool( 0, resizer,SupervisorStrategy.DefaultStrategy,null).Props(Props.Create<BlackHoleActor>()));

            Watch(router);

            latch.Open();
            //Await.ready(latch, remainingOrDefault); //TODO: what is remainingOrDefault
            
            router.Tell(new GetRoutees(),TestActor);
            var routees = ExpectMsg<Routees>().Members.ToList();

            routees.Count().ShouldBe(2);
            routees.ForEach(r => r.Send(PoisonPill.Instance,TestActor));
            // expect no Terminated
            ExpectNoMsg(TimeSpan.FromSeconds(2));
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
            var router = Sys.ActorOf(new BroadcastPool(5).Props(Props.Create<Echo>()));
            router.Tell("hello",TestActor);
            ExpectMsg<IActorRef>();
            ExpectMsg<IActorRef>();
            ExpectMsg<IActorRef>();
            ExpectMsg<IActorRef>();
            ExpectMsg<IActorRef>();
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Router_in_general_must_use_configured_nr_of_instances_when_FromConfig()
        {
            var router = Sys.ActorOf(Props.Create<BlackHoleActor>().WithRouter(FromConfig.Instance), "router1");

            router.Tell(new GetRoutees(),TestActor);
            ExpectMsg<Routees>().Members.Count().ShouldBe(3);
            Watch(router);
            Sys.Stop(router);
            ExpectTerminated(router);
        }

        [Fact]
        public void Router_in_general_must_not_use_configured_nr_of_instances_when_not_FromConfig()
        {
            var router = Sys.ActorOf(Props.Create<BlackHoleActor>(), "router1");

            router.Tell(new GetRoutees(), TestActor);
            ExpectNoMsg();
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
            var router = Sys.ActorOf(Props.Create<BlackHoleActor>().WithRouter(new RoundRobinPool(3)), "router1");
            router.Tell(new GetRoutees(), TestActor);
            ExpectMsg<Routees>().Members.Count().ShouldBe(3);
            Watch(router);
            Sys.Stop(router);
            ExpectTerminated(router);
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
        public void Router_in_general_must_use_specified_resizer_when_resizer_not_configured()
        {
            var latch = new TestLatch(1);
            var resizer = new TestResizer(latch);
            var router =
                Sys.ActorOf(
                    Props.Create<BlackHoleActor>()
                        .WithRouter(new RoundRobinPool(0, resizer, SupervisorStrategy.DefaultStrategy, null)));
            latch.Open();
            router.Tell(new GetRoutees(),TestActor);
            ExpectMsg<Routees>().Members.Count().ShouldBe(2);
            Sys.Stop(router);

        }             

        [Fact]
        public void Router_AddRoute_should_not_add_same_routee()
        {
            var router = new Router(new RoundRobinRoutingLogic(), TestActor);

            var updatedRouter = router.AddRoutee(TestActor);
            updatedRouter.Routees.Count().ShouldBe(1);
            updatedRouter.Routees.First().AsInstanceOf<ActorRefRoutee>().Actor.ShouldBe(TestActor);
        }


        [Fact]
        public void Router_AddRoute_should_add_new_routee()
        {
            var router = new Router(new RoundRobinRoutingLogic(), TestActor);
            var blackHole = ActorOf<BlackHoleActor>();
            var updatedRouter = router.AddRoutee(blackHole);
            updatedRouter.Routees.Count().ShouldBe(2);
            updatedRouter.Routees.Cast<ActorRefRoutee>().Any(r => ReferenceEquals(r.Actor, TestActor)).ShouldBe(true);
            updatedRouter.Routees.Cast<ActorRefRoutee>().Any(r => ReferenceEquals(r.Actor, blackHole)).ShouldBe(true);
        }


        [Fact]
        public void Router_RemoveRoute_should_remove_existing_routee_and_leave_the_rest()
        {
            var blackHole1 = ActorOf<BlackHoleActor>();
            var blackHole2 = ActorOf<BlackHoleActor>();
            var router = new Router(new RoundRobinRoutingLogic(), TestActor, blackHole1, blackHole2);

            var updatedRouter = router.RemoveRoutee(TestActor);
            updatedRouter.Routees.Count().ShouldBe(2);
            updatedRouter.Routees.Cast<ActorRefRoutee>().Any(r => ReferenceEquals(r.Actor, blackHole1)).ShouldBe(true);
            updatedRouter.Routees.Cast<ActorRefRoutee>().Any(r => ReferenceEquals(r.Actor, blackHole2)).ShouldBe(true);
        }

        public class RouterSupervisorSpec : AkkaSpec
        {
            #region Killable actor

            private class KillableActor : ReceiveActor
            {
                private readonly IActorRef TestActor;

                public KillableActor(IActorRef testActor)
                {
                    TestActor = testActor;
                    Receive<string>(s => s == "go away", s => { throw new ArgumentException("Goodbye then!"); });
                }
            }

            #endregion

            #region Tests

            [Fact]
            public void Routers_must_use_provided_supervisor_strategy()
            {
                var router = Sys.ActorOf(Props.Create(() => new KillableActor(TestActor))
                    .WithRouter(
                        new RoundRobinPool(1, null, new AllForOneStrategy(
                            exception =>
                            {
                                TestActor.Tell("supervised");
                                return Directive.Stop;
                            }),
                            null)),
                    "router1");

                router.Tell("go away");

                ExpectMsg("supervised", TimeSpan.FromSeconds(2));
            }

            #endregion
        }
    }
}

