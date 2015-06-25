using System;
using Akka.Actor;
using Akka.Pattern;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Pattern
{
    public class BackoffSupervisorSpec : AkkaSpec
    {
        internal class Child : ReceiveActor
        {
            public Child(IActorRef probe)
            {
                ReceiveAny(msg => probe.Tell(msg));
            }
        }

        [Fact]
        public void BackoffSupervisor_should_start_child_again_when_it_stops()
        {
            var childProps = Props.Create(() => new Child(TestActor));
            var supervisor = Sys.ActorOf(Props.Create(() =>
                new BackoffSupervisor(childProps, "c1", TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(3), 0.2)));

            supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
            var c1 = ExpectMsg<BackoffSupervisor.CurrentChild>().Ref;

            Watch(c1);
            c1.Tell(PoisonPill.Instance);
            ExpectTerminated(c1);

            AwaitAssert(() =>
            {
                supervisor.Tell(BackoffSupervisor.GetCurrentChild.Instance);
                // new instance
                Assert.NotEqual(c1, ExpectMsg<BackoffSupervisor.CurrentChild>().Ref);
            });
        }

        [Fact]
        public void BackoffSupervisor_should_forward_messages_to_the_child()
        {
            var childProps = Props.Create(() => new Child(TestActor));
            var supervisor = Sys.ActorOf(Props.Create(() => 
                new BackoffSupervisor(childProps, "c2", TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(3), 0.2)));

            supervisor.Tell("hello");
            ExpectMsg("hello");
        }
    }
}