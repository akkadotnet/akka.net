//-----------------------------------------------------------------------
// <copyright file="BootstrapSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// Designed to test the <see cref="BootstrapSetup"/> and validate that we can set the `akka.actor.provider` with it.
    /// </summary>
    public class BootstrapSetupSpecs
    {
        // No akka.actor.provider specified
       const string Config = @"    
            akka.cluster {
              auto-down-unreachable-after = 0s
              periodic-tasks-initial-delay = 120 s
              publish-stats-interval = 0 s # always, when it happens
              run-coordinated-shutdown-when-down = off
            }
            akka.coordinated-shutdown.terminate-actor-system = off
            akka.coordinated-shutdown.run-by-actor-system-terminate = off
            akka.remote.log-remote-lifecycle-events = off
            akka.remote.dot-netty.tcp.port = 0";

       // akka.remote specified - need to override
       const string OverrideConfig = @"    
            akka.cluster {
              auto-down-unreachable-after = 0s
              periodic-tasks-initial-delay = 120 s
              publish-stats-interval = 0 s # always, when it happens
              run-coordinated-shutdown-when-down = off
            }
            akka.actor.provider = remote
            akka.coordinated-shutdown.terminate-actor-system = off
            akka.coordinated-shutdown.run-by-actor-system-terminate = off
            akka.remote.log-remote-lifecycle-events = off
            akka.remote.dot-netty.tcp.port = 0";

        [Fact]
        public async Task ShouldSetupClusterActorRefProviderViaBootstrapSetup()
        {
            var bootstrapSetup = BootstrapSetup.Create()
                .WithActorRefProvider(ProviderSelection.Cluster.Instance)
                .WithConfig(Config);

            using (var actorSystem = ActorSystem.Create("Test1", bootstrapSetup))
            {
                actorSystem.Settings.ProviderClass.Should().Be("Akka.Cluster.ClusterActorRefProvider, Akka.Cluster");
                actorSystem.Settings.CoordinatedShutdownRunByActorSystemTerminate.Should().BeFalse();
                actorSystem.Settings.CoordinatedShutdownTerminateActorSystem.Should().BeFalse();
                await actorSystem.Terminate();
            }
        }

        [Fact]
        public async Task ShouldOverrideHoconClusterActorRefProviderViaBootstrapSetup()
        {
            var bootstrapSetup = BootstrapSetup.Create()
                .WithActorRefProvider(ProviderSelection.Cluster.Instance)
                .WithConfig(OverrideConfig);

            using (var actorSystem = ActorSystem.Create("Test2", bootstrapSetup))
            {
                actorSystem.Settings.ProviderClass.Should().Be("Akka.Cluster.ClusterActorRefProvider, Akka.Cluster");
                actorSystem.Settings.CoordinatedShutdownRunByActorSystemTerminate.Should().BeFalse();
                actorSystem.Settings.CoordinatedShutdownTerminateActorSystem.Should().BeFalse();
                await actorSystem.Terminate();
            }
        }
    }
}
