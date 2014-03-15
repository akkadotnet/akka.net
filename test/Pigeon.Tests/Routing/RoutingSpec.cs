using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pigeon.Tests.Routing
{
    [TestClass]
    public class RoutingSpec : AkkaSpec
    {
        protected override string GetConfig()
        {
            return  @"
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
    }
    ";
        }

        public new class TestActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
               
            }
        }

        public class Echo : UntypedActor
        {

            protected override void OnReceive(object message)
            {
               Sender.Tell(Self);
            }
        }

        [TestMethod]
        public void EvictTerminatedRoutees()
        {
            var router = sys.ActorOf(new RoundRobinPool(2).Props(Props.Create<Echo>()));
            router.Tell("",testActor);
            router.Tell("",testActor);
            var c1 = expectMsgType<ActorRef>();
            var c2 = expectMsgType<ActorRef>();
            watch(router);
            watch(c2);
            sys.Stop(c2);
            expectTerminated(c2).ExistenceConfirmed.ShouldBe(true);
            // it might take a while until the Router has actually processed the Terminated message
            Task.Delay(100).Wait();
            router.Tell("", testActor);
            router.Tell("", testActor);
            expectMsgType<ActorRef>().ShouldBe(c1);           
            expectMsgType<ActorRef>().ShouldBe(c1);
            sys.Stop(c1);
            expectTerminated(router).ExistenceConfirmed.ShouldBe(true);
        }
    }
}