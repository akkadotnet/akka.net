//-----------------------------------------------------------------------
// <copyright file="BroadcastSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace Akka.Tests.Routing
{
    public class BroadcastSpec : AkkaSpec
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

        [Fact]
        public void BroadcastGroup_router_must_broadcast_message_using_Tell()
        {
            var doneLatch = new TestLatch(2);

            var counter1 = new AtomicCounter(0);
            var actor1 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter1)));

            var counter2 = new AtomicCounter(0);
            var actor2 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter2)));

            var paths = new List<string> { actor1.Path.ToString(), actor2.Path.ToString() };
            var routedActor = Sys.ActorOf(new BroadcastGroup(paths).Props());
            routedActor.Tell(new Broadcast(1));
            routedActor.Tell(new Broadcast("end"));

            doneLatch.Ready(RemainingOrDefault);

            counter1.Current.Should().Be(1);
            counter2.Current.Should().Be(1);
        }

        [Fact]
        public void BroadcastGroup_router_must_broadcast_message_using_Ask()
        {
            var doneLatch = new TestLatch(2);

            var counter1 = new AtomicCounter(0);
            var actor1 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter1)));

            var counter2 = new AtomicCounter(0);
            var actor2 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter2)));

            var paths = new List<string> { actor1.Path.ToString(), actor2.Path.ToString() };
            var routedActor = Sys.ActorOf(new BroadcastGroup(paths).Props());
            routedActor.Ask(new Broadcast(1));
            routedActor.Tell(new Broadcast("end"));

            doneLatch.Ready(RemainingOrDefault);

            counter1.Current.Should().Be(1);
            counter2.Current.Should().Be(1);
        }
    }
}
