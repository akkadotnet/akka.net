//-----------------------------------------------------------------------
// <copyright file="ClusterShardingFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests;

public class ClusterShardingFailureSpecConfig : MultiNodeClusterShardingConfig
{
    public RoleName Controller { get; }
    public RoleName First { get; }
    public RoleName Second { get; }

    public ClusterShardingFailureSpecConfig(StateStoreMode mode)
        : base(mode: mode, loglevel: "DEBUG", additionalConfig: @"
            akka.cluster.roles = [""backend""]
            akka.cluster.sharding {
                coordinator-failure-backoff = 3s
                shard-failure-backoff = 3s
            }
            # don't leak ddata state across runs
            akka.cluster.sharding.distributed-data.durable.keys = []
            ")
    {
        Controller = Role("controller");
        First = Role("first");
        Second = Role("second");

        TestTransport = true;
    }
}

public class PersistentClusterShardingFailureSpecConfig : ClusterShardingFailureSpecConfig
{
    public PersistentClusterShardingFailureSpecConfig()
        : base(StateStoreMode.Persistence)
    {
    }
}

public class DDataClusterShardingFailureSpecConfig : ClusterShardingFailureSpecConfig
{
    public DDataClusterShardingFailureSpecConfig()
        : base(StateStoreMode.DData)
    {
    }
}

public class PersistentClusterShardingFailureSpec : ClusterShardingFailureSpec
{
    public PersistentClusterShardingFailureSpec()
        : base(new PersistentClusterShardingFailureSpecConfig(), typeof(PersistentClusterShardingFailureSpec))
    {
    }
}

public class DDataClusterShardingFailureSpec : ClusterShardingFailureSpec
{
    public DDataClusterShardingFailureSpec()
        : base(new DDataClusterShardingFailureSpecConfig(), typeof(DDataClusterShardingFailureSpec))
    {
    }
}

public abstract class ClusterShardingFailureSpec : MultiNodeClusterShardingSpec<ClusterShardingFailureSpecConfig>
{
    #region setup

    [Serializable]
    internal sealed record Get(string Id);

    [Serializable]
    internal sealed record Add(string Id, int I);

    [Serializable]
    internal sealed record Value(string Id, int N);

    internal class Entity : ReceiveActor
    {
        private ILoggingAdapter log = Context.GetLogger();
        private int _n = 0;

        public Entity()
        {
            log.Debug("Starting");
            Receive<Get>(get =>
            {
                log.Debug("Got get request from {0}", Sender);
                Sender.Tell(new Value(get.Id, _n));
            });
            Receive<Add>(add =>
            {
                _n += add.I;
                log.Debug("Got add request from {0}", Sender);
            });
        }

        protected override void PostStop()
        {
            log.Debug("Stopping");
            base.PostStop();
        }
    }

    private sealed class MessageExtractor: IMessageExtractor
    {
        public string EntityId(object message)
            => message switch
            {
                Get msg => msg.Id,
                Add msg => msg.Id,
                _ => null
            };

        public object EntityMessage(object message)
            => message;

        public string ShardId(object message)
            => message switch
            {
                Get msg => msg.Id[0].ToString(),
                Add msg => msg.Id[0].ToString(),
                _ => null
            };

        public string ShardId(string entityId, object messageHint = null)
            => entityId[0].ToString();
    }

    private readonly Lazy<IActorRef> _region;

    protected ClusterShardingFailureSpec(ClusterShardingFailureSpecConfig config, Type type)
        : base(config, type)
    {
        _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
    }

    private Task JoinAsync(RoleName from, RoleName to)
    {
        return JoinAsync(from, to, () =>
            StartSharding(
                Sys,
                typeName: "Entity",
                entityProps: Props.Create(() => new Entity()),
                messageExtractor: new MessageExtractor())
        );
    }

    #endregion

    [MultiNodeFact]
    public async Task ClusterSharding_with_flaky_journal_network_specs()
    {
        await ClusterSharding_with_flaky_journal_network_must_join_cluster();
        await ClusterSharding_with_flaky_journal_network_must_recover_after_journal_network_failure();
    }

    private async Task ClusterSharding_with_flaky_journal_network_must_join_cluster()
    {
        // No outer Within: EnterBarrierAsync derives its timeout from RemainingOr(barrier-timeout)
        // (MultiNodeSpec.cs:603), which clamps to zero once an enclosing Within's deadline has
        // passed. Each expectation below carries its own explicit bound instead, so the barrier
        // always gets the full akka.testconductor.barrier-timeout (30s default) rather than
        // whatever scraps a shared clock left over.
        await StartPersistenceIfNeededAsync(Config.Controller, CancellationToken.None, Config.First, Config.Second);

        await JoinAsync(Config.First, Config.First);
        await JoinAsync(Config.Second, Config.First);

        await RunOnAsync(async () =>
        {
            var region = _region.Value;
            region.Tell(new Add("10", 1));
            region.Tell(new Add("20", 2));
            region.Tell(new Add("21", 3));
            region.Tell(new Get("10"));
            await ExpectMsgAsync<Value>(v => v.Id == "10" && v.N == 1, TimeSpan.FromSeconds(5));
            region.Tell(new Get("20"));
            await ExpectMsgAsync<Value>(v => v.Id == "20" && v.N == 2, TimeSpan.FromSeconds(5));
            region.Tell(new Get("21"));
            await ExpectMsgAsync<Value>(v => v.Id == "21" && v.N == 3, TimeSpan.FromSeconds(5));
        }, Config.First);
        await EnterBarrierAsync("after-2");
    }

