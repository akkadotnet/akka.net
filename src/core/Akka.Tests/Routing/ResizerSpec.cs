//-----------------------------------------------------------------------
// <copyright file="ResizerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Xunit;
using FluentAssertions;

namespace Akka.Tests.Routing
{
    /// <summary>
    /// Used to test resizers for <see cref="Pool"/> routers.
    /// 
    /// Based upon https://github.com/akka/akka/blob/master/akka-actor-tests/src/test/scala/akka/routing/ResizerSpec.scala
    /// </summary>
    public class ResizerSpec : AkkaSpec
    {
        public ResizerSpec() : base(GetConfig())
        {
        }

        private static string GetConfig()
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
                new ActorRefRoutee(Sys.ActorOf<ResizerTestActor>()),
                new ActorRefRoutee(Sys.ActorOf<ResizerTestActor>())
            };
            Thread.Sleep(100);
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
            var latch = new TestLatch(3);
            var resizer = new DefaultResizer(2, 3);
            var router = Sys.ActorOf(Props.Create<ResizerTestActor>().WithRouter(new RoundRobinPool(0, resizer)));

            router.Tell(latch);
            router.Tell(latch);
            router.Tell(latch);

            latch.Ready(TestKitSettings.DefaultTimeout);

            //messagesPerResize is 10 so there is no risk of additional resize
            (RouteeSize(router)).ShouldBe(2);
        }

        [Fact]
        public void DefaultResizer_must_be_possible_to_define_in_configuration()
        {
            var latch = new TestLatch(3);
            var router = Sys.ActorOf(Props.Create<ResizerTestActor>().WithRouter(FromConfig.Instance), "router1");

            router.Tell(latch);
            router.Tell(latch);
            router.Tell(latch);

            latch.Ready(TestKitSettings.DefaultTimeout);

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

            var router = Sys.ActorOf(Props.Create<PressureActor>().WithRouter(new RoundRobinPool(0, resizer)));

            //first message should create the minimum number of routees
            router.Tell("echo", TestActor);
            ExpectMsg("reply");

            (RouteeSize(router)).ShouldBe(resizer.LowerBound);

            Action<int, TimeSpan, int?> loopTillAppropriateSize = (loops, span, expectedBound) =>
            {
                for (var i = 0; i < loops; i++)
                {
                    router.Tell(span, TestActor);
                    if (expectedBound.HasValue && RouteeSize(router) >= expectedBound.Value)
                    {
                        return;
                    }

                    //sending too quickly will result in skipped resize due to many resizeInProgress conflicts
                    Thread.Sleep(TimeSpan.FromMilliseconds(20));
                }
            };

            // 2 more should go through without triggering more
            loopTillAppropriateSize(2, TimeSpan.FromMilliseconds(200), null);
            RouteeSize(router).ShouldBe(resizer.LowerBound);

            // a whole bunch should max it out
            loopTillAppropriateSize(200, TimeSpan.FromMilliseconds(500), resizer.UpperBound);
            RouteeSize(router).ShouldBe(resizer.UpperBound);
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

        [Fact]
        public void DefaultResizer_must_backoff()
        {
            Within(TimeSpan.FromSeconds(10), () =>
            {   
               var resizer = new DefaultResizer(2, 5, pressureThreshold: 1, rampupRate: 1.0d, backoffRate: 1.0d,
               messagesPerResize: 2, backoffThreshold: 0.4d);

                var router = Sys.ActorOf(Props.Create<BackoffActor>().WithRouter(new RoundRobinPool(0, resizer)));

                // put some pressure on the router
                for (var i = 0; i < 200; i++)
                {
                    router.Tell(150);
                    if (RouteeSize(router) > 2) 
                        break;

                    Thread.Sleep(20);
                }

                var z = RouteeSize(router);
                Assert.True(z > 2);
                Thread.Sleep(300);

                // let it cool down
                AwaitAssert(() =>
                {
                    router.Tell(0); //trigger resize
                    Thread.Sleep(20);
                    RouteeSize(router).Should().BeLessThan(z);
                }, null, TimeSpan.FromSeconds(1));
            });
        }

        #region Internal methods

        private static int RouteeSize(IActorRef router)
        {
            return ((RoutedActorRef) router).Children.Count();
        }

        #endregion
    }
}

