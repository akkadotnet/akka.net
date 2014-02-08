using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pigeon.Tests
{
    [TestClass]
    public class ActorLifeCycleSpec
    {

/**
 * For testing Supervisor behavior, normally you don't supply the strategy
 * from the outside like this.
 */

        public class TestActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                
            }
        }
        public class Supervisor : UntypedActor
        {
            private SupervisorStrategy supervisorStrategy;
            public Supervisor(SupervisorStrategy supervisorStrategy)
            {
                this.supervisorStrategy = supervisorStrategy;
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return supervisorStrategy;
            }

            protected override void OnReceive(object message)
            {
                Pattern.Match(message)
                    .With<Props>(x => Sender.Tell(Context.ActorOf(x)));
            }

            protected override void PreRestart(Exception cause, object message)
            {
                // need to override the default of stopping all children upon restart, tests rely on keeping them around
            }
        }
        public class AtomicInteger
        {
            private int value;
            public int Value
            {
                get
                {
                    return value;
                }
            }
            public int GetAndIncrement()
            {
                Interlocked.Increment(ref value);
                return value;
            }
        }

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
                testActor.Tell(message);
            }

            protected override void OnReceive(object message)
            {
                if (message is string && (string)message == "Status")
                {
                    Sender.Tell("OK");
                }
            }

            protected override void PostStop()
            {
                Report("PostStop");
            }

            protected override void PreStart()
            {
                Report("PreStart");
            }

            protected override void PreRestart(Exception cause, object message)
            {
                Report("PreRestart");
            }

            protected override void PostRestart(Exception cause, object message)
            {
                Report("PostRestart");
            }
        }


      //  [Description("invoke preRestart, preStart, postRestart when using OneForOneStrategy")]
        [TestMethod()]
        public void ActorLifecycleTest1()
        {
            using (var system = ActorSystem.Create("Test"))
            {
                Action<string, string, int> expectMsg = (a, b, c) => { };
                string id = Guid.NewGuid().ToString();
                var generationProvider = new AtomicInteger();
                OneForOneStrategy strategy = new OneForOneStrategy(3, TimeSpan.FromSeconds(10), x =>
                {
                    return Directive.Resume;
                });
                ActorRef testActor = system.ActorOf<TestActor>();
                var supervisor = system.ActorOf(Props.Create(() => new Supervisor(strategy)));
                var restarterProps = Props.Create(() => new LifeCycleTestActor(testActor, id, generationProvider));

                var restarter = (ActorRef)supervisor.Ask(restarterProps, system).Result;

                expectMsg("preStart", id, 0);
                restarter.Tell(new Kill());
                expectMsg("preRestart", id, 0);
                expectMsg("postRestart", id, 1);
                restarter.Tell("status");
                expectMsg("OK", id, 1);
                restarter.Tell(new Kill());
                expectMsg("preRestart", id, 1);
                expectMsg("postRestart", id, 2);
                restarter.Tell("status");
                expectMsg("OK", id, 2);
                restarter.Tell(new Kill());
                expectMsg("preRestart", id, 2);
                expectMsg("postRestart", id, 3);
                restarter.Tell("status");
                expectMsg("OK", id, 3);
                restarter.Tell(new Kill());
                expectMsg("postStop", id, 3);


            }
        }
    }
}
