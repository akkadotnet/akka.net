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
    public class ActorLifeCycleSpec
    {

/**
 * For testing Supervisor behavior, normally you don't supply the strategy
 * from the outside like this.
 */

        public class TestActor : UntypedActor
        {
            private BlockingCollection<Tuple<string,string,int>> queue;
            public TestActor(BlockingCollection<Tuple<string, string, int>> queue)
            {
                this.queue = queue;
            }
            protected override void OnReceive(object message)
            {
                if (message is Tuple<string,string,int>)
                {
                    queue.Add((Tuple<string,string,int>)message);
                }
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
            private int value=-1;
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

      //  [Description("invoke preRestart, preStart, postRestart when using OneForOneStrategy")]
        [TestMethod()]
        public void ActorLifecycleTest1()
        {
            using (var system = ActorSystem.Create("Test"))
            {
                var generationProvider = new AtomicInteger();
                var queue = new BlockingCollection<Tuple<string, string, int>>();
                Action<string, string, int> expectMsg = (expectedText, expectedId, expectedGeneration) => {
                    var t = queue.Take();
                    var actualText = t.Item1;
                    var actualId = t.Item2;
                    var actualGeneration = t.Item3;
                    Debug.WriteLine(actualText);

                    Assert.AreEqual(expectedText, actualText);
                    Assert.AreEqual(expectedGeneration, actualGeneration);
                    Assert.AreEqual(expectedId, actualId);
                };
                string id = Guid.NewGuid().ToString();
                
                OneForOneStrategy strategy = new OneForOneStrategy(3, TimeSpan.FromSeconds(1000), x =>
                {
                    return Directive.Restart;
                });
                ActorRef testActor = system.ActorOf(Props.Create(() => new TestActor(queue)));
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
                supervisor.Stop();
            }
        }

        [TestMethod()]
        public void ActorLifecycleTest2()
        {
            using (var system = ActorSystem.Create("Test"))
            {
                var generationProvider = new AtomicInteger();
                var queue = new BlockingCollection<Tuple<string, string, int>>();
                Action<string, string, int> expectMsg = (expectedText, expectedId, expectedGeneration) =>
                {
                    var t = queue.Take();
                    var actualText = t.Item1;
                    var actualId = t.Item2;
                    var actualGeneration = t.Item3;
                    Debug.WriteLine(actualText);

                    Assert.AreEqual(expectedText, actualText);
                    Assert.AreEqual(expectedGeneration, actualGeneration);
                    Assert.AreEqual(expectedId, actualId);
                };
                string id = Guid.NewGuid().ToString();

                OneForOneStrategy strategy = new OneForOneStrategy(3, TimeSpan.FromSeconds(1000), x =>
                {
                    return Directive.Restart;
                });
                ActorRef testActor = system.ActorOf(Props.Create(() => new TestActor(queue)));
                var supervisor = system.ActorOf(Props.Create(() => new Supervisor(strategy)));
                var restarterProps = Props.Create(() => new LifeCycleTest2Actor(testActor, id, generationProvider));

                var restarter = (ActorRef)supervisor.Ask(restarterProps, system).Result;

                expectMsg("preStart", id, 0);
                restarter.Tell(new Kill());
                expectMsg("postStop", id, 0);
                expectMsg("preStart", id, 1);

                restarter.Tell("status");
                expectMsg("OK", id, 1);

                restarter.Tell(new Kill());
                expectMsg("postStop", id, 1);
                expectMsg("preStart", id, 2);

                restarter.Tell("status");
                expectMsg("OK", id, 2);

                restarter.Tell(new Kill());
                expectMsg("postStop", id, 2);
                expectMsg("preStart", id, 3);

                restarter.Tell("status");
                expectMsg("OK", id, 3);

                restarter.Tell(new Kill());
                expectMsg("postStop", id, 3);
                supervisor.Stop();
            }
        }
    }
}
