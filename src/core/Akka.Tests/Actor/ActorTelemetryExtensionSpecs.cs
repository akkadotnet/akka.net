//-----------------------------------------------------------------------
// <copyright file="ActorTelemetryExtensionSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Telemetry;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor
{
    /// <summary>
    /// Tests for the TelemetryExtension API and configuration
    /// </summary>
    public class ActorTelemetryExtensionSpecs : AkkaSpec
    {
        public static readonly Config TelemetryEnabled = @"akka.actor.telemetry.enabled = on";
        public static readonly Config TelemetryDisabled = @"akka.actor.telemetry.enabled = off";

        public ActorTelemetryExtensionSpecs(ITestOutputHelper output) : base(TelemetryEnabled, output)
        {
        }

        private class SimpleActor : ReceiveActor
        {
            public SimpleActor()
            {
                ReceiveAny(_ => Sender.Tell("ok"));
            }
        }

        private class AnotherTestActor : ReceiveActor
        {
            public AnotherTestActor()
            {
                ReceiveAny(_ => Sender.Tell("done"));
            }
        }

        [Fact]
        public async Task TelemetryExtension_should_be_accessible_when_enabled()
        {
            // Act
            var extension = TelemetryExtension.Get(Sys);

            // Assert
            extension.Should().NotBeNull();

            // Should be able to query alive actors
            var aliveActors = await extension.GetAliveActors();
            aliveActors.Should().NotBeNull();
        }

        [Fact]
        public async Task TelemetryExtension_GetAliveActors_should_track_actor_starts()
        {
            // Arrange
            var extension = TelemetryExtension.Get(Sys);

            // Act - create some actors
            var actor1 = Sys.ActorOf<SimpleActor>("actor1");
            var actor2 = Sys.ActorOf<SimpleActor>("actor2");
            var actor3 = Sys.ActorOf<SimpleActor>("actor3");

            // Wait for actors to start
            await actor1.Ask<string>("ping");
            await actor2.Ask<string>("ping");
            await actor3.Ask<string>("ping");

            // Assert
            await AwaitAssertAsync(async () =>
            {
                var aliveActors = await extension.GetAliveActors();
                aliveActors.Should().ContainKey("SimpleActor");
                aliveActors["SimpleActor"].count.Should().BeGreaterOrEqualTo(3);
                aliveActors["SimpleActor"].actorType.Should().Be(typeof(SimpleActor));
            });
        }

        [Fact]
        public async Task TelemetryExtension_GetAliveActors_should_track_actor_stops()
        {
            // Arrange
            var extension = TelemetryExtension.Get(Sys);

            // Act - create and stop actors
            var actor1 = Sys.ActorOf<SimpleActor>("stop-test-1");
            var actor2 = Sys.ActorOf<SimpleActor>("stop-test-2");
            var actor3 = Sys.ActorOf<SimpleActor>("stop-test-3");

            // Wait for actors to start
            await actor1.Ask<string>("ping");
            await actor2.Ask<string>("ping");
            await actor3.Ask<string>("ping");

            int initialCount = 0;
            await AwaitAssertAsync(async () =>
            {
                var aliveActors = await extension.GetAliveActors();
                aliveActors.Should().ContainKey("SimpleActor");
                initialCount = aliveActors["SimpleActor"].count;
                initialCount.Should().BeGreaterOrEqualTo(3);
            });

            // Stop 2 actors
            await actor1.GracefulStop(TimeSpan.FromSeconds(3));
            await actor2.GracefulStop(TimeSpan.FromSeconds(3));

            // Assert - count should decrease
            await AwaitAssertAsync(async () =>
            {
                var aliveActors = await extension.GetAliveActors();
                aliveActors.Should().ContainKey("SimpleActor");
                aliveActors["SimpleActor"].count.Should().Be(initialCount - 2);
            });
        }

        [Fact]
        public async Task TelemetryExtension_GetAliveActors_should_handle_zero_count()
        {
            // Arrange
            var extension = TelemetryExtension.Get(Sys);

            // Act - create and immediately stop an actor
            var actor = Sys.ActorOf<SimpleActor>("zero-count-test");
            await actor.Ask<string>("ping");

            await AwaitAssertAsync(async () =>
            {
                var aliveActors = await extension.GetAliveActors();
                aliveActors.Should().ContainKey("SimpleActor");
            });

            await actor.GracefulStop(TimeSpan.FromSeconds(3));

            // The entry should still exist but count may be 0 or removed
            await AwaitAssertAsync(async () =>
            {
                var aliveActors = await extension.GetAliveActors();
                if (aliveActors.ContainsKey("SimpleActor"))
                {
                    aliveActors["SimpleActor"].count.Should().BeGreaterOrEqualTo(0);
                }
            });
        }

        [Fact]
        public async Task TelemetryExtension_GetAliveActors_should_distinguish_different_actor_types()
        {
            // Arrange
            var extension = TelemetryExtension.Get(Sys);

            // Act - create actors of different types
            var simpleActor = Sys.ActorOf<SimpleActor>("type-test-1");
            var anotherActor = Sys.ActorOf<AnotherTestActor>("type-test-2");

            await simpleActor.Ask<string>("ping");
            await anotherActor.Ask<string>("ping");

            // Assert
            await AwaitAssertAsync(async () =>
            {
                var aliveActors = await extension.GetAliveActors();
                aliveActors.Should().ContainKey("SimpleActor");
                aliveActors.Should().ContainKey("AnotherTestActor");
                aliveActors["SimpleActor"].actorType.Should().Be(typeof(SimpleActor));
                aliveActors["AnotherTestActor"].actorType.Should().Be(typeof(AnotherTestActor));
            });
        }

        [Fact]
        public async Task TelemetryExtension_GetAliveActors_should_timeout_appropriately()
        {
            // Arrange
            var extension = TelemetryExtension.Get(Sys);

            // Act & Assert - should complete within timeout
            var task = extension.GetAliveActors();
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));

            completed.Should().Be(task, "GetAliveActors should complete within 2 seconds");
            var result = await task;
            result.Should().NotBeNull();
        }

        [Fact]
        public void TelemetryExtension_should_be_singleton_per_ActorSystem()
        {
            // Act
            var extension1 = TelemetryExtension.Get(Sys);
            var extension2 = TelemetryExtension.Get(Sys);

            // Assert
            extension1.Should().BeSameAs(extension2, "Extension should be singleton per ActorSystem");
        }

        [Fact]
        public async Task TelemetryExtension_should_handle_rapid_actor_creation_and_deletion()
        {
            // Arrange
            var extension = TelemetryExtension.Get(Sys);

            // Act - rapidly create and stop actors
            for (int i = 0; i < 50; i++)
            {
                var actor = Sys.ActorOf<SimpleActor>($"rapid-{i}");
                await actor.Ask<string>("ping");
                if (i % 2 == 0)
                {
                    await actor.GracefulStop(TimeSpan.FromSeconds(1));
                }
            }

            // Assert - should still be queryable and consistent
            await AwaitAssertAsync(async () =>
            {
                var aliveActors = await extension.GetAliveActors();
                aliveActors.Should().NotBeNull();
                if (aliveActors.ContainsKey("SimpleActor"))
                {
                    aliveActors["SimpleActor"].count.Should().BeGreaterOrEqualTo(0);
                }
            });
        }
    }

    /// <summary>
    /// Tests for telemetry when disabled
    /// </summary>
    public class ActorTelemetryDisabledSpecs : AkkaSpec
    {
        public static readonly Config TelemetryDisabled = @"akka.actor.telemetry.enabled = off";

        public ActorTelemetryDisabledSpecs(ITestOutputHelper output) : base(TelemetryDisabled, output)
        {
        }

        private class SimpleActor : ReceiveActor
        {
            public SimpleActor()
            {
                ReceiveAny(_ => Sender.Tell("ok"));
            }
        }

        [Fact]
        public void ActorSystem_should_start_when_telemetry_disabled()
        {
            // Assert
            Sys.Should().NotBeNull();
            Sys.Settings.EmitActorTelemetry.Should().BeFalse();
        }

        [Fact]
        public async Task Actors_should_work_normally_when_telemetry_disabled()
        {
            // Act
            var actor = Sys.ActorOf<SimpleActor>("test-actor");
            var response = await actor.Ask<string>("ping");

            // Assert
            response.Should().Be("ok");
        }

        [Fact]
        public void TelemetryExtension_should_not_be_auto_loaded_when_disabled()
        {
            // Act - Check if the extension exists
            var hasExtension = Sys.HasExtension<TelemetryExtension>();

            // Assert
            hasExtension.Should().BeFalse("Extension should not be auto-loaded when telemetry is disabled");
        }

        [Fact]
        public void TelemetryExtension_can_still_be_manually_loaded_when_disabled()
        {
            // Act - Manually load the extension
            var extension = TelemetryExtension.Get(Sys);

            // Assert
            extension.Should().NotBeNull("Extension can still be manually loaded even when disabled");
        }
    }

    /// <summary>
    /// Tests for IActorTelemetryEvent interface and event types
    /// </summary>
    public class ActorTelemetryEventSpecs : AkkaSpec
    {
        public static readonly Config TelemetryEnabled = @"akka.actor.telemetry.enabled = on";

        public ActorTelemetryEventSpecs(ITestOutputHelper output) : base(TelemetryEnabled, output)
        {
        }

        private class EventCollector : ReceiveActor
        {
            private readonly IActorRef _replyTo;
            public EventCollector(IActorRef replyTo)
            {
                _replyTo = replyTo;

                Receive<ActorStarted>(evt => _replyTo.Tell(evt));
                Receive<ActorStopped>(evt => _replyTo.Tell(evt));
                Receive<ActorRestarted>(evt => _replyTo.Tell(evt));
            }

            protected override void PreStart()
            {
                Context.System.EventStream.Subscribe(Self, typeof(IActorTelemetryEvent));
            }
        }

        private class RestartingActor : ReceiveActor
        {
            public RestartingActor()
            {
                Receive<string>(msg =>
                {
                    if (msg == "restart")
                        throw new ApplicationException("Intentional restart");
                    Sender.Tell("ok");
                });
            }
        }

        [Fact]
        public async Task ActorStarted_event_should_contain_correct_information()
        {
            // Arrange
            var collector = Sys.ActorOf(Props.Create(() => new EventCollector(TestActor)));

            // Clear any startup messages
            ReceiveWhile(TimeSpan.FromMilliseconds(100), _ => true);

            // Act
            var actor = Sys.ActorOf<RestartingActor>("start-event-test");

            // Assert
            var startEvent = await ExpectMsgAsync<ActorStarted>(TimeSpan.FromSeconds(3));
            startEvent.Should().NotBeNull();
            startEvent.Subject.Should().Be(actor);
            startEvent.ActorType.Should().Be(typeof(RestartingActor));
            startEvent.ActorTypeOverride.Should().BeEmpty();
        }

        [Fact]
        public async Task ActorStopped_event_should_be_published_when_actor_stops()
        {
            // Arrange
            var collector = Sys.ActorOf(Props.Create(() => new EventCollector(TestActor)));

            // Clear any startup messages
            ReceiveWhile(TimeSpan.FromMilliseconds(100), _ => true);

            // Act
            var actor = Sys.ActorOf<RestartingActor>("stop-event-test");
            await ExpectMsgAsync<ActorStarted>(TimeSpan.FromSeconds(3)); // Wait for start

            await actor.GracefulStop(TimeSpan.FromSeconds(3));

            // Assert
            var stopEvent = await ExpectMsgAsync<ActorStopped>(TimeSpan.FromSeconds(3));
            stopEvent.Should().NotBeNull();
            stopEvent.Subject.Should().Be(actor);
            stopEvent.ActorType.Should().Be(typeof(RestartingActor));
        }

        [Fact]
        public async Task ActorRestarted_event_should_be_published_when_actor_restarts()
        {
            // Arrange
            var collector = Sys.ActorOf(Props.Create(() => new EventCollector(TestActor)));

            // Clear any startup messages
            ReceiveWhile(TimeSpan.FromMilliseconds(100), _ => true);

            // Act
            var actor = Sys.ActorOf<RestartingActor>("restart-event-test");
            await ExpectMsgAsync<ActorStarted>(TimeSpan.FromSeconds(3)); // Wait for start

            actor.Tell("restart");

            // Assert
            var restartEvent = await ExpectMsgAsync<ActorRestarted>(TimeSpan.FromSeconds(3));
            restartEvent.Should().NotBeNull();
            restartEvent.Subject.Should().Be(actor);
            restartEvent.ActorType.Should().Be(typeof(RestartingActor));
            restartEvent.Reason.Should().BeOfType<ApplicationException>();
            restartEvent.Reason.Message.Should().Be("Intentional restart");
        }

        [Fact]
        public void IActorTelemetryEvent_should_implement_required_interfaces()
        {
            // Assert - compile-time checks
            typeof(IActorTelemetryEvent).Should().Implement<INoSerializationVerificationNeeded>();
            typeof(IActorTelemetryEvent).Should().Implement<INotInfluenceReceiveTimeout>();
        }

        [Fact]
        public void ActorStarted_should_implement_IActorTelemetryEvent()
        {
            // Arrange
            var actor = TestActor;
            var startEvent = new ActorStarted(actor, typeof(RestartingActor));

            // Assert
            startEvent.Should().BeAssignableTo<IActorTelemetryEvent>();
            startEvent.Subject.Should().Be(actor);
            startEvent.ActorType.Should().Be(typeof(RestartingActor));
        }

        [Fact]
        public void ActorStopped_should_implement_IActorTelemetryEvent()
        {
            // Arrange
            var actor = TestActor;
            var stopEvent = new ActorStopped(actor, typeof(RestartingActor));

            // Assert
            stopEvent.Should().BeAssignableTo<IActorTelemetryEvent>();
            stopEvent.Subject.Should().Be(actor);
            stopEvent.ActorType.Should().Be(typeof(RestartingActor));
        }

        [Fact]
        public void ActorRestarted_should_implement_IActorTelemetryEvent()
        {
            // Arrange
            var actor = TestActor;
            var exception = new Exception("test");
            var restartEvent = new ActorRestarted(actor, typeof(RestartingActor), exception);

            // Assert
            restartEvent.Should().BeAssignableTo<IActorTelemetryEvent>();
            restartEvent.Subject.Should().Be(actor);
            restartEvent.ActorType.Should().Be(typeof(RestartingActor));
            restartEvent.Reason.Should().Be(exception);
        }

        [Fact]
        public void ActorTelemetryEvents_should_support_ActorTypeOverride()
        {
            // Arrange
            var actor = TestActor;

            // Act
            var startEvent = new ActorStarted(actor, typeof(RestartingActor), "CustomTypeName");
            var stopEvent = new ActorStopped(actor, typeof(RestartingActor), "CustomTypeName");
            var restartEvent = new ActorRestarted(actor, typeof(RestartingActor), new Exception("test"), "CustomTypeName");

            // Assert
            startEvent.ActorTypeOverride.Should().Be("CustomTypeName");
            stopEvent.ActorTypeOverride.Should().Be("CustomTypeName");
            restartEvent.ActorTypeOverride.Should().Be("CustomTypeName");
        }
    }

    /// <summary>
    /// Tests for configuration and initialization
    /// </summary>
    public class ActorTelemetryConfigurationSpecs
    {
        [Fact]
        public void Default_configuration_should_enable_telemetry()
        {
            // Arrange & Act
            using var sys = ActorSystem.Create("test-default-config");

            // Assert
            sys.Settings.EmitActorTelemetry.Should().BeTrue("Telemetry should be enabled by default");
        }

        [Fact]
        public void Configuration_can_disable_telemetry()
        {
            // Arrange
            var config = ConfigurationFactory.ParseString(@"akka.actor.telemetry.enabled = off");

            // Act
            using var sys = ActorSystem.Create("test-disabled-config", config);

            // Assert
            sys.Settings.EmitActorTelemetry.Should().BeFalse();
        }

        [Fact]
        public void Configuration_can_enable_telemetry_explicitly()
        {
            // Arrange
            var config = ConfigurationFactory.ParseString(@"akka.actor.telemetry.enabled = on");

            // Act
            using var sys = ActorSystem.Create("test-enabled-config", config);

            // Assert
            sys.Settings.EmitActorTelemetry.Should().BeTrue();
        }

        [Fact]
        public void ActorSystem_should_initialize_successfully_with_telemetry_enabled()
        {
            // Arrange
            var config = ConfigurationFactory.ParseString(@"akka.actor.telemetry.enabled = on");

            // Act
            using var sys = ActorSystem.Create("test-init", config);

            // Assert
            sys.Should().NotBeNull();
            sys.Settings.EmitActorTelemetry.Should().BeTrue();

            // Extension should be loaded
            sys.HasExtension<TelemetryExtension>().Should().BeTrue();
        }

        [Fact]
        public void ActorSystem_should_initialize_successfully_with_telemetry_disabled()
        {
            // Arrange
            var config = ConfigurationFactory.ParseString(@"akka.actor.telemetry.enabled = off");

            // Act
            using var sys = ActorSystem.Create("test-init-disabled", config);

            // Assert
            sys.Should().NotBeNull();
            sys.Settings.EmitActorTelemetry.Should().BeFalse();

            // Extension should NOT be auto-loaded
            sys.HasExtension<TelemetryExtension>().Should().BeFalse();
        }
    }
}
