//-----------------------------------------------------------------------
// <copyright file="ClusterMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Routing;
using Akka.TestKit;
using Xunit;
using FluentAssertions;

namespace Akka.Cluster.Tests.Serialization
{
    public class ClusterMessageSerializerSpec : AkkaSpec
    {
        public ClusterMessageSerializerSpec()
            : base(@"akka.actor.provider = cluster")
        {
        }

        private static readonly Member a1 = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Joining);
        private static readonly Member b1 = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Up, ImmutableHashSet.Create("r1"));
        private static readonly Member c1 = TestMember.Create(new Address("akka.tcp", "sys", "c", 2552), MemberStatus.Leaving, ImmutableHashSet.Create("r2"));
        private static readonly Member d1 = TestMember.Create(new Address("akka.tcp", "sys", "d", 2552), MemberStatus.Exiting, ImmutableHashSet.Create("r1", "r2"));
        private static readonly Member e1 = TestMember.Create(new Address("akka.tcp", "sys", "e", 2552), MemberStatus.Down, ImmutableHashSet.Create("r3"));

        [Fact]
        public void Can_serialize_Heartbeat()
        {
            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var message = new ClusterHeartbeatSender.Heartbeat(address);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_HeartbeatRsp()
        {
            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var uniqueAddress = new UniqueAddress(address, 17);
            var message = new ClusterHeartbeatSender.HeartbeatRsp(uniqueAddress);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_GossipEnvelope()
        {
            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var uniqueAddress2 = new UniqueAddress(address, 18);
            var node1 = new VectorClock.Node("node1");
            var node2 = new VectorClock.Node("node2");

            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, c1, d1)).Increment(node1)
                    .Increment(node2)
                    .Seen(a1.UniqueAddress)
                    .Seen(b1.UniqueAddress);
            var message1 = new GossipEnvelope(a1.UniqueAddress, uniqueAddress2, g1);
            var deserialized = AssertAndReturn(message1);
            message1.From.Should().Be(deserialized.From);
            message1.To.Should().Be(deserialized.To);
            message1.Gossip.Members.Should().BeEquivalentTo(deserialized.Gossip.Members);
            message1.Gossip.Overview.Seen.Should().BeEquivalentTo(deserialized.Gossip.Overview.Seen);
            message1.Gossip.Overview.Reachability.Should().Be(deserialized.Gossip.Overview.Reachability);
            message1.Gossip.Version.Versions.Should().Equal(deserialized.Gossip.Version.Versions);
        }

        [Fact]
        public void Can_serialize_GossipStatus()
        {
            var node1 = new VectorClock.Node("node1");
            var node2 = new VectorClock.Node("node2");
            var node3 = new VectorClock.Node("node3");
            var node4 = new VectorClock.Node("node4");
            var g1 =
                new Gossip(ImmutableSortedSet.Create(a1, b1, c1, d1)).Increment(node1)
                    .Increment(node2)
                    .Seen(a1.UniqueAddress)
                    .Seen(b1.UniqueAddress);
            var g2 = g1.Increment(node3).Increment(node4).Seen(a1.UniqueAddress).Seen(c1.UniqueAddress);
            var reachability3 = Reachability.Empty.Unreachable(a1.UniqueAddress, e1.UniqueAddress).Unreachable(b1.UniqueAddress, e1.UniqueAddress);
            var g3 = g2.Copy(members: ImmutableSortedSet.Create(a1, b1, c1, d1, e1), overview: g2.Overview.Copy(reachability: reachability3));

            AssertEqual(new GossipStatus(a1.UniqueAddress, g1.Version));
            AssertEqual(new GossipStatus(a1.UniqueAddress, g2.Version));
            AssertEqual(new GossipStatus(a1.UniqueAddress, g3.Version));
        }

        [Fact]
        public void Can_serialize_Join()
        {
            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var uniqueAddress = new UniqueAddress(address, 17);
            var message = new InternalClusterAction.Join(uniqueAddress, ImmutableHashSet.Create("foo", "bar"));
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_Welcome()
        {
            var node1 = new VectorClock.Node("node1");
            var node2 = new VectorClock.Node("node2");
            var node3 = new VectorClock.Node("node3");
            var node4 = new VectorClock.Node("node4");
            var g1 =
                new Gossip(ImmutableSortedSet.Create(a1, b1, c1, d1)).Increment(node1)
                    .Increment(node2)
                    .Seen(a1.UniqueAddress)
                    .Seen(b1.UniqueAddress);
            var g2 = g1.Increment(node3).Increment(node4).Seen(a1.UniqueAddress).Seen(c1.UniqueAddress);

            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var uniqueAddress = new UniqueAddress(address, 17);
            var message = new InternalClusterAction.Welcome(uniqueAddress, g2);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_ExitingConfirmed()
        {
            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var uniqueAddress = new UniqueAddress(address, 17);
            var message = new InternalClusterAction.ExitingConfirmed(uniqueAddress);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_Leave()
        {
            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var message = new ClusterUserAction.Leave(address);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_Down()
        {
            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var message = new ClusterUserAction.Down(address);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_InitJoin()
        {
            var message = new InternalClusterAction.InitJoin();
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_InitJoinAck()
        {
            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var message = new InternalClusterAction.InitJoinAck(address);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_InitJoinNack()
        {
            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var message = new InternalClusterAction.InitJoinNack(address);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_ClusterRouterPool()
        {
            var roundRobinPool = new RoundRobinPool(nrOfInstances: 4);
            var clusterRouterPoolSettings = new ClusterRouterPoolSettings(2, 5, true, "Richard, Duke");
            var message = new ClusterRouterPool(roundRobinPool, clusterRouterPoolSettings);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_ClusterRouterPoolWithEmptyRole()
        {
            var roundRobinPool = new RoundRobinPool(nrOfInstances: 4);
            var clusterRouterPoolSettings = new ClusterRouterPoolSettings(2, 5, true, null);
            var message = new ClusterRouterPool(roundRobinPool, clusterRouterPoolSettings);
            AssertEqual(message);
        }

        private T AssertAndReturn<T>(T message)
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            return serializer.FromBinary<T>(serialized);
        }

        private void AssertEqual<T>(T message)
        {
            var deserialized = AssertAndReturn(message);
            Assert.Equal(message, deserialized);
        }
    }
}
