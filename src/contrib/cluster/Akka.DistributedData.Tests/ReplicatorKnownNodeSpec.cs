//-----------------------------------------------------------------------
// <copyright file="ReplicatorKnownNodeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster;
using Akka.Configuration;
using Akka.DistributedData.Internal;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.DistributedData.Tests
{
    /// <summary>
    /// <para>
    /// Covers a gap in <see cref="Replicator"/>'s cluster membership tracking: it subscribes to
    /// cluster events with `InitialStateAsEvents`, and that replay reports each current member
    /// at its CURRENT status. A replicator that starts after a member has already moved to
    /// Leaving (or Exiting) therefore receives `MemberLeft`/`MemberExited` for it and never
    /// `MemberUp`. Before the fix, that meant every `Write`/gossip message the member sent was
    /// silently dropped by `IsKnownNode` as coming from an "unknown node" -- for as long as the
    /// member stayed in the cluster. In cluster sharding, that can stall a departing shard
    /// coordinator's ddata write for its whole `updating-state-timeout` during a rolling
    /// restart or coordinated shutdown.
    /// </para>
    /// <para>
    /// These specs reproduce the gap directly at the message level instead of racing a real
    /// member leave: <see cref="_remoteSys"/> is its own single-node cluster that <see cref="AkkaSpec.Sys"/>
    /// never joins, so Sys's own (real) cluster subscription never learns about it. We take a
    /// real, valid <see cref="Member"/> from that foreign cluster, move it to the status we
    /// want with the public <see cref="Member.Copy"/>, and hand it to a freshly created
    /// replicator as the member event a late subscriber would have received -- deterministically,
    /// with no timing race.
    /// </para>
    /// </summary>
    public class ReplicatorKnownNodeSpec : AkkaSpec
    {
        public static readonly Config SpecConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0")
            .WithFallback(DistributedData.DefaultConfig());

        private readonly ActorSystem _remoteSys;

        public ReplicatorKnownNodeSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
            _remoteSys = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        }

        private async Task<Member> SelfJoinAndGetUpMemberAsync(ActorSystem system)
        {
            var cluster = Cluster.Cluster.Get(system);
            cluster.Join(cluster.SelfAddress);
            await AwaitAssertAsync(() => cluster.SelfMember.Status.Should().Be(MemberStatus.Up));
            return cluster.SelfMember;
        }

        [Fact(DisplayName = "Replicator should accept a Write from a member it first saw as Leaving")]
        public async Task Should_accept_write_from_member_first_seen_as_Leaving()
        {
            await SelfJoinAndGetUpMemberAsync(Sys);
            var remoteUp = await SelfJoinAndGetUpMemberAsync(_remoteSys);
            var leavingMember = remoteUp.Copy(MemberStatus.Leaving);

            var replicator = Sys.ActorOf(Replicator.Props(ReplicatorSettings.Create(Sys)));

            // Simulates what a replicator subscribing after the member already left would get on
            // InitialStateAsEvents replay: MemberLeft, never MemberUp.
            replicator.Tell(new ClusterEvent.MemberLeft(leavingMember));

            var probe = CreateTestProbe();
            var envelope = new DataEnvelope(GCounter.Empty.Increment(leavingMember.UniqueAddress));
            replicator.Tell(new Write("known-node-leaving", envelope, leavingMember.UniqueAddress), probe.Ref);

            // Before the fix this write is dropped as coming from an "unknown node" and no reply
            // is ever sent, so this would time out.
            await probe.ExpectMsgAsync<WriteAck>(TimeSpan.FromSeconds(3));
        }

        [Fact(DisplayName = "Replicator should accept a Write from a member it first saw as Exiting")]
        public async Task Should_accept_write_from_member_first_seen_as_Exiting()
        {
            await SelfJoinAndGetUpMemberAsync(Sys);
            var remoteUp = await SelfJoinAndGetUpMemberAsync(_remoteSys);
            var exitingMember = remoteUp.Copy(MemberStatus.Leaving).Copy(MemberStatus.Exiting);

            var replicator = Sys.ActorOf(Replicator.Props(ReplicatorSettings.Create(Sys)));

            // MemberExited routes to ReceiveMemberExiting, a different handler than the generic
            // member-event path MemberLeft/MemberDowned use above, so this exercises that path
            // separately.
            replicator.Tell(new ClusterEvent.MemberExited(exitingMember));

            var probe = CreateTestProbe();
            var envelope = new DataEnvelope(GCounter.Empty.Increment(exitingMember.UniqueAddress));
            replicator.Tell(new Write("known-node-exiting", envelope, exitingMember.UniqueAddress), probe.Ref);

            await probe.ExpectMsgAsync<WriteAck>(TimeSpan.FromSeconds(3));
        }

        [Fact(DisplayName = "Replicator should still ignore a Write from a node it has never seen in any member event")]
        public async Task Should_ignore_write_from_node_never_seen()
        {
            await SelfJoinAndGetUpMemberAsync(Sys);
            var strangerUp = await SelfJoinAndGetUpMemberAsync(_remoteSys);

            var replicator = Sys.ActorOf(Replicator.Props(ReplicatorSettings.Create(Sys)));

            // No member event at all is sent about `strangerUp` this time. This proves the fix
            // does not turn IsKnownNode into an open gate -- a node this replicator has never
            // observed in any status is still rejected.
            var probe = CreateTestProbe();
            var envelope = new DataEnvelope(GCounter.Empty.Increment(strangerUp.UniqueAddress));
            replicator.Tell(new Write("known-node-stranger", envelope, strangerUp.UniqueAddress), probe.Ref);

            await probe.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }

        protected override void AfterAll()
        {
            base.AfterAll();
            Shutdown(_remoteSys);
        }
    }
}
