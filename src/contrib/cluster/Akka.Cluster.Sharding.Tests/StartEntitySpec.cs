//-----------------------------------------------------------------------
// <copyright file="StartEntitySpec.cs" company="Akka.NET Project">
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
    /// Covers some corner cases around sending triggering an entity with StartEntity
    /// </summary>
    public class StartEntitySpec : AkkaSpec
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

        internal class EntityActor : ActorBase
        {
            public static Props Props() => Actor.Props.Create(() => new EntityActor());

            private IActorRef waitingForPassivateAck;

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case "ping":
                        Sender.Tell("pong");
                        return true;
                    case "passivate":
                        Context.Parent.Tell(new Passivate("complete-passivation"));
                        waitingForPassivateAck = Sender;
                        return true;
                    case "simulate-slow-passivate":
                        Context.Parent.Tell(new Passivate("slow-passivate-stop"));
                        waitingForPassivateAck = Sender;
                        return true;
                    case "slow-passivate-stop":
                        // actually, we just don't stop, keeping the passivation state forever for this test
                        waitingForPassivateAck?.Tell("slow-passivate-ack");
                        waitingForPassivateAck = null;
                        return true;
                    case "complete-passivation":
                    case "just-stop":
                        Context.Stop(Self);
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
                case EntityEnvelope e:
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
                akka.remote.dot-netty.tcp.port = 0

                akka.cluster.sharding.state-store-mode = ddata
                akka.cluster.sharding.remember-entities = on
                # no leaks between test runs thank you
                akka.cluster.sharding.distributed-data.durable.keys = []
                akka.cluster.sharding.verbose-debug-logging = on
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on")
                .WithFallback(ClusterSingletonManager.DefaultConfig()
                .WithFallback(ClusterSharding.DefaultConfig()));

        public StartEntitySpec(ITestOutputHelper helper) : base(SpecConfig, helper)
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
        public void StartEntity_while_entity_is_passivating_should_start_it_again_when_the_entity_terminates()
        {
            var sharding = ClusterSharding.Get(Sys).Start(
                "start-entity-1",
                EntityActor.Props(),
                ClusterShardingSettings.Create(Sys),
                extractEntityId,
                extractShardId);

            sharding.Tell(new EntityEnvelope("1", "ping"));
            ExpectMsg("pong");
            var entity = LastSender;

            sharding.Tell(new EntityEnvelope("1", "simulate-slow-passivate"));
            ExpectMsg("slow-passivate-ack");

            // entity is now in passivating state in shard
            // bypass region and send start entity directly to shard
            Sys.ActorSelection(entity.Path.Parent).Tell(new ShardRegion.StartEntity("1"));
            // bypass sharding and tell entity to complete passivation
            entity.Tell("complete-passivation");

            // should trigger start of entity again, and an ack
            ExpectMsg(new ShardRegion.StartEntityAck("1", "1"));
            AwaitAssert(() =>
            {
                sharding.Tell(GetShardRegionState.Instance);
                var state = ExpectMsg<CurrentShardRegionState>();
                state.Shards.Should().HaveCount(1);
                state.Shards.First().EntityIds.Should().BeEquivalentTo("1");
            });
        }

        [Fact]
        public void StartEntity_while_the_entity_is_waiting_for_restart_should_restart_it_immediately()
        {
            // entity crashed and before restart-backoff hit we sent it a StartEntity

            var sharding = ClusterSharding.Get(Sys).Start(
                "start-entity-2",
                EntityActor.Props(),
                ClusterShardingSettings.Create(Sys),
                extractEntityId,
                extractShardId);
            sharding.Tell(new EntityEnvelope("1", "ping"));
            ExpectMsg("pong");
            var entity = LastSender;
            Watch(entity);

            // stop without passivation
            entity.Tell("just-stop");
            ExpectTerminated(entity);

            // the backoff is 10s by default, so plenty time to
            // bypass region and send start entity directly to shard
            Thread.Sleep(200);
            Sys.ActorSelection(entity.Path.Parent).Tell(new ShardRegion.StartEntity("1"));
            ExpectMsg(new ShardRegion.StartEntityAck("1", "1"));
            AwaitAssert(() =>
            {
                sharding.Tell(GetShardRegionState.Instance);
                var state = ExpectMsg<CurrentShardRegionState>();
                state.Shards.Should().HaveCount(1);
                state.Shards.First().EntityIds.Should().BeEquivalentTo("1");
            });
        }

        [Fact]
        public void StartEntity_while_the_entity_is_queued_remember_stop_should_start_it_again_when_that_is_done()
        {
            // this is hard to do deterministically
            var sharding = ClusterSharding.Get(Sys).Start(
                "start-entity-3",
                EntityActor.Props(),
                ClusterShardingSettings.Create(Sys),
                extractEntityId,
                extractShardId);
            sharding.Tell(new EntityEnvelope("1", "ping"));
            ExpectMsg("pong");
            var entity = LastSender;
            Watch(entity);

            // resolve before passivation to save some time
            var shard = Sys.ActorSelection(entity.Path.Parent).ResolveOne(TimeSpan.FromSeconds(3)).Result;

            // stop passivation
            entity.Tell("passivate");
            // store of stop happens after passivation when entity has terminated
            ExpectTerminated(entity);
            shard.Tell(new ShardRegion.StartEntity("1")); // if we are lucky this happens while remember stop is in progress

            // regardless we should get an ack and the entity should be alive
            ExpectMsg(new ShardRegion.StartEntityAck("1", "1"));
            AwaitAssert(() =>
            {
                sharding.Tell(GetShardRegionState.Instance);
                var state = ExpectMsg<CurrentShardRegionState>();
                state.Shards.Should().HaveCount(1);
                state.Shards.First().EntityIds.Should().BeEquivalentTo("1");
            });
        }
    }
}
