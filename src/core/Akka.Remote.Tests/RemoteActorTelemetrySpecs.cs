//-----------------------------------------------------------------------
// <copyright file="RemoteActorTelemetrySpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests
{
    public class RemoteActorTelemetrySpecs : AkkaSpec
    {
        // create HOCON configuraiton that enables telemetry and Akka.Remote
        private static readonly string Config = @"
            akka {
                actor {
                    provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                    telemetry.enabled = true
                }
                remote {
                    log-remote-lifecycle-events = on
                    dot-netty.tcp {
                        port = 0
                        hostname = localhost
                    }
                }
            }";

        public RemoteActorTelemetrySpecs(ITestOutputHelper outputHelper) : base(Config, outputHelper)
        {

        }

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

                private GetTelemetryRequest()
                {
                }
            }

            public TelemetrySubscriber()
            {
                // Receive each type of IActorTelemetryEvent
                Receive<ActorStarted>(e => { _actorCreated++; });
                Receive<ActorStopped>(e => { _actorStopped++; });
                Receive<ActorRestarted>(e => { _actorRestarted++; });
                // receive a request for current counter values and return a GetTelemetry result
                Receive<GetTelemetryRequest>(e =>
                    Sender.Tell(new GetTelemetry(_actorCreated, _actorStopped, _actorRestarted)));
            }

            protected override void PreStart()
            {
                Context.System.EventStream.Subscribe(Self, typeof(IActorTelemetryEvent));
            }
        }

        // create a unit test where a second ActorSystem connects to Sys and receives an IActorRef from Sys and subscribes to Telemetry events
        [Fact]
        public async Task RemoteActorRefs_should_not_produce_telemetry()
        {
            // create a second ActorSystem that connects to Sys
            var system2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            try
            {
                // create a subscriber to receive telemetry events
                var subscriber = system2.ActorOf(Props.Create<TelemetrySubscriber>());

                // send a request for the current telemetry counters
                var telemetry = await subscriber
                    .Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);

                // verify that the counters are all correct
                Assert.Equal(0, telemetry.ActorCreated);
                Assert.Equal(0, telemetry.ActorStopped);
                Assert.Equal(0, telemetry.ActorRestarted);

                // create an actor in Sys
                var actor1 = Sys.ActorOf(BlackHoleActor.Props, "actor1");

                // resolve the currently bound Akka.Remote address for Sys
                var address = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;

                // create a RootActorPath for actor1 that uses the previous address value
                var actor1Path = new RootActorPath(address) / "user" / "actor1";

                // have system2 send a request to actor1 via Akka.Remote
                var actor2 = await system2.ActorSelection(actor1Path).ResolveOne(RemainingOrDefault);

                // send a request for the current telemetry counters
                telemetry = await subscriber
                    .Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                
                // verify that created actors is greater than 1
                var previouslyCreated = telemetry.ActorCreated;
                Assert.True(previouslyCreated > 1); // should have had some /system actors started as well
                Assert.Equal(0, telemetry.ActorStopped);
                Assert.Equal(0, telemetry.ActorRestarted);

                // stop the actor in Sys
                Sys.Stop(actor1);

                // send a request for the current telemetry counters
                telemetry = await subscriber
                    .Ask<TelemetrySubscriber.GetTelemetry>(TelemetrySubscriber.GetTelemetryRequest.Instance);
                // verify that the counters are all zero
                Assert.Equal(previouslyCreated, telemetry.ActorCreated); // should not have changed
                Assert.Equal(0, telemetry.ActorStopped);
                Assert.Equal(0, telemetry.ActorRestarted);
            }
            finally
            {
                Shutdown(system2);
            }
        }
    }
}