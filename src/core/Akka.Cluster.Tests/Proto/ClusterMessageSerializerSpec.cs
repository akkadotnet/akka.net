//-----------------------------------------------------------------------
// <copyright file="ClusterMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Proto;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests.Proto
{
    public class ClusterMessageSerializerSpec : AkkaSpec
    {
        public ClusterMessageSerializerSpec()
            : base(@"akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""")
        {
            Serializer = new ClusterMessageSerializer((ExtendedActorSystem)Sys);
        }

        public Serializer Serializer;

        private void CheckSerialization(object obj)
        {
            var blob = Serializer.ToBinary(obj);
            var reference = Serializer.FromBinary(blob, obj.GetType());

            if (obj is GossipEnvelope)
            {
                var reference1 = (GossipEnvelope)reference;
                var reference2 = (GossipEnvelope)obj;
                Assert.Equal(reference2.From, reference1.From);
                Assert.Equal(reference2.To, reference1.To);
                Assert.Equal(reference2.Gossip.ToString(), reference1.Gossip.ToString());
            }
            else
            {
                Assert.Equal(obj, reference);
            }
        }

        private static readonly Member a1 = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Joining);

        private static readonly Member b1 = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Up, ImmutableHashSet.Create<string>("r1"));

        private static readonly Member c1 = TestMember.Create(new Address("akka.tcp", "sys", "c", 2552), MemberStatus.Leaving,
            ImmutableHashSet.Create<string>("r2"));

        private static readonly Member d1 = TestMember.Create(new Address("akka.tcp", "sys", "d", 2552),
            MemberStatus.Exiting, ImmutableHashSet.Create<string>("r1", "r2"));
        private static readonly Member e1 = TestMember.Create(new Address("akka.tcp", "sys", "e", 2552), MemberStatus.Down, ImmutableHashSet.Create<string>("r3"));

        private static readonly Member f1 = TestMember.Create(new Address("akka.tcp", "sys", "f", 2552), MemberStatus.Removed,
           ImmutableHashSet.Create<string>("r2", "r3"));

        [Fact]
        public void ClusterMessages_must_be_serializable()
        {
            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var uniqueAddress = new UniqueAddress(address, 17);
            var address2 = new Address("akka.tcp", "system", "other.host.org", 4711);
            var uniqueAddress2 = new UniqueAddress(address2, 18);
            CheckSerialization(new InternalClusterAction.Join(uniqueAddress, ImmutableHashSet.Create("foo","bar")));
            CheckSerialization(new ClusterUserAction.Leave(address));
            CheckSerialization(new ClusterUserAction.Down(address));
            CheckSerialization(new InternalClusterAction.InitJoin());
            CheckSerialization(new InternalClusterAction.InitJoinAck(address));
            CheckSerialization(new InternalClusterAction.InitJoinNack(address));
            CheckSerialization(new ClusterHeartbeatSender.Heartbeat(address));
            CheckSerialization(new ClusterHeartbeatSender.HeartbeatRsp(uniqueAddress));

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
            var reachability3 =
                Reachability.Empty.Unreachable(a1.UniqueAddress, e1.UniqueAddress)
                    .Unreachable(b1.UniqueAddress, e1.UniqueAddress);
            var g3 = g2.Copy(members: ImmutableSortedSet.Create(a1, b1, c1, d1, e1),
                overview: g2.Overview.Copy(reachability: reachability3));
            CheckSerialization(new GossipEnvelope(a1.UniqueAddress, uniqueAddress2, g1));
            CheckSerialization(new GossipEnvelope(a1.UniqueAddress, uniqueAddress2, g2));
            CheckSerialization(new GossipEnvelope(a1.UniqueAddress, uniqueAddress2, g3));

            CheckSerialization(new GossipStatus(a1.UniqueAddress, g1.Version));
            CheckSerialization(new GossipStatus(a1.UniqueAddress, g2.Version));
            CheckSerialization(new GossipStatus(a1.UniqueAddress, g3.Version));

            CheckSerialization(new InternalClusterAction.Welcome(uniqueAddress, g2));

            var mg = new MetricsGossip(ImmutableHashSet.Create<NodeMetrics>(new[]
            {
                new NodeMetrics(a1.Address, 4711, ImmutableHashSet.Create<Metric>(new Metric("foo", 1.2, null))),
                new NodeMetrics(b1.Address, 4712,
                    ImmutableHashSet.Create<Metric>(new Metric("foo", 2.1, new EWMA(100.0, 0.18))
                        , new Metric("bar1", Double.MinValue, null), new Metric("bar2", float.MaxValue, null),
                        new Metric("bar3", int.MaxValue, null), new Metric("bar4", long.MaxValue, null), 
                        new Metric("bar5", double.MaxValue, null)))
            }));
            CheckSerialization(new MetricsGossipEnvelope(a1.Address, mg, true));
        }
    }
}

