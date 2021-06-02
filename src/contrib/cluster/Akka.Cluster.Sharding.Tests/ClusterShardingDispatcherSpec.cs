using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.TestKit;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingDispatcherSpec: AkkaSpec
    {
        private static readonly Config SpecConfig;
        private class EchoActor : ReceiveActor
        {
            public EchoActor()
            {
                ReceiveAny(_ => Sender.Tell(_));
            }
        }

        private readonly ExtractEntityId _extractEntityId = message => (message.ToString(), message);

        private readonly ExtractShardId _extractShard = message => (MurmurHash.StringHash(message.ToString())).ToString();

        static ClusterShardingDispatcherSpec()
        {
            SpecConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0")
                .WithFallback(ClusterSingletonManager.DefaultConfig()
                    .WithFallback(ClusterSharding.DefaultConfig()));
        }

        public ClusterShardingDispatcherSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
        }

        [Fact]
        public void ClusterSharding_guardian_should_use_default_dispatcher()
        {
            var props = Props.Create(() => new EchoActor());
            var cluster = ClusterSharding.Get(Sys);
            var region1 = cluster
                .Start("type1", props, ClusterShardingSettings.Create(Sys),
                _extractEntityId, _extractShard);

            var info = cluster.GetType().GetField("_guardian", BindingFlags.Instance | BindingFlags.NonPublic);
            var guardian = ((Lazy<IActorRef>)info.GetValue(cluster)).Value;
            var dispatcher = ((ActorCell)((ActorRefWithCell)guardian).Underlying).Dispatcher;

            var defaultDispatcher = Sys.Dispatchers.Lookup(Dispatchers.DefaultDispatcherId);
            dispatcher.Should().BeEquivalentTo(defaultDispatcher);
        }
    }
}
