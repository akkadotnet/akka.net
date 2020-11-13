//-----------------------------------------------------------------------
// <copyright file="EntityTerminationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests
{
    /// <summary>
    /// Verifies that the automatic restart on terminate/crash that is in place for remember entities does not apply
    /// when remember entities is not enabled
    /// </summary>
    public class EntityTerminationSpec : AkkaSpec
    {
        internal class EntityEnvelope
        {
            public EntityEnvelope(string id, object msg)
            {
                Id = id;
                Msg = msg;
            }

            public string Id { get; }
            public object Msg { get; }
        }

        private class StoppingActor : ActorBase
        {
            public static Props Props() => Actor.Props.Create(() => new StoppingActor());

            private int counter = 0;

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case "stop":
                        Context.Stop(Self);
                        return true;
                    case "ping":
                        counter += 1;
                        Sender.Tell($"pong-{counter}");
                        return true;
                    case "passivate":
                        Context.Parent.Tell(new Passivate("stop"));
                        return true;
                }
                return false;
            }
        }


        private ExtractEntityId extractEntityId = message =>
        {
            if (message is EntityEnvelope e)
                return (e.Id, e.Msg);
            return Option<(string, object)>.None;
        };

        private ExtractShardId extractShardId = message =>
        {
            switch (message)
            {
                case EntityEnvelope _:
                    return "1"; // single shard for all entities
                case ShardRegion.StartEntity se:
                    return "1";
            }
            return null;
        };

        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
                akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
                akka.remote.dot-netty.tcp.port = 0

                akka.cluster.sharding.state-store-mode = ddata
                # no leaks between test runs thank you
                akka.cluster.sharding.distributed-data.durable.keys = []
                akka.cluster.sharding.verbose-debug-logging = on
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on
                akka.cluster.sharding.entity-restart-backoff = 250ms")
                .WithFallback(ClusterSingletonManager.DefaultConfig())
                .WithFallback(ClusterSharding.DefaultConfig());

        public EntityTerminationSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
        }

        protected override void AtStartup()
        {
            // Form a one node cluster
            var cluster = Cluster.Get(Sys);
            cluster.Join(cluster.SelfAddress);
            AwaitAssert(() =>
            {
                cluster.ReadView.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(1);
            });
        }

        [Fact]
        public void Sharding_when_an_entity_terminates_must_allow_stop_without_passivation_if_not_remembering_entities()
        {
            var sharding = ClusterSharding.Get(Sys).Start(
                "regular",
                StoppingActor.Props(),
                ClusterShardingSettings.Create(Sys),
                extractEntityId,
                extractShardId);

            sharding.Tell(new EntityEnvelope("1", "ping"));
            ExpectMsg("pong-1");
            var entity = LastSender;

            sharding.Tell(new EntityEnvelope("2", "ping"));
            ExpectMsg("pong-1");

            Watch(entity);
            sharding.Tell(new EntityEnvelope("1", "stop"));
            ExpectTerminated(entity);

            Thread.Sleep(400); // restart backoff is 250 ms
            sharding.Tell(GetShardRegionState.Instance);
            var regionState = ExpectMsg<CurrentShardRegionState>();
            regionState.Shards.Should().HaveCount(1);
            regionState.Shards.First().EntityIds.Should().BeEquivalentTo("2");

            // make sure the shard didn't crash (coverage for regression bug #29383)
            sharding.Tell(new EntityEnvelope("2", "ping"));
            ExpectMsg("pong-2"); // if it lost state we know it restarted
        }

        [Fact]
        public void Sharding_when_an_entity_terminates_must_automatically_restart_a_terminating_entity_not_passivating_if_remembering_entities()
        {
            var sharding = ClusterSharding.Get(Sys).Start(
                "remembering",
                StoppingActor.Props(),
                ClusterShardingSettings.Create(Sys).WithRememberEntities(true),
                extractEntityId,
                extractShardId);

            sharding.Tell(new EntityEnvelope("1", "ping"));
            ExpectMsg("pong-1");
            var entity = LastSender;
            Watch(entity);

            sharding.Tell(new EntityEnvelope("1", "stop"));
            ExpectTerminated(entity);

            Thread.Sleep(400); // restart backoff is 250 ms
            AwaitAssert(() =>
            {
                sharding.Tell(GetShardRegionState.Instance);
                var regionState = ExpectMsg<CurrentShardRegionState>();
                regionState.Shards.Should().HaveCount(1);
                regionState.Shards.First().EntityIds.Should().HaveCount(1);
            }, TimeSpan.FromSeconds(2));
        }

        [Fact]
        public void Sharding_when_an_entity_terminates_must_allow_terminating_entity_to_passivate_if_remembering_entities()
        {
            var sharding = ClusterSharding.Get(Sys).Start(
                "remembering",
                StoppingActor.Props(),
                ClusterShardingSettings.Create(Sys).WithRememberEntities(true),
                extractEntityId,
                extractShardId);

            sharding.Tell(new EntityEnvelope("1", "ping"));
            ExpectMsg("pong-1");
            var entity = LastSender;
            Watch(entity);

            sharding.Tell(new EntityEnvelope("1", "passivate"));
            ExpectTerminated(entity);
            Thread.Sleep(400); // restart backoff is 250 ms

            sharding.Tell(GetShardRegionState.Instance);
            var regionState = ExpectMsg<CurrentShardRegionState>();
            regionState.Shards.Should().HaveCount(1);
            regionState.Shards.First().EntityIds.Should().BeEmpty();
        }
    }
}
