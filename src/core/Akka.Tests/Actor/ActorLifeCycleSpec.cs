//-----------------------------------------------------------------------
// <copyright file="ActorLifeCycleSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.Tests.TestUtils;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests
{
    
    public class ActorLifeCycleSpec : AkkaSpec
    {
        public class LifeCycleTestActor : UntypedActor
        {
            private AtomicCounter generationProvider;
            private string id;
            private IActorRef testActor;
            private int CurrentGeneration;
            public LifeCycleTestActor(IActorRef testActor,string id,AtomicCounter generationProvider)
            {
                this.testActor = testActor;
                this.id = id;
                this.generationProvider = generationProvider;
                this.CurrentGeneration = generationProvider.Next();
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
            private AtomicCounter generationProvider;
            private string id;
            private IActorRef testActor;
            private int CurrentGeneration;
            public LifeCycleTest2Actor(IActorRef testActor, string id, AtomicCounter generationProvider)
            {
                this.testActor = testActor;
                this.id = id;
                this.generationProvider = generationProvider;
                this.CurrentGeneration = generationProvider.Next();
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

        [Fact(DisplayName = "invoke preRestart, preStart, postRestart when using OneForOneStrategy")]
        public void ActorLifecycleTest1()
        {
            var generationProvider = new AtomicCounter();
            string id = Guid.NewGuid().ToString();
            var supervisor = Sys.ActorOf(Props.Create(() => new Supervisor(new OneForOneStrategy(3, TimeSpan.FromSeconds(1000), x => Directive.Restart))));
            var restarterProps = Props.Create(() => new LifeCycleTestActor(TestActor, id, generationProvider));
            var restarter = supervisor.Ask<IActorRef>(restarterProps).Result;

            ExpectMsg(Tuple.Create( "preStart", id, 0));
            restarter.Tell(Kill.Instance);
            ExpectMsg(Tuple.Create("preRestart", id, 0));
            ExpectMsg(Tuple.Create("postRestart", id, 1));
            restarter.Tell("status");
            ExpectMsg(Tuple.Create("OK", id, 1));
            restarter.Tell(Kill.Instance);
            ExpectMsg(Tuple.Create("preRestart", id, 1));
            ExpectMsg(Tuple.Create("postRestart", id, 2));
            restarter.Tell("status");
            ExpectMsg(Tuple.Create("OK", id, 2));
            restarter.Tell(Kill.Instance);
            ExpectMsg(Tuple.Create("preRestart", id, 2));
            ExpectMsg(Tuple.Create("postRestart", id, 3));
            restarter.Tell("status");
            ExpectMsg(Tuple.Create("OK", id, 3));
            restarter.Tell(Kill.Instance);
            ExpectMsg(Tuple.Create("postStop", id, 3));
            ExpectNoMsg(TimeSpan.FromSeconds(1));
            Sys.Stop(supervisor);
        }

        [Fact(DisplayName="default for preRestart and postRestart is to call postStop and preStart respectively")]
        public void ActorLifecycleTest2()
        {
            var generationProvider = new AtomicCounter();
            string id = Guid.NewGuid().ToString();            
            var supervisor = Sys.ActorOf(Props.Create(() => new Supervisor(new OneForOneStrategy(3, TimeSpan.FromSeconds(1000), x => Directive.Restart))));
            var restarterProps = Props.Create(() => new LifeCycleTest2Actor(TestActor, id, generationProvider));
            var restarter = supervisor.Ask<IActorRef>(restarterProps).Result;

            ExpectMsg(Tuple.Create("preStart", id, 0));
            restarter.Tell(Kill.Instance);
            ExpectMsg(Tuple.Create("postStop", id, 0));
            ExpectMsg(Tuple.Create("preStart", id, 1));
            restarter.Tell("status");
            ExpectMsg(Tuple.Create("OK", id, 1));
            restarter.Tell(Kill.Instance);
            ExpectMsg(Tuple.Create("postStop", id, 1));
            ExpectMsg(Tuple.Create("preStart", id, 2));
            restarter.Tell("status");
            ExpectMsg(Tuple.Create("OK", id, 2));
            restarter.Tell(Kill.Instance);
            ExpectMsg(Tuple.Create("postStop", id, 2));
            ExpectMsg(Tuple.Create("preStart", id, 3));
            restarter.Tell("status");
            ExpectMsg(Tuple.Create("OK", id, 3));
            restarter.Tell(Kill.Instance);
            ExpectMsg(Tuple.Create("postStop", id, 3));
            ExpectNoMsg(TimeSpan.FromSeconds(1));
            Sys.Stop(supervisor);
        } 

        [Fact(DisplayName="not invoke preRestart and postRestart when never restarted using OneForOneStrategy")]
        public void ActorLifecycleTest3()
        {
            var generationProvider = new AtomicCounter();
            string id = Guid.NewGuid().ToString();            
            var supervisor = Sys.ActorOf(Props.Create(() => new Supervisor(new OneForOneStrategy(3, TimeSpan.FromSeconds(1000), x => Directive.Restart))));
            var restarterProps = Props.Create(() => new LifeCycleTest2Actor(TestActor, id, generationProvider));
            var restarter = supervisor.Ask<IInternalActorRef>(restarterProps).Result;

            ExpectMsg(Tuple.Create("preStart", id, 0));
            restarter.Tell("status");
            ExpectMsg(Tuple.Create("OK", id, 0));
            restarter.Stop();
            ExpectMsg(Tuple.Create("postStop", id, 0));
            ExpectNoMsg(TimeSpan.FromSeconds(1));
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

        [Fact(DisplayName="log failures in postStop")]
        public void LogFailuresInPostStop()
        {
            var a = Sys.ActorOf<EmptyActor>();
            EventFilter.Exception<Exception>(message: "hurrah").ExpectOne(() =>
                {
                    a.Tell(PoisonPill.Instance);
                });            
        }

        public class Become
        {
        }
        public class BecomeActor : UntypedActor
        {
            private IActorRef testActor;
            public BecomeActor(IActorRef testActor)
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
                    Context.Become(OnBecome);
                    testActor.Tell("ok");
                }
                else
                {
                    testActor.Tell(42);
                }
            }

            protected void OnBecome(object message)
            {
                if (message.ToString() == "fail")
                {
                    throw new Exception("buh");
                }
                else
                {
                    testActor.Tell(43);
                }
            }
        }

        [Fact]
        public void ClearBehaviorStackUponRestart()
        {
            var a = Sys.ActorOf(Props.Create(() => new BecomeActor(TestActor)));

            a.Tell("hello");
            ExpectMsg(42);
            a.Tell(new Become());
            ExpectMsg("ok");
            a.Tell("hello");
            ExpectMsg(43);

            EventFilter.Exception<Exception>("buh").ExpectOne(() => a.Tell("fail"));
            a.Tell("hello");
            ExpectMsg(42);
        }

        public class SupervisorTestActor : UntypedActor
        {
            private IActorRef testActor;
            public SupervisorTestActor(IActorRef testActor)
            {
                this.testActor = testActor;
            }

            protected override void OnReceive(object message)
            {
                PatternMatch.Match(message)
                    .With<Spawn>(m =>
                    {
                        Context.ActorOf(Props.Create(() => new KillableActor(testActor)), m.Name);
                        testActor.Tell(Tuple.Create("Created", m.Name));
                    })
                    .With<ContextStop>(m =>
                    {
                        var child = Context.Child(m.Name);
                        Context.Stop(child);                    
                    })
                    .With<Stop>(m =>
                    {
                        var child = Context.Child(m.Name);
                        ((IInternalActorRef)child).Stop();
                    })
                    .With<Count>(m => 
                        testActor.Tell(Context.GetChildren().Count()));
            }

            public class Spawn
            {
                public string Name { get; set; }
            }

            public class ContextStop
            {
                public string Name { get; set; }
            }

            public class Stop
            {
                public string Name { get; set; }
            }

            public class Count { }
        }

        public class KillableActor : UntypedActor
        {
            private IActorRef testActor;
            public KillableActor(IActorRef testActor)
            {
                this.testActor = testActor;
            }

            protected override void PostStop()
            {
                Debug.WriteLine("inside poststop");
                testActor.Tell(Tuple.Create("Terminated", Self.Path.Name));
            }

            protected override void OnReceive(object message)
            {
            }
        }

        [Fact(DisplayName="If a parent receives a Terminated event for a child actor, the parent should no longer supervise it")]
        public void ClearChildUponTerminated()
        {
            var names = new[] {"Bob", "Jameson", "Natasha"};
            var supervisor = Sys.ActorOf(Props.Create(() => new SupervisorTestActor(TestActor)));
            supervisor.Tell(new SupervisorTestActor.Spawn(){ Name = names[0] });
            ExpectMsg(Tuple.Create("Created",names[0]));
            supervisor.Tell(new SupervisorTestActor.Count());
            ExpectMsg(1);
            supervisor.Tell(new SupervisorTestActor.Spawn() { Name = names[1] });
            ExpectMsg(Tuple.Create("Created", names[1]));
            supervisor.Tell(new SupervisorTestActor.Count());
            ExpectMsg(2);
            supervisor.Tell(new SupervisorTestActor.ContextStop() { Name = names[1] });
            ExpectMsg(Tuple.Create("Terminated", names[1]));
       
            //we need to wait for the child actor to unregister itself from the parent.
            //this is done after PostStop so we have no way to wait for it
            //ideas?
            Task.Delay(100).Wait();
            supervisor.Tell(new SupervisorTestActor.Count());
            ExpectMsg(1);
            supervisor.Tell(new SupervisorTestActor.Spawn() { Name = names[2] });
            ExpectMsg(Tuple.Create("Created", names[2]));
            Task.Delay(100).Wait();
            supervisor.Tell(new SupervisorTestActor.Count());
            ExpectMsg(2);
            supervisor.Tell(new SupervisorTestActor.Stop() { Name = names[0] });
            ExpectMsg(Tuple.Create("Terminated", names[0]));
            supervisor.Tell(new SupervisorTestActor.Stop() { Name = names[2] });
            ExpectMsg(Tuple.Create("Terminated", names[2]));

            Task.Delay(100).Wait();
            supervisor.Tell(new SupervisorTestActor.Count());
            ExpectMsg(0);
        }


        class MyCustomException : Exception {}

        [Fact(DisplayName="PreRestart should receive correct cause, message and sender")]
        public void CallPreStartWithCorrectMessageAndSender()
        {
            var broken = ActorOf(c =>
            {
                c.Receive<string>((m, context) =>
                {
                    throw new MyCustomException();
                });

                c.OnPreRestart = (ex, mess, context) =>
                {
                    TestActor.Tell(ex);
                    TestActor.Tell(mess);
                    TestActor.Tell(context.Sender);
                };
            });

            const string message = "hello";

            broken.Tell(message);

            ExpectMsg<MyCustomException>();
            ExpectMsg(message);
            ExpectMsg(TestActor);
        }
    }
}

