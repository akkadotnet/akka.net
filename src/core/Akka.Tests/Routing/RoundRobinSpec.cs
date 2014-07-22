using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Akka.Tests.Routing
{
    public class RoundRobinSpec : AkkaSpec
    {
        public class HelloWorldActor : UntypedActor
        {
            private TestLatch _testLatch;
            public HelloWorldActor(TestLatch testLatch)
            {
                _testLatch = testLatch;
            }

            protected override void OnReceive(object message)
            {
                if (message.Equals("hello"))
                {
                    Sender.Tell("world");
                }
            }

            protected override void PostStop()
            {
                _testLatch.CountDown();
            }
        }

        [Fact]
        public void RoundRobin_must_be_able_to_shut_down_its_instance()
        {
            const int routeeCount = 7;
            var testLatch = new TestLatch(sys, routeeCount);
            var router = sys.ActorOf(new RoundRobinPool(routeeCount).Props(Props.Create(() => new HelloWorldActor(testLatch))));
            router.Tell("hello", testActor);
            router.Tell("hello", testActor);
            router.Tell("hello", testActor);
            router.Tell("hello", testActor);
            router.Tell("hello", testActor);

            Within(TimeSpan.FromSeconds(2), () =>
            {
                expectMsg("world");
                expectMsg("world");
                expectMsg("world");
                expectMsg("world");
                expectMsg("world");
                return true;
            });

            sys.Stop(router);
            testLatch.Ready(TimeSpan.FromSeconds(5));
        }
    }
}
