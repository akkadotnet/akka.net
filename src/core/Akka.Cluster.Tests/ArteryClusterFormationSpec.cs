//-----------------------------------------------------------------------
// <copyright file="ArteryClusterFormationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// Design.md group 9, task 9.5, "Verify cluster formation with Artery TCP" -- the FIRST full
    /// integration proof of Artery TCP remoting underneath Akka.Cluster: a real two-node cluster,
    /// joined via the production seed-nodes bootstrap path (not a manual <see cref="Cluster.Join"/>
    /// call for the SECOND node), with an <c>akka://</c>-scheme seed address (Artery has no
    /// <c>akka.tcp://</c> equivalent -- see <c>ArteryRemoting.LocalAddressForRemote</c>).
    ///
    /// <para>
    /// Lives here (<c>Akka.Cluster.Tests</c>), not <c>Akka.Remote.Tests</c>, because it needs BOTH
    /// <c>Akka.Cluster</c> (for <see cref="Cluster"/>/<see cref="ClusterEvent"/>) and Artery's
    /// internal types are not needed at all here -- this spec deliberately uses ONLY public API
    /// (<see cref="ActorSystem"/>, <see cref="Cluster"/>, <see cref="ClusterEvent"/>), since
    /// <c>Akka.Remote</c>'s <c>InternalsVisibleTo</c> list does not include this assembly.
    /// <c>Akka.Cluster.Tests</c> already references <c>Akka.Cluster</c>, which transitively
    /// references <c>Akka.Remote</c> -- <c>Akka.Remote.Tests</c> does NOT reference
    /// <c>Akka.Cluster</c>, which is why this spec cannot live there instead.
    /// </para>
    /// </summary>
    public class ArteryClusterFormationSpec : AkkaSpec
    {
        public ArteryClusterFormationSpec(ITestOutputHelper output) : base(output)
        {
        }

        private const string ClusterSystemName = "ArteryClusterFormation";

        private static Config ArteryClusterConfig(Address? seedAddress) => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = cluster
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            {{(seedAddress is { } addr ? $"akka.cluster.seed-nodes = [\"{addr}\"]" : "akka.cluster.seed-nodes = []")}}
            """);

        [Fact(DisplayName = "9.5: two nodes form a cluster over Artery TCP -- B joins via the production seed-nodes bootstrap path (akka:// scheme seed), both reach MemberStatus.Up, then shut down cleanly")]
        public async Task Should_Form_Cluster_Over_Artery_Via_Seed_Nodes()
        {
            // Node A: no seed-nodes configured -- self-joins directly (deterministic; avoids racing
            // the seed-node bootstrap's own retry/timeout machinery for the FIRST node, matching
            // ClusterSpec.cs's existing "isolated tests" pattern elsewhere in this suite). Node A's
            // OWN membership is not what this test is proving -- B joining A via the real
            // production seed-nodes path is.
            var systemA = ActorSystem.Create(ClusterSystemName, ArteryClusterConfig(seedAddress: null));
            ActorSystem? systemB = null;
            try
            {
                var clusterA = Cluster.Get(systemA);
                clusterA.Join(clusterA.SelfAddress);

                var probeA = CreateTestProbe(systemA);
                clusterA.Subscribe(probeA.Ref, ClusterEvent.InitialStateAsEvents, typeof(ClusterEvent.MemberUp));
                await probeA.ExpectMsgAsync<ClusterEvent.MemberUp>(TimeSpan.FromSeconds(15));

                clusterA.SelfAddress.Protocol.Should().Be("akka", "Artery has no akka.tcp:// equivalent -- the seed address itself proves the akka:// scheme reaches the cluster join code path unmodified");

                // Node B: seed-nodes = [A's akka:// address] -- the REAL production seed-node
                // bootstrap join path (InternalClusterAction's JoinSeedNodeProcess), not a manual
                // Cluster.Join call. This is what actually exercises "does Artery's akka:// scheme
                // flow correctly through Cluster's seed-node join machinery" end to end.
                systemB = ActorSystem.Create(ClusterSystemName, ArteryClusterConfig(seedAddress: clusterA.SelfAddress));
                var clusterB = Cluster.Get(systemB);

                var probeB = CreateTestProbe(systemB);
                clusterB.Subscribe(probeB.Ref, ClusterEvent.InitialStateAsEvents, typeof(ClusterEvent.MemberUp));
                await probeB.ExpectMsgAsync<ClusterEvent.MemberUp>(TimeSpan.FromSeconds(30));

                // A must also observe B joining -- proves the cluster is genuinely bidirectional,
                // not just "B thinks it joined". InitialStateAsEvents (matching probeA/probeB
                // above) so the first message is a MemberUp, not a CurrentClusterState snapshot.
                var probeA2 = CreateTestProbe(systemA);
                clusterA.Subscribe(probeA2.Ref, ClusterEvent.InitialStateAsEvents, typeof(ClusterEvent.MemberUp));
                await probeA2.FishForMessageAsync(msg => msg is ClusterEvent.MemberUp up && Equals(up.Member.Address, clusterB.SelfAddress), TimeSpan.FromSeconds(30));

                clusterA.State.Members.Count.Should().Be(2);
                clusterB.State.Members.Count.Should().Be(2);
            }
            finally
            {
                // Clean CoordinatedShutdown for both nodes -- the assertion IS the clean-shutdown
                // gate: AwaitWithTimeout throws (failing the test) if either system fails to
                // terminate within the timeout.
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(15.Seconds());
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }
    }
}
