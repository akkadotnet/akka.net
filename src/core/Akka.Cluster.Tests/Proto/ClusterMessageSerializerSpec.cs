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
        }
    }
}
