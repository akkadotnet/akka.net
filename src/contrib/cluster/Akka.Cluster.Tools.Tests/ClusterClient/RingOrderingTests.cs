//-----------------------------------------------------------------------
// <copyright file="RingOrderingTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Client;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace Akka.Cluster.Tools.Tests.ClusterClient
{
    public class RingOrderingTests
    {
        // JVM
        // https://github.com/akka/akka/blob/master/akka-cluster-tools/src/main/scala/akka/cluster/client/ClusterClient.scala#L894-L899

        [Fact]
        public void AddTest()
        {
            var set = MakeEmptySet();

            set = set.Add(MakeAddress(1));
            set = set.Add(MakeAddress(2));
            set = set.Add(MakeAddress(5));

            set.Should().Contain(MakeAddress(1));
            set.Should().Contain(MakeAddress(2));
            set.Should().Contain(MakeAddress(5));
        }

        [Fact]
        public void RemoveTest()
        {
            var set = MakeEmptySet();

            set = set.Add(MakeAddress(1))
                .Add(MakeAddress(2))
                .Add(MakeAddress(5));

            set = set.Remove(MakeAddress(2));

            set.Should().Contain(MakeAddress(1));
            set.Should().Contain(MakeAddress(5));
            set.Should().NotContain(MakeAddress(2));

            // check if set after converting to list is the same
            set.ToList().Should().Contain(MakeAddress(1));
            set.ToList().Should().Contain(MakeAddress(5));
            set.ToList().Should().NotContain(MakeAddress(2));
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(5, 5)]
        [InlineData(1, 2)]
        [InlineData(1, 5)]
        [InlineData(2, 5)]
        public void EqualityTest(int p1, int p2)
        {
            if (ClusterReceptionist.RingOrdering.Instance.Compare(MakeAddress(p1), MakeAddress(p2)) == 0)
            {
                ClusterReceptionist.RingOrdering.Instance.Compare(MakeAddress(p2), MakeAddress(p1)).Should().Be(0);
            }
            else if (ClusterReceptionist.RingOrdering.Instance.Compare(MakeAddress(p1), MakeAddress(p2)) < 0)
            {
                ClusterReceptionist.RingOrdering.Instance.Compare(MakeAddress(p2), MakeAddress(p1)).Should().BePositive();
            }
            else
            {
                ClusterReceptionist.RingOrdering.Instance.Compare(MakeAddress(p2), MakeAddress(p1)).Should().BeNegative();
            }

        }

        private static Address MakeAddress(int port, string hostname = "localhost", string actorSystemName = "MySystem")
        {
            return Address.Parse($"akka.tcp://{actorSystemName}@{hostname}:{port}");
        }

        private static ImmutableSortedSet<Address> MakeEmptySet()
        {
            return ImmutableSortedSet<Address>.Empty.WithComparer(ClusterReceptionist.RingOrdering.Instance);
        }
    }
}
