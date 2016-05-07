using System;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Routing
{
    public class RouteeCreationSpec : AkkaSpec
    {
        private class RouteeActor : ReceiveActor
        {
            public RouteeActor(IActorRef testActor)
            {
                Context.ActorSelection(Self.Path).Tell(new Identify(Self.Path), testActor);
            }
        }

        private class ForwardActor : ReceiveActor
        {
            private readonly IActorRef _testActor;

            public ForwardActor(IActorRef testActor)
            {
                _testActor = testActor;
                Context.Parent.Tell("one");

                Receive<string>(s => s == "one", c =>
                {
                    _testActor.Forward("two");
                });
            }
        }

        [Fact]
        public void Creating_routees_must_result_in_visible_routees()
        {
            int n = 100;
            Sys.ActorOf(new RoundRobinPool(n).Props(Props.Create(() => new RouteeActor(TestActor))));

            for (int i = 0; i < n; i++)
            {
                ExpectMsg<ActorIdentity>().Subject.ShouldNotBe(null);
            }
        }

        [Fact]
        public void Creating_routees_must_allow_sending_to_context_parent()
        {
            int n = 100;
            Sys.ActorOf(new RoundRobinPool(n).Props(Props.Create(() => new ForwardActor(TestActor))));

            var gotIt = ReceiveN(n);
            ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            gotIt.Count.ShouldBe(n, $"got only {gotIt.Count} from [{string.Join(", ", gotIt)}]");
        }
    }
}
