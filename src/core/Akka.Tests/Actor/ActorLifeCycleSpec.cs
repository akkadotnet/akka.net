//-----------------------------------------------------------------------
// <copyright file="ActorLifeCycleSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
                testActor.Tell(((string)message,id,CurrentGeneration));
            }

            protected override void OnReceive(object message)
            {
                if (message is string && (string)message == "status")
                {
                    testActor.Tell(("OK",id,CurrentGeneration));
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
                testActor.Tell(((string)message, id, CurrentGeneration));
            }

            protected override void OnReceive(object message)
            {
                if (message is string && (string)message == "status")
                {
                    testActor.Tell(("OK", id, CurrentGeneration));
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
        public async Task Actor_lifecycle_test1()
        {
            var generationProvider = new AtomicCounter();
            string id = Guid.NewGuid().ToString();
            var supervisor = Sys.ActorOf(Props.Create(() => new Supervisor(new OneForOneStrategy(3, TimeSpan.FromSeconds(1000), x => Directive.Restart))));
            var restarterProps = Props.Create(() => new LifeCycleTestActor(TestActor, id, generationProvider));
            var restarter = await supervisor.Ask<IActorRef>(restarterProps);

            await ExpectMsgAsync(("preStart", id, 0));
            restarter.Tell(Kill.Instance);
            await ExpectMsgAsync(("preRestart", id, 0));
            await ExpectMsgAsync(("postRestart", id, 1));
            restarter.Tell("status");
            await ExpectMsgAsync(("OK", id, 1));
            restarter.Tell(Kill.Instance);
            await ExpectMsgAsync(("preRestart", id, 1));
            await ExpectMsgAsync(("postRestart", id, 2));
            restarter.Tell("status");
            await ExpectMsgAsync(("OK", id, 2));
            restarter.Tell(Kill.Instance);
            await ExpectMsgAsync(("preRestart", id, 2));
            await ExpectMsgAsync(("postRestart", id, 3));
            restarter.Tell("status");
            await ExpectMsgAsync(("OK", id, 3));
            restarter.Tell(Kill.Instance);
            await ExpectMsgAsync(("postStop", id, 3));
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            Sys.Stop(supervisor);
        }

        [Fact(DisplayName="default for preRestart and postRestart is to call postStop and preStart respectively")]
        public async Task Actor_lifecycle_test2()
        {
            var generationProvider = new AtomicCounter();
            string id = Guid.NewGuid().ToString();            
            var supervisor = Sys.ActorOf(Props.Create(() => new Supervisor(new OneForOneStrategy(3, TimeSpan.FromSeconds(1000), x => Directive.Restart))));
            var restarterProps = Props.Create(() => new LifeCycleTest2Actor(TestActor, id, generationProvider));
            var restarter = await supervisor.Ask<IActorRef>(restarterProps);

            await ExpectMsgAsync(("preStart", id, 0));
            restarter.Tell(Kill.Instance);
            await ExpectMsgAsync(("postStop", id, 0));
            await ExpectMsgAsync(("preStart", id, 1));
            restarter.Tell("status");
            await ExpectMsgAsync(("OK", id, 1));
            restarter.Tell(Kill.Instance);
            await ExpectMsgAsync(("postStop", id, 1));
            await ExpectMsgAsync(("preStart", id, 2));
            restarter.Tell("status");
            await ExpectMsgAsync(("OK", id, 2));
            restarter.Tell(Kill.Instance);
            await ExpectMsgAsync(("postStop", id, 2));
            await ExpectMsgAsync(("preStart", id, 3));
            restarter.Tell("status");
            await ExpectMsgAsync(("OK", id, 3));
            restarter.Tell(Kill.Instance);
            await ExpectMsgAsync(("postStop", id, 3));
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            Sys.Stop(supervisor);
        } 

        [Fact(DisplayName="not invoke preRestart and postRestart when never restarted using OneForOneStrategy")]
        public async Task Actor_lifecycle_test3()
        {
            var generationProvider = new AtomicCounter();
            string id = Guid.NewGuid().ToString();            
            var supervisor = Sys.ActorOf(Props.Create(() => new Supervisor(new OneForOneStrategy(3, TimeSpan.FromSeconds(1000), x => Directive.Restart))));
            var restarterProps = Props.Create(() => new LifeCycleTest2Actor(TestActor, id, generationProvider));
            var restarter = await supervisor.Ask<IInternalActorRef>(restarterProps);

            await ExpectMsgAsync(("preStart", id, 0));
            restarter.Tell("status");
            await ExpectMsgAsync(("OK", id, 0));
            restarter.Stop();
            await ExpectMsgAsync(("postStop", id, 0));
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
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
        public async Task Log_failures_in_PostStop()
        {
            var a = Sys.ActorOf<EmptyActor>();
            await EventFilter.Exception<Exception>(message: "hurrah").ExpectOneAsync(() =>
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
        public async Task Clear_behavior_stack_upon_restart()
        {
            var a = Sys.ActorOf(Props.Create(() => new BecomeActor(TestActor)));

            a.Tell("hello");
            await ExpectMsgAsync(42);
            a.Tell(new Become());
            await ExpectMsgAsync("ok");
            a.Tell("hello");
            await ExpectMsgAsync(43);

            await EventFilter.Exception<Exception>("buh").ExpectOneAsync(() => a.Tell("fail"));
            a.Tell("hello");
            await ExpectMsgAsync(42);
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
                switch (message)
                {
                    case Spawn m:
                        Context.ActorOf(Props.Create(() => new KillableActor(testActor)), m.Name); 
                        testActor.Tell(("Created", m.Name));
                        break;
                    
                    case ContextStop m:
                    {
                        var child = Context.Child(m.Name);
                        Context.Stop(child);
                        break;
                    }
                    
                    case Stop m:
                    {
                        var child = Context.Child(m.Name);
                        ((IInternalActorRef)child).Stop();
                        break;
                    }
                    
                    case Count _:
                        testActor.Tell(Context.GetChildren().Count());
                        break;
                }
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
                testActor.Tell(("Terminated", Self.Path.Name));
            }

            protected override void OnReceive(object message)
            {
            }
        }

        [Fact(DisplayName="If a parent receives a Terminated event for a child actor, the parent should no longer supervise it")]
        public async Task Clear_child_upon_terminated()
        {
            var names = new[] {"Bob", "Jameson", "Natasha"};
            var supervisor = Sys.ActorOf(Props.Create(() => new SupervisorTestActor(TestActor)));
            supervisor.Tell(new SupervisorTestActor.Spawn(){ Name = names[0] });
            await ExpectMsgAsync(("Created",names[0]));
            supervisor.Tell(new SupervisorTestActor.Count());
            await ExpectMsgAsync(1);
            supervisor.Tell(new SupervisorTestActor.Spawn() { Name = names[1] });
            await ExpectMsgAsync(("Created", names[1]));
            supervisor.Tell(new SupervisorTestActor.Count());
            await ExpectMsgAsync(2);
            supervisor.Tell(new SupervisorTestActor.ContextStop() { Name = names[1] });
            await ExpectMsgAsync(("Terminated", names[1]));
       
            //we need to wait for the child actor to unregister itself from the parent.
            //this is done after PostStop so we have no way to wait for it
            //ideas?
            await Task.Delay(100);
            supervisor.Tell(new SupervisorTestActor.Count());
            await ExpectMsgAsync(1);
            supervisor.Tell(new SupervisorTestActor.Spawn() { Name = names[2] });
            await ExpectMsgAsync(("Created", names[2]));
            await Task.Delay(100);
            supervisor.Tell(new SupervisorTestActor.Count());
            await ExpectMsgAsync(2);
            supervisor.Tell(new SupervisorTestActor.Stop() { Name = names[0] });
            await ExpectMsgAsync(("Terminated", names[0]));
            supervisor.Tell(new SupervisorTestActor.Stop() { Name = names[2] });
            await ExpectMsgAsync(("Terminated", names[2]));

            await Task.Delay(100);
            supervisor.Tell(new SupervisorTestActor.Count());
            await ExpectMsgAsync(0);
        }


        class MyCustomException : Exception {}

        [Fact(DisplayName="PreRestart should receive correct cause, message and sender")]
        public async Task Call_PreStart_with_correct_message_and_sender()
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

            await ExpectMsgAsync<MyCustomException>();
            await ExpectMsgAsync(message);
            await ExpectMsgAsync(TestActor);
        }
    }
}

