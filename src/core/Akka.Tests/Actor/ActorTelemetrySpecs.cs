//-----------------------------------------------------------------------
// <copyright file="ActorTelemetrySpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

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
            public sealed class GetTelemetry
            {
                public int ActorCreated { get; }
                public int ActorStopped { get; }
                public int ActorRestarted { get; }

                public GetTelemetry(int actorCreated, int actorStopped, int actorRestarted)
                {
                    ActorCreated = actorCreated;
                    ActorStopped = actorStopped;
                    ActorRestarted = actorRestarted;
                }
            }
            
            public class GetTelemetryRequest
            {
                // make singleton
                public static readonly GetTelemetryRequest Instance = new GetTelemetryRequest();
                private GetTelemetryRequest() { }
            }

            public TelemetrySubscriber()
            {
                // Receive each type of IActorTelemetryEvent
                Receive<ActorStarted>(e => { _actorCreated++; });
                Receive<ActorStopped>(e => { _actorStopped++; });
                Receive<ActorRestarted>(e => { _actorRestarted++; });
                // receive a request for current counter values and return a GetTelemetry result
                Receive<GetTelemetryRequest>(e => Sender.Tell(new GetTelemetry(_actorCreated, _actorStopped, _actorRestarted)));
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
            public static readonly RestartChildren Instance = new RestartChildren();

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
                Receive<RestartChildren>(cmd => { throw new ApplicationException("Restarting"); });
                ReceiveAny(_ => { });
            }
        }

        [Fact]
        public async Task ActorTelemetry_must_be_accurate()
        {
            // create a TelemetrySubscriber actor
            var subscriber = Sys.ActorOf(Props.Create<TelemetrySubscriber>(), "subscriber");
            
            // request current telemetry values (ensure that the actor has started, so counter values will be accurate)
            await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
            
            // create a parent actor
            var parent = Sys.ActorOf(Props.Create<ParentActor>(), "parent");

            // send a message to the parent to create 100 children
            parent.Tell(new CreateChildren(100));

            // wait for the parent to reply back
            ExpectMsg("done");
            
            // awaitassert collecting data from the telemetry subscriber until we can see that 102 actors have been created
            // 100 children + parent + subscriber itself
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                Assert.Equal(102, telemetry.ActorCreated);
                // assert no restarts or stops recorded
                Assert.Equal(0, telemetry.ActorRestarted);
                Assert.Equal(0, telemetry.ActorStopped);
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
                Assert.Equal(102, telemetry.ActorCreated);
                Assert.Equal(100, telemetry.ActorRestarted);
                // assert no stops recorded
                Assert.Equal(0, telemetry.ActorStopped);
            }, RemainingOrDefault);
            
            // GracefulStop parent actor and assert that 101 actors have been stopped
            await parent.GracefulStop(RemainingOrDefault);
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                // assert that actor start count is still 102
                Assert.Equal(102, telemetry.ActorCreated);
                Assert.Equal(100, telemetry.ActorRestarted);
                Assert.Equal(101, telemetry.ActorStopped);
            }, RemainingOrDefault);
        }
        
        // create a unit test where a parent actor spawns 100 children and then restarts
        [Fact]
        public async Task ActorTelemetry_must_be_accurate_when_parent_restarts()
        {
            // create a TelemetrySubscriber actor
            var subscriber = Sys.ActorOf(Props.Create<TelemetrySubscriber>(), "subscriber");
            
            // request current telemetry values (ensure that the actor has started, so counter values will be accurate)
            await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
            
            // create a parent actor
            var parent = Sys.ActorOf(Props.Create<ParentActor>(), "parent");

            // send a message to the parent to create 100 children
            parent.Tell(new CreateChildren(100));

            // wait for the parent to reply back
            ExpectMsg("done");
            
            // awaitassert collecting data from the telemetry subscriber until we can see that 102 actors have been created
            // 100 children + parent + subscriber itself
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                Assert.Equal(102, telemetry.ActorCreated);
                // assert no restarts or stops recorded
                Assert.Equal(0, telemetry.ActorRestarted);
                Assert.Equal(0, telemetry.ActorStopped);
            }, RemainingOrDefault);
            
            // send a message to the parent to restart
            parent.Tell("restart");
            
            // await assert collecting data from the telemetry subscriber until we can see that 102 actors have been restarted
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                // assert that actor start count is still 102
                Assert.Equal(102, telemetry.ActorCreated);
                
                // only 1 parent restart recorded
                Assert.Equal(1, telemetry.ActorRestarted);
                // assert 100 stops recorded (only the child actors)
                Assert.Equal(100, telemetry.ActorStopped);
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
            
            // request current telemetry values (ensure that the actor has started, so counter values will be accurate)
            await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
            
            // create a pool router
            var router = Sys.ActorOf(Props.Create<ChildActor>().WithRouter(new RoundRobinPool(10)), "router");

            // awaitassert collecting data from the telemetry subscriber until we can see that 10 actors have been created
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                Assert.Equal(12, telemetry.ActorCreated);
                // assert no restarts or stops recorded
                Assert.Equal(0, telemetry.ActorRestarted);
                Assert.Equal(0, telemetry.ActorStopped);
            }, RemainingOrDefault);
            
            // send a message to the router to restart all children
            router.Tell(new Broadcast(RestartChildren.Instance));

            // await assert collecting data from the telemetry subscriber until we can see that 10 actors have been restarted
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                // assert that actor start count is still 10
                Assert.Equal(12, telemetry.ActorCreated);
                
                // bug due to https://github.com/akkadotnet/akka.net/issues/6295 - all routees and the router start each time
                Assert.Equal(110, telemetry.ActorRestarted);
                // assert no stops recorded
                Assert.Equal(0, telemetry.ActorStopped);
            }, RemainingOrDefault);
            
            // GracefulStop router actor and assert that 10 actors have been stopped
            await router.GracefulStop(RemainingOrDefault);
            await AwaitAssertAsync(async () =>
            {
                var telemetry = await subscriber.Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                // assert that actor start count is still 10
                Assert.Equal(12, telemetry.ActorCreated);
                Assert.Equal(110, telemetry.ActorRestarted);
                Assert.Equal(11, telemetry.ActorStopped);
            }, RemainingOrDefault);
        }
    }
}