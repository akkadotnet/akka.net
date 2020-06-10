//-----------------------------------------------------------------------
// <copyright file="ShutdownAfterJoinSeedNodesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class ShutdownAfterJoinSeedNodesSpec : AkkaSpec
    {
        public static readonly Config Config = ConfigurationFactory.ParseString(@"
            akka.actor.provider = ""cluster""
            akka.coordinated-shutdown.terminate-actor-system = on
            akka.remote.dot-netty.tcp.port = 0
            akka.cluster {
                seed-node-timeout = 2s
                retry-unsuccessful-join-after = 2s
                shutdown-after-unsuccessful-join-seed-nodes = 5s
            }");

        private readonly ActorSystem _seed1;
        private readonly ActorSystem _seed2;
        private readonly ActorSystem _ordinary1;

        public ShutdownAfterJoinSeedNodesSpec() : base(Config)
        {
            _seed1 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            _seed2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            _ordinary1 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        }

        protected override void AfterTermination()
        {
            base.AfterTermination();
            Shutdown(_seed1);
            Shutdown(_seed2);
            Shutdown(_ordinary1);
        }

        [Fact]
        public void Joining_seed_nodes_must_be_aborted_after_shutdown_after_unsuccessful_join_seed_nodes()
        {
            var seedNodes = ImmutableList.Create(
                Cluster.Get(_seed1).SelfAddress, 
                Cluster.Get(_seed2).SelfAddress);

            Shutdown(_seed1); // crash so that others will not be able to join

            Cluster.Get(_seed2).JoinSeedNodes(seedNodes);
            Cluster.Get(_ordinary1).JoinSeedNodes(seedNodes);

            AwaitCondition(() => _seed2.WhenTerminated.IsCompleted, Cluster.Get(_seed2).Settings.ShutdownAfterUnsuccessfulJoinSeedNodes + TimeSpan.FromSeconds(10));
            AwaitCondition(() => _ordinary1.WhenTerminated.IsCompleted, Cluster.Get(_ordinary1).Settings.ShutdownAfterUnsuccessfulJoinSeedNodes + TimeSpan.FromSeconds(10));
        }
    }
}
