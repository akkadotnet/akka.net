using System;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Routing
{
    public class RandomSpec : AkkaSpec
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
        public void Random_must_be_able_to_shut_down_its_instance()
        {
            const int routeeCount = 7;
            var testLatch = new TestLatch(Sys, routeeCount);
            var router = Sys.ActorOf(new RandomPool(routeeCount).Props(Props.Create(() => new HelloWorldActor(testLatch))));
            router.Tell("hello", TestActor);
            router.Tell("hello", TestActor);
            router.Tell("hello", TestActor);
            router.Tell("hello", TestActor);
            router.Tell("hello", TestActor);

            Within(TimeSpan.FromSeconds(2), () => {
                ExpectMsg("world");
                ExpectMsg("world");
                ExpectMsg("world");
                ExpectMsg("world");
                ExpectMsg("world");
                return true;
            });
            
            Sys.Stop(router);
            testLatch.Ready(TimeSpan.FromSeconds(5));
        }
    }
}
