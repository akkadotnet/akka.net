//-----------------------------------------------------------------------
// <copyright file="ReplicatorIncarnationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster;
using Akka.Configuration;
using Akka.DistributedData.Internal;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.DistributedData.Tests
{
    /// <summary>
    /// <para>
    /// The <see cref="Replicator"/> must tell two incarnations (UIDs) of one host:port apart.
    /// See https://github.com/akkadotnet/akka.net/issues/8991 and
    /// https://github.com/akkadotnet/akka.net/issues/8994.
    /// </para>
    /// <para>
    /// Before #8993, <c>ClusterEvent.DiffMemberEvents</c> published additions before removals, so
    /// when a node restarted on the same address a replicator could see <c>MemberUp(newUid)</c> and
    /// then <c>MemberRemoved(oldUid)</c>. The replicator must handle either order. These specs feed
    /// events straight to a replicator, the same way <see cref="ReplicatorKnownNodeSpec"/> does, so
    /// no second cluster or timing race is needed.
    /// </para>
    /// <para>
    /// The members are built with the internal <c>Member.Create</c> through reflection, because
    /// there is no public way to make a <see cref="Member"/> with a chosen UID, and Akka.Cluster
    /// does not expose its internals to this test assembly. The addresses point at a closed
    /// port; nothing is listening there.
    /// </para>
    /// </summary>
    public class ReplicatorIncarnationSpec : AkkaSpec
    {
        public static readonly Config SpecConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0")
            .WithFallback(DistributedData.DefaultConfig());

        private static readonly MethodInfo MemberCreate = typeof(Member).GetMethod(
            "Create",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(UniqueAddress), typeof(int), typeof(MemberStatus), typeof(ImmutableHashSet<string>), typeof(AppVersion) },
            null) ?? throw new InvalidOperationException("Member.Create(UniqueAddress, int, MemberStatus, ImmutableHashSet<string>, AppVersion) not found");

        private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(3);

        // Nothing listens on port 1, so messages the replicator sends to this address go nowhere.
        private readonly Address _restartedAddress;

        public ReplicatorIncarnationSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
            _restartedAddress = new Address("akka.tcp", Sys.Name + "-peer", "127.0.0.1", 1);
        }

        private static Member CreateMember(UniqueAddress node, int upNumber, MemberStatus status) =>
            (Member)MemberCreate.Invoke(null, new object[] { node, upNumber, status, ImmutableHashSet<string>.Empty, AppVersion.Zero })!;

        private async Task JoinSelfAsync()
        {
            var cluster = Cluster.Cluster.Get(Sys);
            cluster.Join(cluster.SelfAddress);
            await AwaitAssertAsync(() => cluster.SelfMember.Status.Should().Be(MemberStatus.Up));
        }

        private IActorRef CreateReplicator() => Sys.ActorOf(Replicator.Props(ReplicatorSettings.Create(Sys)));

        private static Write WriteFrom(UniqueAddress node, string key) =>
            new(key, new DataEnvelope(GCounter.Empty.Increment(node)), node);

        [Fact(DisplayName = "Should_accept_write_from_new_incarnation_When_MemberRemoved_for_old_incarnation_follows_its_MemberUp")]
        public async Task Should_accept_write_from_new_incarnation_When_MemberRemoved_for_old_incarnation_follows_its_MemberUp()
        {
            await JoinSelfAsync();
            var oldNode = new UniqueAddress(_restartedAddress, 1);
            var newNode = new UniqueAddress(_restartedAddress, 2);
            var oldMember = CreateMember(oldNode, 1, MemberStatus.Up);
            var newMember = CreateMember(newNode, 2, MemberStatus.Up);

            var replicator = CreateReplicator();
            replicator.Tell(new ClusterEvent.MemberUp(oldMember));

            // The order DiffMemberEvents used before #8993: the new incarnation's status event
            // first, then the removal of the old one.
            replicator.Tell(new ClusterEvent.MemberUp(newMember));
            replicator.Tell(new ClusterEvent.MemberRemoved(oldMember.Copy(MemberStatus.Removed), MemberStatus.Down));

            var probe = CreateTestProbe();
            replicator.Tell(WriteFrom(newNode, "incarnation-up-then-removed"), probe.Ref);
            await probe.ExpectMsgAsync<WriteAck>(ReplyTimeout);

            // The removal must not have dropped the new incarnation from the replica set.
            replicator.Tell(GetReplicaCount.Instance, probe.Ref);
            (await probe.ExpectMsgAsync<ReplicaCount>(ReplyTimeout)).N.Should().Be(2);
        }

        [Fact(DisplayName = "Should_ignore_write_from_new_incarnation_When_only_old_incarnation_is_a_member")]
        public async Task Should_ignore_write_from_new_incarnation_When_only_old_incarnation_is_a_member()
        {
            await JoinSelfAsync();
            var oldNode = new UniqueAddress(_restartedAddress, 1);
            var newNode = new UniqueAddress(_restartedAddress, 2);

            var replicator = CreateReplicator();
            replicator.Tell(new ClusterEvent.MemberUp(CreateMember(oldNode, 1, MemberStatus.Up)));

            // Make sure the MemberUp has been handled before the Write below.
            var probe = CreateTestProbe();
            replicator.Tell(GetReplicaCount.Instance, probe.Ref);
            (await probe.ExpectMsgAsync<ReplicaCount>(ReplyTimeout)).N.Should().Be(2);

            // The restarted process has not joined yet, so its writes must be dropped.
            await EventFilter.Info(contains: "unknown node").ExpectOneAsync(async () =>
            {
                replicator.Tell(WriteFrom(newNode, "incarnation-not-joined"), probe.Ref);
                await Task.CompletedTask;
            });
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

            // A write from the incarnation that is a member still goes through.
            replicator.Tell(WriteFrom(oldNode, "incarnation-not-joined"), probe.Ref);
            await probe.ExpectMsgAsync<WriteAck>(ReplyTimeout);
        }
    }
}
