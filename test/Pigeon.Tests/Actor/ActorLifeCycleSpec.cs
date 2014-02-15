using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pigeon.Actor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pigeon.Tests
{
    [TestClass]
    public class ActorLifeCycleSpec : AkkaSpec
    {
        public class LifeCycleTestActor : UntypedActor
        {
            private AtomicInteger generationProvider;
            private string id;
            private ActorRef testActor;
            private int CurrentGeneration;
            public LifeCycleTestActor(ActorRef testActor,string id,AtomicInteger generationProvider)
            {
                this.testActor = testActor;
                this.id = id;
                this.generationProvider = generationProvider;
                this.CurrentGeneration = generationProvider.GetAndIncrement();
            }

            private void Report(object message)
            {
                testActor.Tell(Tuple.Create((string)message,id,CurrentGeneration));
            }

            protected override void OnReceive(object message)
            {
                if (message is string && (string)message == "status")
                {
                    testActor.Tell(Tuple.Create("OK",id,CurrentGeneration));
                }
            }

            protected override void PostStop()
            {
                Report("postStop");
            }

            protected override void PreStart()
            {
                Report("preStart");
            }

            protected override void PreRestart(Exception cause, object message)
            {
                Report("preRestart");
            }

            protected override void PostRestart(Exception cause)
            {
                Report("postRestart");
            }
        }

        public class LifeCycleTest2Actor : UntypedActor
        {
            private AtomicInteger generationProvider;
            private string id;
            private ActorRef testActor;
            private int CurrentGeneration;
            public LifeCycleTest2Actor(ActorRef testActor, string id, AtomicInteger generationProvider)
            {
                this.testActor = testActor;
                this.id = id;
                this.generationProvider = generationProvider;
                this.CurrentGeneration = generationProvider.GetAndIncrement();
            }

            private void Report(object message)
            {
                testActor.Tell(Tuple.Create((string)message, id, CurrentGeneration));
            }

            protected override void OnReceive(object message)
            {
                if (message is string && (string)message == "status")
                {
                    testActor.Tell(Tuple.Create("OK", id, CurrentGeneration));
                }
            }

            protected override void PostStop()
            {
                Report("postStop");
            }

            protected override void PreStart()
            {
                Report("preStart");
            }
        }

        [Description("invoke preRestart, preStart, postRestart when using OneForOneStrategy")]
        [TestMethod()]
        public void ActorLifecycleTest1()
        {
            var generationProvider = new AtomicInteger();
            string id = Guid.NewGuid().ToString();
            var supervisor = sys.ActorOf(Props.Create(() => new Supervisor(new OneForOneStrategy(3, TimeSpan.FromSeconds(1000), x => Directive.Restart))));
            var restarterProps = Props.Create(() => new LifeCycleTestActor(testActor, id, generationProvider));
            var restarter = (ActorRef)supervisor.Ask(restarterProps, sys).Result;

            expectMsg(Tuple.Create( "preStart", id, 0));
            restarter.Tell(new Kill());
            expectMsg(Tuple.Create("preRestart", id, 0));
            expectMsg(Tuple.Create("postRestart", id, 1));
            restarter.Tell("status");
            expectMsg(Tuple.Create("OK", id, 1));
            restarter.Tell(new Kill());
            expectMsg(Tuple.Create("preRestart", id, 1));
            expectMsg(Tuple.Create("postRestart", id, 2));
            restarter.Tell("status");
            expectMsg(Tuple.Create("OK", id, 2));
            restarter.Tell(new Kill());
            expectMsg(Tuple.Create("preRestart", id, 2));
            expectMsg(Tuple.Create("postRestart", id, 3));
            restarter.Tell("status");
            expectMsg(Tuple.Create("OK", id, 3));
            restarter.Tell(new Kill());
            expectMsg(Tuple.Create("postStop", id, 3));
            expectNoMsg(TimeSpan.FromSeconds(1));
            supervisor.Stop();
        }

        [Description("default for preRestart and postRestart is to call postStop and preStart respectively")]
        [TestMethod()]
        public void ActorLifecycleTest2()
        {
            var generationProvider = new AtomicInteger();
            string id = Guid.NewGuid().ToString();            
            var supervisor = sys.ActorOf(Props.Create(() => new Supervisor(new OneForOneStrategy(3, TimeSpan.FromSeconds(1000), x => Directive.Restart))));
            var restarterProps = Props.Create(() => new LifeCycleTest2Actor(testActor, id, generationProvider));
            var restarter = (ActorRef)supervisor.Ask(restarterProps, sys).Result;

            expectMsg(Tuple.Create("preStart", id, 0));
            restarter.Tell(new Kill());
            expectMsg(Tuple.Create("postStop", id, 0));
            expectMsg(Tuple.Create("preStart", id, 1));
            restarter.Tell("status");
            expectMsg(Tuple.Create("OK", id, 1));
            restarter.Tell(new Kill());
            expectMsg(Tuple.Create("postStop", id, 1));
            expectMsg(Tuple.Create("preStart", id, 2));
            restarter.Tell("status");
            expectMsg(Tuple.Create("OK", id, 2));
            restarter.Tell(new Kill());
            expectMsg(Tuple.Create("postStop", id, 2));
            expectMsg(Tuple.Create("preStart", id, 3));
            restarter.Tell("status");
            expectMsg(Tuple.Create("OK", id, 3));
            restarter.Tell(new Kill());
            expectMsg(Tuple.Create("postStop", id, 3));
            expectNoMsg(TimeSpan.FromSeconds(1));
            supervisor.Stop();
        } 

        [Description("not invoke preRestart and postRestart when never restarted using OneForOneStrategy")]
        [TestMethod]
        public void ActorLifecycleTest3()
        {
            var generationProvider = new AtomicInteger();
            string id = Guid.NewGuid().ToString();            
            var supervisor = sys.ActorOf(Props.Create(() => new Supervisor(new OneForOneStrategy(3, TimeSpan.FromSeconds(1000), x => Directive.Restart))));
            var restarterProps = Props.Create(() => new LifeCycleTest2Actor(testActor, id, generationProvider));
            var restarter = (ActorRef)supervisor.Ask(restarterProps, sys).Result;

            expectMsg(Tuple.Create("preStart", id, 0));
            restarter.Tell("status");
            expectMsg(Tuple.Create("OK", id, 0));
            restarter.Stop();
            expectMsg(Tuple.Create("postStop", id, 0));
            expectNoMsg(TimeSpan.FromSeconds(1));
        }


        public class EmptyActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {         
            }

            protected override void PostStop()
            {
                throw new Exception("hurrah");
            }
        }

        [Description("log failues in postStop")]
        [TestMethod]
        public void LogFailutresInPostStop()
        {
            var a = sys.ActorOf<EmptyActor>();
            EventFilter<Exception>(message: "hurrah",occurances: 1, intercept: () =>
                {
                    a.Tell(new PoisonPill());
                });            
        }

        public class Become
        {
            public Receive x;
        }
        public class BecomeActor : UntypedActor
        {
            private ActorRef testActor;
            public BecomeActor(ActorRef testActor)
            {
                this.testActor = testActor;
            }

            protected override void PreRestart(Exception cause, object message)
            {
                base.PreRestart(cause, message);
            }

            protected override void PostRestart(Exception cause)
            {
                base.PostRestart(cause);
            }
            protected override void OnReceive(object message)
            {
                if (message is Become)
                {
                    Context.Become(((Become)message).x);
                    testActor.Tell("ok");
                }
                else
                {
                    testActor.Tell(42);
                }
            }
        }
        [TestMethod]
        public void ClearBehaviorStackUponRestart()
        {
            var a = sys.ActorOf(Props.Create(() => new BecomeActor(testActor)));

            a.Tell("hello");
            expectMsg(42);
            a.Tell(new Become
            {
                x = m =>
                {
                    if (m.ToString() == "fail")
                    {
                        throw new Exception("buh");
                    }
                    else
                    {
                        testActor.Tell(43);
                    }
                }
            });
            expectMsg("ok");
            a.Tell("hello");
            expectMsg(43);
            EventFilter<Exception>("buh", 1, () =>
            {                
                a.Tell("fail");
            });
            a.Tell("hello");
            expectMsg(42);
        }

    }
}
