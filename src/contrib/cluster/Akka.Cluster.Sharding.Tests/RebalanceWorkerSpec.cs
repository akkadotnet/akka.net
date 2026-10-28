//-----------------------------------------------------------------------
// <copyright file="RebalanceWorkerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using static Akka.Cluster.Sharding.ShardCoordinator;

namespace Akka.Cluster.Sharding.Tests;

public sealed class RebalanceWorkerSpec : AkkaSpec
{
    private static readonly TimeSpan ExpectationTimeout = TimeSpan.FromSeconds(3);

    public RebalanceWorkerSpec(ITestOutputHelper output)
        : base(ConfigurationFactory.ParseString("akka.loglevel = DEBUG"), output)
    {
    }

    [Theory(DisplayName = "Should complete handoff after all acknowledgements when the owner terminates during the acknowledgement phase")]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public async Task Should_CompleteHandoff_When_OwnerTerminatesBeforeAcknowledgementsFinish(
        bool ownerAcknowledged, bool ownerTerminatesLast, bool isRebalance)
    {
        var owner = CreateTestProbe();
        var survivor = CreateTestProbe();
        var worker = await ChildActorOfAsync(RebalanceWorker.Props("Entity", "1", owner.Ref,
            TimeSpan.FromMinutes(1), new[] { owner.Ref, survivor.Ref }, isRebalance));
        (await owner.ExpectMsgAsync<BeginHandOff>(ExpectationTimeout)).Shard.Should().Be("1");
        (await survivor.ExpectMsgAsync<BeginHandOff>(ExpectationTimeout)).Shard.Should().Be("1");

        if (ownerAcknowledged)
            worker.Tell(new BeginHandOffAck("1"), owner.Ref);
        if (ownerTerminatesLast)
            worker.Tell(new BeginHandOffAck("1"), survivor.Ref);
        worker.Tell(new RebalanceWorker.ShardRegionTerminated(owner.Ref));

        if (!ownerTerminatesLast)
        {
            worker.Tell(new Identify("awaiting-survivor"), TestActor);
            (await ExpectMsgAsync<ActorIdentity>(ExpectationTimeout)).Subject.Should().Be(worker);
            worker.Tell(new BeginHandOffAck("1"), survivor.Ref);
        }

        var completed = await ExpectMsgAsync<RebalanceDone>(ExpectationTimeout);
        completed.Shard.Should().Be("1");
        completed.Ok.Should().BeTrue();
        await owner.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        await WatchAsync(worker);
        await ExpectTerminatedAsync(worker, ExpectationTimeout);
    }

    [Theory(DisplayName = "Should wait for the live owner to stop the shard after acknowledgement collection")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_WaitForShardStopped_When_OwnerIsAlive(bool ownerTerminatesAfterHandoff)
    {
        var owner = CreateTestProbe();
        var survivor = CreateTestProbe();
        var worker = await ChildActorOfAsync(RebalanceWorker.Props("Entity", "1", owner.Ref,
            TimeSpan.FromMinutes(1), new[] { owner.Ref, survivor.Ref }, true));
        await owner.ExpectMsgAsync<BeginHandOff>(ExpectationTimeout);
        await survivor.ExpectMsgAsync<BeginHandOff>(ExpectationTimeout);

        worker.Tell(new BeginHandOffAck("1"), owner.Ref);
        worker.Tell(new BeginHandOffAck("1"), survivor.Ref);
        (await owner.ExpectMsgAsync<HandOff>(ExpectationTimeout)).Shard.Should().Be("1");
        worker.Tell(new Identify("waiting-for-shard-stop"), TestActor);
        (await ExpectMsgAsync<ActorIdentity>(ExpectationTimeout)).Subject.Should().Be(worker);

        if (ownerTerminatesAfterHandoff)
            worker.Tell(new RebalanceWorker.ShardRegionTerminated(owner.Ref));
        else
            worker.Tell(new ShardStopped("1"), owner.Ref);
        (await ExpectMsgAsync<RebalanceDone>(ExpectationTimeout)).Ok.Should().BeTrue();
    }

    [Fact(DisplayName = "Should preserve the handoff request when another region terminates")]
    public async Task Should_RequestHandoff_When_NonOwnerTerminates()
    {
        var owner = CreateTestProbe();
        var survivor = CreateTestProbe();
        var departing = CreateTestProbe();
        var worker = await ChildActorOfAsync(RebalanceWorker.Props("Entity", "1", owner.Ref,
            TimeSpan.FromMinutes(1), new[] { owner.Ref, survivor.Ref, departing.Ref }, true));
        await owner.ExpectMsgAsync<BeginHandOff>(ExpectationTimeout);
        await survivor.ExpectMsgAsync<BeginHandOff>(ExpectationTimeout);
        await departing.ExpectMsgAsync<BeginHandOff>(ExpectationTimeout);

        worker.Tell(new RebalanceWorker.ShardRegionTerminated(departing.Ref));
        worker.Tell(new BeginHandOffAck("1"), owner.Ref);
        worker.Tell(new BeginHandOffAck("1"), survivor.Ref);
        (await owner.ExpectMsgAsync<HandOff>(ExpectationTimeout)).Shard.Should().Be("1");
        worker.Tell(new ShardStopped("1"), owner.Ref);
        (await ExpectMsgAsync<RebalanceDone>(ExpectationTimeout)).Ok.Should().BeTrue();
    }

    [Theory(DisplayName = "Should report unsuccessful handoff when either protocol phase times out")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_ReportFailure_When_HandoffTimesOut(bool acknowledgementsComplete)
    {
        var owner = CreateTestProbe();
        var worker = await ChildActorOfAsync(RebalanceWorker.Props("Entity", "1", owner.Ref,
            TimeSpan.FromMinutes(1), new[] { owner.Ref }, true));
        await owner.ExpectMsgAsync<BeginHandOff>(ExpectationTimeout);
        if (acknowledgementsComplete)
        {
            worker.Tell(new BeginHandOffAck("1"), owner.Ref);
            await owner.ExpectMsgAsync<HandOff>(ExpectationTimeout);
        }

        worker.Tell(ReceiveTimeout.Instance);
        var completed = await ExpectMsgAsync<RebalanceDone>(ExpectationTimeout);
        completed.Shard.Should().Be("1");
        completed.Ok.Should().BeFalse();
        await WatchAsync(worker);
        await ExpectTerminatedAsync(worker, ExpectationTimeout);
    }
}
