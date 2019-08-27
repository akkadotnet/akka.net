//-----------------------------------------------------------------------
// <copyright file="ProxyShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ProxyShardingSpec : AkkaSpec
    {
        private sealed class MessageExtractor : HashCodeMessageExtractor
        {
            public MessageExtractor(int maxNumberOfShards) : base(maxNumberOfShards)
            {
            }

            public override string EntityId(object message) => "dummyId";
        }

        public const string SpecConfig = @"
          akka.actor.provider = ""cluster""
          akka.remote.dot-netty.tcp.port = 0";

        private const string Role = "Shard";
        private readonly ClusterSharding _clusterSharding;
        private readonly ClusterShardingSettings _shardingSettings;
        private readonly IActorRef _shardProxy;

        private readonly IMessageExtractor _messageExtractor = new MessageExtractor(10);
        private readonly ExtractEntityId _idExtractor = msg => Tuple.Create(msg.ToString(), msg);
        private readonly ExtractShardId _shardResolver = message => message.ToString();

        public ProxyShardingSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
            _clusterSharding = ClusterSharding.Get(Sys);
            _shardingSettings = ClusterShardingSettings.Create(Sys);
            _shardProxy = _clusterSharding.StartProxy("myType", Role, _idExtractor, _shardResolver);
        }

        [Fact]
        public async Task Proxy_should_be_found()
        {
            var proxyRef = await Sys.ActorSelection($"akka://{Sys.Name}/system/sharding/myTypeProxy").ResolveOne(TimeSpan.FromSeconds(5));
            proxyRef.Path.Should().NotBeNull();
            proxyRef.Path.ToString().Should().EndWith("Proxy");
        }

        [Fact]
        public async Task Shard_region_be_found()
        {
            var shardRegion = await _clusterSharding.StartAsync(
                typeName: "myType",
                entityProps: Props.Create<EchoActor>(),
                settings: _shardingSettings,
                messageExtractor: _messageExtractor);

            shardRegion.Path.Should().NotBeNull();
            shardRegion.Path.ToString().Should().EndWith("myType");
        }

        [Fact]
        public async Task Shard_coordinator_should_be_found()
        {
            var shardRegion = await _clusterSharding.StartAsync(
                typeName: "myType",
                entityProps: Props.Create<EchoActor>(),
                settings: _shardingSettings,
                messageExtractor: _messageExtractor);
            
            var proxyRef = await Sys.ActorSelection($"akka://{Sys.Name}/system/sharding/myTypeCoordinator").ResolveOne(TimeSpan.FromSeconds(5));
            proxyRef.Path.Should().NotBeNull();
            proxyRef.Path.ToString().Should().EndWith("Coordinator");
        }
    }
}
