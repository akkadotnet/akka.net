//-----------------------------------------------------------------------
// <copyright file="ClusterGenerators.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using Akka.Actor;
using Akka.Annotations;
using FsCheck;
using FsCheck.Fluent;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// FsCheck data generators for Akka.Cluster types.
    /// </summary>
    [InternalApi]
    public class ClusterGenerators
    {
        public static Arbitrary<Address> AddressGenerator()
        {
            /*
             * In order to help guarantee collisions and duplicates in random tests, we hold all parts
             * of the address other than the port number constant.
             */
            var gen = ArbMap.Default.GeneratorFor<IPAddress>()
                .Zip(Gen.Choose(1, 65535))
                .Select(t => new Address("akka.tcp", "cluster", t.Item1.ToString(), t.Item2));

            return Arb.From(gen);
        }

        public static Arbitrary<UniqueAddress> UniqueAddressGenerator()
        {
            var gen = ArbMap.Default.GeneratorFor<int>()
                .Zip(AddressGenerator().Generator)
                .Select(t => new UniqueAddress(t.Item2, t.Item1));

            return Arb.From(gen);
        }

        public static Arbitrary<MemberStatus> MemberStatusGenerator()
        {
            return Arb.From(Gen.Elements((MemberStatus[])Enum.GetValues(typeof(MemberStatus))));
        }
    }
}
