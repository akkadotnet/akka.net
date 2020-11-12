//-----------------------------------------------------------------------
// <copyright file="ShardWithLeaseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Cluster.Tools.Tests;
using Akka.Configuration;
using Akka.Coordination;
using Akka.Event;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

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

        public sealed class EntityEnvelope
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

        public static readonly ExtractEntityId ExtractEntityId = message =>
        {
            switch (message)
            {
                case EntityEnvelope env:
                    return (env.EntityId.ToString(), env.Payload);
            }
            return Option<(string, object)>.None;
        };

        public static readonly ExtractShardId ExtractShardId = message =>
        {
            switch (message)
            {
                case EntityEnvelope msg:
                    return (msg.EntityId % NumberOfShards).ToString();
            }
            return null;
        };

        public class BadLease : Exception
        {
            public BadLease(string message) : base(message)
            {
            }

            public BadLease(string message, Exception innerEx)
                : base(message, innerEx)
            {
            }

            protected BadLease(SerializationInfo info, StreamingContext context)
                : base(info, context)
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
                    .Start(TypeName, Props.Create(() => new EntityActor()), settings, ExtractEntityId, ExtractShardId);
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
            ConfigurationFactory.ParseString(@"

                akka.loglevel = DEBUG
                akka.actor.provider = ""cluster""
                akka.remote.dot-netty.tcp.port = 0
                test-lease {
                    lease-class = ""Akka.Cluster.Tools.Tests.TestLease, Akka.Cluster.Tools.Tests""
                    heartbeat-interval = 1s
                    heartbeat-timeout = 120s
                    lease-operation-timeout = 3s
                }
                akka.cluster.sharding.verbose-debug-logging = on
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on")
                .WithFallback(ClusterSingletonManager.DefaultConfig()
                .WithFallback(ClusterSharding.DefaultConfig()));

        private TimeSpan shortDuration = TimeSpan.FromMilliseconds(100);
        private TestLeaseExt testLeaseExt;
        private static AtomicCounter typeIdx = new AtomicCounter(0);

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
    }
}
