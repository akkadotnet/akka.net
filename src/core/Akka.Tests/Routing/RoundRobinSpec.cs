//-----------------------------------------------------------------------
// <copyright file="RoundRobinSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
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
            var testLatch = new TestLatch(routeeCount);
            var router = Sys.ActorOf(new RoundRobinPool(routeeCount).Props(Props.Create(() => new HelloWorldActor(testLatch))));
            router.Tell("hello", TestActor);
            router.Tell("hello", TestActor);
            router.Tell("hello", TestActor);
            router.Tell("hello", TestActor);
            router.Tell("hello", TestActor);

            Within(TimeSpan.FromSeconds(2), () =>
            {
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

