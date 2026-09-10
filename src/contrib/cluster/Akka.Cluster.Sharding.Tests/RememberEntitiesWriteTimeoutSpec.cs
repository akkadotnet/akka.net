//-----------------------------------------------------------------------
// <copyright file="RememberEntitiesWriteTimeoutSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    /// <summary>
    /// Regression coverage for the remember-entities write timeout. <see cref="Shard"/> must arm the
    /// timer guarding a remember-entities store write with <c>updating-state-timeout</c> - the setting
    /// reference.conf documents for this purpose, and the value <see cref="DDataRememberEntitiesShardStore"/>
    /// sizes its own write-retry budget against - not the shorter <c>waiting-for-state-timeout</c>, which
    /// governs shard state reads. A write that finishes after the shorter setting but within the longer one
    /// must not restart the shard, since a restart silently drops the buffered message that triggered the
    /// write (sharding is at-most-once - there is no redelivery).
    /// </summary>
    public class RememberEntitiesWriteTimeoutSpec : AkkaSpec
    {
        private sealed class MessageExtractor : IMessageExtractor
        {
            public string EntityId(object message) => message is int i ? i.ToString() : null;
            public object EntityMessage(object message) => message;
            public string ShardId(object message) => message is int i ? (i % 10).ToString() : null;
            public string ShardId(string entityId, object messageHint = null) => (int.Parse(entityId) % 10).ToString();
        }

        private class EntityActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                Sender.Tell(message);
                return true;
            }
        }

        // Delay applied to every remember-entities write's acknowledgement, set before sharding starts -
        // same pattern RememberEntitiesFailureSpec uses for its static failure switches.
        private static TimeSpan _writeDelay = TimeSpan.Zero;
        private static readonly AtomicCounter StoreStarts = new(0);

        /// <summary>
        /// Custom remember-entities provider (wired via `remember-entities-custom-store`) whose shard
        /// store answers reads immediately but delays every write's acknowledgement by <see cref="_writeDelay"/>.
        /// </summary>
        internal sealed class SlowWriteStore : IRememberEntitiesProvider
        {
            public SlowWriteStore(ClusterShardingSettings settings, string typeName) { }
            public Props ShardStoreProps(string shardId) => Props.Create(() => new SlowWriteShardStoreActor());
            public Props CoordinatorStoreProps() => Props.Create(() => new ImmediateCoordinatorStoreActor());
        }

        private sealed class SlowWriteShardStoreActor : ActorBase, IWithTimers
        {
            private sealed class DelayedAck
            {
                public DelayedAck(IActorRef replyTo, RememberEntitiesShardStore.Update update)
                {
                    ReplyTo = replyTo;
                    Update = update;
                }

                public IActorRef ReplyTo { get; }
                public RememberEntitiesShardStore.Update Update { get; }
            }

            public ITimerScheduler Timers { get; set; }

            public SlowWriteShardStoreActor() => StoreStarts.IncrementAndGet();

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case RememberEntitiesShardStore.GetEntities _:
                        Sender.Tell(new RememberEntitiesShardStore.RememberedEntities(ImmutableHashSet<string>.Empty));
                        return true;
                    case RememberEntitiesShardStore.Update u:
                        Timers.StartSingleTimer("delayed-ack", new DelayedAck(Sender, u), _writeDelay);
                        return true;
                    case DelayedAck d:
                        d.ReplyTo.Tell(new RememberEntitiesShardStore.UpdateDone(d.Update.Started, d.Update.Stopped));
                        return true;
                }
                return false;
            }
        }

        // Coordinator side of the custom store - always responds immediately. The failure/delay under
        // test is the shard's entity-remembering write, not shard allocation.
        private sealed class ImmediateCoordinatorStoreActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case RememberEntitiesCoordinatorStore.GetShards _:
                        Sender.Tell(new RememberEntitiesCoordinatorStore.RememberedShards(ImmutableHashSet<string>.Empty));
                        return true;
                    case RememberEntitiesCoordinatorStore.AddShard m:
                        Sender.Tell(new RememberEntitiesCoordinatorStore.UpdateDone(m.ShardId));
                        return true;
                }
                return false;
            }
        }

        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding.distributed-data.durable.keys = []
                # must be ddata or the remember entities store is ignored
                akka.cluster.sharding.state-store-mode = ddata
                akka.cluster.sharding.remember-entities = on
                akka.cluster.sharding.remember-entities-store = custom
                akka.cluster.sharding.remember-entities-custom-store = ""Akka.Cluster.Sharding.Tests.RememberEntitiesWriteTimeoutSpec+SlowWriteStore, Akka.Cluster.Sharding.Tests""
                akka.cluster.sharding.verbose-debug-logging = on
                # waiting-for-state-timeout (2s) and updating-state-timeout (5s) are left at their
                # reference.conf defaults on purpose - this test exercises exactly those two values.")
                .WithFallback(ClusterSingleton.DefaultConfig()
                .WithFallback(ClusterSharding.DefaultConfig()));

        public RememberEntitiesWriteTimeoutSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
        }

        [Fact(DisplayName =
            "Shard must not restart a remember-entities write that completes after waiting-for-state-timeout " +
            "(2s) but within updating-state-timeout (5s), and must deliver the buffered message that triggered it")]
        public async Task Shard_must_not_restart_for_a_remember_entities_write_slower_than_waiting_for_state_timeout_but_within_updating_state_timeout()
        {
            // 3s sits strictly between waiting-for-state-timeout (2s - the pre-fix, buggy deadline for
            // this write) and updating-state-timeout (5s - the deadline reference.conf documents and
            // DDataRememberEntitiesShardStore sizes its write retries against). 1s/2s margins on either
            // side so this isn't a coin-flip on a loaded CI agent.
            _writeDelay = TimeSpan.FromSeconds(3);
            StoreStarts.Reset();

            var cluster = Cluster.Get(Sys);
            await cluster.JoinAsync(cluster.SelfAddress);
            await AwaitAssertAsync(() => cluster.ReadView.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(1));

            var probe = CreateTestProbe();
            var sharding = ClusterSharding.Get(Sys).Start(
                "slowWrite",
                Props.Create(() => new EntityActor()),
                ClusterShardingSettings.Create(Sys).WithRememberEntities(true),
                new MessageExtractor());

            sharding.Tell(1, probe.Ref);

            // If Shard.SendToRememberStore regresses to arming the timeout with
            // waiting-for-state-timeout, the shard restarts ~2s in, the buffered message is destroyed
            // with it (no redelivery - sharding is at-most-once) and this never arrives.
            await probe.ExpectMsgAsync(1, TimeSpan.FromSeconds(6));

            // The remember-entities store child is (re-)created in Shard's constructor, so a shard
            // restart would show up as a second store start.
            StoreStarts.Current.Should().Be(1, "the shard must not have restarted");

            Sys.Stop(sharding);
        }
    }
}
