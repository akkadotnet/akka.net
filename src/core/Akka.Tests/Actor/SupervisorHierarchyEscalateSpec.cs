using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Routing;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor
{
    public class SupervisorHierarchyEscalateSpec : AkkaSpec
    {
        public SupervisorHierarchyEscalateSpec(ITestOutputHelper output) : base(output)
        {

        }

        enum Command
        {
            Reply,
            Fail
        }

        class CustomException : Exception
        {
            public CustomException(string message): base(message) { }
        }

        class Failer : ReceiveActor
        {
            private readonly ILoggingAdapter _log = Context.GetLogger();
            public Failer()
            {
                Receive<Command>(command => command == Command.Fail, command =>
                {
                    _log.Info("Throwing failure");
                    throw new CustomException("fail");
                });
                Receive<Command>(command => command == Command.Reply, command =>
                {
                    _log.Info("Processing command");
                    Sender.Tell("reply");
                });
            }
        }

        class Shell : ReceiveActor
        {
            private readonly Props _childProps;
            private IActorRef _child;

            public Shell(Props child)
            {
                _childProps = child;

                Receive<Terminated>(_ => Context.Stop(Self));
                ReceiveAny(_ => _child.Forward(_));
            }

            protected override void PreStart()
            {
                _child = Context.ActorOf(_childProps);
                Context.Watch(_child);
            }
        }

        class Supervisor : ReceiveActor
        {
            private readonly int _routeeCount;
            private IActorRef _router;

            public Supervisor(int routeeCount)
            {
                _routeeCount = routeeCount;
                Receive<Terminated>(_ => Context.Stop(Self));
                ReceiveAny(_ => _router.Forward(_));
            }

            protected override void PreStart()
            {
                _router = Context.ActorOf(Props.Create(() => new Failer()).WithRouter(new RoundRobinPool(_routeeCount).WithSupervisorStrategy(new OneForOneStrategy(ex => Directive.Escalate)))
                    , "router");
                Context.Watch(_router);
            }
        }

        class ReportingStrategy : OneForOneStrategy
        {
            private readonly IActorRef _tellTarget;
            private readonly ILoggingAdapter _log;

            public ReportingStrategy(IActorRef tellTarget, ILoggingAdapter log)
            {
                _tellTarget = tellTarget;
                _log = log;
            }

            protected override Directive Handle(IActorRef child, Exception exception)
            {
                if (exception is CustomException)
                {
                    _log.Info("Resuming {0}", child);
                    _tellTarget.Tell(child);
                    return Directive.Resume;
                }

                return Directive.Stop;
            }
        }

        /// <summary>
        /// Reproduction spec for https://github.com/akkadotnet/akka.net/issues/3597
        /// </summary>
        [Fact]
        public void Bugfix3597_supervise_resume_3_layers_deep()
        {
            var childCount = 10;
            var sup = Sys.ActorOf(Props.Create(() => new Supervisor(childCount)).WithSupervisorStrategy(new ReportingStrategy(TestActor, Sys.Log)), "supervisor");

            sup.Tell(new Broadcast(Command.Reply));
            ReceiveN(childCount);

            // will cause grandchild to fail, but the router actor will be the one who shows up here
            // Supervision will be grandchild --> router (Escalate) --> supervisor (Resume)
            sup.Tell(Command.Fail);
            var failedActor = ExpectMsg<IActorRef>();

            // send a message back directly to the failed grandchild
            // and verify that the grandchild has been resumed
            sup.Tell(new Broadcast(Command.Reply));
            ReceiveN(childCount);

            Watch(failedActor);
            sup.Tell(new Broadcast(PoisonPill.Instance)); // should kill all routees and router itself
            ExpectTerminated(failedActor);
        }

        /// <summary>
        /// Reproduction spec for https://github.com/akkadotnet/akka.net/issues/3597
        /// </summary>
        [Fact]
        public void Bugfix3597_supervise_resume_4_layers_deep()
        {
            var childCount = 10;
            var supProps = Props.Create(() => new Supervisor(childCount)).WithSupervisorStrategy(new OneForOneStrategy(ex => Directive.Escalate));
            var sup = Sys.ActorOf(
                Props.Create(() => new Shell(supProps)).WithSupervisorStrategy(new ReportingStrategy(TestActor, Sys.Log)),
                "supervisor");

            for (var i = 0; i < childCount; i++)
                sup.Tell(Command.Reply);
            ReceiveN(childCount);

            // will cause grandchild to fail. 
            // Supervision will be grandchild --> router (Escalate) --> supervisor (Escalate) --> Shell (Resume)
            IActorRef failedActor = null;
            sup.Tell(Command.Fail);
            failedActor = ExpectMsg<IActorRef>();

            // send a message back directly to the failed grandchild
            // and verify that the grandchild has been resumed
            for (var i = 0; i < 100; i++)
            {
                if (i % 5 == 0)
                {
                    sup.Tell(Command.Fail);
                }
                sup.Tell(Command.Reply);
            }
                
            ReceiveN(122);


            Watch(failedActor);
            sup.Tell(new Broadcast(PoisonPill.Instance)); // should kill all routees
            ExpectTerminated(failedActor);
        }
    }
}
