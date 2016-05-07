//-----------------------------------------------------------------------
// <copyright file="BroadcastSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Routing
{
    public class BroadcastSpec : AkkaSpec
    {
        public class BroadcastTarget : UntypedActor
        {
            private AtomicCounter _counter;
            private TestLatch _latch;
            public BroadcastTarget(TestLatch latch, AtomicCounter counter)
            {
                _latch = latch;
                _counter = counter;
            }
            protected override void OnReceive(object message)
            {
                if (message is string)
                {
                    var s = (string)message;
                    if (s == "end")
                    {
                        _latch.CountDown();
                    }
                }
                if (message is int)
                {
                    var i = (int)message;
                    _counter.GetAndAdd(i);
                }
            }
        }
        
        [Fact]
        public void BroadcastGroup_router_must_broadcast_message_using_Tell()
        {
            var doneLatch = new TestLatch(2);
            var counter1 = new AtomicCounter(0);
            var counter2 = new AtomicCounter(0);
            var actor1 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter1)));
            var actor2 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter2)));

            var routedActor = Sys.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(actor1.Path.ToString(), actor2.Path.ToString())));
            routedActor.Tell(new Broadcast(1));
            routedActor.Tell(new Broadcast("end"));

            doneLatch.Ready(RemainingOrDefault);

            counter1.Current.ShouldBe(1);
            counter2.Current.ShouldBe(1);
        }

        [Fact]
        public void BroadcastGroup_router_must_broadcast_message_using_Ask()
        {
            var doneLatch = new TestLatch(2);
            var counter1 = new AtomicCounter(0);
            var counter2 = new AtomicCounter(0);
            var actor1 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter1)));
            var actor2 = Sys.ActorOf(Props.Create(() => new BroadcastTarget(doneLatch, counter2)));

            var routedActor = Sys.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(actor1.Path.ToString(), actor2.Path.ToString())));
            routedActor.Ask(new Broadcast(1));
            routedActor.Tell(new Broadcast("end"));

            doneLatch.Ready(RemainingOrDefault);

            counter1.Current.ShouldBe(1);
            counter2.Current.ShouldBe(1);
        }
    }
}

