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
        // TODO(#8545): TestKit will gain an async dispose chain (override DisposeAsync) -
        // move these two to `await ShutdownAsync(...)` then. Until then they have to stay
        // blocking here.
        //
        // _sysA is Sys: TestKit.Dispose's `finally` already calls the parameterless
        // Shutdown() (== Shutdown(Sys)) right after AfterAll returns, so shutting _sysA down
        // here too was a redundant blocking wait on every run. Only _sysB needs shutting
        // down explicitly.
        if(_sysB != null)
            Shutdown(_sysB);
        base.AfterAll();
    }

    private static ClusterShardingSettings ShardingSettings(ActorSystem sys)
        => ClusterShardingSettings.Create(sys).WithRememberEntities(true);

    private IActorRef StartShard(ActorSystem sys)
    {
        // was ClusterShardingSettings.Create(Sys) - sysB's region was built from sysA's
        // settings. Harmless today (sysB is created from Sys.Settings.Config, so the two are
        // identical) but wrong: use the settings of the system whose region is being started.
        return ClusterSharding.Get(sys).Start(
            ShardTypeName,
            Props.Create(() => new EntityActor()),
            ShardingSettings(sys),
            new MessageExtractor());
    }

    /// <summary>
    /// The first message through a region has to survive coordinator allocation (two ddata
    /// majority writes), shard start (five ddata majority reads) and entity start (one more
    /// majority write) - and on a two-node cluster majority-min-cap (5) exceeds the cluster
    /// size, so every one of those is majority == all. If the Shard's remember-entities write
    /// does not complete in time the Shard throws and the ShardRegion restarts it, and the
    /// restart takes the Shard's _messageBuffers with it while the region has already emptied
    /// its own buffer for that shard. Sharding is at-most-once: the request has to be
    /// re-sent, not merely re-awaited.
    /// </summary>
    private async Task<IActorRef> FirstMessageThrough<T>(
        ActorSystem sys, IActorRef region, object message, T expected)
    {
        var tuning = ShardingSettings(sys).TuningParameters;

        // One shard-start-timeout (the region's own budget for "the shard did not start")
        // plus one updating-state-timeout (the remember-entities store's write budget):
        // the cold path plus one full Shard restart cycle.
        var budget = tuning.ShardStartTimeout + tuning.UpdatingStateTimeout;

        IActorRef entity = null;
        await AwaitAssertAsync(async () =>
        {
            // Fresh probe per attempt: a late reply to a timed-out attempt must not be
            // mistaken for this attempt's, and must not be left sitting in _pA/_pB for the
            // second phase below to consume.
            var probe = CreateTestProbe(sys);
            region.Tell(message, probe.Ref);
            // Per-attempt bound = the region's re-ask cadence for an unanswered
            // GetShardHome, so the loop actually iterates instead of spending the whole
            // budget in one expect.
            await probe.ExpectMsgAsync(expected, tuning.RetryInterval);
            entity = probe.LastSender;
        }, budget, TimeSpan.FromMilliseconds(500));

        return entity;
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
        var entityA1 = await FirstMessageThrough(_sysA, _regionA, new ShardingEnvelope("1", 1), 1);
        var entityB2 = await FirstMessageThrough(_sysB, _regionB, 2, 2);
        var entityB3 = await FirstMessageThrough(_sysB, _regionB, 3, 3);

        var counterAValue = _counterA.Current;
        var counterBValue = _counterB.Current;

        // Each newly instantiated entities should have their messages buffered at least once
        // Buffer message adapter should be called everytime a message is buffered
        counterAValue.Should().BeGreaterOrEqualTo(1);
        counterBValue.Should().BeGreaterOrEqualTo(2);

        // Phase two: the entities are live, so nothing should reach the buffer. These stay
        // single sends on purpose - re-sending here would let a Shard restart hide behind a
        // retry and make the counter assertions below meaningless. The bound is the
        // remember-entities store's own write budget instead of the flat
        // akka.test.single-expect-default the probes otherwise fall back to.
        var warm = ShardingSettings(_sysA).TuningParameters.UpdatingStateTimeout;

        _regionA.Tell(1, _pA.Ref);
        await _pA.ExpectMsgAsync(1, warm);
        // Same entity incarnation as phase one. IActorRef equality compares the path's uid as
        // well as the path (ActorPath.Equals alone compares only address and names), so a
        // Shard restart between phases (which would recreate the entity under a new uid)
        // shows up here instead of leaving the counter assertions below comparing two
        // different buffer histories.
        _pA.LastSender.Should().Be(entityA1,
            because: "a Shard restart between phases would make the counter assertions compare two different buffer histories");

        _regionB.Tell(2, _pB.Ref);
        await _pB.ExpectMsgAsync(2, warm);
        _pB.LastSender.Should().Be(entityB2,
            because: "a Shard restart between phases would make the counter assertions compare two different buffer histories");

        _regionB.Tell(3, _pB.Ref);
        await _pB.ExpectMsgAsync(3, warm);
        _pB.LastSender.Should().Be(entityB3,
            because: "a Shard restart between phases would make the counter assertions compare two different buffer histories");

        // Each entity should not have their messages buffered once they were instantiated
        _counterA.Current.Should().Be(counterAValue);
        _counterB.Current.Should().Be(counterBValue);
    }
}
