//-----------------------------------------------------------------------
// <copyright file="ClusterShardingLeaseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingLeaseSpec : AkkaSpec
    {
        private sealed class MessageExtractor: IMessageExtractor
        {
            public string EntityId(object message)
                => message switch
                {
                    int i => i.ToString(),
                    _ => null
                };

            public object EntityMessage(object message)
                => message;

            public string ShardId(object message)
                => message switch
                {
                    int i => (i % 10).ToString(),
                    _ => null
                };

            public string ShardId(string entityId, object messageHint = null)
                => entityId;
        }

        public class LeaseFailed : Exception
        {
            public LeaseFailed(string message) : base(message)
            {
            }

            public LeaseFailed(string message, Exception innerEx)
                : base(message, innerEx)
            {
            }
        }

        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.loggers = [Akka.Event.DefaultLogger]
                akka.actor.provider = ""cluster""
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding {
                    use-lease = ""test-lease""
                    lease-retry-interval = 200ms
                    distributed-data.durable {
                        keys = []
                    }
                    verbose-debug-logging = on
                    fail-on-invalid-entity-state-transition = on
                }
                ")
                .WithFallback(ClusterSharding.DefaultConfig())
                .WithFallback(ClusterSingleton.DefaultConfig())
                .WithFallback(TestLease.Configuration);

        readonly TimeSpan shortDuration = TimeSpan.FromMilliseconds(200);
        readonly Cluster cluster;
        readonly string leaseOwner;
        readonly TestLeaseExt testLeaseExt;
        readonly bool rememberEntities;

        const string typeName = "echo";
        IActorRef region;

        public ClusterShardingLeaseSpec(ITestOutputHelper helper) : this(null, false, helper)
        {
        }

        protected ClusterShardingLeaseSpec(Config config, bool rememberEntities, ITestOutputHelper helper)
            : base(config?.WithFallback(SpecConfig) ?? SpecConfig, helper)
        {
            cluster = Cluster.Get(Sys);
            leaseOwner = cluster.SelfMember.Address.HostPort();
            testLeaseExt = TestLeaseExt.Get(Sys);
            this.rememberEntities = rememberEntities;
        }

        // Cluster formation used to run here in the constructor, behind a blocking AwaitAssert
        // wrapper (AwaitAssertAsync(...).WaitAndUnwrapException, i.e. Task.Wait). MemberUp needs
        // about six thread-pool dispatches, and akka.cluster.use-dispatcher is empty, so all of
        // them run on akka.actor.default-dispatcher, which is the .NET thread pool. A CI run
        // measured MemberUp at 3.462 s against the flat 3 s single-expect-default and missed by
        // 462 ms, because xUnit never raises MinThreads in this assembly (parallelization is off,
        // so SetupParallelism, the only caller of ThreadPool.SetMinThreads, never runs) and the
        // pool floor stays at ProcessorCount.
        //
        // JoinAsync waits on the real MemberUp signal instead of polling a volatile field, and
        // awaiting it here parks nothing: xUnit v3 awaits InitializeAsync inside its own async
        // pipeline, so the worker goes back to the pool while the cluster forms. StartAsync avoids
        // ClusterSharding.Start's Ask(...).Result on the same thread.
        public override async ValueTask InitializeAsync()
        {
            await base.InitializeAsync();

            using var cts = new CancellationTokenSource(Dilated(TimeSpan.FromSeconds(30)));
            await cluster.JoinAsync(cluster.SelfAddress, cts.Token);

            region = await ClusterSharding.Get(Sys).StartAsync(
                typeName: typeName,
                entityProps: SimpleEchoActor.Props(),
                settings: ClusterShardingSettings.Create(Sys).WithRememberEntities(rememberEntities),
                messageExtractor: new MessageExtractor());
        }

        private async Task<TestLease> LeaseForShardAsync(int shardId)
        {
            TestLease lease = null;
            await AwaitAssertAsync(() =>
            {
                lease = testLeaseExt.GetTestLease(LeaseNameFor(shardId));
            }, TimeSpan.FromSeconds(6));
            return lease;
        }

        private string LeaseNameFor(int shardId, string typeName = typeName) => $"{Sys.Name}-shard-{typeName}-{shardId}";

        [Fact]
        public async Task Cluster_sharding_with_lease_should_not_start_until_lease_is_acquired()
        {
            region.Tell(1, TestActor);
            await ExpectNoMsgAsync(shortDuration);
            var testLease = await LeaseForShardAsync(1);
            testLease.InitialPromise.SetResult(true);
            await ExpectMsgAsync(1);
        }

        [Fact]
        public async Task Cluster_sharding_with_lease_should_retry_if_initial_acquire_is_false()
        {
            region.Tell(2, TestActor);
            await ExpectNoMsgAsync(shortDuration);
            var testLease = await LeaseForShardAsync(2);
            testLease.InitialPromise.SetResult(false);
            await ExpectNoMsgAsync(shortDuration);
            testLease.SetNextAcquireResult(Task.FromResult(true));
            await ExpectMsgAsync(2);
        }

        [Fact]
        public async Task Cluster_sharding_with_lease_should_retry_if_initial_acquire_fails()
        {
            region.Tell(3, TestActor);
            await ExpectNoMsgAsync(shortDuration);
            var testLease = await LeaseForShardAsync(3);
            testLease.InitialPromise.SetException(new LeaseFailed("oh no"));
            await ExpectNoMsgAsync(shortDuration);
            testLease.SetNextAcquireResult(Task.FromResult(true));
            await ExpectMsgAsync(3);
        }

        [Fact]
        public async Task Cluster_sharding_with_lease_should_recover_if_lease_lost()
        {
            // Explicit sender: region.Tell(msg) reads the [ThreadStatic] implicit sender directly
            // and never runs EnsureImplicitSender, so it must not be the first thing a fact does
            // now that InitializeAsync moves us to a different thread.
            region.Tell(4, TestActor);
            await ExpectNoMsgAsync(shortDuration);
            var testLease = await LeaseForShardAsync(4);
            testLease.InitialPromise.SetResult(true);
            await ExpectMsgAsync(4);
            testLease.GetCurrentCallback()(new LeaseFailed("oh dear"));
            // Inner budget was the default 3 s inside a 10 s outer loop, which bought three
            // attempts. TestLease re-acquire returns an already-completed task, so 1 s is ample
            // and the outer loop now gets about ten attempts.
            await AwaitAssertAsync(async () =>
            {
                region.Tell(4, TestActor);
                await ExpectMsgAsync(4, TimeSpan.FromSeconds(1));
            }, TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task Cluster_sharding_with_lease_should_release_lease_when_shard_stopped()
        {
            region.Tell(5, TestActor);
            await ExpectNoMsgAsync(shortDuration);
            var testLease = await LeaseForShardAsync(5);
            testLease.InitialPromise.SetResult(true);
            await testLease.Probe.ExpectMsgAsync(new TestLease.AcquireReq(leaseOwner));
            await ExpectMsgAsync(5);

            region.Tell(new ShardCoordinator.HandOff("5"), TestActor);
            await testLease.Probe.ExpectMsgAsync(new TestLease.ReleaseReq(leaseOwner));
        }
    }

    public class PersistenceClusterShardingLeaseSpec : ClusterShardingLeaseSpec
    {
        public PersistenceClusterShardingLeaseSpec(ITestOutputHelper helper)
            : base(ConfigurationFactory.ParseString(@"
                akka.cluster.sharding {
                    state-store-mode = persistence
                    journal-plugin-id = ""akka.persistence.journal.inmem""
                }
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
