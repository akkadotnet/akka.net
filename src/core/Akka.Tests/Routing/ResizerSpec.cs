using System;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Routing
{
    /// <summary>
    /// Used to test resizers for <see cref="Pool"/> routers.
    /// 
    /// Based upon https://github.com/akka/akka/blob/master/akka-actor-tests/src/test/scala/akka/routing/ResizerSpec.scala
    /// </summary>
    public class ResizerSpec : AkkaSpec
    {
        protected override string GetConfig()
        {
            return @"
            akka.actor.serialize-messages = off
            akka.actor.deployment {
            /router1 {
                router = round-robin-pool
                    resizer {
                        enabled = on
                        lower-bound = 2
                        upper-bound = 3
                    }
                }
            }";
        }

        class ResizerTestActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                message.Match().With<TestLatch>(latch => latch.CountDown());
            }
        }

        [Fact]
        public void DefaultResizer_must_use_settings_to_evaluate_capacity()
        {
            var resizer = new DefaultResizer(2, 3);
            var c1 = resizer.Capacity(new Routee[] { });
            c1.ShouldBe(2);

            var current = new Routee[]
            {
                new ActorRefRoutee(System.ActorOf<ResizerTestActor>()),
                new ActorRefRoutee(System.ActorOf<ResizerTestActor>())
            };
            var c2 = resizer.Capacity(current);
            c2.ShouldBe(0);
        }

        [Fact]
        public void DefaultResizer_must_use_settings_to_evaluate_rampup()
        {
            // ReSharper disable once RedundantArgumentDefaultValue (exposing the values we're testing makes this test understandable.)
            var resizer = new DefaultResizer(2, 10, rampupRate: 0.2d);

            resizer.Rampup(9, 10).ShouldBe(0);
            resizer.Rampup(5, 5).ShouldBe(1);
            resizer.Rampup(6, 6).ShouldBe(2);
        }

        [Fact]
        public void DefaultResizer_must_use_settings_to_evaluate_backoff()
        {
            // ReSharper disable RedundantArgumentDefaultValue (exposing the values we're testing makes this test understandable.)
            var resizer = new DefaultResizer(2, 10, backoffThreshold: 0.3d, backoffRate: 0.1d);
            // ReSharper restore RedundantArgumentDefaultValue

            resizer.Backoff(10, 10).ShouldBe(0);
            resizer.Backoff(4, 10).ShouldBe(0);
            resizer.Backoff(3, 10).ShouldBe(0);
            resizer.Backoff(2, 10).ShouldBe(-1);
            resizer.Backoff(0, 10).ShouldBe(-1);
            resizer.Backoff(1, 9).ShouldBe(-1);
            resizer.Backoff(0, 9).ShouldBe(-1);
        }

        [Fact]
        public void DefaultResizer_must_be_possible_to_define_programmatically()
        {
            var latch = new TestLatch(sys, 3);
            var resizer = new DefaultResizer(2, 3);
            var router = sys.ActorOf(Props.Create<ResizerTestActor>().WithRouter(new RoundRobinPool(0, resizer)));

            router.Tell(latch);
            router.Tell(latch);
            router.Tell(latch);

            latch.Ready(DefaultTimeout);

            //messagesPerResize is 10 so there is no risk of additional resize
            (RouteeSize(router)).ShouldBe(2);
        }

        [Fact]
        public void DefaultResizer_must_be_possible_to_define_in_configuration()
        {
            var latch = new TestLatch(sys, 3);
            var router = sys.ActorOf(Props.Create<ResizerTestActor>().WithRouter(new FromConfig()), "router1");

            router.Tell(latch);
            router.Tell(latch);
            router.Tell(latch);

            latch.Ready(DefaultTimeout);

            //messagesPerResize is 10 so there is no risk of additional resize
            (RouteeSize(router)).ShouldBe(2);
        }

        class PressureActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                message.Match().With<TimeSpan>(
                    d =>
                    {
                        Thread.Sleep(d);
                        Sender.Tell("done");
                    })
                    .With<string>(s =>
                    {
                        if (s.Equals("echo"))
                            Sender.Tell("reply");
                    });
            }
        }

        [Fact]
        public void DefaultResizer_must_grow_as_needed_under_pressure()
        {
            var resizer = new DefaultResizer(3, 5, pressureThreshold: 1, rampupRate: 0.1d, backoffRate: 0.0d,
                messagesPerResize: 1, backoffThreshold: 0.0d);

            var router = sys.ActorOf(Props.Create<PressureActor>().WithRouter(new RoundRobinPool(0, resizer)));

            //first message should create the minimum number of routees
            router.Tell("echo", testActor);
            expectMsg("reply", TimeSpan.FromSeconds(1));

            (RouteeSize(router)).ShouldBe(resizer.LowerBound);

            Action<int, TimeSpan> loop = (loops, span) =>
            {
                for (var i = 0; i < loops; i++)
                {
                    router.Tell(span, testActor);
                    //sending too quickly will result in skipped resize due to many resizeInProgress conflicts
                    Thread.Sleep(TimeSpan.FromMilliseconds(20));
                }
                Within(
                    TimeSpan.FromMilliseconds(span.TotalMilliseconds * loops / resizer.LowerBound) + TimeSpan.FromSeconds(2.5),
                    () =>
                    {
                        for (var i = 0; i < loops; i++) expectMsg("done");
                        return true;
                    });
            };



            // 2 more should go through without triggering more
            loop(2, TimeSpan.FromMilliseconds(200));
            (RouteeSize(router)).ShouldBe(resizer.LowerBound);


            // a whole bunch should max it out
            loop(20, TimeSpan.FromMilliseconds(500));
            (RouteeSize(router)).ShouldBe(resizer.UpperBound);

        }

        class BackoffActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message is int)
                {
                    var i = (int) message;
                    if (i <= 0) return; //done
                    Thread.Sleep(i);
                }
            }
        }

        [Fact(Timeout = 10000)]
        public void DefaultResizer_must_backoff_within_10_seconds()
        {
            var resizer = new DefaultResizer(2, 5, pressureThreshold: 1, rampupRate: 1.0d, backoffRate: 1.0d,
               messagesPerResize: 2, backoffThreshold: 0.4d);

            var router = sys.ActorOf(Props.Create<BackoffActor>().WithRouter(new RoundRobinPool(0, resizer)));

            // put some pressure on the router
            for (var i = 0; i < 15; i++)
            {
                router.Tell(150);
                Thread.Sleep(20);
            }

            var z = RouteeSize(router);
            Assert.True(z > 2);
            Thread.Sleep(300);

            // let it cool down
            AwaitCond(() =>
            {
                router.Tell(0); //trigger resize
                Thread.Sleep(20);
                return RouteeSize(router) < z;
            }, TimeSpan.FromMilliseconds(500)).Wait();
        }

        #region Internal methods

        private int RouteeSize(ActorRef router)
        {
            var routeesTask = router.Ask<Routees>(new GetRoutees(), DefaultTimeout);
            routeesTask.Wait(DefaultTimeout);
            return routeesTask.Result.Members.Count();
        }

        #endregion
    }
}
