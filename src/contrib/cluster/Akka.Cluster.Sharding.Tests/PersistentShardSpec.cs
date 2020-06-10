//-----------------------------------------------------------------------
// <copyright file="PersistentShardSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Extensions;
using FluentAssertions;
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
    }
}
