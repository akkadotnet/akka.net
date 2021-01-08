//-----------------------------------------------------------------------
// <copyright file="ScatterGatherFirstCompletedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Routing
{
    public class ScatterGatherFirstCompletedSpec : AkkaSpec
    {
        private class BroadcastTarget : ReceiveActor
        {
            private readonly AtomicCounter _counter;
            private readonly TestLatch _doneLatch;

            public BroadcastTarget(TestLatch doneLatch, AtomicCounter counter)
            {
                _doneLatch = doneLatch;
                _counter = counter;

                Receive<string>(s => s == "end", c => _doneLatch.CountDown());
                Receive<int>(msg => _counter.AddAndGet(msg));
            }
        }

        private class Stop
        {
            public Stop(int? id = null)
            {
                Id = id;
            }

            public int? Id { get; }
        }

        private class StopActor : ReceiveActor
        {
            private readonly int _id;
            private readonly TestLatch _shutdownLatch;

            public StopActor(int id, TestLatch shutdownLatch)
            {
                _id = id;
                _shutdownLatch = shutdownLatch;

                Receive<Stop>(s =>
                {
                    if (s.Id == null || s.Id == _id)
                    {
                        Context.Stop(Self);
                    }
                });

                Receive<int>(n => n == _id, _ =>
                {

                });

                ReceiveAny(x =>
                {
                    Thread.Sleep(100 * _id);
                    Sender.Tell(_id);
                });
            }

            protected override void PostStop()
            {
                _shutdownLatch.CountDown();
            }
        }

        [Fact]
        public void Scatter_gather_group_must_deliver_a_broadcast_message_using_tell()
        {
            var doneLatch = new TestLatch(2);

            var counter1 = new AtomicCounter(0);
            var actor1 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter1)));

            var counter2 = new AtomicCounter(0);
            var actor2 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter2)));

            var paths = new List<string> { actor1.Path.ToString(), actor2.Path.ToString() };
            var routedActor = Sys.ActorOf(new ScatterGatherFirstCompletedGroup(paths, TimeSpan.FromSeconds(1)).Props());
            routedActor.Tell(new Broadcast(1));
            routedActor.Tell(new Broadcast("end"));

            doneLatch.Ready(TestKitSettings.DefaultTimeout);

            counter1.Current.Should().Be(1);
            counter2.Current.Should().Be(1);
        }

        [Fact]
        public async Task Scatter_gather_group_must_return_response_even_if_one_of_the_actors_has_stopped()
        {
            var shutdownLatch = new TestLatch(1);

            var actor1 = Sys.ActorOf(Props.Create(() => new StopActor(1, shutdownLatch)));
            var actor2 = Sys.ActorOf(Props.Create(() => new StopActor(14, shutdownLatch)));

            var paths = new List<string> { actor1.Path.ToString(), actor2.Path.ToString() };
            var routedActor = Sys.ActorOf(new ScatterGatherFirstCompletedGroup(paths, TimeSpan.FromSeconds(3)).Props());

            routedActor.Tell(new Broadcast(new Stop(1)));
            shutdownLatch.Ready(TestKitSettings.DefaultTimeout);
            var res = await routedActor.Ask<int>(0, TimeSpan.FromSeconds(10));
            res.Should().Be(14);
        }

        [Fact]
        public void Scatter_gather_pool_must_without_routees_should_reply_immediately()
        {
            var probe = CreateTestProbe();
            var routedActor = Sys.ActorOf(new ScatterGatherFirstCompletedPool(0, TimeSpan.FromSeconds(5)).Props(Props.Empty));
            routedActor.Tell("hello", probe.Ref);
            var message = probe.ExpectMsg<Status.Failure>(2.Seconds());
            message.Should().NotBeNull();
            message.Cause.Should().BeOfType<AskTimeoutException>();
        }

        // Resolved https://github.com/akkadotnet/akka.net/issues/1718
        [Fact]
        public void Scatter_gather_group_must_only_return_one_response()
        {
            var actor1 = Sys.ActorOf(Props.Create(() => new StopActor(1, null)));
            var actor2 = Sys.ActorOf(Props.Create(() => new StopActor(14, null)));

            var paths = new List<string> { actor1.Path.ToString(), actor2.Path.ToString() };
            var routedActor = Sys.ActorOf(new ScatterGatherFirstCompletedGroup(paths, TimeSpan.FromSeconds(3)).Props());

            routedActor.Tell(0);

            ExpectMsg<int>();
            ExpectNoMsg();
        }

        // Resolved https://github.com/akkadotnet/akka.net/issues/1718
        [Fact]
        public async Task Scatter_gather_group_must_handle_failing_timeouts()
        {
            var actor1 = Sys.ActorOf(Props.Create(() => new StopActor(50, null)));

            var paths = new List<string> { actor1.Path.ToString() };
            var routedActor = Sys.ActorOf(new ScatterGatherFirstCompletedGroup(paths, TimeSpan.FromSeconds(3)).Props());

            var exception = await routedActor.Ask<Status.Failure>(0, TimeSpan.FromSeconds(5));
            exception.Should().NotBeNull();
            exception.Cause.Should().BeOfType<AskTimeoutException>();
        }
    }
}
