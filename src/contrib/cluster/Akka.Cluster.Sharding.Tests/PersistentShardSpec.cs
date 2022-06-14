//-----------------------------------------------------------------------
// <copyright file="PersistentShardSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests
{
    public class PersistentShardSpec : AkkaSpec
    {
        private static readonly Config SpecConfig;

        internal class EntityActor : UntypedActor
        {
            private readonly string id;

            public EntityActor(string id)
            {
                this.id = id;
            }

            protected override void OnReceive(object message)
            {
            }
        }
        
        
        internal class ConstructorFailActor : ActorBase
        {
            private static bool _thrown;
            private readonly ILoggingAdapter _log = Context.GetLogger();

            public ConstructorFailActor()
            {
                if (!_thrown)
                {
                    _thrown = true;
                    throw new Exception("EXPLODING CONSTRUCTOR!");
                }
            }
            
            protected override bool Receive(object message)
            {
                _log.Info("Msg {0}", message);
                Sender.Tell($"ack {message}");
                return true;
            }
        }

        internal class PreStartFailActor : ActorBase
        {
            private static bool _thrown;
            private readonly ILoggingAdapter _log = Context.GetLogger();

            protected override void PreStart()
            {
                base.PreStart();
                if (!_thrown)
                {
                    _thrown = true;
                    throw new Exception("EXPLODING PRE-START!");
                }
            }

            protected override bool Receive(object message)
            {
                _log.Info("Msg {0}", message);
                Sender.Tell($"ack {message}");
                return true;
            }
        }

        static PersistentShardSpec()
        {
            SpecConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
                akka.remote.dot-netty.tcp.port = 0")
                .WithFallback(ClusterSingletonManager.DefaultConfig()
                .WithFallback(ClusterSharding.DefaultConfig()));
        }

        public PersistentShardSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
        }

        [Fact]
        public void Persistent_Shard_must_remember_entities_started_with_StartEntity()
        {
            Func<string, Props> ep = id => Props.Create(() => new EntityActor(id));

            ExtractEntityId extractEntityId = _ => ("entity-1", "msg");

            var props = Props.Create(() => new PersistentShard(
              "cats",
              "shard-1",
              ep,
              ClusterShardingSettings.Create(Sys),
              extractEntityId,
              _ => "shard-1",
              PoisonPill.Instance
            ));

            var persistentShard = Sys.ActorOf(props);
            Watch(persistentShard);

            persistentShard.Tell(new ShardRegion.StartEntity("entity-1"));
            ExpectMsg(new ShardRegion.StartEntityAck("entity-1", "shard-1"));

            persistentShard.Tell(PoisonPill.Instance);
            ExpectTerminated(persistentShard);

            Sys.Log.Info("Starting shard again");
            var secondIncarnation = Sys.ActorOf(props);

            secondIncarnation.Tell(Shard.GetShardStats.Instance);
            AwaitAssert(() =>
            {
                ExpectMsgAllOf(new Shard.ShardStats("shard-1", 1));
            });
        }

        [Theory(DisplayName = "Persistent shard must recover from transient failures inside sharding entity constructor and PreStart method")]
        [MemberData(nameof(PropsFactory))]
        public async Task Persistent_Shard_must_recover_from_failing_entity(Props entityProp)
        {
            ExtractEntityId extractEntityId = message =>
            {
                switch (message)
                {
                    case ShardSpec.EntityEnvelope env:
                        return (env.Id.ToString(), env.Payload);
                }
                return Option<(string, object)>.None;
            };
            
            ExtractShardId extractShardId = message =>
            {
                switch (message)
                {
                    case ShardSpec.EntityEnvelope msg:
                        return msg.Id.ToString();
                }
                return null;
            };            
            
            var settings = ClusterShardingSettings.Create(Sys);
            var tuning = settings.TuningParameters;
            settings = settings.WithTuningParameters(new TuningParameters
            (
                coordinatorFailureBackoff: tuning.CoordinatorFailureBackoff,
                retryInterval: tuning.RetryInterval,
                bufferSize: tuning.BufferSize,
                handOffTimeout: tuning.HandOffTimeout,
                shardStartTimeout: tuning.ShardStartTimeout,
                shardFailureBackoff: tuning.ShardFailureBackoff,
                entityRestartBackoff: 1.Seconds(),
                rebalanceInterval: tuning.RebalanceInterval,
                snapshotAfter: tuning.SnapshotAfter,
                keepNrOfBatches: tuning.KeepNrOfBatches,
                leastShardAllocationRebalanceThreshold: tuning.LeastShardAllocationRebalanceThreshold,
                leastShardAllocationMaxSimultaneousRebalance: tuning.LeastShardAllocationMaxSimultaneousRebalance,
                waitingForStateTimeout: tuning.WaitingForStateTimeout,
                updatingStateTimeout: tuning.UpdatingStateTimeout,
                entityRecoveryStrategy: tuning.EntityRecoveryStrategy,
                entityRecoveryConstantRateStrategyFrequency: tuning.EntityRecoveryConstantRateStrategyFrequency,
                entityRecoveryConstantRateStrategyNumberOfEntities: tuning.EntityRecoveryConstantRateStrategyNumberOfEntities,
                leastShardAllocationAbsoluteLimit: tuning.LeastShardAllocationAbsoluteLimit,
                leastShardAllocationRelativeLimit: tuning.LeastShardAllocationRelativeLimit
            ));
            
            var props = Props.Create(() => new PersistentShard(
                "cats",
                "shard-1",
                _ => entityProp,
                settings,
                extractEntityId,
                extractShardId,
                PoisonPill.Instance
            ));

            Sys.EventStream.Subscribe<Error>(TestActor);
            
            var persistentShard = Sys.ActorOf(props);

            persistentShard.Tell(new ShardRegion.StartEntity("1"));
            ExpectMsg(new ShardRegion.StartEntityAck("1", "shard-1"));
            
            // entity died here
            var err = ExpectMsg<Error>();
            err.Cause.Should().BeOfType<ActorInitializationException>();

            await Task.Delay(100);
            persistentShard.Tell(Shard.GetCurrentShardState.Instance);
            var state = ExpectMsg<Shard.CurrentShardState>();
            state.EntityIds.Count.Should().Be(0);

            // entity should be restarted when it received this message
            persistentShard.Tell(new ShardSpec.EntityEnvelope(1, "Restarted"));
            ExpectMsg("ack Restarted");
            
            persistentShard.Tell(Shard.GetCurrentShardState.Instance);
            state = ExpectMsg<Shard.CurrentShardState>();
            state.EntityIds.Count.Should().Be(1);
            state.EntityIds.First().Should().Be("1");
        }

        public static IEnumerable<object[]> PropsFactory()
        {
            yield return new object[] { Props.Create(() => new PreStartFailActor()) };
            yield return new object[] { Props.Create(() => new ConstructorFailActor()) };
        }
    }
}
