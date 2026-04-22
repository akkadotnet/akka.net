//-----------------------------------------------------------------------
// <copyright file="ActorTelemetrySpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class ActorTelemetrySpecs : AkkaSpec
    {
        public static readonly Config WithTelemetry = @"akka.actor.telemetry.enabled = on";

        public ActorTelemetrySpecs(ITestOutputHelper output) : base(WithTelemetry, output)
        {
        }

        // create an actor that will subscribe to all of the IActorTelemetryEvents in the EventStream
        private class TelemetrySubscriber : ReceiveActor
        {
            // keep track of integer counters for each event type
            private int _actorCreated;
            private int _actorStopped;
            private int _actorRestarted;

            // create a message type that will send the current values of all counters
            public sealed record GetTelemetry(int ActorCreated, int ActorStopped, int ActorRestarted);
            
            public class GetTelemetryRequest
            {
                // make singleton
                public static readonly GetTelemetryRequest Instance = new();
                private GetTelemetryRequest() { }
            }

            public TelemetrySubscriber()
            {
                // Receive each type of IActorTelemetryEvent
                Receive<ActorStarted>(_ => { _actorCreated++; });
                Receive<ActorStopped>(_ => { _actorStopped++; });
                Receive<ActorRestarted>(_ => { _actorRestarted++; });
                // receive a request for current counter values and return a GetTelemetry result
                Receive<GetTelemetryRequest>(_ => Sender.Tell(new GetTelemetry(_actorCreated, _actorStopped, _actorRestarted)));
            }

            protected override void PreStart()
            {
                Context.System.EventStream.Subscribe(Self, typeof(IActorTelemetryEvent));
            }
        }

        // CreateChildren message type
        public class CreateChildren
        {
            public CreateChildren(int count)
            {
                Count = count;
            }

            public int Count { get; }
        }

        // create a RestartChildren message type
        public class RestartChildren
        {
            // make singleton
            public static readonly RestartChildren Instance = new();

            private RestartChildren()
            {
            }
        }

        // an actor that will spawn a configurable number of child actors
        private class ParentActor : ReceiveActor
        {
            public ParentActor()
            {
                // handle a command that will spawn N children
                Receive<CreateChildren>(cmd =>
                {
                    for (var i = 0; i < cmd.Count; i++)
                    {
                        Context.ActorOf(Props.Create<ChildActor>(), $"child-{i}");
                    }

                    // reply back to sender once complete
                    Sender.Tell("done");
                });

                // forward a restart command to all children
                Receive<RestartChildren>(cmd =>
                {
                    foreach (var child in Context.GetChildren())
                    {
                        child.Forward(cmd);
                    }
                    
                    // reply back to sender once complete
                    Sender.Tell("done");
                });
                
                // handle a command that causes the parent actor to restart
                Receive<string>(cmd =>
                {
                    if (cmd == "restart")
                    {
                        throw new Exception("Restarting");
                    }
                });
            }
        }

        // create the ChildActor implementation
        private class ChildActor : ReceiveActor
        {
            public ChildActor()
            {
                // handle a command that forces a restart
                Receive<RestartChildren>(_ => { throw new ApplicationException("Restarting"); });
                ReceiveAny(_ => { });
            }
        }

        private static async Task WaitUntilStableAsync(IActorRef subscriber, TimeSpan timeout)
        {
            var start = DateTime.Now;
            var end = start + timeout;

            var last = new TelemetrySubscriber.GetTelemetry(0, 0, 0);
            
            var count = 0;
            while (DateTime.Now < end)
            {
                var reply = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                if (reply == last)
                {
                    count++;
                    if (count >= 3)
                        return;
                }
                else
                {
                    count = 0;
                }
                last = reply;
                await Task.Delay(200);
            }
            
            throw new Exception($"Failed to wait for a stable actor telemetry count after {DateTime.Now - start}. Timeout: {timeout}");
        }
        
        [Fact]
        public async Task ActorTelemetry_must_be_accurate()
        {
            // create a TelemetrySubscriber actor
            var subscriber = Sys.ActorOf(Props.Create<TelemetrySubscriber>(), "subscriber");
            
            // wait until telemetry value is stable
            await WaitUntilStableAsync(subscriber, TimeSpan.FromSeconds(5));
            
            // request current telemetry values (ensure that the actor has started, so counter values will be accurate)
            var baseline = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
            
            // create a parent actor
            var parent = Sys.ActorOf(Props.Create<ParentActor>(), "parent");

            // send a message to the parent to create 100 children
            parent.Tell(new CreateChildren(100));

            // wait for the parent to reply back
            ExpectMsg("done");
            
            // awaitassert collecting data from the telemetry subscriber until we can see that 101 actors have been created
            // 100 children + parent
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                Assert.Equal(101, telemetry.ActorCreated - baseline.ActorCreated);
                // assert no restarts or stops recorded
                Assert.Equal(0, telemetry.ActorRestarted - baseline.ActorRestarted);
                Assert.Equal(0, telemetry.ActorStopped - baseline.ActorStopped);
            }, RemainingOrDefault);
            
            // send a message to the parent to restart all children
            parent.Tell(RestartChildren.Instance);
            
            // wait for the parent to reply back
            ExpectMsg("done");

            // await assert collecting data from the telemetry subscriber until we can see that 102 actors have been restarted
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                // assert that actor start count is still 102
                Assert.Equal(101, telemetry.ActorCreated - baseline.ActorCreated);
                Assert.Equal(100, telemetry.ActorRestarted - baseline.ActorRestarted);
                // assert no stops recorded
                Assert.Equal(0, telemetry.ActorStopped - baseline.ActorStopped);
            }, RemainingOrDefault);
            
            // GracefulStop parent actor and assert that 101 actors have been stopped
            await parent.GracefulStop(RemainingOrDefault);
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                // assert that actor start count is still 102
                Assert.Equal(101, telemetry.ActorCreated - baseline.ActorCreated);
                Assert.Equal(100, telemetry.ActorRestarted - baseline.ActorRestarted);
                Assert.Equal(101, telemetry.ActorStopped - baseline.ActorStopped);
            }, RemainingOrDefault);
        }
        
        // create a unit test where a parent actor spawns 100 children and then restarts
        [Fact]
        public async Task ActorTelemetry_must_be_accurate_when_parent_restarts()
        {
            // create a TelemetrySubscriber actor
            var subscriber = Sys.ActorOf(Props.Create<TelemetrySubscriber>(), "subscriber");
            
            await WaitUntilStableAsync(subscriber, TimeSpan.FromSeconds(5));
            
            // request current telemetry values (ensure that the actor has started, so counter values will be accurate)
            var baseline = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
            
            // create a parent actor
            var parent = Sys.ActorOf(Props.Create<ParentActor>(), "parent");

            // send a message to the parent to create 100 children
            parent.Tell(new CreateChildren(100));

            // wait for the parent to reply back
            ExpectMsg("done");
            
            // awaitassert collecting data from the telemetry subscriber until we can see that 102 actors have been created
            // 100 children + parent
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                Assert.Equal(101, telemetry.ActorCreated - baseline.ActorCreated);
                // assert no restarts or stops recorded
                Assert.Equal(0, telemetry.ActorRestarted - baseline.ActorRestarted);
                Assert.Equal(0, telemetry.ActorStopped - baseline.ActorStopped);
            }, RemainingOrDefault);
            
            // send a message to the parent to restart
            parent.Tell("restart");
            
            // await assert collecting data from the telemetry subscriber until we can see that 102 actors have been restarted
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                // assert that actor start count is still 101
                Assert.Equal(101, telemetry.ActorCreated - baseline.ActorCreated);
                
                // only 1 parent restart recorded
                Assert.Equal(1, telemetry.ActorRestarted - baseline.ActorRestarted);
                // assert 100 stops recorded (only the child actors)
                Assert.Equal(100, telemetry.ActorStopped - baseline.ActorStopped);
            }, RemainingOrDefault);
        }
        
        /// <summary>
        /// Pool routers should have their start / stop / restarts counted too
        /// </summary>
        [Fact]
        public async Task ActorTelemetry_must_be_accurate_for_pool_router()
        {
            // create a TelemetrySubscriber actor
            var subscriber = Sys.ActorOf(Props.Create<TelemetrySubscriber>(), "subscriber");
            
            await WaitUntilStableAsync(subscriber, TimeSpan.FromSeconds(5));
            
            // request current telemetry values (ensure that the actor has started, so counter values will be accurate)
            var baseline = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
            
            // create a pool router
            var router = Sys.ActorOf(Props.Create<ChildActor>().WithRouter(new RoundRobinPool(10)), "router");

            // awaitassert collecting data from the telemetry subscriber until we can see that 10 actors have been created
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                Assert.Equal(11, telemetry.ActorCreated - baseline.ActorCreated);
                // assert no restarts or stops recorded
                Assert.Equal(0, telemetry.ActorRestarted - baseline.ActorRestarted);
                Assert.Equal(0, telemetry.ActorStopped - baseline.ActorStopped);
            }, RemainingOrDefault);
            
            // send a message to the router to restart all children
            router.Tell(new Broadcast(RestartChildren.Instance));

            // await assert collecting data from the telemetry subscriber until we can see that 10 actors have been restarted
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                // assert that actor start count is still 10
                Assert.Equal(11, telemetry.ActorCreated - baseline.ActorCreated);
                
                Assert.Equal(10, telemetry.ActorRestarted - baseline.ActorRestarted);
                // assert no stops recorded
                Assert.Equal(0, telemetry.ActorStopped - baseline.ActorStopped);
            }, RemainingOrDefault);
            
            // GracefulStop router actor and assert that 10 actors have been stopped
            await router.GracefulStop(RemainingOrDefault);
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                // assert that actor start count is still 10
                Assert.Equal(11, telemetry.ActorCreated - baseline.ActorCreated);
                Assert.Equal(10, telemetry.ActorRestarted - baseline.ActorRestarted);
                Assert.Equal(11, telemetry.ActorStopped - baseline.ActorStopped);
            }, RemainingOrDefault);
        }
    }
}