    private async Task ClusterSharding_with_flaky_journal_network_must_recover_after_journal_network_failure()
    {
        // No outer Within here (see the join-cluster phase above for why barriers must stay
        // outside one). The Get("21") retry below is the one operation in this phase that is
        // *expected* to take multiple seconds: the coordinator/shard must ride out
        // coordinator-failure-backoff / shard-failure-backoff (3s each,
        // ClusterShardingFailureSpecConfig) against the shared journal's 5s ask-timeout
        // (MemoryJournalShared, MultiNodeClusterShardingConfig.PersistenceConfig) before they can
        // recover from the blackhole. It gets its own bound, sized off those constants, instead
        // of borrowing from a shared clock that would starve the barriers and assertions after it.
        await RunOnAsync(async () =>
        {
            if (PersistenceIsNeeded)
            {
                await TestConductor.BlackholeAsync(Config.Controller, Config.First, ThrottleTransportAdapter.Direction.Both);
                await TestConductor.BlackholeAsync(Config.Controller, Config.Second, ThrottleTransportAdapter.Direction.Both);
            }
            else
            {
                await TestConductor.BlackholeAsync(Config.First, Config.Second, ThrottleTransportAdapter.Direction.Both);
            }
        }, Config.Controller);
        await EnterBarrierAsync("journal-backholded");

        await RunOnAsync(async () =>
        {
            // try with a new shard, will not reply until journal/network is available again
            var region = _region.Value;
            region.Tell(new Add("40", 4));
            var probe = CreateTestProbe();
            region.Tell(new Get("40"), probe.Ref);
            await probe.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }, Config.First);
        await EnterBarrierAsync("first-delayed");

        await RunOnAsync(async () =>
        {
            if (PersistenceIsNeeded)
            {
                await TestConductor.PassThroughAsync(Config.Controller, Config.First, ThrottleTransportAdapter.Direction.Both);
                await TestConductor.PassThroughAsync(Config.Controller, Config.Second, ThrottleTransportAdapter.Direction.Both);
            }
            else
            {
                await TestConductor.PassThroughAsync(Config.First, Config.Second, ThrottleTransportAdapter.Direction.Both);
            }
        }, Config.Controller);
        await EnterBarrierAsync("journal-ok");

        await RunOnAsync(async () =>
        {
            var region = _region.Value;

            // Confirm the ShardCoordinator/Shard have recovered and are routing again.
            // A single-shot expectation can legitimately lose a race against the coordinator's
            // and shard's own failure-backoff retry loop, so retry the request itself - bounded
            // by roughly two worst-case retry cycles (5s journal timeout + 3s backoff each) -
            // rather than gambling on one wait being long enough.
            IActorRef entity21 = null;
            await AwaitAssertAsync(async () =>
            {
                region.Tell(new Get("21"));
                await ExpectMsgAsync<Value>(v => v.Id == "21" && v.N == 3, TimeSpan.FromSeconds(3));
                entity21 = LastSender;
            }, TimeSpan.FromSeconds(30));
            var shard2 = Sys.ActorSelection(entity21.Path.Parent);

            //Test the ShardCoordinator allocating shards after a journal/network failure
            region.Tell(new Add("30", 3));

            //Test the Shard starting entities and persisting after a journal/network failure
            region.Tell(new Add("11", 1));

            //Test the Shard passivate works after a journal failure
            shard2.Tell(new Passivate(PoisonPill.Instance), entity21);

            await AwaitAssertAsync(async () =>
            {
                // Note that the order between this Get message to 21 and the above Passivate to 21 is undefined.
                // If this Get arrives first the reply will be Value("21", 3) and then it is retried by the
                // awaitAssert.
                region.Tell(new Get("21"));
                // counter reset to 0 when started again
                await ExpectMsgAsync<Value>(v => v.Id == "21" && v.N == 0, TimeSpan.FromSeconds(3), hint: "Passivating did not reset Value down to 0");
            }, TimeSpan.FromSeconds(10));

            region.Tell(new Add("21", 1));

            region.Tell(new Get("21"));
            await ExpectMsgAsync<Value>(v => v.Id == "21" && v.N == 1, TimeSpan.FromSeconds(5));

            region.Tell(new Get("30"));
            await ExpectMsgAsync<Value>(v => v.Id == "30" && v.N == 3, TimeSpan.FromSeconds(5));

            region.Tell(new Get("11"));
            await ExpectMsgAsync<Value>(v => v.Id == "11" && v.N == 1, TimeSpan.FromSeconds(5));

            region.Tell(new Get("40"));
            await ExpectMsgAsync<Value>(v => v.Id == "40" && v.N == 4, TimeSpan.FromSeconds(5));
        }, Config.First);
        await EnterBarrierAsync("verified-first");

        await RunOnAsync(async () =>
        {
            var region = _region.Value;
            region.Tell(new Add("10", 1));
            region.Tell(new Add("20", 2));
            region.Tell(new Add("30", 3));
            region.Tell(new Add("11", 4));
            region.Tell(new Get("10"));
            await ExpectMsgAsync<Value>(v => v.Id == "10" && v.N == 2, TimeSpan.FromSeconds(5));
            region.Tell(new Get("11"));
            await ExpectMsgAsync<Value>(v => v.Id == "11" && v.N == 5, TimeSpan.FromSeconds(5));
            region.Tell(new Get("20"));
            await ExpectMsgAsync<Value>(v => v.Id == "20" && v.N == 4, TimeSpan.FromSeconds(5));
            region.Tell(new Get("30"));
            await ExpectMsgAsync<Value>(v => v.Id == "30" && v.N == 6, TimeSpan.FromSeconds(5));
        }, Config.Second);
        await EnterBarrierAsync("after-3");
    }
}