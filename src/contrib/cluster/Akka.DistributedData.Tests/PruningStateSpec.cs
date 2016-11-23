//-----------------------------------------------------------------------
// <copyright file="PruningStateSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class PruningStateSpec
    {
        private readonly UniqueAddress _node1 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2551), 1);
        private readonly UniqueAddress _node2 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2552), 2);
        private readonly UniqueAddress _node3 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2553), 3);
        private readonly UniqueAddress _node4 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2554), 4);

        public PruningStateSpec(ITestOutputHelper output)
        {
        }

        [Fact]
        public void PruningState_should_merge_phase_correctly()
        {
            var p1 = new PruningState(_node1, PruningInitialized.Empty);
            var p2 = new PruningState(_node1, PruningPerformed.Instance);

            Assert.Equal(PruningPerformed.Instance, p1.Merge(p2).Phase);
            Assert.Equal(PruningPerformed.Instance, p2.Merge(p1).Phase);
        }

        [Fact]
        public void PruningState_should_merge_owner_correctly()
        {
            var p1 = new PruningState(_node1, PruningInitialized.Empty);
            var p2 = new PruningState(_node2, PruningInitialized.Empty);

            var expected = new PruningState(_node1, PruningInitialized.Empty);
            Assert.Equal(expected, p1.Merge(p2));
            Assert.Equal(expected, p2.Merge(p1));
        }

        [Fact]
        public void PruningState_should_merge_seen_correctly()
        {
            var p1 = new PruningState(_node1, new PruningInitialized(_node2.Address));
            var p2 = new PruningState(_node1, new PruningInitialized(_node4.Address));

            var expected = new PruningState(_node1, new PruningInitialized(_node2.Address, _node4.Address));
            Assert.Equal(expected, p1.Merge(p2));
            Assert.Equal(expected, p2.Merge(p1));
        }
    }
}