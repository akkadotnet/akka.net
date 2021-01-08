//-----------------------------------------------------------------------
// <copyright file="RoutingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Configuration;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Routing;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Tests.TestUtils;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Routing
{
    public class RoutingSpec : AkkaSpec
    {
        public RoutingSpec() : base(GetConfig())
        {
        }

        private static string GetConfig()
        {
            return @"
                akka.actor.serialize-messages = off
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
                }";
        }

        private class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(c => Sender.Tell(Self));
            }
        }

        private class TestResizer : Resizer
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

            public override int Resize(IEnumerable<Routee> currentRoutees)
            {
                _latch.CountDown();
                return 2;
            }
        }

        private class TestResizer2 : Resizer
        {
            private readonly TestLatch _latch;

            public TestResizer2(TestLatch latch)
            {
                _latch = latch;
            }

            public override bool IsTimeForResize(long messageCounter)
            {
                return messageCounter == 0;
            }

            public override int Resize(IEnumerable<Routee> currentRoutees)
            {
                _latch.CountDown();
                return 3;
            }
        }

        private class RestartActor : UntypedActor
        {
            private readonly IActorRef _testActor;

            public RestartActor(IActorRef testActor)
            {
                _testActor = testActor;
            }

            protected override void OnReceive(object message)
            {
                var msg = (string)message;
                if (msg != null)
                    throw new ArgumentException(msg);
            }

            protected override void PostRestart(Exception reason)
            {
                _testActor.Tell("restarted");
                base.PostRestart(reason);
            }
        }

        private class InlineRouterActor : ReceiveActor
        {
            public InlineRouterActor()
            {
                Receive<string>(s => s == "start", c =>
                {
                    var actor = Context.ActorOf(new RoundRobinPool(2).Props(Props.Create<InlineReceiverActor>()));
                    actor.Ask("hello").PipeTo(Sender);
                });
            }
        }

        private class InlineReceiverActor : ReceiveActor
        {
            public InlineReceiverActor()
            {
                ReceiveAny(c =>
                {
                    Sender.Tell(c);
                });
            }
        }

        private class ForwardActor : ReceiveActor
        {
            private readonly IActorRef _testActor;

            public ForwardActor(IActorRef testActor)
            {
                _testActor = testActor;

                ReceiveAny(msg =>
                {
                    _testActor.Forward(msg);
                });
            }
        }

        [Fact]
        public void Routers_in_general_must_evict_terminated_routees()
        {
            var router = Sys.ActorOf(new RoundRobinPool(2).Props(Props.Create<Echo>()));
            router.Tell("");
            router.Tell("");

            var c1 = ExpectMsg<IActorRef>();
            var c2 = ExpectMsg<IActorRef>();

            Watch(router);
            Watch(c2);
            Sys.Stop(c2);
            ExpectTerminated(c2).ExistenceConfirmed.Should().BeTrue();

            // it might take a while until the Router has actually processed the Terminated message
            AwaitCondition(() =>
            {
                router.Tell("");
                router.Tell("");

                var res = ReceiveWhile(100.Milliseconds(), x =>
                {
                    if (x is IActorRef)
                        return x.AsInstanceOf<IActorRef>();

                    return null;
                }, msgs: 2);

                return res.Count == 2 && res.All(c => c.Equals(c1));
            });

            Sys.Stop(c1);
            ExpectTerminated(router).ExistenceConfirmed.Should().BeTrue();
        }

        [Fact]
        public void Routers_in_general_must_not_terminate_when_resizer_is_used()
        {
            var latch = new TestLatch(1);
            var resizer = new TestResizer(latch);
            var router = Sys.ActorOf(new RoundRobinPool(0, resizer).Props(Props.Create<BlackHoleActor>()));
            Watch(router);
            latch.Ready(RemainingOrDefault);

            router.Tell(new GetRoutees());
            var routees = ExpectMsg<Routees>().Members.ToList();
            routees.Count.Should().Be(2);

            routees.ForEach(r => r.Send(PoisonPill.Instance, TestActor));
            
            // expect no Terminated
            ExpectNoMsg(2.Seconds());
        }

        [Fact]
        public void Routers_in_general_must_use_configured_nr_of_instances_when_FromConfig()
        {
            var router = Sys.ActorOf(FromConfig.Instance.Props(Props.Create<BlackHoleActor>()), "router1");
            router.Tell(new GetRoutees());
            ExpectMsg<Routees>().Members.Count().Should().Be(3);
            Watch(router);
            Sys.Stop(router);
            ExpectTerminated(router);
        }

        [Fact]
        public void Routers_in_general_must_use_configured_nr_of_instances_when_router_is_specified()
        {
            var router = Sys.ActorOf(new RoundRobinPool(0).Props(Props.Create<BlackHoleActor>()), "router2");
            router.Tell(new GetRoutees());
            ExpectMsg<Routees>().Members.Count().Should().Be(3);
            Sys.Stop(router);
        }

        [Fact]
        public void Routers_in_general_must_use_specified_resizer_when_resizer_not_configured()
        {
            var latch = new TestLatch(1);
            var resizer = new TestResizer2(latch);
            var router = Sys.ActorOf(new RoundRobinPool(0, resizer).Props(Props.Create<BlackHoleActor>()), "router3");
            latch.Ready(RemainingOrDefault);
            router.Tell(new GetRoutees());
            ExpectMsg<Routees>().Members.Count().Should().Be(3);
            Sys.Stop(router);
        }

        [Fact]
        public void Routers_in_general_must_set_supplied_supervisorStrategy()
        {
            var escalator = new OneForOneStrategy(e =>
            {
                TestActor.Tell(e);
                return Directive.Escalate;
            });

            var router = Sys.ActorOf(new RoundRobinPool(1, null, escalator, Dispatchers.DefaultDispatcherId).Props(Props.Create<BlackHoleActor>()));
            router.Tell(new GetRoutees());
            EventFilter.Exception<ActorKilledException>().ExpectOne(() =>
            {
                ExpectMsg<Routees>().Members.First().Send(Kill.Instance, TestActor);
            });
            ExpectMsg<ActorKilledException>();

            var router2 = Sys.ActorOf(new RoundRobinPool(1).WithSupervisorStrategy(escalator).Props(Props.Create<BlackHoleActor>()));
            router2.Tell(new GetRoutees());
            EventFilter.Exception<ActorKilledException>().ExpectOne(() =>
            {
                ExpectMsg<Routees>().Members.First().Send(Kill.Instance, TestActor);
            });

            ExpectMsg<ActorKilledException>();
        }

        [Fact]
        public void Routers_in_general_must_set_supplied_supervisorStrategy_for_FromConfig()
        {
            var escalator = new OneForOneStrategy(e =>
            {
                TestActor.Tell(e);
                return Directive.Escalate;
            });

            var router = Sys.ActorOf(FromConfig.Instance.WithSupervisorStrategy(escalator).Props(Props.Create<BlackHoleActor>()), "router1");
            router.Tell(new GetRoutees());
            EventFilter.Exception<ActorKilledException>().ExpectOne(() =>
            {
                ExpectMsg<Routees>().Members.First().Send(Kill.Instance, TestActor);
            });
            ExpectMsg<ActorKilledException>();
        }

        [Fact]
        public void Routers_in_general_must_default_to_all_for_one_always_escalate_strategy()
        {
            var restarter = new OneForOneStrategy(e =>
            {
                TestActor.Tell(e);
                return Directive.Restart;
            });

            var supervisor = Sys.ActorOf(Props.Create(() => new Supervisor(restarter)));

            supervisor.Tell(new RoundRobinPool(3).Props(Props.Create(() => new RestartActor(TestActor))));

            var router = ExpectMsg<IActorRef>();
            EventFilter.Exception<ArgumentException>("die").ExpectOne(() =>
            {
                router.Tell("die");
            });
            ExpectMsg<ArgumentException>().Message.Should().Be("die");
            ExpectMsg("restarted");
            ExpectMsg("restarted");
            ExpectMsg("restarted");
        }

        [Fact]
        public void Routers_in_general_must_start_inline_for_context_actorOf()
        {
            var actor = Sys.ActorOf<InlineRouterActor>();
            actor.Tell("start");
            ExpectMsg("hello");
        }

        [Fact]
        public void NoRouter_must_send_message_to_connection()
        {
            var routedActor = Sys.ActorOf(NoRouter.Instance.Props(Props.Create(() => new ForwardActor(TestActor))));
            routedActor.Tell("hello");
            routedActor.Tell("end");

            ExpectMsg("hello");
            ExpectMsg("end");
        }

        [Fact]
        public void Router_from_config_must_throw_suitable_exception_when_not_configured()
        {
            var e = Assert.Throws<ConfigurationException>(() =>
            {
                Sys.ActorOf(FromConfig.Instance.Props(Props.Create<Echo>()), "routerNotDefined");
            });

            e.Message.Should().Contain("routerNotDefined");
        }

        [Fact]
        public void Routers_from_config_must_allow_external_configuration()
        {
            var sys = ActorSystem.Create(
                "FromConfig",
                ConfigurationFactory.ParseString("akka.actor.deployment./routed.router=round-robin-pool")
                .WithFallback(Sys.Settings.Config));
            try
            {
                sys.ActorOf(FromConfig.Instance.Props(Props.Create<Echo>()), "routed");
            }
            finally
            {
                Shutdown(sys);
            }
        }

        // Custom tests

        [Fact]
        public void Routers_must_be_able_to_send_their_routees()
        {
            var router = Sys.ActorOf(new BroadcastPool(5).Props(Props.Create<Echo>()));
            router.Tell("hello", TestActor);
            ExpectMsg<IActorRef>();
            ExpectMsg<IActorRef>();
            ExpectMsg<IActorRef>();
            ExpectMsg<IActorRef>();
            ExpectMsg<IActorRef>();
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Routers_AddRoute_should_not_add_same_routee()
        {
            var router = new Router(new RoundRobinRoutingLogic(), TestActor);

            var updatedRouter = router.AddRoutee(TestActor);
            updatedRouter.Routees.Count().Should().Be(1);
            updatedRouter.Routees.First().AsInstanceOf<ActorRefRoutee>().Actor.Should().Be(TestActor);
        }

        [Fact]
        public void Routers_AddRoute_should_add_new_routee()
        {
            var router = new Router(new RoundRobinRoutingLogic(), TestActor);
            var blackHole = ActorOf<BlackHoleActor>();
            var updatedRouter = router.AddRoutee(blackHole);
            updatedRouter.Routees.Count().Should().Be(2);
            updatedRouter.Routees.Cast<ActorRefRoutee>().Any(r => ReferenceEquals(r.Actor, TestActor)).Should().BeTrue();
            updatedRouter.Routees.Cast<ActorRefRoutee>().Any(r => ReferenceEquals(r.Actor, blackHole)).Should().BeTrue();
        }

        [Fact]
        public void Routers_RemoveRoute_should_remove_existing_routee_and_leave_the_rest()
        {
            var blackHole1 = ActorOf<BlackHoleActor>();
            var blackHole2 = ActorOf<BlackHoleActor>();
            var router = new Router(new RoundRobinRoutingLogic(), TestActor, blackHole1, blackHole2);

            var updatedRouter = router.RemoveRoutee(TestActor);
            updatedRouter.Routees.Count().Should().Be(2);
            updatedRouter.Routees.Cast<ActorRefRoutee>().Any(r => ReferenceEquals(r.Actor, blackHole1)).Should().BeTrue();
            updatedRouter.Routees.Cast<ActorRefRoutee>().Any(r => ReferenceEquals(r.Actor, blackHole2)).Should().BeTrue();
        }
    }
}
