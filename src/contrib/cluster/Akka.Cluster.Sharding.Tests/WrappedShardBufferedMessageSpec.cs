// -----------------------------------------------------------------------
//  <copyright file="WrappedShardBufferedMessageSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests;

public class WrappedShardBufferedMessageSpec: AkkaSpec
{
    #region Custom Classes

    private sealed class MyEnvelope : IWrappedMessage
    {
        public MyEnvelope(object message)
        {
            Message = message;
        }

        public object Message { get; }
    }
    
    private sealed class BufferMessageAdapter: IShardingBufferMessageAdapter
    {
        public object Apply(object message, IActorContext context)
            => new MyEnvelope(message);

        public object UnApply(object message, IActorContext context)
        {
            return message is MyEnvelope envelope ? envelope.Message : message;
        }
    }
    
    private class EchoActor: UntypedActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        protected override void OnReceive(object message)
        {
            _log.Info($">>>> OnReceive {message.GetType()}: {message}");
            if(message is string)
                Sender.Tell(message);
            else
                Unhandled(message);
        }
    }

    private sealed class FakeRememberEntitiesProvider: IRememberEntitiesProvider
    {
        private readonly IActorRef _probe;

        public FakeRememberEntitiesProvider(IActorRef probe)
        {
            _probe = probe;
        }

        public Props CoordinatorStoreProps() => FakeCoordinatorStoreActor.Props();

        public Props ShardStoreProps(string shardId) => FakeShardStoreActor.Props(shardId, _probe);
    }
    
    private class ShardStoreCreated
    {
        public ShardStoreCreated(IActorRef store, string shardId)
        {
            Store = store;
            ShardId = shardId;
        }

        public IActorRef Store { get; }
        public string ShardId { get; }
    }

    private class CoordinatorStoreCreated
    {
        public CoordinatorStoreCreated(IActorRef store)
        {
            Store = store;
        }

        public IActorRef Store { get; }
    }
    
    private class FakeShardStoreActor : ActorBase
    {
        public static Props Props(string shardId, IActorRef probe) => Actor.Props.Create(() => new FakeShardStoreActor(shardId, probe));

        private readonly string _shardId;
        private readonly IActorRef _probe;

        public FakeShardStoreActor(string shardId, IActorRef probe)
        {
            _shardId = shardId;
            _probe = probe;
            Context.System.EventStream.Publish(new ShardStoreCreated(Self, shardId));
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case RememberEntitiesShardStore.GetEntities:
                    Sender.Tell(new RememberEntitiesShardStore.RememberedEntities(ImmutableHashSet<string>.Empty));
                    return true;
                case RememberEntitiesShardStore.Update m:
                    _probe.Tell(new RememberEntitiesShardStore.UpdateDone(m.Started, m.Stopped));
                    return true;
            }
            return false;
        }
    }

    private class FakeCoordinatorStoreActor : ActorBase
    {
        public static Props Props() => Actor.Props.Create(() => new FakeCoordinatorStoreActor());

        public FakeCoordinatorStoreActor()
        {
            Context.System.EventStream.Publish(new CoordinatorStoreCreated(Context.Self));
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case RememberEntitiesCoordinatorStore.GetShards _:
                    Sender.Tell(new RememberEntitiesCoordinatorStore.RememberedShards(ImmutableHashSet<string>.Empty));
                    return true;
                case RememberEntitiesCoordinatorStore.AddShard m:
                    Sender.Tell(new RememberEntitiesCoordinatorStore.UpdateDone(m.ShardId));
                    return true;
            }
            return false;
        }
    }

    private static Config GetConfig()
    {
        return ConfigurationFactory.ParseString(@"
                akka.loglevel=DEBUG
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding.state-store-mode = ddata
                akka.cluster.sharding.remember-entities = on

                # no leaks between test runs thank you
                akka.cluster.sharding.distributed-data.durable.keys = []
                akka.cluster.sharding.verbose-debug-logging = on
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on")

            .WithFallback(Sharding.ClusterSharding.DefaultConfig())
            .WithFallback(DistributedData.DistributedData.DefaultConfig())
            .WithFallback(ClusterSingleton.DefaultConfig());
    }
    
    #endregion
    
    private const string Msg = "hit";
    private readonly IActorRef _shard;
    private IActorRef _store;

    public WrappedShardBufferedMessageSpec(ITestOutputHelper output) : base(GetConfig(), output)
    {
        Sys.EventStream.Subscribe(TestActor, typeof(ShardStoreCreated));
        Sys.EventStream.Subscribe(TestActor, typeof(CoordinatorStoreCreated));
        
        _shard = ChildActorOf(Shard.Props(
            typeName: "test",
            shardId: "test",
            entityProps: _ => Props.Create(() => new EchoActor()), 
            settings: ClusterShardingSettings.Create(Sys), 
            extractor: new ExtractorAdapter(HashCodeMessageExtractor.Create(10, m => m.ToString())), 
            handOffStopMessage: PoisonPill.Instance, 
            rememberEntitiesProvider: new FakeRememberEntitiesProvider(TestActor),
            bufferMessageAdapter: new BufferMessageAdapter()));
    }

    private async Task<RememberEntitiesShardStore.UpdateDone> ExpectShardStartup()
    {
        var createdEvent = await ExpectMsgAsync<ShardStoreCreated>();
        createdEvent.ShardId.Should().Be("test");
        
        _store = createdEvent.Store;
        
        await ExpectMsgAsync<ShardInitialized>();
        
        _shard.Tell(new ShardRegion.StartEntity(Msg));
        
        return await ExpectMsgAsync<RememberEntitiesShardStore.UpdateDone>();
    }
    
    [Fact(DisplayName = "Message wrapped in ShardingEnvelope, buffered by Shard, transformed by BufferMessageAdapter, must arrive in entity actor")]
    public async Task WrappedMessageDelivery()
    {
        IgnoreMessages<ShardRegion.StartEntityAck>();
        
        var continueMessage = await ExpectShardStartup();
        
        // this message should be buffered
        _shard.Tell(new ShardingEnvelope(Msg, Msg));
        await Task.Yield();
        
        // Tell shard to continue processing
        _shard.Tell(continueMessage);

        await ExpectMsgAsync(Msg);
    }
}