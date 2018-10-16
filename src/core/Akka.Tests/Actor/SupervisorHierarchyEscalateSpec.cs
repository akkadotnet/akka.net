using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class SupervisorHierarchyEscalateSpec : AkkaSpec
    {
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
            public Failer()
            {
                Receive<Command>(command => command == Command.Fail, command => throw new CustomException("fail"));
                Receive<Command>(command => command == Command.Reply, command => Sender.Tell("reply"));
            }
        }

        class Supervisor : ReceiveActor
        {
            private readonly IActorRef _testActor;
            private readonly int _routeeCount;
            private IActorRef _router;

            public Supervisor(IActorRef testActor, int routeeCount)
            {
                _testActor = testActor;
                _routeeCount = routeeCount;

                ReceiveAny(_ => _router.Tell(_));
            }

            protected override void PreStart()
            {
                _router = Context.ActorOf(Props.Create(() => new Failer()).WithRouter(new RoundRobinPool(_routeeCount))
                    .WithSupervisorStrategy(new OneForOneStrategy(ex => Directive.Escalate)), "router");
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(ex =>
                {
                    if (ex is CustomException)
                    {
                        _testActor.Tell(Sender);
                        return Directive.Resume;
                    }

                    return Directive.Stop;
                });
            }
        }

        /// <summary>
        /// Reproduction spec for https://github.com/akkadotnet/akka.net/issues/3597
        /// </summary>
        [Fact]
        public void Bugfix3597_supervise_resume()
        {
            var childCount = 10;
            var sup = Sys.ActorOf(Props.Create(() => new Supervisor(TestActor, childCount)), "supervisor");

            sup.Tell(new Broadcast(Command.Reply));
            ReceiveN(childCount);

            sup.Tell(Command.Fail);
            var failedActor = ExpectMsg<IActorRef>();
        }
    }
}
