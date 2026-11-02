//-----------------------------------------------------------------------
// <copyright file="ShardWithLeaseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Coordination;
using Akka.Event;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class ShardWithLeaseSpec : AkkaSpec
    {
        internal class EntityActor : ActorBase
        {
            private readonly ILoggingAdapter _log = Context.GetLogger();

            protected override bool Receive(object message)
            {
                _log.Info("Msg {0}", message);
                Sender.Tell($"ack {message}");
                return true;
            }
        }

        private sealed class EntityEnvelope
        {
            public readonly long EntityId;
            public readonly object Payload;
            public EntityEnvelope(int entityId, object payload)
            {
                EntityId = entityId;
                Payload = payload;
            }
        }

        public const int NumberOfShards = 5;

        private class MessageExtractor: IMessageExtractor
        {
            public string EntityId(object message)
                => message switch
                {
                    EntityEnvelope env => env.EntityId.ToString(),
                    _ => null
                };

            public object EntityMessage(object message)
                => message switch
                {
                    EntityEnvelope env => env.Payload,
                    _ => message
                };

            public string ShardId(object message)
                => message switch
                {
                    EntityEnvelope msg => (msg.EntityId % NumberOfShards).ToString(),
                    _ => null
                };

            public string ShardId(string entityId, object messageHint = null)
                => (int.Parse(entityId) % NumberOfShards).ToString();
        }

        public class BadLease : Exception
        {
            public BadLease(string message) : base(message)
            {
            }

            public BadLease(string message, Exception innerEx)
                : base(message, innerEx)
            {
            }
        }

        private class Setup
        {
            private readonly ClusterShardingSettings settings;
            public string TypeName { get; }
            private readonly ShardWithLeaseSpec spec;
            public IActorRef Sharding { get; }

            public Setup(ShardWithLeaseSpec spec)
            {
                settings = ClusterShardingSettings.Create(spec.Sys).WithLeaseSettings(new LeaseUsageSettings("test-lease", TimeSpan.FromSeconds(2)));

                // unique type name for each test
                TypeName = $"type{spec.NextTypeIdx()}";

                Sharding = ClusterSharding.Get(spec.Sys)
                    .Start(TypeName, Props.Create(() => new EntityActor()), settings, new MessageExtractor());
                this.spec = spec;
            }

            public TestLease LeaseFor(string shardId)
            {
                TestLease lease = null;
                spec.AwaitAssert(() =>
                {
                    var leaseName = $"{spec.Sys.Name}-shard-{TypeName}-{shardId}";
                    lease = spec.testLeaseExt.GetTestLease(leaseName);
                });
                return lease;
            }
        }

        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(
                    $$"""
                    akka.loglevel = DEBUG
                    akka.actor.provider = "cluster"
                    akka.remote.dot-netty.tcp.port = 0
                    test-lease {
                        lease-class = "{{typeof(TestLease).FullName}}, {{typeof(TestLease).Assembly.GetName().Name}}"
                        heartbeat-interval = 1s
                        heartbeat-timeout = 120s
                        lease-operation-timeout = 3s
                    }
                    akka.cluster.sharding.verbose-debug-logging = on
                    akka.cluster.sharding.fail-on-invalid-entity-state-transition = on
                    """)
                .WithFallback(ClusterSingleton.DefaultConfig())
                .WithFallback(ClusterSharding.DefaultConfig());

        private TimeSpan shortDuration = TimeSpan.FromMilliseconds(100);
        private TestLeaseExt testLeaseExt;
        private static AtomicCounter typeIdx = new(0);

        public ShardWithLeaseSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
            testLeaseExt = TestLeaseExt.Get(Sys);
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

        private string NextTypeIdx() => $"{typeIdx.IncrementAndGet()}";

        [Fact]
        public void Lease_handling_in_sharding_must_not_initialize_the_shard_until_the_lease_is_acquired()
        {
            var setup = new Setup(this);
            var probe = CreateTestProbe();
            setup.Sharding.Tell(new EntityEnvelope(1, "hello"), probe.Ref);
            probe.ExpectNoMsg(shortDuration);
            setup.LeaseFor("1").InitialPromise.SetResult(true);
            probe.ExpectMsg("ack hello");
        }

        [Fact]
        public void Lease_handling_in_sharding_must_retry_if_lease_acquire_returns_false()
        {
            var setup = new Setup(this);
            var probe = CreateTestProbe();
            TestLease lease = null;
            EventFilter.Error(start: $"{setup.TypeName}: Failed to get lease for shard id [1]").ExpectOne(() =>
            {
                setup.Sharding.Tell(new EntityEnvelope(1, "hello"), probe.Ref);
                lease = setup.LeaseFor("1");
                lease.InitialPromise.SetResult(false);
                probe.ExpectNoMsg(shortDuration);
            });

            lease.SetNextAcquireResult(Task.FromResult(true));
            probe.ExpectMsg("ack hello");
        }

        [Fact]
        public void Lease_handling_in_sharding_must_retry_if_the_lease_acquire_fails()
        {
            var setup = new Setup(this);
            var probe = CreateTestProbe();
            TestLease lease = null;
            EventFilter.Error(start: $"{setup.TypeName}: Failed to get lease for shard id [1]").ExpectOne(() =>
            {
                setup.Sharding.Tell(new EntityEnvelope(1, "hello"), probe.Ref);
                lease = setup.LeaseFor("1");
                lease.InitialPromise.SetException(new BadLease("no lease for you"));
                probe.ExpectNoMsg(shortDuration);
            });

            lease.SetNextAcquireResult(Task.FromResult(true));
            probe.ExpectMsg("ack hello");
        }


        [Fact]
        public void Lease_handling_in_sharding_must_shutdown_if_lease_is_lost()
        {
            var setup = new Setup(this);
            var probe = CreateTestProbe();
            setup.Sharding.Tell(new EntityEnvelope(1, "hello"), probe.Ref);
            var lease = setup.LeaseFor("1");
            lease.InitialPromise.SetResult(true);
            probe.ExpectMsg("ack hello");

            EventFilter.Error(
                start: $"{setup.TypeName}: Shard id [1] lease lost, stopping shard and killing [1] entities. Reason for losing lease: {typeof(BadLease).FullName}: bye bye lease").ExpectOne(() =>
            {
                lease.GetCurrentCallback()(new BadLease("bye bye lease"));
                setup.Sharding.Tell(new EntityEnvelope(1, "hello"), probe.Ref);
                probe.ExpectNoMsg(shortDuration);
            });
        }

        /// <summary>
        /// Reproduces https://github.com/akkadotnet/akka.net/issues/8147
        /// When a shard is in AwaitingLease state, HandOff messages should be handled
        /// immediately (not stashed) since the shard has no active entities.
        /// </summary>
        [Fact]
        public async Task Lease_handling_in_sharding_should_respond_to_HandOff_while_awaiting_lease()
        {
            var setup = new Setup(this);
            var probe = CreateTestProbe();

            // Send a message to trigger shard creation - shard enters AwaitingLease
            setup.Sharding.Tell(new EntityEnvelope(1, "hello"), probe.Ref);

            // Wait for the shard to start and enter AwaitingLease (don't resolve the lease)
            setup.LeaseFor("1");

            // Verify shard is blocked on lease
            await probe.ExpectNoMsgAsync(shortDuration);

            // Send HandOff to the ShardRegion - it should forward to the shard via Forward
            var handOffProbe = CreateTestProbe();
            setup.Sharding.Tell(new ShardCoordinator.HandOff("1"), handOffProbe.Ref);

            // The shard should respond with ShardStopped immediately since it has no entities.
            // BUG: Currently fails because AwaitingLease stashes HandOff indefinitely.
            await handOffProbe.ExpectMsgAsync<ShardCoordinator.ShardStopped>(TimeSpan.FromSeconds(3));
        }

        /// <summary>
        /// Reproduces https://github.com/akkadotnet/akka.net/issues/8146
        /// When a ShardStopped is received from a region that is NOT the current owner
        /// of the shard, the coordinator should ignore it (stale backup from completed rebalance).
        /// </summary>
        [Fact]
        public async Task Coordinator_should_ignore_stale_ShardStopped_from_non_owner_region()
        {
            var setup = new Setup(this);
            var probe = CreateTestProbe();

            // Create shard and acquire lease
            setup.Sharding.Tell(new EntityEnvelope(1, "hello"), probe.Ref);
            var lease = setup.LeaseFor("1");
            lease.InitialPromise.SetResult(true);
            await probe.ExpectMsgAsync("ack hello");

            // Resolve the coordinator actor
            var encName = Uri.EscapeDataString(setup.TypeName);
            var coordinatorPath = $"/system/sharding/{encName}Coordinator/singleton/coordinator";
            IActorRef coordinator = null;
            await AwaitAssertAsync(async () =>
            {
                coordinator = await Sys.ActorSelection(coordinatorPath).ResolveOne(TimeSpan.FromSeconds(1));
            });

            // Send ShardStopped from a TestProbe (simulating a stale backup from a
            // dead/old region that no longer owns the shard)
            var staleRegionProbe = CreateTestProbe();

            // The coordinator should ignore ShardStopped from a non-owner sender.
            // Use Identify as a FIFO mailbox barrier to prove the coordinator has
            // processed the ShardStopped before we check for the log message.
            await EventFilter.Info(contains: "late deallocation").ExpectAsync(0, async () =>
            {
                coordinator.Tell(new ShardCoordinator.ShardStopped("1"), staleRegionProbe.Ref);
                coordinator.Tell(new Identify("flush"), probe.Ref);
                await probe.ExpectMsgAsync<ActorIdentity>(TimeSpan.FromSeconds(3));
            });

            // Shard should still be functioning without any disruption
            setup.Sharding.Tell(new EntityEnvelope(1, "hello2"), probe.Ref);
            await probe.ExpectMsgAsync("ack hello2", TimeSpan.FromSeconds(3));
        }
    }
}
