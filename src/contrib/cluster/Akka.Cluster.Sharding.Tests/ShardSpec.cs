//-----------------------------------------------------------------------
// <copyright file="ShardSpec.cs" company="Akka.NET Project">
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
using Akka.Util.Extensions;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ShardSpec : AkkaSpec
    {
        private static readonly Config SpecConfig;

        internal class EntityActor : ActorBase
        {
            private readonly ILoggingAdapter _log = Context.GetLogger();

            protected override bool Receive(object message)
            {
                _log.Info("Msg {0}", message);
                Sender.Tell($"ack ${message}");
                return true;
            }
        }



        static ShardSpec()
        {
            SpecConfig = ConfigurationFactory.ParseString(@"

                akka.loglevel = INFO
                akka.actor.provider = ""cluster""
                akka.remote.dot-netty.tcp.port = 0
                test-lease {
                    lease-class = ""Akka.Cluster.Tools.Tests.TestLease, Akka.Cluster.Tools.Tests""
                    heartbeat-interval = 1s
                    heartbeat-timeout = 120s
                    lease-operation-timeout = 3s
                }")
                .WithFallback(ClusterSingletonManager.DefaultConfig()
                .WithFallback(ClusterSharding.DefaultConfig()));
        }

        public sealed class EntityEnvelope
        {
            public readonly long Id;
            public readonly object Payload;
            public EntityEnvelope(long id, object payload)
            {
                Id = id;
                Payload = payload;
            }
        }

        public const int NumberOfShards = 5;

        public static readonly ExtractEntityId ExtractEntityId = message =>
        {
            switch (message)
            {
                case EntityEnvelope env:
                    return (env.Id.ToString(), env.Payload);
            }
            return Option<(string, object)>.None;
        };

        public static readonly ExtractShardId ExtractShardId = message =>
        {
            switch (message)
            {
                case EntityEnvelope msg:
                    return (msg.Id % NumberOfShards).ToString();
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
            public string ShardId { get; }
            public TestProbe Parent { get; }
            public TestLease Lease { get; private set; }
            public IActorRef Shard { get; }
            private readonly ClusterShardingSettings settings;
            private const string typeName = "type1";

            public Setup(ShardSpec spec)
            {
                ShardId = spec.NextShardId();
                Parent = spec.CreateTestProbe();
                settings = ClusterShardingSettings.Create(spec.Sys).WithLeaseSettings(new LeaseUsageSettings("test-lease", TimeSpan.FromSeconds(2)));

                Shard =
                Parent.ChildActorOf(Shards.Props(
                    typeName,
                    ShardId,
                    _ => Props.Create(() => new EntityActor()),
                    settings,
                    ExtractEntityId,
                    ExtractShardId,
                    PoisonPill.Instance,
                    spec.Sys.DeadLetters,
                    1));

                spec.AwaitAssert(() =>
                {
                    Lease = spec.testLeaseExt.GetTestLease(spec.LeaseNameForShard(typeName, ShardId));
                });
            }
        }

        private TimeSpan shortDuration = TimeSpan.FromMilliseconds(100);
        private TestLeaseExt testLeaseExt;
        private AtomicCounter shardIds = new AtomicCounter(0);

        public ShardSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
            testLeaseExt = TestLeaseExt.Get(Sys);
        }

        private string LeaseNameForShard(string typeName, string shardId) => $"{Sys.Name}-shard-{typeName}-{shardId}";

        private string NextShardId() => $"{shardIds.GetAndIncrement()}";


        [Fact]
        public void Cluster_Shard_should_not_initialize_the_shard_until_the_lease_is_acquired()
        {
            var setup = new Setup(this);
            setup.Parent.ExpectNoMsg(shortDuration);
            setup.Lease.InitialPromise.SetResult(true);
            setup.Parent.ExpectMsg(new ShardInitialized(setup.ShardId));
        }

        [Fact]
        public void Cluster_Shard_should_retry_if_lease_acquire_returns_false()
        {
            var setup = new Setup(this);
            setup.Lease.InitialPromise.SetResult(false);
            setup.Parent.ExpectNoMsg(shortDuration);
            setup.Lease.SetNextAcquireResult(Task.FromResult(true));
            setup.Parent.ExpectMsg(new ShardInitialized(setup.ShardId));
        }

        [Fact]
        public void Cluster_Shard_should_retry_if_the_lease_acquire_fails()
        {
            var setup = new Setup(this);
            setup.Lease.InitialPromise.SetException(new BadLease("no lease for you"));
            setup.Parent.ExpectNoMsg(shortDuration);
            setup.Lease.SetNextAcquireResult(Task.FromResult(true));
            setup.Parent.ExpectMsg(new ShardInitialized(setup.ShardId));
        }


        [Fact]
        public void Cluster_Shard_should_shutdown_if_lease_is_lost()
        {
            var setup = new Setup(this);
            var probe = CreateTestProbe();
            probe.Watch(setup.Shard);
            setup.Lease.InitialPromise.SetResult(true);
            setup.Parent.ExpectMsg(new ShardInitialized(setup.ShardId));
            setup.Lease.GetCurrentCallback()(new BadLease("bye bye lease"));
            probe.ExpectTerminated(setup.Shard);
        }
    }
}
