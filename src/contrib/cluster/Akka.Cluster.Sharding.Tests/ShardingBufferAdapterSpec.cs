//-----------------------------------------------------------------------
// <copyright file="ShardingBufferAdapterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Cluster.Sharding.Tests;

public class ShardingBufferAdapterSpec: AkkaSpec
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
                => (int.Parse(entityId) % 10).ToString();
        }

    private class EntityActor : ActorBase
    {
        protected override bool Receive(object message)
        {
            Sender.Tell(message);
            return true;
        }
    }
    
    private class TestMessageAdapter: IShardingBufferMessageAdapter
    {
        private readonly AtomicCounter _counter;

        public TestMessageAdapter(AtomicCounter counter)
        {
            _counter = counter;
        }

        public object Apply(object message, IActorContext context)
        {
            _counter.IncrementAndGet();
            return message;
        }

        public object UnApply(object message, IActorContext context)
        {
            return message;
        }
    }

    private const string ShardTypeName = "Caat";

    private static Config SpecConfig =>
        ConfigurationFactory.ParseString("""
                                         
                                                     akka.loglevel = DEBUG
                                                     akka.actor.provider = cluster
                                                     akka.remote.dot-netty.tcp.port = 0
                                                     akka.remote.log-remote-lifecycle-events = off
                                         
                                                     akka.test.single-expect-default = 5 s
                                                     akka.cluster.sharding.state-store-mode = "ddata"
                                                     akka.cluster.sharding.verbose-debug-logging = on
                                                     akka.cluster.sharding.fail-on-invalid-entity-state-transition = on
                                                     akka.cluster.sharding.distributed-data.durable.keys = []
                                         """)
            .WithFallback(ClusterSingleton.DefaultConfig()
            .WithFallback(ClusterSharding.DefaultConfig()));

    private readonly AtomicCounter _counterA = new (0);
    private readonly AtomicCounter _counterB = new (0);

    private readonly ActorSystem _sysA;
    private readonly ActorSystem _sysB;

    private readonly TestProbe _pA;
    private readonly TestProbe _pB;

    private readonly IActorRef _regionA;
    private readonly IActorRef _regionB;
    
    public ShardingBufferAdapterSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
    {
        _sysA = Sys;
        _sysB = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        
        InitializeLogger(_sysB, "[sysB]");
        
        // ReSharper disable VirtualMemberCallInConstructor
        _pA = CreateTestProbe(_sysA);
        _pB = CreateTestProbe(_sysB);
        // ReSharper restore VirtualMemberCallInConstructor
        
        ClusterSharding.Get(_sysA).SetShardingBufferMessageAdapter(new TestMessageAdapter(_counterA));
        ClusterSharding.Get(_sysB).SetShardingBufferMessageAdapter(new TestMessageAdapter(_counterB));
        
        _regionA = StartShard(_sysA);
        _regionB = StartShard(_sysB);
    }

    protected override void AfterAll()
    {
        if(_sysA != null)
            Shutdown(_sysA);
        if(_sysB != null)
            Shutdown(_sysB);
        base.AfterAll();
    }
    
    private IActorRef StartShard(ActorSystem sys)
    {
        return ClusterSharding.Get(sys).Start(
            ShardTypeName,
            Props.Create(() => new EntityActor()),
            ClusterShardingSettings.Create(Sys).WithRememberEntities(true),
            new MessageExtractor());
    }
    
    [Fact(DisplayName = "ClusterSharding buffer message adapter must be called when message was buffered")]
    public async Task ClusterSharding_must_initialize_cluster_and_allocate_sharded_actors()
    {
        await Cluster.Get(_sysA).JoinAsync(Cluster.Get(_sysA).SelfAddress); // coordinator on A
        
        await AwaitAssertAsync(() =>
        {
            Cluster.Get(_sysA).SelfMember.Status.Should().Be(MemberStatus.Up);
        }, TimeSpan.FromSeconds(1));

        await Cluster.Get(_sysB).JoinAsync(Cluster.Get(_sysA).SelfAddress);

        await WithinAsync(TimeSpan.FromSeconds(10), async () =>
        {
            await AwaitAssertAsync(async () =>
            {
                foreach (var s in ImmutableHashSet.Create(_sysA, _sysB))
                {
                    Cluster.Get(s).SendCurrentClusterState(TestActor);
                    (await ExpectMsgAsync<ClusterEvent.CurrentClusterState>()).Members.Count.Should().Be(2);
                }
            });
        });

        // need to make sure that ShardingEnvelope doesn't impacted by this change
        _regionA.Tell(new ShardingEnvelope("1", 1), _pA.Ref);
        await _pA.ExpectMsgAsync(1);

        _regionB.Tell(2, _pB.Ref);
        await _pB.ExpectMsgAsync(2);

        _regionB.Tell(3, _pB.Ref);
        await _pB.ExpectMsgAsync(3);

        var counterAValue = _counterA.Current;
        var counterBValue = _counterB.Current;
        
        // Each newly instantiated entities should have their messages buffered at least once
        // Buffer message adapter should be called everytime a message is buffered
        counterAValue.Should().BeGreaterOrEqualTo(1);
        counterBValue.Should().BeGreaterOrEqualTo(2);
        
        _regionA.Tell(1, _pA.Ref);
        await _pA.ExpectMsgAsync(1);

        _regionB.Tell(2, _pB.Ref);
        await _pB.ExpectMsgAsync(2);

        _regionB.Tell(3, _pB.Ref);
        await _pB.ExpectMsgAsync(3);
        
        // Each entity should not have their messages buffered once they were instantiated
        _counterA.Current.Should().Be(counterAValue);
        _counterB.Current.Should().Be(counterBValue);
    }
}
