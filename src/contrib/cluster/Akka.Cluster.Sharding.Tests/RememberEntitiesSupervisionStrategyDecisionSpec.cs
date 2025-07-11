//-----------------------------------------------------------------------
// <copyright file="RememberEntitiesFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Cluster.Sharding.Internal;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests;

public class RememberEntitiesSupervisionStrategyDecisionSpec : AkkaSpec
{
    private sealed record EntityEnvelope(long Id, object Payload);

    private class ConstructorFailActor : ActorBase
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public ConstructorFailActor()
        {
            throw new Exception("EXPLODING CONSTRUCTOR!");
        }

        protected override bool Receive(object message)
        {
            _log.Info("Msg {0}", message);
            Sender.Tell($"ack {message}");
            return true;
        }
    }

    private class PreStartFailActor : ActorBase
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        protected override void PreStart()
        {
            base.PreStart();
            throw new Exception("EXPLODING PRE-START!");
        }

        protected override bool Receive(object message)
        {
            _log.Info("Msg {0}", message);
            Sender.Tell($"ack {message}");
            return true;
        }
    }
    
    private sealed class TestMessageExtractor: IMessageExtractor
    {
        public string EntityId(object message)
            => message switch
            {
                EntityEnvelope env => env.Id.ToString(),
                _ => null
            };

        public object EntityMessage(object message)
            => message switch
            {
                EntityEnvelope env => env.Payload,
                _ => message
            };

        public string ShardId(object message)
            => message switch
            {
                EntityEnvelope msg => msg.Id.ToString(),
                _ => null
            };

        public string ShardId(string entityId, object messageHint = null)
            => entityId;
    }
    
    private class FakeShardRegion : ReceiveActor
    {
        private readonly ClusterShardingSettings _settings;
        private readonly Props _entityProps;
        private IActorRef? _shard;
        
        public FakeShardRegion(ClusterShardingSettings settings, Props entityProps)
        {
            _settings = settings;
            _entityProps = entityProps;
            
            Receive<ShardInitialized>(_ =>
            {
                // no-op
            });
            Receive<ShardRegion.StartEntity>(msg =>
            {
                _shard.Forward(msg);
            });
        }

        protected override void PreStart()
        {
            base.PreStart();
            var provider = new FakeStore(_settings, "cats");

            var props = Props.Create(() => new Shard(
                "cats",
                "shard-1",
                _ => _entityProps,
                _settings,
                new TestMessageExtractor(),
                PoisonPill.Instance,
                provider,
                null
            ));
            _shard = Context.ActorOf(props);
        }
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

    private class FakeStore : IRememberEntitiesProvider
    {
        public FakeStore(ClusterShardingSettings settings, string typeName)
        {
        }

        public Props ShardStoreProps(string shardId)
        {
            return FakeShardStoreActor.Props(shardId);
        }

        public Props CoordinatorStoreProps()
        {
            return FakeCoordinatorStoreActor.Props();
        }
    }

    private class FakeShardStoreActor : ActorBase, IWithTimers
    {
        public static Props Props(string shardId) => Actor.Props.Create(() => new FakeShardStoreActor(shardId));

        private readonly string _shardId;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public FakeShardStoreActor(string shardId)
        {
            _shardId = shardId;
            Context.System.EventStream.Publish(new ShardStoreCreated(Self, shardId));
        }

        public ITimerScheduler Timers { get; set; }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case RememberEntitiesShardStore.GetEntities _:
                    Sender.Tell(new RememberEntitiesShardStore.RememberedEntities(ImmutableHashSet<string>.Empty.Add("1")));
                    return true;
                case RememberEntitiesShardStore.Update m:
                    Sender.Tell(new RememberEntitiesShardStore.UpdateDone(m.Started, m.Stopped));
                    return true;
            }
            return false;
        }
    }

    private class FakeCoordinatorStoreActor : ActorBase, IWithTimers
    {
        public static Props Props() => Actor.Props.Create(() => new FakeCoordinatorStoreActor());

        private readonly ILoggingAdapter _log = Context.GetLogger();

        public ITimerScheduler Timers { get; set; }

        public FakeCoordinatorStoreActor()
        {
            Context.System.EventStream.Publish(new CoordinatorStoreCreated(Context.Self));
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case RememberEntitiesCoordinatorStore.GetShards _:
                    Sender.Tell(new RememberEntitiesCoordinatorStore.RememberedShards(ImmutableHashSet<string>.Empty.Add("1")));
                    return true;
                case RememberEntitiesCoordinatorStore.AddShard m:
                    Sender.Tell(new RememberEntitiesCoordinatorStore.UpdateDone(m.ShardId));
                    return true;
            }
            return false;
        }
    }

    private class TestSupervisionStrategy: ShardSupervisionStrategy
    {
        private readonly AtomicCounter _counter;
        
        public TestSupervisionStrategy(AtomicCounter counter, int maxRetry, int window, Func<Exception, Directive> localOnlyDecider)
            : base(maxRetry, window, localOnlyDecider)
        {
            _counter = counter;
        }

        public override void ProcessFailure(IActorContext context, bool restart, IActorRef child, Exception cause, ChildRestartStats stats,
            IReadOnlyCollection<ChildRestartStats> children)
        {
            _counter.GetAndIncrement();
            base.ProcessFailure(context, restart, child, cause, stats, children);
        }
    }
    
    private static Config SpecConfig =>
        ConfigurationFactory.ParseString(
                """
                akka {
                    loglevel = DEBUG
                    actor.provider = cluster
                    remote.dot-netty.tcp.port = 0
                    
                    cluster.sharding {
                        distributed-data.durable.keys = []
                        state-store-mode = ddata
                        remember-entities = on
                        remember-entities-store = custom
                        remember-entities-custom-store = "Akka.Cluster.Sharding.Tests.RememberEntitiesSupervisionStrategyDecisionSpec+FakeStore, Akka.Cluster.Sharding.Tests"
                        verbose-debug-logging = on
                    }
                }
                """)
            .WithFallback(ClusterSingleton.DefaultConfig())
            .WithFallback(ClusterSharding.DefaultConfig());

    public RememberEntitiesSupervisionStrategyDecisionSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
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

    public Directive TestDecider(Exception cause)
    {
        return Directive.Restart;
    }
    
    [Fact(DisplayName = "Persistent shard must stop remembered entity with excessive failures")]
    public async Task Persistent_Shard_must_stop_remembered_entity_with_excessive_restart_attempt()
    {
        var strategyCounter = new AtomicCounter(0);
        
        var settings = ClusterShardingSettings.Create(Sys);
        settings = settings
            .WithTuningParameters(settings.TuningParameters.WithEntityRestartBackoff(0.1.Seconds()))
            .WithRememberEntities(true)
            .WithSupervisorStrategy(new TestSupervisionStrategy(strategyCounter, 3, 1000, TestDecider));

        var storeProbe = CreateTestProbe();
        Sys.EventStream.Subscribe<ShardStoreCreated>(storeProbe);
        Sys.EventStream.Subscribe<Error>(TestActor);

        var entityProps = Props.Create(() => new PreStartFailActor());
        await EventFilter.Error(contains: "cats: Remembered entity 1 was stopped: entity failed repeatedly")
            .ExpectOneAsync(async () =>
            {
                _ = Sys.ActorOf(Props.Create(() => new FakeShardRegion(settings, entityProps)));
                storeProbe.ExpectMsg<ShardStoreCreated>();
                await Task.Yield();
            });

        // Failed on the 4th call
        strategyCounter.Current.Should().Be(4);
    }
    
    [Fact(DisplayName = "Persistent shard must stop remembered entity when stopped using Directive.Stop decision")]
    public async Task Persistent_Shard_must_stop_remembered_entity_with_stop_directive_on_constructor_failure()
    {
        var strategyCounter = new AtomicCounter(0);
        
        var settings = ClusterShardingSettings.Create(Sys);
        settings = settings
            .WithTuningParameters(settings.TuningParameters.WithEntityRestartBackoff(0.1.Seconds()))
            .WithRememberEntities(true)
            .WithSupervisorStrategy(new TestSupervisionStrategy(strategyCounter, 3, 1000, SupervisorStrategy.DefaultDecider.Decide));

        var storeProbe = CreateTestProbe();
        Sys.EventStream.Subscribe<ShardStoreCreated>(storeProbe);
        Sys.EventStream.Subscribe<Error>(TestActor);

        var entityProps = Props.Create(() => new ConstructorFailActor());
        await EventFilter.Error(contains: "cats: Remembered entity 1 was stopped: entity stopped by Directive.Stop decision")
            .ExpectOneAsync(async () =>
            {
                _ = Sys.ActorOf(Props.Create(() => new FakeShardRegion(settings, entityProps)));
                storeProbe.ExpectMsg<ShardStoreCreated>();
                await Task.Yield();
            });

        // Failed on the 1st call
        strategyCounter.Current.Should().Be(1);
    }
    
}