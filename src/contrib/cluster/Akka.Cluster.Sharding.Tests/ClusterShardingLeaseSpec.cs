//-----------------------------------------------------------------------
// <copyright file="ClusterShardingLeaseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Cluster.Tools.Tests;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Cluster.Sharding.ShardRegion;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingLeaseSpec : AkkaSpec
    {

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                #akka.loggers = [""akka.testkit.SilenceAllTestEventListener""]
                akka.loggers = [Akka.Event.DefaultLogger]
                akka.actor.provider = ""cluster""
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding {
                    use-lease = ""test-lease""
                    lease-retry-interval = 200ms
                    distributed-data.durable {
                        keys = []
                    }
                }
                ")
                .WithFallback(ClusterSharding.DefaultConfig())
                .WithFallback(ClusterSingletonManager.DefaultConfig())
                .WithFallback(TestLease.Configuration);
        }

        public static readonly ExtractEntityId ExtractEntityId = message =>
        {
            if (message is int i)
                return (i.ToString(), i);
            return Option<(string, object)>.None;
        };

        public static readonly ExtractShardId ExtractShardId = message =>
        {
            switch (message)
            {
                case int i:
                    return (i % 10).ToString();
                case StartEntity se:
                    return (int.Parse(se.EntityId) % 10).ToString();
            }
            return null;
        };

        public class LeaseFailed : Exception
        {
            public LeaseFailed(string message) : base(message)
            {
            }

            public LeaseFailed(string message, Exception innerEx)
                : base(message, innerEx)
            {
            }

            protected LeaseFailed(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {
            }
        }

        bool rememberEntities;
        TimeSpan shortDuration = TimeSpan.FromMilliseconds(200);
        Cluster cluster;
        string leaseOwner;
        TestLeaseExt testLeaseExt;

        const string typeName = "echo";
        IActorRef region;

        public ClusterShardingLeaseSpec(ITestOutputHelper helper) : this(null, false, helper)
        {
        }

        protected ClusterShardingLeaseSpec(Config config, bool rememberEntities, ITestOutputHelper helper)
            : base(config?.WithFallback(GetConfig()) ?? GetConfig(), helper)
        {
            this.rememberEntities = rememberEntities;

            cluster = Cluster.Get(Sys);
            leaseOwner = cluster.SelfMember.Address.HostPort();
            testLeaseExt = TestLeaseExt.Get(Sys);

            cluster.Join(cluster.SelfAddress);
            AwaitAssert(() =>
            {
                cluster.SelfMember.Status.ShouldBe(MemberStatus.Up);
            });
            ClusterSharding.Get(Sys).Start(
              typeName: typeName,
              entityProps: EchoActor.Props(this),
              settings: ClusterShardingSettings.Create(Sys).WithRememberEntities(rememberEntities),
              extractEntityId: ExtractEntityId,
              extractShardId: ExtractShardId);

            region = ClusterSharding.Get(Sys).ShardRegion(typeName);
        }


        private TestLease LeaseForShard(int shardId)
        {
            TestLease lease = null;
            AwaitAssert(() =>
            {
                lease = testLeaseExt.GetTestLease(LeaseNameFor(shardId));
            }, TimeSpan.FromSeconds(6));
            return lease;
        }

        private string LeaseNameFor(int shardId, string typeName = typeName) => $"{Sys.Name}-shard-{typeName}-{shardId}";

        [Fact]
        public void Cluster_sharding_with_lease_should_not_start_until_lease_is_acquired()
        {
            region.Tell(1);
            ExpectNoMsg(shortDuration);
            var testLease = LeaseForShard(1);
            testLease.InitialPromise.SetResult(true);
            ExpectMsg(1);
        }

        [Fact]
        public void Cluster_sharding_with_lease_should_retry_if_initial_acquire_is_false()
        {
            region.Tell(2);
            ExpectNoMsg(shortDuration);
            var testLease = LeaseForShard(2);
            testLease.InitialPromise.SetResult(false);
            ExpectNoMsg(shortDuration);
            testLease.SetNextAcquireResult(Task.FromResult(true));
            ExpectMsg(2);
        }

        [Fact]
        public void Cluster_sharding_with_lease_should_retry_if_initial_acquire_fails()
        {
            region.Tell(3);
            ExpectNoMsg(shortDuration);
            var testLease = LeaseForShard(3);
            testLease.InitialPromise.SetException(new LeaseFailed("oh no"));
            ExpectNoMsg(shortDuration);
            testLease.SetNextAcquireResult(Task.FromResult(true));
            ExpectMsg(3);
        }

        [Fact]
        public void Cluster_sharding_with_lease_should_recover_if_lease_lost()
        {
            region.Tell(4);
            ExpectNoMsg(shortDuration);
            var testLease = LeaseForShard(4);
            testLease.InitialPromise.SetResult(true);
            ExpectMsg(4);
            testLease.GetCurrentCallback()(new LeaseFailed("oh dear"));
            AwaitAssert(() =>
            {
                region.Tell(4);
                ExpectMsg(4, TimeSpan.FromSeconds(1));
            }, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void Cluster_sharding_with_lease_should_release_lease_when_shard_stopped()
        {
            region.Tell(5);
            ExpectNoMsg(shortDuration);
            var testLease = LeaseForShard(5);
            testLease.InitialPromise.SetResult(true);
            testLease.Probe.ExpectMsg(new TestLease.AcquireReq(leaseOwner));
            ExpectMsg(5);

            region.Tell(new PersistentShardCoordinator.HandOff("5"));
            testLease.Probe.ExpectMsg(new TestLease.ReleaseReq(leaseOwner));
        }
    }

    public class PersistenceClusterShardingLeaseSpec : ClusterShardingLeaseSpec
    {
        public PersistenceClusterShardingLeaseSpec(ITestOutputHelper helper)
            : base(ConfigurationFactory.ParseString(@"
                akka.cluster.sharding {
                    state-store-mode = persistence
                }
                akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
                "), true, helper)
        {
        }
    }

    public class DDataClusterShardingLeaseSpec : ClusterShardingLeaseSpec
    {
        public DDataClusterShardingLeaseSpec(ITestOutputHelper helper)
            : base(ConfigurationFactory.ParseString(@"
                akka.cluster.sharding {
                    state-store-mode = ddata
                }
                "), true, helper)
        {
        }
    }
}