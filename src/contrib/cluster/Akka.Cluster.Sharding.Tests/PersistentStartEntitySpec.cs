//-----------------------------------------------------------------------
// <copyright file="PersistentStartEntitySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
    public class PersistentStartEntitySpec : AkkaSpec
    {
        private static readonly Config SpecConfig;

        internal class EntityActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case "give-me-shard":
                        Sender.Tell(Context.Parent);
                        return true;
                    case var msg:
                        Sender.Tell(msg);
                        return true;
                }
            }
        }

        internal class EntityEnvelope
        {
            public EntityEnvelope(int entityId, object msg)
            {
                EntityId = entityId;
                Msg = msg;
            }

            public int EntityId { get; }
            public object Msg { get; }
        }

        private ExtractEntityId extractEntityId = message =>
        {
            if (message is EntityEnvelope e)
                return (e.EntityId.ToString(), e.Msg);
            return Option<(string, object)>.None;
        };

        private ExtractShardId extractShardId = message =>
        {
            switch (message)
            {
                case EntityEnvelope e:
                    return (e.EntityId % 10).ToString();
                case ShardRegion.StartEntity se:
                    return (int.Parse(se.EntityId) % 10).ToString();
            }
            return null;
        };

        static PersistentStartEntitySpec()
        {
            SpecConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
                akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding.verbose-debug-logging = on
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on")
                .WithFallback(ClusterSingletonManager.DefaultConfig()
                .WithFallback(ClusterSharding.DefaultConfig()));
        }

        public PersistentStartEntitySpec(ITestOutputHelper helper) : base(SpecConfig, helper)
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
        public void Persistent_Shard_must_remember_entities_started_with_StartEntity()
        {
            var sharding = ClusterSharding.Get(Sys).Start(
              "startEntity",
              Props.Create<EntityActor>(),
              ClusterShardingSettings.Create(Sys)
                .WithRememberEntities(true)
                .WithStateStoreMode(StateStoreMode.Persistence),
              extractEntityId,
              extractShardId);

            sharding.Tell(new ShardRegion.StartEntity("1"));
            ExpectMsg(new ShardRegion.StartEntityAck("1", "1"));
            var shard = LastSender;

            Watch(shard);
            shard.Tell(PoisonPill.Instance);
            ExpectTerminated(shard);

            // trigger shard start by messaging other actor in it
            Thread.Sleep(200);
            Sys.Log.Info("Starting shard again");
            sharding.Tell(new EntityEnvelope(11, "give-me-shard"));
            var secondShardIncarnation = ExpectMsg<IActorRef>();

            AwaitAssert(() =>
            {
                secondShardIncarnation.Tell(Shard.GetShardStats.Instance);
                // the remembered 1 and 11 which we just triggered start of
                ExpectMsg(new Shard.ShardStats("1", 2));
            });
        }
    }
}
