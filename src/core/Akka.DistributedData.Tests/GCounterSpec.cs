using Akka.Cluster;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Akka.DistributedData.Tests
{
    public class GCounterSpec
    {
        private static UniqueAddress Node1 = new UniqueAddress(new Actor.Address("akka.tcp", "Sys", "localhost", 2551), 1);
        private static UniqueAddress Node2 = new UniqueAddress(Node1.Address.WithPort(2552), 2);
        private static UniqueAddress Node3 = new UniqueAddress(Node1.Address.WithPort(2553), 3);

        [Fact]
        public void AGCounterMustBeAbleToIncrementEachNodesRecordByOne()
        {
            var c1 = new GCounter();
            var c2 = c1.Increment(Node1);
            var c3 = c2.Increment(Node1);
            var c4 = c3.Increment(Node2);
            var c5 = c4.Increment(Node2);
            var c6 = c5.Increment(Node2);

            Assert.Equal(new BigInteger(2), c6.State[Node1]);
            Assert.Equal(new BigInteger(3), c5.State[Node2]);
        }
    }
}
