//-----------------------------------------------------------------------
// <copyright file="EntityTerminationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    /// <summary>
    /// Verifies that the automatic restart on terminate/crash that is in place for remember entities does not apply
    /// when remember entities is not enabled
    /// </summary>
    public class EntityTerminationSpec : AkkaSpec
    {
        private class EntityEnvelope
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

        private sealed class MessageExtractor: IMessageExtractor
        {
            public string EntityId(object message)
                => message switch
                {
                    EntityEnvelope e => e.Id,
                    _ => null
                };

            public object EntityMessage(object message)
                => message switch
                {
                    EntityEnvelope e => e.Msg,
                    _ => message
                };

            public string ShardId(object message)
                => message switch
                {
                    EntityEnvelope => "1",
                    ShardRegion.StartEntity => "1",
                    _ => null
                };

            public string ShardId(string entityId, object messageHint = null)
                => "1";
        }

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
                .WithFallback(ClusterSingleton.DefaultConfig())
                .WithFallback(ClusterSharding.DefaultConfig());

        public EntityTerminationSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
        }

        private async Task JoinSelfAsync()
        {
            // Form a one node cluster
            var cluster = Cluster.Get(Sys);
            cluster.Join(cluster.SelfAddress);
            await AwaitAssertAsync(() =>
            {
                cluster.ReadView.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(1);
            }, TimeSpan.FromSeconds(10));
        }

        /// <summary>
        /// Asks the region for its state through a fresh probe. A fresh probe per request means a reply that
        /// arrives after the caller stopped waiting goes to an actor nobody reads, instead of sitting in the
        /// test actor's queue where a later expect would take it for a current snapshot. The region's own
        /// query timeout is 3 s, which is longer than the per-attempt bound the polling helper uses.
        /// </summary>
        private async Task<CurrentShardRegionState> QueryRegionStateAsync(IActorRef sharding, TimeSpan timeout)
        {
            var probe = CreateTestProbe();
            probe.Send(sharding, GetShardRegionState.Instance);
            var state = await probe.ExpectMsgAsync<CurrentShardRegionState>(timeout);
            state.Failed.Should().BeEmpty("every shard must answer the state query");
            state.Shards.Should().HaveCount(1);
            return state;
        }

        /// <summary>
        /// Polls the region until the shard's active entity set is exactly <paramref name="expected"/>.
        /// An entity's Terminated reaches the shard as a user message, queued behind whatever else is in the
        /// shard's mailbox, so the test actor seeing Terminated says nothing about the shard having processed
        /// it. The active set is the observable the assertions in this spec read, so it is the anchor.
        /// </summary>
        private async Task AwaitActiveEntitiesAsync(IActorRef sharding, params string[] expected)
        {
            await AwaitAssertAsync(async () =>
            {
                var state = await QueryRegionStateAsync(sharding, TimeSpan.FromSeconds(1));
                state.Shards.First().EntityIds.Should().BeEquivalentTo(expected);
            }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100));
        }

        /// <summary>
        /// Polls the region until the shard's active entity set is empty. See <see cref="AwaitActiveEntitiesAsync"/>.
        /// </summary>
        private async Task AwaitNoActiveEntitiesAsync(IActorRef sharding)
        {
            await AwaitAssertAsync(async () =>
            {
                var state = await QueryRegionStateAsync(sharding, TimeSpan.FromSeconds(1));
                state.Shards.First().EntityIds.Should().BeEmpty();
            }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task Sharding_when_an_entity_terminates_must_allow_stop_without_passivation_if_not_remembering_entities()
        {
            await JoinSelfAsync();
            var sharding = ClusterSharding.Get(Sys).Start(
                "regular",
                StoppingActor.Props(),
                ClusterShardingSettings.Create(Sys),
                new MessageExtractor());

            sharding.Tell(new EntityEnvelope("1", "ping"));
            await ExpectMsgAsync("pong-1");
            var entity = LastSender;

            sharding.Tell(new EntityEnvelope("2", "ping"));
            await ExpectMsgAsync("pong-1");

            await WatchAsync(entity);
            sharding.Tell(new EntityEnvelope("1", "stop"));
            await ExpectTerminatedAsync(entity, TimeSpan.FromSeconds(3));

            // With remember-entities off there is no restart path, so there is no backoff to wait out:
            // once the shard has processed the entity's Terminated, "2" is the whole active set.
            await AwaitActiveEntitiesAsync(sharding, "2");

            // make sure the shard didn't crash (coverage for regression bug #29383)
            sharding.Tell(new EntityEnvelope("2", "ping"));
            await ExpectMsgAsync("pong-2"); // if it lost state we know it restarted
        }

        [Fact]
        public async Task Sharding_when_an_entity_terminates_must_automatically_restart_a_terminating_entity_not_passivating_if_remembering_entities()
        {
            await JoinSelfAsync();
            var sharding = ClusterSharding.Get(Sys).Start(
                "remembering",
                StoppingActor.Props(),
                ClusterShardingSettings.Create(Sys).WithRememberEntities(true),
                new MessageExtractor());

            sharding.Tell(new EntityEnvelope("1", "ping"));
            await ExpectMsgAsync("pong-1");
            var entity = LastSender;
            await WatchAsync(entity);

            // The restart is the shard's own event: when entity-restart-backoff (250 ms) expires it starts the
            // entity again and logs the same "Started entity" line it logged the first time. Waiting on that
            // line proves the restart happened, and that nothing here caused it, without racing the shard's
            // bookkeeping: until the shard processes the Terminated, the active set still holds the dead ref,
            // so a state poll on its own could pass on the old incarnation.
            await EventFilter.Debug(contains: "Started entity").ExpectOneAsync(TimeSpan.FromSeconds(5), async () =>
            {
                sharding.Tell(new EntityEnvelope("1", "stop"));
                await ExpectTerminatedAsync(entity, TimeSpan.FromSeconds(3));
            });

            await AwaitActiveEntitiesAsync(sharding, "1");
        }

        [Fact]
        public async Task Sharding_when_an_entity_terminates_must_allow_terminating_entity_to_passivate_if_remembering_entities()
        {
            await JoinSelfAsync();
            var sharding = ClusterSharding.Get(Sys).Start(
                "remembering",
                StoppingActor.Props(),
                ClusterShardingSettings.Create(Sys).WithRememberEntities(true),
                new MessageExtractor());

            sharding.Tell(new EntityEnvelope("1", "ping"));
            await ExpectMsgAsync("pong-1");
            var entity = LastSender;
            await WatchAsync(entity);

            sharding.Tell(new EntityEnvelope("1", "passivate"));
            await ExpectTerminatedAsync(entity, TimeSpan.FromSeconds(3));

            // Anchor on the shard's own bookkeeping first. The entity leaves the active set when the shard
            // processes its Terminated, which is also the moment a shard that mistook the passivation for a
            // crash would arm the entity-restart-backoff timer (250 ms). The remember-entities write for the
            // stop may still be in flight at this point; it does not matter for what follows.
            await AwaitNoActiveEntitiesAsync(sharding);

            // Now nothing may restart. "Started entity" is the shard's own restart line, so a zero-count filter
            // held open for longer than the backoff observes a wrongful restart directly. The filter waits the
            // whole window without blocking a thread-pool worker: this test body runs on one, and on a two-core
            // agent the pool has only two.
            await EventFilter.Debug(contains: "Started entity")
                .ExpectAsync(0, TimeSpan.FromMilliseconds(600), () => Task.CompletedTask);

            // Deliberately a single read, not a poll: polling for "empty" would accept the first empty reading
            // and stop proving that nothing restarted in the meantime.
            var regionState = await QueryRegionStateAsync(sharding, TimeSpan.FromSeconds(5));
            regionState.Shards.First().EntityIds.Should().BeEmpty();
        }
    }
}
